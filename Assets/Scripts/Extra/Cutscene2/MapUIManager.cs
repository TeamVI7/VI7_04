using System.Collections;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// UI layer for flat map operation intro.
/// Handles: fade panel, operation title, briefing text, typewriter effect.
/// 
/// HIERARCHY:
///   IntroCanvas
///   ├── FlatMapIntroUIManager (this script)
///   ├── MapContainer
///   │   ├── MapRawImage (your DOOM/tactical map texture)
///   │   ├── ScanlineOverlay (optional subtle scanline image)
///   │   └── [ObjectiveMarker2D children go here at runtime]
///   ├── OverlayPanel (CanvasGroup)
///   │   ├── TopBar
///   │   │   ├── OperationLabel  e.g. "OPERATION AUGUR"
///   │   │   └── SubtitleLabel   e.g. "CONTAMINATED: HARD DECK"
///   │   ├── SidePanel
///   │   │   ├── BriefingHeader  "MISSION BRIEFING"
///   │   │   └── BriefingBody    (typewriter text)
///   │   └── TeamLabel           e.g. "NATO: ATTACKER"
///   └── FadePanel
///       └── FadeImage (full black, 100% stretch)
/// </summary>
public class FlatMapUIManager : MonoBehaviour
{
    // ─── Inspector ───────────────────────────────────────────────────────────

    [Header("=== FADE ===")]
    public Image fadeImage;

    [Header("=== OVERLAY ===")]
    public CanvasGroup overlayGroup;
    [Range(0.3f, 2f)] public float overlayFadeInDuration  = 0.5f;
    [Range(1f, 8f)]   public float overlayHoldDuration    = 3.5f;
    [Range(0.3f, 2f)] public float overlayFadeOutDuration = 0.6f;

    [Header("=== TITLE LABELS ===")]
    public Text operationLabel;
    public Text subtitleLabel;
    public Text teamLabel;
    public Text floorLabel;         // "2nd Floor", "1st Floor" etc.

    [Header("=== BRIEFING ===")]
    public Text briefingHeaderText;
    public Text briefingBodyText;
    [TextArea(4, 10)]
    public string briefingContent =
        "Eliminate all hostiles across both floors.\n\n" +
        "□  Secure sector objectives before advancing.\n" +
        "□  Collect Praetor tokens to upgrade armor.\n" +
        "□  Runes grant passive combat abilities.\n" +
        "□  Final boss awaits on the lower level.";

    [Header("=== OPERATION DATA ===")]
    public string operationName    = "OPERATION AUGUR";
    public string operationSubtitle = "CONTAMINATED: HARD DECK";
    public string teamName          = "NATO: ATTACKER";
    public string currentFloor      = "2nd Floor";

    [Header("=== TYPEWRITER ===")]
    public bool useTypewriter = true;
    [Range(20f, 200f)] public float charsPerSecond = 50f;

    [Header("=== GLITCH EFFECT ===")]
    [Tooltip("Quick glitch flicker on title appear — military HUD feel")]
    public bool glitchOnReveal = true;
    [Range(1, 5)] public int glitchFlashes = 3;

    // ─── Private ─────────────────────────────────────────────────────────────

    private bool _initialized;

    // ─── Unity ───────────────────────────────────────────────────────────────

    private void Awake()
    {
        SetFadeAlpha(1f);
        if (overlayGroup != null) overlayGroup.alpha = 0f;
        PopulateLabels();
        _initialized = true;
    }

    // ─── Public API ──────────────────────────────────────────────────────────

    public IEnumerator FadeFromBlack(float duration)
    {
        yield return StartCoroutine(AnimateFade(1f, 0f, duration));
    }

    public IEnumerator FadeToBlack(float duration)
    {
        yield return StartCoroutine(AnimateFade(0f, 1f, duration));
    }

    public IEnumerator ShowBriefingOverlay()
    {
        // Fade in overlay
        if (overlayGroup != null)
            yield return StartCoroutine(FadeGroup(overlayGroup, 0f, 1f, overlayFadeInDuration));

        // Glitch title
        if (glitchOnReveal && operationLabel != null)
            yield return StartCoroutine(GlitchLabel(operationLabel));

        // Typewriter briefing
        if (useTypewriter && briefingBodyText != null)
            yield return StartCoroutine(Typewriter(briefingBodyText, briefingContent));
        else if (briefingBodyText != null)
            briefingBodyText.text = briefingContent;

        // Hold
        yield return new WaitForSeconds(overlayHoldDuration);

        // Fade out overlay
        if (overlayGroup != null)
            yield return StartCoroutine(FadeGroup(overlayGroup, 1f, 0f, overlayFadeOutDuration));
    }

    public void ForceHideAll()
    {
        SetFadeAlpha(0f);
        if (overlayGroup != null) overlayGroup.alpha = 0f;
    }

    /// <summary>Inject data from server before PlayIntro (multiplayer use)</summary>
    public void SetMissionData(string opName, string subtitle, string team, string floor, string briefing)
    {
        operationName     = opName;
        operationSubtitle = subtitle;
        teamName          = team;
        currentFloor      = floor;
        briefingContent   = briefing;
        if (_initialized) PopulateLabels();
    }

    // ─── Routines ────────────────────────────────────────────────────────────

    private IEnumerator AnimateFade(float from, float to, float duration)
    {
        if (fadeImage == null) yield break;
        float elapsed = 0f;
        SetFadeAlpha(from);
        while (elapsed < duration)
        {
            SetFadeAlpha(Mathf.Lerp(from, to, elapsed / duration));
            elapsed += Time.deltaTime;
            yield return null;
        }
        SetFadeAlpha(to);
    }

    private IEnumerator FadeGroup(CanvasGroup grp, float from, float to, float dur)
    {
        float elapsed = 0f;
        grp.alpha = from;
        while (elapsed < dur)
        {
            grp.alpha = Mathf.Lerp(from, to, elapsed / dur);
            elapsed += Time.deltaTime;
            yield return null;
        }
        grp.alpha = to;
    }

    private IEnumerator Typewriter(Text target, string content)
    {
        target.text = "";
        float interval = 1f / charsPerSecond;
        foreach (char c in content)
        {
            target.text += c;
            yield return new WaitForSeconds(interval);
        }
    }

    private IEnumerator GlitchLabel(Text target)
    {
        string original = target.text;
        for (int i = 0; i < glitchFlashes; i++)
        {
            target.text = "";
            yield return new WaitForSeconds(0.05f);
            target.text = original;
            yield return new WaitForSeconds(0.08f);
        }
    }

    // ─── Helpers ─────────────────────────────────────────────────────────────

    private void SetFadeAlpha(float alpha)
    {
        if (fadeImage == null) return;
        Color c = fadeImage.color;
        c.a = alpha;
        fadeImage.color = c;
    }

    private void PopulateLabels()
    {
        if (operationLabel   != null) operationLabel.text   = operationName;
        if (subtitleLabel    != null) subtitleLabel.text    = operationSubtitle;
        if (teamLabel        != null) teamLabel.text        = teamName;
        if (floorLabel       != null) floorLabel.text       = currentFloor;
        if (!useTypewriter && briefingBodyText != null) briefingBodyText.text = briefingContent;
    }
}