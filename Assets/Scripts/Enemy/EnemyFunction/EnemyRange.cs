using System;
using System.Collections;
using UnityEngine;

/// <summary>
/// Shared gating for enemy hitscan-style attacks: Aggro-check, range-check, a
/// fire-rate cooldown, a facing/aim gate, shot spread, and an optional visible
/// telegraph before the shot actually lands. Concrete weapons only implement
/// Fire() — the actual raycast/damage/trail logic.
///
/// NOT used by SniperAttackBehaviour (own charge/lock/rotate state machine)
/// or LaserBehaviour (continuous beam, not a discrete cooldown-gated shot).
/// </summary>
[RequireComponent(typeof(EnemyBrain))]
public abstract class EnemyRangedAttackBehaviour : MonoBehaviour, IEnemyAimController
{
    [Header("Base Setup")]
    public Transform FirePoint;
    public float     AttackRange = 20f;
    public float     FireRate    = 0.1f;
    [Tooltip("Layers that block/absorb this shot — walls, terrain, props. Should NOT include the player's own layer.")]
    public LayerMask ObstacleLayers;
    [Tooltip("The player's own physics layer. Combined with ObstacleLayers for the actual firing raycast — without this, the shot can only ever hit obstacles and can never register a hit on the player at all.")]
    public LayerMask PlayerLayer;

    /// <summary>Obstacles + player combined — use this for the firing raycast, not ObstacleLayers alone.</summary>
    protected LayerMask FireHitMask => ObstacleLayers | PlayerLayer;

    [Header("Aiming")]
    [Tooltip("Degrees/sec the enemy body turns to face the player before it's allowed to fire.")]
    public float AimTurnSpeed = 220f;
    [Tooltip("Max facing-angle error (degrees) allowed to actually fire. Enemy must turn into this cone first — prevents snap-shots regardless of orientation.")]
    public float MaxFireAngle = 12f;
    [Tooltip("If false, restores the old instant-fire-on-cooldown behaviour (no aim gate).")]
    public bool RequireAimToFire = true;

    /// <summary>True once the enemy's forward vector is within MaxFireAngle of the player.</summary>
    public bool IsAimed { get; private set; }

    /// <summary>Fired whenever IsAimed flips. Arg: new value.</summary>
    public event Action<bool> OnAimingChanged;

    [Header("Accuracy")]
    [Tooltip("Cone half-angle (degrees) of random inaccuracy applied to each shot. This — not the aim gate — is what stops shots reading as a perfectly-tracking laser. 0 = laser-perfect, not recommended.")]
    public float Spread = 2.5f;
    [Tooltip("Extra spread added per consecutive shot while sustained-firing (SMG-style), reset after SpreadRecoveryCooldown seconds without firing.")]
    public float SpreadBuildPerShot = 0f;
    public float MaxSpreadBuildup = 4f;
    public float SpreadRecoveryCooldown = 0.4f;

    private float _spreadBuildup;
    private float _lastFireTime = float.NegativeInfinity;

    [Header("Telegraph")]
    [Tooltip("Seconds of visible wind-up before the shot actually fires, once the fire-rate timer trips. 0 = instant/legacy hitscan.")]
    public float TelegraphTime = 0f;
    [Tooltip("Optional — enabled for the duration of the telegraph, disabled the instant the shot fires or is cancelled. Hook a muzzle glow, laser dot, aim pose, whatever reads as 'about to shoot'.")]
    public GameObject TelegraphVFX;

    /// <summary>Fired the instant a shot is taken — EnemyAudio subscribes to this.</summary>
    public event Action OnFired;
    /// <summary>Fired the instant the telegraph wind-up begins, before the shot lands.</summary>
    public event Action OnTelegraphStart;
    /// <summary>Fired if a telegraph is interrupted (target lost, left range, no longer Aggro) before it resolves into a shot.</summary>
    public event Action OnTelegraphCancelled;

    protected EnemyBrain Brain { get; private set; }
    protected bool IsTelegraphing { get; private set; }

    private float     _nextFireTimer;
    private Coroutine _telegraphRoutine;
    private bool      _holdingFireSlot;

    // ── IEnemyAimController ─────────────────────────────────────────────────
    public int AimPriority => 20;

    public bool WantsAim =>
        Brain.State == EnemyState.Aggro &&
        PlayerHealth.Transform != null &&
        FirePoint != null &&
        Vector3.Distance(transform.position, PlayerHealth.Transform.position) <= AttackRange;

    protected virtual void Awake()
    {
        Brain = GetComponent<EnemyBrain>();
    }

    protected virtual void Update()
    {
        if (SpreadBuildPerShot > 0f && Time.time - _lastFireTime > SpreadRecoveryCooldown)
            _spreadBuildup = 0f;

        if (Brain.State != EnemyState.Aggro) { CancelTelegraph(); ResetAim(); return; }
        if (PlayerHealth.Transform == null)  { CancelTelegraph(); ResetAim(); return; }
        if (FirePoint == null)               { CancelTelegraph(); ResetAim(); return; }

        float dist = Vector3.Distance(transform.position, PlayerHealth.Transform.position);
        if (dist > AttackRange) { CancelTelegraph(); ResetAim(); return; }

        // Rotation itself + IsAimed are driven by EnemyAimCoordinator (LateUpdate),
        // which calls TickAim() below when this behaviour wins aim ownership for
        // the frame — IsAimed here reflects the previous frame's result (1-frame lag).

        if (IsTelegraphing) return; // wind-up coroutine already owns the shot this cycle

        _nextFireTimer += Time.deltaTime;
        if (_nextFireTimer >= FireRate)
        {
            if (RequireAimToFire && !IsAimed)
            {
                _nextFireTimer = FireRate;
                return;
            }

            if (!EnemySquadCoordinator.TryAcquireFireSlot())
            {
                _nextFireTimer = FireRate;
                return;
            }

            _holdingFireSlot = true;
            _nextFireTimer = 0f;

            if (TelegraphTime > 0f)
                _telegraphRoutine = StartCoroutine(Co_TelegraphThenFire());
            else
                FireNow();
        }
    }

