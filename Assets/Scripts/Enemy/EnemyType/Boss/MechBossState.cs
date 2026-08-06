using System;
using System.Collections;
using UnityEngine;
using UnityEngine.AI;
using DG.Tweening;

public enum MechBossState { Dropping, Idle, Attacking, Staggered, Dead }

[RequireComponent(typeof(EnemyHealth))]
public class MechBossBrain : MonoBehaviour
{
    #region Inspector

    [Header("Drop Intro")]
    public Animator animator;
    public Transform visualRoot;
    public float dropHeight = 20f;
    public float dropDuration = 0.8f;
    public Ease dropEase = Ease.InQuad;

    [Header("Impact Squash")]
    public Vector3 squashPunch = new Vector3(0.2f, -0.25f, 0.2f);
    public float squashDuration = 0.35f;
    public int squashVibrato = 6;
    public float squashElasticity = 0.6f;

    [Header("Phases")]
    [Tooltip("HP fraction that triggers each phase-up, in order. One entry = 2 phases, two entries = 3 phases, etc.")]
    [Range(0.01f, 1f)] public float[] phaseHealthThresholds = { 0.5f };
    public Vector3 phaseTransitionPunch = new Vector3(0.3f, -0.3f, 0.3f);
    public float phaseTransitionDuration = 0.4f;

    [Header("Refs")]
    public NavMeshAgent agent;
    public Collider mainCollider;

    [Header("Debug")]
    [SerializeField] private bool showDebugLogs = true;

    #endregion

    #region Events

    public MechBossState State { get; private set; } = MechBossState.Dropping;
    public int Phase { get; private set; } = 1;
    public event Action<MechBossState, MechBossState> OnStateChanged;
    public event Action<int, int> OnPhaseChanged;
    public event Action OnIntroImpact;
    public event Action OnIntroComplete;

    #endregion

    private EnemyHealth _health;
    private bool _introDone;
    private bool _animSignal_ImpactLand;
    private int _phaseIndex;
    private static readonly int AnimDropTrigger = Animator.StringToHash("DropIn");
    private static readonly int AnimPhaseUp = Animator.StringToHash("PhaseUp");

    #region Unity Lifecycle

    private void Awake()
    {
        _health = GetComponent<EnemyHealth>();
        _health.OnDied += _ => SetState(MechBossState.Dead);
        _health.OnStaggerEntered += () => SetState(MechBossState.Staggered);
        _health.OnStaggerExpired += () => SetState(MechBossState.Idle);
        _health.OnDamaged += (amount, curHP, maxHP, dir, point) => CheckPhaseTransition(curHP, maxHP);

        if (visualRoot == null && animator != null) visualRoot = animator.transform;

        if (agent != null) agent.enabled = false;
        if (mainCollider != null) mainCollider.enabled = false;
    }

    private void Start() => StartCoroutine(Co_Intro());

    #endregion

    public void AnimEvent_ImpactLand() => _animSignal_ImpactLand = true;

    #region Intro

    private IEnumerator Co_Intro()
    {
        Vector3 groundPos = transform.position;
        transform.position = groundPos + Vector3.up * dropHeight;

        if (animator != null) animator.SetTrigger(AnimDropTrigger);

        yield return transform
            .DOMove(groundPos, dropDuration)
            .SetEase(dropEase)
            .WaitForCompletion();

        Log("Impact.");
        OnIntroImpact?.Invoke();

        if (visualRoot != null)
        {
            yield return visualRoot
                .DOPunchScale(squashPunch, squashDuration, squashVibrato, squashElasticity)
                .WaitForCompletion();
        }

        float waited = 0f;
        while (!_animSignal_ImpactLand && waited < 1f)
        {
            waited += Time.deltaTime;
            yield return null;
        }

        if (mainCollider != null) mainCollider.enabled = true;
        if (agent != null) agent.enabled = true;

        _introDone = true;
        OnIntroComplete?.Invoke();
        SetState(MechBossState.Idle);
        Log("Intro complete.");
    }

    #endregion

    #region Phase

    private void CheckPhaseTransition(float currentHP, float maxHP)
    {
        if (!_introDone || State == MechBossState.Dead) return;
        if (_phaseIndex >= phaseHealthThresholds.Length) return;
        if (currentHP / maxHP > phaseHealthThresholds[_phaseIndex]) return;

        _phaseIndex++;
        int prev = Phase;
        Phase = _phaseIndex + 1;
        Log($"Phase {prev} -> {Phase}");
        OnPhaseChanged?.Invoke(prev, Phase);
        StartCoroutine(Co_PhaseTransition());
    }

    private IEnumerator Co_PhaseTransition()
    {
        // Only borrow the Attacking gate if the boss is actually Idle right now.
        // If it's mid-attack or Staggered, forcing it to Attacking-then-Idle here
        // would end that attack's/stagger's gate early (letting the selector fire
        // a second attack on top of the first, or cutting the stagger window short).
        bool borrowedGate = State == MechBossState.Idle;
        if (borrowedGate) NotifyAttackStart();

        if (animator != null) animator.SetTrigger(AnimPhaseUp);

        if (visualRoot != null)
        {
            yield return visualRoot
                .DOPunchScale(phaseTransitionPunch, phaseTransitionDuration, squashVibrato, squashElasticity)
                .WaitForCompletion();
        }

        if (borrowedGate) NotifyAttackEnd();
    }

    #endregion

    #region Attack State Bridge

    public void NotifyAttackStart() => SetState(MechBossState.Attacking);

    public void NotifyAttackEnd()
    {
        if (State == MechBossState.Attacking) SetState(MechBossState.Idle);
    }

    #endregion

    private void SetState(MechBossState next)
    {
        if (!_introDone && next != MechBossState.Dead) return;
        if (State == next) return;
        var prev = State;
        State = next;
        OnStateChanged?.Invoke(prev, next);
        Log($"State: {prev} -> {next}");
    }

    [System.Diagnostics.Conditional("UNITY_EDITOR")]
    private void Log(string msg)
    {
        if (showDebugLogs) Debug.Log($"[MechBossBrain] {msg}", this);
    }
}