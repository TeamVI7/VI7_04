using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Hides/shows the crosshair based on ADS state.
///
/// SETUP:
///   1. Attach to the crosshair GameObject (or any persistent GO).
///   2. Assign crosshairRoot — the parent CanvasGroup or Image to toggle.
///   3. Assign proceduralAnimator.
///
/// EXTEND:
///   - Swap crosshairRoot for a CanvasGroup to get alpha fade instead of snap.
///   - Add per-weapon crosshair swapping via OnSwitchComplete on WeaponSwitcherProcedural.
///
/// DEBUG:
///   - Enable showDebugLogs to trace ADS transitions.
/// </summary>
public class CrosshairController : MonoBehaviour
{
    // ─────────────────────────────────────────────────────────────────────────
    #region Inspector
    // ─────────────────────────────────────────────────────────────────────────

    [Header("References")]
    public ProceduralWeaponAnimator proceduralAnimator;

    [Header("Crosshair")]
    [Tooltip("Assign the crosshair CanvasGroup for fade support, or the root GameObject.")]
    public CanvasGroup crosshairGroup;

    [Header("Fade")]
    public bool  useFade       = true;
    public float fadeInSpeed   = 10f;
    public float fadeOutSpeed  = 14f;

    [Header("Debug")]
    [SerializeField] private bool showDebugLogs = false;

    #endregion

    // ─────────────────────────────────────────────────────────────────────────
    #region Private State
    // ─────────────────────────────────────────────────────────────────────────

    private float _targetAlpha = 1f;

    #endregion

    // ─────────────────────────────────────────────────────────────────────────
    #region Unity Lifecycle
    // ─────────────────────────────────────────────────────────────────────────

    private void Start()
    {
        if (proceduralAnimator == null)
        {
            FPSDebug.LogWarning("CrosshairController: proceduralAnimator not assigned.", this);
            return;
        }

        proceduralAnimator.OnADSChanged += HandleADSChanged;

        if (crosshairGroup != null)
            crosshairGroup.alpha = 1f;
    }

    private void OnDestroy()
    {
        if (proceduralAnimator != null)
            proceduralAnimator.OnADSChanged -= HandleADSChanged;
    }

    private void Update()
    {
        if (!useFade || crosshairGroup == null) return;

        float speed = _targetAlpha > crosshairGroup.alpha ? fadeInSpeed : fadeOutSpeed;
        crosshairGroup.alpha = Mathf.MoveTowards(
            crosshairGroup.alpha, _targetAlpha, speed * Time.deltaTime);
    }

    #endregion

    // ─────────────────────────────────────────────────────────────────────────
    #region Event Handlers
    // ─────────────────────────────────────────────────────────────────────────

    private void HandleADSChanged(bool isADS)
    {
        _targetAlpha = isADS ? 0f : 1f;

        // Snap instantly if fade disabled
        if (!useFade && crosshairGroup != null)
            crosshairGroup.alpha = _targetAlpha;

        Log($"ADS={isADS} → crosshair alpha target={_targetAlpha}");
    }

    #endregion

    // ─────────────────────────────────────────────────────────────────────────
    #region Debug
    // ─────────────────────────────────────────────────────────────────────────

    [System.Diagnostics.Conditional("UNITY_EDITOR")]
    private void Log(string msg)
    {
        if (showDebugLogs) Debug.Log($"[CrosshairController] {msg}", this);
    }

    #endregion
}