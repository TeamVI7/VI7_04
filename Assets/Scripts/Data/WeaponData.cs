// ============================================================
//  WeaponData.cs  —  Out of Bullet
//  ScriptableObject config for each weapon type.
//  Create via: Assets → Create → OutOfBullet → WeaponData
//  GDD §4 — small mag, high damage, disposable.
// ============================================================
using UnityEngine;

namespace OutOfBullet.Data
{
    [CreateAssetMenu(
        fileName = "WPN_NewWeapon",
        menuName  = "OutOfBullet/WeaponData",
        order     = 0)]
    public class WeaponData : ScriptableObject
    {
        [Header("Identity")]
        public string WeaponName = "Pistol";
        public EnemyWeaponClass WeaponClass = EnemyWeaponClass.Pistol;

        [Header("Magazine (GDD §4.3)")]
        [Tooltip("Full magazine capacity.")]
        public int MagazineSize = 8;

        [Tooltip("Min % of full mag at acquisition (GDD: 50-80%).")]
        [Range(0.1f, 1f)]
        public float AcquisitionMinFill = 0.5f;

        [Tooltip("Max % of full mag at acquisition (GDD: 50-80%).")]
        [Range(0.1f, 1f)]
        public float AcquisitionMaxFill = 0.8f;

        [Header("Fire")]
        public FireMode FireMode = FireMode.SemiAuto;

        [Tooltip("Rounds per minute.")]
        public float FireRate = 300f;

        [Tooltip("Damage per bullet.")]
        public float DamagePerShot = 25f;

        [Tooltip("Bullet range in metres.")]
        public float Range = 50f;

        [Header("Throw Stagger (GDD §4.1)")]
        [Tooltip("Stagger potency when thrown — Pistol=Moderate, Shotgun=High, etc.")]
        public StaggerPotency ThrowStagger = StaggerPotency.Moderate;

        [Header("Projectile / Hitscan")]
        [Tooltip("Hitscan = instant; false = spawns a bullet projectile.")]
        public bool IsHitscan = true;

        [Tooltip("If not hitscan, bullet travel speed.")]
        public float BulletSpeed = 200f;

        [Header("View Model")]
        [Tooltip("Prefab of the weapon view model (first-person display).")]
        public GameObject ViewModelPrefab;

        [Tooltip("Prefab of the world-space weapon (for throwing).")]
        public GameObject WorldModelPrefab;

        [Header("Audio")]
        public AudioClip FireSound;
        public AudioClip EmptyClickSound;

        // ── Helpers ──────────────────────────────────────────────
        /// <summary>Returns a randomized starting ammo count for acquisition.</summary>
        public int GetRandomAcquisitionAmmo()
        {
            float fill = Random.Range(AcquisitionMinFill, AcquisitionMaxFill);
            return Mathf.Max(1, Mathf.RoundToInt(MagazineSize * fill));
        }

        /// <summary>Seconds between shots derived from FireRate.</summary>
        public float FireInterval => 60f / FireRate;
    }

    // ── Enums ────────────────────────────────────────────────────

    public enum FireMode
    {
        SemiAuto,
        FullAuto,
        Burst3,
        PumpAction,
        BreakAction
    }

    public enum EnemyWeaponClass
    {
        Pistol,
        SMG,
        Shotgun,
        Revolver,
        GrenadeLauncher,
        BurstRifle
    }

    /// <summary>
    /// Stagger contribution when the weapon is thrown.
    /// GDD §4.1 — throw stagger column.
    /// </summary>
    public enum StaggerPotency
    {
        Low,        // SMG
        Moderate,   // Pistol, BurstRifle
        High,       // Shotgun, Revolver
        Extreme     // GrenadeLauncher
    }
}
