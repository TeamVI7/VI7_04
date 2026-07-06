using System;
using System.Collections;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Active melee attack — closes distance via NavMeshAgent (owned by PatrolBehaviour),
/// then swings when player is within AttackRange. Two hit-timing modes so it scales
/// from "no animator yet" prototyping to fully anim-event-driven combat later.
///
/// SETUP:
///   1. Attach next to EnemyBrain / EnemyHealth / NavMeshAgent.
///   2. Optional: assign EnemyAnimatorController to trigger "Punch" + get anim-driven timing.
///   3. Mode = Timed: swing lands after windupTime, no animator wiring needed.
///      Mode = AnimationDriven: call AnimEvent_MeleeHit() from an Animation Event on
///      the swing clip's impact frame (same pattern as AnimationEventReceiver).
///
/// EXTENDING:
///   - Multiple attack variants → add an enum + pick windup/damage per variant in TryAttack().
///   - Combo strings → chain coroutines off OnAttackLanded.
///   - Block/parry reaction → have PlayerHealth check EnemyMeleeAttack.IsWindingUp before damage.
///   - Cone check instead of sphere → swap the check in IsPlayerInRange().
///
/// DEBUG:
///   - debugLog logs range checks, windup, hit/miss.
///   - Gizmo sphere shows AttackRange in Scene view when selected.
/// </summary>
[RequireComponent(typeof(EnemyBrain))]
[RequireComponent(typeof(EnemyHealth))]
public class MeleeAttackBehaviour : MonoBehaviour
{
    // ─────────────────────────────────────────────────────────────────────────
    #region Inspector
    // ─────────────────────────────────────────────────────────────────────────

    public enum HitTiming { Timed, AnimationDriven }

    [Header("Range")]
    public float AttackRange     = 2f;
    [Tooltip("Player must be within this angle (degrees) of forward facing to be hit. 180 = ignore facing.")]
    public float AttackConeAngle = 180f;

    [Header("Damage")]
    public float Damage   = 15f;
    public float KnockbackForce = 8f;
    public float Cooldown = 1.5f;

    [Header("Timing")]
    public HitTiming TimingMode = HitTiming.Timed;
    [Tooltip("Used only when TimingMode = Timed. Seconds from TryAttack() to damage applying.")]
    public float WindupTime = 0.35f;
    [Tooltip("Seconds after damage before attack is fully 'done' and cooldown starts counting from.")]
    public float RecoveryTime = 0.25f;

    [Header("References")]
    [Tooltip("Optional — triggers Punch anim + lets HitTiming.AnimationDriven work.")]
    public EnemyAnimatorController AnimCtrl;

    [Header("Debug")]
    [SerializeField] private bool debugLog   = false;
    [SerializeField] private bool drawGizmos = true;

    #endregion

    // ─────────────────────────────────────────────────────────────────────────
    #region Events
    // ─────────────────────────────────────────────────────────────────────────

    public event Action OnAttackStarted;   // windup begins
    public event Action OnAttackLanded;    // damage applied (hit confirmed)
    public event Action OnAttackMissed;    // windup finished but player left range
    public event Action OnAttackRecovered; // cooldown now free

    #endregion

    // ─────────────────────────────────────────────────────────────────────────
    #region Public Read-only State
    // ─────────────────────────────────────────────────────────────────────────

    public bool IsWindingUp   { get; private set; }
    public bool IsOnCooldown  => _cooldownTimer > 0f;
    public bool CanAttack     => !IsWindingUp && !IsOnCooldown && _brain.State == EnemyState.Aggro;

    #endregion

    // ─────────────────────────────────────────────────────────────────────────
    #region Private
    // ─────────────────────────────────────────────────────────────────────────

    private EnemyBrain    _brain;
    private NavMeshAgent   _nav;
    private float          _cooldownTimer;
    private bool           _pendingAnimHit; // set true during AnimationDriven windup
    private Coroutine      _attackRoutine;

    #endregion

    // ─────────────────────────────────────────────────────────────────────────
    #region Unity Lifecycle
    // ─────────────────────────────────────────────────────────────────────────

    private void Awake()
    {
        _brain = GetComponent<EnemyBrain>();
        _nav   = GetComponent<NavMeshAgent>();

        if (AnimCtrl == null) AnimCtrl = GetComponent<EnemyAnimatorController>();

        _brain.OnStateChanged += OnStateChanged;
    }

    private void OnDestroy() => _brain.OnStateChanged -= OnStateChanged;

    private void Update()
    {
        if (_cooldownTimer > 0f) _cooldownTimer -= Time.deltaTime;

        if (_brain.State != EnemyState.Aggro) return;
        if (PlayerHealth.Transform == null)   return;

        if (IsPlayerInRange() && CanAttack)
            TryAttack();
    }

