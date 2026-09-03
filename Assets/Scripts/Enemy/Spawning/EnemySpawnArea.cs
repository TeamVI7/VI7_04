using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// A procedural encounter volume. Drop one in a room, point it at an
/// <see cref="EnemySpawnTable"/>, give each wave a threat budget, and it fills
/// itself — no per-enemy authoring.
///
/// Two modes:
///   ArenaWaves     — waves arrive in sequence, the next one triggers once the
///                    previous is mostly dead, encounter ends when all are cleared.
///   OneShotAmbush  — fires a single wave and disarms itself. Same budget/roster
///                    machinery, no clear tracking driving anything.
///
/// Composition is budget-first: each wave buys from the table until it can't
/// afford anything left, so the same authored data produces a different fight
/// each run. Waves can also declare Anchors — specific enemies that always show
/// up (the sniper the fight is built around), optionally at an exact marker.
/// Anchors are paid for out of the same budget, so they crowd out filler rather
/// than stacking on top of it.
///
/// Placement rules live in <see cref="EnemySpawnPlacement"/>.
/// </summary>
[DisallowMultipleComponent]
public class EnemySpawnArea : MonoBehaviour
{
    // ─────────────────────────────────────────────────────────────────────────
    #region Types
    // ─────────────────────────────────────────────────────────────────────────

    // Unity's default inspector doesn't render [Tooltip] on enum members, so these
    // stay as comments — the field-level tooltips below carry the guidance instead.
    public enum EncounterMode
    {
        ArenaWaves,     // waves in sequence, next arrives once the current is mostly dead
        OneShotAmbush,  // a single wave, then the area disarms itself
    }

    public enum ActivationMode
    {
        PlayerTrigger,   // OnTriggerEnter on this GameObject
        PlayerProximity, // distance check against ActivationRadius, no collider needed
        Manual,          // only fires when something else calls Activate()
    }

    [Serializable]
    public class AnchorSpawn
    {
        [Tooltip("Always spawned when this wave starts, regardless of what the budget rolls.")]
        public GameObject Prefab;

        [Tooltip("Optional. Forces this enemy to appear at exactly this transform, skipping the sampling and visibility rules entirely — this is your escape hatch for 'the sniper must be on that balcony'. Leave empty to place it by the normal rules.")]
        public Transform SpawnPoint;

        [Tooltip("Threat cost deducted from the wave budget. Set 0 to make this anchor free, so it doesn't eat into the procedural fill.")]
        [Min(0)] public int Cost = 1;
    }

    [Serializable]
    public class Wave
    {
        public string Label = "Wave";

        [Tooltip("Threat points to spend on this wave. The wave buys from the table until nothing affordable is left, so this is the single dial for 'how hard is this wave'.")]
        [Min(0)] public int Budget = 6;

        [Tooltip("Enemies that always appear in this wave, optionally at a fixed point. Paid out of Budget unless their Cost is 0.")]
        public AnchorSpawn[] Anchors;

        [Tooltip("Seconds to wait after the previous wave clears before this one starts arriving. Breathing room — without it waves run together.")]
        public float DelayBeforeStart = 1.5f;

        [Tooltip("Seconds between individual spawns within this wave, so a wave trickles in rather than popping in as one block.")]
        public float SpawnInterval = 0.4f;

        [Tooltip("Advance once this fraction (or fewer) of the enemies alive at the end of this wave's arrival are still standing. 0 = the field must be fully cleared. 0.25 = the next wave starts while 25% remain, which keeps the pressure on. Ignored by OneShotAmbush.")]
        [Range(0f, 1f)] public float RemainingFractionToAdvance = 0f;

        [Tooltip("Safety valve, in seconds: advance anyway if the wave hasn't cleared in this long. Stops one enemy stuck behind geometry from soft-locking the encounter. 0 = no timeout.")]
        [Min(0f)] public float MaxWaveDuration = 0f;
    }

    #endregion

    // ─────────────────────────────────────────────────────────────────────────
    #region Inspector
    // ─────────────────────────────────────────────────────────────────────────

    [Header("Encounter")]
    [Tooltip("ArenaWaves = waves in sequence, each waiting on the previous to be mostly dead.\nOneShotAmbush = fires the first wave only, then disarms. Both report cleared once everything they spawned is dead.")]
    public EncounterMode Mode = EncounterMode.ArenaWaves;

    [Tooltip("The roster this area buys from. Shareable — several areas can pull from one table.")]
    public EnemySpawnTable Table;

    [Tooltip("Waves in order. OneShotAmbush uses only the first entry.")]
    public Wave[] Waves = new Wave[1];

