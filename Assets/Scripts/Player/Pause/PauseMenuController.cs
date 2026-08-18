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
    private const string MainMenuSceneName = "MainMenu";

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

    [Header("Buttons")]
    public Button resumeButton;
    public Button optionsButton;
    public Button optionsBackButton;
    public Button quitToMainMenuButton;
    public Button quitGameButton;

    private PlayerCam playerCam;

    private void Awake()
    {
        Instance = this;
        playerCam = FindObjectOfType<PlayerCam>();
    }

    /// <summary>
    /// IsPaused is static and gates WeaponsController.IsInputBlocked. Being destroyed while
    /// paused (scene load from a means other than QuitToMainMenu) used to strand it at true —
    /// the next scene would start unable to fire, with timeScale still at 0.
    /// </summary>
    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
        if (!IsPaused) return;

        IsPaused       = false;
        Time.timeScale = 1f;
    }

    private void Start()
    {
        if (pauseCanvas != null) pauseCanvas.SetActive(false);

        resumeButton?.onClick.AddListener(Resume);
        optionsButton?.onClick.AddListener(ShowOptions);
        optionsBackButton?.onClick.AddListener(ShowMain);
        quitToMainMenuButton?.onClick.AddListener(QuitToMainMenu);
        quitGameButton?.onClick.AddListener(QuitGame);

        if (sensitivitySlider != null)
        {
            sensitivitySlider.minValue = minSensitivity;
            sensitivitySlider.maxValue = maxSensitivity;
            sensitivitySlider.value = PlayerPrefs.GetFloat(SensitivityPrefKey, defaultSensitivity);
            sensitivitySlider.onValueChanged.AddListener(SetSensitivity);
        }
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

    public void QuitToMainMenu()
    {
        IsPaused = false;
        Time.timeScale = 1f;
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