    // Rotates the enemy body toward the player and updates IsAimed. Called by
    // EnemyAimCoordinator whenever WantsAim is true, so the enemy is always
    // tracking while Aggro + in range, not just at fire time.
    public void TickAim(float deltaTime)
    {
        Vector3 toTarget = PlayerHealth.Transform.position - transform.position;
        toTarget.y = 0f;
        if (toTarget.sqrMagnitude < 0.0001f) return;

        Quaternion targetRot = Quaternion.LookRotation(toTarget);
        transform.rotation = Quaternion.RotateTowards(transform.rotation, targetRot, AimTurnSpeed * deltaTime);

        float angle = Vector3.Angle(transform.forward, toTarget);
        SetAimed(angle <= MaxFireAngle);
    }

    private void ResetAim() => SetAimed(false);

    private void SetAimed(bool aimed)
    {
        if (IsAimed == aimed) return;
        IsAimed = aimed;
        OnAimingChanged?.Invoke(IsAimed);
    }

    /// <summary>
    /// Perturbs a fire direction by Spread (+ any sustained-fire buildup) degrees.
    /// Call this in every concrete Fire() override before raycasting — without it,
    /// the shot is a mathematically perfect line to the target regardless of facing,
    /// which is what reads as a "laser" even after the aim gate is in place.
    /// </summary>
    protected Vector3 ApplySpread(Vector3 direction)
    {
        _lastFireTime = Time.time;

        float totalSpread = Spread + _spreadBuildup;
        _spreadBuildup = Mathf.Min(_spreadBuildup + SpreadBuildPerShot, MaxSpreadBuildup);

        if (totalSpread <= 0f) return direction;

        Vector3 right = Vector3.Cross(Vector3.up, direction).normalized;
        if (right.sqrMagnitude < 0.0001f) right = Vector3.Cross(Vector3.forward, direction).normalized;
        Vector3 up = Vector3.Cross(direction, right).normalized;

        float yaw   = UnityEngine.Random.Range(-totalSpread, totalSpread);
        float pitch = UnityEngine.Random.Range(-totalSpread, totalSpread);

        Vector3 spread = right * Mathf.Tan(yaw * Mathf.Deg2Rad) + up * Mathf.Tan(pitch * Mathf.Deg2Rad);
        return (direction + spread).normalized;
    }

    private IEnumerator Co_TelegraphThenFire()
    {
        IsTelegraphing = true;
        if (TelegraphVFX != null) TelegraphVFX.SetActive(true);
        OnTelegraphStart?.Invoke();

        float t = 0f;
        while (t < TelegraphTime)
        {
            if (Brain.State != EnemyState.Aggro || PlayerHealth.Transform == null ||
                Vector3.Distance(transform.position, PlayerHealth.Transform.position) > AttackRange ||
                (RequireAimToFire && !IsAimed))
            {
                CancelTelegraph();
                yield break;
            }

            // Rotation/IsAimed are handled by EnemyAimCoordinator's own LateUpdate
            // call to TickAim() — don't also drive it here, or it gets applied twice
            // in the same frame (double AimTurnSpeed) while telegraphing.
            t += Time.deltaTime;
            yield return null;
        }

        if (TelegraphVFX != null) TelegraphVFX.SetActive(false);
        IsTelegraphing    = false;
        _telegraphRoutine = null;

        FireNow();
    }

    private void FireNow()
    {
        OnFired?.Invoke();
        Fire();
        ReleaseFireSlot();
    }

    private void CancelTelegraph()
    {
        if (_telegraphRoutine != null)
        {
            StopCoroutine(_telegraphRoutine);
            _telegraphRoutine = null;
        }

        if (IsTelegraphing)
        {
            IsTelegraphing = false;
            if (TelegraphVFX != null) TelegraphVFX.SetActive(false);
            OnTelegraphCancelled?.Invoke();
        }

        ReleaseFireSlot();
    }

    private void ReleaseFireSlot()
    {
        if (!_holdingFireSlot) return;
        EnemySquadCoordinator.ReleaseFireSlot();
        _holdingFireSlot = false;
    }

    private void OnDisable()
    {
        ReleaseFireSlot();
        ResetAim();
    }

    /// <summary>Implement the actual shot here — raycast(s), damage, trail VFX. Call
    /// ApplySpread() on your fire direction before raycasting.</summary>
    protected abstract void Fire();

    protected Vector3 GetTargetPoint(float heightOffset = 0.5f) =>
        PlayerHealth.Transform.position + Vector3.up * heightOffset;

#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        Vector3 center = EnemyGizmos.Ground(transform.position);

        // Reach ring, tinted by whether the shot is currently lined up.
        Color reach = EnemyGizmos.Live(IsAimed, EnemyGizmos.Ranged);
        EnemyGizmos.GroundRing(center, AttackRange, reach, $"fire {AttackRange:0.#}m", 60f);

        // Fire arc drawn flat and filled out to the reach ring, so the angle gate
        // and the range gate read as one shape instead of two loose rays.
        EnemyGizmos.GroundCone(center, transform.forward, MaxFireAngle, AttackRange,
                               reach * 0.8f, $"±{MaxFireAngle:0}°");
    }
#endif
}