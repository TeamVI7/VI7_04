using System;
using UnityEngine;
using UnityEngine.AI;

public enum EnemyState { Idle, Aggro, Staggered, Dead }

/// <summary>
/// Owns the state machine and player detection. Nothing else.
/// Behaviours (patrol, attack, shield...) subscribe to state-change events
/// and drive themselves.
/// </summary>
[RequireComponent(typeof(NavMeshAgent))]
[RequireComponent(typeof(EnemyHealth))]
public class EnemyBrain : MonoBehaviour
{
    [Header("Detection")]
    public float     AggroRadius       = 15f;
    public float     RadarRange        = 40f;
    public LayerMask LOSBlockingLayers;

    public EnemyState State { get; private set; } = EnemyState.Idle;

    // ── Events ───────────────────────────────────────────────────────────────
    public event Action<EnemyState> OnStateChanged;

    // ── Internal ─────────────────────────────────────────────────────────────
    private EnemyHealth _health;

    private void Awake()
    {
        _health = GetComponent<EnemyHealth>();
        _health.OnDied           += _ => SetState(EnemyState.Dead);
        _health.OnStaggerEntered += () => SetState(EnemyState.Staggered);
        _health.OnStaggerExpired += () => SetState(EnemyState.Aggro);
    }

    private void Update()
    {
        if (State == EnemyState.Dead || State == EnemyState.Staggered) return;

        switch (State)
        {
            case EnemyState.Idle:  TickIdle();  break;
            case EnemyState.Aggro: TickAggro(); break;
        }
    }

    private void TickIdle()
    {
        if (!TryDetectPlayer(out float dist)) return;
        if (dist <= AggroRadius) SetState(EnemyState.Aggro);
    }

    private void TickAggro()
    {
        if (PlayerHealth.Transform == null) { SetState(EnemyState.Idle); return; }
        float dist = Vector3.Distance(transform.position, PlayerHealth.Transform.position);
        if (dist > RadarRange) SetState(EnemyState.Idle);
    }

    public bool TryDetectPlayer(out float distance)
    {
        distance = float.MaxValue;
        if (PlayerHealth.Transform == null) return false;

        distance = Vector3.Distance(transform.position, PlayerHealth.Transform.position);
        if (distance > RadarRange) return false;

        Vector3 eye    = transform.position + Vector3.up * 1.5f;
        Vector3 target = PlayerHealth.Transform.position + Vector3.up * 1f;
        if (Physics.Raycast(eye, (target - eye).normalized,
                            Vector3.Distance(eye, target), LOSBlockingLayers))
            return false;

        return true;
    }

    public void SetState(EnemyState next)
    {
        if (State == next) return;
        State = next;
        OnStateChanged?.Invoke(next);
    }
}