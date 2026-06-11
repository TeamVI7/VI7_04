using UnityEngine;

/// <summary>
/// Procedural spring-based recoil for an FPS weapon.
///
/// SETUP:
///   1. Attach to the WeaponPivot (a child of the camera, parent of the weapon mesh).
///   2. Assign weaponController in the Inspector.
///   3. This script owns only recoil offsets — it adds to localPosition/localRotation each
///      frame and relies on ProceduralWeaponAnimator for the base idle/bob pose.
///
/// HIERARCHY:
///   [Camera]
///     └─ [WeaponPivot]           ← ProceduralRecoil + ProceduralWeaponAnimator here
///           └─ [WeaponRoot]      ← weapon mesh, animator, WeaponsController
///
/// EXTEND: Add a new RecoilProfile ScriptableObject per weapon and swap it out from
///         WeaponSwitcherProcedural when the weapon changes.
/// </summary>
public class ProceduralRecoil : MonoBehaviour
{
    // ─────────────────────────────────────────────────────────────────────────
    #region Inspector
    // ─────────────────────────────────────────────────────────────────────────

    [Header("References")]
    [Tooltip("The WeaponsController that fires OnWeaponFired.")]
    public WeaponsController weaponController;

    [Header("Positional Recoil")]
    [Tooltip("How far back the weapon kicks per shot (local Z).")]
    public float kickbackAmount   = 0.04f;
    [Tooltip("How far up the weapon kicks per shot (local Y).")]
    public float kickUpAmount     = 0.01f;
    [Tooltip("Random horizontal spread per shot (local X).")]
    public float kickSideAmount   = 0.005f;

    [Header("Rotational Recoil (degrees)")]
    [Tooltip("Pitch (X) rotation per shot — upward kick.")]
    public float recoilX          = 3f;
    [Tooltip("Random yaw (Y) per shot — horizontal walk.")]
    public float recoilY          = 0.8f;
    [Tooltip("Random roll (Z) per shot — weapon tilt.")]
    public float recoilZ          = 0.5f;

    [Header("ADS Multiplier")]
    [Tooltip("Scales all recoil while aiming down sights.")]
    [Range(0f, 1f)]
    public float adsRecoilMultiplier = 0.35f;

    [Header("Spring — Position")]
    public float positionStiffness   = 200f;
    public float positionDamping     = 20f;
    public float positionMass        = 1f;

    [Header("Spring — Rotation")]
    public float rotationStiffness   = 300f;
    public float rotationDamping     = 25f;
    public float rotationMass        = 1f;

    [Header("Debug")]
    [SerializeField] private bool showDebugLogs = false;

    #endregion

    // ─────────────────────────────────────────────────────────────────────────
    #region Private State
    // ─────────────────────────────────────────────────────────────────────────

    // Current offset from the base pose that this script adds
    private Vector3    _currentPosOffset;
    private Vector3    _currentRotOffset;

    // Spring velocities
    private Vector3    _posVelocity;
    private Vector3    _rotVelocity;

    // Target values the spring is pulling toward (the recoil impulse accumulates here)
    private Vector3    _targetPosRecoil;
    private Vector3    _targetRotRecoil;

    private bool       _isADS;

    #endregion

    // ─────────────────────────────────────────────────────────────────────────
    #region Unity Lifecycle
    // ─────────────────────────────────────────────────────────────────────────

    private void OnEnable()
    {
        if (weaponController != null)
            weaponController.OnWeaponFired += HandleWeaponFired;
    }

    private void OnDisable()
    {
        if (weaponController != null)
            weaponController.OnWeaponFired -= HandleWeaponFired;
    }

    private void Update()
    {
        // Let ProceduralWeaponAnimator set the base pose first (executed in LateUpdate),
        // then this runs in Update to accumulate offsets that LateUpdate will apply.
        // If ordering is an issue, move both to LateUpdate with explicit Script Execution Order.
        TickSprings();
    }

    public void ApplyProfile(RecoilProfile p)
    {
        kickbackAmount    = p.kickbackAmount;
        kickUpAmount      = p.kickUpAmount;
        recoilX           = p.recoilX;
        recoilY           = p.recoilY;
        positionStiffness = p.positionStiffness;
        rotationStiffness = p.rotationStiffness;
    }
    
