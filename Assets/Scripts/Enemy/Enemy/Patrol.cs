using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Handles NavMeshAgent movement — patrol when Idle, chase when Aggro.
/// Drop on any enemy that moves. Remove it from enemies that don't (turrets, etc.).
/// </summary>
[RequireComponent(typeof(NavMeshAgent))]
[RequireComponent(typeof(EnemyBrain))]
public class PatrolBehaviour : MonoBehaviour
{
    [Header("Movement")]
    public float PatrolSpeed = 2f;
    public float ChaseSpeed = 10f;
    public float PreferredRange = 10f;

    [Header("Patrol")]
    public float PatrolRadius = 12f;
    public float WaypointWaitTime = 2.5f;

    private NavMeshAgent _nav;
    private EnemyBrain _brain;
    private Vector3 _spawnPos;
    private bool _hasWaypoint;
    private float _waitTimer;

    private void Awake()
    {
        _nav = GetComponent<NavMeshAgent>();
        _brain = GetComponent<EnemyBrain>();
        _brain.OnStateChanged += OnStateChanged;
    }

    private void OnDestroy() => _brain.OnStateChanged -= OnStateChanged;

    private void Start() => _spawnPos = transform.position;

    private void Update()
    {
        if (!_nav.enabled || !_nav.isOnNavMesh) return;

        switch (_brain.State)
        {
            case EnemyState.Idle: TickPatrol(); break;
            case EnemyState.Aggro: TickChase(); break;
        }
    }

    private void TickPatrol()
    {
        _nav.speed = PatrolSpeed;
        if (!_hasWaypoint)
        {
            _waitTimer += Time.deltaTime;
            if (_waitTimer >= WaypointWaitTime) FindWaypoint();
        }
        else if (!_nav.pathPending && _nav.remainingDistance <= _nav.stoppingDistance)
        {
            _hasWaypoint = false;
            _waitTimer = 0f;
        }
    }

    private void TickChase()
    {
        if (PlayerHealth.Transform == null) return;
        float dist = Vector3.Distance(transform.position, PlayerHealth.Transform.position);

        if (dist > PreferredRange)
        {
            _nav.speed = ChaseSpeed;
            _nav.SetDestination(PlayerHealth.Transform.position);
        }
        else
        {
            _nav.speed = PatrolSpeed;
            _nav.ResetPath();
            FacePlayer();
        }
    }

    public void FacePlayer()
    {
        if (PlayerHealth.Transform == null) return;
        Vector3 dir = PlayerHealth.Transform.position - transform.position;
        dir.y = 0f;
        if (dir != Vector3.zero)
            transform.rotation = Quaternion.Slerp(transform.rotation,
                Quaternion.LookRotation(dir), 8f * Time.deltaTime);
    }

    private void OnStateChanged(EnemyState state)
    {
        if (!_nav) return;
        bool canMove = state == EnemyState.Idle || state == EnemyState.Aggro;
        _nav.enabled = canMove;

        // KHI QUÁI MẤT DẤU PLAYER VÀ QUAY VỀ TRẠNG THÁI IDLE
        if (state == EnemyState.Idle)
        {
            _hasWaypoint = false;   // Xóa cờ hiệu đường đi cũ
            _waitTimer = WaypointWaitTime; // Ép timer đạt tối đa để LẬP TỨC tìm đường đi tuần mới ở khung hình tiếp theo, không bắt quái đứng đợi 2.5s nữa
            if (_nav.enabled && _nav.isOnNavMesh) _nav.ResetPath(); // Xóa mục tiêu đuổi theo Player cũ
        }

        if (state == EnemyState.Staggered || state == EnemyState.Dead)
        {
            if (_nav.enabled && _nav.isOnNavMesh) _nav.ResetPath();
        }
    }

    private void FindWaypoint()
    {
        Vector3 rand = Random.insideUnitSphere * PatrolRadius + _spawnPos;
        if (NavMesh.SamplePosition(rand, out NavMeshHit hit, PatrolRadius, NavMesh.AllAreas))
        {
            _nav.SetDestination(hit.position);
            _hasWaypoint = true;
        }
        _waitTimer = 0f;
    }
}