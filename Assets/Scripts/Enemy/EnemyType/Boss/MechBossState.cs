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

    [Header("Refs")]
    public NavMeshAgent agent;
    public Collider mainCollider;

    [Header("Debug")]
    [SerializeField] private bool showDebugLogs = true;

    #endregion

    #region Events

    public MechBossState State { get; private set; } = MechBossState.Dropping;
    public event Action<MechBossState, MechBossState> OnStateChanged;
    public event Action OnIntroImpact;
    public event Action OnIntroComplete;

    #endregion

    private EnemyHealth _health;
    private bool _introDone;
    private bool _animSignal_ImpactLand;
    private static readonly int AnimDropTrigger = Animator.StringToHash("DropIn");

    #region Unity Lifecycle

    private void Awake()
    {
        _health = GetComponent<EnemyHealth>();
        _health.OnDied += _ => SetState(MechBossState.Dead);
        _health.OnStaggerEntered += () => SetState(MechBossState.Staggered);
        _health.OnStaggerExpired += () => SetState(MechBossState.Idle);

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