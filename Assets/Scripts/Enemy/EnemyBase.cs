// ============================================================
//  EnemyBase.cs  —  Out of Bullet
//  GDD §5 — Enemies are resource nodes, not obstacles.
//  Strict C# enum FSM per GDD §10.3.
//  Health, stagger, ragdoll, siphon, weapon drop all live here.
// ============================================================
using System.Collections;
using UnityEngine;
using OutOfBullet.Core;
using OutOfBullet.Data;
using OutOfBullet.Player;

namespace OutOfBullet.Enemy
{
    // ── Enums ────────────────────────────────────────────────────

    public enum EnemyTier
    {
        Fodder,
        Heavy
    }

    /// <summary>
    /// Strict FSM states per GDD §5.4.
    /// Transitions must register within 1 physics frame of trigger.
    /// </summary>
    public enum EnemyState
    {
        Idle,
        Aggro,
        Staggered,
        Ragdoll     // Terminal
    }

    // ── EnemyBase ────────────────────────────────────────────────

    [RequireComponent(typeof(RagdollController))]
    public abstract class EnemyBase : MonoBehaviour
    {
        // ── Inspector ────────────────────────────────────────────
        [Header("Identity")]
        public EnemyTier Tier = EnemyTier.Fodder;

        [Header("Stats")]
        public float MaxHP = 100f;

        [Tooltip("Health siphon when this enemy is executed (% of player max HP).")]
        [Range(0f, 0.5f)]
        public float SiphonFraction = 0.07f;

        [Tooltip("Weapon this enemy drops on execution.")]
        public WeaponData CarriedWeaponData;

        [Header("Detection")]
        [Tooltip("GDD §10.3: OverlapSphere-based aggro — NOT trigger colliders.")]
        public float AggroRadius = 15f;

        public LayerMask PlayerLayer;

        [Header("Stagger")]
        [Tooltip("Cumulative damage threshold to auto-stagger a Heavy (GDD §5.3.1: 40% max HP in window).")]
        [Range(0.1f, 1f)]
        public float StaggerThresholdFraction = 0.4f;

        [Tooltip("Window in seconds to accumulate stagger damage (GDD: ~4s).")]
        public float StaggerWindow = 4f;

        // ── Runtime ──────────────────────────────────────────────
        public float      CurrentHP    { get; private set; }
        public bool       IsAlive      => CurrentHP > 0f && _state != EnemyState.Ragdoll;
        public bool       IsStaggered  => _state == EnemyState.Staggered;
        public EnemyState State        => _state;

        protected EnemyState _state        = EnemyState.Idle;
        private   float      _staggerTimer;
        private   float      _damageAccumulator;
        private   float      _damageWindowTimer;
        protected RagdollController _ragdoll;

        // ── Unity ────────────────────────────────────────────────

        /// <summary>
        /// FIX: Subclasses MUST set Tier and MaxHP BEFORE calling base.Awake()
        /// so that CurrentHP is initialised with the correct MaxHP value.
        /// </summary>
        protected virtual void Awake()
        {
            CurrentHP = MaxHP;          // Safe: subclass has already overridden MaxHP
            _ragdoll  = GetComponent<RagdollController>();
        }

        protected virtual void Update()
        {
            if (!IsAlive) return;

            TickStateMachine();
            TickDamageWindow();
        }

        // ── State Machine ────────────────────────────────────────
        private void TickStateMachine()
        {
            switch (_state)
            {
                case EnemyState.Idle:      TickIdle();      break;
                case EnemyState.Aggro:     TickAggro();     break;
                case EnemyState.Staggered: TickStaggered(); break;
            }
        }

        protected virtual void TickIdle()
        {
            // GDD §10.3: OverlapSphere aggro detection — no missed triggers
            if (Physics.CheckSphere(transform.position, AggroRadius, PlayerLayer))
                TransitionTo(EnemyState.Aggro);
        }

        protected virtual void TickAggro()      { /* Override in subclasses */ }
        protected virtual void TickStaggered()
        {
            _staggerTimer -= Time.deltaTime;
            if (_staggerTimer <= 0f)
            {
                // Stagger expired → resume with boosted aggression (GDD §5.3.2)
                OnStaggerExpired();
                TransitionTo(EnemyState.Aggro);
                EventBus.Publish(new EnemyStaggerExpiredEvent { Enemy = gameObject });
            }
        }

