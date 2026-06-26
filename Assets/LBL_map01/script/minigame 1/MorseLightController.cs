using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class MorseLightController : MonoBehaviour
{
    [Header("Renderer & Material")]
    public Renderer targetRenderer;
    public int materialIndex = 0;

    [Header("Glow Colors")]
    [ColorUsage(true, true)]
    public Color glowOnColor  = new Color(5f, 4f, 0f, 1f);   // vàng khi nháy

    [ColorUsage(true, true)]
    [Tooltip("Màu KHI NGHỈ — đặt đỏ để dễ phân biệt")]
    public Color glowOffColor = new Color(1f, 0f, 0f, 1f);   // đỏ khi nghỉ

    [ColorUsage(true, true)]
    [Tooltip("Màu khi đèn hoàn toàn tắt (chưa đến lượt / đã xong)")]
    public Color glowIdleColor = new Color(0.1f, 0f, 0f, 1f); // đỏ tối khi idle

    [Header("Fade Settings")]
    public float fadeInSpeed  = 30f;
    public float fadeOutSpeed = 50f;

    [Range(0.001f, 0.05f)]
    public float offThreshold = 0.01f;

    [Header("Morse Timing (giây)")]
    public float dotDuration  = 0.15f;
    public float dashDuration = 0.55f;
    public float symbolGap    = 0.25f;
    public float letterGap    = 0.65f;
    public float wordGap      = 1.5f;
    public float repeatDelay  = 2.5f;

    [Header("Playback")]
    public string messageToEncode = "SOS";
    public bool   playOnStart     = true;
    public bool   looping         = true;

    // ─── Trạng thái nguồn điện ──────────────────────────────────
    // true = đang có điện (idle đỏ tối / đang nháy). false = MẤT ĐIỆN HẲN (đen, 0 emission).
    private bool _powered = true;

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
    private Color     _targetEmission;
    private Coroutine _playCoroutine;
    private static readonly int EmissionProp = Shader.PropertyToID("_EmissionColor");

    // ─── Unity ───────────────────────────────────────────────────
    private void Awake()
    {
        if (targetRenderer == null)
            targetRenderer = GetComponent<Renderer>();

        if (targetRenderer == null) { Debug.LogError("[Morse] Thiếu Renderer!", this); return; }

        var mats = targetRenderer.materials;
        _mat = mats[materialIndex];
        _mat.EnableKeyword("_EMISSION");
        targetRenderer.materials = mats;

        // Bắt đầu bằng màu idle (đỏ tối). Nếu cần tắt hẳn ngay từ đầu,
        // gọi SetPowerOn(false) từ script điều khiển (ví dụ WireBoxInteraction)
        // trong Start() của nó — Awake luôn chạy trước Start nên không bị chớp sáng.
        _currentEmission = glowIdleColor;
        _targetEmission  = glowIdleColor;
        _mat.SetColor(EmissionProp, glowIdleColor);
    }

    private void Start()
    {
        // Nếu không có MorseLightSequencer thì tự phát
        if (playOnStart && FindFirstObjectByType<MorseLightSequencer>() == null)
            PlayMessage(messageToEncode);
    }

    private void Update()
    {
        if (_mat == null || !_powered) return; // mất điện thì không fade, giữ nguyên màu đen đã set
        _currentEmission = Color.Lerp(_currentEmission, _targetEmission,
                                      Time.deltaTime * (_targetEmission == glowOnColor ? fadeInSpeed : fadeOutSpeed));
        _mat.SetColor(EmissionProp, _currentEmission);
    }

    // ─── Public API ───────────────────────────────────────────────

    /// <summary>
    /// TẮT/MỞ ĐIỆN THẬT cho đèn — khác với StopMorse() (chỉ về idle đỏ tối).
    /// false  = dừng mọi coroutine, set emission về ĐEN tuyệt đối (0 sáng), khoá Update().
    /// true   = mở lại, về trạng thái idle (đỏ tối), cho phép Play lại bình thường.
    /// Gọi hàm này thay cho việc set `enabled = false` — vì enabled=false KHÔNG
    /// ngăn được các lệnh gọi trực tiếp như PlayOnce()/PlayMessage() từ script khác
    /// (ví dụ MorseLightSequencer), chỉ ngăn Update()/coroutine tự sinh ra.
    /// </summary>
    public void SetPowerOn(bool on)
    {
        if (_playCoroutine != null) { StopCoroutine(_playCoroutine); _playCoroutine = null; }

        _powered = on;

        if (!on)
        {
            _currentEmission = Color.black;
            _targetEmission  = Color.black;
            _mat?.SetColor(EmissionProp, Color.black);
        }
        else
        {
            _currentEmission = glowIdleColor;
            _targetEmission  = glowIdleColor;
            _mat?.SetColor(EmissionProp, glowIdleColor);
        }
    }

    public void PlayMessage(string message)
    {
        if (!_powered) return; // mất điện thì không cho phát
        StopMorse();
        _playCoroutine = StartCoroutine(PlayCoroutine(message.ToUpper()));
    }

    /// <summary>
    /// Phát 1 lần rồi gọi callback khi xong. Dùng bởi MorseLightSequencer.
    /// </summary>
    public void PlayOnce(string message, System.Action onDone)
    {
        if (!_powered) { onDone?.Invoke(); return; } // mất điện thì coi như xong ngay, không sáng
        StopMorse();
        _playCoroutine = StartCoroutine(PlayOnceCoroutine(message.ToUpper(), onDone));
    }

    public void StopMorse()
    {
        if (_playCoroutine != null) { StopCoroutine(_playCoroutine); _playCoroutine = null; }
        if (!_powered) return; // đang mất điện thì giữ đen, không nhảy về đỏ tối
        SetTarget(glowIdleColor); // về idle (đỏ tối)
        _currentEmission = glowIdleColor;
        _mat?.SetColor(EmissionProp, glowIdleColor);
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
        SetTarget(glowOffColor); // đỏ sáng khi đang active
        do
        {
            yield return StartCoroutine(TransmitMessage(message));
            if (looping)
            {
                yield return StartCoroutine(WaitUntilOff());
                yield return new WaitForSeconds(repeatDelay);
            }
        } while (looping);

        SetTarget(glowIdleColor);
    }

    private IEnumerator PlayOnceCoroutine(string message, System.Action onDone)
    {
        SetTarget(glowOffColor); // đỏ sáng khi đang active
        yield return StartCoroutine(TransmitMessage(message));
        yield return StartCoroutine(WaitUntilOff());
        SetTarget(glowIdleColor); // về đỏ tối sau khi xong
        onDone?.Invoke();
    }

    private IEnumerator TransmitMessage(string message)
    {
        bool firstWord = true;
        foreach (string word in message.Split(' '))
        {
            if (!firstWord)
            {
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
                        yield return StartCoroutine(WaitUntilOff());
                        yield return new WaitForSeconds(symbolGap);
                    }
                    firstSymbol = false;

                    SetTarget(glowOnColor); // vàng khi nháy
                    yield return new WaitForSeconds(symbol == '.' ? dotDuration : dashDuration);
                    SetTarget(glowOffColor); // đỏ khi nghỉ giữa ký hiệu
                }
            }
        }

        yield return StartCoroutine(WaitUntilOff());
    }

    private IEnumerator WaitUntilOff()
    {
        SetTarget(glowOffColor);
        float timeout = 1f;
        while (timeout > 0f)
        {
            // So sánh với glowOffColor thay vì so với 0
            float diff = Vector4.Distance((Vector4)_currentEmission, (Vector4)glowOffColor);
            if (diff < offThreshold) yield break;
            timeout -= Time.deltaTime;
            yield return null;
        }
        _currentEmission = glowOffColor;
        _mat?.SetColor(EmissionProp, glowOffColor);
    }

    private void SetTarget(Color c) => _targetEmission = c;

/*#if UNITY_EDITOR
    private void OnValidate()
    {
        if (!string.IsNullOrEmpty(messageToEncode))
            Debug.Log($"[Morse] \"{messageToEncode}\" → {TextToMorseString(messageToEncode)}");
    }
#endif*/
}