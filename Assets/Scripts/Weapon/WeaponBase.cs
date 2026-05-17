// ============================================================
//  WeaponBase.cs  —  Out of Bullet
//  Abstract base for all scavenged weapons.
//  No reload, no ammo reserve. Fire → empty → throw.
//  GDD §4 design philosophy: small mag, high damage, disposable.
// ============================================================
using System.Collections;
using UnityEngine;
using OutOfBullet.Core;
using OutOfBullet.Data;
using OutOfBullet.Player;

namespace OutOfBullet.Weapon
{
    public abstract class WeaponBase : MonoBehaviour
    {
        // ── Config ───────────────────────────────────────────────
        public WeaponData Data { get; private set; }

        // ── Runtime ──────────────────────────────────────────────
        public int   CurrentAmmo { get; protected set; }
        public bool  IsEmpty     => CurrentAmmo <= 0;
        public bool  CanFire     => !IsEmpty && !_inFireDelay;

        private bool _inFireDelay;

        // ── Init ─────────────────────────────────────────────────
        public virtual void Initialize(WeaponData data, int startAmmo)
        {
            Data         = data;
            CurrentAmmo  = startAmmo;
        }

        // ── Fire ─────────────────────────────────────────────────
        public void TryFire()
        {
            if (!CanFire)
            {
                if (IsEmpty) OnEmptyFire();
                return;
            }

            ConsumeAmmo();
            StartCoroutine(FireDelay());
            PerformFire();

            EventBus.Publish(new WeaponFiredEvent
            {
                RemainingAmmo = CurrentAmmo,
                WeaponName    = Data.WeaponName
            });

            if (IsEmpty)
                EventBus.Publish(new WeaponEmptyEvent { WeaponName = Data.WeaponName });
        }

        protected abstract void PerformFire();

        private void ConsumeAmmo()
        {
            CurrentAmmo = Mathf.Max(0, CurrentAmmo - 1);
        }

        private IEnumerator FireDelay()
        {
            _inFireDelay = true;
            yield return new WaitForSeconds(Data.FireInterval);
            _inFireDelay = false;
        }

        private void OnEmptyFire()
        {
            // Dry-click feedback handled via WeaponEmptyEvent → AudioManager
        }

        // ── Hitscan ──────────────────────────────────────────────
        protected void FireHitscan(Camera cam, float damage, float range, LayerMask hitLayers)
        {
            if (!Physics.Raycast(cam.transform.position, cam.transform.forward, out RaycastHit hit, range, hitLayers))
                return;

            var enemy = hit.collider.GetComponentInParent<OutOfBullet.Enemy.EnemyBase>();
            enemy?.ApplyDamage(damage, null);
        }
    }
}

// ============================================================
//  WeaponPistol.cs  —  Vertical slice weapon (GDD §11.1)
//  8-round, semi-auto, hitscan. The only weapon in slice.
// ============================================================
namespace OutOfBullet.Weapon
{
    using OutOfBullet.Data;

    public class WeaponPistol : WeaponBase
    {
        [Header("Pistol Config")]
        public LayerMask HitLayers;

        private Camera _cam;

        private void Awake() => _cam = Camera.main;

        protected override void PerformFire()
        {
            FireHitscan(_cam, Data.DamagePerShot, Data.Range, HitLayers);
        }
    }
}

// ============================================================
//  WeaponHolder.cs  —  Manages the player's current weapon.
//  Zero-animation swap on execution (GDD §3.3).
//  Handles fire input, auto-fire for full-auto, and throw.
// ============================================================
namespace OutOfBullet.Weapon
{
    using OutOfBullet.Core;
    using OutOfBullet.Data;
    using OutOfBullet.Projectile;

    public class WeaponHolder : MonoBehaviour
    {
        // ── Inspector ────────────────────────────────────────────
        [Header("Mount Points")]
        [Tooltip("Transform where view model is parented.")]
        public Transform ViewModelMount;

        [Header("Throw")]
        public WeaponThrow ThrowSystem;

