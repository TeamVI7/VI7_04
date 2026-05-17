// ============================================================
//  HUDController.cs  —  Out of Bullet
//  GDD §8 — Minimal, diegetic where possible, never interruptive.
//  All state driven by EventBus — zero per-frame polling.
//  Vertical slice: flat placeholder bars per GDD §11.1.
// ============================================================
using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using OutOfBullet.Core;

namespace OutOfBullet.UI
{
    public class HUDController : MonoBehaviour
    {
        // ── Inspector ─── Assign in Inspector ────────────────────
        [Header("Health Bar (Bottom-Left)")]
        public Slider  HealthBar;
        public Image   HealthFill;
        public Color   HealthNormal   = Color.white;
        public Color   HealthCritical = Color.red;
        [Range(0f, 0.5f)]
        public float CriticalThreshold = 0.25f;    // GDD §8.1: red below 25%

        [Header("Ammo Counter (Bottom-Right)")]
        public TextMeshProUGUI AmmoText;
        public float           AmmoFlashDuration = 0.2f;
        private Coroutine      _ammoFlashRoutine;

        [Header("Grapple Cooldown Radial (Center-Low)")]
        public Image GrappleCooldownArc;           // Image type = Filled, radial
        public CanvasGroup GrappleCooldownGroup;   // Alpha 0 when ready

        [Header("Dash Charges (Left Forearm — Placeholder Pips)")]
        public Image[] DashPips;                   // 3 UI images
        public Color   DashPipActive   = Color.cyan;
        public Color   DashPipDepleted = new Color(0.2f, 0.2f, 0.2f, 1f);

        [Header("Crosshair")]
        public RectTransform CrosshairRoot;
        public float         CrosshairIdleSize    = 20f;
        public float         CrosshairSpreadScale = 2.5f;   // expands with movement speed
        public float         CrosshairLerpSpeed   = 10f;
        private float        _targetCrosshairSize;

        [Header("Stagger Indicator")]
        [Tooltip("World-space canvas prefab instantiated on enemies when staggered.")]
        public GameObject StaggerIndicatorPrefab;

        // ── Unity ────────────────────────────────────────────────
        private void OnEnable()
        {
            EventBus.Subscribe<PlayerHealthChangedEvent>(OnHealthChanged);
            EventBus.Subscribe<WeaponFiredEvent>(OnWeaponFired);
            EventBus.Subscribe<WeaponAcquiredEvent>(OnWeaponAcquired);
            EventBus.Subscribe<WeaponEmptyEvent>(OnWeaponEmpty);
            EventBus.Subscribe<PlayerDashUsedEvent>(OnDashUsed);
            EventBus.Subscribe<PlayerDashChargeRestoredEvent>(OnDashRestored);
            EventBus.Subscribe<GrappleCooldownStartedEvent>(OnGrappleCooldownStart);
            EventBus.Subscribe<GrappleCooldownEndedEvent>(OnGrappleCooldownEnd);
            EventBus.Subscribe<PlayerVelocityChangedEvent>(OnVelocityChanged);
            EventBus.Subscribe<EnemyStaggeredEvent>(OnEnemyStaggered);
        }

        private void OnDisable()
        {
            EventBus.Unsubscribe<PlayerHealthChangedEvent>(OnHealthChanged);
            EventBus.Unsubscribe<WeaponFiredEvent>(OnWeaponFired);
            EventBus.Unsubscribe<WeaponAcquiredEvent>(OnWeaponAcquired);
            EventBus.Unsubscribe<WeaponEmptyEvent>(OnWeaponEmpty);
            EventBus.Unsubscribe<PlayerDashUsedEvent>(OnDashUsed);
            EventBus.Unsubscribe<PlayerDashChargeRestoredEvent>(OnDashRestored);
            EventBus.Unsubscribe<GrappleCooldownStartedEvent>(OnGrappleCooldownStart);
            EventBus.Unsubscribe<GrappleCooldownEndedEvent>(OnGrappleCooldownEnd);
            EventBus.Unsubscribe<PlayerVelocityChangedEvent>(OnVelocityChanged);
            EventBus.Unsubscribe<EnemyStaggeredEvent>(OnEnemyStaggered);
        }

        private void Start()
        {
            // Init states
            SetGrappleCooldownVisible(false);
            RefreshAllDashPips(3, 3);
        }

        private void Update()
        {
            // Crosshair size lerp — driven by velocity event, applied each frame
            if (CrosshairRoot != null)
            {
                float current = CrosshairRoot.sizeDelta.x;
                float next    = Mathf.Lerp(current, _targetCrosshairSize, CrosshairLerpSpeed * Time.deltaTime);
                CrosshairRoot.sizeDelta = new Vector2(next, next);
            }

            // Grapple arc fill — poll GrappleSystem directly for smooth radial
            UpdateGrappleArc();
        }

