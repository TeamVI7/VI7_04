// MechBossReinforcements.cs
// The mech calls for backup as the fight escalates: reaching a phase fires a wave
// out of one designated EnemySpawnArea.
//
// Everything comes from a single area on purpose — the adds should arrive from the
// same authored place every time (the hangar doors, the gantry) so the player can
// learn where to watch. Which *wave* that area runs is what changes per phase, so
// the phase-2 pair of rushers and the phase-3 squad are authored side by side in
// that one area's wave list.
//
// Drop this on the boss next to MechBossBrain and point Area at the spawn area.
// With no area assigned it does nothing at all.
using System;
using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(MechBossBrain))]
public class MechBossReinforcements : MonoBehaviour
{
    [Serializable]
    public class PhaseWave
    {
        public string label = "Phase 2 — first reinforcements";

        [Tooltip("Phase the boss must ENTER for this to fire. 2 is the first transition (i.e. after phase 1 ends), 3 the second, and so on. 1 never fires — the boss starts there.")]
        [Min(2)] public int phase = 2;

        [Tooltip("Index into the spawn area's Waves list. Each phase normally gets its own entry there, so the reinforcements escalate alongside the boss.")]
        [Min(0)] public int waveIndex = 0;

        [Tooltip("Seconds to wait after the phase transition before the wave is called in. Worth a beat or two — the phase-up animation is the boss's moment, and adds arriving on top of it read as noise. Note the wave's own Delay Before Start is added on top of this.")]
        [Min(0f)] public float delay = 0f;

        [Tooltip("Fire at most once per fight. Turn off only if you want this wave to repeat should the phase somehow be re-entered.")]
        public bool onlyOnce = true;

        [NonSerialized] public bool Fired;
    }

    [Header("Spawn Area")]
    [Tooltip("The one area every wave of reinforcements comes from. LEAVE THIS EMPTY on a boss prefab — a prefab field can't reference a scene object, and this boss is instantiated at runtime by MinigameFlowController, so the slot would always come through null. Use Area Id below instead. Only assign this when the boss is placed in the scene by hand.")]
    public EnemySpawnArea area;

    [Tooltip("How the boss finds its spawn area when Area is empty: the Scripted Id on the EnemySpawnArea (or, failing that, its GameObject name). This is the wiring that survives being spawned from a prefab.")]
    public string areaId = "BossReinforcements";

    [Tooltip("Last resort if no area matches the id: use the nearest spawn area within this radius of the boss. Set to 0 to disable the fallback and fail loudly instead — safer in a level with several encounters, where 'nearest' can silently pick the wrong one.")]
    [Min(0f)] public float nearestAreaFallbackRadius = 0f;

    [Header("Waves")]
    [Tooltip("Which wave to call in at which phase. Entries are matched against the phase the boss just entered.")]
    public PhaseWave[] waves =
    {
        new PhaseWave { label = "Phase 2 — first reinforcements", phase = 2, waveIndex = 0 },
    };

    [Header("On Boss Death")]
    [Tooltip("Stop the spawn area when the boss dies, so a wave still trickling in doesn't keep arriving after the fight is over.")]
    public bool stopSpawningOnDeath = true;

    [Tooltip("Also kill the adds still standing when the boss dies. Off by default — mopping up the last few stragglers is usually a better ending than having them vanish.")]
    public bool despawnAddsOnDeath = false;

    [Header("Debug")]
    [SerializeField] private bool showDebugLogs = true;

    private MechBossBrain _brain;
    private EnemyHealth _health;
    private readonly List<PhaseWave> _pending = new List<PhaseWave>();

    private void Awake()
    {
        _brain = GetComponent<MechBossBrain>();
        _health = GetComponent<EnemyHealth>();

        _brain.OnPhaseChanged += HandlePhaseChanged;
        if (_health != null) _health.OnDied += HandleBossDied;
    }

    // Resolved in Start, not Awake: the boss is instantiated mid-level, and its own
    // Awake runs before any Awake/OnEnable that the same frame's other new objects
    // might need. By Start every enabled area has registered itself.
    private void Start()
    {
        ResolveArea();
        Validate();
    }

    /// <summary>Finds the spawn area this boss pulls from. The inspector reference
    /// is only usable on a hand-placed boss — a prefab can't hold a scene reference,
    /// and this boss is spawned as a clone (MinigameFlowController.SpawnBoss), so
    /// the field arrives null and the id lookup is the real wiring.</summary>
    private EnemySpawnArea ResolveArea()
    {
        if (area != null) return area;

        area = EnemySpawnArea.Find(areaId);
        if (area != null)
        {
            Log($"Resolved spawn area '{area.name}' by id '{areaId}'.");
            return area;
        }

        if (nearestAreaFallbackRadius > 0f)
        {
            area = EnemySpawnArea.FindNearest(transform.position, nearestAreaFallbackRadius);
            if (area != null)
            {
                Debug.LogWarning($"[MechBossReinforcements] No spawn area with id '{areaId}' — fell back to the nearest one, '{area.name}'. " +
                                 "Set its Scripted Id so this link doesn't move when the level is rearranged.", this);
                return area;
            }
        }

        return null;
    }

    private void OnDestroy()
    {
        _brain.OnPhaseChanged -= HandlePhaseChanged;
        if (_health != null) _health.OnDied -= HandleBossDied;
    }

