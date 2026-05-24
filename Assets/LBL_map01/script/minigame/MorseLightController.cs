using System.Collections;
using System.Collections.Generic;
using UnityEngine;
 
/// <summary>
/// Điều khiển Glow Morse bằng Material Emission.
/// Fix: tắt hẳn trong khoảng nghỉ, dot/dash rõ ràng, không nháy sát nhau.
/// </summary>
public class MorseLightController : MonoBehaviour
{
    [Header("Renderer & Material")]
    public Renderer targetRenderer;
    public int materialIndex = 0;
 
    [Header("Glow Colors")]
    [ColorUsage(true, true)]
    [Tooltip("Màu khi bật — dùng HDR, tăng Intensity lên 3-6")]
    public Color glowOnColor  = new Color(5f, 4f, 0f, 1f);
 
    [ColorUsage(true, true)]
    [Tooltip("PHẢI để (0,0,0,1) để tắt hẳn hoàn toàn")]
    public Color glowOffColor = new Color(0f, 0f, 0f, 1f);
 
    [Header("Fade Settings")]
    [Tooltip("Tốc độ FADE IN (bật sáng) — cao = bật nhanh, sắc nét")]
    public float fadeInSpeed  = 30f;
 
    [Tooltip("Tốc độ FADE OUT (tắt) — cao = tắt nhanh, dứt khoát hơn")]
    public float fadeOutSpeed = 50f;
 
    [Tooltip("Ngưỡng coi là 'đã tắt hoàn toàn' (0.01 = 1% brightness).\n" +
             "Coroutine chờ đến khi đạt ngưỡng này mới chạy ký hiệu tiếp theo.")]
    [Range(0.001f, 0.05f)]
    public float offThreshold = 0.01f;
 
    // ─── Morse Timing ────────────────────────────────────────────
    [Header("Morse Timing (giây)")]
    [Tooltip("Thời gian bật cho dấu CHẤM — ngắn")]
    public float dotDuration    = 0.15f;
 
    [Tooltip("Thời gian bật cho dấu GẠCH — dài, nên gấp 3-4 lần dot")]
    public float dashDuration   = 0.55f;
 
    [Tooltip("Khoảng TẮT giữa 2 ký hiệu trong cùng 1 chữ.\n" +
             "Script sẽ CHỜ đèn tắt hẳn TRƯỚC KHI tính thời gian này.")]
    public float symbolGap      = 0.25f;
 
    [Tooltip("Khoảng TẮT giữa 2 chữ cái")]
    public float letterGap      = 0.65f;
 
    [Tooltip("Khoảng TẮT giữa 2 từ")]
    public float wordGap        = 1.5f;
 
    [Tooltip("Chờ trước khi lặp lại từ đầu")]
    public float repeatDelay    = 2.5f;
 
    // ─── Playback ────────────────────────────────────────────────
    [Header("Playback")]
    public string messageToEncode = "SOS";
    public bool   playOnStart     = true;
    public bool   looping         = true;
 
    // ─── Morse Table ─────────────────────────────────────────────
    private static readonly Dictionary<char, string> MorseTable = new()
    {
        {'A',".-"},  {'B',"-..."}, {'C',"-.-."}, {'D',"-.." },
        {'E',"."},   {'F',"..-."}, {'G',"--." },  {'H',"...."},
        {'I',".."},  {'J',".---"}, {'K',"-.-" },  {'L',".-.."},
        {'M',"--"},  {'N',"-." },  {'O',"---" },  {'P',".--."},
        {'Q',"--.-"},{'R',".-."},  {'S',"..." },  {'T',"-"   },
        {'U',"..-"}, {'V',"...-"}, {'W',".--" },  {'X',"-..-"},
        {'Y',"-.--"},{'Z',"--.."},
        {'0',"-----"},{'1',".----"},{'2',"..---"},{'3',"...--"},
        {'4',"....-"},{'5',"....."},{'6',"-...."},{'7',"--..."},
        {'8',"---.."},{'9',"----."},
    };
 
    // ─── Internal ────────────────────────────────────────────────
    private Material  _mat;
    private Color     _currentEmission;
    private bool      _glowOn;
    private Coroutine _playCoroutine;
    private static readonly int EmissionProp = Shader.PropertyToID("_EmissionColor");
 
    // ─── Unity ───────────────────────────────────────────────────
    private void Awake()
    {
        if (targetRenderer == null)
            targetRenderer = GetComponent<Renderer>();
 
        if (targetRenderer == null) { Debug.LogError("[Morse] Thiếu Renderer!", this); return; }
 
        // Instance material — không đụng asset gốc
        var mats = targetRenderer.materials;
        _mat = mats[materialIndex];
        _mat.EnableKeyword("_EMISSION");
        targetRenderer.materials = mats;
 
        _currentEmission = glowOffColor;
        _mat.SetColor(EmissionProp, glowOffColor);
        _glowOn = false;
    }
 
