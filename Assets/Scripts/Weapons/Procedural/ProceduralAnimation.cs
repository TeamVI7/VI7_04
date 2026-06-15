using UnityEngine;

/// <summary>
/// Drives all procedural weapon movement: idle breathing, walk bob, sway,
/// sprint tilt, and ADS lerp.  Works exclusively through localPosition /
/// localRotation on the WeaponPivot — never touches the Animator.
///
/// EXECUTION ORDER:
///   This script writes to localPosition/localRotation in LateUpdate.
///   ProceduralRecoil then *adds* its offsets on top in its own LateUpdate.
///   Set Script Execution Order so ProceduralWeaponAnimator runs BEFORE
///   ProceduralRecoil, or move both to LateUpdate and rely on order here.
///
/// EXTEND:
///   • Add a WeaponProceduralProfile ScriptableObject per weapon and swap
///     it from WeaponSwitcherProcedural to get per-weapon feel.
///   • Hook into playerMovement.state for landing impact shake.
/// </summary>
public class ProceduralWeaponAnimator : MonoBehaviour
{
    // ─────────────────────────────────────────────────────────────────────────
    #region Inspector
    // ─────────────────────────────────────────────────────────────────────────

    [Header("References")]
    public PlayerMovement    playerMovement;
    public ProceduralRecoil  recoilModule;
    public Camera            playerCamera;

    [Header("ADS Profile")]
    [Tooltip("Active per-weapon profile. Swap via LoadProfile() when switching weapons.")]
    public WeaponADSProfile  adsProfile;

    [Header("ADS Input")]
    public KeyCode adsKey           = KeyCode.Mouse1;

    [Header("Fallback Pose (used when no profile is assigned)")]
    public Vector3 fallbackHipPos   = new Vector3(0.15f, -0.18f, 0.35f);
    public Vector3 fallbackADSPos   = new Vector3(0f,   -0.12f, 0.25f);
    [Range(1f, 25f)]
    public float   fallbackADSLerp  = 10f;

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
    private float  _adsBlend;   // 0 = hip, 1 = ADS
    public bool IsSwitching { get; set; }
    #endregion

    // ─────────────────────────────────────────────────────────────────────────
    #region Unity Lifecycle
    // ─────────────────────────────────────────────────────────────────────────
    // Add this property


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
    #region ADS
    // ─────────────────────────────────────────────────────────────────────────

