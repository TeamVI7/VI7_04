// MechAttackSelector.cs
using System;
using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(MechBossBrain))]
public class MechAttackSelector : MonoBehaviour
{
    /// <summary>Swaps one attack out for another once a phase is reached — the
    /// phase 2 boss doing an earth spike where the phase 1 boss did a plain stomp.
    /// Better than just phase-gating the new move, because gating alone leaves the
    /// old one in the pool and the boss keeps mixing in its weaker version.</summary>
    [Serializable]
    public class AttackReplacement
    {
        [Tooltip("The move the boss stops using at this phase. It's removed from the random pool entirely, and any rotation slot holding it plays the replacement instead.")]
        public MechAttackBehaviour retired;
        [Tooltip("What it becomes. Leave empty to simply retire the old attack with nothing taking its place.")]
        public MechAttackBehaviour replacement;
    }

    /// <summary>An authored attack plan for a phase. Without one the boss just
    /// rolls weighted-random every time it goes Idle, which lets the same move
    /// come out several times in a row and reads as the boss having no plan.</summary>
    [Serializable]
    public class AttackDirective
    {
        public string label = "Phase 1";
        [Tooltip("This directive applies when the boss is at this phase or higher. The highest-numbered matching entry wins, so give each phase its own.")]
        public int phase = 1;
        [Tooltip("Ordered rotation the boss works through, looping. Entries that aren't currently usable (out of range, on cooldown, phase-gated, unaffordable) are skipped and the rotation moves past them — so the sequence is a priority order, not a rigid script.")]
        public MechAttackBehaviour[] rotation;
        [Tooltip("Upgrades unlocked at this phase, e.g. Stomp -> Earth Spike at phase 2. Applies to the rotation AND the random fallback, and STAYS APPLIED for later phases too — an upgrade is permanent, so you only list it on the phase that introduces it.")]
        public AttackReplacement[] replacements;
        [Tooltip("If nothing in the rotation is usable, fall back to a weighted-random pick over everything available. Turn this off to make the boss wait for its rotation instead of improvising.")]
        public bool fallbackToRandom = true;
        [Tooltip("Breathing room between attacks, in seconds — counted from the moment one finishes. Without this the selector fires the next attack on the same frame the last one ends, which is what makes the boss feel like it's spasming rather than fighting.")]
        public float recoveryBetweenAttacks = 1.2f;
    }

    [Header("Directives")]
    [Tooltip("Leave empty to keep the old pure weighted-random behaviour.")]
    public AttackDirective[] directives;
    [Tooltip("Used when no directive matches the current phase.")]
    public float defaultRecovery = 1.2f;

    [Header("Tokens")]
    [Tooltip("Saves up for expensive moves. When an ultimate is otherwise ready and only the token cost is missing, the boss stops spending on anything above the cap below — so it does light work while banking instead of frittering the pool away and never reaching the big move. Turn off to let it always fire whatever it can afford.")]
    public bool bankForUltimates = true;
    [Tooltip("An attack costing at least this much counts as an ultimate worth saving for.")]
    public int ultimateCostThreshold = 20;
    [Tooltip("While banking, the boss will only use attacks costing this or less. Keep it at or above your cheapest move's cost, or the boss will stand still while it saves.")]
    public int spendCapWhileSaving = 5;

    [Header("Repeats")]
    [Tooltip("Applies to the random fallback only — a rotation already controls its own order. Stops the same move being rolled twice running when something else was usable.")]
    public bool avoidRepeats = true;
    [Tooltip("How many of the most recent picks to avoid re-rolling. 1 = never the same attack twice in a row.")]
    public int repeatMemory = 1;

    [Header("Debug")]
    [SerializeField] private bool showDebugLogs = true;

    private MechBossBrain _brain;
    private MechTokenPool _tokens;
    private MechAttackBehaviour[] _attacks;
    private readonly List<MechAttackBehaviour> _recent = new List<MechAttackBehaviour>();
    private readonly List<MechAttackBehaviour> _validBuffer = new List<MechAttackBehaviour>();
    private AttackDirective _activeDirective;
    private int _rotationIndex;
    private float _recoveryTimer;

    // Recomputed once per selection tick and read by the affordability checks, so
    // the rotation and the random fallback can't disagree about what's usable.
    private int _spendCap;

    // Every replacement from every phase the boss has reached, not just the active
    // directive's. Upgrades are permanent — scoping them to one directive would
    // mean adding a phase 3 entry silently un-retires the phase 2 upgrades unless
    // you remember to copy them forward, and the old move quietly returns.
    private readonly List<AttackReplacement> _activeReplacements = new List<AttackReplacement>();
    private int _replacementsBuiltForPhase = -1;

    private void Awake()
    {
        _brain = GetComponent<MechBossBrain>();
        _tokens = GetComponent<MechTokenPool>(); // optional — null means costs are ignored
        _attacks = GetComponents<MechAttackBehaviour>();
        ValidateSetup();
    }

