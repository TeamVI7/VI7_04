using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

/// <summary>
/// Operation intro for FLAT 2D maps (image-based like DOOM automap, tactical maps).
/// 
/// WORKFLOW:
///   1. Assign your map Sprite/Texture to mapImage
///   2. Place ObjectiveMarker2D entries with normalized positions (0-1 range on map image)
///   3. Configure camera pan (optional — pans the UI map rect, not a 3D camera)
///   4. Hit Play — sequence runs automatically
/// 
/// EXTEND: Add new IntroPhase entries. Wire onFadeComplete to scene load.
/// DEBUG:  debugMode logs every phase. ContextMenu methods test phases in isolation.
/// </summary>
public class FlatMapIntroController : MonoBehaviour
{
    // ─── Inspector ───────────────────────────────────────────────────────────

    [Header("=== MAP IMAGE ===")]
    [Tooltip("The RawImage or Image component showing your map texture")]
    public RectTransform mapRect;
    [Tooltip("Optional: animated scan-line overlay on the map")]
    public CanvasGroup mapScanlineGroup;

    [Header("=== MAP PAN / ZOOM ===")]
    public MapPanSettings mapPan;

    [Header("=== OBJECTIVE MARKERS ===")]
    [Tooltip("Each marker + its normalized position on the map (0,0 = bottom-left, 1,1 = top-right)")]
    public List<MarkerEntry> markers = new List<MarkerEntry>();
    [Range(0f, 2f)] public float markerStaggerDelay = 0.35f;

    [Header("=== UI ===")]
    public FlatMapUIManager uiManager;

    [Header("=== FADE ===")]
    public FadeConfig fade;

    [Header("=== PLAYBACK ===")]
    public bool autoPlay = true;
    public bool debugMode = true;
    [Tooltip("Press in Play mode to skip to gameplay")]
    public bool skipIntro = false;

    [Header("=== EVENTS ===")]
    public UnityEvent onIntroStart;
    public UnityEvent onMarkersShown;
    public UnityEvent onFadeComplete;   // ← Wire SceneManager.LoadScene here
    public UnityEvent onGameplayStart;

    // ─── Private ─────────────────────────────────────────────────────────────

    private bool _isPlaying;
    private RectTransform _canvasRect;

    // ─── Unity ───────────────────────────────────────────────────────────────

    private void Awake()
    {
        _canvasRect = GetComponentInParent<Canvas>()?.GetComponent<RectTransform>();
        ValidateSetup();
    }

    private void Start()
    {
        if (autoPlay) StartCoroutine(IntroSequence());
    }

    private void Update()
    {
        // Debug skip in Play mode
        if (skipIntro && _isPlaying)
        {
            skipIntro = false;
            SkipToGameplay();
        }
    }

    // ─── Public API ──────────────────────────────────────────────────────────

    [ContextMenu("▶ Play Intro (Runtime)")]
    public void PlayIntro()
    {
        if (_isPlaying) return;
        StartCoroutine(IntroSequence());
    }

    public void SkipToGameplay()
    {
        StopAllCoroutines();
        _isPlaying = false;
        HideAllMarkers();
        if (uiManager != null) uiManager.ForceHideAll();
        onGameplayStart?.Invoke();
        Log("Intro SKIPPED");
    }

    // ─── Sequence ────────────────────────────────────────────────────────────

    private IEnumerator IntroSequence()
    {
        _isPlaying = true;
        Log("=== INTRO SEQUENCE START ===");
        onIntroStart?.Invoke();

        // Phase 1 — Fade in from black + show map
        if (uiManager != null)
            yield return StartCoroutine(uiManager.FadeFromBlack(fade.fadeInDuration));

        // Phase 2 — Show operation briefing overlay
        if (uiManager != null)
            yield return StartCoroutine(uiManager.ShowBriefingOverlay());

        // Phase 3 — Scan-line appear on map
        yield return StartCoroutine(ShowMapWithScanline());

        // Phase 4 — Pan/zoom the map
        if (mapPan.enabled)
            yield return StartCoroutine(PanMap());

        // Phase 5 — Stagger in objective markers
        yield return StartCoroutine(RevealMarkers());
        onMarkersShown?.Invoke();

        // Phase 6 — Hold
        Log($"Hold {fade.holdDuration}s");
        yield return new WaitForSeconds(fade.holdDuration);

        // Phase 7 — Fade to black
        if (uiManager != null)
            yield return StartCoroutine(uiManager.FadeToBlack(fade.fadeOutDuration));

        onFadeComplete?.Invoke();
        Log("=== FADE COMPLETE → Load scene via onFadeComplete event ===");

        yield return new WaitForSeconds(fade.blackHoldDuration);

        onGameplayStart?.Invoke();
        _isPlaying = false;
    }

    // ─── Phase Routines ──────────────────────────────────────────────────────

    private IEnumerator ShowMapWithScanline()
    {
        if (mapScanlineGroup == null) yield break;
        Log("Map scanline reveal");
        float elapsed = 0f;
        float dur = 0.8f;
        while (elapsed < dur)
        {
            mapScanlineGroup.alpha = Mathf.Lerp(0f, 1f, elapsed / dur);
            elapsed += Time.deltaTime;
            yield return null;
        }
        mapScanlineGroup.alpha = 1f;
    }

