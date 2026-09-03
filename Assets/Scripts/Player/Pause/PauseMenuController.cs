using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Escape-toggled pause menu. Freezes gameplay via Time.timeScale, disables player
/// control the same way MissionPanelController does (direct Behaviour.enabled toggling,
/// not PlayerActionLock — nothing currently reads from that system).
/// </summary>
public class PauseMenuController : MonoBehaviour
{
    private const string SensitivityPrefKey = "MouseSensitivity";
    private const string MainMenuSceneName = "2_MainMenu";

    public static PauseMenuController Instance { get; private set; }
    public static bool IsPaused { get; private set; }

    [Header("Core References")]
    public GameObject pauseCanvas;
    public GameObject mainPanel;
    public GameObject optionsPanel;

    [Header("Player Control Suppression")]
    public Behaviour[] playerScriptsToDisable;

    [Header("Toggle Input")]
    public KeyCode toggleKey = KeyCode.Escape;

    [Header("Options — Sensitivity")]
    public Slider sensitivitySlider;
    public float minSensitivity = 100f;
    public float maxSensitivity = 1000f;
    public float defaultSensitivity = 400f;

    /// <summary>
    /// The level authors its own player, so a reload always produces a fresh one. Restart
    /// destroys any player that outlived the load rather than leaving a duplicate behind.
    /// </summary>
    [Header("Restart — Persistent Player")]
    [Tooltip("The player root: the object holding PlayerMovement and the Rigidbody. " +
             "Left empty, it is found by PlayerMovement on first use.")]
    public Transform playerRoot;

    [Header("Buttons")]
    public Button resumeButton;
    public Button restartButton;
    public Button optionsButton;
    public Button optionsBackButton;
    public Button quitToMainMenuButton;
    public Button quitGameButton;

    /// <summary>
    /// Manual save/load. Optional — leave unassigned for a checkpoint-only level and the
    /// autosave still runs at every checkpoint.
    /// </summary>
    [Header("Save / Load")]
    public Button saveButton;
    public Button loadButton;

    [Tooltip("Which manual slot the pause menu's Save and Load buttons use. " +
             "1..SaveStorage.ManualSlots — slot 0 is the autosave and is written by " +
             "checkpoints, so pointing this at it would let a manual save overwrite the " +
             "player's checkpoint progress.")]
    [Range(1, SaveStorage.ManualSlots)]
    public int manualSlot = 1;

    [Tooltip("Optional label that reports the result of a save or load — 'SAVED TO SLOT 1', " +
             "'NO DATA IN SLOT 1'.")]
    public TMPro.TextMeshProUGUI saveStatusLabel;

    private PlayerCam playerCam;

    // Static: restart destroys this menu along with the player, so the post-load cleanup is
    // picked up by whichever instance the reloaded scene builds, not by this one.
    private static bool _restartPending;

    private void Awake()
    {
        Instance = this;
        playerCam = FindObjectOfType<PlayerCam>();

        SceneManager.sceneLoaded += HandleSceneLoaded;
    }

    /// <summary>
    /// IsPaused is static and gates WeaponsController.IsInputBlocked. Being destroyed while
    /// paused (scene load from a means other than QuitToMainMenu) used to strand it at true —
    /// the next scene would start unable to fire, with timeScale still at 0.
    /// </summary>
    private void OnDestroy()
    {
        SceneManager.sceneLoaded -= HandleSceneLoaded;

        if (Instance == this) Instance = null;
        if (!IsPaused) return;

        IsPaused       = false;
        Time.timeScale = 1f;
    }

