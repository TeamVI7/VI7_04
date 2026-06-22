using System;
using UnityEngine;

/// <summary>
/// Drives all procedural weapon movement: idle breathing, walk bob, sway,
/// sprint tilt, ADS lerp, and full scope-overlay support.  Works exclusively
/// through localPosition / localRotation on the WeaponPivot — never touches
/// the Animator.
///
/// PER-WEAPON PROFILES:
///   Call LoadProfile(WeaponADSProfile) from WeaponSwitcherProcedural whenever
///   the active weapon changes. This drives hip/ADS pose, FOV, ADS lerp speed,
///   and sway/bob damping — so each weapon can feel distinct without touching code.
///   If no profile is assigned, falls back to this component's own Inspector
///   defaults (basePosOffset/adsBaseOffset/adsLerpSpeed etc.) so it still works
///   on weapons that don't have a profile yet.
///
/// EXECUTION ORDER:
///   This script writes to localPosition/localRotation in LateUpdate.
///   ProceduralRecoil then *adds* its offsets on top in its own LateUpdate.
///   Set Script Execution Order so ProceduralWeaponAnimator runs BEFORE
///   ProceduralRecoil, or move both to LateUpdate and rely on order here.
///
/// SCOPE OVERLAY:
///   If the currently bound WeaponData has useScopeOverlay = true, ADS
///   hides weaponMeshRoot, zooms the camera to WeaponData.scopeFov, and
///   fires OnScopeChanged so a ScopeOverlayController can show the
///   black-mask + reticle UI. Call RebindWeaponData() from
///   WeaponSwitcherProcedural whenever the active weapon changes.
///
/// EXTEND:
///   • Add more fields to WeaponADSProfile (e.g. per-weapon breath amplitude)
///     and read them here the same way adsSwayDamping/adsBobDamping are read.
///   • Hook into playerMovement.state for landing impact shake.
/// </summary>
public class ProceduralWeaponAnimator : MonoBehaviour
{
    // ─────────────────────────────────────────────────────────────────────────
    #region Inspector
    // ─────────────────────────────────────────────────────────────────────────

    [Header("References")]
    public PlayerMovement   playerMovement;
    public ProceduralRecoil recoilModule;
    public Camera           playerCamera;

    [Header("Scope")]
    [Tooltip("Root of the weapon mesh/animator to hide while scoped (e.g. WeaponRoot child). " +
             "Only used when the active WeaponData has useScopeOverlay = true.")]
    public GameObject weaponMeshRoot;

    [Header("Base Position (rest pose) — FALLBACK when no WeaponADSProfile is loaded")]
    public Vector3 basePosOffset   = new Vector3(0.15f, -0.18f, 0.35f);
    public Vector3 adsBaseOffset   = new Vector3(0f,   -0.12f, 0.25f);

    [Header("ADS — FALLBACK when no WeaponADSProfile is loaded")]
    public KeyCode adsKey          = KeyCode.Mouse1;
    [Range(1f, 20f)]
    public float adsLerpSpeed      = 10f;

    [Header("Idle Breathing")]
    public float breathAmplitudeY  = 0.002f;
    public float breathAmplitudeX  = 0.001f;
    public float breathFrequency   = 0.8f;   // cycles per second

    [Header("Walk Bob")]
    public float bobFrequencyWalk  = 7f;
    public float bobAmplitudeY     = 0.006f;
    public float bobAmplitudeX     = 0.003f;

    [Header("Sprint Bob")]
    public float bobFrequencySprint = 12f;
    public float bobAmplitudeYSprint = 0.014f;
    public float bobAmplitudeXSprint = 0.008f;

    [Header("Sway (mouse look)")]
    [Tooltip("How strongly the weapon lags behind mouse movement.")]
    public float swayAmountX       = 0.04f;   // horizontal
    public float swayAmountY       = 0.02f;   // vertical
    public float swaySmoothing     = 8f;
    public float swayMaxDelta      = 0.1f;    // clamp so fast flicks don't overshoot

    [Header("Rotational Sway")]
    public float rotSwayAmountX    = 4f;      // tilt on mouse Y
    public float rotSwayAmountY    = 2f;      // yaw on mouse X
    public float rotSwaySmoothing  = 8f;

    [Header("Sprint Tilt")]
    public float sprintTiltZ       = -5f;     // roll while sprinting
    public float sprintTiltSpeed   = 6f;

    [Header("Landing Impact")]
    [Tooltip("How hard the weapon dips when the player lands.")]
    public float landingDipAmount  = 0.06f;
    public float landingDipSpeed   = 12f;

    [Header("Debug")]
    [SerializeField] private bool showDebugLogs = false;

