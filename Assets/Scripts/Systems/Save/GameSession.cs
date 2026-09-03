using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// The run currently in progress: which slot it came from, which mission it is, and
/// how long it has been going.
///
/// Everything here has to outlive a scene load, because a run spans the menu, the
/// level, and any reload in between. It creates itself on demand rather than needing
/// to be placed in a scene — the menu and the level both touch it, and neither should
/// have to own it.
///
/// This replaces the free-floating PlayerPrefs keys ("SelectedLevel", "LastMission",
/// "Playtime", "HasSave") that the BIOS menu used to write and nothing ever read back.
/// </summary>
public class GameSession : MonoBehaviour
{
    private static GameSession _instance;

    public static GameSession Instance
    {
        get
        {
            if (_instance != null) return _instance;

            var go = new GameObject("[GameSession]");
            _instance = go.AddComponent<GameSession>();
            DontDestroyOnLoad(go);
            return _instance;
        }
    }

    /// <summary>True when a session exists, without creating one. Use this in teardown
    /// paths (OnDestroy, OnApplicationQuit) so shutdown never resurrects the singleton.</summary>
    public static bool Exists => _instance != null;

    // ── Run state ────────────────────────────────────────────────────────────

    /// <summary>Which slot this run was started or resumed from, for reporting. Note that
    /// checkpoint autosaves deliberately ignore this and always write the auto slot — see
    /// <see cref="SaveSystem.Autosave"/>.</summary>
    public int ActiveSlot { get; private set; } = SaveStorage.AutoSlot;

    /// <summary>Human-readable mission name, shown by the menu on the save entry.</summary>
    public string MissionDisplayName { get; set; } = "";

    /// <summary>Seconds of gameplay in this run, excluding time spent paused.</summary>
    public float Playtime { get; private set; }

    /// <summary>Set while a load is in flight so the level knows to restore a snapshot
    /// on arrival instead of starting fresh. Consumed exactly once.</summary>
    public SaveData PendingRestore { get; private set; }

    private ProfileData _profile;

    /// <summary>Campaign progress, read from disk on first access and held in memory
    /// after that. Call <see cref="SaveProfile"/> after mutating it.</summary>
    public ProfileData Profile => _profile ??= SaveStorage.ReadProfile();

    // ── Lifecycle ────────────────────────────────────────────────────────────

    private void Awake()
    {
        if (_instance != null && _instance != this)
        {
            Destroy(gameObject);
            return;
        }

        _instance = this;
        DontDestroyOnLoad(gameObject);
    }

    private void OnDestroy()
    {
        if (_instance == this) _instance = null;
    }

    // Statics survive Play Mode entry when domain reload is off.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => _instance = null;

    private void Update()
    {
        // unscaledDeltaTime would keep the clock running through the pause menu, and
        // deltaTime is already zero there because pausing sets timeScale to 0 — so
        // plain deltaTime is what makes "time in field" mean time actually played.
        Playtime += Time.deltaTime;
    }

    // ── Run control ──────────────────────────────────────────────────────────

    /// <summary>Starts a fresh run. Clears the clock and any pending restore so a
    /// New Deployment cannot inherit state from the run before it.</summary>
    public void BeginNewRun(string missionDisplayName, int slot = SaveStorage.AutoSlot)
    {
        ActiveSlot         = SaveStorage.IsValidSlot(slot) ? slot : SaveStorage.AutoSlot;
        MissionDisplayName = missionDisplayName ?? "";
        Playtime           = 0f;
        PendingRestore     = null;
    }

    /// <summary>Resumes a run from a save. The snapshot is held until the level asks
    /// for it via <see cref="ConsumePendingRestore"/>.</summary>
    public void BeginLoadedRun(SaveData data, int slot)
    {
        if (data == null) return;

        ActiveSlot         = SaveStorage.IsValidSlot(slot) ? slot : SaveStorage.AutoSlot;
        MissionDisplayName = data.missionDisplayName;
        Playtime           = data.playtimeSeconds;
        PendingRestore     = data;
    }

    /// <summary>Hands over the pending snapshot and clears it, so a later scene reload
    /// (a restart, a death that reloads) does not silently re-apply an old save.</summary>
    public SaveData ConsumePendingRestore()
    {
        SaveData data  = PendingRestore;
        PendingRestore = null;
        return data;
    }

    /// <summary>Ends the run — called when returning to the main menu.</summary>
    public void EndRun()
    {
        Playtime       = 0f;
        PendingRestore = null;
        SaveProfile();
    }

    // ── Profile ──────────────────────────────────────────────────────────────

    public void SaveProfile()
    {
        if (_profile == null) return;
        SaveStorage.WriteProfile(_profile);
    }

    /// <summary>Files a mission completion against the profile and unlocks the next
    /// mission. This is the single entry point for "the player beat a level".</summary>
    public void CompleteMission(string completedScene, float timeSeconds, string rank,
                                string nextSceneToUnlock = null)
    {
        Profile.SubmitResult(completedScene, timeSeconds, rank);
        Profile.Unlock(nextSceneToUnlock);
        Profile.totalPlaytimeSeconds += timeSeconds;

        if (!string.IsNullOrEmpty(rank)) Profile.boardRank = rank;

        SaveProfile();
    }

    /// <summary>Convenience for callers that just want the scene the run is in.</summary>
    public static string CurrentSceneName => SceneManager.GetActiveScene().name;
}
