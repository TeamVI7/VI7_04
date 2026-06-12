using System.Collections.Generic;
using UnityEngine;
using TMPro;

/// <summary>
/// BIOSOptionsPanel
/// UP/DOWN   = select row
/// LEFT/RIGHT or F5/F6 = change value
/// ENTER     = confirm & save
/// ESCAPE    = go back without saving
/// </summary>
public class BIOSOptionsPanel : MonoBehaviour
{
    [Header("Row Labels (assign in Inspector, top to bottom)")]
    [SerializeField] private List<TextMeshProUGUI> rowLabels;

    [Header("Audio Sources")]
    [SerializeField] private AudioSource musicAudioSource;
    [SerializeField] private AudioSource sfxAudioSource;

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
    private int _selectedRow = 0;

    private readonly string[] _resolutions = {
        "1280x720", "1366x768", "1920x1080", "2560x1440"
    };

    // ── Lifecycle ──────────────────────────────────────────────────────
    private bool _blockInput = false;

    void OnEnable()
    {
        _blockInput = true;
        _options = new List<Option>
        {
            new Option { Label = "Master Volume", Value = PlayerPrefs.GetInt("MasterVol", 80), Min = 0, Max = 100, Step = 5 },
            new Option { Label = "Music Volume",  Value = PlayerPrefs.GetInt("MusicVol",  80), Min = 0, Max = 100, Step = 5 },
            new Option { Label = "SFX Volume",    Value = PlayerPrefs.GetInt("SFXVol",    80), Min = 0, Max = 100, Step = 5 },
            new Option { Label = "Resolution",    Value = PlayerPrefs.GetInt("ResIndex",   2), Min = 0, Max = _resolutions.Length - 1, Step = 1, Choices = new List<string>(_resolutions) },
            new Option { Label = "Fullscreen",    Value = PlayerPrefs.GetInt("Fullscreen", 1), Min = 0, Max = 1, Step = 1, Choices = new List<string> { "OFF", "ON" } },
            new Option { Label = "Quality",       Value = PlayerPrefs.GetInt("Quality",    2), Min = 0, Max = 2, Step = 1, Choices = new List<string> { "Low", "Medium", "High" } },
        };

        _selectedRow = 0;
        RefreshAll();
        Highlight(_selectedRow);

        // Apply saved settings on load
        for (int i = 0; i < _options.Count; i++)
            ApplyOption(i);
    }

    void Update()
    {
        if (!gameObject.activeInHierarchy) return;

        if (_blockInput) { _blockInput = false; return; }

        // UP / DOWN — move between rows
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

        // LEFT / RIGHT or F5 / F6 — change value
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

        // ENTER — save and go back
        if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter))
        {
            SaveAndBack();
            MenuAudio.Instance?.PlayConfirm();
        }

        // ESCAPE — go back without saving
        if (Input.GetKeyDown(KeyCode.Escape))
            BIOSMainMenu.Instance?.SwitchTab(0);
    }

    // ── Logic ──────────────────────────────────────────────────────────
    void ChangeValue(int dir)
    {
        var opt = _options[_selectedRow];
        opt.Value = Mathf.Clamp(opt.Value + dir * opt.Step, opt.Min, opt.Max);
        ApplyOption(_selectedRow);
        RefreshRow(_selectedRow);
    }

    void ApplyOption(int index)
    {
        var opt = _options[index];
        switch (index)
        {
            case 0:
                AudioListener.volume = opt.Value / 100f;
                break;
            case 1:
                if (musicAudioSource != null)
                    musicAudioSource.volume = opt.Value / 100f;
                break;
            case 2:
                if (sfxAudioSource != null)
                    sfxAudioSource.volume = opt.Value / 100f;
                break;
            case 3:
                var parts = _resolutions[_options[3].Value].Split('x');
                bool isFullscreen = _options[4].Value == 1;
                Screen.SetResolution(int.Parse(parts[0]), int.Parse(parts[1]), isFullscreen);
                break;
            case 4:
                bool shouldBeFullscreen = _options[4].Value == 1;
                Screen.fullScreen = shouldBeFullscreen;
                // Reapply resolution with the new fullscreen state
                var resParts = _resolutions[_options[3].Value].Split('x');
                Screen.SetResolution(int.Parse(resParts[0]), int.Parse(resParts[1]), shouldBeFullscreen);
                break;
            case 5: QualitySettings.SetQualityLevel(opt.Value); break;
        }
    }

    void SaveAndBack()
    {
        PlayerPrefs.SetInt("MasterVol",  _options[0].Value);
        PlayerPrefs.SetInt("MusicVol",   _options[1].Value);
        PlayerPrefs.SetInt("SFXVol",     _options[2].Value);
        PlayerPrefs.SetInt("ResIndex",   _options[3].Value);
        PlayerPrefs.SetInt("Fullscreen", _options[4].Value);
        PlayerPrefs.SetInt("Quality",    _options[5].Value);
        PlayerPrefs.Save();
        BIOSMainMenu.Instance?.SwitchTab(0);
    }

    // ── Display ────────────────────────────────────────────────────────
    void RefreshAll()
    {
        for (int i = 0; i < _options.Count; i++)
            RefreshRow(i);
    }

    void RefreshRow(int index)
    {
        if (index >= rowLabels.Count || rowLabels[index] == null) return;

        var opt = _options[index];
        string valueStr;

        if (opt.Choices != null)
        {
            valueStr = "< " + opt.Choices[Mathf.Clamp(opt.Value, 0, opt.Choices.Count - 1)] + " >";
        }
        else
        {
            int filled = Mathf.RoundToInt(opt.Value / (float)opt.Max * 10);
            string bar = "[" + new string('\u2588', filled) + new string('\u2591', 10 - filled) + "]";
            valueStr = bar + " " + opt.Value;
        }

        int pad = Mathf.Max(1, 46 - opt.Label.Length - valueStr.Length);
        rowLabels[index].text = opt.Label + new string(' ', pad) + valueStr;
    }

    void Highlight(int index)
    {
        for (int i = 0; i < rowLabels.Count; i++)
        {
            if (rowLabels[i] == null) continue;
            rowLabels[i].color = i == index ? selectedColor : normalColor;
        }
    }
}