    #endregion

    // ─────────────────────────────────────────────────────────────────────────
    #region Events
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>Fired the frame ADS state flips. Arg: isADS.</summary>
    public event Action<bool> OnADSChanged;

    /// <summary>Fired the frame scope-overlay state flips. Arg: isScoped.</summary>
    public event Action<bool> OnScopeChanged;

    #endregion

    // ─────────────────────────────────────────────────────────────────────────
    #region Private State
    // ─────────────────────────────────────────────────────────────────────────

    private float  _bobTimer;
    private float  _breathTimer;

    private Vector3    _currentSway;
    private Quaternion _currentRotSway = Quaternion.identity;

    private float  _currentSprintTilt;
    private float  _landingDip;

    private bool   _wasGrounded;
    private bool   _isADS;
    private bool   _isScoped;
    private float  _adsBlend;   // 0 = hip, 1 = ADS

    private WeaponData       _currentWeaponData;
    private WeaponADSProfile _currentProfile;     // null = use Inspector fallback values

    public bool IsSwitching { get; set; }

    #endregion

    // ─────────────────────────────────────────────────────────────────────────
    #region Resolved Values (profile-or-fallback)
    // ─────────────────────────────────────────────────────────────────────────
    public Vector3 CurrentHipPos => ResolvedHipPos;
    private Vector3 ResolvedHipPos   => _currentProfile != null ? _currentProfile.hipPosOffset : basePosOffset;
    private Vector3 ResolvedAdsPos   => _currentProfile != null ? _currentProfile.adsPosOffset  : adsBaseOffset;
    private Vector3 ResolvedHipRot   => _currentProfile != null ? _currentProfile.hipRotOffset  : Vector3.zero;
    private Vector3 ResolvedAdsRot   => _currentProfile != null ? _currentProfile.adsRotOffset  : Vector3.zero;
    private float   ResolvedLerpSpeed=> _currentProfile != null ? _currentProfile.adsLerpSpeed  : adsLerpSpeed;
    private float   ResolvedHipFov   => _currentProfile != null ? _currentProfile.hipFOV        : 80f;
    private float   ResolvedAdsFov   => _currentProfile != null ? _currentProfile.adsFOV        : 55f;
    private float   ResolvedSwayDamp => _currentProfile != null ? _currentProfile.adsSwayDamping: 0.75f;
    private float   ResolvedBobDamp  => _currentProfile != null ? _currentProfile.adsBobDamping : 0.8f;
    private bool    ResolvedBlockSprintADS => _currentProfile == null || _currentProfile.blockSprintWhileADS;

    #endregion

    // ─────────────────────────────────────────────────────────────────────────
    #region Unity Lifecycle
    // ─────────────────────────────────────────────────────────────────────────

    private void LateUpdate()
    {
        if (playerMovement == null) return;
        if (IsSwitching) return;  // ← don't overwrite pivot during switch animation

        UpdateADS();
        UpdateLandingDip();
        UpdateSway();
        UpdateBobAndBreath();
        ApplyFinalPose();
    }

    #endregion

    // ─────────────────────────────────────────────────────────────────────────
    #region Weapon Binding
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Call from WeaponSwitcherProcedural whenever the active weapon changes,
    /// so ADS knows whether this weapon uses the scope overlay and what its
    /// scope FOV / sensitivity / reticle settings are.
    /// </summary>
    public void RebindWeaponData(WeaponData data) => _currentWeaponData = data;

    /// <summary>Current weapon's data — used by ScopeOverlayController for reticle/sensitivity.</summary>
    public WeaponData CurrentScopeData => _currentWeaponData;

    /// <summary>
    /// Loads a per-weapon ADS profile (hip/ADS pose, FOV, lerp speed, sway/bob damping,
    /// and forwards its RecoilProfile to ProceduralRecoil). Pass null to fall back to
    /// this component's own Inspector defaults.
    /// Call from WeaponSwitcherProcedural.GetProfile() result on every weapon switch.
    /// </summary>
    public void LoadProfile(WeaponADSProfile profile)
    {
        _currentProfile = profile;

        if (profile != null && profile.recoilProfile != null)
            recoilModule?.ApplyProfile(profile.recoilProfile);

        Log(profile != null
            ? $"Loaded ADS profile '{profile.name}'."
            : "No ADS profile — using Inspector fallback values.");
    }

    #endregion

    // ─────────────────────────────────────────────────────────────────────────
    #region ADS
    // ─────────────────────────────────────────────────────────────────────────

