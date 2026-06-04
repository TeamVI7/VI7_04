using System.Collections;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 2D UI objective marker for flat map intros.
/// Renders as animated square/diamond over a map image.
/// 
/// Designed to sit as child of the map RectTransform so it pans/zooms with the map.
/// 
/// EXTEND: Add new MarkerType entries. Style via markerColor per objective.
/// DEBUG:  Right-click component → ShowMarker / HideMarker to test in Play mode.
/// </summary>
public class ObjectiveMarker2D : MonoBehaviour
{
    // ─── Inspector ───────────────────────────────────────────────────────────

    [Header("=== IDENTITY ===")]
    public string objectiveName = "A";
    public MarkerType markerType = MarkerType.Diamond;
    public Color markerColor = new Color(0.2f, 0.8f, 1f, 1f); // NATO cyan default

    [Header("=== UI REFERENCES ===")]
    [Tooltip("Outer rotating frame image")]
    public RectTransform frameRect;
    public Image frameImage;

    [Tooltip("Inner fill (optional — solid center)")]
    public Image fillImage;

    [Tooltip("Letter label: A, B, HQ, etc.")]
    public Text labelText;

    [Tooltip("Expanding ping ring on reveal")]
    public RectTransform pingRect;
    public Image pingImage;

    [Header("=== ANIMATION ===")]
    [Range(0.2f, 1.5f)] public float revealDuration  = 0.4f;
    [Range(10f, 120f)]  public float rotationSpeed   = 60f;   // degrees/sec
    [Range(0.95f, 1.1f)]public float pulseAmount     = 1.04f;
    [Range(0.4f, 3f)]   public float pulsePeriod     = 1.0f;

    [Header("=== PING ===")]
    public bool pingOnReveal = true;
    [Range(0.3f, 2f)] public float pingDuration = 0.7f;
    [Range(1.5f, 5f)] public float pingMaxScale  = 3f;

    // ─── Private ─────────────────────────────────────────────────────────────

    private bool _active;
    private Vector3 _baseFrameScale;
    private float _revealTime;

    // ─── Unity ───────────────────────────────────────────────────────────────

    private void Awake()
    {
        if (frameRect != null) _baseFrameScale = frameRect.localScale;
        ApplyMarkerType();
        ApplyColor();
        SetAllAlpha(0f);
        if (labelText != null) labelText.text = objectiveName;
    }

    private void Update()
    {
        if (!_active || frameRect == null) return;

        // Rotation
        if (markerType == MarkerType.Diamond || markerType == MarkerType.RotatingSquare)
            frameRect.Rotate(0f, 0f, rotationSpeed * Time.deltaTime);

        // Pulse
        float t = Mathf.Sin((Time.time - _revealTime) * (Mathf.PI * 2f / pulsePeriod));
        float s = 1f + t * (pulseAmount - 1f);
        frameRect.localScale = _baseFrameScale * s;
    }

    // ─── Public API ──────────────────────────────────────────────────────────

    [ContextMenu("▶ Show Marker")]
    public void ShowMarker()
    {
        gameObject.SetActive(true);
        StopAllCoroutines();
        StartCoroutine(RevealRoutine());
    }

    [ContextMenu("■ Hide Marker")]
    public void HideMarker()
    {
        _active = false;
        StopAllCoroutines();
        SetAllAlpha(0f);
        gameObject.SetActive(false);
    }

    /// <summary>Change label at runtime (e.g., objective captured → show new name)</summary>
    public void SetLabel(string label)
    {
        objectiveName = label;
        if (labelText != null) labelText.text = label;
    }

    /// <summary>Swap team color at runtime</summary>
    public void SetColor(Color color)
    {
        markerColor = color;
        ApplyColor();
    }

    // ─── Routines ────────────────────────────────────────────────────────────

    private IEnumerator RevealRoutine()
    {
        _active = false;
        SetAllAlpha(0f);
        if (frameRect != null) frameRect.localScale = _baseFrameScale * 0.2f;

        float elapsed = 0f;
        while (elapsed < revealDuration)
        {
            float t = elapsed / revealDuration;
            float eased = EaseOutBack(t);
            SetAllAlpha(Mathf.Clamp01(t * 2f)); // alpha faster than scale
            if (frameRect != null)
                frameRect.localScale = _baseFrameScale * Mathf.Clamp(eased, 0.01f, 1.5f);
            elapsed += Time.deltaTime;
            yield return null;
        }

        SetAllAlpha(1f);
        if (frameRect != null) frameRect.localScale = _baseFrameScale;
        _revealTime = Time.time;
        _active = true;

        if (pingOnReveal && pingRect != null)
            StartCoroutine(PingRoutine());
    }

    private IEnumerator PingRoutine()
    {
        pingRect.gameObject.SetActive(true);
        Vector3 startScale = Vector3.one;
        float elapsed = 0f;

        while (elapsed < pingDuration)
        {
            float t = elapsed / pingDuration;
            pingRect.localScale = Vector3.Lerp(startScale, startScale * pingMaxScale, t);
            if (pingImage != null)
            {
                Color c = markerColor;
                c.a = Mathf.Lerp(0.9f, 0f, t);
                pingImage.color = c;
            }
            elapsed += Time.deltaTime;
            yield return null;
        }

        pingRect.gameObject.SetActive(false);
    }

    // ─── Helpers ─────────────────────────────────────────────────────────────

    private void SetAllAlpha(float alpha)
    {
        SetImageAlpha(frameImage, alpha);
        SetImageAlpha(fillImage, alpha * 0.4f); // fill more transparent
        if (labelText != null)
        {
            Color c = labelText.color;
            c.a = alpha;
            labelText.color = c;
        }
    }

    private void SetImageAlpha(Image img, float alpha)
    {
        if (img == null) return;
        Color c = markerColor;
        c.a = alpha;
        img.color = c;
    }

    private void ApplyColor()
    {
        // Preserve current alpha when changing color
        if (frameImage != null) { Color c = markerColor; c.a = frameImage.color.a; frameImage.color = c; }
        if (fillImage  != null) { Color c = markerColor; c.a = fillImage.color.a;  fillImage.color  = c; }
    }

    private void ApplyMarkerType()
    {
        if (frameRect == null) return;
        switch (markerType)
        {
            case MarkerType.Diamond:
                frameRect.localEulerAngles = new Vector3(0f, 0f, 45f); // start at 45°
                break;
            case MarkerType.Square:
            case MarkerType.RotatingSquare:
                frameRect.localEulerAngles = Vector3.zero;
                break;
        }
    }

    private float EaseOutBack(float t)
    {
        const float c1 = 1.70158f;
        const float c3 = c1 + 1f;
        return 1f + c3 * Mathf.Pow(t - 1f, 3f) + c1 * Mathf.Pow(t - 1f, 2f);
    }

    // ─── Enums ───────────────────────────────────────────────────────────────

    public enum MarkerType
    {
        Diamond,        // 45° rotated, keeps rotating (BF-style)
        RotatingSquare, // Upright square, keeps rotating
        Square,         // Static square, pulse only
        Cross,          // + shape (use cross sprite)
    }
}