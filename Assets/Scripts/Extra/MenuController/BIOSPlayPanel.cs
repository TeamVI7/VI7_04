using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using TMPro;
using UnityEngine.UIElements;

public class BIOSPlayPanel : MonoBehaviour
{
    [System.Serializable]
    public struct MissionEntry
    {
        public string displayName;       // shown in menu
        public string sceneName;         // exact name in Build Settings
        public bool   unlockedByDefault; // tick for first mission only
    }

    private enum PanelState { Main, MissionSelect }
    private PanelState _currentState = PanelState.Main;

    [Header("Row Buttons (Main Deploy Only)")]
    [SerializeField] private UnityEngine.UI.Button btnContinue;
    [SerializeField] private UnityEngine.UI.Button btnNewGame;

    [Header("Row Labels (Reused for Missions)")]
    [SerializeField] private TextMeshProUGUI lblContinue;
    [SerializeField] private TextMeshProUGUI lblNewGame;
    [SerializeField] private TextMeshProUGUI lblLastSaved;
    [SerializeField] private TextMeshProUGUI lblSaveStatus;
    [SerializeField] private TextMeshProUGUI lblPlaytime;
    [SerializeField] private TextMeshProUGUI lblRank;

    [Header("Missions")]
    [SerializeField] private List<MissionEntry> missions = new List<MissionEntry>
    {
        new MissionEntry { displayName = "Sector 1 — Awakening", sceneName = "Level_01", unlockedByDefault = true  },
        new MissionEntry { displayName = "Sector 2 — Descent",   sceneName = "Level_02", unlockedByDefault = false },
        new MissionEntry { displayName = "Sector 3 — Gridlock",  sceneName = "Level_03", unlockedByDefault = false },
    };

    [Header("Colors")]
    [SerializeField] private Color normalColor   = new Color(0.67f, 0.67f, 0.67f);
    [SerializeField] private Color selectedColor = Color.cyan;
    [SerializeField] private Color lockedColor   = new Color(0.35f, 0.35f, 0.35f);
    [SerializeField] private Color disabledColor = new Color(0.35f, 0.35f, 0.35f);
    [SerializeField] private Color valueColor    = new Color(0.67f, 0.67f, 0.67f);
    [SerializeField] private Color activeColor   = Color.green;
    [SerializeField] private Color warningColor  = new Color(1f, 0.27f, 0.27f);
    [SerializeField] private Color lockedSelColor = new Color(0.5f, 0.5f, 0.5f); // selected but locked

    [Header("Save Keys")]
    [SerializeField] private string saveKey        = "HasSave";
    [SerializeField] private string missionKey     = "LastMission";
    [SerializeField] private string playtimeKey    = "Playtime";
    [SerializeField] private string rankKey        = "BoardRank";
    [SerializeField] private string targetLevelKey = "SelectedLevel";

    // Per-mission stat key prefixes (stored as "BestTime_Level_01", etc.)
    private const string k_BestTime = "BestTime_";
    private const string k_BestRank = "BestRank_";
    private const string k_Unlocked = "Unlocked_";

    private TextMeshProUGUI[] _allRows;
    private int  _selectedRow     = 0;
    private int  _selectedMission = 0;
    private bool _hasSave;
    private bool _blockInput;

    // ── Static API (call from game scene on level complete) ────────────
    /// <summary>
    /// Call this when player finishes a level.
    /// Saves best time/rank for that scene, unlocks the next one.
    /// </summary>
    public static void CompleteMission(string completedScene, float time, string rank, string nextSceneName = null)
    {
        // Save best time (lower is better)
        float existing = PlayerPrefs.GetFloat(k_BestTime + completedScene, float.MaxValue);
        if (time < existing)
        {
            PlayerPrefs.SetFloat(k_BestTime + completedScene, time);
            PlayerPrefs.SetString(k_BestRank + completedScene, rank);
        }

        // Unlock next mission
        if (!string.IsNullOrEmpty(nextSceneName))
            PlayerPrefs.SetInt(k_Unlocked + nextSceneName, 1);

        PlayerPrefs.Save();
    }

