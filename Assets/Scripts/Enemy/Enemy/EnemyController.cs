using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

public enum EnemyState { Idle, Aggro, Staggered, Dead }

// Vai trò trong đội hình bầy đàn — được PackCoordinator (tĩnh, trong chính class này) gán tự động
public enum PackRole { None, Vanguard, FlankLeft, FlankRight, Rear }

[RequireComponent(typeof(NavMeshAgent))]
[RequireComponent(typeof(EnemyHealth))]
public class EnemyBrain : MonoBehaviour
{
    [Header("Detection")]
    public float AggroRadius = 15f;
    public float RadarRange = 40f;
    public LayerMask LOSBlockingLayers;
    [Tooltip("Khoảng thời gian giữa các lần quét tầm nhìn (Giúp tối ưu CPU)")]
    [SerializeField] private float detectionInterval = 0.2f;
    private float _detectionTimer;

    [Header("Aggro Cooldown (Last Known Position)")]
    [SerializeField] private float lostSightCooldown = 3.0f;
    private float _lostSightTimer;

    [Header("Patrol (Khi mất dấu Player)")]
    [Tooltip("Bán kính lượn quanh vị trí Spawn khi đang Patrol")]
    [SerializeField] private float patrolRadius = 6f;
    [Tooltip("Thời gian đứng chờ giữa mỗi điểm Patrol")]
    [SerializeField] private float patrolWaitTime = 2.5f;
    [Tooltip("Khoảng cách coi như đã tới điểm patrol/spawn")]
    [SerializeField] private float arriveThreshold = 0.5f;
    private float _patrolWaitTimer;
    private bool _returningToSpawn;

    [Header("Flanking System (Hội Đồng Bầy Đàn)")]
    [Tooltip("Khoảng cách giữ cự ly bao vây xung quanh Player")]
    [SerializeField] private float attackFlankRadius = 2.5f;
    public PackRole CurrentPackRole { get; private set; } = PackRole.None;
    private float _myFlankAngle; // góc quanh player, tính theo hướng nhìn của player (0 = trước mặt player)

    [Header("Hit Slowdown (Chậm Lại Khi Bị Bắn)")]
    [Tooltip("Hệ số nhân tốc độ NavMeshAgent khi bị trúng damage (0.4 = còn 40% tốc độ gốc)")]
    [Range(0.05f, 1f)]
    public float hitSlowdownMultiplier = 0.4f;
    [Tooltip("Thời gian duy trì trạng thái chậm sau mỗi lần trúng đòn")]
    public float hitSlowdownDuration = 0.6f;
    [Tooltip("TẮT (mặc định): mỗi lần trúng mới RESET lại timer chậm, không cộng dồn.\nBẬT: mỗi lần trúng mới sẽ CỘNG THÊM thời gian chậm, có thể dồn lâu nếu bị bắn liên tục.")]
    public bool stackSlowdownDuration = false;

    // Hook cho VFX/SFX bên ngoài (VD: flash trắng, camera rung nhẹ khi trúng đạn)
    public event Action OnHitReceived;

    private float _hitSlowdownTimer;
    private float _baseAgentSpeed;

    public Vector3 LastKnownPosition { get; private set; }
    public EnemyState State { get; private set; } = EnemyState.Idle;

    // ── Events ───────────────────────────────────────────────────────────────
    public event Action<EnemyState> OnStateChanged;

    // ── Internal ─────────────────────────────────────────────────────────────
    private NavMeshAgent _agent;
    private EnemyHealth _health;
    private Vector3 _spawnPos;
    private bool _canSeePlayerCache;

    // ── PACK COORDINATOR (static) ───────────────────────────────────────────
    // Danh sách tất cả enemy đang Aggro (cùng nhắm vào 1 player) — dùng để chia vai & chia đều góc bao vây
    private static readonly List<EnemyBrain> _packMembers = new List<EnemyBrain>();

    private void Awake()
    {
        _agent = GetComponent<NavMeshAgent>();
        _health = GetComponent<EnemyHealth>();
        _baseAgentSpeed = _agent.speed;

        _health.OnDied += _ => { LeavePack(); SetState(EnemyState.Dead); };
        _health.OnStaggerEntered += () => SetState(EnemyState.Staggered);
        _health.OnStaggerExpired += () => SetState(EnemyState.Aggro);

        // EnemyHealth.OnDamaged có signature (currentHP, maxHP) -> chỉ cần biết CÓ trúng đòn, không cần số damage
        _health.OnDamaged += (currentHP, maxHP) => TriggerHitSlowdown();

        _spawnPos = transform.position;
    }

