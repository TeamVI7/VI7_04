using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Turns the live scene into a <see cref="SaveData"/> and back again.
///
/// This is the only place that knows which subsystems make up "the game state".
/// Everything else — checkpoints, the pause menu, the BIOS menu — goes through
/// <see cref="Capture"/> / <see cref="Apply"/> and stays ignorant of the details.
///
/// Both directions are defensive by design: a missing PlayerHealth, a weapon list
/// that no longer matches the save, a spawn area that was deleted since the save was
/// written. A save is data from the past, and the scene it is being poured back into
/// is allowed to have moved on. Skipping a piece that no longer applies is always
/// better than throwing halfway through a restore and leaving the player in a
/// half-rewound world.
/// </summary>
public static class SaveSystem
{
    /// <summary>Raised after a snapshot has been fully applied. HUD elements that cache
    /// values (ammo counters, health bars) can refresh off this.</summary>
    public static event Action OnAfterRestore;

    /// <summary>Raised after a save has been written, with the slot it went to.</summary>
    public static event Action<int> OnAfterSave;

    // ─────────────────────────────────────────────────────────────────────────
    // Capture
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>Snapshots the current scene. <paramref name="overridePosition"/> lets a
    /// checkpoint record the respawn point rather than wherever the player happened to
    /// be standing when the trigger fired.</summary>
    public static SaveData Capture(string checkpointId = "",
                                   Vector3? overridePosition = null,
                                   Quaternion? overrideRotation = null)
    {
        // GameSession.Instance, not an Exists check. Guarding with Exists meant that a run
        // which never touched the deploy menu had no session, so every save it wrote got the
        // fallback values — a blank mission name and a playtime of zero — which then showed
        // up in the menu as a save named after its scene with no time on the clock.
        // Capture only ever runs during gameplay, so creating the session here is safe.
        GameSession session = GameSession.Instance;

        var data = new SaveData
        {
            version            = SaveData.CurrentVersion,
            sceneName          = SceneManager.GetActiveScene().name,
            checkpointId       = checkpointId ?? "",
            savedAtUtcTicks    = DateTime.UtcNow.Ticks,
            playtimeSeconds    = session.Playtime,
            missionDisplayName = session.MissionDisplayName,
        };

        CapturePlayer(data.player, overridePosition, overrideRotation);
        CaptureWorld(data.world);

        return data;
    }

    private static void CapturePlayer(PlayerSaveState state, Vector3? overridePos, Quaternion? overrideRot)
    {
        PlayerHealth health = PlayerHealth.Instance;
        if (health != null)
        {
            state.position  = overridePos ?? health.transform.position;
            state.rotation  = overrideRot ?? health.transform.rotation;
            state.health    = health.HP;
            state.maxHealth = health.MaxHP;
        }
        else
        {
            state.position = overridePos ?? Vector3.zero;
            state.rotation = overrideRot ?? Quaternion.identity;
        }

        WeaponSwitcherProcedural switcher = WeaponSwitcherProcedural.Instance;
        if (switcher == null) return;

        state.currentWeaponIndex = switcher.CurrentIndex;

        for (int i = 0; i < switcher.weapons.Count; i++)
        {
            WeaponsController weapon = switcher.weapons[i];
            if (weapon == null) continue;

            state.weapons.Add(new WeaponSaveState
            {
                slotIndex  = i,
                weaponName = weapon.name,
                clip       = weapon.CurrentAmmo,
                reserve    = weapon.ReserveAmmo,
                chambered  = weapon.RoundInChamber,
                unlocked   = switcher.IsUnlocked(i),
            });
        }
    }

