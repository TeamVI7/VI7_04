using System.Collections;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// BIOS-terminal style loading screen visuals. Subscribes to
/// LoadingScreenController events only — never touches scene loading or
/// shader warmup directly, so the visual style can change freely without
/// risk to the actual loading logic.
///
/// SETUP:
///   1. Place on a Canvas (or child of one) alongside LoadingScreenController.
///      IMPORTANT: this object must live under the SAME persistent root as
///      LoadingScreenController (or be persisted itself) — if the Canvas
///      gets destroyed on scene unload while the controller survives,
///      screenGroup becomes a dead reference and nothing will render, with
///      no error logged.
///   2. Assign controller, terminalText, progressBarFill (Image, Filled type),
///      percentText.
///   3. Optional: assign spinner (AsciiSpinner, defined below in this same
///      file) for a "/ - \ |" spin while loading is active.
///   4. Optional: assign injectFillBar (Image, Filled type) for the hold-E
///      confirm bar — only shows up on transitions whose SceneTransitionConfig
///      has requireHoldToConfirm checked. Leave unassigned if you never use
///      that feature.
///   5. Call controller.BeginLoad(...) from wherever you trigger a scene
///      change (CutsceneManager, BIOSMainMenu.OnDeploy, etc).
///
/// EXTEND:
///   - Add more bootLines-style flavour text by editing flavourLines below.
///   - Swap the progress bar for a spinner by disabling progressBarFill and
///     driving a rotating Image instead — OnProgressChanged still fires the
///     same either way.
/// </summary>
public class LoadingBIOSDisplay : MonoBehaviour
{
    // ─────────────────────────────────────────────────────────────────────────
    #region Inspector
    // ─────────────────────────────────────────────────────────────────────────

    [Header("References")]
    public LoadingScreenController controller;
    public CanvasGroup screenGroup;
    public TextMeshProUGUI terminalText;
    public TextMeshProUGUI percentText;
    public Image progressBarFill;     // Image Type = Filled, Fill Method = Horizontal

    [Header("Spinner")]
    public TextMeshProUGUI spinnerText;
    public float spinnerFrameInterval = 0.1f;

    [Header("Hold To Confirm (optional, per-transition)")]
    [Tooltip("Only appears on transitions whose SceneTransitionConfig has " +
             "requireHoldToConfirm checked. Leave unassigned if unused.")]
    public Image injectFillBar;       // Image Type = Filled, Fill Method = Horizontal
    public KeyCode confirmKey = KeyCode.E;
    public bool allowMouseConfirm = false; // mirrors Input.GetMouseButton(0) if true
    public float holdDuration = 1f;
    public AudioClip injectHoldSound;
    [Tooltip("How fast the hold bar drains per second (as a multiple of " +
             "holdDuration) when the key is released early.")]
    public float releaseDrainMultiplier = 3f;

    [Header("Flavour Lines")]
    [Tooltip("Random line shown above the status label, purely cosmetic.")]
    public string[] flavourLines =
    {
        "ESTABLISHING UPLINK...",
        "SYNCHRONISING SECTOR CLOCK...",
        "VERIFYING OPERATIVE CREDENTIALS...",
        "ROUTING THROUGH SEC SUBNET...",
    };

    [Header("Glitch")]
    public float glitchIntensity = 2f;
    public float glitchChance    = 0.05f;   // per status-line update

    [Header("Audio")]
    public AudioSource audioSource;
    public AudioClip   tickSound;
    public AudioClip   readySound;

    [Header("Fade")]
    public float fadeInDuration  = 0.25f;
    public float fadeOutDuration = 0.35f;

    [Header("Shader Chatter")]
    public string shaderStepLabelMatch = "WARMING RENDER CACHE";
    public float chatterInterval = 0.12f;
    public int maxChatterLines = 10;
    [Tooltip("Even if the real load finishes instantly, the displayed bar " +
             "won't jump — it animates toward the target at this much " +
             "progress-per-second minimum. Higher = snappier, lower = slower crawl.")]
    public float minFillSpeedPerSecond = 0.6f;

    [Header("Debug")]
    [SerializeField] private bool showDebugLogs = false;

    #endregion

    // ─────────────────────────────────────────────────────────────────────────
    #region Private State
    // ─────────────────────────────────────────────────────────────────────────

    private readonly StringBuilder _textBuilder = new StringBuilder();
    private string _currentLabel = "";
    private bool _isLoading;

    // Hold-to-confirm state
    private bool  _awaitingConfirm;
    private bool  _injecting;
    private float _holdTimer;

    // Bar smoothing: real progress can jump straight to 1 on fast loads,
    // this keeps what's ON SCREEN crawling up instead of popping.
    private float _targetProgress;
    private float _displayedProgress;

    private static readonly string[] SpinnerFrames = { "/", "-", "\\", "|" };
    private int _spinnerIndex;
    private float _spinnerTimer;

    private Coroutine _chatterCoroutine;

    #endregion

    // ─────────────────────────────────────────────────────────────────────────
    #region Unity Lifecycle
    // ─────────────────────────────────────────────────────────────────────────