        // ── Health ───────────────────────────────────────────────
        private void OnHealthChanged(PlayerHealthChangedEvent evt)
        {
            if (HealthBar != null)
                HealthBar.value = evt.CurrentHP / evt.MaxHP;

            if (HealthFill != null)
                HealthFill.color = (evt.CurrentHP / evt.MaxHP) < CriticalThreshold
                    ? HealthCritical
                    : HealthNormal;
        }

        // ── Ammo ─────────────────────────────────────────────────
        private void OnWeaponFired(WeaponFiredEvent evt)
        {
            SetAmmoText(evt.RemainingAmmo.ToString());
        }

        private void OnWeaponAcquired(WeaponAcquiredEvent evt)
        {
            SetAmmoText(evt.Ammo.ToString());
            FlashAmmo();   // GDD §8.1: brief flash on acquisition
        }

        private void OnWeaponEmpty(WeaponEmptyEvent evt)
        {
            SetAmmoText("0");
            // Dimmed state handled via color
            if (AmmoText != null)
                AmmoText.color = new Color(0.5f, 0.5f, 0.5f, 1f);
        }

        private void SetAmmoText(string val)
        {
            if (AmmoText != null)
            {
                AmmoText.text  = val;
                AmmoText.color = Color.white;
            }
        }

        private void FlashAmmo()
        {
            if (_ammoFlashRoutine != null) StopCoroutine(_ammoFlashRoutine);
            _ammoFlashRoutine = StartCoroutine(AmmoFlashRoutine());
        }

        private IEnumerator AmmoFlashRoutine()
        {
            if (AmmoText == null) yield break;
            AmmoText.color = Color.yellow;
            yield return new WaitForSeconds(AmmoFlashDuration);
            AmmoText.color = Color.white;
        }

        // ── Dash Pips ────────────────────────────────────────────
        private void OnDashUsed(PlayerDashUsedEvent evt)
        {
            RefreshAllDashPips(evt.ChargesRemaining, evt.MaxCharges);
        }

        private void OnDashRestored(PlayerDashChargeRestoredEvent evt)
        {
            RefreshAllDashPips(evt.ChargesRemaining, evt.MaxCharges);
        }

        private void RefreshAllDashPips(int current, int max)
        {
            if (DashPips == null) return;
            for (int i = 0; i < DashPips.Length; i++)
            {
                if (DashPips[i] == null) continue;
                DashPips[i].color = i < current ? DashPipActive : DashPipDepleted;
            }
        }

        // ── Grapple Cooldown Radial ───────────────────────────────
        private float _grappleCooldownDuration;
        private float _grappleCooldownStart;
        private bool  _grappleOnCooldown;

        private void OnGrappleCooldownStart(GrappleCooldownStartedEvent evt)
        {
            _grappleCooldownDuration = evt.Duration;
            _grappleCooldownStart    = Time.time;
            _grappleOnCooldown       = true;
            SetGrappleCooldownVisible(true);
        }

        private void OnGrappleCooldownEnd(GrappleCooldownEndedEvent evt)
        {
            _grappleOnCooldown = false;
            SetGrappleCooldownVisible(false);
        }

        private void UpdateGrappleArc()
        {
            if (!_grappleOnCooldown || GrappleCooldownArc == null) return;

            float elapsed   = Time.time - _grappleCooldownStart;
            float remaining = 1f - Mathf.Clamp01(elapsed / _grappleCooldownDuration);
            GrappleCooldownArc.fillAmount = remaining;
        }

        private void SetGrappleCooldownVisible(bool visible)
        {
            if (GrappleCooldownGroup != null)
                GrappleCooldownGroup.alpha = visible ? 1f : 0f;
        }

        // ── Crosshair ────────────────────────────────────────────
        private void OnVelocityChanged(PlayerVelocityChangedEvent evt)
        {
            float t = Mathf.Clamp01(evt.Speed / 20f);   // 20 m/s = fully expanded
            _targetCrosshairSize = Mathf.Lerp(CrosshairIdleSize,
                CrosshairIdleSize * CrosshairSpreadScale, t);
        }

        // ── Stagger Indicator (World Space) ──────────────────────
        private void OnEnemyStaggered(EnemyStaggeredEvent evt)
        {
            if (StaggerIndicatorPrefab == null || evt.Enemy == null) return;

            var indicator = Instantiate(
                StaggerIndicatorPrefab,
                evt.Position + Vector3.up * 2f,
                Quaternion.identity);

            // Parent to enemy so it tracks it
            indicator.transform.SetParent(evt.Enemy.transform, true);

            // Auto-destroy after stagger duration (2.5s per GDD §5.3.1)
            Destroy(indicator, 2.5f);
        }
    }
}
