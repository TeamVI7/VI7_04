using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

[RequireComponent(typeof(EnemyBrain))]
public class EnemyAnimatorController : MonoBehaviour
{
    // ─────────────────────────────────────────────────────────────────────────
    #region Inspector
    // ─────────────────────────────────────────────────────────────────────────

    [Header("References")]
    public Animator     animator;
    public NavMeshAgent  agent;

    [Header("Speed Blend")]
    [Tooltip("NavMeshAgent speed considered 'full run' for normalising the Speed param.")]
    public float runSpeedReference = 10f;
    public float speedSmoothing    = 10f;

    [Header("Animator Parameter Names")]
    [SerializeField] private string speedParam     = "Speed";
    [SerializeField] private string punchTrigger    = "Punch";
    [SerializeField] private string staggerTrigger  = "Stagger";
    [SerializeField] private string deadTrigger     = "Die";
    [SerializeField] private string isChasingBool   = "IsChasing"; // future use, safe if absent
    [SerializeField] private string weaponTypeParam = "WeaponType"; // Int — set once, drives Equals-based idle transitions

    [Header("Debug")]
    [SerializeField] private bool debugLog = false;

    #endregion

    // ─────────────────────────────────────────────────────────────────────────
    #region Private State
    // ─────────────────────────────────────────────────────────────────────────

    private EnemyBrain _brain;
    private float      _currentSpeed;

    private int  _hashSpeed, _hashPunch, _hashStagger, _hashDead, _hashChasing, _hashWeaponType;
    private bool _hashesCached;
    private HashSet<int> _validParamHashes = new();

    #endregion

    // ─────────────────────────────────────────────────────────────────────────
    #region Unity Lifecycle
    // ─────────────────────────────────────────────────────────────────────────

    private void Awake()
    {
        _brain = GetComponent<EnemyBrain>();
        if (animator == null) animator = GetComponentInChildren<Animator>();
        if (agent    == null) agent    = GetComponent<NavMeshAgent>();

        CacheHashes();
    }

    private void OnEnable()
    {
        if (_brain != null) _brain.OnStateChanged += HandleStateChanged;
    }

    private void OnDisable()
    {
        if (_brain != null) _brain.OnStateChanged -= HandleStateChanged;
    }

    private void Update()
    {
        if (animator == null) return;
        UpdateSpeedBlend();
    }

    #endregion

    // ─────────────────────────────────────────────────────────────────────────
    #region Hash Cache — avoids "parameter does not exist" spam
    // ─────────────────────────────────────────────────────────────────────────

    private void CacheHashes()
    {
        _hashSpeed    = Animator.StringToHash(speedParam);
        _hashPunch    = Animator.StringToHash(punchTrigger);
        _hashStagger  = Animator.StringToHash(staggerTrigger);
        _hashDead     = Animator.StringToHash(deadTrigger);
        _hashChasing  = Animator.StringToHash(isChasingBool);
        _hashWeaponType = Animator.StringToHash(weaponTypeParam);

        _validParamHashes.Clear();
        if (animator == null) return;

        foreach (var p in animator.parameters)
            _validParamHashes.Add(p.nameHash);

        _hashesCached = true;
    }

    /// <summary>
    /// Guards against execution-order races: if another script (e.g. EnemySetup,
    /// which runs at DefaultExecutionOrder -100, before this component's own Awake)
    /// calls a public setter before CacheHashes() has run, this catches it up on
    /// demand instead of silently no-opping.
    /// </summary>
    private void EnsureCached()
    {
        if (_hashesCached) return;
        if (animator == null) animator = GetComponentInChildren<Animator>();
        CacheHashes();
    }

    private bool HasParam(int hash) => _validParamHashes.Contains(hash);

    #endregion

    // ─────────────────────────────────────────────────────────────────────────
    #region Speed Blend (Idle <-> Running, also covers "chasing" via faster speed)
    // ─────────────────────────────────────────────────────────────────────────

