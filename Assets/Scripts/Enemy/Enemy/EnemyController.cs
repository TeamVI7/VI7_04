using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

public enum EnemyState { Idle, Aggro, Staggered, Dead, Investigate }
public enum PackRole { None, Vanguard, FlankLeft, FlankRight, Rear }

[RequireComponent(typeof(NavMeshAgent))]
[RequireComponent(typeof(EnemyHealth))]
public class EnemyBrain : MonoBehaviour
{
    [Header("Detection")]
    public float AggroRadius = 15f;
    public float RadarRange = 40f;
    public float ViewAngle = 100f; // Cải tiến: FOV (đây là TỔNG góc nhìn, ví dụ 100 = 50 độ mỗi bên)
    [Tooltip("Trong phạm vi này, enemy phát hiện player dù KHÔNG nhìn thẳng mặt (mô phỏng phản xạ/giác quan khi bị áp sát, và bù cho việc NavMeshAgent tự xoay theo hướng di chuyển chứ không phải hướng player lúc patrol/investigate/về tổ). Vẫn bị chặn bởi LOSBlockingLayers (không xuyên tường).")]
    public float CloseRangeRadius = 4f; // [NEW]
    public LayerMask LOSBlockingLayers;
    [Tooltip("Khoảng thời gian giữa các lần quét tầm nhìn")]
    [SerializeField] private float detectionInterval = 0.2f;
    private float _detectionTimer;

    [Header("Aggro Cooldown (Last Known Position)")]
    [SerializeField] private float lostSightCooldown = 3.0f;
    private float _lostSightTimer;

    [Header("Patrol (Khi mất dấu Player)")]
    [SerializeField] private float patrolRadius = 6f;
    [SerializeField] private float patrolWaitTime = 2.5f;
    [SerializeField] private float arriveThreshold = 0.5f;
    public float PatrolRadius { get => patrolRadius; set => patrolRadius = value; }
    public float PatrolWaitTime { get => patrolWaitTime; set => patrolWaitTime = value; }
    private float _patrolWaitTimer;
    private bool _returningToSpawn;

    [Header("Flanking System (Hội Đồng Bầy Đàn)")]
    [SerializeField] private float attackFlankRadius = 2.5f;
    public PackRole CurrentPackRole { get; private set; } = PackRole.None;
    private float _myFlankAngle;

    [Header("Hit Slowdown")]
    [Range(0.05f, 1f)] public float hitSlowdownMultiplier = 0.4f;
    public float hitSlowdownDuration = 0.6f;
    public bool stackSlowdownDuration = false;

    public event Action OnHitReceived;
    private float _hitSlowdownTimer;
    private float _baseAgentSpeed;

    private bool _hasExternalSpeedController;

    public float CurrentSpeedMultiplier => _hitSlowdownTimer > 0f ? hitSlowdownMultiplier : 1f;

    public Vector3 LastKnownPosition { get; private set; }
    public EnemyState State { get; private set; } = EnemyState.Idle;
    public event Action<EnemyState> OnStateChanged;

    private NavMeshAgent _agent;
    private EnemyHealth _health;
    private Vector3 _spawnPos;
    private bool _canSeePlayerCache;
    private static readonly List<EnemyBrain> _packMembers = new List<EnemyBrain>();

    private float _investigationTimer;

    private void Awake()
    {
        _agent = GetComponent<NavMeshAgent>();
        _health = GetComponent<EnemyHealth>();
        _baseAgentSpeed = _agent.speed;
        _hasExternalSpeedController = GetComponent<PatrolBehaviour>() != null;

        _health.OnDied += _ => { LeavePack(); SetState(EnemyState.Dead); };
        _health.OnStaggerEntered += () => SetState(EnemyState.Staggered);
        _health.OnStaggerExpired += () => SetState(EnemyState.Aggro);
        _health.OnDamaged += (currentHP, maxHP) => TriggerHitSlowdown();

        _spawnPos = transform.position;
    }

    private void OnDestroy() { LeavePack(); }

