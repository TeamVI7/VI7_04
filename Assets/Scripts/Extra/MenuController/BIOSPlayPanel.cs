using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using TMPro;

/// <summary>
/// DEPLOY tab of the BIOS terminal: resume the last run, or start a new one from the
/// mission list.
///
/// Reads real save data. Every figure on this panel — whether there is anything to
/// resume, which mission it was, time in field, best times, which missions are
/// unlocked — comes from <see cref="SaveStorage"/> and <see cref="ProfileData"/>.
/// The panel used to read a set of PlayerPrefs keys that nothing in the project ever
/// wrote, which left RESUME DEPLOYMENT permanently unavailable.
/// </summary>
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
    [SerializeField] private Button btnContinue;
    [SerializeField] private Button btnNewGame;

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

    private TextMeshProUGUI[] _allRows;
    private int  _selectedRow     = 0;
    private int  _selectedMission = 0;
    private bool _blockInput;

    /// <summary>The save RESUME DEPLOYMENT will load, or null when there is nothing to
    /// resume. Re-read every time the panel opens so it reflects the run just played.</summary>
    private SaveData _resumeSave;
    private int      _resumeSlot = -1;

    private bool HasSave => _resumeSave != null;

    // ── Static API (call from game scene on level complete) ────────────
    /// <summary>
    /// Call this when the player finishes a level. Files the result against the profile
    /// and unlocks the next mission.
    /// </summary>
    public static void CompleteMission(string completedScene, float time, string rank,
                                       string nextSceneName = null)
    {
        GameSession.Instance.CompleteMission(completedScene, time, rank, nextSceneName);
    }

    // ── Unity ──────────────────────────────────────────────────────────────
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
        RefreshResumeTarget();
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

    /// <summary>Finds the newest save across every slot — the autosave is usually it, but
    /// a manual save made after the last checkpoint should win.</summary>
    private void RefreshResumeTarget()
    {
        _resumeSlot = SaveSystem.MostRecentSlot();
        _resumeSave = _resumeSlot >= 0 ? SaveStorage.ReadSlot(_resumeSlot) : null;

        // A save from an older build cannot be restored, so offering to resume it would
        // just fail at the loading screen.
        if (_resumeSave != null && !_resumeSave.IsCompatible)
        {
            _resumeSave = null;
            _resumeSlot = -1;
        }
    }

    // ── State ──────────────────────────────────────────────────────────────
    void SetState(PanelState state)
    {
        _currentState = state;
        if (state == PanelState.Main) RefreshMainView();
        else                          RefreshMissionView();
    }

    // ── Main ───────────────────────────────────────────────────────────────
    void HandleMainInput()
    {
        if (Input.GetKeyDown(KeyCode.DownArrow) || Input.GetKeyDown(KeyCode.S)) SelectMainRow((_selectedRow + 1) % 2);
        if (Input.GetKeyDown(KeyCode.UpArrow) || Input.GetKeyDown(KeyCode.W))   SelectMainRow((_selectedRow - 1 + 2) % 2);
        if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter))
            ConfirmMainRow();
    }

    void RefreshMainView()
    {
        if (btnContinue != null) btnContinue.interactable = HasSave;
        if (btnNewGame  != null) btnNewGame.interactable  = true;

        lblContinue.text = "RESUME DEPLOYMENT";
        lblNewGame.text  = "NEW DEPLOYMENT";

        lblSaveStatus.text  = HasSave ? "[OPERATIVE DATA FOUND]" : "[NO DATA]";
        lblSaveStatus.color = HasSave ? activeColor : warningColor;

        SetTextRow(lblLastSaved, "Last Mission",  HasSave ? ResumeMissionName() : "---");
        SetTextRow(lblPlaytime,  "Time in Field", HasSave ? SaveSystem.FormatPlaytime(_resumeSave.playtimeSeconds) : "---");
        SetTextRow(lblRank,      "Board Rank",    HasSave ? GameSession.Instance.Profile.boardRank : "---");

        btnContinue?.onClick.RemoveAllListeners();
        btnNewGame?.onClick.RemoveAllListeners();
        btnContinue?.onClick.AddListener(() => { SelectMainRow(0); ConfirmMainRow(); });
        btnNewGame?.onClick.AddListener(()  => { SelectMainRow(1); ConfirmMainRow(); });

        SelectMainRow(_selectedRow);
    }

    /// <summary>The save's own mission name, falling back to whichever configured mission
    /// matches its scene — a save written before the name was set still reads sensibly.</summary>
    private string ResumeMissionName()
    {
        if (!string.IsNullOrEmpty(_resumeSave.missionDisplayName))
            return _resumeSave.missionDisplayName;

        foreach (MissionEntry m in missions)
            if (m.sceneName == _resumeSave.sceneName) return m.displayName;

        return _resumeSave.sceneName;
    }

    void SelectMainRow(int row)
    {
        if (row == 0 && !HasSave) row = 1;
        _selectedRow = row;

        lblContinue.color = (row == 0 && HasSave) ? selectedColor : (HasSave ? normalColor : disabledColor);
        lblNewGame.color  = (row == 1) ? selectedColor : normalColor;

        MenuAudio.Instance?.PlayNavigate();
    }

    void ConfirmMainRow()
    {
        MenuAudio.Instance?.PlayConfirm();

        if (_selectedRow == 0 && HasSave) ResumeSave();
        else                              SetState(PanelState.MissionSelect);
    }

    /// <summary>Loads the resume save. SaveSystem owns the scene transition and hands the
    /// snapshot to the level's CheckpointManager on arrival.</summary>
    void ResumeSave()
    {
        if (SaveSystem.LoadSlot(_resumeSlot)) return;

        // The file was readable a moment ago and isn't now — deleted underneath us, or
        // the disk went away. Fall back to the mission list rather than doing nothing.
        Debug.LogWarning($"[BIOSPlayPanel] Could not resume {SaveStorage.SlotLabel(_resumeSlot)}.");
        RefreshResumeTarget();
        SetState(PanelState.MissionSelect);
    }

    // ── Mission Select ─────────────────────────────────────────────────────
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
        if (btnNewGame  != null) btnNewGame.interactable  = false;
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
                    MissionRecord record = GameSession.Instance.Profile.GetRecord(sel.sceneName);

                    string bestTime = record != null && record.hasBestTime
                                    ? SaveSystem.FormatPlaytime(record.bestTimeSeconds)
                                    : "--:--:--";
                    string bestRank = record != null && !string.IsNullOrEmpty(record.bestRank)
                                    ? record.bestRank
                                    : "---";

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

        // Clears the run clock and any queued snapshot so the level starts genuinely
        // fresh rather than restoring whatever was last loaded.
        GameSession.Instance.BeginNewRun(chosen.displayName);

        // Only the autosave is wiped. Manual slots are the player's own bookmarks and a
        // new deployment has no business deleting them — but leaving the old autosave in
        // place would have the next checkpoint's write racing a stale file that RESUME
        // still points at.
        SaveStorage.DeleteSlot(SaveStorage.AutoSlot);

        SaveSystem.LoadSceneFor(chosen.sceneName);
    }

    // ── Lock / Stat Helpers ────────────────────────────────────────────────
    bool IsMissionUnlocked(MissionEntry m)
        => m.unlockedByDefault || GameSession.Instance.Profile.IsUnlocked(m.sceneName);

    // ── Utilities ──────────────────────────────────────────────────────────
    void SetTextRow(TextMeshProUGUI lbl, string key, string value)
    {
        if (lbl == null) return;
        int pad = Mathf.Max(1, 48 - key.Length - value.Length);
        lbl.text  = key + new string(' ', pad) + value;
        lbl.color = valueColor;
    }
}
