using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Flight controller for airborne enemies — the NavMesh-free counterpart of
/// PatrolBehaviour. Drives transform.position directly with simple steering
/// (accelerate toward a desired point, hold an altitude over whatever ground is
/// below, push away from obstacles) and owns facing through IEnemyAimController
/// at the same lowest priority PatrolBehaviour uses, so attack behaviours still
/// win rotation while they aim.
///
/// EnemyBrain requires a NavMeshAgent, so one still exists on the object; this
/// component disables it in Awake because a flyer must never be snapped to the
/// walkable surface.
/// </summary>
[RequireComponent(typeof(EnemyBrain))]
public class FlyingMovement : MonoBehaviour, IEnemyAimController
{
    public enum FlightStyle
    {
        Orbit,      // holds PreferredRange and circles the player
        Standoff,   // holds PreferredRange and hangs still
        Aggressive, // never stops closing
    }

    private enum FaceMode { None, Player, Movement }

    [Header("Altitude")]
    [Tooltip("Height held above whatever ground is directly below.")]
    public float HoverAltitude = 6f;
    [Tooltip("Extra height held above the player while in combat. The flyer takes whichever is higher: ground+HoverAltitude, or player+CombatAltitudeAbovePlayer.")]
    public float CombatAltitudeAbovePlayer = 4f;
    [Tooltip("Vertical correction speed toward the target altitude.")]
    public float ClimbSpeed = 6f;
    [Tooltip("What counts as ground for the altitude probe. Should include terrain/floors but not the player.")]
    public LayerMask GroundLayers = ~0;

    [Header("Speed")]
    public float PatrolSpeed = 4f;
    public float ChaseSpeed = 9f;
    [Tooltip("How fast velocity converges on the steering target. Low = floaty drone, high = darty quadcopter.")]
    public float Acceleration = 10f;
    public float TurnSpeed = 6f;

    [Header("Engagement")]
    public FlightStyle Style = FlightStyle.Orbit;
    public float PreferredRange = 18f;
    [Tooltip("If the player closes inside this, the flyer backs off. 0 disables retreating.")]
    public float MinRange = 8f;
    [Tooltip("Seconds between picking a new point on the orbit ring (Orbit style only).")]
    public float OrbitInterval = 2.5f;
    public float OrbitRadius = 6f;

    [Header("Patrol")]
    public float PatrolRadius = 15f;
    public float WaypointWaitTime = 2f;

    [Header("Obstacle Avoidance")]
    [Tooltip("Layers the flyer steers around. Should NOT include the player or other enemies.")]
    public LayerMask ObstacleLayers;
    public float BodyRadius = 0.5f;
    [Tooltip("How far ahead the avoidance probe looks.")]
    public float AvoidDistance = 3.5f;
    public float AvoidStrength = 14f;

    [Header("Idle Bob")]
    public float BobAmplitude = 0.2f;
    public float BobFrequency = 1.5f;

    [Header("Banking (optional)")]
    [Tooltip("Child mesh root to roll into turns. Leave empty for no banking.")]
    public Transform VisualRoot;
    public float MaxBankAngle = 25f;

    // Lets an attack behaviour (the kamikaze dive) take position control over
    // completely for a window — mirrors PatrolBehaviour.MovementOverrideActive.
    [HideInInspector] public bool MovementOverrideActive;
    [HideInInspector] public float ExternalSpeedMultiplier = 1f;

    public Vector3 Velocity => _velocity;

    private EnemyBrain _brain;
    private EnemyHealth _health;
    private Vector3 _spawnPos;
    private Vector3 _velocity;
    private Vector3 _waypoint;
    private bool _hasWaypoint;
    private float _waitTimer;
    private float _orbitTimer;
    private Vector3 _orbitOffsetDir = Vector3.forward;
    private float _bobPhase;
    private float _bankAngle;

    private FaceMode _pendingFace = FaceMode.None;

    // ── IEnemyAimController ─────────────────────────────────────────────────
    public int AimPriority => 0;
    public bool WantsAim => _pendingFace != FaceMode.None;

    public void TickAim(float deltaTime)
    {
        Vector3 dir = _pendingFace == FaceMode.Player && PlayerHealth.Transform != null
            ? PlayerHealth.Transform.position - transform.position
            : _velocity;

        dir.y = 0f;
        if (dir.sqrMagnitude < 0.01f) return;

        transform.rotation = Quaternion.Slerp(transform.rotation,
            Quaternion.LookRotation(dir.normalized), TurnSpeed * deltaTime);
    }

