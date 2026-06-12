using UnityEngine;

/// <summary>
/// Per-weapon recoil tuning data.
/// Create via: Right-click > FPS > Recoil Profile
/// Assign to WeaponsController.recoilProfile, then call
/// recoilModule.ApplyProfile() from WeaponSwitcherProcedural on switch.
/// </summary>
[CreateAssetMenu(fileName = "NewRecoilProfile", menuName = "FPS/Recoil Profile")]
public class RecoilProfile : ScriptableObject
{
    [Header("Identity")]
    public string weaponName = "Weapon";

    // ─────────────────────────────────────────────────────────────────────────
    #region Positional Kick
    // ─────────────────────────────────────────────────────────────────────────

    [Header("Positional Kick")]
    [Tooltip("How far back the weapon flies per shot.")]
    public float kickbackAmount  = 0.04f;

    [Tooltip("How far up the weapon jumps per shot.")]
    public float kickUpAmount    = 0.01f;

    [Tooltip("Random side-to-side per shot.")]
    public float kickSideAmount  = 0.005f;

    #endregion

    // ─────────────────────────────────────────────────────────────────────────
    #region Rotational Kick (degrees)
    // ─────────────────────────────────────────────────────────────────────────

    [Header("Rotational Kick (degrees)")]
    [Tooltip("Upward pitch per shot.")]
    public float recoilX         = 3f;

    [Tooltip("Random yaw per shot — horizontal walk.")]
    public float recoilY         = 0.8f;

    [Tooltip("Random roll per shot — weapon tilt.")]
    public float recoilZ         = 0.5f;

    #endregion

    // ─────────────────────────────────────────────────────────────────────────
    #region Spring — Position
    // ─────────────────────────────────────────────────────────────────────────

    [Header("Spring — Position")]
    [Tooltip("Higher = snappier return to rest.")]
    public float positionStiffness = 200f;

    [Tooltip("Higher = less oscillation.")]
    public float positionDamping   = 20f;

    public float positionMass      = 1f;

    #endregion

    // ─────────────────────────────────────────────────────────────────────────
    #region Spring — Rotation
    // ─────────────────────────────────────────────────────────────────────────

    [Header("Spring — Rotation")]
    public float rotationStiffness = 300f;
    public float rotationDamping   = 25f;
    public float rotationMass      = 1f;

    #endregion

    // ─────────────────────────────────────────────────────────────────────────
    #region ADS
    // ─────────────────────────────────────────────────────────────────────────

    [Header("ADS")]
    [Range(0f, 1f)]
    [Tooltip("Scales all recoil while ADS. 0 = no recoil, 1 = full recoil.")]
    public float adsRecoilMultiplier = 0.35f;

    #endregion
}