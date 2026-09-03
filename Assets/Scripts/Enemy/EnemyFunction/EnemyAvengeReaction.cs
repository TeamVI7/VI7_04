using System;
using System.Collections;
using UnityEngine;

// When this enemy dies, nearby squadmates react: Idle ones are instantly alerted
// into Aggro, already-Aggro ones get a temporary chase-speed spike. Registers with
// EnemySquadCoordinator so a death only has to check nearby registered reactors,
// not every enemy in the scene.
[RequireComponent(typeof(EnemyBrain))]
[RequireComponent(typeof(EnemyHealth))]
public class EnemyAvengeReaction : MonoBehaviour
{
    [Header("Trigger")]
    public float AvengeRadius = 15f;

    [Header("Enrage (applies only to squadmates already Aggro)")]
    [Tooltip("Multiplies PatrolBehaviour.ChaseSpeed for EnrageDuration seconds.")]
    public float EnrageSpeedMultiplier = 1.3f;
    public float EnrageDuration = 4f;

    [Header("Debug")]
    [SerializeField] private bool debugLog = false;

    // Fired whenever this enemy receives (not sends) an avenge signal — EnemyAudio
    // subscribes to this for a bark.
    public event Action OnAvengeTriggered;

    private EnemyBrain      _brain;
    private EnemyHealth     _health;
    private PatrolBehaviour _patrol;

    private Coroutine _enrageRoutine;
    private bool      _enrageActive;
    private float     _originalChaseSpeed;

    private void Awake()
    {
        _brain  = GetComponent<EnemyBrain>();
        _health = GetComponent<EnemyHealth>();
        _patrol = GetComponent<PatrolBehaviour>();
    }

    private void OnEnable()
    {
        _health.OnDied += HandleDied;
        EnemySquadCoordinator.RegisterAvenger(this);
    }

    private void OnDisable()
    {
        _health.OnDied -= HandleDied;
        EnemySquadCoordinator.UnregisterAvenger(this);

        if (_enrageRoutine != null) { StopCoroutine(_enrageRoutine); _enrageRoutine = null; }
        EndEnrage();
    }

    private void HandleDied(Vector3 impulse)
    {
        Log("Died — notifying nearby squadmates.");
        EnemySquadCoordinator.NotifyAllyDied(transform.position, this, AvengeRadius);
    }

    // Called by EnemySquadCoordinator on every OTHER registered reactor within
    // AvengeRadius of the enemy that just died.
    public void ReceiveAvengeSignal()
    {
        if (_brain.State == EnemyState.Idle)
        {
            Log("Avenge signal received while Idle — alerted straight to Aggro.");
            _brain.SetState(EnemyState.Aggro);
        }
        else if (_brain.State == EnemyState.Aggro && _patrol != null)
        {
            Log("Avenge signal received while Aggro — enraging.");
            if (_enrageRoutine != null) StopCoroutine(_enrageRoutine);
            else _originalChaseSpeed = _patrol.ChaseSpeed;

            _enrageActive = true;
            _patrol.ChaseSpeed = _originalChaseSpeed * EnrageSpeedMultiplier;
            _enrageRoutine = StartCoroutine(Co_Enrage());
        }
        // Staggered/Dead — nothing meaningful to react with, ignore.

        OnAvengeTriggered?.Invoke();
    }

    private IEnumerator Co_Enrage()
    {
        yield return new WaitForSeconds(EnrageDuration);
        EndEnrage();
    }

    private void EndEnrage()
    {
        if (_enrageActive && _patrol != null) _patrol.ChaseSpeed = _originalChaseSpeed;
        _enrageActive = false;
        _enrageRoutine = null;
    }

    [System.Diagnostics.Conditional("UNITY_EDITOR")]
    private void Log(string msg)
    {
        if (debugLog) Debug.Log($"[EnemyAvenge] {name}: {msg}", this);
    }

#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        // Dashed: this isn't a reach, it's an earshot — allies dying inside it pull
        // this enemy into aggro.
        Vector3 center = EnemyGizmos.Ground(transform.position);
        EnemyGizmos.GroundRing(center, AvengeRadius, EnemyGizmos.Avenge,
                               $"avenge {AvengeRadius:0.#}m", 200f, dashed: true);
    }
#endif
}
