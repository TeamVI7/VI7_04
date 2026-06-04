using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// BIOSDeployPanel — "DEPLOY" tab.
///
/// RESUME DEPLOYMENT          [OPERATIVE DATA FOUND]
/// NEW DEPLOYMENT
/// ─────────────────────────────────────────────────
/// Last Mission               Sector 4 — Gridlock
/// Time in Field              04:23:11
/// Board Rank                 PAWN
/// </summary>
public class BIOSPlayPanel : MonoBehaviour
{
    [Header("Row Buttons")]
    [SerializeField] private Button btnContinue;
    [SerializeField] private Button btnNewGame;

    [Header("Row Labels")]
    [SerializeField] private TextMeshProUGUI lblContinue;
    [SerializeField] private TextMeshProUGUI lblNewGame;
    [SerializeField] private TextMeshProUGUI lblSaveStatus;
    [SerializeField] private TextMeshProUGUI lblLastSaved;
    [SerializeField] private TextMeshProUGUI lblPlaytime;
    [SerializeField] private TextMeshProUGUI lblRank;

    [Header("Colors")]
    [SerializeField] private Color normalColor   = new Color(0.67f, 0.67f, 0.67f);
    [SerializeField] private Color selectedColor = Color.cyan;
    [SerializeField] private Color disabledColor = new Color(0.35f, 0.35f, 0.35f);
    [SerializeField] private Color valueColor    = new Color(0.67f, 0.67f, 0.67f);
    [SerializeField] private Color activeColor   = Color.green;
    [SerializeField] private Color warningColor  = new Color(1f, 0.27f, 0.27f);

    [Header("Save Keys")]
    [SerializeField] private string saveKey     = "HasSave";
    [SerializeField] private string missionKey  = "LastMission";
    [SerializeField] private string playtimeKey = "Playtime";
    [SerializeField] private string rankKey     = "BoardRank";

    private int  _selectedRow = 0;
    private bool _hasSave;
    private bool _blockInput = false;

    void OnEnable()
    {
        _hasSave    = PlayerPrefs.GetInt(saveKey, 0) == 1;
        _blockInput = true;
        RefreshRows();
        SelectRow(0);
    }

    void Update()
    {
        if (!gameObject.activeInHierarchy) return;
        if (_blockInput) { _blockInput = false; return; }

        if (Input.GetKeyDown(KeyCode.DownArrow)) SelectRow((_selectedRow + 1) % 2);
        if (Input.GetKeyDown(KeyCode.UpArrow))   SelectRow((_selectedRow - 1 + 2) % 2);

        if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter))
            ConfirmRow();
    }

    void RefreshRows()
    {
        // Continue / Resume
        if (btnContinue != null)  btnContinue.interactable = _hasSave;
        if (lblContinue != null)
        {
            lblContinue.text  = "RESUME DEPLOYMENT";
            lblContinue.color = _hasSave ? normalColor : disabledColor;
        }
        if (lblNewGame != null)
        {
            lblNewGame.text  = "NEW DEPLOYMENT";
            lblNewGame.color = normalColor;
        }
        if (lblSaveStatus != null)
        {
            lblSaveStatus.text  = _hasSave ? "[OPERATIVE DATA FOUND]" : "[NO DATA]";
            lblSaveStatus.color = _hasSave ? activeColor : warningColor;
        }

        // Info rows
        string mission  = PlayerPrefs.GetString(missionKey, "---");
        float  secs     = PlayerPrefs.GetFloat(playtimeKey, 0f);
        string rank     = PlayerPrefs.GetString(rankKey, "PAWN");

        SetRow(lblLastSaved, "Last Mission",  _hasSave ? mission        : "---");
        SetRow(lblPlaytime,  "Time in Field", _hasSave ? FormatTime(secs): "---");
        SetRow(lblRank,      "Board Rank",    _hasSave ? rank            : "---");

        // Wire buttons
        btnContinue?.onClick.RemoveAllListeners();
        btnNewGame?.onClick.RemoveAllListeners();
        btnContinue?.onClick.AddListener(() => { SelectRow(0); ConfirmRow(); });
        btnNewGame?.onClick.AddListener(()  => { SelectRow(1); ConfirmRow(); });
    }

    void SelectRow(int row)
    {
        if (row == 0 && !_hasSave) row = 1;
        _selectedRow = row;

        if (lblContinue != null)
            lblContinue.color = (row == 0 && _hasSave) ? selectedColor : (_hasSave ? normalColor : disabledColor);
        if (lblNewGame != null)
            lblNewGame.color = row == 1 ? selectedColor : normalColor;

        MenuAudio.Instance?.PlayNavigate();
    }

    void ConfirmRow()
    {
        if (_selectedRow == 0 && _hasSave)
        {
            BIOSMainMenu.Instance?.OnDeploy();
        }
        else
        {
            PlayerPrefs.DeleteKey(saveKey);
            PlayerPrefs.DeleteKey(missionKey);
            PlayerPrefs.DeleteKey(playtimeKey);
            PlayerPrefs.DeleteKey(rankKey);
            PlayerPrefs.Save();
            BIOSMainMenu.Instance?.OnDeploy();
        }
    }

    void SetRow(TextMeshProUGUI lbl, string key, string value)
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