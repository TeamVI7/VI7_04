using System;
using System.Collections;
using UnityEngine;

/// <summary>
/// Airborne marksman. Holds long range and high altitude on FlyingMovement,
/// paints the player with a charge laser that hardens into a lock, fires a
/// hitscan round at the locked point, then immediately relocates so it never
/// takes two shots from the same piece of sky.
///
/// Same charge → lock → fire contract as the ground SniperAttackBehaviour, but
/// aims in full 3D (it shoots down at the player) and drives its own
/// hold-still / reposition rhythm through FlyingMovement.
/// </summary>
[RequireComponent(typeof(EnemyBrain))]
[RequireComponent(typeof(FlyingMovement))]
public class FlyingSniperBehaviour : MonoBehaviour, IEnemyAimController
{
    [Header("Rifle")]
    public Transform FirePoint;
    public float AttackRange = 80f;
    public float Damage = 30f;
    [Tooltip("Seconds between the end of one shot and the start of the next aim.")]
    public float FireCooldown = 4f;

    [Header("Charge / Lock")]
    [Tooltip("Laser tracks the player during this window — the player's window to break line-of-sight.")]
    public float ChargeTime = 1.8f;
    [Tooltip("Laser freezes on the locked point during this window — the player's window to leave it.")]
    public float LockTime = 0.6f;
    [Tooltip("Aim ahead of the player's velocity when locking. 0 = lock exactly where they stood.")]
    public float LeadTime = 0.2f;
    public float TargetHeightOffset = 0.9f;

    [Header("Aim")]
    public float AimTurnSpeed = 5f;
    [Tooltip("The shot is skipped if the body is still turned further than this off the locked point when the lock expires.")]
    public float MaxFireAngle = 25f;

    [Header("Line of Sight")]
    [Tooltip("Layers that block the laser and the shot. Should NOT include the player's own layer.")]
    public LayerMask ObstacleLayers;

    [Header("Laser")]
    public LineRenderer LaserRenderer;
    public float LaserWidth = 0.035f;
    public Color AimColor = Color.red;
    public Color LockColor = new Color(1f, 0.3f, 0f);

    [Header("Flight Rhythm")]
    [Tooltip("Speed multiplier applied to FlyingMovement while charging/locking — a steady platform reads as 'it is aiming at me'.")]
    [Range(0f, 1f)] public float AimingSpeedMultiplier = 0.15f;
    [Tooltip("Relocate to a new firing position immediately after each shot.")]
    public bool RepositionAfterShot = true;

    [Header("VFX")]
    public GameObject MuzzleFlashPrefab;
    public GameObject HitVFXPrefab;
    public float HitVFXLifetime = 2f;

    // Audio hooks — EnemyAudio subscribes to these; this behaviour owns no AudioSource.
    public event Action OnChargeStarted;
    public event Action OnLockAcquired;
    public event Action OnAimAborted;
    public event Action OnShotFired;

    [HideInInspector] public float CooldownMultiplier = 1f;

    /// <summary>True from the first frame of the charge until the shot resolves or aborts.</summary>
    public bool IsAiming => _aiming;

    // ── IEnemyAimController ─────────────────────────────────────────────────
    public int AimPriority => 20;
    public bool WantsAim => _aiming;

    public void TickAim(float deltaTime)
    {
        Vector3 target = _locking ? _lockedPoint : GetAimPoint();
        Vector3 dir = target - transform.position;
        if (dir.sqrMagnitude < 0.001f) return;

        // Full 3D — a sniper hovering above the player has to pitch down to aim.
        transform.rotation = Quaternion.Slerp(transform.rotation,
            Quaternion.LookRotation(dir.normalized), AimTurnSpeed * deltaTime);
    }

    private EnemyBrain _brain;
    private FlyingMovement _flight;

    private bool _aiming;
    private bool _locking;
    private float _cooldownTimer;
    private Vector3 _lockedPoint;
    private Coroutine _routine;

    private void Awake()
    {
        _brain = GetComponent<EnemyBrain>();
        _flight = GetComponent<FlyingMovement>();
        _brain.OnStateChanged += OnStateChanged;

        if (LaserRenderer != null)
        {
            LaserRenderer.startWidth = LaserWidth;
            LaserRenderer.endWidth = LaserWidth;
            LaserRenderer.positionCount = 2;
            LaserRenderer.enabled = false;
        }
        else
        {
            Debug.LogWarning($"[FlyingSniper] No LineRenderer assigned on {gameObject.name} — the shot will have no telegraph.", this);
        }
    }

    private void OnDestroy() => _brain.OnStateChanged -= OnStateChanged;

    private void Update()
    {
        if (_brain.State == EnemyState.Dead || _brain.State == EnemyState.Staggered)
        {
            if (_aiming) Abort();
            return;
        }

        if (_aiming) return;

        if (_cooldownTimer > 0f)
        {
            _cooldownTimer -= Time.deltaTime;
            return;
        }

        if (_brain.State != EnemyState.Aggro) return;
        if (!HasShot()) return;

        _routine = StartCoroutine(FireSequence());
    }

