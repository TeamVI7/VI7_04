using UnityEngine;
using UnityEngine.AI;
using FIMSpace.FProceduralAnimation;

/// <summary>
/// Activates ragdoll physics when EnemyHealth reports death.
/// GDD §7.4 — all deaths physics-resolved, fast arrival = violent impact.
///
/// Drives the Ragdoll Animator 2 asset (FImpossible Creations) instead of manually
/// toggling Rigidbody/Collider per-bone — the asset owns bone physics, blending,
/// and get-up. This component still owns the surrounding gameplay state (NavMeshAgent,
/// main collider, top-level Animator, despawn timing) since those aren't part of the
/// asset's rig.
///
/// CONFIRMED API (FIMSpace.FProceduralAnimation.RagdollAnimator2):
///   Fall/ragdoll   -> User_SwitchFallState(false)
///   Impact seeding -> User_AddAllBonesImpact(velocity, duration, forceMode)
///   Recovery       -> User_TransitionToStandingMode(duration, delay)
///   These are extension methods on IRagdollAnimator2HandlerOwner, which
///   RagdollAnimator2 implements — call them directly on the component reference.
/// </summary>
[RequireComponent(typeof(EnemyHealth))]
public class EnemyRagdoll : MonoBehaviour
{
    // ─────────────────────────────────────────────────────────────────────────
    #region Inspector
    // ─────────────────────────────────────────────────────────────────────────

    [Header("Ragdoll Animator 2 Driver")]
    [Tooltip("The RagdollAnimator2 component set up on this character via their setup tool.")]
    public RagdollAnimator2 ragdollAnimator;

    [Header("Config — same fields EnemySetup.cs already pushes into")]
    public float VelocitySeedScale  = 1.4f;
    public float UpwardKick         = 2f;
    public bool  AutoDespawn        = true;
    public float LifetimeAfterDeath = 8f;

    [Header("Impact")]
    [Tooltip("Duration passed to User_AddAllBonesImpact — 0 applies as an instant impulse.")]
    public float impactDuration = 0f;
    public ForceMode impactForceMode = ForceMode.Impulse;

    [Header("Auto Recovery (optional)")]
    [Tooltip("If true, transitions back to standing/animated after a delay. Leave off for permanent-death enemies.")]
    public bool  autoRecover        = false;
    public float recoverDelay       = 4f;
    public float recoverTransitionDuration = 0.8f;

    [Header("Debug")]
    [SerializeField] private bool debugLog = false;

    #endregion

    // ─────────────────────────────────────────────────────────────────────────
    #region Private State
    // ─────────────────────────────────────────────────────────────────────────

    private Animator     _animator;
    private NavMeshAgent  _nav;
    private Collider      _mainCollider;
    private Rigidbody     _mainRb;
    private EnemyHealth   _health;
    private bool          _triggered;

    #endregion

    // ─────────────────────────────────────────────────────────────────────────
    #region Unity Lifecycle
    // ─────────────────────────────────────────────────────────────────────────

    private void Awake()
    {
        _health       = GetComponent<EnemyHealth>();
        _animator     = GetComponent<Animator>();
        _nav          = GetComponent<NavMeshAgent>();
        _mainCollider = GetComponent<Collider>();
        _mainRb       = GetComponent<Rigidbody>();

        if (ragdollAnimator == null)
            ragdollAnimator = GetComponentInChildren<RagdollAnimator2>();

        _health.OnDied += Activate;

        if (ragdollAnimator == null)
            LogWarning("ragdollAnimator not assigned/found — death will still disable nav/collider/animator, but no ragdoll will play.");
    }

    private void OnDestroy() => _health.OnDied -= Activate;

    #endregion

    // ─────────────────────────────────────────────────────────────────────────
    #region Death / Ragdoll Trigger
    // ─────────────────────────────────────────────────────────────────────────

    private void Activate(Vector3 impulse)
    {
        if (_triggered) return;
        _triggered = true;

        // Hand off gameplay control the same way the old manual version did —
        // this part has nothing to do with which physics backend drives the bones.
        if (_nav          != null) _nav.enabled          = false;
        if (_mainCollider != null) _mainCollider.enabled  = false;
        if (_mainRb       != null) _mainRb.isKinematic    = true;
        if (_animator     != null) _animator.enabled      = false;

        TriggerRagdoll(impulse);

        if (AutoDespawn) Destroy(gameObject, LifetimeAfterDeath);
    }

    private void TriggerRagdoll(Vector3 impulse)
    {
        if (ragdollAnimator == null) return;

        Vector3 seed = impulse * VelocitySeedScale + Vector3.up * UpwardKick;

        ragdollAnimator.User_SwitchFallState(false);
        ragdollAnimator.User_AddAllBonesImpact(seed, impactDuration, impactForceMode);

        Log($"Ragdoll triggered — seed velocity {seed}.");

        if (autoRecover)
            Invoke(nameof(TryRecover), recoverDelay);
    }

    private void TryRecover()
    {
        if (ragdollAnimator == null) return;

        // 3 positional args forces the compiler to pick the 6-param overload —
        // with only 1-2 args both overloads' optional params match equally and
        // the call is ambiguous (see RagdollHandlerUtils.GetUpHelpers.cs).
        ragdollAnimator.User_TransitionToStandingMode(recoverTransitionDuration, 0.6f, 0.1f);
        Log("Ragdoll recovery started.");
    }

    #endregion

    // ─────────────────────────────────────────────────────────────────────────
    #region Debug
    // ─────────────────────────────────────────────────────────────────────────

    [System.Diagnostics.Conditional("UNITY_EDITOR")]
    private void Log(string msg)
    {
        if (debugLog) Debug.Log($"[EnemyRagdoll] {name}: {msg}", this);
    }

    private void LogWarning(string msg) => Debug.LogWarning($"[EnemyRagdoll] {name}: {msg}", this);

    #endregion
}