    /// <summary>Startup sanity pass. Every problem it catches shows up in play as
    /// "the boss just stands there" or "the upgrade never happened" with no error
    /// of its own, which is miserable to diagnose from the scene view.</summary>
    [System.Diagnostics.Conditional("UNITY_EDITOR")]
    private void ValidateSetup()
    {
        if (directives != null)
        {
            foreach (var d in directives)
            {
                if (d?.replacements == null) continue;

                foreach (var r in d.replacements)
                {
                    if (r == null || r.retired == null) continue;

                    if (Array.IndexOf(_attacks, r.retired) < 0)
                        Warn($"'{d.label}' retires {r.retired.GetType().Name}, which isn't a component on this boss — the mapping will never match.");

                    if (r.replacement == null) continue;

                    if (r.replacement == r.retired)
                        Warn($"'{d.label}' maps {r.retired.GetType().Name} to itself. Remove the entry.");

                    if (Array.IndexOf(_attacks, r.replacement) < 0)
                        Warn($"'{d.label}' replaces with {r.replacement.GetType().Name}, which isn't a component on this boss — the slot will be dead.");

                    // The upgrade fires from phase d.phase onward, so the new move
                    // has to be legal by then or the slot retires into nothing.
                    if (r.replacement.minPhase > d.phase)
                        Warn($"'{d.label}' (phase {d.phase}) upgrades to {r.replacement.GetType().Name}, but its Min Phase is {r.replacement.minPhase} — {r.retired.GetType().Name} is retired before its replacement is usable. Set Min Phase to {d.phase} or lower.");
                }
            }
        }

        if (_tokens == null) return;

        int cheapest = int.MaxValue;
        foreach (var a in _attacks)
        {
            if (a.tokenCost > _tokens.Max)
                Warn($"{a.GetType().Name} costs {a.tokenCost} but the pool only holds {_tokens.Max} — it can never be afforded.");
            cheapest = Mathf.Min(cheapest, a.tokenCost);
        }

        if (bankForUltimates && cheapest != int.MaxValue && cheapest > spendCapWhileSaving)
            Warn($"Cheapest attack costs {cheapest} but Spend Cap While Saving is {spendCapWhileSaving} — the boss will stand still while banking for an ultimate. Raise the cap or give one filler attack a lower cost.");
    }

    private void Warn(string msg) => Debug.LogWarning($"[MechAttackSelector] {msg}", this);

    private void Update()
    {
        if (_recoveryTimer > 0f) _recoveryTimer -= Time.deltaTime;

        if (_brain.State != MechBossState.Idle) return;
        if (PlayerHealth.Transform == null) return;
        if (_recoveryTimer > 0f) return;

        AttackDirective directive = ResolveDirective();
        float dist = Vector3.Distance(transform.position, PlayerHealth.Transform.position);

        RebuildReplacements();
        _spendCap = ResolveSpendCap(dist);

        MechAttackBehaviour chosen = PickFromRotation(directive, dist);
        if (chosen == null && (directive == null || directive.fallbackToRandom)) chosen = PickWeighted(dist);
        if (chosen == null) return;

        // Pay only once the pick is final. Every candidate was already checked as
        // affordable, so this failing means something else drained the pool this
        // frame — drop the turn rather than firing an attack that wasn't paid for.
        if (_tokens != null && !_tokens.TrySpend(chosen.tokenCost)) return;

        RememberPick(chosen);

        // Phase aggression shortens the gap between attacks too, not just each
        // attack's own cooldown — otherwise phase 3 still paces like phase 1.
        float recovery = (directive != null ? directive.recoveryBetweenAttacks : defaultRecovery)
                         * _brain.CooldownMultiplier;

        _brain.NotifyAttackStart();
        chosen.BeginCooldown();
        Log($"Executing {chosen.GetType().Name}" + (directive != null ? $" [{directive.label}]" : " [random]")
            + (_tokens != null ? $" (-{chosen.tokenCost} tokens)" : ""));

        chosen.Execute(() =>
        {
            _brain.NotifyAttackEnd();
            _recoveryTimer = recovery;
        });
    }

    /// <summary>Highest-numbered directive whose phase the boss has reached.</summary>
    private AttackDirective ResolveDirective()
    {
        if (directives == null || directives.Length == 0) return null;

        AttackDirective best = null;
        foreach (var d in directives)
        {
            if (d == null || d.phase > _brain.Phase) continue;
            if (best == null || d.phase > best.phase) best = d;
        }

        // Starting a new phase's plan mid-rotation would drop the player into the
        // middle of a sequence they haven't seen the opening of.
        if (best != _activeDirective)
        {
            _activeDirective = best;
            _rotationIndex = 0;
        }

        return best;
    }

    #region Tokens

    /// <summary>Max token cost the boss is willing to spend this tick. Normally
    /// everything it can afford; while an ultimate is one bank away, only the
    /// cheap stuff.</summary>
    private int ResolveSpendCap(float dist)
    {
        if (_tokens == null || !bankForUltimates) return int.MaxValue;
        return IsSavingUp(dist) ? spendCapWhileSaving : int.MaxValue;
    }