    [Tooltip("Hard ceiling on enemies alive from THIS area at once. A wave that would exceed it holds its remaining spawns until something dies, so a 20-point wave becomes sustained pressure rather than a 20-enemy dogpile.")]
    [Min(1)] public int MaxConcurrentAlive = 8;

    [Header("Activation")]
    [Tooltip("PlayerTrigger = OnTriggerEnter on this GameObject (needs a trigger collider here).\nPlayerProximity = distance check against Activation Radius, no collider needed.\nManual = only fires when another script calls Activate(), for scripted beats and cutscene hand-offs.")]
    public ActivationMode Activation = ActivationMode.PlayerTrigger;

    [Tooltip("Used by PlayerTrigger, and only for the gizmo — the actual detection is OnTriggerEnter on THIS GameObject, so the trigger collider must live here, not on a child. Tick Is Trigger on it.")]
    public Collider TriggerVolume;

    [Tooltip("Used by PlayerProximity.")]
    public float ActivationRadius = 20f;

    [Tooltip("Optional name that runtime-spawned objects can find this area by. A prefab field cannot reference a scene object, so anything instantiated during play — a boss dropped in by a flow controller — has no way to be wired to this area in the inspector, and looks it up by this id instead. Leave empty if nothing needs to find it.")]
    public string ScriptedId;

    [Tooltip("Re-arm after the encounter clears so the area can run again on a later visit. Off means it fires once for the whole session.")]
    public bool CanRetrigger = false;

    [Tooltip("Seconds after clearing before the area can fire again. Only used when Can Retrigger is on.")]
    public float RetriggerCooldown = 30f;

    [Header("Spawn Region")]
    [Tooltip("Box defining where enemies may be sampled. Leave empty to use a sphere of Spawn Radius around this transform. Ignored for anchors that name their own Spawn Point.")]
    public BoxCollider SpawnRegion;

    [Tooltip("Used when Spawn Region is empty.")]
    public float SpawnRadius = 15f;

    [Header("Placement")]
    public EnemySpawnPlacement Placement = new EnemySpawnPlacement();

    [Header("Presentation")]
    [Tooltip("Optional VFX spawned at each enemy's arrival point — a teleport flash, dust puff, drop-pod impact. Sells the arrival instead of a bare pop-in.")]
    public GameObject SpawnVFXPrefab;
    public float SpawnVFXLifetime = 3f;

    [Header("Debug")]
    [SerializeField] private bool showDebugLogs = false;
    [SerializeField] private bool drawGizmos = true;

    #endregion

    // ─────────────────────────────────────────────────────────────────────────
    #region Events
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>Encounter has begun. Hook doors closing, music, an arena lockdown.</summary>
    public event Action OnEncounterStarted;
    /// <summary>Wave index (0-based) has started arriving.</summary>
    public event Action<int> OnWaveStarted;
    /// <summary>Wave index (0-based) met its advance condition.</summary>
    public event Action<int> OnWaveCleared;
    /// <summary>Every wave done and every enemy dead. Hook doors opening, loot, music.</summary>
    public event Action OnEncounterCleared;
    /// <summary>An enemy just arrived — the spawned instance.</summary>
    public event Action<GameObject> OnEnemySpawned;

    public bool IsRunning  { get; private set; }
    public bool IsCleared  { get; private set; }
    public int  WaveIndex  { get; private set; } = -1;
    public int  AliveCount => _alive.Count;

    #endregion

    // ─────────────────────────────────────────────────────────────────────────
    #region Private State
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>A live enemy plus the prefab it came from. The prefab reference is
    /// what MaxConcurrent counts against — matching on name would break the moment
    /// two archetypes share a prefix ("Enemy_Rusher" / "Enemy_RusherHeavy"), since
    /// Unity appends "(Clone)" and leaves the rest intact.</summary>
    private readonly struct LiveEnemy
    {
        public readonly EnemyHealth Health;
        public readonly GameObject  SourcePrefab;

        public LiveEnemy(EnemyHealth health, GameObject sourcePrefab)
        {
            Health       = health;
            SourcePrefab = sourcePrefab;
        }
    }

    private readonly List<LiveEnemy> _alive = new List<LiveEnemy>();
    private Coroutine _encounterRoutine;
    private float     _rearmTime;
    private bool      _hasFired;

    // Scripted waves (ActivateWave) run outside the main encounter sequence and can
    // overlap each other, so they're tracked as a set rather than a single handle —
    // Abort has to be able to stop all of them.
    private readonly List<Coroutine> _scriptedRoutines = new List<Coroutine>();
    private int _scriptedWavesInFlight;