    private IEnumerator PanMap()
    {
        if (mapRect == null) yield break;
        Log("Map pan START");

        Vector2 startPos   = mapPan.startAnchoredPos;
        Vector2 endPos     = mapPan.endAnchoredPos;
        Vector3 startScale = Vector3.one * mapPan.startZoom;
        Vector3 endScale   = Vector3.one * mapPan.endZoom;

        float elapsed = 0f;
        while (elapsed < mapPan.panDuration)
        {
            float t = mapPan.panCurve.Evaluate(elapsed / mapPan.panDuration);
            mapRect.anchoredPosition = Vector2.Lerp(startPos, endPos, t);
            mapRect.localScale = Vector3.Lerp(startScale, endScale, t);
            elapsed += Time.deltaTime;
            yield return null;
        }

        mapRect.anchoredPosition = endPos;
        mapRect.localScale = endScale;
        Log("Map pan END");
    }

    private IEnumerator RevealMarkers()
    {
        Log($"Revealing {markers.Count} markers");

        foreach (var entry in markers)
        {
            if (entry.marker == null) continue;

            // Convert normalized map position → screen anchored position
            PositionMarkerOnMap(entry);
            entry.marker.ShowMarker();
            Log($"  → {entry.marker.objectiveName} at normalized {entry.normalizedMapPosition}");

            yield return new WaitForSeconds(markerStaggerDelay);
        }
    }

    // ─── Marker Positioning ──────────────────────────────────────────────────

    /// <summary>
    /// Places a marker UI element at the correct pixel position over the map image.
    /// normalizedMapPosition: (0,0) = bottom-left, (1,1) = top-right of map image.
    /// 
    /// NOTE: If your map image has padding/borders baked in, adjust mapImagePaddingNormalized
    /// in MapPanSettings to offset correctly.
    /// </summary>
    private void PositionMarkerOnMap(MarkerEntry entry)
    {
        if (mapRect == null || entry.marker == null) return;

        RectTransform markerRect = entry.marker.GetComponent<RectTransform>();
        if (markerRect == null) return;

        // Map rect bounds in local space
        Vector2 mapSize  = mapRect.rect.size;
        Vector2 pivot    = mapRect.pivot; // Usually (0.5, 0.5)

        // Offset from map center
        Vector2 normalizedOffset = entry.normalizedMapPosition - pivot;
        Vector2 localPos = new Vector2(
            normalizedOffset.x * mapSize.x,
            normalizedOffset.y * mapSize.y
        );

        // Apply map pan offset if map has been panned
        markerRect.SetParent(mapRect, false);
        markerRect.anchoredPosition = localPos + entry.pixelOffset;
    }

    private void HideAllMarkers()
    {
        foreach (var entry in markers)
            if (entry.marker != null) entry.marker.HideMarker();
    }

    // ─── Validation & Debug ──────────────────────────────────────────────────

    [ContextMenu("🗺 Reposition All Markers (Runtime)")]
    public void DebugRepositionMarkers()
    {
        if (!Application.isPlaying) { Debug.LogWarning("Enter Play mode first."); return; }
        foreach (var entry in markers)
            PositionMarkerOnMap(entry);
    }

    private void ValidateSetup()
    {
        if (mapRect == null)     Debug.LogWarning("[FlatMapIntro] mapRect not assigned!");
        if (uiManager == null)   Debug.LogWarning("[FlatMapIntro] uiManager not assigned!");
        if (markers.Count == 0)  Debug.LogWarning("[FlatMapIntro] No markers configured!");
    }

    private void Log(string msg)
    {
        if (debugMode) Debug.Log($"[FlatMapIntro] {msg}");
    }

    // ─── Serializable Settings ───────────────────────────────────────────────

    [Serializable]
    public class MapPanSettings
    {
        public bool enabled = true;
        [Tooltip("Starting anchored position of map RectTransform")]
        public Vector2 startAnchoredPos = new Vector2(-200f, 100f);
        [Tooltip("End anchored position (pan target)")]
        public Vector2 endAnchoredPos   = new Vector2(0f, 0f);
        [Range(0.5f, 3f)] public float startZoom = 1.3f;
        [Range(0.5f, 3f)] public float endZoom   = 1.0f;
        [Range(1f, 15f)]  public float panDuration = 5f;
        public AnimationCurve panCurve = AnimationCurve.EaseInOut(0, 0, 1, 1);
    }

    [Serializable]
    public class FadeConfig
    {
        [Range(0.2f, 3f)] public float fadeInDuration    = 1f;
        [Range(1f, 8f)]   public float holdDuration      = 3f;
        [Range(0.5f, 3f)] public float fadeOutDuration   = 1.5f;
        [Range(0f, 2f)]   public float blackHoldDuration = 0.3f;
    }

    [Serializable]
    public class MarkerEntry
    {
        public ObjectiveMarker2D marker;

        [Tooltip("Position on map texture. (0,0) = bottom-left, (1,1) = top-right")]
        [Range(0f, 1f)] public float normalizedX = 0.5f;
        [Range(0f, 1f)] public float normalizedY = 0.5f;

        [Tooltip("Fine-tune offset in pixels after normalized placement")]
        public Vector2 pixelOffset = Vector2.zero;

        public Vector2 normalizedMapPosition => new Vector2(normalizedX, normalizedY);
    }
}