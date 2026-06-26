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
///   2. Assign controller, terminalText, progressBarFill (Image, Filled type),
///      percentText.
///   3. Call controller.BeginLoad(...) from wherever you trigger a scene
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

    [Header("Debug")]
    [SerializeField] private bool showDebugLogs = false;

    #endregion

    // ─────────────────────────────────────────────────────────────────────────
    #region Private State
    // ─────────────────────────────────────────────────────────────────────────

    private readonly StringBuilder _textBuilder = new StringBuilder();
    private string _currentLabel = "";

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

        SetVisible(false);
    }

    private void OnDisable()
    {
        if (controller == null) return;

        controller.OnLoadStart       -= HandleLoadStart;
        controller.OnStepChanged     -= HandleStepChanged;
        controller.OnProgressChanged -= HandleProgressChanged;
        controller.OnLoadComplete    -= HandleLoadComplete;
    }

    #endregion

    // ─────────────────────────────────────────────────────────────────────────
    #region Event Handlers
    // ─────────────────────────────────────────────────────────────────────────

    private void HandleLoadStart()
    {
        Log("Load start — showing terminal.");
        _textBuilder.Clear();

        if (flavourLines != null && flavourLines.Length > 0)
            _textBuilder.AppendLine(flavourLines[Random.Range(0, flavourLines.Length)]);

        SetTerminalText(_textBuilder.ToString());
        SetProgressVisual(0f);
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
    }

    private void HandleProgressChanged(float progress, string label)
    {
        SetProgressVisual(progress);

        if (percentText != null)
            percentText.text = $"{Mathf.RoundToInt(progress * 100f)}%";

        if (progress >= 1f && label == "READY")
        {
            _textBuilder.AppendLine("READY.");
            SetTerminalText(_textBuilder.ToString());
            if (audioSource != null && readySound != null)
                audioSource.PlayOneShot(readySound);
        }
    }

    private void HandleLoadComplete()
    {
        Log("Load complete — hiding terminal.");
        StartCoroutine(Co_FadeOut());
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