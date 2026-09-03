using System;
using System.Collections;
using UnityEngine;
using UnityEngine.AI;

// Reacts to PlayerAimThreat: if the player's crosshair settles on this enemy
// before it's actually been shot, sidesteps out of the way. Hands PatrolBehaviour's
// NavMeshAgent control over to itself for the dodge window via MovementOverrideActive
// — without that hand-off, Patrol's own per-frame SetDestination stomps the dodge
// destination the very next tick (same ownership problem TickAim solves for rotation).
[RequireComponent(typeof(EnemyBrain))]
[RequireComponent(typeof(EnemyHealth))]
[RequireComponent(typeof(PatrolBehaviour))]
public class EnemyDodgeBehaviour : MonoBehaviour
{
    [Header("Trigger")]
    [Range(0f, 1f)] public float DodgeChance = 0.65f;
    public float DodgeCooldown = 2.5f;
    [Tooltip("Only reacts to being aimed at within this distance band — too close and a sidestep barely helps, too far and the threat isn't real yet.")]
    public float MinTriggerRange = 4f;
    public float MaxTriggerRange = 30f;

    [Header("Movement")]
    public float DodgeDistance = 3f;
    public float DodgeSpeed = 9f;

    [Header("Debug")]
    [SerializeField] private bool debugLog = false;

    public event Action OnDodgeStarted;

    private EnemyBrain           _brain;
    private EnemyHealth          _health;
    private NavMeshAgent          _nav;
    private PatrolBehaviour       _patrol;
    private MeleeAttackBehaviour  _meleeGate;
    private SniperAttackBehaviour _sniperGate;

    private float     _cooldownTimer;
    private Coroutine _dodgeRoutine;

    private void Awake()
    {
        _brain      = GetComponent<EnemyBrain>();
        _health     = GetComponent<EnemyHealth>();
        _nav        = GetComponent<NavMeshAgent>();
        _patrol     = GetComponent<PatrolBehaviour>();
        _meleeGate  = GetComponent<MeleeAttackBehaviour>();
        _sniperGate = GetComponent<SniperAttackBehaviour>();

        _brain.OnStateChanged += HandleStateChanged;
    }

    private void OnDestroy() => _brain.OnStateChanged -= HandleStateChanged;

    private void Update()
    {
        if (_cooldownTimer > 0f) _cooldownTimer -= Time.deltaTime;

        if (_dodgeRoutine != null) return;
        if (_cooldownTimer > 0f) return;
        if (_brain.State != EnemyState.Aggro) return;
        if (PlayerHealth.Transform == null) return;
        if (_patrol.MovementStyle == PatrolBehaviour.CombatMovementStyle.Aggressive) return; // rushers never stop closing, don't undercut that
        if (IsGated()) return;
        if (PlayerAimThreat.Instance.CurrentTarget != _health) return;

        float dist = Vector3.Distance(transform.position, PlayerHealth.Transform.position);
        if (dist < MinTriggerRange || dist > MaxTriggerRange) return;

        if (UnityEngine.Random.value > DodgeChance)
        {
            // Failed roll still eats a short cooldown so sustained aim doesn't re-roll every frame.
            _cooldownTimer = DodgeCooldown * 0.3f;
            return;
        }

        if (!TryGetDodgePoint(out Vector3 point)) return;
        if (!EnemySquadCoordinator.TryAcquireDodgeSlot()) return;

        Log("Dodge triggered.");
        _dodgeRoutine = StartCoroutine(Co_Dodge(point));
    }

    private bool IsGated()
    {
        if (_meleeGate  != null && _meleeGate.IsWindingUp)        return true;
        if (_sniperGate != null && _sniperGate.IsAimingOrLocking) return true;
        return false;
    }

    // Prefers whichever side actually has NavMesh room — random pick if both do.
    private bool TryGetDodgePoint(out Vector3 point)
    {
        point = default;
        if (!_nav.enabled || !_nav.isOnNavMesh) return false;

        Vector3 toSelf = transform.position - PlayerHealth.Transform.position;
        toSelf.y = 0f;
        if (toSelf.sqrMagnitude < 0.01f) toSelf = transform.forward;
        toSelf.Normalize();

        Vector3 side = Vector3.Cross(Vector3.up, toSelf);

        bool rightOk = NavMesh.SamplePosition(transform.position + side * DodgeDistance, out NavMeshHit right, DodgeDistance * 0.75f, NavMesh.AllAreas);
        bool leftOk  = NavMesh.SamplePosition(transform.position - side * DodgeDistance, out NavMeshHit left,  DodgeDistance * 0.75f, NavMesh.AllAreas);

        if (rightOk && leftOk) { point = UnityEngine.Random.value < 0.5f ? right.position : left.position; return true; }
        if (rightOk) { point = right.position; return true; }
        if (leftOk)  { point = left.position;  return true; }
        return false;
    }

    private IEnumerator Co_Dodge(Vector3 point)
    {
        _patrol.MovementOverrideActive = true;
        float originalSpeed = _nav.speed;
        _nav.speed = DodgeSpeed;
        _nav.SetDestination(point);

        OnDodgeStarted?.Invoke();

        float window = DodgeDistance / Mathf.Max(1f, DodgeSpeed) + 0.15f;
        yield return new WaitForSeconds(window);

        EndDodge(originalSpeed);
    }

    private void EndDodge(float originalSpeed)
    {
        if (_nav.enabled && _nav.isOnNavMesh) _nav.speed = originalSpeed;
        _patrol.MovementOverrideActive = false;
        _cooldownTimer = DodgeCooldown;
        _dodgeRoutine  = null;
        EnemySquadCoordinator.ReleaseDodgeSlot();
        Log("Dodge ended.");
    }

    private void HandleStateChanged(EnemyState state)
    {
        if (state != EnemyState.Dead && state != EnemyState.Staggered) return;
        if (_dodgeRoutine == null) return;

        StopCoroutine(_dodgeRoutine);
        _dodgeRoutine = null;
        _patrol.MovementOverrideActive = false;
        EnemySquadCoordinator.ReleaseDodgeSlot();
    }

    [System.Diagnostics.Conditional("UNITY_EDITOR")]
    private void Log(string msg)
    {
        if (debugLog) Debug.Log($"[EnemyDodge] {name}: {msg}", this);
    }

#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        // Dodging only fires inside this band, so draw it as a band.
        Vector3 center = EnemyGizmos.Ground(transform.position);
        EnemyGizmos.GroundBand(center, MinTriggerRange, MaxTriggerRange, EnemyGizmos.Dodge,
                               $"dodge {MinTriggerRange:0.#}–{MaxTriggerRange:0.#}m", 130f);
    }
#endif
}
