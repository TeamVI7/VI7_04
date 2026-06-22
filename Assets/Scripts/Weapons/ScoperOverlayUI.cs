using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Drives the full-screen scope overlay (black mask + circular cutout + reticle)
/// and scales mouse sensitivity while a scope-flagged weapon is aiming down sights.
///
/// SETUP:
///   1. Build a UI Canvas (Screen Space - Overlay) with:
///        - maskImage: full-screen black Image with a circular cutout
///          (use a sprite with alpha-cut circle, or a shader/RawImage mask).
///        - reticleImage: small centered Image, swapped per-weapon via WeaponData.scopeReticle.
///   2. Wrap both under one CanvasGroup (scopeCanvasGroup) so they fade together.
///   3. Assign proceduralAnimator and playerCam in the Inspector.
///
/// EXTEND:
///   - Add scope-in/out audio via OnScopeChanged.
///   - Add chromatic aberration / DoF post-processing toggle here too.
///   - Support multiple mask sprites per weapon (e.g. different scope shapes) by
///     adding a Sprite field to WeaponData and swapping maskImage.sprite on bind.
///
/// DEBUG:
///   - Enable showDebugLogs to trace scope enter/exit and sensitivity changes.
/// </summary>
public class ScopeOverlayController : MonoBehaviour
{
    // ─────────────────────────────────────────────────────────────────────────
    #region Inspector
    // ─────────────────────────────────────────────────────────────────────────

    [Header("References")]
    public ProceduralWeaponAnimator proceduralAnimator;
    public PlayerCam                playerCam;

    [Header("UI")]
    [Tooltip("Parent CanvasGroup wrapping the mask + reticle — toggled/faded as one unit.")]
    public CanvasGroup scopeCanvasGroup;
    [Tooltip("Full-screen black image with a circular cutout (alpha or shader-based).")]
    public Image maskImage;
    [Tooltip("Small centered reticle image — sprite swapped per weapon.")]
    public Image reticleImage;

    [Header("Fade")]
    public float fadeInSpeed  = 14f;
    public float fadeOutSpeed = 18f;

    [Header("Sensitivity")]
    [Tooltip("Cached base sensitivity is captured on Start — don't change PlayerCam.sensX/Y at runtime elsewhere while scoped.")]
    public bool scaleSensitivity = true;

    [Header("Debug")]
    [SerializeField] private bool showDebugLogs = false;

    #endregion

    // ─────────────────────────────────────────────────────────────────────────
    #region Private State
    // ─────────────────────────────────────────────────────────────────────────

    private float _targetAlpha;
    private float _baseSensX;
    private float _baseSensY;
    private bool  _scoped;

    #endregion

    // ─────────────────────────────────────────────────────────────────────────
    #region Unity Lifecycle
    // ─────────────────────────────────────────────────────────────────────────

    private void Start()
    {
        if (proceduralAnimator == null)
        {
            FPSDebug.LogWarning("ScopeOverlayController: proceduralAnimator not assigned.", this);
            return;
        }

        proceduralAnimator.OnScopeChanged += HandleScopeChanged;

        if (playerCam != null)
        {
            _baseSensX = playerCam.sensX;
            _baseSensY = playerCam.sensY;
        }

        if (scopeCanvasGroup != null)
        {
            scopeCanvasGroup.alpha          = 0f;
            scopeCanvasGroup.blocksRaycasts = false;
            scopeCanvasGroup.interactable   = false;
        }
    }

    private void OnDestroy()
    {
        if (proceduralAnimator != null)
            proceduralAnimator.OnScopeChanged -= HandleScopeChanged;

        RestoreSensitivity();
    }

    private void Update()
    {
        if (scopeCanvasGroup == null) return;

        scopeCanvasGroup.alpha = Mathf.MoveTowards(
            scopeCanvasGroup.alpha, _targetAlpha,
            (_targetAlpha > scopeCanvasGroup.alpha ? fadeInSpeed : fadeOutSpeed) * Time.deltaTime);
    }

    #endregion

    // ─────────────────────────────────────────────────────────────────────────
    #region Event Handlers
    // ─────────────────────────────────────────────────────────────────────────

    private void HandleScopeChanged(bool scoped)
    {
        _scoped       = scoped;
        _targetAlpha  = scoped ? 1f : 0f;

        if (scopeCanvasGroup != null)
            scopeCanvasGroup.blocksRaycasts = scoped;

        if (scoped) ApplyReticle();
        if (scaleSensitivity) ApplySensitivity(scoped);

        Log($"Scope {(scoped ? "ENTER" : "EXIT")}");
    }

    #endregion

    // ─────────────────────────────────────────────────────────────────────────
    #region Reticle
    // ─────────────────────────────────────────────────────────────────────────

    private void ApplyReticle()
    {
        if (reticleImage == null) return;

        var data = CurrentWeaponData();
        if (data != null && data.scopeReticle != null)
        {
            reticleImage.sprite = data.scopeReticle;
            reticleImage.enabled = true;
        }
        else
        {
            reticleImage.enabled = false;
        }
    }

    private WeaponData CurrentWeaponData()
    {
        // ProceduralWeaponAnimator tracks this internally via RebindWeaponData;
        // expose a getter there if you need it elsewhere too.
        return proceduralAnimator != null ? proceduralAnimator.CurrentScopeData : null;
    }

    #endregion

    // ─────────────────────────────────────────────────────────────────────────
    #region Sensitivity
    // ─────────────────────────────────────────────────────────────────────────

    private void ApplySensitivity(bool scoped)
    {
        if (playerCam == null) return;

        var data = CurrentWeaponData();
        float mult = (scoped && data != null) ? data.scopeSensitivityMultiplier : 1f;

        playerCam.sensX = _baseSensX * mult;
        playerCam.sensY = _baseSensY * mult;
    }

    private void RestoreSensitivity()
    {
        if (playerCam == null) return;
        playerCam.sensX = _baseSensX;
        playerCam.sensY = _baseSensY;
    }

    #endregion

    // ─────────────────────────────────────────────────────────────────────────
    #region Debug
    // ─────────────────────────────────────────────────────────────────────────

    [System.Diagnostics.Conditional("UNITY_EDITOR")]
    private void Log(string msg)
    {
        if (showDebugLogs) Debug.Log($"[ScopeOverlayController] {msg}", this);
    }

    #endregion
}