    private void UpdateSpeedBlend()
    {
        float target = GetCurrentSpeed();
        _currentSpeed = Mathf.Lerp(_currentSpeed, target, Time.deltaTime * speedSmoothing);
        SetFloatSafe(_hashSpeed, _currentSpeed);
    }

    /// <summary>Normalised 0-1 speed. Swap this if movement source changes.</summary>
    private float GetCurrentSpeed()
    {
        if (agent == null || !agent.enabled || !agent.isOnNavMesh) return 0f;
        if (_brain != null &&
            (_brain.State == EnemyState.Dead || _brain.State == EnemyState.Staggered))
            return 0f;

        return Mathf.Clamp01(agent.velocity.magnitude / Mathf.Max(0.01f, runSpeedReference));
    }

    #endregion

    // ─────────────────────────────────────────────────────────────────────────
    #region State → Animator Mapping
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// EXTEND HERE when new EnemyState values are added — one case, one line.
    /// Keep the actual behaviour logic in the state owner, not here.
    /// </summary>
    private void HandleStateChanged(EnemyState next)
    {
        switch (next)
        {
            case EnemyState.Idle:
                SetBoolSafe(_hashChasing, false);
                break;

            case EnemyState.Aggro:
                SetBoolSafe(_hashChasing, true);   // Speed float still drives idle/run
                break;

            case EnemyState.Staggered:
                SetBoolSafe(_hashChasing, false);
                TriggerSafe(_hashStagger);
                break;

            case EnemyState.Dead:
                TriggerSafe(_hashDead);
                break;
        }

        Log($"State → {next}");
    }

    #endregion

    // ─────────────────────────────────────────────────────────────────────────
    #region Public Triggers — call from any attack/ability script, zero coupling
    // ─────────────────────────────────────────────────────────────────────────

    public void TriggerPunch() => TriggerSafe(_hashPunch);

    /// <summary>
    /// Sets which idle/hold pose this enemy uses (Rifle=0/Sniper=1/Pistol=2).
    /// Called once by EnemySetup at spawn. Drives Equals-based transitions between
    /// discrete idle states (RifleGunHolding/Sniper_Idle/PistolGunHolding) —
    /// not a blend tree, since Equals conditions require an Int parameter, not Float.
    /// </summary>
    public void SetWeaponType(EnemyWeaponAnimType type) => SetIntSafe(_hashWeaponType, (int)type);

    /// <summary>Escape hatch for triggers not wired above yet.</summary>
    public void TriggerCustom(string paramName) => TriggerSafe(Animator.StringToHash(paramName));

    #endregion

    // ─────────────────────────────────────────────────────────────────────────
    #region Safe Setters
    // ─────────────────────────────────────────────────────────────────────────

    private void SetFloatSafe(int hash, float value)
    {
        EnsureCached();
        if (animator == null || !HasParam(hash)) return;
        animator.SetFloat(hash, value);
    }

    private void SetIntSafe(int hash, int value)
    {
        EnsureCached();
        if (animator == null || !HasParam(hash)) return;
        animator.SetInteger(hash, value);
    }

    private void SetBoolSafe(int hash, bool value)
    {
        EnsureCached();
        if (animator == null || !HasParam(hash)) return;
        animator.SetBool(hash, value);
    }

    private void TriggerSafe(int hash)
    {
        EnsureCached();
        if (animator == null || !HasParam(hash)) return;
        animator.SetTrigger(hash);
        Log($"Trigger fired (hash {hash}).");
    }

    #endregion

    // ─────────────────────────────────────────────────────────────────────────
    #region Debug
    // ─────────────────────────────────────────────────────────────────────────

    [System.Diagnostics.Conditional("UNITY_EDITOR")]
    private void Log(string msg)
    {
        if (debugLog) Debug.Log($"[EnemyAnimatorController] {name}: {msg}", this);
    }

    #endregion
}