    // Liveness is polled rather than event-driven on purpose. Subscribing to
    // EnemyHealth.OnDied would miss an enemy that gets Destroyed without dying
    // (scene teardown, a despawn, a ragdoll lifetime racing the death sequence),
    // and leave the encounter waiting on a corpse that no longer exists. A 4Hz
    // sweep over a list this small is cheaper than that class of bug.
    private const float LivenessPollInterval = 0.25f;

    #endregion

    // ─────────────────────────────────────────────────────────────────────────
    #region Unity Lifecycle
    // ─────────────────────────────────────────────────────────────────────────

    private void Reset()
    {
        TriggerVolume = GetComponent<Collider>();
        if (TriggerVolume != null) TriggerVolume.isTrigger = true;
    }

    private void OnEnable() => Registry.Add(this);
    private void OnDisable() => Registry.Remove(this);

    private void Update()
    {
        if (Activation != ActivationMode.PlayerProximity) return;
        if (IsRunning || !CanFire()) return;
        if (PlayerHealth.Transform == null) return;

        if (Vector3.Distance(transform.position, PlayerHealth.Transform.position) <= ActivationRadius)
            Activate();
    }

    private void OnTriggerEnter(Collider other)
    {
        if (Activation != ActivationMode.PlayerTrigger) return;
        if (!other.CompareTag("Player")) return;
        if (IsRunning || !CanFire()) return;

        Activate();
    }

    #endregion

    // ─────────────────────────────────────────────────────────────────────────
    #region Public API
    // ─────────────────────────────────────────────────────────────────────────

    // ── Runtime lookup ───────────────────────────────────────────────────────
    //
    // Every enabled area registers itself here so objects created during play can
    // find one. This exists because a prefab field can't hold a scene reference:
    // a boss instantiated mid-level has no inspector link to the area it's meant
    // to pull reinforcements from, and has to resolve it by name at runtime.

    private static readonly List<EnemySpawnArea> Registry = new List<EnemySpawnArea>();

    /// <summary>Every spawn area currently enabled in the scene.</summary>
    public static IReadOnlyList<EnemySpawnArea> All => Registry;

    // Statics survive between play sessions when Enter Play Mode has domain reload
    // turned off, which would leave the registry holding destroyed areas from the
    // last run. Cheaper to clear it than to make every reader defend against that.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetRegistry() => Registry.Clear();

    /// <summary>The enabled area with this <see cref="ScriptedId"/>, or null. Falls
    /// back to matching the GameObject's name, so an area that was never given an
    /// explicit id can still be found by what it's called in the hierarchy.</summary>
    public static EnemySpawnArea Find(string scriptedId)
    {
        if (string.IsNullOrWhiteSpace(scriptedId)) return null;

        foreach (EnemySpawnArea area in Registry)
            if (area != null && string.Equals(area.ScriptedId, scriptedId, StringComparison.OrdinalIgnoreCase))
                return area;

        foreach (EnemySpawnArea area in Registry)
            if (area != null && string.Equals(area.name, scriptedId, StringComparison.OrdinalIgnoreCase))
                return area;

        return null;
    }

    /// <summary>Closest enabled area to a point, within <paramref name="maxDistance"/>.
    /// A last resort for scripted callers — prefer <see cref="Find"/>, since "nearest"
    /// silently picks a different area the moment the level is rearranged.</summary>
    public static EnemySpawnArea FindNearest(Vector3 point, float maxDistance = Mathf.Infinity)
    {
        EnemySpawnArea best = null;
        float bestSqr = maxDistance * maxDistance;

        foreach (EnemySpawnArea area in Registry)
        {
            if (area == null) continue;

            float sqr = (area.transform.position - point).sqrMagnitude;
            if (sqr > bestSqr) continue;

            bestSqr = sqr;
            best = area;
        }

        return best;
    }

    /// <summary>Starts the encounter. Safe to call redundantly — ignored while one
    /// is already running or while the area is spent/cooling down.</summary>
    public void Activate()
    {
        if (IsRunning || !CanFire()) return;

        if (Table == null || Table.Entries == null || Table.Entries.Length == 0)
        {
            Debug.LogWarning($"[EnemySpawnArea] '{name}': no spawn Table assigned (or it's empty) — nothing to spawn.", this);
            return;
        }
        if (Waves == null || Waves.Length == 0)
        {
            Debug.LogWarning($"[EnemySpawnArea] '{name}': no Waves configured — nothing to spawn.", this);
            return;
        }

        _hasFired = true;
        IsRunning = true;
        IsCleared = false;
        _encounterRoutine = StartCoroutine(Co_Encounter());
    }