    private void OnEnable()
    {
        if (controller == null)
        {
            LogWarning("controller not assigned — display will do nothing.");
            return;
        }

        controller.OnLoadStart       += HandleLoadStart;
        controller.OnStepChanged     += HandleStepChanged;
        controller.OnProgressChanged += HandleProgressChanged;
        controller.OnLoadComplete    += HandleLoadComplete;
        controller.OnReadyForConfirm += HandleReadyForConfirm;

        SetVisible(false);
    }

    private void OnDisable()
    {
        if (controller == null) return;

        controller.OnLoadStart       -= HandleLoadStart;
        controller.OnStepChanged     -= HandleStepChanged;
        controller.OnProgressChanged -= HandleProgressChanged;
        controller.OnLoadComplete    -= HandleLoadComplete;
        controller.OnReadyForConfirm -= HandleReadyForConfirm;
    }

    private void Update()
    {
        if (_isLoading)
            TickSpinner();

        if (_awaitingConfirm)
            TickHoldToConfirm();

        TickProgressSmoothing();
    }

    #endregion

    // ─────────────────────────────────────────────────────────────────────────
    #region Event Handlers
    // ─────────────────────────────────────────────────────────────────────────

    private void HandleLoadStart()
    {
        _isLoading = true;
        Log("Load start — showing terminal.");
        _textBuilder.Clear();

        if (flavourLines != null && flavourLines.Length > 0)
            _textBuilder.AppendLine(flavourLines[Random.Range(0, flavourLines.Length)]);

        SetTerminalText(_textBuilder.ToString());
        _targetProgress = 0f;
        _displayedProgress = 0f;
        SetProgressVisual(0f);

        _spinnerIndex = 0;
        _spinnerTimer = 0f;
        if (spinnerText != null) spinnerText.text = SpinnerFrames[0];

        StartCoroutine(Co_FadeIn());
    }

    private void HandleStepChanged(int index, int total, string label)
    {
        _currentLabel = label;
        Log($"Step changed → [{index + 1}/{total}] {label}");

        _textBuilder.AppendLine($"[{index + 1}/{total}] {label}...");
        SetTerminalText(_textBuilder.ToString());

        PlayTick();
        MaybeGlitch();

        if (_chatterCoroutine != null)
        {
            StopCoroutine(_chatterCoroutine);
            _chatterCoroutine = null;
        }

        if (label.ToUpperInvariant() == shaderStepLabelMatch.ToUpperInvariant())
            _chatterCoroutine = StartCoroutine(Co_ShaderChatter());
    }

    private void HandleProgressChanged(float progress, string label)
    {
        // Don't set the bar directly — feed the target and let
        // TickProgressSmoothing crawl toward it, so fast/instant real loads
        // still read as a load on screen instead of popping to full.
        _targetProgress = progress;

        if (progress >= 1f && label == "READY")
        {
            _textBuilder.AppendLine("READY.");
            SetTerminalText(_textBuilder.ToString());
            if (audioSource != null && readySound != null)
                audioSource.PlayOneShot(readySound);
        }
    }

    private void TickProgressSmoothing()
    {
        if (Mathf.Approximately(_displayedProgress, _targetProgress)) return;

        _displayedProgress = Mathf.MoveTowards(
            _displayedProgress, _targetProgress,
            minFillSpeedPerSecond * Time.unscaledDeltaTime);

        SetProgressVisual(_displayedProgress);
        if (percentText != null)
            percentText.text = $"{Mathf.RoundToInt(_displayedProgress * 100f)}%";
    }

    private void HandleReadyForConfirm()
    {
        Log("Awaiting hold-to-confirm.");
        _awaitingConfirm = true;
        _injecting = false;
        _holdTimer = 0f;

        if (injectFillBar != null)
        {
            injectFillBar.gameObject.SetActive(true);
            injectFillBar.fillAmount = 0f;
        }
    }

    private void HandleLoadComplete()
    {
        _isLoading = false;
        _awaitingConfirm = false;
        if (injectFillBar != null) injectFillBar.gameObject.SetActive(false);

        if (_chatterCoroutine != null)
        {
            StopCoroutine(_chatterCoroutine);
            _chatterCoroutine = null;
        }

        Log("Load complete — hiding terminal.");
        StartCoroutine(Co_FadeOut());
    }

    private void TickSpinner()
    {
        _spinnerTimer += Time.unscaledDeltaTime;
        if (_spinnerTimer < spinnerFrameInterval) return;

        _spinnerTimer = 0f;
        _spinnerIndex = (_spinnerIndex + 1) % SpinnerFrames.Length;
        if (spinnerText != null) spinnerText.text = SpinnerFrames[_spinnerIndex];
    }

    private IEnumerator Co_ShaderChatter()
    {
        int lines = 0;
        while (lines < maxChatterLines)
        {
            uint hex = (uint)Random.Range(0, 0xFFFFFF);
            _textBuilder.AppendLine($"C:\\SYS> compiling shader_variant_0x{hex:X6}.spv... OK");
            SetTerminalText(_textBuilder.ToString());
            PlayTick();
            lines++;
            yield return new WaitForSecondsRealtime(chatterInterval);
        }
        _chatterCoroutine = null;
    }

