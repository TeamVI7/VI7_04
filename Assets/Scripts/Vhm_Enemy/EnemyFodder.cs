// ============================================================
//  EnemyFodder.cs  —  Out of Bullet
//  GDD §5.1 — 1-hit kill from any source. Loop filler.
//  FIX: Bắn tầm xa phương ngang (súng lục), radar rộng độc lập patrol
//  FIX: Không cần đến sát mặt mới bắn — giữ khoảng cách tối ưu
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
        public float FireRate       = 1.5f;
        public float FireRange      = 20f;
        public float PreferredRange = 10f;   // Khoảng cách lý tưởng để dừng và bắn

        [Header("Fodder — Nav")]
        public float MoveSpeed = 4.5f;

        [Header("Fodder — SpeedChase")]
        [Tooltip("Tốc độ chase khi aggro — tăng tức thì khi phát hiện player.")]
        public float ChaseSpeed = 10f;

        // ── Projectile (Súng lục — bắn phương ngang) ────────────
        [Header("Fodder — Pistol (Horizontal Shot)")]
        [Tooltip("Prefab viên đạn acid/pistol — gán AcidProjectile.cs lên đó.")]
        public GameObject AcidProjectilePrefab;

        [Tooltip("Điểm phát đạn — tạo Empty GO ở miệng/tay enemy, kéo vào đây.")]
        public Transform  FirePoint;

        [Tooltip("Tầm bắn tối đa (xa hơn PreferredRange nhiều).")]
        public float AcidRange    = 30f;    // Rộng hơn mặc định cũ (15f)

        [Tooltip("Tốc độ bắn (lần/giây).")]
        public float AcidFireRate = 1.2f;

        // ── Radar (Độc lập với patrol, phát hiện player từ xa) ──
        [Header("Fodder — Radar")]
        [Tooltip("Bán kính radar để PHÁT HIỆN và BẮN — lớn hơn AggroRadius nhiều.")]
        public float RadarRange = 40f;      // Quét thấy player từ rất xa

        [Tooltip("Layer của player để radar quét.")]
        public LayerMask RadarPlayerLayer;

        [Header("Fodder — Patrol Settings")]
        public float PatrolSpeed      = 2.0f;
        public float PatrolRadius     = 12f;
        public float WaypointWaitTime = 2.5f;

        // ── Runtime ──────────────────────────────────────────────
        protected NavMeshAgent _nav;
        protected Transform    _player;
        protected float        _fireTimer; // Đổi sang protected để lớp con Drone dùng nếu cần

        private Vector3 _spawnPosition;
        private Vector3 _patrolTarget;
        private bool    _hasPatrolTarget;
        private float   _waitTimer;

        // ── Unity ────────────────────────────────────────────────
        protected override void Awake()
        {
            Tier  = EnemyTier.Fodder;
            MaxHP = 100f;
            base.Awake();

            _nav         = GetComponent<NavMeshAgent>();
            _nav.speed   = MoveSpeed;
            _nav.enabled = true;
        }

        private void Start()
        {
            _spawnPosition = transform.position;

            var playerGo = GameObject.FindGameObjectWithTag("Player");
            if (playerGo != null) _player = playerGo.transform;
        }

        // ── State Behaviors ──────────────────────────────────────
        protected override void TickIdle()
        {
            // --- RADAR CHECK: độc lập với patrol ---
            if (TryRadarDetectPlayer(out float radarDist))
            {
                if (radarDist <= AcidRange)
                {
                    FacePlayer();
                    TryFireHorizontal(radarDist);
                }

                if (radarDist <= AggroRadius)
                {
                    _hasPatrolTarget = false;
                    TransitionTo(EnemyState.Aggro);
                    return;
                }
            }

            // --- PATROL ---
            if (_nav == null || !_nav.enabled || !_nav.isOnNavMesh) return;
            _nav.speed = PatrolSpeed;

            if (!_hasPatrolTarget)
            {
                _waitTimer += Time.deltaTime;
                if (_waitTimer >= WaypointWaitTime)
                    FindNewPatrolPoint();
            }
            else
            {
                if (!_nav.pathPending && _nav.remainingDistance <= _nav.stoppingDistance)
                {
                    _hasPatrolTarget = false;
                    _waitTimer = 0f;
                }
            }
        }

        protected override void TickAggro()
        {
            // ── Shield Break Stun Lock (dùng cho EnemyShielder kế thừa) ──
            EnemyShielder shielder = GetComponent<EnemyShielder>();
            if (shielder != null && shielder.IsShieldBreakRecovering)
            {
                if (_nav != null)
                {
                    _nav.ResetPath();
                    _nav.velocity  = Vector3.zero;
                    _nav.isStopped = true;
                }
                return;
            }
            if (_nav != null && _nav.isStopped)
                _nav.isStopped = false;

            if (_player == null) return;

            float dist = Vector3.Distance(transform.position, _player.position);

            if (dist > RadarRange)
            {
                _nav.ResetPath();
                _hasPatrolTarget = false;
                _waitTimer = 0f;
                TransitionTo(EnemyState.Idle);
                return;
            }

            if (_nav == null || !_nav.enabled || !_nav.isOnNavMesh) return;

            if (dist > PreferredRange)
            {
                _nav.speed = ChaseSpeed;
                _nav.SetDestination(_player.position);
            }
            else
            {
                _nav.speed = MoveSpeed;
                _nav.ResetPath();
                FacePlayer();
            }

            TryFireHorizontal(dist);
        }

        // ── Pistol Fire — Phương Ngang ───────────────────────────
        /// <summary>
        /// Bắn đạn theo phương NGANG nhắm thẳng vào thân player.
        /// </summary>
        protected virtual void TryFireHorizontal(float dist)
        {
            if (dist > AcidRange) return;
            if (AcidProjectilePrefab == null) return;

            _fireTimer += Time.deltaTime;
            if (_fireTimer < 1f / AcidFireRate) return;
            _fireTimer = 0f;

            Vector3 origin = FirePoint != null
                ? FirePoint.position
                : transform.position + Vector3.up * 1.4f;

            Vector3 targetPos  = _player.position + Vector3.up * 1.0f;
            Vector3 toTarget   = targetPos - origin;
            toTarget.y = 0f;

            Vector3 direction = toTarget.sqrMagnitude > 0.01f
                ? toTarget.normalized
                : transform.forward;

            GameObject projGO  = Instantiate(AcidProjectilePrefab, origin, Quaternion.LookRotation(direction));
            AcidProjectile proj = projGO.GetComponent<AcidProjectile>();
            if (proj != null)
                proj.Init(direction);

            Debug.DrawRay(origin, direction * AcidRange, Color.green, 0.5f);
            GameManager.Instance?.DebugLog($"[Fodder] Bắn súng lục → player dist={dist:F1}");
        }

        // ── Radar Detection ──────────────────────────────────────
        protected bool TryRadarDetectPlayer(out float distance)
        {
            distance = float.MaxValue;

            if (_player == null)
            {
                var go = GameObject.FindGameObjectWithTag("Player");
                if (go != null) _player = go.transform;
                else return false;
            }

            distance = Vector3.Distance(transform.position, _player.position);
            return distance <= RadarRange;
        }

        protected void FacePlayer()
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
            switch (newState)
            {
                case EnemyState.Idle:
                case EnemyState.Aggro:
                    if (_nav != null) _nav.enabled = true;
                    break;

                case EnemyState.Staggered:
                case EnemyState.Ragdoll:
                    if (_nav != null) _nav.enabled = false;
                    break;
            }
        }

        private void FindNewPatrolPoint()
        {
            Vector3 randomDirection = Random.insideUnitSphere * PatrolRadius;
            randomDirection += _spawnPosition;

            NavMeshHit hit;
            if (NavMesh.SamplePosition(randomDirection, out hit, PatrolRadius, NavMesh.AllAreas))
            {
                _patrolTarget    = hit.position;
                _nav.SetDestination(_patrolTarget);
                _hasPatrolTarget = true;
            }
        }
    }
}