    private void UpdateADS()
    {
        bool wasADS = _isADS;

        bool blockedBySprint = ResolvedBlockSprintADS && IsSprintingNow();
        _isADS = Input.GetKey(adsKey) && CanADS() && !blockedBySprint;

        if (_isADS != wasADS)
        {
            OnADSChanged?.Invoke(_isADS);
            Log($"ADS: {wasADS} → {_isADS}");
        }

        float targetBlend = _isADS ? 1f : 0f;
        _adsBlend = Mathf.Lerp(_adsBlend, targetBlend, Time.deltaTime * ResolvedLerpSpeed);

        recoilModule?.SetADS(_isADS);

        // ── Scope overlay branch ────────────────────────────────────────────
        bool wantsScope = _isADS && _currentWeaponData != null && _currentWeaponData.useScopeOverlay;

        if (wantsScope != _isScoped)
        {
            _isScoped = wantsScope;

            if (weaponMeshRoot != null)
                weaponMeshRoot.SetActive(!_isScoped);

            OnScopeChanged?.Invoke(_isScoped);
            Log($"Scope: {_isScoped}");
        }

        if (playerCamera == null) return;

        if (_isScoped)
        {
            float scopeFov  = _currentWeaponData.scopeFov;
            float zoomSpeed = _currentWeaponData.scopeZoomSpeed > 0f
                             ? _currentWeaponData.scopeZoomSpeed
                             : ResolvedLerpSpeed;

            playerCamera.fieldOfView = Mathf.Lerp(
                playerCamera.fieldOfView, scopeFov, Time.deltaTime * zoomSpeed);
        }
        else
        {
            // Normal hip ↔ ADS FOV blend — now profile-driven (falls back to 80/55 if no profile).
            float targetFov = _isADS ? ResolvedAdsFov : ResolvedHipFov;
            playerCamera.fieldOfView = Mathf.Lerp(
                playerCamera.fieldOfView, targetFov, Time.deltaTime * ResolvedLerpSpeed);
        }
    }

    private bool CanADS()
    {
        if (playerMovement == null) return true;
        // Block ADS while dashing or mid-air with low control
        return playerMovement.state != PlayerMovement.MovementState.dashing;
    }

    private bool IsSprintingNow() =>
        playerMovement.state == PlayerMovement.MovementState.sprinting;

    #endregion

    // ─────────────────────────────────────────────────────────────────────────
    #region Landing Dip
    // ─────────────────────────────────────────────────────────────────────────

    private void UpdateLandingDip()
    {
        bool grounded = playerMovement.grounded;

        if (!_wasGrounded && grounded)
        {
            // Just landed — kick a dip downward
            _landingDip = -landingDipAmount;
            Log("Landing dip triggered.");
        }

        _landingDip = Mathf.Lerp(_landingDip, 0f, Time.deltaTime * landingDipSpeed);
        _wasGrounded = grounded;
    }

    #endregion

    // ─────────────────────────────────────────────────────────────────────────
    #region Sway
    // ─────────────────────────────────────────────────────────────────────────

    private void UpdateSway()
    {
        float mouseX = Input.GetAxisRaw("Mouse X");
        float mouseY = Input.GetAxisRaw("Mouse Y");

        // Clamp raw deltas so heavy mouse throws don't look broken
        mouseX = Mathf.Clamp(mouseX, -swayMaxDelta, swayMaxDelta);
        mouseY = Mathf.Clamp(mouseY, -swayMaxDelta, swayMaxDelta);

        // Profile-driven sway damping while ADS (0 = full sway, 1 = none) replaces the old hardcoded 0.25.
        float adsScale = Mathf.Lerp(1f, 1f - ResolvedSwayDamp, _adsBlend);

        // Positional sway — weapon lags behind look direction
        Vector3 targetSway = new Vector3(-mouseX * swayAmountX, -mouseY * swayAmountY, 0f) * adsScale;
        _currentSway = Vector3.Lerp(_currentSway, targetSway, Time.deltaTime * swaySmoothing);

        // Rotational sway — weapon tilts on mouse look
        Quaternion targetRotSway = Quaternion.Euler(
             mouseY * rotSwayAmountX * adsScale,
             mouseX * rotSwayAmountY * adsScale,
             0f);
        _currentRotSway = Quaternion.Slerp(_currentRotSway, targetRotSway,
                                            Time.deltaTime * rotSwaySmoothing);
    }

    #endregion

    // ─────────────────────────────────────────────────────────────────────────
    #region Bob & Breath
    // ─────────────────────────────────────────────────────────────────────────

