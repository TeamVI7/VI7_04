using UnityEngine;
using UnityEngine.AI;

[RequireComponent(typeof(NavMeshAgent))]
public class EnemyFodder : EnemyBase
{
    [Header("Nav")]
    public float MoveSpeed  = 4.5f;
    public float ChaseSpeed = 10f;
    public float PreferredRange = 10f;

    [Header("Radar")]
    public float RadarRange = 40f;

    [Header("Patrol")]
    public float PatrolSpeed      = 2.0f;
    public float PatrolRadius     = 12f;
    public float WaypointWaitTime = 2.5f;

    [Header("LOS")]
    [Tooltip("Layers that block line-of-sight. Assign wall/geometry layers here.")]
    public LayerMask LOSBlockingLayers;

    // Shared attack point — subclasses define their own attack logic
    public Transform FirePoint;

    protected NavMeshAgent _nav;
    protected Transform    _player;

    private Vector3 _spawnPosition;
    protected bool  _hasPatrolTarget;
    private float   _waitTimer;

    protected override void Awake()
    {
        Tier  = EnemyTier.Fodder;
        MaxHP = 100f;
        base.Awake();

        _nav       = GetComponent<NavMeshAgent>();
        _nav.speed = MoveSpeed;
    }

    private void Start()
    {
        _spawnPosition = transform.position;
        GameObject playerObj = GameObject.FindGameObjectWithTag("Player");
        if (playerObj != null) _player = playerObj.transform;
    }

    protected override void TickIdle()
    {
        if (TryRadarDetectPlayer(out float radarDist))
        {
            if (radarDist <= AggroRadius)
            {
                _hasPatrolTarget = false;
                TransitionTo(EnemyState.Aggro);
                return;
            }

            // Subclasses can still fire during idle if in range
            if (radarDist <= GetAttackRange())
            {
                FacePlayer();
                TryFireProjectile(radarDist);
            }
        }

        if (_nav == null || !_nav.enabled || !_nav.isOnNavMesh) return;
        _nav.speed = PatrolSpeed;

        if (!_hasPatrolTarget)
        {
            _waitTimer += Time.deltaTime;
            if (_waitTimer >= WaypointWaitTime) FindNewPatrolPoint();
        }
        else if (!_nav.pathPending && _nav.remainingDistance <= _nav.stoppingDistance)
        {
            _hasPatrolTarget = false;
            _waitTimer       = 0f;
        }
    }

    protected override void TickAggro()
    {
        if (_nav != null && _nav.isStopped) _nav.isStopped = false;
        if (_player == null) return;

        float dist = Vector3.Distance(transform.position, _player.position);

        if (dist > RadarRange)
        {
            _nav.ResetPath();
            _hasPatrolTarget = false;
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

        TryFireProjectile(dist);
    }

    /// <summary>
    /// Override in subclasses to define attack behaviour.
    /// Base does nothing — EnemyFodder itself has no weapon.
    /// </summary>
    protected virtual void TryFireProjectile(float dist) { }

    /// <summary>
    /// Override to return the range at which TryFireProjectile is called.
    /// Defaults to AggroRadius if not overridden.
    /// </summary>
    protected virtual float GetAttackRange() => AggroRadius;

    /// <summary>
    /// LOS check with wall raycast. Returns false if a blocking layer is in the way.
    /// If LOSBlockingLayers is empty the raycast hits nothing and LOS is always clear.
    /// </summary>
    protected bool TryRadarDetectPlayer(out float distance)
    {
        distance = float.MaxValue;
        if (_player == null) return false;

        distance = Vector3.Distance(transform.position, _player.position);
        if (distance > RadarRange) return false;

        Vector3 eyePos    = transform.position + Vector3.up * 1.5f;
        Vector3 targetPos = _player.position   + Vector3.up * 1.0f;
        float   dist2     = Vector3.Distance(eyePos, targetPos);

        if (Physics.Raycast(eyePos, (targetPos - eyePos).normalized, dist2, LOSBlockingLayers))
            return false;

        return true;
    }

    protected void FacePlayer()
    {
        if (_player == null) return;
        Vector3 dir = _player.position - transform.position;
        dir.y = 0f;
        if (dir != Vector3.zero)
            transform.rotation = Quaternion.Slerp(transform.rotation,
                Quaternion.LookRotation(dir), 8f * Time.deltaTime);
    }

    protected override void OnStateEntered(EnemyState newState)
    {
        if (_nav == null) return;
        _nav.enabled = (newState == EnemyState.Idle || newState == EnemyState.Aggro);
    }

    private void FindNewPatrolPoint()
    {
        Vector3 randomDir = Random.insideUnitSphere * PatrolRadius + _spawnPosition;
        if (NavMesh.SamplePosition(randomDir, out NavMeshHit hit, PatrolRadius, NavMesh.AllAreas))
        {
            _nav.SetDestination(hit.position);
            _hasPatrolTarget = true;
        }
        _waitTimer = 0f;
    }
}