    [System.Diagnostics.Conditional("UNITY_EDITOR")]
    private void Validate()
    {
        if (area == null)
        {
            Debug.LogWarning($"[MechBossReinforcements] Couldn't find a spawn area — the boss will never call in reinforcements. " +
                             $"Nothing in the scene has Scripted Id '{areaId}'" +
                             (EnemySpawnArea.All.Count == 0
                                 ? ", and there are no enabled EnemySpawnAreas at all."
                                 : $". Enabled areas: {string.Join(", ", DescribeAreas())}."), this);
            return;
        }

        if (area.Activation != EnemySpawnArea.ActivationMode.Manual)
        {
            Debug.LogWarning($"[MechBossReinforcements] '{area.name}' is set to {area.Activation}, so the player can trip the whole encounter " +
                             "just by walking into the arena — before the boss has called anything in. Set its Activation to Manual.", area);
        }

        if (waves == null) return;

        int waveCount = area.Waves != null ? area.Waves.Length : 0;
        foreach (PhaseWave entry in waves)
        {
            if (entry == null) continue;

            if (entry.waveIndex >= waveCount)
            {
                Debug.LogWarning($"[MechBossReinforcements] '{entry.label}' asks for wave {entry.waveIndex} but '{area.name}' only has {waveCount} — " +
                                 "that phase will spawn nothing.", this);
            }

            int thresholds = _brain.phaseHealthThresholds != null ? _brain.phaseHealthThresholds.Length : 0;
            if (entry.phase > thresholds + 1)
            {
                Debug.LogWarning($"[MechBossReinforcements] '{entry.label}' fires at phase {entry.phase}, but the boss only reaches phase {thresholds + 1} " +
                                 $"({thresholds} threshold(s) configured) — it will never fire.", this);
            }
        }
    }

    /// <summary>Fires every entry for a phase the boss has just reached. The range
    /// check matters because a single big hit can cross several thresholds at once
    /// (see MechBossBrain.CheckPhaseTransition) — going 1 → 3 in one blow should
    /// still owe the player the phase 2 wave rather than quietly skipping it. The
    /// area's Max Concurrent Alive keeps that from landing as one dogpile.</summary>
    private void HandlePhaseChanged(int previous, int next)
    {
        if (waves == null) return;

        // Retried here as well as in Start: an area that was disabled at boss-spawn
        // time (a room the player hasn't opened yet) isn't in the registry until it
        // switches on, which can easily happen after the boss lands.
        if (ResolveArea() == null)
        {
            Debug.LogWarning($"[MechBossReinforcements] Phase {next} wanted reinforcements but no spawn area with id '{areaId}' exists — nothing spawned.", this);
            return;
        }

        _pending.Clear();
        foreach (PhaseWave entry in waves)
        {
            if (entry == null) continue;
            if (entry.phase <= previous || entry.phase > next) continue;
            if (entry.onlyOnce && entry.Fired) continue;

            entry.Fired = true;
            _pending.Add(entry);
        }

        foreach (PhaseWave entry in _pending)
        {
            if (entry.delay > 0f) StartCoroutine(Co_DelayedCall(entry));
            else CallIn(entry);
        }
    }

    private System.Collections.IEnumerator Co_DelayedCall(PhaseWave entry)
    {
        yield return new WaitForSeconds(entry.delay);

        // The boss can die during the delay. Reinforcements arriving for a fight
        // that's already over is worse than none at all.
        if (_health != null && !_health.IsAlive) yield break;

        CallIn(entry);
    }

    private void CallIn(PhaseWave entry)
    {
        Log($"Phase {_brain.Phase}: calling in '{entry.label}' — wave {entry.waveIndex} of '{area.name}'.");
        area.ActivateWave(entry.waveIndex);
    }

    private void HandleBossDied(Vector3 impulse)
    {
        if (area == null || !stopSpawningOnDeath) return;

        StopAllCoroutines(); // drop any wave still waiting out its delay
        area.Abort(despawnAddsOnDeath);
        Log("Boss died — spawn area stopped.");
    }

    /// <summary>Names the areas that DO exist, so a failed lookup tells you what to
    /// type into Area Id instead of just saying it found nothing.</summary>
    private static IEnumerable<string> DescribeAreas()
    {
        foreach (EnemySpawnArea a in EnemySpawnArea.All)
        {
            if (a == null) continue;
            yield return string.IsNullOrWhiteSpace(a.ScriptedId) ? $"{a.name} (no id)" : $"{a.name} (id '{a.ScriptedId}')";
        }
    }

    [System.Diagnostics.Conditional("UNITY_EDITOR")]
    private void Log(string msg)
    {
        if (showDebugLogs) Debug.Log($"[MechBossReinforcements] {msg}", this);
    }

    private void OnDrawGizmosSelected()
    {
        if (area == null) return;

        // A line to the area it pulls from, so the link is visible in the scene
        // rather than living only in the inspector.
        Gizmos.color = new Color(1f, 0.55f, 0.15f);
        Gizmos.DrawLine(transform.position, area.transform.position);
        Gizmos.DrawWireSphere(area.transform.position, 1f);
        MechGizmos.Label(area.transform.position + Vector3.up * 1.5f, "reinforcements", Gizmos.color);
    }
}