    private void LateUpdate()
    {
        ApplyOffsets();
    }

    #endregion

    // ─────────────────────────────────────────────────────────────────────────
    #region Public API
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>Called by ProceduralWeaponAnimator to inform us whether ADS is active.</summary>
    public void SetADS(bool isADS) => _isADS = isADS;

    /// <summary>
    /// Rebind to a new WeaponsController when the player switches weapons.
    /// Called by WeaponSwitcherProcedural.
    /// </summary>
    public void RebindController(WeaponsController newController)
    {
        if (weaponController != null)
            weaponController.OnWeaponFired -= HandleWeaponFired;

        weaponController = newController;

        if (weaponController != null)
            weaponController.OnWeaponFired += HandleWeaponFired;

        // Reset spring state so old recoil doesn't carry over.
        _currentPosOffset = Vector3.zero;
        _currentRotOffset = Vector3.zero;
        _targetPosRecoil  = Vector3.zero;
        _targetRotRecoil  = Vector3.zero;
        _posVelocity      = Vector3.zero;
        _rotVelocity      = Vector3.zero;
    }

    #endregion

    // ─────────────────────────────────────────────────────────────────────────
    #region Recoil Impulse
    // ─────────────────────────────────────────────────────────────────────────

    private void HandleWeaponFired(Vector3 hitPoint)
    {
        float multiplier = _isADS ? adsRecoilMultiplier : 1f;

        // Positional kick — back and up, small random side
        _targetPosRecoil += new Vector3(
            Random.Range(-kickSideAmount, kickSideAmount) * multiplier,
            kickUpAmount   * multiplier,
           -kickbackAmount * multiplier
        );

        // Rotational kick — upward pitch, random yaw and roll
        _targetRotRecoil += new Vector3(
           -recoilX * multiplier,
            Random.Range(-recoilY, recoilY) * multiplier,
            Random.Range(-recoilZ, recoilZ) * multiplier
        );

        Log($"Recoil impulse | pos={_targetPosRecoil:F3}  rot={_targetRotRecoil:F2}");
    }

    #endregion

    // ─────────────────────────────────────────────────────────────────────────
    #region Spring Simulation
    // ─────────────────────────────────────────────────────────────────────────

    private void TickSprings()
    {
        float dt = Time.deltaTime;

        // ── Positional spring ──────────────────────────────────────────────
        Vector3 posSpringForce = (_targetPosRecoil - _currentPosOffset) * positionStiffness
                               - _posVelocity * positionDamping;
        _posVelocity      += posSpringForce * dt / positionMass;
        _currentPosOffset += _posVelocity * dt;

        // ── Rotational spring ──────────────────────────────────────────────
        Vector3 rotSpringForce = (_targetRotRecoil - _currentRotOffset) * rotationStiffness
                               - _rotVelocity * rotationDamping;
        _rotVelocity      += rotSpringForce * dt / rotationMass;
        _currentRotOffset += _rotVelocity * dt;

        // ── Return target toward zero (recoil decays back to rest) ─────────
        _targetPosRecoil = Vector3.Lerp(_targetPosRecoil, Vector3.zero, dt * (positionDamping * 0.5f));
        _targetRotRecoil = Vector3.Lerp(_targetRotRecoil, Vector3.zero, dt * (rotationDamping * 0.5f));
    }

    private void ApplyOffsets()
    {
        // Add our recoil offset on top of whatever pose ProceduralWeaponAnimator set.
        transform.localPosition += _currentPosOffset;
        transform.localRotation *= Quaternion.Euler(_currentRotOffset);
    }

    #endregion

    // ─────────────────────────────────────────────────────────────────────────
    #region Debug
    // ─────────────────────────────────────────────────────────────────────────

    [System.Diagnostics.Conditional("UNITY_EDITOR")]
    private void Log(string msg)
    {
        if (showDebugLogs) Debug.Log($"[ProceduralRecoil] {msg}", this);
    }

    #endregion
}