    private static void CaptureWorld(WorldSaveState state)
    {
        foreach (EnemySpawnArea area in EnemySpawnArea.All)
        {
            if (area == null) continue;
            state.spawnAreas.Add(area.CaptureSaveState());
        }

        foreach (PickupBase pickup in PickupBase.All)
        {
            if (pickup == null || !pickup.IsCollected) continue;
            state.collectedPickups.Add(pickup.SaveId);
        }

        if (MissionManager.Instance != null)
            state.activeMissions.AddRange(MissionManager.Instance.ActiveMissionIds);

        state.custom = SaveableRegistry.CaptureAll();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Apply
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>Pours a snapshot back into the live scene. Assumes the correct scene is
    /// already loaded — see <see cref="LoadSlot"/> for the version that handles that.</summary>
    public static void Apply(SaveData data)
    {
        if (data == null)
        {
            Debug.LogWarning("[SaveSystem] Apply called with no data.");
            return;
        }

        if (!data.IsCompatible)
        {
            Debug.LogError($"[SaveSystem] Save is version {data.version}, this build reads " +
                           $"version {SaveData.CurrentVersion}. Refusing to restore it.");
            return;
        }

        string activeScene = SceneManager.GetActiveScene().name;
        if (!string.IsNullOrEmpty(data.sceneName) && data.sceneName != activeScene)
        {
            Debug.LogError($"[SaveSystem] Save is for scene '{data.sceneName}' but '{activeScene}' " +
                           "is loaded. Refusing to restore it into the wrong level.");
            return;
        }

        // World first, player second. Restoring the world despawns live enemies and
        // re-arms encounters, and a spawn area re-arming while the player already sits
        // inside its trigger volume would immediately re-fire it — placing the player
        // last means they arrive into a world that has already settled.
        //
        // Every stage is isolated. A restore reaches into a lot of unrelated systems, and
        // several of them raise events that arbitrary scene objects listen to — a single
        // misconfigured listener throwing used to abort Apply partway, which stranded the
        // player unrestored while the world had already rewound. Nothing downstream of a
        // failure is worth losing: a broken objective list is a cosmetic problem, a player
        // who never got teleported is a broken save.
        Debug.Log($"[SaveSystem] Restoring scene '{data.sceneName}' at checkpoint " +
                  $"'{data.checkpointId}' — {data.world.spawnAreas.Count} spawn areas, " +
                  $"{data.player.weapons.Count} weapons.");

        RunStage("world",  () => ApplyWorld(data.world));
        RunStage("player", () => ApplyPlayer(data.player));

        RunStage("post-restore listeners", () => OnAfterRestore?.Invoke());

        Debug.Log("[SaveSystem] Restore complete.");
    }

    private static void RunStage(string stageName, Action stage)
    {
        try
        {
            stage();
        }
        catch (Exception e)
        {
            Debug.LogError($"[SaveSystem] Restore stage '{stageName}' failed and was skipped. " +
                           $"The rest of the restore continued.\n{e}");
        }
    }

    private static void ApplyPlayer(PlayerSaveState state)
    {
        if (state == null) return;

        // Position and health are the two things a restore absolutely must land, so they
        // go first and get their own isolation — a weapon problem below must never cost
        // the player their respawn point.
        PlayerHealth health = PlayerHealth.Instance;
        if (health != null)
        {
            RunStage("player transform", () => TeleportPlayer(health.transform, state.position, state.rotation));
            RunStage("player health",    () => health.RestoreHealth(state.health > 0f ? state.health : health.MaxHP));
        }
        else
        {
            Debug.LogError("[SaveSystem] No PlayerHealth in the scene — the player could not " +
                           "be restored. Is the player spawned by the time the restore runs?");
        }

        WeaponSwitcherProcedural switcher = WeaponSwitcherProcedural.Instance;
        if (switcher == null) return;

        // Three passes, and the order is load-bearing.
        //
        // Unlocks first, because ForceSwitchTo refuses to equip a locked slot and would
        // silently fall back to slot 0.
        foreach (WeaponSaveState w in state.weapons)
        {
            if (w.slotIndex < 0 || w.slotIndex >= switcher.weapons.Count) continue;
            switcher.SetUnlocked(w.slotIndex, w.unlocked);
        }

        // Equip second. This is what activates the weapon GameObject, and activating one
        // for the first time runs its Awake synchronously — which initialises ammo.
        RunStage("equip weapon", () => switcher.ForceSwitchTo(state.currentWeaponIndex));

        // Ammo last, so that Awake has already been and gone and cannot overwrite it.
        foreach (WeaponSaveState w in state.weapons)
        {
            if (w.slotIndex < 0 || w.slotIndex >= switcher.weapons.Count) continue;

            WeaponsController weapon = switcher.weapons[w.slotIndex];
            if (weapon == null) continue;

            WeaponSaveState captured = w;
            RunStage($"ammo for '{weapon.name}'",
                     () => weapon.RestoreAmmo(captured.clip, captured.reserve, captured.chambered));
        }
    }

    private static void ApplyWorld(WorldSaveState state)
    {
        if (state == null) return;

        // Index the saved areas by id so areas that exist now but weren't in the save
        // (a level edited since) can be told apart from ones that simply didn't change.
        var savedAreas = new Dictionary<string, SpawnAreaSaveState>();
        foreach (SpawnAreaSaveState s in state.spawnAreas)
            if (s != null && !string.IsNullOrEmpty(s.id)) savedAreas[s.id] = s;

        // Copied out first: RestoreSaveState destroys live enemies, and an enemy's teardown
        // can disable a spawn area, which mutates EnemySpawnArea.All mid-iteration.
        var areas = new List<EnemySpawnArea>(EnemySpawnArea.All);
        foreach (EnemySpawnArea area in areas)
        {
            if (area == null) continue;

            // Per-area, so one area with a broken wave table cannot leave every later area
            // still armed and full of enemies from the discarded timeline.
            EnemySpawnArea captured = area;
            savedAreas.TryGetValue(captured.SaveKey, out SpawnAreaSaveState saved);
            RunStage($"spawn area '{captured.SaveKey}'", () => captured.RestoreSaveState(saved));
        }

        var collected = new HashSet<string>(state.collectedPickups);
        foreach (PickupBase pickup in new List<PickupBase>(PickupBase.All))
        {
            if (pickup == null) continue;

            PickupBase captured = pickup;
            RunStage($"pickup '{captured.SaveId}'",
                     () => captured.RestoreCollected(collected.Contains(captured.SaveId)));
        }

        RunStage("missions",       () => MissionManager.Instance?.RestoreActiveMissions(state.activeMissions));
        RunStage("custom savables", () => SaveableRegistry.RestoreAll(state.custom));
    }

    /// <summary>
    /// Moves the player without the Rigidbody dragging them back.
    ///
    /// A Rigidbody caches its own position independently of its Transform. Writing
    /// only the Transform gets overwritten by the stale rigidbody pose on the next
    /// physics step — the bug that made early respawns snap back to the death spot.
    /// </summary>
    public static void TeleportPlayer(Transform player, Vector3 position, Quaternion rotation)
    {
        if (player == null) return;

        player.SetPositionAndRotation(position, rotation);

        if (!player.TryGetComponent(out Rigidbody rb)) return;

        rb.position        = position;
        rb.rotation        = rotation;
        rb.linearVelocity  = Vector3.zero;
        rb.angularVelocity = Vector3.zero;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Slots
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>Captures the scene and writes it to a slot. Returns false if the write
    /// failed, so a UI can say so instead of pretending the save happened.</summary>
    public static bool SaveToSlot(int slot, string checkpointId = "")
    {
        SaveData data = Capture(checkpointId);

        if (!SaveStorage.WriteSlot(slot, data)) return false;

        // Plain Debug.Log, not editor-only: these are the breadcrumbs that make a save bug
        // diagnosable from a player's Player.log.
        Debug.Log($"[SaveSystem] Wrote {SaveStorage.SlotLabel(slot)} — scene '{data.sceneName}', " +
                  $"checkpoint '{data.checkpointId}', {FormatPlaytime(data.playtimeSeconds)} played.");

        OnAfterSave?.Invoke(slot);
        return true;
    }

    /// <summary>
    /// Writes the run's autosave. Called at every checkpoint.
    ///
    /// Always the auto slot, never the slot the run was loaded from. Following ActiveSlot
    /// here meant that resuming a manual save turned every later checkpoint into an
    /// overwrite of that manual save — the player's own bookmark quietly destroyed by
    /// walking forward, and the autosave slot left empty so Continue had nothing recent
    /// to offer.
    /// </summary>
    public static bool Autosave(string checkpointId = "") =>
        SaveToSlot(SaveStorage.AutoSlot, checkpointId);

    /// <summary>Reads a slot and starts the run it describes: if its scene is already
    /// the active one the snapshot is applied in place, otherwise the scene is loaded
    /// and <see cref="CheckpointManager"/> applies the snapshot on arrival.
    /// Returns false if the slot is empty or unreadable.</summary>
    public static bool LoadSlot(int slot)
    {
        SaveData data = SaveStorage.ReadSlot(slot);
        if (data == null)
        {
            Debug.LogWarning($"[SaveSystem] {SaveStorage.SlotLabel(slot)} is empty — nothing to load.");
            return false;
        }

        if (!data.IsCompatible)
        {
            Debug.LogError($"[SaveSystem] {SaveStorage.SlotLabel(slot)} was written by an " +
                           $"incompatible version ({data.version}) and cannot be loaded.");
            return false;
        }

        GameSession.Instance.BeginLoadedRun(data, slot);

        if (data.sceneName == SceneManager.GetActiveScene().name)
        {
            Apply(GameSession.Instance.ConsumePendingRestore());
            return true;
        }

        LoadSceneFor(data.sceneName);
        return true;
    }

    /// <summary>Routes a scene load through the BIOS loading screen when one exists,
    /// and falls back to a plain load when it doesn't (editor, bootstrap not run).</summary>
    public static void LoadSceneFor(string sceneName)
    {
        if (string.IsNullOrEmpty(sceneName)) return;

        // A load started from the pause menu inherits timeScale 0 and would hang on the
        // first frame of the loading screen.
        Time.timeScale = 1f;

        LoadingScreenController controller = LoadingScreenController.Instance;
        if (controller == null)
        {
            SceneManager.LoadScene(sceneName);
            return;
        }

        // Reuse the scene's own transition config so a restore warms the same
        // shader collections a normal transition into that scene would. Building
        // a bare SceneLoadStep here instead would land the player in a cold
        // scene that hitches through its first frames of JIT compilation.
        SceneTransitionConfig config = controller.FindConfigFor(sceneName);

        if (config != null)
        {
            controller.BeginLoad(config.BuildSteps("RESTORING OPERATIVE STATE"));
            return;
        }

        Debug.LogWarning($"[SaveSystem] No SceneTransitionConfig registered for '{sceneName}' — " +
                         "restoring without shader warmup, expect first-frame hitching. Add the " +
                         "config to LoadingScreenController.transitionConfigs.");

        controller.BeginLoad(new List<ILoadingStep>
        {
            new SceneLoadStep(sceneName, 1f, "RESTORING OPERATIVE STATE")
        });
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Queries for UI
    // ─────────────────────────────────────────────────────────────────────────

    public static bool HasAnySave()
    {
        foreach (int slot in SaveStorage.AllSlots())
            if (SaveStorage.SlotExists(slot)) return true;

        return false;
    }

    /// <summary>The slot a "Continue" button should resume: the most recently written
    /// one. Returns -1 when there is nothing to continue.</summary>
    public static int MostRecentSlot()
    {
        int  best      = -1;
        long bestTicks = long.MinValue;

        foreach (int slot in SaveStorage.AllSlots())
        {
            SaveData data = SaveStorage.ReadSlot(slot);
            if (data == null || data.savedAtUtcTicks <= bestTicks) continue;

            bestTicks = data.savedAtUtcTicks;
            best      = slot;
        }

        return best;
    }

    /// <summary>Formats a duration the way the BIOS terminal shows it.</summary>
    public static string FormatPlaytime(float seconds)
    {
        if (seconds < 0f) seconds = 0f;

        int h = (int)(seconds / 3600f);
        int m = (int)(seconds % 3600f / 60f);
        int s = (int)(seconds % 60f);

        return $"{h:D2}:{m:D2}:{s:D2}";
    }
}
