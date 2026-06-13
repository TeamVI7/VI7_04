using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Audio;
using TMPro;

/// <summary>
/// BIOSOptionsPanel — fixed:
///   1. Escape restores values (no silent apply-on-exit)
///   2. Enum-keyed ApplyOption (no magic index)
///   3. Const PlayerPrefs keys (no magic strings)
///   4. Two TMP labels per row (name | value) — no fake monospace padding
///   5. AudioMixer SetFloat with dB conversion (no direct AudioSource volume)
/// </summary>
public class BIOSOptionsPanel : MonoBehaviour
{
    // ── Fix 3: Const Keys ──────────────────────────────────────────────
    private const string KEY_MASTER     = "MasterVol";
    private const string KEY_MUSIC      = "MusicVol";
    private const string KEY_SFX        = "SFXVol";
    private const string KEY_RES        = "ResIndex";
    private const string KEY_FULLSCREEN = "Fullscreen";
    private const string KEY_QUALITY    = "Quality";

    // AudioMixer exposed param names — must match your Mixer asset
    private const string MIX_MASTER = "MasterVol";
    private const string MIX_MUSIC  = "MusicVol";
    private const string MIX_SFX    = "SFXVol";

    // ── Fix 2: Enum instead of magic index ─────────────────────────────
    private enum Opt { MasterVol = 0, MusicVol, SFXVol, Resolution, Fullscreen, Quality }

    // ── Fix 4: Two label lists per row ─────────────────────────────────
    [Header("Row Labels — assign Name labels (left) and Value labels (right) top to bottom")]
    [SerializeField] private List<TextMeshProUGUI> rowNameLabels;
    [SerializeField] private List<TextMeshProUGUI> rowValueLabels;

    // ── Fix 5: AudioMixer ──────────────────────────────────────────────
    [Header("Audio")]
    [SerializeField] private AudioMixer audioMixer;

    [Header("Colors")]
    [SerializeField] private Color normalColor   = new Color(0.67f, 0.67f, 0.67f);
    [SerializeField] private Color selectedColor = Color.cyan;

    private class Option
    {
        public string       Label;
        public int          Value;
        public int          Min, Max, Step;
        public List<string> Choices; // null = bar slider
    }

    private List<Option> _options;
    private int[]        _savedValues; // Fix 1: cache for escape restore
    private int          _selectedRow;
    private bool         _blockInput;

    private readonly string[] _resolutions = {
        "1280x720", "1366x768", "1920x1080", "2560x1440"
    };

    // ── Lifecycle ──────────────────────────────────────────────────────
    void OnEnable()
    {
        _blockInput = true;

        _options = new List<Option>
        {
            new Option { Label = "Master Volume", Value = PlayerPrefs.GetInt(KEY_MASTER,      80), Min = 0, Max = 100, Step = 5 },
            new Option { Label = "Music Volume",  Value = PlayerPrefs.GetInt(KEY_MUSIC,       80), Min = 0, Max = 100, Step = 5 },
            new Option { Label = "SFX Volume",    Value = PlayerPrefs.GetInt(KEY_SFX,         80), Min = 0, Max = 100, Step = 5 },
            new Option { Label = "Resolution",    Value = PlayerPrefs.GetInt(KEY_RES,          2), Min = 0, Max = _resolutions.Length - 1, Step = 1, Choices = new List<string>(_resolutions) },
            new Option { Label = "Fullscreen",    Value = PlayerPrefs.GetInt(KEY_FULLSCREEN,   1), Min = 0, Max = 1,   Step = 1, Choices = new List<string> { "OFF", "ON" } },
            new Option { Label = "Quality",       Value = PlayerPrefs.GetInt(KEY_QUALITY,      2), Min = 0, Max = 2,   Step = 1, Choices = new List<string> { "Low", "Medium", "High" } },
        };

        // Fix 1: cache before any live-apply
        _savedValues = new int[_options.Count];
        for (int i = 0; i < _options.Count; i++)
            _savedValues[i] = _options[i].Value;

        _selectedRow = 0;
        RefreshAll();
        Highlight(_selectedRow);

        for (int i = 0; i < _options.Count; i++)
            ApplyOption(i);
    }

