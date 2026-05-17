// ============================================================
//  EnemyFodder.cs  —  Out of Bullet
//  GDD §5.1 — 1-hit kill from any source. Loop filler.
//  Aggros on sight, fires at player, minimal flanking.
//  Chains of 3+ Fodder executes should feel effortless.
// ============================================================
using UnityEngine;
using UnityEngine.AI;
using OutOfBullet.Core;
using OutOfBullet.Player;

namespace OutOfBullet.Enemy
{
    [RequireComponent(typeof(NavMeshAgent))]
    public class EnemyFodder : EnemyBase
    {
        // ── Inspector ────────────────────────────────────────────
        [Header("Fodder — Combat")]
        public float FireRate       = 1.5f;     // shots per second
        public float FireRange      = 20f;
        public float PreferredRange = 8f;
        public LayerMask PlayerMask;

        [Header("Fodder — Nav")]
        public float MoveSpeed = 4.5f;

        // ── Runtime ──────────────────────────────────────────────
        private NavMeshAgent _nav;
        private Transform    _player;
        private float        _fireTimer;

        // ── Unity ────────────────────────────────────────────────
        protected override void Awake()
        {
            base.Awake();
            Tier  = EnemyTier.Fodder;
            MaxHP = 1f;  // Fodder dies to anything (GDD §5.1)

            _nav = GetComponent<NavMeshAgent>();
            _nav.speed      = MoveSpeed;
            _nav.enabled    = false;    // enabled on Aggro
        }

        private void Start()
        {
            // Cache player reference — avoids per-frame Find
            var playerGo = GameObject.FindGameObjectWithTag("Player");
            if (playerGo != null) _player = playerGo.transform;
        }

        // ── State Behaviors ──────────────────────────────────────
        protected override void TickAggro()
        {
            if (_player == null) return;

            float dist = Vector3.Distance(transform.position, _player.position);

            // Move to preferred engagement range
            if (dist > PreferredRange)
            {
                _nav.SetDestination(_player.position);
            }
            else
            {
                _nav.ResetPath();
                FacePlayer();
                TryFire(dist);
            }
        }

        private void TryFire(float dist)
        {
            if (dist > FireRange) return;

            _fireTimer += Time.deltaTime;
            if (_fireTimer < 1f / FireRate) return;
            _fireTimer = 0f;

            // Simple hitscan toward player with small random spread
            Vector3 spread = Random.insideUnitSphere * 0.05f;
            Vector3 dir    = (_player.position + Vector3.up * 1f - transform.position).normalized + spread;

            if (Physics.Raycast(transform.position + Vector3.up * 1f, dir, out RaycastHit hit, FireRange, PlayerMask))
            {
                var health = hit.collider.GetComponentInParent<OutOfBullet.Player.PlayerHealth>();
                health?.TakeDamage(10f);  // Fodder bullet damage — tune per GDD testing
            }
        }

        private void FacePlayer()
        {
            if (_player == null) return;
            Vector3 dir = (_player.position - transform.position);
            dir.y = 0f;
            if (dir != Vector3.zero)
                transform.rotation = Quaternion.Slerp(
                    transform.rotation, Quaternion.LookRotation(dir), 8f * Time.deltaTime);
        }

        protected override void OnStateEntered(EnemyState newState)
        {
            _nav.enabled = (newState == EnemyState.Aggro);
            if (newState == EnemyState.Ragdoll)
                _nav.enabled = false;
        }
    }
}

// ============================================================
//  EnemyHeavy.cs  —  Out of Bullet
//  GDD §5.2 — High HP, stagger-gated execution.
//  Vertical slice: simplified stagger-stub per GDD §11.1.
//  Full behavior tree deferred to full production (GDD §11.2).
// ============================================================
namespace OutOfBullet.Enemy
{
    using UnityEngine;
    using UnityEngine.AI;
    using OutOfBullet.Core;
    using OutOfBullet.Player;

    [RequireComponent(typeof(NavMeshAgent))]
    public class EnemyHeavy : EnemyBase
    {
        // ── Inspector ────────────────────────────────────────────
        [Header("Heavy — Combat")]
        public float FireRate         = 0.7f;
        public float FireRange        = 30f;
        public float BulletDamage     = 20f;
        public LayerMask PlayerMask;

        [Header("Heavy — Nav")]
        public float MoveSpeed        = 2.5f;

        [Header("Heavy — Aggression Boost on Stagger Expire")]
        [Tooltip("Move speed multiplier after stagger expires (GDD §5.3.2).")]
        public float PostStaggerSpeedMul = 1.5f;

        // ── Runtime ──────────────────────────────────────────────
        private NavMeshAgent _nav;
        private Transform    _player;
        private float        _fireTimer;
        private bool         _postStaggerBoosted;

        // ── Unity ────────────────────────────────────────────────
        protected override void Awake()
        {
            base.Awake();
            Tier    = EnemyTier.Heavy;
            MaxHP   = 200f;   // Tuning target — placeholder

            _nav = GetComponent<NavMeshAgent>();
            _nav.speed   = MoveSpeed;
            _nav.enabled = false;
        }

        private void Start()
        {
            var playerGo = GameObject.FindGameObjectWithTag("Player");
            if (playerGo != null) _player = playerGo.transform;
        }

        // ── State Behaviors ──────────────────────────────────────
        protected override void TickAggro()
        {
            if (_player == null) return;

            _nav.SetDestination(_player.position);
            FacePlayer();
            TryFire();
        }

        protected override void TickStaggered()
        {
            base.TickStaggered();   // handles timer expiry
            // Staggered: cannot fire, cannot move (GDD §5.3.2)
        }

        private void TryFire()
        {
            if (_player == null) return;
            float dist = Vector3.Distance(transform.position, _player.position);
            if (dist > FireRange) return;

            _fireTimer += Time.deltaTime;
            if (_fireTimer < 1f / FireRate) return;
            _fireTimer = 0f;

            Vector3 dir = (_player.position + Vector3.up * 1f - transform.position + Vector3.up * 1f).normalized;
            if (Physics.Raycast(transform.position + Vector3.up * 1.2f, dir, out RaycastHit hit, FireRange, PlayerMask))
            {
                var health = hit.collider.GetComponentInParent<OutOfBullet.Player.PlayerHealth>();
                health?.TakeDamage(BulletDamage);
            }
        }

        private void FacePlayer()
        {
            if (_player == null) return;
            Vector3 dir = _player.position - transform.position;
            dir.y = 0;
            if (dir != Vector3.zero)
                transform.rotation = Quaternion.Slerp(
                    transform.rotation, Quaternion.LookRotation(dir), 5f * Time.deltaTime);
        }

        protected override void OnStateEntered(EnemyState newState)
        {
            switch (newState)
            {
                case EnemyState.Aggro:
                    _nav.enabled = true;
                    _nav.speed   = _postStaggerBoosted ? MoveSpeed * PostStaggerSpeedMul : MoveSpeed;
                    break;

                case EnemyState.Staggered:
                    _nav.enabled = false;   // Cannot move while staggered
                    break;

                case EnemyState.Ragdoll:
                    _nav.enabled = false;
                    break;
            }
        }

        protected override void OnStaggerExpired()
        {
            // GDD §5.3.2: resume with boosted aggression
            _postStaggerBoosted = true;
            GameManager.Instance?.DebugLog($"[Heavy:{name}] Stagger expired — aggression boosted!");
        }
    }
}