    /// <summary>Runs ONE wave from <see cref="Waves"/>, right now, whatever the
    /// activation mode says. This is the hook for scripted beats — a boss calling in
    /// reinforcements as it changes phase — where the pacing belongs to the fight
    /// rather than to this area's own wave sequencing.
    ///
    /// Differences from <see cref="Activate"/>, all deliberate:
    /// • It doesn't gate on <see cref="CanFire"/>, so it can be called several times
    ///   over one fight without needing Can Retrigger.
    /// • It doesn't wait for the previous wave to die first. <see cref="MaxConcurrentAlive"/>
    ///   still applies, so a wave called down on top of live stragglers trickles in
    ///   rather than dogpiling.
    /// • It leaves the player-trigger gate alone — an area used for both a scripted
    ///   add-wave and a normal ambush isn't spent by the scripted call.
    /// • Clear reporting (IsCleared / OnEncounterCleared) stays owned by Activate();
    ///   this only raises OnWaveStarted.</summary>
    public void ActivateWave(int waveIndex)
    {
        if (Table == null || Table.Entries == null || Table.Entries.Length == 0)
        {
            Debug.LogWarning($"[EnemySpawnArea] '{name}': ActivateWave({waveIndex}) — no spawn Table assigned (or it's empty).", this);
            return;
        }
        if (Waves == null || waveIndex < 0 || waveIndex >= Waves.Length || Waves[waveIndex] == null)
        {
            Debug.LogWarning($"[EnemySpawnArea] '{name}': ActivateWave({waveIndex}) — no such wave (this area has {Waves?.Length ?? 0}).", this);
            return;
        }

        IsCleared = false;
        _scriptedRoutines.Add(StartCoroutine(Co_ScriptedWave(Waves[waveIndex], waveIndex)));
    }

    /// <summary>Stops the encounter immediately. Spawned enemies are left alone —
    /// pass true to remove them too (used by checkpoint reloads).</summary>
    public void Abort(bool despawnLiveEnemies = false)
    {
        if (_encounterRoutine != null) { StopCoroutine(_encounterRoutine); _encounterRoutine = null; }

        foreach (Coroutine routine in _scriptedRoutines)
            if (routine != null) StopCoroutine(routine);
        _scriptedRoutines.Clear();
        _scriptedWavesInFlight = 0;

        IsRunning = false;

        if (despawnLiveEnemies)
        {
            foreach (LiveEnemy e in _alive)
                if (e.Health != null) Destroy(e.Health.gameObject);
        }
        _alive.Clear();
        Log("Aborted.");
    }

    /// <summary>Makes a spent one-shot area fireable again. For checkpoint restores.</summary>
    public void Rearm()
    {
        _hasFired  = false;
        IsCleared  = false;
        _rearmTime = 0f;
        WaveIndex  = -1;
    }

    // ── Save / restore ───────────────────────────────────────────────────────

    /// <summary>Stable identity for the save file. Prefers <see cref="ScriptedId"/> so a
    /// designer can rename the GameObject without orphaning saved state, and falls back
    /// to the hierarchy path for the areas that never needed a scripted id.</summary>
    public string SaveKey =>
        !string.IsNullOrWhiteSpace(ScriptedId) ? ScriptedId : SaveableId.Resolve(this);

    public SpawnAreaSaveState CaptureSaveState() => new SpawnAreaSaveState
    {
        id        = SaveKey,
        hasFired  = _hasFired,
        cleared   = IsCleared,
        waveIndex = WaveIndex,
    };

    /// <summary>
    /// Rewinds this area to a saved state. Pass null for an area the save does not
    /// mention — it returns to fully untouched, which is right for an area added to the
    /// level after that save was written.
    ///
    /// Live enemies are always destroyed, whatever the target state. Anything currently
    /// on the field belongs to the timeline being discarded: leaving it alive would let
    /// the player rewind to a checkpoint before an ambush and still be shot by it.
    /// </summary>
    public void RestoreSaveState(SpawnAreaSaveState state)
    {
        Abort(despawnLiveEnemies: true);
        Rearm();

        if (state == null) return;

        _hasFired = state.hasFired;
        IsCleared = state.cleared;
        WaveIndex = state.waveIndex;

        // A retriggerable area saved while cleared has to start its cooldown from now,
        // not from whenever it cleared in the discarded timeline — Time.time from the
        // old run means nothing after a scene load.
        _rearmTime = state.cleared && CanRetrigger ? Time.time + RetriggerCooldown : 0f;

        Log($"Restored — hasFired={_hasFired} cleared={IsCleared} wave={WaveIndex}");
    }

    #endregion

    // ─────────────────────────────────────────────────────────────────────────
    #region Encounter Flow
    // ─────────────────────────────────────────────────────────────────────────

