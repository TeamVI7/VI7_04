// MechAttackBehaviour.cs
using System;
using System.Collections;
using UnityEngine;

public abstract class MechAttackBehaviour : MonoBehaviour
{
    [Header("Range")]
    public float minRange = 0f;
    public float maxRange = 10f;

    [Header("Cooldown / Weight")]
    public float cooldown = 5f;
    [Range(0f, 10f)] public float weight = 1f;

    [Header("Token Cost")]
    [Tooltip("Tokens this attack spends from the boss's MechTokenPool. Cheap jabs ~5, heavy moves ~10-15, ultimates ~20+ so the boss has to visibly build up to them. 0 makes the attack free — use that for the filler move the boss falls back on with an empty pool. Ignored entirely if the boss has no MechTokenPool component.")]
    public int tokenCost = 5;

    [Header("Phase Gating")]
    [Tooltip("Boss must be at this Phase or higher for this attack to be selectable. Leave at 1 for attacks available from the start.")]
    public int minPhase = 1;

    [Header("Gizmos")]
    [Tooltip("Draw this attack's ranges in the scene view while the boss is selected. Every attack on the boss draws at once, so switch off the ones you aren't currently tuning — that's the difference between a readable diagram and a ball of wire.")]
    public bool drawGizmos = true;

    protected MechBossBrain brain;
    private float _cooldownTimer;

    private Coroutine _routine;
    private Action _onComplete;
    private bool _completed;

    /// <summary>True from the moment <see cref="Execute"/> is called until the
    /// attack finishes or is aborted. Owned by this base class — concrete attacks
    /// never set it, so it can't get out of sync with the running coroutine.</summary>
    public bool IsExecuting { get; private set; }

    public float CooldownRemaining => Mathf.Max(0f, _cooldownTimer);

    protected virtual void Awake() => brain = GetComponent<MechBossBrain>();

    protected virtual void Update()
    {
        if (_cooldownTimer > 0f) _cooldownTimer -= Time.deltaTime;
    }

    public virtual bool IsAvailable(float distanceToPlayer) =>
        !IsExecuting && _cooldownTimer <= 0f
        && brain.Phase >= minPhase
        && distanceToPlayer >= minRange && distanceToPlayer <= maxRange;

    /// <summary>True if MechChaseBehaviour should keep repositioning the mech while
    /// this attack executes (sustained ranged attacks that benefit from kiting/
    /// strafing). Defaults to false — most attacks (melee, AOE anchored to a ground
    /// point, or ones like Dash that drive the transform themselves) want the mech
    /// planted for their duration. The mech still turns to face the player during
    /// any attack regardless of this flag.</summary>
    public virtual bool AllowsMovementDuringExecution => false;

    // Scaled by the brain's per-phase multiplier so later phases come back at the
    // player faster without every attack needing its own phase-aware tuning.
    public void BeginCooldown() => _cooldownTimer = cooldown * (brain != null ? brain.CooldownMultiplier : 1f);

    #region Run / Abort

    /// <summary>The attack itself. Implement this instead of driving your own
    /// coroutine: the base class owns IsExecuting, the completion callback and the
    /// abort path, so every attack gets interruption for free and none of them can
    /// strand the boss in the Attacking state.</summary>
    protected abstract IEnumerator Run();

    /// <summary>Optional cleanup when an attack is cut short mid-flight — restoring
    /// a NavMeshAgent, clearing animator bools, tearing down beams. Not called on a
    /// normal finish, where <see cref="Run"/> has already cleaned up after itself.</summary>
    protected virtual void OnAborted() { }

    public void Execute(Action onComplete)
    {
        // Re-entry used to return without invoking the callback, which left the
        // brain in Attacking forever and froze the boss for the rest of the fight.
        // Hand the turn straight back instead.
        if (IsExecuting)
        {
            Debug.LogWarning($"[{GetType().Name}] Execute called while already executing — ignoring and returning the turn.", this);
            onComplete?.Invoke();
            return;
        }

        _onComplete = onComplete;
        _completed = false;
        IsExecuting = true;
        _routine = StartCoroutine(Co_Run());
    }

    // Run() is yielded inline rather than started as its own coroutine on purpose:
    // a nested StartCoroutine survives StopCoroutine on its parent, so aborting
    // would stop this wrapper and leave the actual attack running.
    private IEnumerator Co_Run()
    {
        yield return Run();
        Complete();
    }

    /// <summary>Cuts the attack short — the boss got staggered, or died, mid-swing.
    /// Safe to call on an idle attack. The completion callback still fires, so the
    /// brain and the selector's recovery timer both resolve normally.</summary>
    public void Abort()
    {
        if (!IsExecuting) return;

        if (_routine != null)
        {
            StopCoroutine(_routine);
            _routine = null;
        }

        OnAborted();
        RaiseTelegraphCancelled(); // pull down any telegraph visual this attack put up
        Complete();
    }

    private void Complete()
    {
        if (_completed) return;
        _completed = true;
        IsExecuting = false;
        _routine = null;

        // Cleared before invoking: the callback re-enters the selector, which can
        // pick and start the next attack in the same call stack.
        Action callback = _onComplete;
        _onComplete = null;
        callback?.Invoke();
    }

    #endregion

    #region Telegraph

    // Shared wind-up event API so any telegraph preset (laser, decal, emission
    // flash) can hook a concrete attack without knowing its internals. Each
    // concrete attack calls these at the equivalent points in its own Run()
    // coroutine — RaiseTelegraphStart(windupField) at the start of the wind-up,
    // RaiseTelegraphResolved() when the attack actually lands/fires, and
    // RaiseTelegraphCancelled() if it's aborted before resolving.
    public event System.Action OnTelegraphStart;
    public event System.Action OnTelegraphResolved;
    public event System.Action OnTelegraphCancelled;

    public float TelegraphDuration { get; private set; }

    protected void RaiseTelegraphStart(float duration)
    {
        TelegraphDuration = duration;
        OnTelegraphStart?.Invoke();
    }

    protected void RaiseTelegraphResolved() => OnTelegraphResolved?.Invoke();
    protected void RaiseTelegraphCancelled() => OnTelegraphCancelled?.Invoke();

    #endregion
}
