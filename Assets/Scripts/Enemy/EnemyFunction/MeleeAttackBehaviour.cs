using System;
using System.Collections;
using UnityEngine;
using UnityEngine.AI;

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
    [Tooltip("Degrees/sec the enemy turns to face the player during windup. Replaces an instant snap-to-face so the tell is actually readable mid-swing.")]
    public float TurnSpeed = 12f;

    [Header("Lunge")]
    [Tooltip("Distance the enemy dashes toward the player the instant a swing starts, on top of normal chase movement. 0 disables the lunge.")]
    public float LungeDistance = 1.5f;
    [Tooltip("NavMeshAgent speed used only during the lunge burst.")]
    public float LungeSpeed = 14f;

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
    private Coroutine      _lungeRoutine;

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

        // Stop normal chase movement — the lunge below drives any movement during the
        // swing instead, so the dash reads as a deliberate burst, not just more chase.
        if (_nav != null && _nav.isOnNavMesh) _nav.isStopped = true;

        AnimCtrl?.TriggerPunch();
        OnAttackStarted?.Invoke();
        Log("Attack windup start.");

        if (_lungeRoutine != null) StopCoroutine(_lungeRoutine);
        _lungeRoutine = StartCoroutine(Co_Lunge());

        if (TimingMode == HitTiming.Timed)
        {
            yield return StartCoroutine(Co_TurnDuringWindup(WindupTime));
            ResolveHit();
        }
        else // AnimationDriven — wait for AnimEvent_MeleeHit(), with a safety timeout
        {
            _pendingAnimHit = true;
            float timeout = WindupTime + 1f;
            float elapsed  = 0f;
            while (_pendingAnimHit && elapsed < timeout)
            {
                FaceTargetSmooth();
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

    private IEnumerator Co_TurnDuringWindup(float duration)
    {
        float t = 0f;
        while (t < duration)
        {
            FaceTargetSmooth();
            t += Time.deltaTime;
            yield return null;
        }
    }

    private void FaceTargetSmooth()
    {
        if (PlayerHealth.Transform == null) return;
        Vector3 dir = PlayerHealth.Transform.position - transform.position;
        dir.y = 0f;
        if (dir.sqrMagnitude < 0.0001f) return;

        transform.rotation = Quaternion.RotateTowards(
            transform.rotation, Quaternion.LookRotation(dir), TurnSpeed * 10f * Time.deltaTime);
    }

    private IEnumerator Co_Lunge()
    {
        if (LungeDistance <= 0f || _nav == null || !_nav.isOnNavMesh || PlayerHealth.Transform == null)
            yield break;

        Vector3 toPlayer = PlayerHealth.Transform.position - transform.position;
        toPlayer.y = 0f;
        if (toPlayer.sqrMagnitude < 0.01f) yield break;

        Vector3 lungeTarget = transform.position + toPlayer.normalized * Mathf.Min(LungeDistance, toPlayer.magnitude);

        float originalSpeed = _nav.speed;
        _nav.speed = LungeSpeed;
        _nav.isStopped = false;
        _nav.SetDestination(lungeTarget);

        float lungeWindow = Mathf.Min(WindupTime, LungeDistance / Mathf.Max(1f, LungeSpeed) + 0.05f);
        yield return new WaitForSeconds(lungeWindow);

        if (_nav != null && _nav.isOnNavMesh)
        {
            _nav.isStopped = true;
            _nav.speed = originalSpeed;
        }

        _lungeRoutine = null;
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
            if (_lungeRoutine  != null) { StopCoroutine(_lungeRoutine);  _lungeRoutine  = null; }
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