using UnityEngine;

/// <summary>
/// Per-weapon recoil spring profile.
///
/// SETUP:
///   1. Right-click in Project → Create → FPS / Recoil Profile.
///   2. One asset per weapon (e.g. "Recoil_Rifle", "Recoil_Shotgun").
///   3. Call ProceduralRecoil.ApplyProfile() from WeaponSwitcherProcedural.FinishSwitch()
///      alongside LoadProfile() — or extend WeaponADSProfile to hold a RecoilProfile ref.
///
/// CONSUMED BY:
///   ProceduralRecoil.ApplyProfile() — weapon-mesh kick
///   CameraRecoil — actual camera/aim kick (see cameraKick* fields below)
/// </summary>
[CreateAssetMenu(menuName = "FPS/Recoil Profile", fileName = "Recoil_NewWeapon")]
public class RecoilProfile : ScriptableObject
{
    // ─────────────────────────────────────────────────────────────────────────
    #region Positional Kick
    // ─────────────────────────────────────────────────────────────────────────

    [Header("Positional Kick")]
    [Tooltip("How far back the weapon kicks per shot (local −Z).")]
    public float kickbackAmount = 0.04f;

    [Tooltip("How far up the weapon kicks per shot (local +Y).")]
    public float kickUpAmount = 0.01f;

    [Tooltip("Random horizontal spread per shot (local ±X).")]
    public float kickSideAmount = 0.005f;

    #endregion

    // ─────────────────────────────────────────────────────────────────────────
    #region Rotational Kick
    // ─────────────────────────────────────────────────────────────────────────

    [Header("Rotational Kick (degrees)")]
    [Tooltip("Pitch per shot — upward kick.")]
    public float recoilX = 3f;

    [Tooltip("Random yaw per shot — horizontal walk.")]
    public float recoilY = 0.8f;

    [Tooltip("Random roll per shot — weapon tilt.")]
    public float recoilZ = 0.5f;

    #endregion

    // ─────────────────────────────────────────────────────────────────────────
    #region Spring Constants
    // ─────────────────────────────────────────────────────────────────────────

    [Header("Spring — Position")]
    public float positionStiffness = 200f;
    public float positionDamping   = 20f;

    [Header("Spring — Rotation")]
    public float rotationStiffness = 300f;
    public float rotationDamping   = 25f;

    #endregion

    // ─────────────────────────────────────────────────────────────────────────
    #region Camera Kick (actual aim/mouse recoil — CameraRecoil.cs)
    // ─────────────────────────────────────────────────────────────────────────

    [Header("Camera Kick — Pitch (view kicks up)")]
    [Tooltip("Degrees the camera pitch kicks up per shot. This is separate from " +
             "the weapon-mesh kick above — this one actually moves your aim, " +
             "so the player has to move the mouse down to compensate.")]
    public float cameraKickPitch = 1.2f;

    [Header("Camera Kick — Yaw (random left/right walk)")]
    [Tooltip("Max degrees of random horizontal kick per shot, applied as ±range.")]
    public float cameraKickYaw = 0.4f;

    [Header("Camera Kick — Recovery")]
    [Tooltip("Degrees/sec the camera kick settles back down at. Higher = snappier recovery.")]
    public float cameraKickRecoverySpeed = 6f;

    #endregion
}