    private void OnDestroy()
    {
        // Tránh reference rác trong static list khi object bị destroy giữa lúc đang Aggro
        LeavePack();
    }

    private void TriggerHitSlowdown()
    {
        _hitSlowdownTimer = stackSlowdownDuration
            ? _hitSlowdownTimer + hitSlowdownDuration
            : hitSlowdownDuration;

        OnHitReceived?.Invoke();
    }

    private void Update()
    {
        if (State == EnemyState.Dead) return;

        // Hit Slowdown cập nhật độc lập với state machine bên dưới, chạy ở mọi trạng thái (trừ Dead)
        if (_hitSlowdownTimer > 0f)
        {
            _hitSlowdownTimer -= Time.deltaTime;
            _agent.speed = _hitSlowdownTimer > 0f ? _baseAgentSpeed * hitSlowdownMultiplier : _baseAgentSpeed;
        }

        if (State == EnemyState.Staggered) return;
        if (PlayerHealth.Transform == null) { SetState(EnemyState.Idle); return; }

        _detectionTimer -= Time.deltaTime;
        if (_detectionTimer <= 0)
        {
            _canSeePlayerCache = CheckLineOfSight();
            _detectionTimer = detectionInterval;
        }

        switch (State)
        {
            case EnemyState.Idle: TickIdle(); break;
            case EnemyState.Aggro: TickAggro(); break;
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // IDLE / PATROL
    // ─────────────────────────────────────────────────────────────────────────
    private void TickIdle()
    {
        float actualDistToPlayer = Vector3.Distance(transform.position, PlayerHealth.Transform.position);

        // Player đến đủ gần + có tầm nhìn -> vào Aggro, tham gia bầy
        if (actualDistToPlayer <= AggroRadius && _canSeePlayerCache)
        {
            JoinPack();
            _lostSightTimer = lostSightCooldown;
            SetState(EnemyState.Aggro);
            return;
        }

        TickPatrol();
    }

    private void TickPatrol()
    {
        // Bước 1: nếu vừa rời Aggro, phải quay về khu vực spawn trước đã
        if (_returningToSpawn)
        {
            if (_agent.pathPending) return;
            if (_agent.remainingDistance <= arriveThreshold)
            {
                _returningToSpawn = false;
                _patrolWaitTimer = patrolWaitTime;
            }
            return;
        }

        // Bước 2: patrol lượn quanh spawn point
        if (_agent.pathPending) return;

        if (!_agent.hasPath || _agent.remainingDistance <= arriveThreshold)
        {
            if (_patrolWaitTimer > 0f)
            {
                _patrolWaitTimer -= Time.deltaTime;
                return;
            }

            Vector2 rnd = UnityEngine.Random.insideUnitCircle * patrolRadius;
            Vector3 candidate = _spawnPos + new Vector3(rnd.x, 0f, rnd.y);

            if (NavMesh.SamplePosition(candidate, out NavMeshHit hit, 2.5f, NavMesh.AllAreas))
            {
                _agent.SetDestination(hit.position);
            }
            _patrolWaitTimer = patrolWaitTime;
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // AGGRO / PACK ATTACK
    // ─────────────────────────────────────────────────────────────────────────
    private void TickAggro()
    {
        float distToSpawn = Vector3.Distance(_spawnPos, PlayerHealth.Transform.position);
        float actualDistToPlayer = Vector3.Distance(transform.position, PlayerHealth.Transform.position);

        // Player chạy quá xa khỏi vùng Radar của con này -> rút khỏi bầy, quay lại Patrol
        if (distToSpawn > RadarRange)
        {
            LeavePack();
            ReturnToPatrol();
            return;
        }

        if (_canSeePlayerCache && actualDistToPlayer <= RadarRange)
        {
            UpdateTargetFlankPosition(PlayerHealth.Transform.position);
            _agent.SetDestination(LastKnownPosition);
            _lostSightTimer = lostSightCooldown;
        }
        else
        {
            _lostSightTimer -= Time.deltaTime;
            if (_lostSightTimer <= 0)
            {
                LeavePack();
                ReturnToPatrol();
            }
        }
    }

    private void ReturnToPatrol()
    {
        SetState(EnemyState.Idle);
        _returningToSpawn = true;
        _agent.SetDestination(_spawnPos);
    }

    // Tính vị trí bao vây dựa trên vai trò (_myFlankAngle) và hướng nhìn hiện tại của player
    // -> vòng vây luôn xoay theo player, không còn là offset world-space cố định
    private void UpdateTargetFlankPosition(Vector3 playerPos)
    {
        Vector3 playerForward = PlayerHealth.Transform.forward;
        float playerYaw = Mathf.Atan2(playerForward.x, playerForward.z) * Mathf.Rad2Deg;
        float worldAngle = playerYaw + _myFlankAngle;

        Vector3 offset = new Vector3(
            Mathf.Sin(worldAngle * Mathf.Deg2Rad),
            0,
            Mathf.Cos(worldAngle * Mathf.Deg2Rad));

        Vector3 targetFlankPoint = playerPos + offset * attackFlankRadius;

        if (NavMesh.SamplePosition(targetFlankPoint, out NavMeshHit hit, 3.0f, NavMesh.AllAreas))
        {
            LastKnownPosition = hit.position;
        }
        else
        {
            LastKnownPosition = playerPos;
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // PACK COORDINATOR — chia vai & chia đều góc bao vây cho cả nhóm
    // ─────────────────────────────────────────────────────────────────────────
    private void JoinPack()
    {
        if (!_packMembers.Contains(this))
            _packMembers.Add(this);

        ReassignPackRoles();
    }

    private void LeavePack()
    {
        if (_packMembers.Remove(this))
        {
            CurrentPackRole = PackRole.None;
            ReassignPackRoles();
        }
    }

    // Gọi lại mỗi khi có 1 con nhập/rời bầy -> chia lại đều 360° cho các con còn Aggro,
    // đảm bảo không có "lỗ hổng" trong vòng vây và không có 2 con nào trùng góc.
    private static void ReassignPackRoles()
    {
        int count = _packMembers.Count;
        if (count == 0) return;

        float angleStep = 360f / count;

        for (int i = 0; i < count; i++)
        {
            EnemyBrain member = _packMembers[i];
            float angle = i * angleStep; // 0 = trước mặt player, 180 = sau lưng player

            member._myFlankAngle = angle;
            member.CurrentPackRole = ClassifyRole(angle);
        }
    }

    // Chỉ để gắn nhãn vai trò cho animation/logic khác (VD: Vanguard đánh trực diện mạnh hơn,
    // Rear tấn công lén...). Vị trí thực tế vẫn do _myFlankAngle quyết định ở trên.
    private static PackRole ClassifyRole(float angle)
    {
        angle = ((angle % 360f) + 360f) % 360f;

        if (angle <= 45f || angle >= 315f) return PackRole.Vanguard;   // trước mặt player
        if (angle > 45f && angle < 135f) return PackRole.FlankRight;   // bên phải player
        if (angle >= 135f && angle <= 225f) return PackRole.Rear;      // sau lưng player
        return PackRole.FlankLeft;                                    // bên trái player
    }

    // ─────────────────────────────────────────────────────────────────────────
    private bool CheckLineOfSight()
    {
        if (PlayerHealth.Transform == null) return false;

        Vector3 eye = transform.position + Vector3.up * 1.5f;
        Vector3 target = PlayerHealth.Transform.position + Vector3.up * 1f;
        float distance = Vector3.Distance(eye, target);

        if (Physics.Raycast(eye, (target - eye).normalized, distance, LOSBlockingLayers))
        {
            return false;
        }
        return true;
    }

    public void SetState(EnemyState next)
    {
        if (State == next) return;
        State = next;
        OnStateChanged?.Invoke(next);
    }

    private void OnDrawGizmosSelected()
    {
        Vector3 radarCenter = Application.isPlaying ? _spawnPos : transform.position;
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(radarCenter, RadarRange);

        Gizmos.color = Color.cyan;
        Gizmos.DrawWireSphere(_spawnPos, patrolRadius);

        Gizmos.color = Color.blue;
        Gizmos.DrawWireSphere(transform.position, AggroRadius);

        if (Application.isPlaying && State == EnemyState.Aggro)
        {
            Gizmos.color = Color.red;
            Gizmos.DrawWireCube(LastKnownPosition, Vector3.one * 0.4f);
            Gizmos.DrawLine(transform.position, LastKnownPosition);
        }
    }
}