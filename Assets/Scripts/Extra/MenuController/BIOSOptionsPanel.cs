using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Audio;
using TMPro;

public class BIOSOptionsPanel : MonoBehaviour
{
    private const string KEY_MASTER     = "Master";
    private const string KEY_MUSIC      = "Music";
    private const string KEY_SFX        = "SFX";
    private const string KEY_Enemies    = "Enemies";
    private const string KEY_Guns       = "Guns";
    private const string KEY_Dialogue   = "Dialogue";
    private const string KEY_RES        = "ResIndex";
    private const string KEY_FULLSCREEN = "Fullscreen";
    private const string KEY_QUALITY    = "Quality";

    // AudioMixer exposed param names — MUST match names in the Mixer's
    // "Exposed Parameters" list exactly (case-sensitive). Right-click each
    // fader in the mixer -> Expose -> rename in top-left panel.
    private const string MIX_MASTER   = "Master";
    private const string MIX_MUSIC    = "Music";
    private const string MIX_SFX      = "SFX";
    private const string MIX_Enemies  = "Enemies";
    private const string MIX_Guns     = "Guns";
    private const string MIX_Dialogue = "Dialogue";

    private enum Opt { MasterVol = 0, MusicVol, SFXVol, EnemiesVol, GunsVol, DialogueVol, Resolution, Fullscreen, Quality }

