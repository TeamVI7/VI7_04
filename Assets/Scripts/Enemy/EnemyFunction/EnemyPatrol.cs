using UnityEngine;
using UnityEngine.AI;

[RequireComponent(typeof(NavMeshAgent))]
[RequireComponent(typeof(EnemyBrain))]
public class PatrolBehaviour : MonoBehaviour, IEnemyAimController
{
    public enum CombatMovementStyle { Standoff, Strafe, Aggressive }
    private enum FaceMode { None, Player, Movement }

    [Header("Movement")]
    public float PatrolSpeed = 2f;
    public float ChaseSpeed = 10f;
    public float PreferredRange = 10f;

    [Header("Retreat")]
    [Tooltip("If the player gets closer than this, the enemy backs away instead of holding position or continuing to close in. 0 disables retreating entirely.")]
    public float MinRange = 3f;

    [Header("Patrol")]
    public float PatrolRadius = 12f;
    public float WaypointWaitTime = 2.5f;

    [Header("Combat Style")]
    public CombatMovementStyle MovementStyle = CombatMovementStyle.Standoff;
    public float StrafeRadius = 3f;
    public float StrafeInterval = 1.2f;

    // Lets another behaviour (e.g. a sustained-fire attack rooting the enemy mid-burst)
    // temporarily scale movement speed without fighting this component for ownership of
    // _nav.speed, which it otherwise overwrites every frame. 1 = no change.
    [HideInInspector] public float ExternalSpeedMultiplier = 1f;

    // Lets another behaviour (e.g. EnemyDodgeBehaviour sidestepping) take the
    // NavMeshAgent's destination over completely for a short window. Without this,
    // this component's own per-frame SetDestination stomps whatever the other
    // behaviour just set, the very next tick.
    [HideInInspector] public bool MovementOverrideActive = false;

    private NavMeshAgent _nav;
    private EnemyBrain _brain;
    private Vector3 _spawnPos;
    private bool _hasWaypoint;
    private float _waitTimer;
    private float _strafeTimer;
    private int _formationSlot = -1;
    private bool _isFlanker;

    // Aggressive is "never stops closing" by design (melee rushers rely on it), so
    // retreat only applies there if this enemy actually has something to shoot with —
    // a melee-only Aggressive enemy should still barrel straight in.
    private bool _hasRangedAttack;

    // ── IEnemyAimController ─────────────────────────────────────────────────
    // Lowest priority — patrol/chase facing only wins when nothing else (attack
    // behaviours) currently wants control. Set by TickPatrol/TickChase each Update,
    // consumed by TickAim (called externally by EnemyAimCoordinator).
    private FaceMode _pendingFace = FaceMode.None;

    public int AimPriority => 0;
    public bool WantsAim => _pendingFace != FaceMode.None;

    public void TickAim(float deltaTime)
    {
        if (_pendingFace == FaceMode.Player) FacePlayer();
        else if (_pendingFace == FaceMode.Movement) FaceMovementDirection();
    }

    private void Awake()
    {
        _nav = GetComponent<NavMeshAgent>();
        _nav.updateRotation = false; // this component (and EnemyLookAt) own rotation now
        _brain = GetComponent<EnemyBrain>();
        _brain.OnStateChanged += OnStateChanged;
        _hasRangedAttack = GetComponent<EnemyRangedAttackBehaviour>() != null;
    }

    private void OnDestroy()
    {
        _brain.OnStateChanged -= OnStateChanged;
        if (_formationSlot >= 0) EnemyFormationCoordinator.Unregister(_formationSlot);
        if (_isFlanker) EnemyFormationCoordinator.ReleaseFlank();
    }

    private void Start() => _spawnPos = transform.position;

    private void Update()
    {
        _pendingFace = FaceMode.None;
        if (MovementOverrideActive) return;
        if (!_nav.enabled || !_nav.isOnNavMesh) return;

        switch (_brain.State)
        {
            case EnemyState.Idle: TickPatrol(); break;
            case EnemyState.Aggro: TickChase(); break;
        }
    }

