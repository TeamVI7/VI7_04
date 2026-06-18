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
///   ProceduralRecoil.ApplyProfile()
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
}