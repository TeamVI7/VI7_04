using System;
using UnityEngine;

/// <summary>
/// Bộ điều khiển DUY NHẤT cho Drone: di chuyển (Patrol/Aggro FSM), animation nhấp nhô (hover),
/// và bắn laser khi đang Aggro trong tầm.
///
/// Gộp toàn bộ vì Drone là enemy đặc thù (bay tự do, không dùng NavMeshAgent) nên không thể
/// dùng chung EnemyBrain/PatrolBehaviour/LaserBehaviour vốn được thiết kế cho enemy đi bộ trên NavMesh.
///
/// EnemyHealth vẫn là script RIÊNG, KHÔNG đụng vào — nó tự lo trọn vẹn HP/nổ khi chết/rớt đồ,
/// không cần controller này "gọi" gì cả. Nếu bắn không nổ, lỗi nằm ở phía script vũ khí/collider,
/// không phải ở đây (xem ghi chú cuối file).
///
/// SETUP: gắn script này + EnemyHealth lên Drone. XÓA các component sau nếu còn:
/// NavMeshAgent, Enemy Brain, Patrol Behaviour, Laser Behaviour, Hover Animation — đều dư thừa,
/// script này đã thay thế toàn bộ chức năng của cả 4 cái đó.
/// </summary>
[RequireComponent(typeof(EnemyHealth))]
public class DroneController : MonoBehaviour
{
    private enum DroneState { Patrol, Aggro }

    [Header("Movement")]
    public float moveSpeed = 4f;
    public float aggroSpeed = 6f;
    public float aggroRadius = 15f;

    [Header("Patrol")]
    [Tooltip("Bán kính lượn quanh vị trí spawn ban đầu")]
    public float patrolRadius = 5f;
    [Tooltip("Khoảng cách coi như đã tới điểm patrol -> chọn điểm mới")]
    public float arriveThreshold = 1f;

    [Header("Hover Animation (Nhấp Nhô)")]
    [Tooltip("Biên độ nhấp nhô lên xuống")]
    public float hoverAmount = 0.2f;
    [Tooltip("Tốc độ nhấp nhô")]
    public float hoverSpeed = 1f;

    [Header("Laser")]
    public LineRenderer LaserPrefab;
    public Transform FirePoint;
    public float AttackRange = 30f;
    public float DamagePerSecond = 15f;
    public LayerMask ObstacleLayers;
    public float TargetHeightOffset = 0.5f;

    public event Action<bool> OnLaserToggled;

    private DroneState _currentState = DroneState.Patrol;
    private Vector3 _spawnPos;
    private Vector3 _patrolPoint;

    // Vị trí "logic" của đường bay (KHÔNG bao gồm hover) — dùng để tính khoảng cách/patrol chính xác,
    // tránh hover animation làm sai lệch các phép Vector3.Distance mỗi frame.
    private Vector3 _basePosition;
    private Vector3 _lastMoveDir;
    private float _hoverPhase; // lệch pha ngẫu nhiên để nhiều Drone không nhấp nhô đồng bộ y hệt nhau

    private EnemyHealth _health;
    private bool _isDead;

    private LineRenderer _laser;
    private bool _laserActive;

    private void Awake()
    {
        _health = GetComponent<EnemyHealth>();
        _health.OnDied += _ => { _isDead = true; SetLaser(false); };
        _health.OnStaggerEntered += () => SetLaser(false);

        _hoverPhase = UnityEngine.Random.Range(0f, Mathf.PI * 2f);

        if (LaserPrefab != null)
        {
            _laser = Instantiate(LaserPrefab, transform);
            _laser.positionCount = 2;
            _laser.useWorldSpace = true;
            _laser.enabled = false;
        }
    }

    private void OnDisable() => SetLaser(false);

    private void Start()
    {
        _spawnPos = transform.position;
        _basePosition = transform.position;
        PickNewPatrolPoint();
    }

    private void Update()
    {
        if (_isDead || !_health.IsAlive) return;

        Transform player = PlayerHealth.Transform;
        if (player == null) { SetLaser(false); return; }

        float dist = Vector3.Distance(_basePosition, player.position);
        _currentState = (dist <= aggroRadius) ? DroneState.Aggro : DroneState.Patrol;

        switch (_currentState)
        {
            case DroneState.Patrol:
                SetLaser(false);
                ExecutePatrol();
                break;
            case DroneState.Aggro:
                ExecuteAggro(player);
                break;
        }

        ApplyHoverAndRotation();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // MOVEMENT (FSM)
    // ─────────────────────────────────────────────────────────────────────────
    private void ExecutePatrol()
    {
        MoveToward(_patrolPoint, moveSpeed);

        if (Vector3.Distance(_basePosition, _patrolPoint) < arriveThreshold)
            PickNewPatrolPoint();
    }

    private void ExecuteAggro(Transform target)
    {
        MoveToward(target.position, aggroSpeed);

        float dist = Vector3.Distance(_basePosition, target.position);
        if (dist <= AttackRange)
        {
            SetLaser(true);
            UpdateBeam(target);
        }
        else
        {
            SetLaser(false);
        }
    }

    private void PickNewPatrolPoint()
    {
        Vector2 rnd = UnityEngine.Random.insideUnitCircle * patrolRadius;
        _patrolPoint = _spawnPos + new Vector3(rnd.x, 0f, rnd.y);
    }

    private void MoveToward(Vector3 dest, float speed)
    {
        Vector3 dir = (dest - _basePosition).normalized;
        _basePosition += dir * speed * Time.deltaTime;
        if (dir.sqrMagnitude > 0.0001f) _lastMoveDir = dir;
    }

    // Cộng offset nhấp nhô lên trục Y của _basePosition mỗi frame để ra vị trí hiển thị thật.
    // KHÔNG đụng vào _basePosition -> mọi phép tính khoảng cách ở trên luôn chính xác, không bị hover làm nhiễu.
    private void ApplyHoverAndRotation()
    {
        float hoverOffset = Mathf.Sin(Time.time * hoverSpeed + _hoverPhase) * hoverAmount;
        transform.position = _basePosition + Vector3.up * hoverOffset;

        if (_lastMoveDir.sqrMagnitude > 0.0001f)
            transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(_lastMoveDir), Time.deltaTime * 5f);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // LASER (gộp nguyên logic từ LaserBehaviour cũ, chỉ đổi nguồn điều kiện từ _brain.State -> _currentState)
    // ─────────────────────────────────────────────────────────────────────────
    private void UpdateBeam(Transform target)
    {
        if (_laser == null || FirePoint == null) return;

        Vector3 origin = FirePoint.position;
        Vector3 targetPos = target.position + Vector3.up * TargetHeightOffset;
        Vector3 dir = (targetPos - origin).normalized;

        _laser.SetPosition(0, origin);

        if (Physics.Raycast(origin, dir, out RaycastHit hit, AttackRange, ObstacleLayers))
        {
            _laser.SetPosition(1, hit.point);
            if (hit.collider.CompareTag("Player")) DealDamage();
        }
        else
        {
            _laser.SetPosition(1, targetPos);
            if (Physics.Raycast(origin, dir, out RaycastHit ph, AttackRange))
                if (ph.collider.CompareTag("Player")) DealDamage();
        }
    }

    private void DealDamage() => PlayerHealth.Instance?.TakeDamage(DamagePerSecond * Time.deltaTime);

    private void SetLaser(bool on)
    {
        if (_laserActive == on) return;
        _laserActive = on;
        if (_laser != null) _laser.enabled = on;
        OnLaserToggled?.Invoke(on);
    }
}
