// MechChaseBehaviour.cs
using UnityEngine;
using UnityEngine.AI;

[RequireComponent(typeof(NavMeshAgent))]
[RequireComponent(typeof(MechBossBrain))]
public class MechChaseBehaviour : MonoBehaviour
{
    [Header("Movement")]
    public float chaseSpeed      = 4f;
    public float preferredRange  = 6f;   // stop approaching once this close
    public float repositionRate  = 0.3f; // seconds between destination updates

    [Header("Phase Speed")]
    public float phase2SpeedMultiplier = 1.3f;
    public float phase3SpeedMultiplier = 1.6f;

    [Header("Locomotion Animation")]
    [Tooltip("Optional. Auto-found in children if left empty. Only needed for the two options below.")]
    public Animator animator;
    [Tooltip("Float parameter on the controller to feed the mech's actual ground speed into, for a walk/run blend tree. Leave empty if your controller has no such parameter.")]
    public string speedParameter = "";
    [Tooltip("Smoothing on that parameter, in seconds. Stops the blend snapping when the agent brakes and re-accelerates between attacks.")]
    public float speedParameterDamping = 0.15f;
    [Tooltip("Scale the walk clip's playback to match how fast the mech is really travelling. Fixes the skating look you get when the body moves faster than the clip was authored for — but only use it if the controller has no blend tree, since a blend tree does this better.")]
    public bool matchClipToSpeed = false;
    [Tooltip("Ground speed the walk clip was animated for. Playback is scaled by actualSpeed / this.")]
    public float clipAuthoredSpeed = 2f;
    [Tooltip("Clamp on that playback scale, so a sprint doesn't turn the walk into a blur.")]
    public Vector2 clipSpeedClamp = new Vector2(0.6f, 2f);

    [Header("Combat Facing")]
    [Tooltip("Turn speed (Slerp factor) used to face the player while idle at close range.")]
    public float faceTurnSpeed = 4f;
    [Tooltip("Turn speed used to face the player while an attack is executing. Usually faster than the idle value so the body keeps up with a moving player during sustained attacks like the Gatling.")]
    public float attackFaceTurnSpeed = 6f;

    [Header("Debug")]
    [SerializeField] private bool showDebugLogs = false;

    private NavMeshAgent _nav;
    private MechBossBrain _brain;
    private MechAttackBehaviour[] _attacks;
    private float _repositionTimer;
    private float _baseSpeed;
    private int _speedParamHash;

    private void Awake()
    {
        _nav   = GetComponent<NavMeshAgent>();
        _brain = GetComponent<MechBossBrain>();
        _attacks = GetComponents<MechAttackBehaviour>();

        if (animator == null) animator = GetComponentInChildren<Animator>();
        if (!string.IsNullOrEmpty(speedParameter)) _speedParamHash = Animator.StringToHash(speedParameter);
        _baseSpeed = chaseSpeed;
        _nav.speed = _baseSpeed;

        _brain.OnPhaseChanged += HandlePhaseChanged;
    }

    private void OnDestroy() => _brain.OnPhaseChanged -= HandlePhaseChanged;

    private void HandlePhaseChanged(int prev, int next)
    {
        float mult = next >= 3 ? phase3SpeedMultiplier
                   : next >= 2 ? phase2SpeedMultiplier
                   : 1f;
        _nav.speed = _baseSpeed * mult;
        Log($"Speed -> {_nav.speed:F1} (phase {next})");
    }

    private void Update()
    {
        // Ahead of the guards below: those bail out whenever the mech isn't
        // chasing, and the locomotion still needs to be told to settle back to
        // idle in exactly those cases.
        UpdateLocomotionAnimation();

        if (!_nav.enabled || !_nav.isOnNavMesh) return;
        if (PlayerHealth.Transform == null) return;

        bool attacking = _brain.State == MechBossState.Attacking;

        if (attacking)
        {
            // Keep tracking the player with the body even mid-attack — previously
            // this whole method bailed out on any non-Idle state, which is why the
            // mech went completely static (no turning, no repositioning) for the
            // entire duration of every attack.
            FaceTarget(attackFaceTurnSpeed);

            if (!CurrentAttackAllowsMovement())
            {
                _nav.isStopped = true;
                _nav.ResetPath();
                return;
            }
        }
        else if (_brain.State != MechBossState.Idle)
        {
            _nav.ResetPath();
            return;
        }

        _repositionTimer -= Time.deltaTime;
        if (_repositionTimer > 0f) return;
        _repositionTimer = repositionRate;

        float dist = Vector3.Distance(transform.position, PlayerHealth.Transform.position);

        if (dist > preferredRange)
        {
            _nav.isStopped = false;
            _nav.SetDestination(PlayerHealth.Transform.position);
            Log($"Chasing — dist {dist:F1}");
        }
        else
        {
            _nav.isStopped = true;
            if (!attacking) FaceTarget(faceTurnSpeed);
        }
    }

    /// <summary>Whether the attack currently running on this mech (if any) wants the
    /// chase behaviour to keep repositioning it. See <see cref="MechAttackBehaviour.AllowsMovementDuringExecution"/>.</summary>
    private bool CurrentAttackAllowsMovement()
    {
        foreach (var a in _attacks)
            if (a.IsExecuting) return a.AllowsMovementDuringExecution;
        return false;
    }

    /// <summary>Feeds real ground speed to the animator. Without this the walk clip
    /// plays at a fixed cadence no matter how fast the body is actually travelling,
    /// which is what makes a fast mech look like it's skating and a slow one look
    /// like it's marching on the spot.</summary>
    private void UpdateLocomotionAnimation()
    {
        if (animator == null) return;

        float speed = (_nav.enabled && _nav.isOnNavMesh)
            ? Vector3.ProjectOnPlane(_nav.velocity, Vector3.up).magnitude
            : 0f;

        if (_speedParamHash != 0)
            animator.SetFloat(_speedParamHash, speed, speedParameterDamping, Time.deltaTime);

        if (!matchClipToSpeed) return;

        // Left alone during attacks — attack clips are timed against animation
        // events and windup values, and rescaling them would desync every telegraph.
        bool walking = speed > 0.1f && _brain.State != MechBossState.Attacking;
        animator.speed = walking
            ? Mathf.Clamp(speed / Mathf.Max(0.01f, clipAuthoredSpeed), clipSpeedClamp.x, clipSpeedClamp.y)
            : 1f;
    }

    private void FaceTarget(float turnSpeed)
    {
        Vector3 dir = PlayerHealth.Transform.position - transform.position;
        dir.y = 0f;
        if (dir.sqrMagnitude < 0.01f) return;
        transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(dir), turnSpeed * Time.deltaTime);
    }

    [System.Diagnostics.Conditional("UNITY_EDITOR")]
    private void Log(string msg)
    {
        if (showDebugLogs) Debug.Log($"[MechChaseBehaviour] {msg}", this);
    }
}