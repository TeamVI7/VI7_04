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
    [Tooltip("Trong phạm vi này, enemy phát hiện player dù KHÔNG nhìn thẳng mặt (mô phỏng phản xạ/giác quan khi bị áp sát, và bù cho việc NavMeshAgent tự xoay theo hướng di chuyển chứ không phải hướng player lúc patrol/investigate/về tổ).")]
    public float CloseRangeRadius = 4f;
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

    [Header("Predictive Investigate (Dự Đoán Hướng Tìm)")]
    [Tooltip("Khi mất dấu player, enemy sẽ lệch tâm vùng lùng sục về phía hướng player đang chạy lúc mất dấu (thay vì chỉ dò đều 360 độ quanh 1 điểm tĩnh). 0 = tắt tính năng, dò đều như cũ.")]
    public float PredictiveBiasDistance = 4f; // [NEW]
    [Tooltip("Độ mượt khi ước lượng vận tốc player (0-1). Càng cao càng phản ứng nhanh nhưng dễ giật do rung số.")]
    [Range(0.05f, 1f)] public float velocitySmoothing = 0.3f; // [NEW]
    private Vector3 _prevPlayerPos; // [NEW]
    private Vector3 _smoothedPlayerVelocity; // [NEW]
    private bool _hasPrevPlayerPos; // [NEW]
    private Vector3 _predictedSearchDirection; // [NEW] Hướng lệch tâm tìm kiếm, chốt lúc vừa vào Investigate

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

    // Thêm vào vùng khai báo private
    private Vector3 _currentSearchPoint; // Điểm đích đang đi tới
    private float _searchPointTimer;     // Timer để đổi hướng tìm kiếm

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

        // Xử lý line of sight
        _detectionTimer -= Time.deltaTime;
        if (_detectionTimer <= 0)
        {
            _canSeePlayerCache = CheckLineOfSight();
            _detectionTimer = detectionInterval;
        }

        // [NEW] Ước lượng vận tốc player MỖI FRAME (không gated theo detectionInterval để mượt hơn),
        // chỉ tính khi đang thực sự thấy player. Giá trị này được dùng làm "dự đoán" lúc mất dấu.
        TrackPlayerVelocityIfVisible();

        // FSM thực thụ: Chỉ 1 state được quyền chạy logic
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

    // [NEW] Theo dõi vị trí player mỗi frame để suy ra vận tốc (đã làm mượt bằng Lerp chống rung số).
    // Khi mất dấu (canSeePlayerCache == false), KHÔNG reset _smoothedPlayerVelocity - giữ nguyên giá trị
    // cuối cùng để dùng làm dự đoán hướng tìm kiếm trong Investigate. Chỉ reset _hasPrevPlayerPos để
    // tránh tính sai vận tốc "giật cục" khi thấy lại player sau một khoảng thời gian dài không track.
    private void TrackPlayerVelocityIfVisible()
    {
        if (!_canSeePlayerCache || PlayerHealth.Transform == null)
        {
            _hasPrevPlayerPos = false;
            return;
        }

        Vector3 currentPos = PlayerHealth.Transform.position;
        if (_hasPrevPlayerPos && Time.deltaTime > 0f)
        {
            Vector3 instantVelocity = (currentPos - _prevPlayerPos) / Time.deltaTime;
            _smoothedPlayerVelocity = Vector3.Lerp(_smoothedPlayerVelocity, instantVelocity, velocitySmoothing);
        }
        _prevPlayerPos = currentPos;
        _hasPrevPlayerPos = true;
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
            EnterInvestigate(); // [CHANGED] gộp về hàm chung, xem mục EnterInvestigate()
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
                EnterInvestigate(); // [CHANGED]
            }
        }
    }


    private void TickInvestigate()
    {
        // 1. Check điều kiện phát hiện: Nhìn thấy OR Player đứng quá gần (giác quan áp sát)
        float distToPlayer = Vector3.Distance(transform.position, PlayerHealth.Transform.position);

        // Nếu thấy hoặc chạy vào phạm vi "sát sườn"
        if ((_canSeePlayerCache || distToPlayer <= CloseRangeRadius)
            && Vector3.Distance(_spawnPos, PlayerHealth.Transform.position) <= RadarRange)
        {
            _agent.ResetPath(); // Cắt đường tìm kiếm cũ, quái sẽ dừng "rè rè" ngay
            JoinPack();
            _investigationTimer = 0f;
            _lostSightTimer = lostSightCooldown;
            SetState(EnemyState.Aggro);
            return;
        }

        // 2. Logic tìm kiếm (giữ nguyên)
        _investigationTimer += Time.deltaTime;
        if (_investigationTimer < 10.0f)
        {
            _searchPointTimer -= Time.deltaTime;
            if (_searchPointTimer <= 0 || _agent.remainingDistance < 0.5f)
            {
                Vector3 searchCenter = LastKnownPosition + _predictedSearchDirection * PredictiveBiasDistance;
                Vector3 randomOffset = UnityEngine.Random.insideUnitSphere * 3f;
                Vector3 target = searchCenter + randomOffset;

                if (NavMesh.SamplePosition(target, out NavMeshHit hit, 3.0f, NavMesh.AllAreas))
                {
                    _agent.SetDestination(hit.position);
                    _agent.isStopped = false;
                }
                _searchPointTimer = UnityEngine.Random.Range(2.0f, 4.0f);
            }
        }
        else
        {
            ReturnToPatrol();
        }
    }

    // [NEW] Điểm vào duy nhất khi chuyển sang Investigate - chốt hướng dự đoán tại thời điểm này
    // (dùng _smoothedPlayerVelocity đã tích luỹ được lúc còn thấy player), tránh việc hướng dự đoán
    // bị cập nhật lung tung giữa chừng investigate (vì lúc đó không còn thấy player để track vận tốc nữa).
    private void EnterInvestigate()
    {
        _investigationTimer = 0f;
        _predictedSearchDirection = _smoothedPlayerVelocity.sqrMagnitude > 0.01f
            ? _smoothedPlayerVelocity.normalized
            : Vector3.zero;
        SetState(EnemyState.Investigate);
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
        Gizmos.color = Color.green; Gizmos.DrawWireSphere(transform.position, CloseRangeRadius);
        if (Application.isPlaying && (State == EnemyState.Aggro || State == EnemyState.Investigate))
        {
            Gizmos.color = Color.red;
            Gizmos.DrawWireCube(LastKnownPosition, Vector3.one * 0.4f);
            Gizmos.DrawLine(transform.position, LastKnownPosition);

            // [NEW] Vẽ mũi tên trắng thể hiện hướng dự đoán + tâm vùng lùng sục thực tế (lệch tâm)
            if (State == EnemyState.Investigate && _predictedSearchDirection != Vector3.zero)
            {
                Gizmos.color = Color.white;
                Vector3 searchCenter = LastKnownPosition + _predictedSearchDirection * PredictiveBiasDistance;
                Gizmos.DrawLine(LastKnownPosition, searchCenter);
                Gizmos.DrawWireSphere(searchCenter, 3f);
            }
        }
    }
}