    private bool CanFire()
    {
        if (!_hasFired) return true;
        if (!CanRetrigger) return false;
        return Time.time >= _rearmTime;
    }

    private IEnumerator Co_Encounter()
    {
        Log("Encounter started.");
        OnEncounterStarted?.Invoke();

        int waveCount = Mode == EncounterMode.OneShotAmbush ? 1 : Waves.Length;

        for (int i = 0; i < waveCount; i++)
        {
            Wave wave = Waves[i];
            if (wave == null) continue;

            WaveIndex = i;

            if (wave.DelayBeforeStart > 0f)
                yield return new WaitForSeconds(wave.DelayBeforeStart);

            Log($"Wave {i + 1}/{waveCount} — '{wave.Label}' (budget {wave.Budget}).");
            OnWaveStarted?.Invoke(i);

            yield return StartCoroutine(Co_RunWave(wave, i));

            // OneShotAmbush never gates on a clear condition — the player is free to
            // run past a half-killed ambush and the area won't hold anything up.
            if (Mode == EncounterMode.OneShotAmbush) break;

            yield return StartCoroutine(Co_WaitForWaveClear(wave));
            OnWaveCleared?.Invoke(i);
            Log($"Wave {i + 1} cleared.");
        }

        // Both modes drain before reporting cleared, so OnEncounterCleared always
        // means "everything this area spawned is dead" — a door or music cue hung
        // off it can trust that. The mode difference is in *pacing* (whether waves
        // wait on each other), not in what "cleared" means.
        while (true)
        {
            PruneDead();
            if (_alive.Count == 0) break;
            yield return new WaitForSeconds(LivenessPollInterval);
        }

        _encounterRoutine = null;
        // A scripted wave (ActivateWave) can still be trickling in underneath the
        // encounter, and it owns IsRunning until it's done.
        if (_scriptedWavesInFlight == 0) IsRunning = false;
        IsCleared = true;
        _rearmTime = Time.time + RetriggerCooldown;

        Log("Encounter cleared.");
        OnEncounterCleared?.Invoke();
    }

    /// <summary>One wave, run on demand by <see cref="ActivateWave"/>. Kept separate
    /// from Co_Encounter so a scripted wave can't disturb the encounter's own
    /// wave-index bookkeeping or its cleared reporting.</summary>
    private IEnumerator Co_ScriptedWave(Wave wave, int waveIndex)
    {
        _scriptedWavesInFlight++;
        IsRunning = true;

        if (wave.DelayBeforeStart > 0f)
            yield return new WaitForSeconds(wave.DelayBeforeStart);

        Log($"Scripted wave {waveIndex + 1} — '{wave.Label}' (budget {wave.Budget}).");
        OnWaveStarted?.Invoke(waveIndex);

        yield return Co_RunWave(wave, waveIndex);

        // Counted rather than inferred from the coroutine list: scripted waves can
        // overlap, and the main encounter can be running underneath them. IsRunning
        // only drops when nothing at all is still spawning.
        _scriptedWavesInFlight--;
        if (_scriptedWavesInFlight == 0 && _encounterRoutine == null) IsRunning = false;
    }

    private IEnumerator Co_RunWave(Wave wave, int waveIndex)
    {
        List<PendingSpawn> queue = BuildWave(wave, waveIndex + 1);
        if (queue.Count == 0)
        {
            Debug.LogWarning($"[EnemySpawnArea] '{name}': wave '{wave.Label}' resolved to zero enemies — budget {wave.Budget} can't afford anything in '{Table.name}' at wave {waveIndex + 1}.", this);
            yield break;
        }

        foreach (PendingSpawn pending in queue)
        {
            // Hold the rest of the wave back while the field is full. This is what
            // turns an oversized budget into sustained pressure instead of a dogpile.
            while (true)
            {
                PruneDead();
                if (_alive.Count < MaxConcurrentAlive) break;
                yield return new WaitForSeconds(LivenessPollInterval);
            }

            SpawnOne(pending);

            if (wave.SpawnInterval > 0f)
                yield return new WaitForSeconds(wave.SpawnInterval);
        }
    }

    private IEnumerator Co_WaitForWaveClear(Wave wave)
    {
        int spawned = _alive.Count;
        int threshold = Mathf.FloorToInt(spawned * wave.RemainingFractionToAdvance);
        float deadline = wave.MaxWaveDuration > 0f ? Time.time + wave.MaxWaveDuration : float.MaxValue;

        while (true)
        {
            PruneDead();
            if (_alive.Count <= threshold) yield break;

            if (Time.time >= deadline)
            {
                Log($"Wave '{wave.Label}' hit its {wave.MaxWaveDuration}s timeout with {_alive.Count} alive — advancing anyway.");
                yield break;
            }

            yield return new WaitForSeconds(LivenessPollInterval);
        }
    }