    private void TriggerHitSlowdown()
    {
        _hitSlowdownTimer = stackSlowdownDuration ? _hitSlowdownTimer + hitSlowdownDuration : hitSlowdownDuration;
        OnHitReceived?.Invoke();
    }

    private void Update()
    {
        if (State == EnemyState.Dead) return;
        if (State == EnemyState.Staggered) return;

        TickHitSlowdown();

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
            case EnemyState.Investigate: TickInvestigate(); break;
        }
    }

    private void TickHitSlowdown()
    {
        if (_hitSlowdownTimer <= 0f) return;

        _hitSlowdownTimer -= Time.deltaTime;
        if (_hitSlowdownTimer < 0f) _hitSlowdownTimer = 0f;

        if (!_hasExternalSpeedController)
        {
            _agent.speed = _baseAgentSpeed * CurrentSpeedMultiplier;
        }
    }

    private void TickIdle()
    {
        if (Vector3.Distance(transform.position, PlayerHealth.Transform.position) <= AggroRadius && _canSeePlayerCache)
        {
            JoinPack(); _lostSightTimer = lostSightCooldown; SetState(EnemyState.Aggro); return;
        }
        TickPatrol();
    }

    private void TickPatrol()
    {
        if (_returningToSpawn)
        {
            if (_agent.pathPending) return;
            if (_agent.remainingDistance <= arriveThreshold) { _returningToSpawn = false; _patrolWaitTimer = patrolWaitTime; }
            return;
        }
        if (_agent.pathPending) return;
        if (!_agent.hasPath || _agent.remainingDistance <= arriveThreshold)
        {
            if (_patrolWaitTimer > 0f) { _patrolWaitTimer -= Time.deltaTime; return; }
            Vector2 rnd = UnityEngine.Random.insideUnitCircle * patrolRadius;
            Vector3 candidate = _spawnPos + new Vector3(rnd.x, 0f, rnd.y);
            if (NavMesh.SamplePosition(candidate, out NavMeshHit hit, 2.5f, NavMesh.AllAreas)) _agent.SetDestination(hit.position);
            _patrolWaitTimer = patrolWaitTime;
        }
    }

    private void TickAggro()
    {
        if (Vector3.Distance(_spawnPos, PlayerHealth.Transform.position) > RadarRange)
        {
            LeavePack();
            _investigationTimer = 0f;
            SetState(EnemyState.Investigate);
            return;
        }

        if (_canSeePlayerCache)
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
                _investigationTimer = 0f;
                SetState(EnemyState.Investigate);
            }
        }
    }


    private void TickInvestigate()
    {
        if (_canSeePlayerCache && Vector3.Distance(_spawnPos, PlayerHealth.Transform.position) <= RadarRange)
        {
            JoinPack();
            _investigationTimer = 0f;
            _lostSightTimer = lostSightCooldown;
            SetState(EnemyState.Aggro);
            return;
        }

        if (Vector3.Distance(transform.position, LastKnownPosition) > 1.0f)
        {
            _agent.SetDestination(LastKnownPosition);
            return;
        }

        _agent.ResetPath();
        _investigationTimer += Time.deltaTime;

        if (_investigationTimer < 2.0f)
        {
            transform.Rotate(0, 45 * Time.deltaTime, 0);
        }
        else if (_investigationTimer < 6.0f)
        {
            if (!_agent.hasPath || _agent.remainingDistance < 0.5f)
            {
                Vector3 randomTarget = LastKnownPosition + (UnityEngine.Random.insideUnitSphere * 3f);
                if (NavMesh.SamplePosition(randomTarget, out NavMeshHit hit, 3.0f, NavMesh.AllAreas))
                    _agent.SetDestination(hit.position);
            }
        }
        else
        {
            _investigationTimer = 0;
            ReturnToPatrol();
        }
    }



    private void ReturnToPatrol()
    {
        LeavePack();
        SetState(EnemyState.Idle);
        _returningToSpawn = true;
        _patrolWaitTimer = patrolWaitTime;
        _agent.SetDestination(_spawnPos);
        Debug.Log("AI đã hoàn thành tìm kiếm và đang về tổ.");
    }

    private void UpdateTargetFlankPosition(Vector3 playerPos)
    {
        float playerYaw = Mathf.Atan2(PlayerHealth.Transform.forward.x, PlayerHealth.Transform.forward.z) * Mathf.Rad2Deg;
        float worldAngle = playerYaw + _myFlankAngle;
        Vector3 offset = new Vector3(Mathf.Sin(worldAngle * Mathf.Deg2Rad), 0, Mathf.Cos(worldAngle * Mathf.Deg2Rad));
        Vector3 targetFlankPoint = playerPos + offset * attackFlankRadius;
        LastKnownPosition = NavMesh.SamplePosition(targetFlankPoint, out NavMeshHit hit, 3.0f, NavMesh.AllAreas) ? hit.position : playerPos;
    }

    private void JoinPack() { if (!_packMembers.Contains(this)) _packMembers.Add(this); ReassignPackRoles(); }
    private void LeavePack() { if (_packMembers.Remove(this)) ReassignPackRoles(); }

    private static void ReassignPackRoles()
    {
        int count = _packMembers.Count;
        if (count == 0) return;
        float angleStep = 360f / count;
        for (int i = 0; i < count; i++) { _packMembers[i]._myFlankAngle = i * angleStep; _packMembers[i].CurrentPackRole = ClassifyRole(i * angleStep); }
    }

    private static PackRole ClassifyRole(float angle)
    {
        angle = ((angle % 360f) + 360f) % 360f;
        if (angle <= 45f || angle >= 315f) return PackRole.Vanguard;
        if (angle > 45f && angle < 135f) return PackRole.FlankRight;
        if (angle >= 135f && angle <= 225f) return PackRole.Rear;
        return PackRole.FlankLeft;
    }

    private bool CheckLineOfSight()
    {
        if (PlayerHealth.Transform == null) return false;
        Vector3 dir = (PlayerHealth.Transform.position - transform.position).normalized;
        float dist = Vector3.Distance(transform.position, PlayerHealth.Transform.position);

        // [FIX] Nếu ngoài FOV NHƯNG player áp sát trong CloseRangeRadius -> vẫn coi là phát hiện được
        // (bù cho việc transform.forward lúc patrol/investigate/về tổ tự xoay theo NavMeshAgent,
        // không phải hướng player - nguyên nhân enemy "trơ ra" dù player đứng ngay trước mặt).
        // Raycast chặn tường (LOSBlockingLayers) vẫn giữ nguyên bên dưới - không xuyên tường được.
        bool withinFOV = Vector3.Angle(transform.forward, dir) <= ViewAngle * 0.5f;
        bool withinCloseRange = dist <= CloseRangeRadius;

        if (!withinFOV && !withinCloseRange) return false;

        return !Physics.Raycast(transform.position + Vector3.up * 1.5f, dir, dist, LOSBlockingLayers);
    }

    public void SetState(EnemyState next) { if (State == next) return; State = next; OnStateChanged?.Invoke(next); }

    private void OnDrawGizmosSelected()
    {
        Vector3 radarCenter = Application.isPlaying ? _spawnPos : transform.position;
        Gizmos.color = Color.yellow; Gizmos.DrawWireSphere(radarCenter, RadarRange);
        Gizmos.color = Color.cyan; Gizmos.DrawWireSphere(_spawnPos, patrolRadius);
        Gizmos.color = Color.blue; Gizmos.DrawWireSphere(transform.position, AggroRadius);
        Gizmos.color = Color.green; Gizmos.DrawWireSphere(transform.position, CloseRangeRadius); // [NEW]
        if (Application.isPlaying && (State == EnemyState.Aggro || State == EnemyState.Investigate))
        {
            Gizmos.color = Color.red;
            Gizmos.DrawWireCube(LastKnownPosition, Vector3.one * 0.4f);
            Gizmos.DrawLine(transform.position, LastKnownPosition);
        }
    }
}