    [Header("Row Labels — assign Name labels (left) and Value labels (right) top to bottom")]
    [SerializeField] private List<TextMeshProUGUI> rowNameLabels;
    [SerializeField] private List<TextMeshProUGUI> rowValueLabels;

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
        public List<string> Choices;
    }

    private List<Option> _options;
    private int[]        _savedValues;
    private int          _selectedRow;
    private bool         _blockInput;

    // Fix 3: built from Screen.resolutions at runtime instead of a hardcoded
    // list, so the values on screen always match what the build/monitor
    // actually supports.
    private List<Resolution> _resolutions;
    private List<string>     _resolutionLabels;

    void OnEnable()
    {
        _blockInput = true;

        BuildResolutionList();

        _options = new List<Option>
        {
            new Option { Label = "Master Volume",   Value = PlayerPrefs.GetInt(KEY_MASTER,   80), Min = 0, Max = 100, Step = 5 },
            new Option { Label = "Music Volume",    Value = PlayerPrefs.GetInt(KEY_MUSIC,    80), Min = 0, Max = 100, Step = 5 },
            new Option { Label = "SFX Volume",      Value = PlayerPrefs.GetInt(KEY_SFX,      80), Min = 0, Max = 100, Step = 5 },
            new Option { Label = "Enemies Volume",  Value = PlayerPrefs.GetInt(KEY_Enemies,  80), Min = 0, Max = 100, Step = 5 },
            new Option { Label = "Guns Volume",     Value = PlayerPrefs.GetInt(KEY_Guns,     80), Min = 0, Max = 100, Step = 5 },
            new Option { Label = "Dialogue Volume", Value = PlayerPrefs.GetInt(KEY_Dialogue, 80), Min = 0, Max = 100, Step = 5 },
            new Option { Label = "Resolution",      Value = PlayerPrefs.GetInt(KEY_RES, GetCurrentResolutionIndex()), Min = 0, Max = _resolutions.Count - 1, Step = 1, Choices = _resolutionLabels },
            new Option { Label = "Fullscreen",      Value = PlayerPrefs.GetInt(KEY_FULLSCREEN, 1), Min = 0, Max = 1, Step = 1, Choices = new List<string> { "OFF", "ON" } },
            new Option { Label = "Quality",         Value = PlayerPrefs.GetInt(KEY_QUALITY,  2), Min = 0, Max = 2, Step = 1, Choices = new List<string> { "Low", "Medium", "High" } },
        };

        _savedValues = new int[_options.Count];
        for (int i = 0; i < _options.Count; i++)
            _savedValues[i] = _options[i].Value;

        _selectedRow = 0;
        RefreshAll();
        Highlight(_selectedRow);

        for (int i = 0; i < _options.Count; i++)
            ApplyOption(i);
    }

    // Fix 3: pull real supported resolutions instead of a hardcoded array.
    // Dedupe by width/height (Screen.resolutions can list the same size at
    // multiple refresh rates) and clamp index to whatever the build reports.
    void BuildResolutionList()
    {
        var all = Screen.resolutions;
        _resolutions = new List<Resolution>();
        _resolutionLabels = new List<string>();

        var seen = new HashSet<(int, int)>();
        foreach (var r in all)
        {
            var key = (r.width, r.height);
            if (seen.Contains(key)) continue;
            seen.Add(key);
            _resolutions.Add(r);
            _resolutionLabels.Add($"{r.width}x{r.height}");
        }

        // Fallback if Screen.resolutions comes back empty (can happen on
        // some platforms before the window is created).
        if (_resolutions.Count == 0)
        {
            var cur = Screen.currentResolution;
            _resolutions.Add(cur);
            _resolutionLabels.Add($"{cur.width}x{cur.height}");
        }
    }

    int GetCurrentResolutionIndex()
    {
        int w = Screen.currentResolution.width;
        int h = Screen.currentResolution.height;
        for (int i = 0; i < _resolutions.Count; i++)
            if (_resolutions[i].width == w && _resolutions[i].height == h)
                return i;
        return _resolutions.Count - 1;
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

        if (Input.GetKeyDown(KeyCode.Escape))
        {
            RestoreAndBack();
            MenuAudio.Instance?.PlayNavigate();
        }
    }

    void ChangeValue(int dir)
    {
        var opt = _options[_selectedRow];
        opt.Value = Mathf.Clamp(opt.Value + dir * opt.Step, opt.Min, opt.Max);
        ApplyOption(_selectedRow);
        RefreshRow(_selectedRow);
    }

    // Fix 1: all six mix groups now handled (Enemies/Guns/Dialogue were
    // falling through to default and never reaching SetFloat).
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

            case Opt.EnemiesVol:
                audioMixer?.SetFloat(MIX_Enemies, LinearToDb(opt.Value));
                break;

            case Opt.GunsVol:
                audioMixer?.SetFloat(MIX_Guns, LinearToDb(opt.Value));
                break;

            case Opt.DialogueVol:
                audioMixer?.SetFloat(MIX_Dialogue, LinearToDb(opt.Value));
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

    float LinearToDb(int value) =>
        value > 0 ? Mathf.Log10(value / 100f) * 20f : -80f;

    // Fix 3: FullScreenWindowed instead of ExclusiveFullScreen (exclusive
    // mode is unreliable in built Windows players — causes the "stuck at
    // wrong res" / black-screen-on-alt-tab symptoms), and refresh rate is
    // passed explicitly since Unity 2022.2+ requires a RefreshRate struct
    // or it silently ignores the call in builds.
    void ApplyResolution()
    {
        var res  = _resolutions[_options[(int)Opt.Resolution].Value];
        var mode = _options[(int)Opt.Fullscreen].Value == 1
                    ? FullScreenMode.FullScreenWindow
                    : FullScreenMode.Windowed;

        RefreshRate rate = res.refreshRateRatio.value > 0
            ? res.refreshRateRatio
            : Screen.currentResolution.refreshRateRatio;

        Screen.SetResolution(res.width, res.height, mode, rate);
    }

    void SaveAndBack()
    {
        PlayerPrefs.SetInt(KEY_MASTER,     _options[(int)Opt.MasterVol].Value);
        PlayerPrefs.SetInt(KEY_MUSIC,      _options[(int)Opt.MusicVol].Value);
        PlayerPrefs.SetInt(KEY_SFX,        _options[(int)Opt.SFXVol].Value);
        PlayerPrefs.SetInt(KEY_Enemies,    _options[(int)Opt.EnemiesVol].Value);
        PlayerPrefs.SetInt(KEY_Guns,       _options[(int)Opt.GunsVol].Value);
        PlayerPrefs.SetInt(KEY_Dialogue,   _options[(int)Opt.DialogueVol].Value);
        PlayerPrefs.SetInt(KEY_RES,        _options[(int)Opt.Resolution].Value);
        PlayerPrefs.SetInt(KEY_FULLSCREEN, _options[(int)Opt.Fullscreen].Value);
        PlayerPrefs.SetInt(KEY_QUALITY,    _options[(int)Opt.Quality].Value);
        PlayerPrefs.Save();
        BIOSMainMenu.Instance?.SwitchTab(0);
    }

    void RestoreAndBack()
    {
        for (int i = 0; i < _options.Count; i++)
        {
            _options[i].Value = _savedValues[i];
            ApplyOption(i);
        }
        BIOSMainMenu.Instance?.SwitchTab(0);
    }

    void RefreshAll()
    {
        for (int i = 0; i < _options.Count; i++)
            RefreshRow(i);
    }

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