    #endregion

    // ─────────────────────────────────────────────────────────────────────────
    #region Composition
    // ─────────────────────────────────────────────────────────────────────────

    private readonly struct PendingSpawn
    {
        public readonly GameObject Prefab;
        public readonly Transform  FixedPoint; // null = place by the normal rules

        public PendingSpawn(GameObject prefab, Transform fixedPoint)
        {
            Prefab     = prefab;
            FixedPoint = fixedPoint;
        }
    }

    /// <summary>Anchors first (they're mandatory and are what defines the fight's
    /// shape), then weighted-random fill with whatever budget survives.</summary>
    private List<PendingSpawn> BuildWave(Wave wave, int waveNumber)
    {
        var queue = new List<PendingSpawn>();
        int remaining = wave.Budget;

        if (wave.Anchors != null)
        {
            foreach (AnchorSpawn anchor in wave.Anchors)
            {
                if (anchor?.Prefab == null) continue;
                queue.Add(new PendingSpawn(anchor.Prefab, anchor.SpawnPoint));
                remaining -= anchor.Cost;
            }
        }

        // Anchors can legitimately overspend — they're mandatory. That just means
        // this wave is entirely authored, with no procedural filler on top.
        int cheapest = Table.CheapestCost(waveNumber);
        int guard = 0;

        while (remaining >= cheapest && guard++ < 256)
        {
            EnemySpawnTable.Entry pick = RollEntry(waveNumber, remaining, queue);
            if (pick == null) break;

            queue.Add(new PendingSpawn(pick.Prefab, null));
            remaining -= pick.Cost;
        }

        return queue;
    }

    /// <summary>Weighted pick over everything affordable, unlocked for this wave,
    /// and not already at its concurrency cap.</summary>
    private EnemySpawnTable.Entry RollEntry(int waveNumber, int budgetLeft, List<PendingSpawn> queued)
    {
        float totalWeight = 0f;
        _rollBuffer.Clear();

        foreach (EnemySpawnTable.Entry e in Table.Entries)
        {
            if (!EnemySpawnTable.IsUsable(e, waveNumber)) continue;
            if (e.Cost > budgetLeft) continue;
            if (e.MaxConcurrent > 0 && CountOf(e.Prefab, queued) >= e.MaxConcurrent) continue;

            _rollBuffer.Add(e);
            totalWeight += e.Weight;
        }

        if (_rollBuffer.Count == 0 || totalWeight <= 0f) return null;

        float roll = UnityEngine.Random.Range(0f, totalWeight);
        float sum  = 0f;
        foreach (EnemySpawnTable.Entry e in _rollBuffer)
        {
            sum += e.Weight;
            if (roll <= sum) return e;
        }
        return _rollBuffer[_rollBuffer.Count - 1];
    }

    private readonly List<EnemySpawnTable.Entry> _rollBuffer = new List<EnemySpawnTable.Entry>();

    /// <summary>How many of this prefab are already committed — both alive on the
    /// field and queued but not yet spawned, so MaxConcurrent holds across the
    /// whole wave rather than only counting what happens to be breathing.</summary>
    private int CountOf(GameObject prefab, List<PendingSpawn> queued)
    {
        int count = 0;
        foreach (PendingSpawn p in queued)
            if (p.Prefab == prefab) count++;

        foreach (LiveEnemy e in _alive)
            if (e.SourcePrefab == prefab) count++;

        return count;
    }

    #endregion

    // ─────────────────────────────────────────────────────────────────────────
    #region Spawning
    // ─────────────────────────────────────────────────────────────────────────

    private void SpawnOne(PendingSpawn pending)
    {
        if (!TryResolveSpawnPosition(pending, out Vector3 position, out Quaternion rotation))
        {
            Debug.LogWarning($"[EnemySpawnArea] '{name}': couldn't find a NavMesh position for '{pending.Prefab.name}' — check that the Spawn Region overlaps baked NavMesh.", this);
            return;
        }

        GameObject instance = Instantiate(pending.Prefab, position, rotation);

        if (instance.TryGetComponent(out EnemyHealth health)) _alive.Add(new LiveEnemy(health, pending.Prefab));
        else Debug.LogWarning($"[EnemySpawnArea] '{name}': '{instance.name}' has no EnemyHealth on its root — it won't count toward wave clearing.", instance);

        if (SpawnVFXPrefab != null)
            Destroy(Instantiate(SpawnVFXPrefab, position, rotation), SpawnVFXLifetime);

        Log($"Spawned {pending.Prefab.name} at {position}.");
        OnEnemySpawned?.Invoke(instance);
    }

