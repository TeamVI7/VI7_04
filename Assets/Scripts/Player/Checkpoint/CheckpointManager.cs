using System.Collections;
using UnityEngine;

/// <summary>
/// Owns the checkpoint lifecycle for the level: taking snapshots, rewinding to one on
/// death, and applying a snapshot handed over by a load from the menu.
///
/// A checkpoint is a full <see cref="SaveData"/>, not just a position. Hitting one
/// captures the player, their weapons and the state of every encounter; dying rewinds
/// all of it. That is why death does not reload the scene — the snapshot already
/// describes everything a reload would have reset, and applying it in place skips the
/// loading screen entirely.
///
/// The same snapshot is written to disk as the run's autosave, so the checkpoint the
/// player is standing on is also the point they resume from next session.
/// </summary>
public class CheckpointManager : MonoBehaviour
{
    [Header("Run Identity")]
    [Tooltip("Mission name recorded in saves written in this level, e.g. \"Operation R.A.I\". " +
             "Shown by the BIOS menu as Last Mission.\n\n" +
             "The level declares this itself because a mission spans more than one scene — " +
             "the deploy menu lists Operation R.A.I against Cutscene_Mission_1, but every " +
             "checkpoint is actually written in map1, so the menu has no way to work the name " +
             "back out from the save. Only used when the run did not already start from the " +
             "menu; a name chosen there wins.")]
    [SerializeField] private string missionDisplayName = "";

    [Header("Death")]
    [Tooltip("Extra beat held on a fully black screen after the death fade completes, " +
             "before the world rewinds and fades back in.")]
    [SerializeField] private float holdBlackScreen = 0.5f;

    [Header("Autosave")]
    [Tooltip("Write the run's autosave to disk every time a checkpoint is reached. " +
             "Turn off for a level that should only ever be saved manually.")]
    [SerializeField] private bool autosaveOnCheckpoint = true;

    [Header("Debug")]
    [SerializeField] private bool debugLog = true;

    public static CheckpointManager Instance { get; private set; }

    /// <summary>Raised when a checkpoint is taken, with its id.</summary>
    public static event System.Action<string> OnCheckpointReached;

    /// <summary>Raised once the world has finished rewinding after a death.</summary>
    public static event System.Action OnRespawned;

    /// <summary>The snapshot a death will rewind to, or null if none has been taken.</summary>
    private SaveData _snapshot;

    private Coroutine _respawnRoutine;

    public bool  HasCheckpoint     => _snapshot != null;
    public string CurrentCheckpoint => _snapshot?.checkpointId ?? "";

    // ─────────────────────────────────────────────────────────────────────────
    #region Lifecycle
    // ─────────────────────────────────────────────────────────────────────────

    private void Awake()
    {
        Instance = this;
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    private void OnEnable()  => PlayerHealth.OnDied += HandleDeath;
    private void OnDisable() => PlayerHealth.OnDied -= HandleDeath;

    private IEnumerator Start()
    {
        // Touching Instance creates the session if nothing has yet. Gameplay reached by any
        // route other than the deploy menu — pressing Play on this scene, a direct launch,
        // a cutscene that loads straight in — used to leave no session at all, and the
        // checkpoint that followed wrote a save with no mission name and a playtime frozen
        // at zero. The level is the one place guaranteed to be there for every route.
        GameSession session = GameSession.Instance;

        // The menu sets this when a deployment starts, and that choice wins: it knows which
        // mission the player actually picked. This only fills the gap left by other routes.
        if (string.IsNullOrEmpty(session.MissionDisplayName) && !string.IsNullOrEmpty(missionDisplayName))
            session.MissionDisplayName = missionDisplayName;

        // One frame of grace: the level's own Awake/Start pass has to finish building
        // the player, the weapons and the spawn areas before a snapshot can be poured
        // into them. Applying on the same frame restores ammo into a WeaponsController
        // whose Start() has not run yet, and InitAmmo would overwrite it right after.
        yield return null;

        SaveData pending = session.ConsumePendingRestore();
        if (pending == null) yield break;

        Log($"Applying loaded save (checkpoint '{pending.checkpointId}').");

        SaveSystem.Apply(pending);

        // Resume from this point: the loaded save becomes the checkpoint a death
        // rewinds to, so dying right after loading does not strand the player without one.
        _snapshot = pending;
    }

    #endregion

    // ─────────────────────────────────────────────────────────────────────────
    #region Checkpoints
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>Takes a checkpoint. <paramref name="respawnPos"/> is where a death should
    /// put the player back, which is usually the checkpoint marker rather than wherever
    /// the player was standing when they tripped it.</summary>
    public void SetCheckpoint(string checkpointId, Vector3 respawnPos, Quaternion respawnRot)
    {
        _snapshot = SaveSystem.Capture(checkpointId, respawnPos, respawnRot);

        Log($"Checkpoint '{checkpointId}' captured at {respawnPos}.");
        OnCheckpointReached?.Invoke(checkpointId);

        if (!autosaveOnCheckpoint) return;

        if (!SaveSystem.Autosave(checkpointId))
            Debug.LogWarning("[Checkpoint] Autosave failed — the run continues, but this " +
                             "checkpoint will not be there next session.");
    }

    /// <summary>
    /// Drops the stored checkpoint. Called on a level restart so the next death does not
    /// send the player back into the run they just restarted out of.
    /// </summary>
    public void ClearCheckpoint()
    {
        _snapshot = null;

        if (_respawnRoutine != null)
        {
            StopCoroutine(_respawnRoutine);
            _respawnRoutine = null;
        }
    }

    #endregion

    // ─────────────────────────────────────────────────────────────────────────
    #region Death & Respawn
    // ─────────────────────────────────────────────────────────────────────────

    private void HandleDeath()
    {
        if (_snapshot == null)
        {
            Log("Died with no checkpoint — death stands, no auto-respawn.");
            return;
        }

        if (_respawnRoutine != null) return; // already rewinding

        _respawnRoutine = StartCoroutine(Co_Respawn());
    }

    private IEnumerator Co_Respawn()
    {
        float delay = holdBlackScreen;
        if (DeathCamera.Instance != null) delay += DeathCamera.Instance.DeathToBlackDuration;

        // Unscaled: the death sequence has to play out even if something froze timeScale.
        yield return new WaitForSecondsRealtime(delay);

        Log($"Rewinding to checkpoint '{_snapshot.checkpointId}'.");

        // Order matters. BeginRespawn reattaches the camera and reactivates the player
        // GameObject; the snapshot cannot be poured into a deactivated player, because a
        // disabled Rigidbody ignores position writes and disabled weapons never see theirs.
        DeathCamera.Instance?.BeginRespawn();

        SaveSystem.Apply(_snapshot);

        DeathCamera.Instance?.FinishRespawn();

        _respawnRoutine = null;
        OnRespawned?.Invoke();
    }

    #endregion

    // Deliberately NOT [Conditional("UNITY_EDITOR")]. Checkpoint and save problems show up
    // in a built player far more often than in the editor — a stale build, a scene missing
    // from Build Settings, a file that will not write — and stripping these left Player.log
    // with no record at all of whether a checkpoint fired. The toggle above is the off switch.
    private void Log(string msg)
    {
        if (debugLog) Debug.Log($"[Checkpoint] {msg}", this);
    }
}