    private void Start()
    {
        if (pauseCanvas != null) pauseCanvas.SetActive(false);

        resumeButton?.onClick.AddListener(Resume);
        restartButton?.onClick.AddListener(RestartLevel);
        optionsButton?.onClick.AddListener(ShowOptions);
        optionsBackButton?.onClick.AddListener(ShowMain);
        quitToMainMenuButton?.onClick.AddListener(QuitToMainMenu);
        quitGameButton?.onClick.AddListener(QuitGame);
        saveButton?.onClick.AddListener(SaveToManualSlot);
        loadButton?.onClick.AddListener(LoadFromManualSlot);

        if (sensitivitySlider != null)
        {
            sensitivitySlider.minValue = minSensitivity;
            sensitivitySlider.maxValue = maxSensitivity;
            sensitivitySlider.value = PlayerPrefs.GetFloat(SensitivityPrefKey, defaultSensitivity);
            sensitivitySlider.onValueChanged.AddListener(SetSensitivity);
        }

        // Covers the case where this menu was made persistent from inside the gameplay
        // scene — sceneLoaded already fired for it and will not fire again.
        StartCoroutine(AfterSceneLoad());
    }

    private void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (mode == LoadSceneMode.Additive) return;
        StartCoroutine(AfterSceneLoad());
    }

    /// <summary>
    /// The reloaded level rebuilds the player, the HUD and the DeathCamera from scratch, but
    /// the global singletons outlive it. PlayerActionLock in particular still holds the Dead
    /// lock if the restart came from a death — the fresh player would spawn unable to shoot.
    /// </summary>
    private IEnumerator AfterSceneLoad()
    {
        yield return null;

        if (!_restartPending) yield break;
        _restartPending = false;

        PlayerActionLock.Instance?.ClearAll();

        // Otherwise the first death after a restart teleports the player back to a
        // checkpoint from the run they just abandoned.
        CheckpointManager.Instance?.ClearCheckpoint();
    }

    private Transform ResolvePlayer()
    {
        if (playerRoot != null) return playerRoot;

        var movement = FindObjectOfType<PlayerMovement>();
        if (movement != null) playerRoot = movement.transform;
        return playerRoot;
    }

    private void Update()
    {
        if (!Input.GetKeyDown(toggleKey)) return;
        if (MissionPanelController.AnyOpen) return;

        if (IsPaused) Resume();
        else           Pause();
    }

    public void Pause()
    {
        IsPaused = true;
        Time.timeScale = 0f;

        ShowMain();
        if (pauseCanvas != null) pauseCanvas.SetActive(true);

        Cursor.lockState = CursorLockMode.None;
        Cursor.visible   = true;

        SetPlayerScriptsEnabled(false);
    }

    public void Resume()
    {
        IsPaused = false;
        Time.timeScale = 1f;

        if (pauseCanvas != null) pauseCanvas.SetActive(false);

        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible   = false;

        SetPlayerScriptsEnabled(true);
    }

    private void ShowMain()
    {
        if (mainPanel != null)    mainPanel.SetActive(true);
        if (optionsPanel != null) optionsPanel.SetActive(false);

        RefreshSaveButtons();
    }

    // ── Save / Load ────────────────────────────────────────────────────────

    private void RefreshSaveButtons()
    {
        if (loadButton != null) loadButton.interactable = SaveStorage.SlotExists(manualSlot);
        SetSaveStatus("");
    }

    /// <summary>Writes the current state to the configured manual slot.</summary>
    public void SaveToManualSlot()
    {
        string label = SaveStorage.SlotLabel(manualSlot);

        bool ok = SaveSystem.SaveToSlot(manualSlot, $"manual:{manualSlot}");
        SetSaveStatus(ok ? $"SAVED TO {label}" : $"SAVE TO {label} FAILED");

        if (loadButton != null) loadButton.interactable = SaveStorage.SlotExists(manualSlot);
    }

    /// <summary>Restores the configured manual slot. Unpauses first — the load either
    /// applies in place (same scene) or starts a scene transition, and both would stall
    /// against a frozen timeScale.</summary>
    public void LoadFromManualSlot()
    {
        if (!SaveStorage.SlotExists(manualSlot))
        {
            SetSaveStatus($"NO DATA IN {SaveStorage.SlotLabel(manualSlot)}");
            return;
        }

        Resume();

        if (!SaveSystem.LoadSlot(manualSlot))
            SetSaveStatus($"{SaveStorage.SlotLabel(manualSlot)} COULD NOT BE READ");
    }

    private void SetSaveStatus(string message)
    {
        if (saveStatusLabel != null) saveStatusLabel.text = message;
    }

    private void ShowOptions()
    {
        if (mainPanel != null)    mainPanel.SetActive(false);
        if (optionsPanel != null) optionsPanel.SetActive(true);
    }

    private void SetSensitivity(float value)
    {
        if (playerCam != null)
        {
            playerCam.sensX = value;
            playerCam.sensY = value;
        }
        PlayerPrefs.SetFloat(SensitivityPrefKey, value);
    }

    public void RestartLevel()
    {
        // Resume() rather than just clearing the flags: if this menu is one of the copies
        // that outlived a previous load, the reload will not hide its canvas and the menu
        // stays up over the fresh level — which is what made Restart look like it did
        // nothing at all.
        Resume();

        _restartPending = true;

        // A restart is a fresh attempt at the mission, so the clock goes back to zero and
        // any snapshot still queued from a load is dropped — otherwise the reloaded scene
        // would immediately restore the save the player just chose to abandon.
        if (GameSession.Exists)
            GameSession.Instance.BeginNewRun(GameSession.Instance.MissionDisplayName,
                                             GameSession.Instance.ActiveSlot);

        // Destroy before the load, not after: the reloaded scene's own singletons run their
        // "destroy the duplicate" guards during Awake, and by then whichever copy is going
        // to survive is already decided. Destroy() is deferred to the end of the frame,
        // ahead of the scene load, so this method keeps running.
        DestroyPersistentPlayer();

        SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
    }

    /// <summary>
    /// The level authors its own player, HUD and pause menu, so anything that survived a
    /// previous load is a duplicate: two cameras fighting over Camera.main, two Escape
    /// listeners, two HUDs drawn on top of each other. Destroy the persistent copies and
    /// let the freshly loaded scene own them.
    /// </summary>
    private void DestroyPersistentPlayer()
    {
        Transform player = ResolvePlayer();
        if (player != null) DestroyIfPersistent(player);

        // This menu is a scene object in the level too — same reasoning.
        DestroyIfPersistent(transform);
    }

    private static void DestroyIfPersistent(Transform t)
    {
        Transform victim = HighestSafeAncestor(t);
        if (victim == null) return;

        Destroy(victim.gameObject);
    }

    /// <summary>
    /// Walks up to the outermost ancestor that is still safe to destroy, or null if the
    /// object is a plain scene object (the reload takes care of those by itself).
    /// </summary>
    private static Transform HighestSafeAncestor(Transform t)
    {
        // buildIndex -1 is the DontDestroyOnLoad scene. Anything else dies with the reload.
        if (t.gameObject.scene.buildIndex != -1) return null;

        Transform safe = t;
        for (Transform p = t.parent; p != null; p = p.parent)
        {
            // Stop below any ancestor carrying a project-wide singleton. Those are meant to
            // live for the whole session, and something being parented under one is exactly
            // how the player ends up persistent by accident — take the player, not the pool.
            if (IsGlobalSingleton(p)) break;
            safe = p;
        }
        return safe;
    }

    private static bool IsGlobalSingleton(Transform t) =>
        t.GetComponent<LoadingScreenController>() != null ||
        t.GetComponent<BulletCasingPool>()        != null ||
        t.GetComponent<ImpactDecalPool>()         != null ||
        t.GetComponent<PlayerActionLock>()        != null ||
        t.GetComponent<PlayerAimThreat>()         != null ||
        // Declared in EnemySFX.cs — the file name and the class name disagree.
        t.GetComponent<SoundManager>()            != null ||
        t.GetComponent<GameSession>()             != null;

    public void QuitToMainMenu()
    {
        Resume();

        Cursor.lockState = CursorLockMode.None;
        Cursor.visible   = true;

        // Flushes the profile (unlocks, records) and ends the run, so the menu reads a
        // settled state rather than one still holding this run's clock.
        if (GameSession.Exists) GameSession.Instance.EndRun();

        SceneManager.LoadScene(MainMenuSceneName);
    }

    public void QuitGame()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    private void SetPlayerScriptsEnabled(bool enabled)
    {
        if (playerScriptsToDisable == null) return;
        foreach (var s in playerScriptsToDisable)
            if (s != null) s.enabled = enabled;
    }
}