    private void UpdateADS()
    {
        bool wantADS = Input.GetKey(adsKey) && CanADS();
        _isADS = wantADS;

        float targetBlend = _isADS ? 1f : 0f;
        float speed       = adsProfile != null ? adsProfile.adsLerpSpeed : fallbackADSLerp;
        _adsBlend = Mathf.Lerp(_adsBlend, targetBlend, Time.deltaTime * speed);

        recoilModule?.SetADS(_isADS);

        // Per-weapon FOV
        if (playerCamera != null)
        {
            float hipFOV = adsProfile != null ? adsProfile.hipFOV : 80f;
            float adsFOV = adsProfile != null ? adsProfile.adsFOV : 55f;
            float targetFov = Mathf.Lerp(hipFOV, adsFOV, _adsBlend);
            playerCamera.fieldOfView = Mathf.Lerp(playerCamera.fieldOfView, targetFov,
                                                   Time.deltaTime * speed);
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

        mouseX = Mathf.Clamp(mouseX, -swayMaxDelta, swayMaxDelta);
        mouseY = Mathf.Clamp(mouseY, -swayMaxDelta, swayMaxDelta);

        float swayDamp = adsProfile != null ? adsProfile.adsSwayDamping : 0.75f;
        float adsScale = Mathf.Lerp(1f, 1f - swayDamp, _adsBlend);

        Vector3 targetSway = new Vector3(-mouseX * swayAmountX, -mouseY * swayAmountY, 0f) * adsScale;
        _currentSway = Vector3.Lerp(_currentSway, targetSway, Time.deltaTime * swaySmoothing);

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

        bool isSprinting = playerMovement.state == PlayerMovement.MovementState.sprinting;

        float bobFreq, bobAmpY, bobAmpX;

        switch (playerMovement.state)
        {
            case PlayerMovement.MovementState.walking:
                bobFreq = bobFrequencyWalk;
                bobAmpY = bobAmplitudeY;
                bobAmpX = bobAmplitudeX;
                break;
            case PlayerMovement.MovementState.sprinting:
                bobFreq = bobFrequencySprint;
                bobAmpY = bobAmplitudeYSprint;
                bobAmpX = bobAmplitudeXSprint;
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

        float scaleADS = Mathf.Lerp(1f, 0.2f, _adsBlend);

        // Combine bob + idle breath into a single offset
        float bobY = Mathf.Sin(_bobTimer)         * bobAmpY   * scaleADS * (isMoving ? 1f : 0f);
        float bobX = Mathf.Sin(_bobTimer * 0.5f)  * bobAmpX   * scaleADS * (isMoving ? 1f : 0f);
        float breathY = Mathf.Sin(_breathTimer)   * breathAmplitudeY * scaleADS;
        float breathX = Mathf.Cos(_breathTimer * 0.6f) * breathAmplitudeX * scaleADS;

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
        float bobDamp = adsProfile != null ? adsProfile.adsBobDamping : 0.8f;
        float scaleADS = Mathf.Lerp(1f, 1f - bobDamp, _adsBlend);

        bool isSprinting = playerMovement.state == PlayerMovement.MovementState.sprinting;
        bool isMoving    = (playerMovement.state == PlayerMovement.MovementState.walking
                         || playerMovement.state == PlayerMovement.MovementState.sprinting)
                        && playerMovement.grounded;

        float bobY = Mathf.Sin(_bobTimer)        * (isMoving ? (isSprinting ? bobAmplitudeYSprint : bobAmplitudeY) : 0f) * scaleADS;
        float bobX = Mathf.Sin(_bobTimer * 0.5f) * (isMoving ? (isSprinting ? bobAmplitudeXSprint : bobAmplitudeX) : 0f) * scaleADS;
        float breathY = Mathf.Sin(_breathTimer)          * breathAmplitudeY * scaleADS;
        float breathX = Mathf.Cos(_breathTimer * 0.6f)   * breathAmplitudeX * scaleADS;

        // ── Position ──────────────────────────────────────────────────────
        Vector3 hipPos = adsProfile != null ? adsProfile.hipPosOffset : fallbackHipPos;
        Vector3 adsPos = adsProfile != null ? adsProfile.adsPosOffset : fallbackADSPos;
        Vector3 basePos  = Vector3.Lerp(hipPos, adsPos, _adsBlend);
        Vector3 dynamicP = new Vector3(
            bobX + breathX + _currentSway.x,
            bobY + breathY + _currentSway.y + _landingDip,
            _currentSway.z
        );
        transform.localPosition = basePos + dynamicP;

        // ── Rotation ──────────────────────────────────────────────────────
        Vector3 hipRot = adsProfile != null ? adsProfile.hipRotOffset : Vector3.zero;
        Vector3 adsRot = adsProfile != null ? adsProfile.adsRotOffset : Vector3.zero;
        Quaternion baseRot   = Quaternion.Euler(Vector3.Lerp(hipRot, adsRot, _adsBlend));
        Quaternion tiltRot   = Quaternion.Euler(0f, 0f, _currentSprintTilt);
        transform.localRotation = _currentRotSway * baseRot * tiltRot;
    }

    #endregion

    // ─────────────────────────────────────────────────────────────────────────
    #region Public API
    // ─────────────────────────────────────────────────────────────────────────

    /// <returns>True if ADS is currently active.</returns>
    public bool IsADS => _isADS;

    /// <summary>
    /// The current hip-fire localPosition target — reads from active profile or fallback.
    /// Used by WeaponSwitcherProcedural to know where to animate the rise-in toward.
    /// </summary>
    public Vector3 ActiveHipPos =>
        adsProfile != null ? adsProfile.hipPosOffset : fallbackHipPos;

    /// <summary>
    /// Swap to a new per-weapon ADS profile.
    /// Call from WeaponSwitcherProcedural.FinishSwitch() after the incoming
    /// weapon is active. Pass null to fall back to the hardcoded fallback pose.
    /// </summary>
    public void LoadProfile(WeaponADSProfile profile)
    {
        adsProfile = profile;
        Log($"Loaded ADS profile: {(profile != null ? profile.name : "null (using fallback)")}");
    }

    /// <summary>Force-snap back to hip pose instantly (call after weapon switch).</summary>
    public void SnapToHip()
    {
        _adsBlend       = 0f;
        _currentSway    = Vector3.zero;
        _currentRotSway = Quaternion.identity;
        _isADS          = false;

        // Reset pivot to hip pose immediately so there's no 1-frame artifact
        if (adsProfile != null)
            transform.localPosition = adsProfile.hipPosOffset;
        else
            transform.localPosition = fallbackHipPos;

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