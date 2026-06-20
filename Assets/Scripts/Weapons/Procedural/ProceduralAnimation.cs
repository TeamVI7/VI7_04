using UnityEngine;
using System;
/// <summary>
/// Drives all procedural weapon movement: idle breathing, walk bob, sway,
/// sprint tilt, and ADS lerp. Works exclusively through localPosition /
/// localRotation on the WeaponPivot — never touches the Animator.
///
/// EXECUTION ORDER:
///   This script writes to localPosition/localRotation in LateUpdate.
///   ProceduralRecoil then *adds* its offsets on top in its own LateUpdate.
///   Set Script Execution Order so ProceduralWeaponAnimator runs BEFORE ProceduralRecoil.
///
/// EXTEND:
///   - Add a WeaponADSProfile ScriptableObject per weapon, swap via LoadProfile() on switch.
///   - Subscribe to playerMovement.OnLanded for landing shake variations.
///   - Add a WeaponProceduralProfile SO for per-weapon bob/sway feel.
///
/// DEBUG:
///   - Enable showDebugLogs in Inspector for state logs.
///   - _wallContactBlend visible in debugger — watch it during wall run to confirm blend.
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

    [Header("ADS Profile")]
    [Tooltip("Active per-weapon profile. Swap via LoadProfile() when switching weapons.")]
    public WeaponADSProfile adsProfile;

    [Header("ADS Input")]
    public KeyCode adsKey = KeyCode.Mouse1;

    [Header("Fallback Pose (used when no profile assigned)")]
    public Vector3 fallbackHipPos  = new Vector3(0.15f, -0.18f, 0.35f);
    public Vector3 fallbackADSPos  = new Vector3(0f, -0.12f, 0.25f);
    [Range(1f, 25f)]
    public float   fallbackADSLerp = 10f;

    [Header("Idle Breathing")]
    public float breathAmplitudeY = 0.002f;
    public float breathAmplitudeX = 0.001f;
    public float breathFrequency  = 0.8f;

    [Header("Walk Bob")]
    public float bobFrequencyWalk  = 7f;
    public float bobAmplitudeY     = 0.006f;
    public float bobAmplitudeX     = 0.003f;

    [Header("Sprint Bob")]
    public float bobFrequencySprint  = 12f;
    public float bobAmplitudeYSprint = 0.014f;
    public float bobAmplitudeXSprint = 0.008f;

    [Header("Sway (mouse look)")]
    public float swayAmountX   = 0.04f;
    public float swayAmountY   = 0.02f;
    public float swaySmoothing = 8f;
    public float swayMaxDelta  = 0.1f;

    [Header("Rotational Sway")]
    public float rotSwayAmountX   = 4f;
    public float rotSwayAmountY   = 2f;
    public float rotSwaySmoothing = 8f;

    [Header("Sprint Tilt")]
    public float sprintTiltZ    = -5f;
    public float sprintTiltSpeed = 6f;

    [Header("Landing Impact")]
    public float landingDipAmount = 0.06f;
    public float landingDipSpeed  = 12f;

    [Header("Wall Contact IK Suppression")]
    [Tooltip("Speed at which bob/sway blends out when pressing a wall. Higher = snappier IK.")]
    public float wallContactBlendSpeed = 10f;

    [Header("Debug")]
    [SerializeField] private bool showDebugLogs = false;

    #endregion

    // ─────────────────────────────────────────────────────────────────────────
    #region Public State
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>True while WeaponSwitcher is animating a swap — suppresses all pose writes.</summary>
    public bool IsSwitching { get; set; }

    /// <summary>True if ADS is currently active.</summary>
    public bool IsADS => _isADS;

    /// <summary>
    /// Current hip-fire localPosition target — reads from active profile or fallback.
    /// Used by WeaponSwitcherProcedural to know where to animate the rise-in toward.
    /// </summary>
    public Vector3 ActiveHipPos =>
        adsProfile != null ? adsProfile.hipPosOffset : fallbackHipPos;

    #endregion

    // ─────────────────────────────────────────────────────────────────────────
    #region Private State
    // ─────────────────────────────────────────────────────────────────────────

    private float      _bobTimer;
    private float      _breathTimer;
    private float      _wallContactBlend; // 0 = full bob/sway, 1 = static rest pose

    private Vector3    _currentSway;
    private Quaternion _currentRotSway = Quaternion.identity;

    private float  _currentSprintTilt;
    private float  _landingDip;

    private bool   _wasGrounded;
    private bool   _isADS;
    private float  _adsBlend; // 0 = hip, 1 = ADS
    public event Action<bool> OnADSChanged;
    #endregion

    // ─────────────────────────────────────────────────────────────────────────
    #region Unity Lifecycle
    // ─────────────────────────────────────────────────────────────────────────

    private void LateUpdate()
    {
        if (playerMovement == null) return;
        if (IsSwitching) return;

        // Wall contact: blend weapon to static rest pose so WallHandIK has a stable base.
        // Without this, bob/sway displacement fights the IK arm reach.
        bool wallContact = playerMovement.wallrunning
                        || playerMovement.wallSliding
                        || playerMovement.sliding;

        _wallContactBlend = Mathf.MoveTowards(
            _wallContactBlend,
            wallContact ? 1f : 0f,
            Time.deltaTime * wallContactBlendSpeed);

        // Always tick sub-systems so state stays accurate (FOV, grounded, etc.)
        UpdateADS();
        UpdateLandingDip();
        UpdateSway();
        UpdateBobAndBreath();
        ApplyFinalPose();

        // Post-pose: override toward static rest when wall contact blend is active.
        // Applied AFTER ApplyFinalPose so it overrides the full dynamic pose.
        if (_wallContactBlend > 0f)
        {
            Vector3 hipPos    = adsProfile != null ? adsProfile.hipPosOffset : fallbackHipPos;
            Vector3 adsPos    = adsProfile != null ? adsProfile.adsPosOffset : fallbackADSPos;
            Vector3 staticPos = Vector3.Lerp(hipPos, adsPos, _adsBlend);

            transform.localPosition = Vector3.Lerp(
                transform.localPosition, staticPos, _wallContactBlend);

            transform.localRotation = Quaternion.Slerp(
                transform.localRotation, Quaternion.identity, _wallContactBlend);
        }
    }

    #endregion

    // ─────────────────────────────────────────────────────────────────────────
    #region ADS
    // ─────────────────────────────────────────────────────────────────────────

    private void UpdateADS()
    {
        bool wasADS = _isADS;
        _isADS = Input.GetKey(adsKey) && CanADS();
        
        if (_isADS != wasADS)
            OnADSChanged?.Invoke(_isADS);
        bool wantADS = Input.GetKey(adsKey) && CanADS();
        _isADS = wantADS;

        float targetBlend = _isADS ? 1f : 0f;
        float speed       = adsProfile != null ? adsProfile.adsLerpSpeed : fallbackADSLerp;
        _adsBlend = Mathf.Lerp(_adsBlend, targetBlend, Time.deltaTime * speed);

        recoilModule?.SetADS(_isADS);

        if (playerCamera != null)
        {
            float hipFOV    = adsProfile != null ? adsProfile.hipFOV : 80f;
            float adsFOV    = adsProfile != null ? adsProfile.adsFOV : 55f;
            float targetFov = Mathf.Lerp(hipFOV, adsFOV, _adsBlend);
            playerCamera.fieldOfView = Mathf.Lerp(
                playerCamera.fieldOfView, targetFov, Time.deltaTime * speed);
        }
    }

    private bool CanADS()
    {
        if (playerMovement == null) return true;
        if (playerMovement.state == PlayerMovement.MovementState.dashing) return false;

        bool blockSprint = adsProfile == null || adsProfile.blockSprintWhileADS;
        if (blockSprint && playerMovement.state == PlayerMovement.MovementState.sprinting) return false;

        return true;
    }

    #endregion

    // ─────────────────────────────────────────────────────────────────────────
    #region Landing Dip
    // ─────────────────────────────────────────────────────────────────────────

    private void UpdateLandingDip()
    {
        bool grounded = playerMovement.grounded;

        if (!_wasGrounded && grounded)
        {
            _landingDip = -landingDipAmount;
            Log("Landing dip triggered.");
        }

        _landingDip  = Mathf.Lerp(_landingDip, 0f, Time.deltaTime * landingDipSpeed);
        _wasGrounded = grounded;
    }

    #endregion

    // ─────────────────────────────────────────────────────────────────────────
    #region Sway
    // ─────────────────────────────────────────────────────────────────────────

    private void UpdateSway()
    {

        float mouseX = Mathf.Clamp(Input.GetAxisRaw("Mouse X"), -swayMaxDelta, swayMaxDelta);
        float mouseY = Mathf.Clamp(Input.GetAxisRaw("Mouse Y"), -swayMaxDelta, swayMaxDelta);

        float swayDamp = adsProfile != null ? adsProfile.adsSwayDamping : 0.75f;
        float adsScale = Mathf.Lerp(1f, 1f - swayDamp, _adsBlend);

        Vector3 targetSway = new Vector3(
            -mouseX * swayAmountX,
            -mouseY * swayAmountY,
            0f) * adsScale;

        _currentSway = Vector3.Lerp(_currentSway, targetSway, Time.deltaTime * swaySmoothing);

        Quaternion targetRotSway = Quaternion.Euler(
             mouseY * rotSwayAmountX * adsScale,
             mouseX * rotSwayAmountY * adsScale,
             0f);

        _currentRotSway = Quaternion.Slerp(
            _currentRotSway, targetRotSway, Time.deltaTime * rotSwaySmoothing);
    }

    #endregion

    // ─────────────────────────────────────────────────────────────────────────
    #region Bob & Breath
    // ─────────────────────────────────────────────────────────────────────────

    private void UpdateBobAndBreath()
    {
        _breathTimer += Time.deltaTime * breathFrequency * Mathf.PI * 2f;

        float bobFreq, bobAmpY, bobAmpX;

        switch (playerMovement.state)
        {
            case PlayerMovement.MovementState.sprinting:
                bobFreq = bobFrequencySprint;
                bobAmpY = bobAmplitudeYSprint;
                bobAmpX = bobAmplitudeXSprint;
                break;
            case PlayerMovement.MovementState.walking:
                bobFreq = bobFrequencyWalk;
                bobAmpY = bobAmplitudeY;
                bobAmpX = bobAmplitudeX;
                break;
            default:
                bobFreq = bobFrequencyWalk;
                bobAmpY = 0f;
                bobAmpX = 0f;
                break;
        }

        bool isMoving = (playerMovement.state == PlayerMovement.MovementState.walking
                      || playerMovement.state == PlayerMovement.MovementState.sprinting)
                      && playerMovement.grounded;

        _bobTimer += Time.deltaTime * bobFreq * (isMoving ? 1f : 0.1f);

        bool isSprinting = playerMovement.state == PlayerMovement.MovementState.sprinting;
        float targetTilt = (isSprinting && !_isADS) ? sprintTiltZ : 0f;
        _currentSprintTilt = Mathf.Lerp(_currentSprintTilt, targetTilt, Time.deltaTime * sprintTiltSpeed);
    }

    #endregion

    // ─────────────────────────────────────────────────────────────────────────
    #region Final Pose
    // ─────────────────────────────────────────────────────────────────────────

    private void ApplyFinalPose()
    {
        float bobDamp  = adsProfile != null ? adsProfile.adsBobDamping : 0.8f;
        float scaleADS = Mathf.Lerp(1f, 1f - bobDamp, _adsBlend);

        bool isSprinting = playerMovement.state == PlayerMovement.MovementState.sprinting;
        bool isMoving    = (playerMovement.state == PlayerMovement.MovementState.walking
                         || isSprinting)
                        && playerMovement.grounded;

        float bobY    = Mathf.Sin(_bobTimer)       * (isMoving ? (isSprinting ? bobAmplitudeYSprint : bobAmplitudeY) : 0f) * scaleADS;
        float bobX    = Mathf.Sin(_bobTimer * 0.5f)* (isMoving ? (isSprinting ? bobAmplitudeXSprint : bobAmplitudeX) : 0f) * scaleADS;
        float breathY = Mathf.Sin(_breathTimer)             * breathAmplitudeY * scaleADS;
        float breathX = Mathf.Cos(_breathTimer * 0.6f)      * breathAmplitudeX * scaleADS;

        // ── Position ──────────────────────────────────────────────────────
        Vector3 hipPos  = adsProfile != null ? adsProfile.hipPosOffset : fallbackHipPos;
        Vector3 adsPos  = adsProfile != null ? adsProfile.adsPosOffset : fallbackADSPos;
        Vector3 basePos = Vector3.Lerp(hipPos, adsPos, _adsBlend);

        transform.localPosition = basePos + new Vector3(
            bobX + breathX + _currentSway.x,
            bobY + breathY + _currentSway.y + _landingDip,
            _currentSway.z);

        // ── Rotation ──────────────────────────────────────────────────────
        Vector3    hipRot  = adsProfile != null ? adsProfile.hipRotOffset : Vector3.zero;
        Vector3    adsRot  = adsProfile != null ? adsProfile.adsRotOffset : Vector3.zero;
        Quaternion baseRot = Quaternion.Euler(Vector3.Lerp(hipRot, adsRot, _adsBlend));
        Quaternion tiltRot = Quaternion.Euler(0f, 0f, _currentSprintTilt);

        transform.localRotation = _currentRotSway * baseRot * tiltRot;
    }

    #endregion

    // ─────────────────────────────────────────────────────────────────────────
    #region Public API
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Swap to a new per-weapon ADS profile.
    /// Call from WeaponSwitcherProcedural after incoming weapon is active.
    /// Pass null to fall back to hardcoded fallback pose.
    /// </summary>
    public void LoadProfile(WeaponADSProfile profile)
    {
        adsProfile = profile;
        Log($"Loaded ADS profile: {(profile != null ? profile.name : "null (fallback)")}");
    }

    /// <summary>Force-snap to hip pose instantly. Call after weapon switch.</summary>
    public void SnapToHip()
    {
        _adsBlend          = 0f;
        _currentSway       = Vector3.zero;
        _currentRotSway    = Quaternion.identity;
        _isADS             = false;
        _wallContactBlend  = 0f;

        transform.localPosition = adsProfile != null ? adsProfile.hipPosOffset : fallbackHipPos;
        transform.localRotation = Quaternion.identity;
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