    private void Awake()
    {
        _brain = GetComponent<EnemyBrain>();
        _health = GetComponent<EnemyHealth>();

        // A flyer must never be pulled down onto the walkable surface — the agent
        // only exists because EnemyBrain requires the component.
        if (TryGetComponent(out NavMeshAgent nav)) nav.enabled = false;

        _bobPhase = Random.Range(0f, Mathf.PI * 2f);
        _orbitOffsetDir = Random.insideUnitSphere;
        _orbitOffsetDir.y = 0f;
        _orbitOffsetDir = _orbitOffsetDir.sqrMagnitude < 0.01f ? Vector3.forward : _orbitOffsetDir.normalized;

        if (_health != null) _health.OnDied += _ => enabled = false;
    }

    private void Start() => _spawnPos = transform.position;

    private void Update()
    {
        _pendingFace = FaceMode.None;

        if (_brain.State == EnemyState.Dead) return;

        float dt = Time.deltaTime;

        if (MovementOverrideActive) return;

        if (_brain.State == EnemyState.Staggered)
        {
            // Lose thrust but keep some drift so a staggered drone visibly wobbles.
            _velocity = Vector3.Lerp(_velocity, Vector3.zero, 2f * dt);
            transform.position += _velocity * dt;
            return;
        }

        Vector3 desiredPoint = _brain.State == EnemyState.Aggro ? GetCombatPoint() : GetPatrolPoint();
        float speed = (_brain.State == EnemyState.Aggro ? ChaseSpeed : PatrolSpeed) * ExternalSpeedMultiplier;

        Steer(desiredPoint, speed, dt);
        ApplyBank(dt);
    }

    // ── Steering ────────────────────────────────────────────────────────────
    private void Steer(Vector3 targetPoint, float speed, float dt)
    {
        Vector3 toTarget = targetPoint - transform.position;

        // Horizontal and vertical are steered separately: horizontal chases the
        // target point at full speed, vertical is a softer altitude correction so
        // the flyer doesn't rocket up and down while repositioning.
        Vector3 horizontal = new Vector3(toTarget.x, 0f, toTarget.z);
        Vector3 desiredVel = horizontal.sqrMagnitude > 0.04f
            ? horizontal.normalized * Mathf.Min(speed, horizontal.magnitude * 2f)
            : Vector3.zero;

        desiredVel.y = Mathf.Clamp(toTarget.y * 2f, -ClimbSpeed, ClimbSpeed);
        desiredVel += GetAvoidance();

        _velocity = Vector3.Lerp(_velocity, desiredVel, Acceleration * dt);

        _bobPhase += dt * BobFrequency;
        float bob = Mathf.Cos(_bobPhase) * BobAmplitude * BobFrequency;

        transform.position += (_velocity + Vector3.up * bob) * dt;
    }

    // Probe along the direction of travel plus straight down, so the flyer both
    // stops nosing into walls and refuses to sink into geometry the ground probe
    // may have missed (overhangs, thin platforms).
    private Vector3 GetAvoidance()
    {
        Vector3 push = Vector3.zero;
        Vector3 dir = _velocity.sqrMagnitude > 0.01f ? _velocity.normalized : transform.forward;

        if (Physics.SphereCast(transform.position, BodyRadius, dir, out RaycastHit hit,
                               AvoidDistance, ObstacleLayers, QueryTriggerInteraction.Ignore))
        {
            float closeness = 1f - Mathf.Clamp01(hit.distance / AvoidDistance);
            push += hit.normal * (AvoidStrength * closeness);
        }

        if (Physics.SphereCast(transform.position, BodyRadius, Vector3.down, out RaycastHit below,
                               BodyRadius + 1f, ObstacleLayers, QueryTriggerInteraction.Ignore))
        {
            push += Vector3.up * (AvoidStrength * 0.5f * (1f - Mathf.Clamp01(below.distance / (BodyRadius + 1f))));
        }

        return push;
    }

    private void ApplyBank(float dt)
    {
        if (VisualRoot == null) return;

        // Roll proportional to how much of the velocity is sideways relative to facing.
        float lateral = Vector3.Dot(transform.right, _velocity);
        float target = Mathf.Clamp(-lateral / Mathf.Max(ChaseSpeed, 0.01f), -1f, 1f) * MaxBankAngle;

        _bankAngle = Mathf.Lerp(_bankAngle, target, 4f * dt);
        VisualRoot.localRotation = Quaternion.Euler(0f, 0f, _bankAngle);
    }

