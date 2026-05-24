using UnityEngine;

// =============================================================================
//  WeaponData.cs
//  ScriptableObject that holds every tunable stat for one weapon.
//  Create via:  Right-click in Project → FPS → Weapon Data
//
//  Workflow: Create one asset per gun (e.g. "WD_Pistol", "WD_Rifle").
//  Drag the asset into FPSWeaponController.weaponData on each weapon prefab.
//  Tweak numbers here without touching code.
// =============================================================================

[CreateAssetMenu(fileName = "WD_NewWeapon", menuName = "FPS/Weapon Data", order = 0)]
public class WeaponData : ScriptableObject
{
    // -------------------------------------------------------------------------
    //  Identity
    // -------------------------------------------------------------------------

    [Header("Identity")]
    [Tooltip("Display name shown in HUD.")]
    public string weaponName = "Weapon";

    [Tooltip("Slot index this weapon occupies (0–3).")]
    [Range(0, 3)]
    public int slotIndex = 0;

    // -------------------------------------------------------------------------
    //  Fire Settings
    // -------------------------------------------------------------------------

    [Header("Fire Settings")]
    public FireMode defaultFireMode = FireMode.SemiAuto;

    [Tooltip("Allow the player to toggle between semi/auto at runtime.")]
    public bool canSwitchFireMode = false;

    [Tooltip("Seconds between shots in semi-auto.")]
    public float semiAutoDelay = 0.15f;

    [Tooltip("Seconds between shots in full-auto.")]
    public float fullAutoDelay = 0.08f;

    // -------------------------------------------------------------------------
    //  Ammo
    // -------------------------------------------------------------------------

    [Header("Ammo")]
    [Tooltip("Max rounds in the magazine (not counting the chambered round).")]
    public int maxAmmo = 30;

    [Tooltip("Starting reserve ammo.")]
    public int reserveAmmo = 90;

    [Tooltip("Does this weapon spawn with a round already chambered?")]
    public bool startsWithRoundInChamber = true;

    [Tooltip("Length of the reload animation in seconds.  "
           + "If you use Animation Events for the reload, set this to 0 "
           + "and let AnimEvent_ReloadComplete() finish the reload instead.")]
    public float reloadTime = 2.0f;

    // -------------------------------------------------------------------------
    //  Damage & Range
    // -------------------------------------------------------------------------

    [Header("Damage & Range")]
    public int   damage = 25;
    public float range  = 200f;

    // -------------------------------------------------------------------------
    //  Spread
    // -------------------------------------------------------------------------

    [Header("Spread")]
    public float baseSpread              = 0.02f;
    public float adsSpreadMultiplier     = 0.30f;
    public float moveSpreadMultiplier    = 2.00f;
    public float sprintSpreadMultiplier  = 4.00f;
    public float crouchSpreadMultiplier  = 0.60f;
    public float spreadBuildPerShot      = 0.008f;
    public float spreadRecoveryRate      = 0.04f;
    public float maxSpreadBuildup        = 0.08f;

    // -------------------------------------------------------------------------
    //  Recoil
    // -------------------------------------------------------------------------

    [Header("Recoil")]
    [Tooltip("True = random kick within constraints.  "
           + "False = follow recoilPattern array per shot.")]
    public bool randomizeRecoil = true;

    [Tooltip("X = max yaw deviation, Y = max pitch kick (used when randomizeRecoil = true).")]
    public Vector2 randomRecoilConstraints = new Vector2(0.3f, 0.6f);

    [Tooltip("Per-shot recoil offsets used when randomizeRecoil = false.")]
    public Vector2[] recoilPattern;

    // -------------------------------------------------------------------------
    //  Jam & Rack
    // -------------------------------------------------------------------------

    [Header("Jam & Rack")]
    [Range(0f, 1f)]
    [Tooltip("Probability per shot of jamming (0 = never, 1 = always).")]
    public float jamChance = 0f;

    [Tooltip("Seconds the rack animation takes.")]
    public float rackDuration = 0.4f;

    // -------------------------------------------------------------------------
    //  Cosmetics — Casings & Mags
    // -------------------------------------------------------------------------

    [Header("Cosmetics — Casings")]
    public float casingEjectForce   = 2f;
    public float casingDestroyTime  = 5f;

    [Header("Cosmetics — Magazine")]
    public float magEjectForce      = 1.5f;
    public float magDestroyTime     = 8f;

    // -------------------------------------------------------------------------
    //  Audio Volumes  (clips are assigned on the prefab, not here,
    //                  so different skins can share the same WeaponData)
    // -------------------------------------------------------------------------

    [Header("Audio Volumes")]
    [Range(0f, 1f)] public float shootVolume    = 1.0f;
    [Range(0f, 1f)] public float reloadVolume   = 0.8f;
    [Range(0f, 1f)] public float dryFireVolume  = 0.7f;
    [Range(0f, 1f)] public float rackVolume     = 0.9f;
}

// ---------------------------------------------------------------------------
//  Shared Enums  (defined here so both WeaponData and FPSWeaponController
//                 can reference them without a separate file.)
// ---------------------------------------------------------------------------

public enum FireMode    { SemiAuto, Auto }
public enum WeaponState { Ready, Empty, Jammed, NeedsRack }