    private bool TryResolveSpawnPosition(PendingSpawn pending, out Vector3 position, out Quaternion rotation)
    {
        rotation = Quaternion.identity;

        // An anchor with an explicit point is a deliberate authored placement —
        // it bypasses the visibility and distance rules entirely. That's the whole
        // reason to reach for one.
        if (pending.FixedPoint != null)
        {
            rotation = pending.FixedPoint.rotation;
            if (Placement.TryResolveOnNavMesh(pending.FixedPoint.position, out position)) return true;

            // Fall back to the raw marker position rather than dropping the anchor —
            // a mandatory enemy failing to appear is worse than one placed slightly
            // off the NavMesh, and its agent will snap on its first frame anyway.
            position = pending.FixedPoint.position;
            Debug.LogWarning($"[EnemySpawnArea] '{name}': anchor point '{pending.FixedPoint.name}' isn't on the NavMesh — spawning there regardless.", this);
            return true;
        }

        if (!Placement.TryGetPoint(RandomPointInRegion, out position)) return false;

        // Face the player on arrival so reinforcements read as deliberate rather
        // than arriving with their back turned.
        if (PlayerHealth.Transform != null)
        {
            Vector3 toPlayer = PlayerHealth.Transform.position - position;
            toPlayer.y = 0f;
            if (toPlayer.sqrMagnitude > 0.01f) rotation = Quaternion.LookRotation(toPlayer);
        }

        return true;
    }

    private Vector3 RandomPointInRegion()
    {
        if (SpawnRegion != null)
        {
            Bounds b = SpawnRegion.bounds;
            return new Vector3(
                UnityEngine.Random.Range(b.min.x, b.max.x),
                UnityEngine.Random.Range(b.min.y, b.max.y),
                UnityEngine.Random.Range(b.min.z, b.max.z));
        }

        Vector2 flat = UnityEngine.Random.insideUnitCircle * SpawnRadius;
        return transform.position + new Vector3(flat.x, 0f, flat.y);
    }

    private void PruneDead()
    {
        for (int i = _alive.Count - 1; i >= 0; i--)
        {
            EnemyHealth h = _alive[i].Health;
            if (h == null || !h.IsAlive) _alive.RemoveAt(i);
        }
    }

    #endregion

    // ─────────────────────────────────────────────────────────────────────────
    #region Debug
    // ─────────────────────────────────────────────────────────────────────────

    [System.Diagnostics.Conditional("UNITY_EDITOR")]
    private void Log(string msg)
    {
        if (showDebugLogs) Debug.Log($"[EnemySpawnArea] {name}: {msg}", this);
    }

#if UNITY_EDITOR
    private static readonly Color ColRegion   = new Color(1f, 0.45f, 0f, 0.9f);
    private static readonly Color ColActivate = new Color(0.2f, 0.8f, 1f, 0.9f);
    private static readonly Color ColMarker   = new Color(0.3f, 1f, 0.3f, 1f);
    private static readonly Color ColAnchor   = new Color(1f, 0.2f, 1f, 1f);
    private static readonly Color ColReject   = new Color(1f, 0.25f, 0.2f, 1f);
    private static readonly Color ColSettled  = new Color(1f, 0.85f, 0.1f, 1f);

    // Unselected: just enough to spot the area while working elsewhere in the scene.
    private void OnDrawGizmos()
    {
        if (!drawGizmos) return;

        Gizmos.color = IsRunning ? ColReject : ColRegion;
        Gizmos.DrawWireSphere(transform.position, 0.6f);
        Gizmos.DrawLine(transform.position, transform.position + Vector3.up * 2.5f);

        if (Application.isPlaying && IsRunning) DrawLiveStateLabel();
    }