    // ── Target selection ────────────────────────────────────────────────────
    private Vector3 GetPatrolPoint()
    {
        if (!_hasWaypoint)
        {
            _waitTimer += Time.deltaTime;
            if (_waitTimer >= WaypointWaitTime)
            {
                Vector2 rand = Random.insideUnitCircle * PatrolRadius;
                _waypoint = _spawnPos + new Vector3(rand.x, Random.Range(-1f, 1f), rand.y);
                _hasWaypoint = true;
                _waitTimer = 0f;
            }
        }
        else
        {
            Vector3 flat = _waypoint - transform.position;
            flat.y = 0f;
            if (flat.magnitude <= 1.5f) { _hasWaypoint = false; _waitTimer = 0f; }
        }

        Vector3 point = _hasWaypoint ? _waypoint : transform.position;
        point.y = GetTargetAltitude(point, false);

        if (_hasWaypoint) _pendingFace = FaceMode.Movement;
        return point;
    }

    private Vector3 GetCombatPoint()
    {
        if (PlayerHealth.Transform == null) return GetPatrolPoint();

        Vector3 playerPos = PlayerHealth.Transform.position;
        Vector3 toSelf = transform.position - playerPos;
        toSelf.y = 0f;
        float dist = toSelf.magnitude;
        if (dist < 0.01f) { toSelf = -transform.forward; dist = 0.01f; }
        toSelf /= dist;

        _pendingFace = FaceMode.Player;

        Vector3 point;

        if (MinRange > 0f && dist < MinRange)
        {
            point = playerPos + toSelf * (MinRange + 2f);
        }
        else if (Style == FlightStyle.Aggressive)
        {
            point = playerPos;
            if (dist > PreferredRange) _pendingFace = FaceMode.Movement;
        }
        else if (Style == FlightStyle.Orbit)
        {
            _orbitTimer -= Time.deltaTime;
            if (_orbitTimer <= 0f)
            {
                _orbitTimer = OrbitInterval;
                Vector3 side = Vector3.Cross(Vector3.up, toSelf) * (Random.value < 0.5f ? 1f : -1f);
                _orbitOffsetDir = (toSelf * PreferredRange + side * OrbitRadius).normalized;
            }
            point = playerPos + _orbitOffsetDir * PreferredRange;
        }
        else // Standoff
        {
            point = playerPos + toSelf * PreferredRange;
        }

        point.y = GetTargetAltitude(point, true);
        return point;
    }

    // Ground probe from well above the sample point, so the flyer keeps its
    // altitude over whatever is actually below it rather than over its spawn floor.
    private float GetTargetAltitude(Vector3 atPoint, bool combat)
    {
        float groundY = atPoint.y - HoverAltitude;

        Vector3 probeOrigin = new Vector3(atPoint.x, transform.position.y + 5f, atPoint.z);
        if (Physics.Raycast(probeOrigin, Vector3.down, out RaycastHit hit, 200f, GroundLayers, QueryTriggerInteraction.Ignore))
            groundY = hit.point.y;

        float target = groundY + HoverAltitude;

        if (combat && PlayerHealth.Transform != null)
            target = Mathf.Max(target, PlayerHealth.Transform.position.y + CombatAltitudeAbovePlayer);

        return target;
    }

    /// <summary>
    /// Forces a fresh orbit position immediately — used by attack behaviours that
    /// want the flyer to relocate after firing instead of shooting from the same
    /// spot every cycle.
    /// </summary>
    public void Reposition()
    {
        _orbitTimer = 0f;
        _hasWaypoint = false;
        _waitTimer = WaypointWaitTime;
    }

    /// <summary>Hands a velocity back after an override (dive, knockback) so the flyer resumes smoothly.</summary>
    public void SetVelocity(Vector3 velocity) => _velocity = velocity;

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.cyan;
        Gizmos.DrawWireSphere(transform.position, PreferredRange);
        if (MinRange > 0f)
        {
            Gizmos.color = Color.red;
            Gizmos.DrawWireSphere(transform.position, MinRange);
        }

        Gizmos.color = Color.green;
        Gizmos.DrawLine(transform.position, transform.position + Vector3.down * HoverAltitude);
    }
}