    #endregion

    // ─────────────────────────────────────────────────────────────────────────
    #region Range Check
    // ─────────────────────────────────────────────────────────────────────────

    private bool IsPlayerInRange()
    {
        Vector3 toPlayer = PlayerHealth.Transform.position - transform.position;
        toPlayer.y = 0f;

        if (toPlayer.magnitude > AttackRange) return false;
        if (AttackConeAngle >= 180f) return true;

        float angle = Vector3.Angle(transform.forward, toPlayer);
        return angle <= AttackConeAngle * 0.5f;
    }

    #endregion

    // ─────────────────────────────────────────────────────────────────────────
    #region Attack Logic
    // ─────────────────────────────────────────────────────────────────────────

    private void TryAttack()
    {
        if (_attackRoutine != null) StopCoroutine(_attackRoutine);
        _attackRoutine = StartCoroutine(Co_Attack());
    }

    private IEnumerator Co_Attack()
    {
        IsWindingUp = true;
        _pendingAnimHit = false;

        // Stop moving while swinging — snappier read, no orbit-strafe-punch nonsense
        if (_nav != null && _nav.isOnNavMesh) _nav.isStopped = true;

        FaceTarget();
        AnimCtrl?.TriggerPunch();
        OnAttackStarted?.Invoke();
        Log("Attack windup start.");

        if (TimingMode == HitTiming.Timed)
        {
            yield return new WaitForSeconds(WindupTime);
            ResolveHit();
        }
        else // AnimationDriven — wait for AnimEvent_MeleeHit(), with a safety timeout
        {
            _pendingAnimHit = true;
            float timeout = WindupTime + 1f;
            float elapsed  = 0f;
            while (_pendingAnimHit && elapsed < timeout)
            {
                elapsed += Time.deltaTime;
                yield return null;
            }
            if (_pendingAnimHit)
            {
                LogWarning("AnimationDriven hit timed out — did you forget the Animation Event?");
                ResolveHit();
            }
        }

        yield return new WaitForSeconds(RecoveryTime);

        IsWindingUp    = false;
        _cooldownTimer = Cooldown;
        if (_nav != null && _nav.isOnNavMesh) _nav.isStopped = false;

        OnAttackRecovered?.Invoke();
        Log("Attack recovered.");
        _attackRoutine = null;
    }

    /// <summary>Call from an Animation Event on the swing's impact frame when TimingMode = AnimationDriven.</summary>
    public void AnimEvent_MeleeHit()
    {
        _pendingAnimHit = false;
        ResolveHit();
    }

    private void ResolveHit()
    {
        if (!IsPlayerInRange())
        {
            Log("Attack missed — player left range.");
            OnAttackMissed?.Invoke();
            return;
        }

        Vector3 knockDir = PlayerHealth.Transform.position - transform.position;
        knockDir.y = 0f;
        knockDir = knockDir.sqrMagnitude > 0.001f ? knockDir.normalized : transform.forward;

        PlayerHealth.Instance?.TakeDamage(Damage);
        Log($"Attack landed — {Damage} dmg, {KnockbackForce} knockback.");
        OnAttackLanded?.Invoke();
    }

    private void FaceTarget()
    {
        Vector3 dir = PlayerHealth.Transform.position - transform.position;
        dir.y = 0f;
        if (dir != Vector3.zero)
            transform.rotation = Quaternion.LookRotation(dir);
    }

    #endregion

    // ─────────────────────────────────────────────────────────────────────────
    #region State Reaction
    // ─────────────────────────────────────────────────────────────────────────

    private void OnStateChanged(EnemyState state)
    {
        if (state == EnemyState.Dead || state == EnemyState.Staggered)
        {
            if (_attackRoutine != null) { StopCoroutine(_attackRoutine); _attackRoutine = null; }
            IsWindingUp = false;
            if (_nav != null && _nav.isOnNavMesh) _nav.isStopped = false;
        }
    }

    #endregion

    // ─────────────────────────────────────────────────────────────────────────
    #region Debug
    // ─────────────────────────────────────────────────────────────────────────

    [System.Diagnostics.Conditional("UNITY_EDITOR")]
    private void Log(string msg)
    {
        if (debugLog) Debug.Log($"[MeleeAttack] {name}: {msg}", this);
    }

    private void LogWarning(string msg) => Debug.LogWarning($"[MeleeAttack] {name}: {msg}", this);

#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        if (!drawGizmos) return;
        Gizmos.color = CanAttack ? Color.red : new Color(1f, 0.5f, 0f, 0.6f);
        Gizmos.DrawWireSphere(transform.position, AttackRange);
    }
#endif

    #endregion
}