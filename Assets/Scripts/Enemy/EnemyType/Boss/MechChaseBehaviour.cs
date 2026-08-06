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

    [Header("Phase 2")]
    public float phase2SpeedMultiplier = 1.3f;

    [Header("Debug")]
    [SerializeField] private bool showDebugLogs = false;

    private NavMeshAgent _nav;
    private MechBossBrain _brain;
    private float _repositionTimer;
    private float _baseSpeed;

    private void Awake()
    {
        _nav   = GetComponent<NavMeshAgent>();
        _brain = GetComponent<MechBossBrain>();
        _baseSpeed = chaseSpeed;
        _nav.speed = _baseSpeed;

        _brain.OnPhaseChanged += HandlePhaseChanged;
    }

    private void OnDestroy() => _brain.OnPhaseChanged -= HandlePhaseChanged;

    private void HandlePhaseChanged(int prev, int next)
    {
        _nav.speed = _baseSpeed * (next >= 2 ? phase2SpeedMultiplier : 1f);
        Log($"Speed -> {_nav.speed:F1} (phase {next})");
    }

    private void Update()
    {
        if (!_nav.enabled || !_nav.isOnNavMesh) return;
        if (_brain.State != MechBossState.Idle) { _nav.ResetPath(); return; }
        if (PlayerHealth.Transform == null) return;

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
            FaceTarget();
        }
    }

    private void FaceTarget()
    {
        Vector3 dir = PlayerHealth.Transform.position - transform.position;
        dir.y = 0f;
        if (dir.sqrMagnitude < 0.01f) return;
        transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(dir), 4f * Time.deltaTime);
    }

    [System.Diagnostics.Conditional("UNITY_EDITOR")]
    private void Log(string msg)
    {
        if (showDebugLogs) Debug.Log($"[MechChaseBehaviour] {msg}", this);
    }
}