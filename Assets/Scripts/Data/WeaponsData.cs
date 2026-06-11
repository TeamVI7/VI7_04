using UnityEngine;

/// <summary>
/// ScriptableObject that holds every tunable stat for a weapon.
/// Create via: Right-click > FPS > Weapon Data
/// Assign to WeaponsController.weaponData in the Inspector.
/// </summary>
[CreateAssetMenu(fileName = "NewWeaponData", menuName = "FPS/Weapon Data")]
public class WeaponData : ScriptableObject
{
    // ─────────────────────────────────────────────────────────────────────────
    #region Core Stats
    // ─────────────────────────────────────────────────────────────────────────
    [Header("HUD Display")]
    public string displayName = "WEAPON";
    public string caliber     = "";
    public string ammoType    = "FMJ";

    [Header("Core Stats")]
    [Tooltip("When true, weapon can fire automatically while trigger is held.")]
    public bool isFullAuto = true;
    [Tooltip("Rounds per second (lower = faster).")]
    public float fireRate = 0.1f;

    [Tooltip("Maximum rounds per magazine.")]
    public int clipSize = 30;

    [Tooltip("Total reserve ammo the player carries.")]
    public int reservedAmmoCapacity = 270;

    [Tooltip("Base hit damage per bullet.")]
    public int weaponDamage = 25;

    [Tooltip("Maximum hitscan range in world units.")]
    public float maxRange = 999f;

    [Tooltip("Impulse force applied to a hit Rigidbody.")]
    public float hitImpulseForce = 10f;
    
    [Header("Bolt Action")]
    public bool  isBoltAction    = false;
    public float boltOutDuration = 0.3f;   // timed fallback
    public float boltInDuration  = 0.25f;  // timed fallback
    [Header("Bolt Action SFX")]
    public AudioClip boltOutSound;
    public float     boltOutVolume  = 1f;
    public AudioClip boltInSound;
    public float     boltInVolume   = 1f;
    #endregion

    // ─────────────────────────────────────────────────────────────────────────
    #region Spread
    // ─────────────────────────────────────────────────────────────────────────

    [Header("Spread")]
    public float baseSpread           = 0.02f;
    public float adsSpreadMultiplier  = 0.20f;
    public float moveSpreadMultiplier = 2.50f;
    public float sprintSpreadMultiplier = 5.0f;
    public float crouchSpreadMultiplier = 0.60f;
    public float standSpreadMultiplier  = 0.80f;
    public float spreadBuildPerShot   = 0.01f;
    public float spreadRecoveryRate   = 0.04f;
    public float maxSpreadBuildup     = 0.08f;

    #endregion

    // ─────────────────────────────────────────────────────────────────────────
    #region Reload Timing
    // ─────────────────────────────────────────────────────────────────────────

    [Header("Reload Timing")]
    [Tooltip("Total duration of the reload animation in seconds.")]
    public float reloadTotalTime = 2.4f;

    [Tooltip("Seconds after reload starts before the mag-out event fires.")]
    public float reloadMagOutDelay = 0.5f;

    [Tooltip("Seconds after mag-out before the mag-in event fires.")]
    public float reloadMagInDelay = 1.0f;

    [Tooltip("Seconds after mag-in before the chamber-round event fires. " +
             "Set to 0 if the weapon auto-chambers on insertion.")]
    public float reloadChamberDelay = 0.5f;

    [Tooltip("When true, an extra dry-chamber step is added if the gun fired " +
             "its last round (bolt/slide must be racked).")]
    public bool requiresManualChamber = false;

    #endregion

    // ─────────────────────────────────────────────────────────────────────────
    #region Audio
    // ─────────────────────────────────────────────────────────────────────────

    [Header("Audio")]
    public AudioClip[] shootSounds;
    public AudioClip dryFireSound;

    [Tooltip("Sound played when the magazine is ejected.")]
    public AudioClip magOutSound;

    [Tooltip("Sound played when a fresh magazine is inserted.")]
    public AudioClip magInSound;

    [Tooltip("Sound played when the bolt/slide chambers a round.")]
    public AudioClip chamberRoundSound;

    [Range(0f, 1f)] public float shootVolume      = 1.0f;
    [Range(0f, 1f)] public float reloadVolume      = 0.8f;
    [Range(0f, 1f)] public float dryFireVolume     = 0.8f;
    [Range(0f, 1f)] public float chamberVolume     = 0.8f;

    #endregion

    // ─────────────────────────────────────────────────────────────────────────
    #region Casing Physics
    // ─────────────────────────────────────────────────────────────────────────

    [Header("Casing Physics")]
    public float casingEjectForce   = 2f;
    public float casingDestroyTime  = 5f;

    #endregion
}