    /// <summary>True when some ultimate is ready in every way except the tokens.
    /// If it's on cooldown or out of range there's nothing to save for — the boss
    /// should be spending freely instead.</summary>
    private bool IsSavingUp(float dist)
    {
        foreach (var a in _attacks)
        {
            if (a.tokenCost < ultimateCostThreshold) continue;
            if (IsRetired(a)) continue;
            if (!a.IsAvailable(dist)) continue;
            if (_tokens.CanAfford(a.tokenCost)) continue;
            return true;
        }
        return false;
    }

    private bool CanUse(MechAttackBehaviour attack, float dist)
    {
        if (attack == null || !attack.IsAvailable(dist)) return false;
        if (IsRetired(attack)) return false;
        if (_tokens == null) return true;
        return attack.tokenCost <= _spendCap && _tokens.CanAfford(attack.tokenCost);
    }

    #endregion

    #region Replacements

    /// <summary>Collects the replacements from every directive at or below the
    /// current phase. Rebuilt only when the phase actually changes — this runs
    /// inside the per-frame selection tick.</summary>
    private void RebuildReplacements()
    {
        if (_replacementsBuiltForPhase == _brain.Phase) return;
        _replacementsBuiltForPhase = _brain.Phase;

        _activeReplacements.Clear();
        if (directives == null) return;

        foreach (var d in directives)
        {
            if (d?.replacements == null || d.phase > _brain.Phase) continue;
            foreach (var r in d.replacements)
                if (r != null && r.retired != null) _activeReplacements.Add(r);
        }
    }

    private bool IsRetired(MechAttackBehaviour attack)
    {
        foreach (var r in _activeReplacements)
            if (r.retired == attack) return true;

        return false;
    }

    /// <summary>Maps an attack through this phase's replacements. Chains are
    /// followed (a stomp upgraded twice across two phases lands on the newest
    /// version) with a hard step limit so a mis-authored A->B->A can't hang.</summary>
    private MechAttackBehaviour ResolveReplacement(MechAttackBehaviour attack)
    {
        if (attack == null || _activeReplacements.Count == 0) return attack;

        MechAttackBehaviour current = attack;
        for (int step = 0; step < 4; step++)
        {
            bool mapped = false;
            foreach (var r in _activeReplacements)
            {
                if (r == null || r.retired != current) continue;
                // An entry with no replacement means "retired, nothing takes its
                // place" — the rotation slot goes dead rather than falling back
                // to the move we just decided the boss has outgrown.
                if (r.replacement == null) return null;
                current = r.replacement;
                mapped = true;
                break;
            }
            if (!mapped) return current; // settled: nothing upgrades this one further
        }

        Debug.LogWarning($"[MechAttackSelector] Replacement chain from {attack.GetType().Name} didn't settle in 4 steps — check the directives for a loop.", this);
        return current;
    }

    #endregion

    private MechAttackBehaviour PickFromRotation(AttackDirective directive, float dist)
    {
        if (directive == null || directive.rotation == null || directive.rotation.Length == 0) return null;

        int len = directive.rotation.Length;
        for (int step = 0; step < len; step++)
        {
            int i = (_rotationIndex + step) % len;

            // Resolved before the usability check so a rotation authored for phase 1
            // keeps working after an upgrade — the Stomp slot simply becomes the
            // Earth Spike slot instead of being skipped as retired.
            MechAttackBehaviour candidate = ResolveReplacement(directive.rotation[i]);
            if (!CanUse(candidate, dist)) continue;

            _rotationIndex = (i + 1) % len;
            return candidate;
        }

        return null;
    }

    private MechAttackBehaviour PickWeighted(float dist)
    {
        MechAttackBehaviour pick = PickWeighted(dist, respectRecent: avoidRepeats);
        // Nothing left once recents are excluded — better to repeat a move than
        // to stand there doing nothing.
        if (pick == null && avoidRepeats) pick = PickWeighted(dist, respectRecent: false);
        return pick;
    }

    private MechAttackBehaviour PickWeighted(float dist, bool respectRecent)
    {
        _validBuffer.Clear();
        float totalWeight = 0f;

        foreach (var a in _attacks)
        {
            if (!CanUse(a, dist)) continue;
            if (respectRecent && _recent.Contains(a)) continue;
            _validBuffer.Add(a);
            totalWeight += a.weight;
        }
        if (_validBuffer.Count == 0) return null;

        float roll = UnityEngine.Random.Range(0f, totalWeight);
        float sum = 0f;
        foreach (var a in _validBuffer)
        {
            sum += a.weight;
            if (roll <= sum) return a;
        }
        return _validBuffer[_validBuffer.Count - 1];
    }

    private void RememberPick(MechAttackBehaviour attack)
    {
        _recent.Add(attack);
        while (_recent.Count > Mathf.Max(0, repeatMemory)) _recent.RemoveAt(0);
    }

    [System.Diagnostics.Conditional("UNITY_EDITOR")]
    private void Log(string msg)
    {
        if (showDebugLogs) Debug.Log($"[MechAttackSelector] {msg}", this);
    }
}