    // ── Unity ──────────────────────────────────────────────────────────
    void Awake()
    {
        _allRows = new TextMeshProUGUI[]
        {
            lblContinue, lblNewGame, lblLastSaved,
            lblSaveStatus, lblPlaytime, lblRank
        };
    }

    void OnEnable()
    {
        _hasSave    = PlayerPrefs.GetInt(saveKey, 0) == 1;
        _blockInput = true;
        SetState(PanelState.Main);
    }

    void Update()
    {
        if (!gameObject.activeInHierarchy) return;
        if (_blockInput) { _blockInput = false; return; }

        if (_currentState == PanelState.Main)
            HandleMainInput();
        else
            HandleMissionInput();
    }

    // ── State ──────────────────────────────────────────────────────────
    void SetState(PanelState state)
    {
        _currentState = state;
        if (state == PanelState.Main) RefreshMainView();
        else                          RefreshMissionView();
    }

    // ── Main ───────────────────────────────────────────────────────────
    void HandleMainInput()
    {
        if (Input.GetKeyDown(KeyCode.DownArrow) || Input.GetKeyDown(KeyCode.S)) SelectMainRow((_selectedRow + 1) % 2);
        if (Input.GetKeyDown(KeyCode.UpArrow) || Input.GetKeyDown(KeyCode.W))   SelectMainRow((_selectedRow - 1 + 2) % 2);
        if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter))
            ConfirmMainRow();
    }

    void RefreshMainView()
    {
        if (btnContinue != null) btnContinue.interactable = _hasSave;

        lblContinue.text = "RESUME DEPLOYMENT";
        lblNewGame.text  = "NEW DEPLOYMENT";

        lblSaveStatus.text  = _hasSave ? "[OPERATIVE DATA FOUND]" : "[NO DATA]";
        lblSaveStatus.color = _hasSave ? activeColor : warningColor;

        string mission = PlayerPrefs.GetString(missionKey, "---");
        float  secs    = PlayerPrefs.GetFloat(playtimeKey, 0f);
        string rank    = PlayerPrefs.GetString(rankKey, "PAWN");

        SetTextRow(lblLastSaved, "Last Mission",  _hasSave ? mission          : "---");
        SetTextRow(lblPlaytime,  "Time in Field", _hasSave ? FormatTime(secs) : "---");
        SetTextRow(lblRank,      "Board Rank",    _hasSave ? rank             : "---");

        btnContinue?.onClick.RemoveAllListeners();
        btnNewGame?.onClick.RemoveAllListeners();
        btnContinue?.onClick.AddListener(() => { SelectMainRow(0); ConfirmMainRow(); });
        btnNewGame?.onClick.AddListener(()  => { SelectMainRow(1); ConfirmMainRow(); });

        SelectMainRow(_selectedRow);
    }

    void SelectMainRow(int row)
    {
        if (row == 0 && !_hasSave) row = 1;
        _selectedRow = row;

        lblContinue.color = (row == 0 && _hasSave) ? selectedColor : (_hasSave ? normalColor : disabledColor);
        lblNewGame.color  = (row == 1) ? selectedColor : normalColor;

        MenuAudio.Instance?.PlayNavigate();
    }

    void ConfirmMainRow()
    {
        MenuAudio.Instance?.PlayConfirm();
        if (_selectedRow == 0 && _hasSave)
            BIOSMainMenu.Instance?.OnDeploy();
        else
            SetState(PanelState.MissionSelect);
    }

    // ── Mission Select ─────────────────────────────────────────────────
    void HandleMissionInput()
    {
        if (missions.Count == 0) return;

        if (Input.GetKeyDown(KeyCode.DownArrow) || Input.GetKeyDown(KeyCode.S))
            SelectMission((_selectedMission + 1) % missions.Count);
        if (Input.GetKeyDown(KeyCode.UpArrow) || Input.GetKeyDown(KeyCode.W))
            SelectMission((_selectedMission - 1 + missions.Count) % missions.Count);
        if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter))
            ConfirmMission();
        if (Input.GetKeyDown(KeyCode.Escape))
        {
            MenuAudio.Instance?.PlayNavigate();
            SetState(PanelState.Main);
        }
    }

    void RefreshMissionView()
    {
        if (btnContinue != null) btnContinue.interactable = false;
        if (btnNewGame   != null) btnNewGame.interactable  = false;
        SelectMission(0);
    }

    void SelectMission(int index)
    {
        if (missions.Count == 0) return;
        _selectedMission = index;

        for (int i = 0; i < _allRows.Length; i++)
        {
            if (_allRows[i] == null) continue;

            if (i < missions.Count)
            {
                bool locked   = !IsMissionUnlocked(missions[i]);
                bool selected = (i == _selectedMission);

                _allRows[i].text = locked
                    ? $"[LOCKED]  {missions[i].displayName}"
                    : missions[i].displayName;

                _allRows[i].color = locked
                    ? (selected ? lockedSelColor : lockedColor)
                    : (selected ? selectedColor  : normalColor);
            }
            else if (i == _allRows.Length - 1)
            {
                // Bottom row — stats for currently highlighted mission
                MissionEntry sel    = missions[_selectedMission];
                bool         locked = !IsMissionUnlocked(sel);

                if (locked)
                {
                    _allRows[i].text  = "STATUS: LOCKED  //  COMPLETE PRIOR SECTOR";
                    _allRows[i].color = warningColor;
                }
                else
                {
                    string bestTime = GetBestTime(sel.sceneName);
                    string bestRank = GetBestRank(sel.sceneName);
                    _allRows[i].text  = $"BEST: {bestTime}    RANK: {bestRank}";
                    _allRows[i].color = valueColor;
                }
            }
            else
            {
                _allRows[i].text = "";
            }
        }

        MenuAudio.Instance?.PlayNavigate();
    }

    void ConfirmMission()
    {
        MissionEntry chosen = missions[_selectedMission];

        if (!IsMissionUnlocked(chosen))
        {
            // Deny — play navigate as a "nope" sound (swap for a deny SFX if you have one)
            MenuAudio.Instance?.PlayNavigate();
            return;
        }

        MenuAudio.Instance?.PlayConfirm();

        PlayerPrefs.DeleteKey(saveKey);
        PlayerPrefs.DeleteKey(playtimeKey);
        PlayerPrefs.DeleteKey(rankKey);
        PlayerPrefs.SetString(missionKey,     chosen.displayName);
        PlayerPrefs.SetString(targetLevelKey, chosen.sceneName);
        PlayerPrefs.Save();

        SceneManager.LoadScene(chosen.sceneName);
    }

    // ── Lock / Stat Helpers ────────────────────────────────────────────
    bool IsMissionUnlocked(MissionEntry m)
        => m.unlockedByDefault || PlayerPrefs.GetInt(k_Unlocked + m.sceneName, 0) == 1;

    string GetBestTime(string sceneName)
    {
        float t = PlayerPrefs.GetFloat(k_BestTime + sceneName, -1f);
        return t < 0f ? "--:--:--" : FormatTime(t);
    }

    string GetBestRank(string sceneName)
        => PlayerPrefs.GetString(k_BestRank + sceneName, "---");

    // ── Utilities ──────────────────────────────────────────────────────
    void SetTextRow(TextMeshProUGUI lbl, string key, string value)
    {
        if (lbl == null) return;
        int pad = Mathf.Max(1, 48 - key.Length - value.Length);
        lbl.text  = key + new string(' ', pad) + value;
        lbl.color = valueColor;
    }

    string FormatTime(float s)
    {
        int h = (int)(s / 3600), m = (int)(s % 3600 / 60), sec = (int)(s % 60);
        return $"{h:D2}:{m:D2}:{sec:D2}";
    }
}