    private void UpdateBobAndBreath()
    {
        _breathTimer += Time.deltaTime * breathFrequency * Mathf.PI * 2f;

        bool isSprinting = IsSprintingNow();

        // Select bob parameters based on movement state
        float bobFreq, bobAmpY, bobAmpX;

        switch (playerMovement.state)
        {
            case PlayerMovement.MovementState.walking:
            case PlayerMovement.MovementState.sprinting:
                bobFreq = isSprinting ? bobFrequencySprint : bobFrequencyWalk;
                bobAmpY = isSprinting ? bobAmplitudeYSprint : bobAmplitudeY;
                bobAmpX = isSprinting ? bobAmplitudeXSprint : bobAmplitudeX;
                break;

            default:
                bobFreq = bobFrequencyWalk;
                bobAmpY = 0f;
                bobAmpX = 0f;
                break;
        }

        bool isMoving = playerMovement.state == PlayerMovement.MovementState.walking
                     || playerMovement.state == PlayerMovement.MovementState.sprinting;

        if (isMoving && playerMovement.grounded)
        {
            _bobTimer += Time.deltaTime * bobFreq;
        }
        else
        {
            // Smooth bob to zero when not walking
            _bobTimer += Time.deltaTime * bobFreq * 0.1f;
        }

        // Sprint tilt
        float targetTilt = (isSprinting && !_isADS) ? sprintTiltZ : 0f;
        _currentSprintTilt = Mathf.Lerp(_currentSprintTilt, targetTilt,
                                         Time.deltaTime * sprintTiltSpeed);
    }

    #endregion

    // ─────────────────────────────────────────────────────────────────────────
    #region Final Pose
    // ─────────────────────────────────────────────────────────────────────────

    private void ApplyFinalPose()
    {
        // Profile-driven bob damping while ADS (0 = full bob, 1 = none) replaces the old hardcoded 0.2.
        float scaleADS = Mathf.Lerp(1f, 1f - ResolvedBobDamp, _adsBlend);

        bool isSprinting = IsSprintingNow();
        bool isMoving    = (playerMovement.state == PlayerMovement.MovementState.walking
                          || playerMovement.state == PlayerMovement.MovementState.sprinting)
                         && playerMovement.grounded;

        float bobY = Mathf.Sin(_bobTimer)        * (isMoving ? (isSprinting ? bobAmplitudeYSprint : bobAmplitudeY) : 0f) * scaleADS;
        float bobX = Mathf.Sin(_bobTimer * 0.5f) * (isMoving ? (isSprinting ? bobAmplitudeXSprint : bobAmplitudeX) : 0f) * scaleADS;
        float breathY = Mathf.Sin(_breathTimer)  * breathAmplitudeY * scaleADS;
        float breathX = Mathf.Cos(_breathTimer * 0.6f) * breathAmplitudeX * scaleADS;

        // ── Position ──────────────────────────────────────────────────────
        Vector3 basePos  = Vector3.Lerp(ResolvedHipPos, ResolvedAdsPos, _adsBlend);
        Vector3 dynamicP = new Vector3(
            bobX + breathX + _currentSway.x,
            bobY + breathY + _currentSway.y + _landingDip,
            _currentSway.z
        );
        transform.localPosition = basePos + dynamicP;

        // ── Rotation ──────────────────────────────────────────────────────
        Vector3    baseRotEuler = Vector3.Lerp(ResolvedHipRot, ResolvedAdsRot, _adsBlend);
        Quaternion baseRot      = Quaternion.Euler(baseRotEuler);
        Quaternion tiltRot      = Quaternion.Euler(0f, 0f, _currentSprintTilt);
        transform.localRotation = baseRot * _currentRotSway * tiltRot;
    }

    #endregion

    // ─────────────────────────────────────────────────────────────────────────
    #region Public API
    // ─────────────────────────────────────────────────────────────────────────

    /// <returns>True if ADS is currently active.</returns>
    public bool IsADS => _isADS;

    /// <returns>True if the current weapon's scope overlay is currently active.</returns>
    public bool IsScoped => _isScoped;

    /// <summary>Force-snap back to hip pose instantly (call after weapon switch).</summary>
    public void SnapToHip()
    {
        _adsBlend       = 0f;
        _currentSway    = Vector3.zero;
        _currentRotSway = Quaternion.identity;
        _isADS          = false;

        if (_isScoped)
        {
            _isScoped = false;
            if (weaponMeshRoot != null) weaponMeshRoot.SetActive(true);
            OnScopeChanged?.Invoke(false);
        }
    }

    #endregion

    // ─────────────────────────────────────────────────────────────────────────
    #region Debug
    // ─────────────────────────────────────────────────────────────────────────

    [System.Diagnostics.Conditional("UNITY_EDITOR")]
    private void Log(string msg)
    {
        if (showDebugLogs) Debug.Log($"[ProceduralWeaponAnimator] {msg}", this);
    }

    #endregion
}