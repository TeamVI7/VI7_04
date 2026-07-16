using UnityEngine;
using UnityEngine.AI;

[RequireComponent(typeof(NavMeshAgent))]
[RequireComponent(typeof(EnemyBrain))]
public class PatrolBehaviour : MonoBehaviour
{
    public enum CombatMovementStyle { Standoff, Strafe, Aggressive }

    [Header("Movement")]
    public float PatrolSpeed = 2f;
    public float ChaseSpeed = 10f;
    public float PreferredRange = 10f;

    [Header("Patrol")]
    public float PatrolRadius = 12f;
    public float WaypointWaitTime = 2.5f;

    [Header("Combat Style")]
    public CombatMovementStyle MovementStyle = CombatMovementStyle.Standoff;
    public float StrafeRadius = 3f;
    public float StrafeInterval = 1.2f;

    private NavMeshAgent _nav;
    private EnemyBrain _brain;
    private Vector3 _spawnPos;
    private bool _hasWaypoint;
    private float _waitTimer;
    private float _strafeTimer;
    private int _formationSlot = -1;

    private void Awake()
    {
        _nav = GetComponent<NavMeshAgent>();
        _nav.updateRotation = false; // this component (and EnemyLookAt) own rotation now
        _brain = GetComponent<EnemyBrain>();
        _brain.OnStateChanged += OnStateChanged;
    }

    private void OnDestroy()
    {
        _brain.OnStateChanged -= OnStateChanged;
        if (_formationSlot >= 0) EnemyFormationCoordinator.Unregister(_formationSlot);
    }

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

        if (_hasWaypoint) FaceMovementDirection();
    }

    private void TickChase()
    {
        if (PlayerHealth.Transform == null) return;

        switch (MovementStyle)
        {
            case CombatMovementStyle.Aggressive: TickChaseAggressive(); break;
            case CombatMovementStyle.Strafe:     TickChaseStrafe();     break;
            default:                              TickChaseStandoff();   break;
        }
    }

    private void TickChaseStandoff()
    {
        float dist = Vector3.Distance(transform.position, PlayerHealth.Transform.position);

        if (dist > PreferredRange)
        {
            _nav.speed = ChaseSpeed;
            _nav.SetDestination(GetApproachPoint(PreferredRange));
            FaceMovementDirection();
        }
        else
        {
            _nav.speed = PatrolSpeed;
            _nav.ResetPath();
            FacePlayer();
        }
    }

    private void TickChaseAggressive()
    {
        float dist = Vector3.Distance(transform.position, PlayerHealth.Transform.position);
        _nav.speed = ChaseSpeed;

        Vector3 destination = dist > PreferredRange
            ? GetApproachPoint(PreferredRange)
            : PlayerHealth.Transform.position;

        _nav.SetDestination(destination);

        if (dist <= PreferredRange) FacePlayer();
        else FaceMovementDirection();
    }

    private void TickChaseStrafe()
    {
        float dist = Vector3.Distance(transform.position, PlayerHealth.Transform.position);

        if (dist > PreferredRange * 1.3f)
        {
            _nav.speed = ChaseSpeed;
            _nav.SetDestination(GetApproachPoint(PreferredRange));
            _strafeTimer = 0f;
            FaceMovementDirection();
            return;
        }

        _nav.speed = PatrolSpeed;
        _strafeTimer -= Time.deltaTime;

        if (_strafeTimer <= 0f)
        {
            _strafeTimer = StrafeInterval;

            Vector3 toSelf = transform.position - PlayerHealth.Transform.position;
            toSelf.y = 0f;
            if (toSelf.sqrMagnitude < 0.01f) toSelf = Random.insideUnitSphere;
            toSelf.Normalize();

            Vector3 side = Vector3.Cross(Vector3.up, toSelf) * (Random.value < 0.5f ? 1f : -1f);
            Vector3 strafePoint = PlayerHealth.Transform.position + toSelf * PreferredRange + side * StrafeRadius;

            if (NavMesh.SamplePosition(strafePoint, out NavMeshHit hit, StrafeRadius * 1.5f, NavMesh.AllAreas))
                _nav.SetDestination(hit.position);
        }

        FacePlayer();
    }

    // Aims at the enemy's assigned formation slot, projected onto the ring around
    // the player at ringRadius, instead of the raw player position — this is what
    // spreads multiple aggro'd enemies across different approach angles instead of
    // all beelining the same point.
    private Vector3 GetApproachPoint(float ringRadius)
    {
        if (PlayerHealth.Transform == null) return transform.position;
        if (_formationSlot < 0) return PlayerHealth.Transform.position;

        Vector3 dir = EnemyFormationCoordinator.GetSlotDirection(_formationSlot);
        Vector3 point = PlayerHealth.Transform.position + dir * ringRadius;

        if (NavMesh.SamplePosition(point, out NavMeshHit hit, ringRadius * 0.5f + 1f, NavMesh.AllAreas))
            return hit.position;

        return PlayerHealth.Transform.position;
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

    private void FaceMovementDirection()
    {
        Vector3 vel = _nav.desiredVelocity;
        vel.y = 0f;
        if (vel.sqrMagnitude < 0.01f) return;
        transform.rotation = Quaternion.Slerp(transform.rotation,
            Quaternion.LookRotation(vel.normalized), 8f * Time.deltaTime);
    }

    private void OnStateChanged(EnemyState state)
    {
        if (state == EnemyState.Aggro)
        {
            if (_formationSlot < 0) _formationSlot = EnemyFormationCoordinator.Register();
        }
        else if (_formationSlot >= 0)
        {
            EnemyFormationCoordinator.Unregister(_formationSlot);
            _formationSlot = -1;
        }

        if (!_nav) return;
        bool canMove = state == EnemyState.Idle || state == EnemyState.Aggro;
        _nav.enabled = canMove;

        if (state == EnemyState.Idle)
        {
            _hasWaypoint = false;
            _waitTimer = WaypointWaitTime;
            _strafeTimer = 0f;
            if (_nav.enabled && _nav.isOnNavMesh) _nav.ResetPath();
        }

        if (state == EnemyState.Staggered || state == EnemyState.Dead)
        {
            _strafeTimer = 0f;
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