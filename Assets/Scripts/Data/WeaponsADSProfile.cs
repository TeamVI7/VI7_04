using UnityEngine;

/// <summary>
/// Per-weapon ADS (Aim-Down-Sights) profile.
///
/// SETUP:
///   1. Right-click in Project → Create → FPS / Weapon ADS Profile.
///   2. One asset per weapon (e.g. "ADS_Rifle", "ADS_Shotgun").
///   3. Drag into WeaponSwitcherProcedural.adsProfiles[] matching weapon slot order.
///
/// CONSUMED BY:
///   • ProceduralWeaponAnimator.LoadProfile()
///   • WeaponSwitcherProcedural.GetProfile()
/// </summary>
[CreateAssetMenu(menuName = "FPS/Weapon ADS Profile", fileName = "ADS_NewWeapon")]
public class WeaponADSProfile : ScriptableObject
{
    // ─────────────────────────────────────────────────────────────────────────
    #region Hip Pose
    // ─────────────────────────────────────────────────────────────────────────

    [Header("Hip Pose")]
    [Tooltip("WeaponPivot localPosition at hip fire.")]
    public Vector3 hipPosOffset = new Vector3(0.15f, -0.18f, 0.35f);

    [Tooltip("WeaponPivot localRotation (Euler) at hip fire.")]
    public Vector3 hipRotOffset = Vector3.zero;

    #endregion

    // ─────────────────────────────────────────────────────────────────────────
    #region ADS Pose
    // ─────────────────────────────────────────────────────────────────────────

    [Header("ADS Pose")]
    [Tooltip("WeaponPivot localPosition when fully aimed.")]
    public Vector3 adsPosOffset = new Vector3(0f, -0.12f, 0.25f);

    [Tooltip("WeaponPivot localRotation (Euler) when fully aimed.")]
    public Vector3 adsRotOffset = Vector3.zero;

    #endregion

    // ─────────────────────────────────────────────────────────────────────────
    #region Transition
    // ─────────────────────────────────────────────────────────────────────────

    [Header("Transition")]
    [Tooltip("Lerp speed from hip to ADS pose (and back). Higher = snappier.")]
    [Range(2f, 30f)]
    public float adsLerpSpeed = 10f;

    #endregion

    // ─────────────────────────────────────────────────────────────────────────
    #region FOV
    // ─────────────────────────────────────────────────────────────────────────

    [Header("FOV")]
    [Tooltip("Camera FOV at hip fire.")]
    [Range(60f, 110f)]
    public float hipFOV = 80f;

    [Tooltip("Camera FOV when fully ADS. Lower = more zoom.")]
    [Range(20f, 80f)]
    public float adsFOV = 55f;

    #endregion

    // ─────────────────────────────────────────────────────────────────────────
    #region Feel Modifiers
    // ─────────────────────────────────────────────────────────────────────────

    [Header("Feel Modifiers")]
    [Tooltip("How much sway is suppressed while ADS. 0 = full sway, 1 = no sway.")]
    [Range(0f, 1f)]
    public float adsSwayDamping = 0.75f;

    [Tooltip("How much bob is suppressed while ADS. 0 = full bob, 1 = no bob.")]
    [Range(0f, 1f)]
    public float adsBobDamping = 0.8f;

    [Tooltip("Prevent ADS from activating while sprinting.")]
    public bool blockSprintWhileADS = true;

    #endregion

    // ─────────────────────────────────────────────────────────────────────────
    #region Recoil
    // ─────────────────────────────────────────────────────────────────────────

    [Header("Recoil")]
    [Tooltip("Per-weapon recoil spring profile. Null = keep ProceduralRecoil's Inspector values.")]
    public RecoilProfile recoilProfile;

    #endregion
}