    private bool HasShot()
    {
        if (PlayerHealth.Transform == null) return false;

        Vector3 origin = GetFireOrigin();
        if (Vector3.Distance(origin, PlayerHealth.Transform.position) > AttackRange) return false;

        return EnemyVision.HasLineOfSight(origin, PlayerHealth.Transform, AttackRange, ObstacleLayers);
    }

    private IEnumerator FireSequence()
    {
        _aiming = true;
        _locking = false;
        _flight.ExternalSpeedMultiplier = AimingSpeedMultiplier;

        if (LaserRenderer != null)
        {
            LaserRenderer.enabled = true;
            LaserRenderer.startColor = AimColor;
            LaserRenderer.endColor = AimColor;
        }

        OnChargeStarted?.Invoke();

        // ── Charge: laser tracks the player, breaking LOS cancels the shot ──
        for (float t = 0f; t < ChargeTime; t += Time.deltaTime)
        {
            if (PlayerHealth.Transform == null || !HasShot()) { Abort(); yield break; }
            DrawLaser(GetAimPoint());
            yield return null;
        }

        // ── Lock: laser freezes, the player can still step out of it ────────
        _lockedPoint = GetAimPoint();
        _locking = true;

        if (LaserRenderer != null)
        {
            LaserRenderer.startColor = LockColor;
            LaserRenderer.endColor = LockColor;
        }

        OnLockAcquired?.Invoke();

        for (float t = 0f; t < LockTime; t += Time.deltaTime)
        {
            DrawLaser(_lockedPoint);
            yield return null;
        }

        Fire();
        EndCycle();
    }

    private void Fire()
    {
        Vector3 origin = GetFireOrigin();
        Vector3 dir = (_lockedPoint - origin);
        if (dir.sqrMagnitude < 0.001f) return;
        dir.Normalize();

        // The body may still be swinging onto the locked point — if it never got
        // there, the round misses rather than teleporting onto target.
        if (Vector3.Angle(transform.forward, dir) > MaxFireAngle) return;

        if (MuzzleFlashPrefab != null)
            Destroy(Instantiate(MuzzleFlashPrefab, origin, Quaternion.LookRotation(dir)), 1f);

        if (Physics.Raycast(origin, dir, out RaycastHit hit, AttackRange))
        {
            hit.collider.GetComponentInParent<PlayerHealth>()?.TakeDamage(Damage, origin);

            if (HitVFXPrefab != null)
            {
                Quaternion rot = hit.normal.sqrMagnitude > 0.001f ? Quaternion.LookRotation(hit.normal) : Quaternion.identity;
                Destroy(Instantiate(HitVFXPrefab, hit.point, rot), HitVFXLifetime);
            }
        }

        OnShotFired?.Invoke();
    }

    private void DrawLaser(Vector3 target)
    {
        if (LaserRenderer == null) return;

        Vector3 origin = GetFireOrigin();
        Vector3 dir = (target - origin);
        if (dir.sqrMagnitude < 0.001f) return;
        dir.Normalize();

        Vector3 end = Physics.Raycast(origin, dir, out RaycastHit hit, AttackRange)
            ? hit.point
            : origin + dir * AttackRange;

        LaserRenderer.SetPosition(0, origin);
        LaserRenderer.SetPosition(1, end);
    }

    private Vector3 GetAimPoint()
    {
        if (PlayerHealth.Transform == null) return transform.position + transform.forward * 10f;

        Vector3 point = PlayerHealth.Transform.position + Vector3.up * TargetHeightOffset;

        if (LeadTime > 0f && PlayerHealth.Transform.TryGetComponent(out Rigidbody rb))
            point += rb.linearVelocity * LeadTime;

        return point;
    }

    private Vector3 GetFireOrigin() => FirePoint != null ? FirePoint.position : transform.position;

    private void EndCycle()
    {
        _aiming = false;
        _locking = false;
        _routine = null;
        _cooldownTimer = FireCooldown * CooldownMultiplier;

        if (LaserRenderer != null) LaserRenderer.enabled = false;
        _flight.ExternalSpeedMultiplier = 1f;

        if (RepositionAfterShot) _flight.Reposition();
    }

    // Cancels an in-progress aim (LOS lost, stagger, death) without paying the
    // full post-shot cooldown — it re-acquires as soon as it has a shot again.
    private void Abort()
    {
        if (_routine != null) { StopCoroutine(_routine); _routine = null; }

        _aiming = false;
        _locking = false;

        if (LaserRenderer != null) LaserRenderer.enabled = false;
        _flight.ExternalSpeedMultiplier = 1f;
        _cooldownTimer = Mathf.Max(_cooldownTimer, 0.5f);

        OnAimAborted?.Invoke();
    }

    private void OnStateChanged(EnemyState state)
    {
        if (state != EnemyState.Aggro && _aiming) Abort();
    }

    private void OnDrawGizmosSelected()
    {
        // Ring on the floor beneath the flyer, tethered by a drop line — the reach
        // is a flat XZ distance, so showing it at altitude just floats it loose.
        Vector3 center = EnemyGizmos.Ground(transform.position);
        EnemyGizmos.GroundRing(center, AttackRange, EnemyGizmos.Sniper,
                               $"snipe {AttackRange:0.#}m", 250f);
        EnemyGizmos.DropLine(transform.position, center, EnemyGizmos.Sniper);
    }
}