    private void Start()
    {
        if (playOnStart) PlayMessage(messageToEncode);
    }
 
    private void Update()
    {
        if (_mat == null) return;
 
        // Target: nếu glowOn → fade tới màu sáng, ngược lại fade về 0
        Color target = _glowOn ? glowOnColor : glowOffColor;
        float speed  = _glowOn ? fadeInSpeed  : fadeOutSpeed;
 
        _currentEmission = Color.Lerp(_currentEmission, target, Time.deltaTime * speed);
        _mat.SetColor(EmissionProp, _currentEmission);
    }
 
    // ─── Public API ───────────────────────────────────────────────
    public void PlayMessage(string message)
    {
        StopMorse();
        _playCoroutine = StartCoroutine(PlayCoroutine(message.ToUpper()));
    }
 
    public void StopMorse()
    {
        if (_playCoroutine != null) { StopCoroutine(_playCoroutine); _playCoroutine = null; }
        _glowOn = false;
        // Tắt ngay lập tức không chờ lerp
        _currentEmission = glowOffColor;
        _mat?.SetColor(EmissionProp, glowOffColor);
    }
 
    public string TextToMorseString(string text)
    {
        var words = text.ToUpper().Split(' ');
        var result = new List<string>();
        foreach (var word in words)
        {
            var letters = new List<string>();
            foreach (char c in word)
                if (MorseTable.TryGetValue(c, out var code)) letters.Add(code);
            result.Add(string.Join(" ", letters));
        }
        return string.Join("  /  ", result);
    }
 
    // ─── Coroutines ───────────────────────────────────────────────
    private IEnumerator PlayCoroutine(string message)
    {
        do
        {
            yield return StartCoroutine(TransmitMessage(message));
            if (looping)
            {
                yield return StartCoroutine(WaitUntilOff());
                yield return new WaitForSeconds(repeatDelay);
            }
        } while (looping);
    }
 
    private IEnumerator TransmitMessage(string message)
    {
        bool firstWord = true;
        foreach (string word in message.Split(' '))
        {
            if (!firstWord)
            {
                // Khoảng nghỉ giữa từ — chờ tắt hẳn rồi mới nghỉ thêm
                yield return StartCoroutine(WaitUntilOff());
                yield return new WaitForSeconds(wordGap);
            }
            firstWord = false;
 
            bool firstLetter = true;
            foreach (char c in word)
            {
                if (!MorseTable.TryGetValue(c, out string morseCode)) continue;
 
                if (!firstLetter)
                {
                    yield return StartCoroutine(WaitUntilOff());
                    yield return new WaitForSeconds(letterGap);
                }
                firstLetter = false;
 
                bool firstSymbol = true;
                foreach (char symbol in morseCode)
                {
                    if (!firstSymbol)
                    {
                        // *** KEY FIX: chờ đèn tắt HẲN trước khi nghỉ ***
                        yield return StartCoroutine(WaitUntilOff());
                        yield return new WaitForSeconds(symbolGap);
                    }
                    firstSymbol = false;
 
                    // Bật đèn
                    _glowOn = true;
                    float duration = (symbol == '.') ? dotDuration : dashDuration;
                    yield return new WaitForSeconds(duration);
 
                    // Tắt đèn
                    _glowOn = false;
                }
            }
        }
 
        // Đảm bảo tắt hẳn cuối chuỗi
        yield return StartCoroutine(WaitUntilOff());
    }
 
    /// <summary>
    /// Chờ cho đến khi emission gần bằng 0 hoàn toàn.
    /// Giải quyết vấn đề "vẫn còn đỏ đỏ" trong khoảng nghỉ.
    /// </summary>
    private IEnumerator WaitUntilOff()
    {
        _glowOn = false;
        // Timeout tránh vòng lặp vô tận nếu có bug
        float timeout = 1f;
        while (timeout > 0f)
        {
            float brightness = _currentEmission.maxColorComponent;
            if (brightness <= offThreshold) yield break;
            timeout -= Time.deltaTime;
            yield return null;
        }
        // Force tắt nếu timeout
        _currentEmission = glowOffColor;
        _mat?.SetColor(EmissionProp, glowOffColor);
    }
 
#if UNITY_EDITOR
    private void OnValidate()
    {
        if (!string.IsNullOrEmpty(messageToEncode))
            Debug.Log($"[Morse] \"{messageToEncode}\" → {TextToMorseString(messageToEncode)}");
    }
#endif
}