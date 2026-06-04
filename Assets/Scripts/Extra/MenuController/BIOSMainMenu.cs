using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// BIOSMainMenu — ROOKIE / The Board operative terminal.
/// Tabs: DEPLOY | CONFIG | DOSSIER | DISCONNECT
/// </summary>
public class BIOSMainMenu : MonoBehaviour
{
    public static BIOSMainMenu Instance { get; private set; }

    [Header("Scene")]
    [SerializeField] private string gameSceneName = "GameScene";

    [Header("Panels")]
    [SerializeField] private GameObject deployPanel;
    [SerializeField] private GameObject configPanel;
    [SerializeField] private GameObject dossierPanel;
    [SerializeField] private GameObject disconnectPanel;

    [Header("Tab Buttons")]
    [SerializeField] private Button tabDeploy;
    [SerializeField] private Button tabConfig;
    [SerializeField] private Button tabDossier;
    [SerializeField] private Button tabDisconnect;

    [Header("Tab Labels")]
    [SerializeField] private TextMeshProUGUI lblDeploy;
    [SerializeField] private TextMeshProUGUI lblConfig;
    [SerializeField] private TextMeshProUGUI lblDossier;
    [SerializeField] private TextMeshProUGUI lblDisconnect;

    [Header("Header Text")]
    [SerializeField] private TextMeshProUGUI lblCorpHeader;   // "ULTRA TECHNOLOGY CORPORATION"
    [SerializeField] private TextMeshProUGUI lblSystemName;   // "VINSON DYNAMICS v4.2.1"
    [SerializeField] private TextMeshProUGUI lblStatusBar;    // "SECTOR 4 — GRIDLOCK ACTIVE"

    [Header("Transition")]
    [SerializeField] private CanvasGroup fadeOverlay;
    [SerializeField] private float fadeDuration = 0.5f;

    [Header("Colors")]
    public Color colNormalText    = new Color(0.67f, 0.67f, 0.67f);
    public Color colTabActiveBg   = new Color(0.67f, 0.67f, 0.67f);
    public Color colTabActiveTxt  = Color.black;
    public Color colTabInactiveTxt= new Color(0.67f, 0.67f, 0.67f);
    public Color colAccent        = Color.cyan;
    public Color colWarning       = Color.red; // red warning

    private int _currentTab = 0;
    public  int CurrentTab => _currentTab;
    private readonly GameObject[]       _panels    = new GameObject[4];
    private readonly TextMeshProUGUI[]  _tabLabels = new TextMeshProUGUI[4];
    private readonly Button[]           _tabButtons= new Button[4];

    // ── Lifecycle ──────────────────────────────────────────────────────
    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    void Start()
    {
        _panels[0]     = deployPanel;      _panels[1]     = configPanel;
        _panels[2]     = dossierPanel;     _panels[3]     = disconnectPanel;
        _tabLabels[0]  = lblDeploy;        _tabLabels[1]  = lblConfig;
        _tabLabels[2]  = lblDossier;       _tabLabels[3]  = lblDisconnect;
        _tabButtons[0] = tabDeploy;        _tabButtons[1] = tabConfig;
        _tabButtons[2] = tabDossier;       _tabButtons[3] = tabDisconnect;

        tabDeploy?.onClick.AddListener(()     => SwitchTab(0));
        tabConfig?.onClick.AddListener(()     => SwitchTab(1));
        tabDossier?.onClick.AddListener(()    => SwitchTab(2));
        tabDisconnect?.onClick.AddListener(() => SwitchTab(3));

        // Header lore text
        if (lblCorpHeader  != null) lblCorpHeader.text  = "ADVANCE TERMINAL - COPYRIGHTED (C) 2016 - 2032,              ULTRA TECHNOLOGY CORPORATION.";


        SwitchTab(0);

        if (fadeOverlay != null) StartCoroutine(FadeIn());
    }

    void Update()
    {
        // TAB key cycles tabs only — arrows belong to panels
        if (Input.GetKeyDown(KeyCode.Tab))
            SwitchTab((_currentTab + 1) % 4);
    }

    // ── Tab Switching ──────────────────────────────────────────────────
    public void SwitchTab(int index)
    {
        _currentTab = index;

        for (int i = 0; i < _panels.Length; i++)
        {
            bool active = i == index;
            if (_panels[i]    != null) _panels[i].SetActive(active);
            if (_tabLabels[i] != null) _tabLabels[i].color = active ? colTabActiveTxt : colTabInactiveTxt;
            if (_tabButtons[i] != null)
            {
                var img = _tabButtons[i].GetComponent<Image>();
                if (img != null) img.color = active ? colTabActiveBg : Color.clear;
            }
        }

        MenuAudio.Instance?.PlayNavigate();
        UnityEngine.EventSystems.EventSystem.current?.SetSelectedGameObject(null);
    }

    // ── Actions ────────────────────────────────────────────────────────
    public void OnDeploy()
    {
        MenuAudio.Instance?.PlayConfirm();
        StartCoroutine(LoadWithFade(gameSceneName));
    }

    public void OnDisconnect()
    {
        MenuAudio.Instance?.PlayConfirm();
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    // ── Fade ───────────────────────────────────────────────────────────
    IEnumerator LoadWithFade(string scene)
    {
        if (fadeOverlay != null) yield return StartCoroutine(FadeOut());
        SceneManager.LoadScene(scene);
    }

    IEnumerator FadeIn()
    {
        fadeOverlay.gameObject.SetActive(true);
        fadeOverlay.alpha = 1f;
        float t = 0f;
        while (t < fadeDuration) { fadeOverlay.alpha = 1f - (t / fadeDuration); t += Time.deltaTime; yield return null; }
        fadeOverlay.alpha = 0f;
        fadeOverlay.gameObject.SetActive(false);
    }

    IEnumerator FadeOut()
    {
        fadeOverlay.gameObject.SetActive(true);
        float t = 0f;
        while (t < fadeDuration) { fadeOverlay.alpha = t / fadeDuration; t += Time.deltaTime; yield return null; }
        fadeOverlay.alpha = 1f;
    }
}