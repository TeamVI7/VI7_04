using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// BIOSPlayPanel — "DEPLOY" tab.
/// Reuses the 6 existing row labels to double as a mission selection menu,
/// completely preserving the VerticalLayoutGroup structure.
/// </summary>
public class BIOSPlayPanel : MonoBehaviour
{
    private enum PanelState { Main, MissionSelect }
    private PanelState _currentState = PanelState.Main;

    [Header("Row Buttons (Main Deploy Only)")]
    [SerializeField] private Button btnContinue;
    [SerializeField] private Button btnNewGame;

    [Header("Row Labels (Reused for Missions)")]
    [SerializeField] private TextMeshProUGUI lblContinue;
    [SerializeField] private TextMeshProUGUI lblNewGame;
    [SerializeField] private TextMeshProUGUI lblLastSaved;
    [SerializeField] private TextMeshProUGUI lblSaveStatus;
    [SerializeField] private TextMeshProUGUI lblPlaytime;
    [SerializeField] private TextMeshProUGUI lblRank;

    [Header("Mission Select")]
    [SerializeField] private List<string> availableMissions = new List<string> { 
        "Sector 1 — Awakening", 
        "Sector 2 — Descent", 
        "Sector 3 — Gridlock" 
    };

    [Header("Colors")]
    [SerializeField] private Color normalColor   = new Color(0.67f, 0.67f, 0.67f);
    [SerializeField] private Color selectedColor = Color.cyan;
    [SerializeField] private Color disabledColor = new Color(0.35f, 0.35f, 0.35f);
    [SerializeField] private Color valueColor    = new Color(0.67f, 0.67f, 0.67f);
    [SerializeField] private Color activeColor   = Color.green;
    [SerializeField] private Color warningColor  = new Color(1f, 0.27f, 0.27f);

    [Header("Save Keys")]
    [SerializeField] private string saveKey         = "HasSave";
    [SerializeField] private string missionKey      = "LastMission";
    [SerializeField] private string playtimeKey     = "Playtime";
    [SerializeField] private string rankKey         = "BoardRank";
    [SerializeField] private string targetLevelKey  = "SelectedLevel";

    private TextMeshProUGUI[] _allRows;
    private int  _selectedRow = 0;
    private int  _selectedMission = 0;
    private bool _hasSave;
    private bool _blockInput = false;

    void Awake()
    {
        // Cache all rows into an array for easy iteration during Mission Select
        _allRows = new TextMeshProUGUI[] { 
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
        else if (_currentState == PanelState.MissionSelect)
            HandleMissionInput();
    }

    // ── State Management ───────────────────────────────────────────────
    void SetState(PanelState state)
    {
        _currentState = state;

        if (state == PanelState.Main)
        {
            RefreshMainView();
        }
        else if (state == PanelState.MissionSelect)
        {
            RefreshMissionView();
        }
    }

    // ── Main Deploy Logic ──────────────────────────────────────────────
    void HandleMainInput()
    {
        if (Input.GetKeyDown(KeyCode.DownArrow)) SelectMainRow((_selectedRow + 1) % 2);
        if (Input.GetKeyDown(KeyCode.UpArrow))   SelectMainRow((_selectedRow - 1 + 2) % 2);

        if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter))
            ConfirmMainRow();
    }

    void RefreshMainView()
    {
        if (btnContinue != null)  btnContinue.interactable = _hasSave;
        
        lblContinue.text = "RESUME DEPLOYMENT";
        lblNewGame.text  = "NEW DEPLOYMENT";

        lblSaveStatus.text  = _hasSave ? "[OPERATIVE DATA FOUND]" : "[NO DATA]";
        lblSaveStatus.color = _hasSave ? activeColor : warningColor;

        string mission  = PlayerPrefs.GetString(missionKey, "---");
        float  secs     = PlayerPrefs.GetFloat(playtimeKey, 0f);
        string rank     = PlayerPrefs.GetString(rankKey, "PAWN");

        SetTextRow(lblLastSaved, "Last Mission",  _hasSave ? mission        : "---");
        SetTextRow(lblPlaytime,  "Time in Field", _hasSave ? FormatTime(secs): "---");
        SetTextRow(lblRank,      "Board Rank",    _hasSave ? rank            : "---");

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
        {
            BIOSMainMenu.Instance?.OnDeploy();
        }
        else
        {
            SetState(PanelState.MissionSelect);
        }
    }

    // ── Mission Select Logic ───────────────────────────────────────────
    void HandleMissionInput()
    {
        if (availableMissions.Count == 0) return;

        if (Input.GetKeyDown(KeyCode.DownArrow)) SelectMission((_selectedMission + 1) % availableMissions.Count);
        if (Input.GetKeyDown(KeyCode.UpArrow))   SelectMission((_selectedMission - 1 + availableMissions.Count) % availableMissions.Count);

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
        // Disable main button interactions while in mission select
        if (btnContinue != null) btnContinue.interactable = false;
        if (btnNewGame != null) btnNewGame.interactable = false;

        SelectMission(0);
    }

    void SelectMission(int index)
    {
        if (availableMissions.Count == 0) return;
        _selectedMission = index;

        // Iterate through all 6 available UI rows
        for (int i = 0; i < _allRows.Length; i++)
        {
            if (_allRows[i] == null) continue;

            if (i < availableMissions.Count)
            {
                // Top rows become mission selections
                _allRows[i].text = availableMissions[i];
                _allRows[i].color = (i == _selectedMission) ? selectedColor : normalColor;
            }
            else if (i == _allRows.Length - 1)
            {
                // Use the very bottom row to display the target details
                _allRows[i].text = $"TARGET: {availableMissions[_selectedMission]}\nSTATUS: READY";
                _allRows[i].color = valueColor;
            }
            else
            {
                // Clear any unused rows in the middle
                _allRows[i].text = "";
            }
        }

        MenuAudio.Instance?.PlayNavigate();
    }

    void ConfirmMission()
    {
        MenuAudio.Instance?.PlayConfirm();
        
        PlayerPrefs.DeleteKey(saveKey);
        PlayerPrefs.DeleteKey(playtimeKey);
        PlayerPrefs.DeleteKey(rankKey);
        
        PlayerPrefs.SetString(missionKey, availableMissions[_selectedMission]);
        PlayerPrefs.SetString(targetLevelKey, availableMissions[_selectedMission]);
        PlayerPrefs.Save();

        BIOSMainMenu.Instance?.OnDeploy();
    }

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