    void Update()
    {
        if (!gameObject.activeInHierarchy) return;
        if (_blockInput) { _blockInput = false; return; }

        if (Input.GetKeyDown(KeyCode.DownArrow))
        {
            _selectedRow = (_selectedRow + 1) % _options.Count;
            Highlight(_selectedRow);
            MenuAudio.Instance?.PlayNavigate();
        }
        if (Input.GetKeyDown(KeyCode.UpArrow))
        {
            _selectedRow = (_selectedRow - 1 + _options.Count) % _options.Count;
            Highlight(_selectedRow);
            MenuAudio.Instance?.PlayNavigate();
        }

        if (Input.GetKeyDown(KeyCode.RightArrow) || Input.GetKeyDown(KeyCode.F6))
        {
            ChangeValue(+1);
            MenuAudio.Instance?.PlayNavigate();
        }
        if (Input.GetKeyDown(KeyCode.LeftArrow) || Input.GetKeyDown(KeyCode.F5))
        {
            ChangeValue(-1);
            MenuAudio.Instance?.PlayNavigate();
        }

        if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter))
        {
            SaveAndBack();
            MenuAudio.Instance?.PlayConfirm();
        }

        // Fix 1: escape restores saved values
        if (Input.GetKeyDown(KeyCode.Escape))
        {
            RestoreAndBack();
            MenuAudio.Instance?.PlayNavigate();
        }
    }

    // ── Logic ──────────────────────────────────────────────────────────
    void ChangeValue(int dir)
    {
        var opt = _options[_selectedRow];
        opt.Value = Mathf.Clamp(opt.Value + dir * opt.Step, opt.Min, opt.Max);
        ApplyOption(_selectedRow);
        RefreshRow(_selectedRow);
    }

    // Fix 2: enum switch, Fix 5: AudioMixer dB
    void ApplyOption(int index)
    {
        var opt = _options[index];
        switch ((Opt)index)
        {
            case Opt.MasterVol:
                audioMixer?.SetFloat(MIX_MASTER, LinearToDb(opt.Value));
                break;

            case Opt.MusicVol:
                audioMixer?.SetFloat(MIX_MUSIC, LinearToDb(opt.Value));
                break;

            case Opt.SFXVol:
                audioMixer?.SetFloat(MIX_SFX, LinearToDb(opt.Value));
                break;

            case Opt.Resolution:
            case Opt.Fullscreen:
                ApplyResolution();
                break;

            case Opt.Quality:
                QualitySettings.SetQualityLevel(opt.Value);
                break;
        }
    }

    // Fix 5: proper dB conversion (linear 0-100 → dB)
    float LinearToDb(int value) =>
        value > 0 ? Mathf.Log10(value / 100f) * 20f : -80f;

    // Unified resolution apply — no duplicate code between cases 3 & 4
    void ApplyResolution()
    {
        var parts = _resolutions[_options[(int)Opt.Resolution].Value].Split('x');
        var mode  = _options[(int)Opt.Fullscreen].Value == 1
                    ? FullScreenMode.ExclusiveFullScreen
                    : FullScreenMode.Windowed;
        Screen.SetResolution(int.Parse(parts[0]), int.Parse(parts[1]), mode);
    }

    void SaveAndBack()
    {
        PlayerPrefs.SetInt(KEY_MASTER,     _options[(int)Opt.MasterVol].Value);
        PlayerPrefs.SetInt(KEY_MUSIC,      _options[(int)Opt.MusicVol].Value);
        PlayerPrefs.SetInt(KEY_SFX,        _options[(int)Opt.SFXVol].Value);
        PlayerPrefs.SetInt(KEY_RES,        _options[(int)Opt.Resolution].Value);
        PlayerPrefs.SetInt(KEY_FULLSCREEN, _options[(int)Opt.Fullscreen].Value);
        PlayerPrefs.SetInt(KEY_QUALITY,    _options[(int)Opt.Quality].Value);
        PlayerPrefs.Save();
        BIOSMainMenu.Instance?.SwitchTab(0);
    }

    // Fix 1: restore cached values then exit
    void RestoreAndBack()
    {
        for (int i = 0; i < _options.Count; i++)
        {
            _options[i].Value = _savedValues[i];
            ApplyOption(i);
        }
        BIOSMainMenu.Instance?.SwitchTab(0);
    }

    // ── Display ────────────────────────────────────────────────────────
    void RefreshAll()
    {
        for (int i = 0; i < _options.Count; i++)
            RefreshRow(i);
    }

    // Fix 4: separate name/value labels — no fake padding
    void RefreshRow(int index)
    {
        if (index >= rowNameLabels.Count  || rowNameLabels[index]  == null) return;
        if (index >= rowValueLabels.Count || rowValueLabels[index] == null) return;

        var opt = _options[index];
        rowNameLabels[index].text = opt.Label;

        if (opt.Choices != null)
        {
            rowValueLabels[index].text = "< " + opt.Choices[Mathf.Clamp(opt.Value, 0, opt.Choices.Count - 1)] + " >";
        }
        else
        {
            int filled = Mathf.RoundToInt(opt.Value / (float)opt.Max * 10);
            string bar = "[" + new string('\u2588', filled) + new string('\u2591', 10 - filled) + "]";
            rowValueLabels[index].text = bar + " " + opt.Value;
        }
    }

    void Highlight(int index)
    {
        for (int i = 0; i < rowNameLabels.Count; i++)
        {
            Color c = i == index ? selectedColor : normalColor;
            if (rowNameLabels[i]  != null) rowNameLabels[i].color  = c;
            if (i < rowValueLabels.Count && rowValueLabels[i] != null)
                rowValueLabels[i].color = c;
        }
    }
}