    #endregion

    // ─────────────────────────────────────────────────────────────────────────
    #region Hold To Confirm
    // ─────────────────────────────────────────────────────────────────────────

    private void TickHoldToConfirm()
    {
        bool holding = Input.GetKey(confirmKey) || (allowMouseConfirm && Input.GetMouseButton(0));

        if (holding)
        {
            if (!_injecting)
                PlayHoldSound();

            _injecting = true;
            if (injectFillBar != null) injectFillBar.gameObject.SetActive(true);

            _holdTimer += Time.unscaledDeltaTime;
            if (injectFillBar != null)
                injectFillBar.fillAmount = _holdTimer / holdDuration;

            if (_holdTimer >= holdDuration)
            {
                _awaitingConfirm = false;
                StopHoldSound();
                if (injectFillBar != null) injectFillBar.gameObject.SetActive(false);
                controller.ConfirmReady();
            }
        }
        else
        {
            if (_injecting) StopHoldSound();
            _injecting = false;

            _holdTimer = Mathf.Max(0f, _holdTimer - Time.unscaledDeltaTime * releaseDrainMultiplier);
            if (injectFillBar != null)
            {
                injectFillBar.fillAmount = _holdTimer / holdDuration;
                if (_holdTimer <= 0f)
                    injectFillBar.gameObject.SetActive(false);
            }
        }
    }

    private void PlayHoldSound()
    {
        if (audioSource == null || injectHoldSound == null) return;
        audioSource.clip  = injectHoldSound;
        audioSource.loop  = true;
        audioSource.pitch = 1f;
        audioSource.Play();
    }

    private void StopHoldSound()
    {
        if (audioSource == null) return;
        if (audioSource.clip == injectHoldSound && audioSource.isPlaying)
            audioSource.Stop();
        audioSource.loop = false;
    }

    #endregion

    // ─────────────────────────────────────────────────────────────────────────
    #region Visual Helpers
    // ─────────────────────────────────────────────────────────────────────────

    private void SetTerminalText(string text)
    {
        if (terminalText != null) terminalText.text = text;
    }

    private void SetProgressVisual(float progress)
    {
        if (progressBarFill != null) progressBarFill.fillAmount = progress;
    }

    private void SetVisible(bool visible)
    {
        if (screenGroup == null) return;
        screenGroup.alpha          = visible ? 1f : 0f;
        screenGroup.blocksRaycasts = visible;
        screenGroup.gameObject.SetActive(visible);
    }

    private void PlayTick()
    {
        if (audioSource != null && tickSound != null)
            audioSource.PlayOneShot(tickSound, 0.4f);
    }

    /// <summary>Occasionally jitter the terminal text position — same cosmetic
    /// trick as MissionTitleUI's glitch effect, kept tiny so it reads as a
    /// flicker rather than full-on corruption.</summary>
    private void MaybeGlitch()
    {
        if (terminalText == null) return;
        if (Random.value > glitchChance) return;

        var rt = terminalText.rectTransform;
        Vector2 original = rt.anchoredPosition;
        rt.anchoredPosition = original + new Vector2(
            Random.Range(-glitchIntensity, glitchIntensity), 0f);

        StartCoroutine(Co_SnapBack(rt, original));
    }

    private IEnumerator Co_SnapBack(RectTransform rt, Vector2 original)
    {
        yield return new WaitForSecondsRealtime(0.04f);
        if (rt != null) rt.anchoredPosition = original;
    }

    #endregion

    // ─────────────────────────────────────────────────────────────────────────
    #region Fade
    // ─────────────────────────────────────────────────────────────────────────

    private IEnumerator Co_FadeIn()
    {
        if (screenGroup == null) yield break;
        screenGroup.gameObject.SetActive(true);
        screenGroup.blocksRaycasts = true;

        float t = 0f;
        while (t < fadeInDuration)
        {
            t += Time.unscaledDeltaTime;
            screenGroup.alpha = Mathf.Clamp01(t / fadeInDuration);
            yield return null;
        }
        screenGroup.alpha = 1f;
    }

    private IEnumerator Co_FadeOut()
    {
        if (screenGroup == null) yield break;

        float t = 0f;
        while (t < fadeOutDuration)
        {
            t += Time.unscaledDeltaTime;
            screenGroup.alpha = 1f - Mathf.Clamp01(t / fadeOutDuration);
            yield return null;
        }
        screenGroup.alpha = 0f;
        screenGroup.blocksRaycasts = false;
        screenGroup.gameObject.SetActive(false);
    }

    #endregion

    // ─────────────────────────────────────────────────────────────────────────
    #region Debug
    // ─────────────────────────────────────────────────────────────────────────

    [System.Diagnostics.Conditional("UNITY_EDITOR")]
    private void Log(string msg)
    {
        if (showDebugLogs) Debug.Log($"[LoadingBIOSDisplay] {msg}", this);
    }

    [System.Diagnostics.Conditional("UNITY_EDITOR")]
    private void LogWarning(string msg) => Debug.LogWarning($"[LoadingBIOSDisplay] ⚠ {msg}", this);

    #endregion
}