    private void TickPatrol()
    {
        _nav.speed = PatrolSpeed * ExternalSpeedMultiplier;
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

        if (_hasWaypoint) _pendingFace = FaceMode.Movement;
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

    private bool TryRetreat(float dist)
    {
        if (MinRange <= 0f || dist >= MinRange) return false;

        _nav.speed = ChaseSpeed * ExternalSpeedMultiplier;
        _nav.SetDestination(GetRetreatPoint());
        _pendingFace = FaceMode.Player;
        return true;
    }

    private void TickChaseStandoff()
    {
        float dist = Vector3.Distance(transform.position, PlayerHealth.Transform.position);

        if (TryRetreat(dist)) return;

        if (dist > PreferredRange)
        {
            _nav.speed = ChaseSpeed * ExternalSpeedMultiplier;
            _nav.SetDestination(GetApproachPoint(PreferredRange));
            _pendingFace = FaceMode.Movement;
        }
        else
        {
            _nav.speed = PatrolSpeed * ExternalSpeedMultiplier;
            _nav.ResetPath();
            _pendingFace = FaceMode.Player;
        }
    }

    private void TickChaseAggressive()
    {
        float dist = Vector3.Distance(transform.position, PlayerHealth.Transform.position);

        if (_hasRangedAttack && TryRetreat(dist)) return;

        _nav.speed = ChaseSpeed * ExternalSpeedMultiplier;

        Vector3 destination = dist > PreferredRange
            ? GetApproachPoint(PreferredRange)
            : PlayerHealth.Transform.position;

        _nav.SetDestination(destination);

        _pendingFace = dist <= PreferredRange ? FaceMode.Player : FaceMode.Movement;
    }

    private void TickChaseStrafe()
    {
        float dist = Vector3.Distance(transform.position, PlayerHealth.Transform.position);

        if (TryRetreat(dist)) { _strafeTimer = 0f; return; }

        if (dist > PreferredRange * 1.3f)
        {
            _nav.speed = ChaseSpeed * ExternalSpeedMultiplier;
            _nav.SetDestination(GetApproachPoint(PreferredRange));
            _strafeTimer = 0f;
            _pendingFace = FaceMode.Movement;
            return;
        }

        _nav.speed = PatrolSpeed * ExternalSpeedMultiplier;
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

        _pendingFace = FaceMode.Player;
    }

    // Aims at the enemy's assigned formation slot, projected onto the ring around
    // the player at ringRadius, instead of the raw player position — this is what
    // spreads multiple aggro'd enemies across different approach angles instead of
    // all beelining the same point.
    private Vector3 GetApproachPoint(float ringRadius)
    {
        if (PlayerHealth.Transform == null) return transform.position;

        // Flankers ignore their ring slot and path to behind the player's current
        // facing instead — this is what makes a flank read as intentional rather
        // than just another point on the same symmetric ring everyone else uses.
        if (_isFlanker)
        {
            Vector3 behind = -PlayerHealth.Transform.forward;
            Vector3 flankPoint = PlayerHealth.Transform.position + behind * ringRadius;

            if (NavMesh.SamplePosition(flankPoint, out NavMeshHit flankHit, ringRadius * 0.5f + 1f, NavMesh.AllAreas))
                return flankHit.position;

            return PlayerHealth.Transform.position;
        }

        if (_formationSlot < 0) return PlayerHealth.Transform.position;

        Vector3 dir = EnemyFormationCoordinator.GetSlotDirection(_formationSlot);
        Vector3 point = PlayerHealth.Transform.position + dir * ringRadius;

        if (NavMesh.SamplePosition(point, out NavMeshHit hit, ringRadius * 0.5f + 1f, NavMesh.AllAreas))
            return hit.position;

        return PlayerHealth.Transform.position;
    }

    // Mirrors GetApproachPoint but projects away from the player instead of toward
    // them — used when the player has closed inside MinRange.
    private Vector3 GetRetreatPoint()
    {
        if (PlayerHealth.Transform == null) return transform.position;

        Vector3 away = transform.position - PlayerHealth.Transform.position;
        away.y = 0f;
        if (away.sqrMagnitude < 0.01f) away = -transform.forward;
        away.Normalize();

        Vector3 point = transform.position + away * (MinRange + 2f);

        if (NavMesh.SamplePosition(point, out NavMeshHit hit, MinRange + 2f, NavMesh.AllAreas))
            return hit.position;

        return transform.position;
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

            // Aggressive rushers already beeline the player by design — flanking
            // would just be a slower way of doing the same thing they already do.
            if (!_isFlanker && MovementStyle != CombatMovementStyle.Aggressive)
                _isFlanker = EnemyFormationCoordinator.TryClaimFlank();
        }
        else
        {
            if (_formationSlot >= 0)
            {
                EnemyFormationCoordinator.Unregister(_formationSlot);
                _formationSlot = -1;
            }
            if (_isFlanker)
            {
                EnemyFormationCoordinator.ReleaseFlank();
                _isFlanker = false;
            }
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

#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.cyan;
        Gizmos.DrawWireSphere(transform.position, PreferredRange);
        if (MinRange > 0f)
        {
            Gizmos.color = Color.red;
            Gizmos.DrawWireSphere(transform.position, MinRange);
        }
    }
#endif
}