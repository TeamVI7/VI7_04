// MechTokenPool.cs — the boss's "energy" budget for attacking.
//
// Instead of every attack being gated only by its own cooldown, the boss holds a
// pool of tokens that refills over time and every attack costs some to fire. A
// jab costs a little and comes out often; an ultimate costs a big slice of the
// pool, so the boss visibly has to go quiet and build up to it. That rhythm —
// pressure, lull, huge hit — is the whole point of the system, and it's what a
// pile of independent cooldowns can't produce on its own.
//
// This component is OPTIONAL. With no MechTokenPool on the boss, MechAttackSelector
// ignores costs entirely and behaves exactly as it did before.
using System;
using UnityEngine;

[RequireComponent(typeof(MechBossBrain))]
public class MechTokenPool : MonoBehaviour
{
    [Header("Pool")]
    public int maxTokens = 50;
    [Tooltip("Tokens the boss starts the fight with. Keep it below the cost of the ultimate so the fight can't open with one.")]
    public int startingTokens = 10;

    [Header("Regen")]
    [Tooltip("Tokens gained per second while the boss is alive. This is the real pacing dial: cost of an attack divided by this is roughly how many seconds of build-up it represents.")]
    public float regenPerSecond = 2f;
    [Tooltip("Regen is multiplied by this, indexed by phase (element 0 = phase 1, element 1 = phase 2, ...). Above 1 means the later phase pays for its big moves faster. Phases past the end reuse the last entry.")]
    public float[] phaseRegenMultipliers = { 1f, 1.3f, 1.8f };
    [Tooltip("Regen while the boss is mid-attack, as a fraction of normal. Below 1 means long attacks cost the boss build-up time as well as tokens, so spamming cheap moves genuinely delays the ultimate instead of being free.")]
    [Range(0f, 1f)] public float regenDuringAttack = 0.5f;
    [Tooltip("Regen while staggered, as a fraction of normal. 0 means a stagger is a real tempo win for the player — it doesn't just interrupt the boss, it sets its build-up back.")]
    [Range(0f, 1f)] public float regenWhileStaggered = 0f;

    [Header("Phase Up")]
    [Tooltip("Tokens granted instantly on each phase transition, so the new phase can open with a real move instead of the boss standing there saving up.")]
    public int tokensOnPhaseUp = 15;

    [Header("Debug")]
    [SerializeField] private bool showDebugLogs = true;

    /// <summary>Fired whenever the balance changes. (current, max) — for a boss
    /// energy bar, or just to watch the economy while tuning.</summary>
    public event Action<int, int> OnTokensChanged;

    public int Current { get; private set; }
    public int Max => Mathf.Max(1, maxTokens);
    public float Normalized => Mathf.Clamp01(Current / (float)Max);

    private MechBossBrain _brain;
    private float _fraction; // sub-token regen carried between frames
    private int _lastReported = -1;

    private void Awake()
    {
        _brain = GetComponent<MechBossBrain>();
        Current = Mathf.Clamp(startingTokens, 0, Max);
        _brain.OnPhaseChanged += HandlePhaseChanged;
    }

    private void OnDestroy()
    {
        if (_brain != null) _brain.OnPhaseChanged -= HandlePhaseChanged;
    }

    private void Start() => Report();

    private void Update()
    {
        if (_brain.State == MechBossState.Dead) return;
        // No regen during the drop-in either — the boss shouldn't land with a full
        // bar just because the intro cutscene ran long.
        if (_brain.State == MechBossState.Dropping) return;
        if (Current >= Max) return;

        _fraction += regenPerSecond * PhaseRegenMultiplier() * StateRegenMultiplier() * Time.deltaTime;
        if (_fraction < 1f) return;

        int gained = Mathf.FloorToInt(_fraction);
        _fraction -= gained;
        Current = Mathf.Min(Max, Current + gained);
        Report();
    }

    public bool CanAfford(int cost) => Current >= cost;

    /// <summary>Pays for an attack. Returns false and spends nothing if the boss
    /// can't cover the cost — callers should treat that as "not usable yet".</summary>
    public bool TrySpend(int cost)
    {
        if (cost <= 0) return true; // free attacks always go through
        if (Current < cost) return false;

        Current -= cost;
        Report();
        Log($"Spent {cost} — {Current}/{Max} left.");
        return true;
    }

    public void Grant(int amount)
    {
        if (amount <= 0) return;
        Current = Mathf.Min(Max, Current + amount);
        Report();
    }

    private float PhaseRegenMultiplier()
    {
        if (phaseRegenMultipliers == null || phaseRegenMultipliers.Length == 0) return 1f;
        int i = Mathf.Clamp(_brain.Phase - 1, 0, phaseRegenMultipliers.Length - 1);
        return Mathf.Max(0f, phaseRegenMultipliers[i]);
    }

    private float StateRegenMultiplier()
    {
        switch (_brain.State)
        {
            case MechBossState.Attacking: return regenDuringAttack;
            case MechBossState.Staggered: return regenWhileStaggered;
            default: return 1f;
        }
    }

    private void HandlePhaseChanged(int prev, int next)
    {
        if (tokensOnPhaseUp <= 0) return;
        Grant(tokensOnPhaseUp);
        Log($"Phase {next} grant: +{tokensOnPhaseUp} — {Current}/{Max}.");
    }

    private void Report()
    {
        if (Current == _lastReported) return;
        _lastReported = Current;
        OnTokensChanged?.Invoke(Current, Max);
    }

    private void OnValidate()
    {
        maxTokens = Mathf.Max(1, maxTokens);
        startingTokens = Mathf.Clamp(startingTokens, 0, maxTokens);
    }

    [System.Diagnostics.Conditional("UNITY_EDITOR")]
    private void Log(string msg)
    {
        if (showDebugLogs) Debug.Log($"[MechTokenPool] {msg}", this);
    }
}