        // ── Transitions ──────────────────────────────────────────
        protected void TransitionTo(EnemyState next)
        {
            if (_state == next) return;
            var prev = _state;
            _state   = next;

            GameManager.Instance?.DebugLog(
                $"[Enemy:{name}] {prev} → {next}");

            OnStateEntered(next);
        }

        protected virtual void OnStateEntered(EnemyState newState) { }
        protected virtual void OnStaggerExpired() { }

        // ── Damage ───────────────────────────────────────────────
        /// <param name="instigator">Player controller reference for velocity seeding.</param>
        public void ApplyDamage(float amount, PlayerController instigator)
        {
            if (!IsAlive) return;

            CurrentHP = Mathf.Max(0f, CurrentHP - amount);

            // Accumulate toward stagger threshold (GDD §5.3.1)
            if (Tier == EnemyTier.Heavy && _state == EnemyState.Aggro)
            {
                _damageAccumulator  += amount;
                _damageWindowTimer   = StaggerWindow;   // Reset window on new damage

                float threshold = MaxHP * StaggerThresholdFraction;
                if (_damageAccumulator >= threshold)
                {
                    EnterStagger(2.5f, StaggerPotency.Moderate);
                    _damageAccumulator = 0f;
                }
            }

            if (CurrentHP <= 0f)
                Die(instigator);
        }

        // ── Stagger ──────────────────────────────────────────────
        public void EnterStagger(float duration, StaggerPotency potency)
        {
            if (!IsAlive || _state == EnemyState.Staggered) return;

            _staggerTimer = duration;
            TransitionTo(EnemyState.Staggered);

            EventBus.Publish(new EnemyStaggeredEvent
            {
                Enemy    = gameObject,
                Position = transform.position
            });
        }

        // ── Execute ──────────────────────────────────────────────
        /// <summary>
        /// Called by GrappleSystem on arrival at this enemy.
        /// Routes to KatanaController which does the actual execute + vault.
        /// </summary>
        public void TriggerExecute(PlayerController player)
        {
            var katana = player.GetComponent<KatanaController>();
            katana?.Execute(this);
        }

        // ── Death ────────────────────────────────────────────────
        protected virtual void Die(PlayerController instigator)
        {
            TransitionTo(EnemyState.Ragdoll);

            Vector3 playerVel = instigator != null ? instigator.Rb.linearVelocity : Vector3.zero;

            // Publish BEFORE ragdoll to let other systems react
            EventBus.Publish(new EnemyKilledEvent
            {
                Enemy               = gameObject,
                DeathPosition       = transform.position,
                PlayerVelocityAtKill = playerVel
            });

            // Calculate siphon amount using player's max HP
            float siphon = 0f;
            if (instigator != null)
            {
                var hp = instigator.GetComponent<PlayerHealth>();
                siphon = hp != null ? hp.MaxHP * SiphonFraction : 0f;
            }

            EventBus.Publish(new EnemyExecutedEvent
            {
                Enemy              = gameObject,
                HealthSiphonAmount = siphon,
                WeaponDropped      = CarriedWeaponData?.WeaponName ?? "none"
            });

            // Ragdoll physics — velocity-seeded from player (GDD §7.4)
            _ragdoll?.ActivateRagdoll(playerVel);

            GameManager.Instance?.DebugLog(
                $"[Enemy:{name}] Died — siphon: {siphon:F1}  weapon: {CarriedWeaponData?.WeaponName}");
        }

        // ── Damage Window Tick ───────────────────────────────────
        private void TickDamageWindow()
        {
            if (_damageWindowTimer > 0f)
            {
                _damageWindowTimer -= Time.deltaTime;
                if (_damageWindowTimer <= 0f)
                    _damageAccumulator = 0f;  // Window expired — reset accumulator
            }
        }

#if UNITY_EDITOR
        private void OnDrawGizmosSelected()
        {
            Gizmos.color = Color.yellow;
            Gizmos.DrawWireSphere(transform.position, AggroRadius);

            Gizmos.color = IsStaggered ? Color.cyan : (IsAlive ? Color.green : Color.grey);
            Gizmos.DrawWireSphere(transform.position + Vector3.up * 1.5f, 0.3f);
        }
#endif
    }
}