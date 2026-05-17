// ============================================================
//  PlayerHealth.cs  —  Out of Bullet
//  GDD §6 — Single HP bar, kill-gated only.
//  No regen, no pickups, no shields.
//  I-frames are self-contained via IFrame event — no dash ref.
// ============================================================
using UnityEngine;
using OutOfBullet.Core;

namespace OutOfBullet.Player
{
    public class PlayerHealth : MonoBehaviour
    {
        [Header("Health")]
        public float MaxHP = 100f;

        public float CurrentHP  { get; private set; }
        public bool  IsAlive    => CurrentHP > 0f;
        public float Fraction   => CurrentHP / MaxHP;

        private bool _isDead;
        private bool _isInvincible;   // set by dash via SetInvincible()

        private void Awake()
        {
            CurrentHP = MaxHP;
        }

        private void OnEnable()
        {
            EventBus.Subscribe<EnemyExecutedEvent>(OnEnemyExecuted);
            EventBus.Subscribe<ArenaResetEvent>(OnArenaReset);
        }

        private void OnDisable()
        {
            EventBus.Unsubscribe<EnemyExecutedEvent>(OnEnemyExecuted);
            EventBus.Unsubscribe<ArenaResetEvent>(OnArenaReset);
        }

        // ── Damage ───────────────────────────────────────────────
        public void TakeDamage(float amount)
        {
            if (_isDead || _isInvincible) return;
            ApplyDelta(-amount);
        }

        // ── I-frames — called by PlayerController ─
        public void SetInvincible(bool invincible)
        {
            _isInvincible = invincible;
        }

        // ── Siphon ───────────────────────────────────────────────
        public void ApplySiphon(float amount)
        {
            if (_isDead) return;
            ApplyDelta(amount);
        }

        // ── Internal ─────────────────────────────────────────────
        private void ApplyDelta(float delta)
        {
            CurrentHP = Mathf.Clamp(CurrentHP + delta, 0f, MaxHP);
            EventBus.Publish(new PlayerHealthChangedEvent
            {
                CurrentHP = CurrentHP,
                MaxHP     = MaxHP,
                Delta     = delta
            });
            if (CurrentHP <= 0f && !_isDead) Die();
        }

        private void Die()
        {
            _isDead = true;
            EventBus.Publish(new PlayerDiedEvent());
        }

        // ── Events ───────────────────────────────────────────────
        private void OnEnemyExecuted(EnemyExecutedEvent evt)
        {
            ApplySiphon(evt.HealthSiphonAmount);
        }

        private void OnArenaReset(ArenaResetEvent evt)
        {
            _isDead   = false;
            CurrentHP = MaxHP;
            EventBus.Publish(new PlayerHealthChangedEvent
            {
                CurrentHP = CurrentHP,
                MaxHP     = MaxHP,
                Delta     = MaxHP
            });
        }
    }
}