        [Header("Input")]
        public KeyCode FireKey   = KeyCode.Mouse0;
        public KeyCode ThrowKey  = KeyCode.G;

        // ── Runtime ──────────────────────────────────────────────
        public WeaponBase CurrentWeapon { get; private set; }
        public bool HasWeapon           => CurrentWeapon != null;

        private GameObject _activeViewModel;

        // ── Unity ────────────────────────────────────────────────
        private void OnEnable()
        {
            EventBus.Subscribe<EnemyExecutedEvent>(OnEnemyExecuted);
        }

        private void OnDisable()
        {
            EventBus.Unsubscribe<EnemyExecutedEvent>(OnEnemyExecuted);
        }

        private void Update()
        {
            if (!HasWeapon) return;
            HandleFireInput();
            HandleThrowInput();
        }

        // ── Input ────────────────────────────────────────────────
        private void HandleFireInput()
        {
            bool firePressed = CurrentWeapon.Data.FireMode == FireMode.FullAuto
                ? Input.GetKey(FireKey)
                : Input.GetKeyDown(FireKey);

            if (firePressed)
                CurrentWeapon.TryFire();
        }

        private void HandleThrowInput()
        {
            if (Input.GetKeyDown(ThrowKey))
                TryThrow();
        }

        // ── Throw ────────────────────────────────────────────────
        private void TryThrow()
        {
            if (!HasWeapon || ThrowSystem == null) return;

            WeaponData data   = CurrentWeapon.Data;
            float      speed  = GetComponent<PlayerController>()?.Speed ?? 0f;

            ThrowSystem.Throw(data, transform.position, Camera.main.transform.forward, speed);

            // Discard current weapon — empty or not (GDD §4.2: always throwable)
            DiscardCurrentWeapon();
        }

        // ── Equip ────────────────────────────────────────────────
        /// <summary>
        /// Equip weapon from enemy execution. Zero-frame, no animation.
        /// GDD §3.3 Step 3: instantaneous swap.
        /// </summary>
        public void EquipFromExecution(WeaponData data)
        {
            if (data == null) return;

            DiscardCurrentWeapon();

            // Spawn view model
            if (data.ViewModelPrefab != null && ViewModelMount != null)
            {
                _activeViewModel = Instantiate(data.ViewModelPrefab, ViewModelMount);
                _activeViewModel.transform.localPosition = Vector3.zero;
                _activeViewModel.transform.localRotation = Quaternion.identity;
            }

            // Create weapon component on this GO
            WeaponBase weapon;
            switch (data.WeaponClass)
            {
                case EnemyWeaponClass.Pistol:
                default:
                    weapon = gameObject.AddComponent<WeaponPistol>();
                    break;
            }

            weapon.Initialize(data, data.GetRandomAcquisitionAmmo());
            CurrentWeapon = weapon;

            EventBus.Publish(new WeaponAcquiredEvent
            {
                WeaponName = data.WeaponName,
                Ammo       = CurrentWeapon.CurrentAmmo
            });

            GameManager.Instance?.DebugLog(
                $"[WeaponHolder] Equipped {data.WeaponName} — ammo: {CurrentWeapon.CurrentAmmo}");
        }

        private void DiscardCurrentWeapon()
        {
            if (_activeViewModel != null) Destroy(_activeViewModel);
            if (CurrentWeapon   != null) Destroy(CurrentWeapon);
            CurrentWeapon    = null;
            _activeViewModel = null;
        }

        // ── Event Handlers ───────────────────────────────────────
        private void OnEnemyExecuted(EnemyExecutedEvent evt)
        {
            // Find the weapon data by name on the enemy — handled via WeaponData registry
            // For vertical slice: assume enemies carry a pre-assigned WeaponData ref
            var enemy = evt.Enemy?.GetComponent<Enemy.EnemyBase>();
            if (enemy?.CarriedWeaponData != null)
                EquipFromExecution(enemy.CarriedWeaponData);
        }
    }
}