    private void OnDrawGizmosSelected()
    {
        if (!drawGizmos) return;

        // ── Spawn region ─────────────────────────────────────────────────────
        Gizmos.color = ColRegion;
        if (SpawnRegion != null) Gizmos.DrawWireCube(SpawnRegion.bounds.center, SpawnRegion.bounds.size);
        else                     Gizmos.DrawWireSphere(transform.position, SpawnRadius);

        // ── Activation ───────────────────────────────────────────────────────
        if (Activation == ActivationMode.PlayerProximity)
        {
            Gizmos.color = ColActivate;
            Gizmos.DrawWireSphere(transform.position, ActivationRadius);
        }
        else if (Activation == ActivationMode.PlayerTrigger && TriggerVolume != null)
        {
            Gizmos.color = ColActivate;
            Gizmos.DrawWireCube(TriggerVolume.bounds.center, TriggerVolume.bounds.size);
        }

        // ── Fixed markers, with a line showing which are currently blocked ────
        if (Placement?.SpawnPoints != null)
        {
            foreach (Transform p in Placement.SpawnPoints)
            {
                if (p == null) continue;
                Gizmos.color = ColMarker;
                Gizmos.DrawWireSphere(p.position, 0.5f);
                Gizmos.DrawRay(p.position, p.forward * 1.5f);
            }
        }

        // ── Anchor points — these bypass the placement rules entirely ─────────
        if (Waves != null)
        {
            Gizmos.color = ColAnchor;
            foreach (Wave w in Waves)
            {
                if (w?.Anchors == null) continue;
                foreach (AnchorSpawn a in w.Anchors)
                {
                    if (a?.SpawnPoint == null) continue;
                    Gizmos.DrawWireCube(a.SpawnPoint.position, Vector3.one * 0.8f);
                    Gizmos.DrawRay(a.SpawnPoint.position, Vector3.up * 2f);
                }
            }
        }

        if (!Application.isPlaying) { DrawBudgetPreviewLabel(); return; }

        // ── Runtime: the no-spawn bubble around the player ───────────────────
        if (PlayerHealth.Transform != null)
        {
            Gizmos.color = ColReject;
            Gizmos.DrawWireSphere(PlayerHealth.Transform.position, Placement.MinDistanceFromPlayer);
        }

        // ── Runtime: why the last spawn landed where it did ──────────────────
        // Green = used, red = rejected (visible or too close), yellow = settled
        // for because nothing legal turned up. If you see a cloud of red, the
        // area is too small or too exposed for its Min Distance setting.
        if (Placement?.LastCandidates != null)
        {
            foreach (var c in Placement.LastCandidates)
            {
                Gizmos.color = c.Verdict switch
                {
                    EnemySpawnPlacement.CandidateVerdict.Accepted   => ColMarker,
                    EnemySpawnPlacement.CandidateVerdict.SettledFor => ColSettled,
                    _                                                => ColReject,
                };
                Gizmos.DrawWireSphere(c.Position, 0.4f);
                Gizmos.DrawLine(c.Position, c.Position + Vector3.up * 1.2f);
            }
        }

        // ── Runtime: who's alive and where ───────────────────────────────────
        Gizmos.color = ColActivate;
        foreach (LiveEnemy e in _alive)
        {
            if (e.Health == null) continue;
            Gizmos.DrawLine(transform.position, e.Health.transform.position);
            Gizmos.DrawWireSphere(e.Health.transform.position, 0.4f);
        }

        DrawLiveStateLabel();
    }

    private void DrawLiveStateLabel()
    {
        string wave = WaveIndex >= 0 && Waves != null && WaveIndex < Waves.Length
            ? $"{WaveIndex + 1}/{Waves.Length} '{Waves[WaveIndex].Label}'"
            : "—";

        string state = IsCleared ? "CLEARED" : IsRunning ? "RUNNING" : "idle";

        UnityEditor.Handles.Label(
            transform.position + Vector3.up * 3f,
            $"{name}\n{state}  |  wave {wave}\nalive {_alive.Count}/{MaxConcurrentAlive}");
    }

    // Edit-time sanity read: what each wave will actually cost and roughly how
    // many enemies it buys, so budgets can be tuned without entering play mode.
    private void DrawBudgetPreviewLabel()
    {
        if (Table == null || Waves == null || Waves.Length == 0) return;

        int cheapest = Table.CheapestCost(1);
        if (cheapest == int.MaxValue) return;

        var sb = new System.Text.StringBuilder(name);
        sb.Append('\n').Append(Mode).Append("  |  cheapest unit ").Append(cheapest).Append("pt");

        int waveCount = Mode == EncounterMode.OneShotAmbush ? 1 : Waves.Length;
        for (int i = 0; i < waveCount; i++)
        {
            Wave w = Waves[i];
            if (w == null) continue;

            int anchorCost = 0, anchorCount = 0;
            if (w.Anchors != null)
            {
                foreach (AnchorSpawn a in w.Anchors)
                {
                    if (a?.Prefab == null) continue;
                    anchorCost += a.Cost;
                    anchorCount++;
                }
            }

            int fillBudget = Mathf.Max(0, w.Budget - anchorCost);
            sb.Append($"\n  {i + 1}. '{w.Label}' {w.Budget}pt → {anchorCount} anchored + up to {fillBudget / cheapest} filled");
        }

        UnityEditor.Handles.Label(transform.position + Vector3.up * 3f, sb.ToString());
    }
#endif

    #endregion
}
