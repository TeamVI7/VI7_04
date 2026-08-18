// MechAttackBehaviour.cs
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

    protected MechBossBrain brain;
    private float _cooldownTimer;

    public bool IsExecuting { get; protected set; }
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

    public abstract void Execute(System.Action onComplete);

    #region Telegraph

    // Shared wind-up event API so any telegraph preset (laser, decal, emission
    // flash) can hook a concrete attack without knowing its internals. Each
    // concrete attack calls these at the equivalent points in its own Execute()
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