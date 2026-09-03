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
[RequireComponent(typeof(EnemyAimCoordinator))]
public class EnemyBrain : MonoBehaviour
{
    [Header("Detection")]
    public float AggroRadius = 15f;
    public float RadarRange = 40f;
    public LayerMask LOSBlockingLayers;

    [Header("Chase / Leash")]
    [Tooltip("Seconds the enemy keeps chasing after losing line-of-sight to the player before giving up and returning to patrol. While the player IS in sight, the enemy chases indefinitely (no spawn-distance cutoff).")]
    public float LoseSightGracePeriod = 3f;

    public EnemyState State { get; private set; } = EnemyState.Idle;

    // ── Events ───────────────────────────────────────────────────────────────
    public event Action<EnemyState> OnStateChanged;

    // ── Internal ─────────────────────────────────────────────────────────────
    private EnemyHealth _health;
    private Vector3 _spawnPos; // Lưu vị trí xuất phát gốc để làm tâm vùng bảo vệ cố định
    private float _timeSinceLastSeen; // đếm thời gian mất dấu Player khi đang Aggro

    private void Awake()
    {
        _health = GetComponent<EnemyHealth>();
        _health.OnDied += _ => SetState(EnemyState.Dead);
        _health.OnStaggerEntered += () => SetState(EnemyState.Staggered);
        _health.OnStaggerExpired += () => SetState(EnemyState.Aggro);

        // Ghi nhớ vị trí tổ cắm chốt ban đầu của con quái
        _spawnPos = transform.position;
    }

    private void Update()
    {
        if (State == EnemyState.Dead || State == EnemyState.Staggered) return;

        switch (State)
        {
            case EnemyState.Idle: TickIdle(); break;
            case EnemyState.Aggro: TickAggro(); break;
        }
    }

    private void TickIdle()
    {

        if (!TryDetectPlayerFromSpawn(out float distToSpawn)) return;

        if (distToSpawn <= AggroRadius) SetState(EnemyState.Aggro);
    }

    private void TickAggro()
    {
        if (PlayerHealth.Transform == null) { SetState(EnemyState.Idle); return; }
        if (TryDetectPlayerFromCurrentPosition())
        {
            _timeSinceLastSeen = 0f;
            return;
        }

        _timeSinceLastSeen += Time.deltaTime;
        if (_timeSinceLastSeen >= LoseSightGracePeriod)
            SetState(EnemyState.Idle);
    }
    private bool TryDetectPlayerFromCurrentPosition()
    {
        if (PlayerHealth.Transform == null) return false;

        float actualDist = Vector3.Distance(transform.position, PlayerHealth.Transform.position);
        if (actualDist > RadarRange) return false;

        if (actualDist <= AggroRadius) return true;

        Vector3 eye = transform.position + Vector3.up * 1.5f;
        return EnemyVision.HasLineOfSight(eye, PlayerHealth.Transform, RadarRange, LOSBlockingLayers);
    }

    public bool TryDetectPlayerFromSpawn(out float distanceToSpawn)
    {
        distanceToSpawn = float.MaxValue;
        if (PlayerHealth.Transform == null) return false;

        distanceToSpawn = Vector3.Distance(_spawnPos, PlayerHealth.Transform.position);
        if (distanceToSpawn > RadarRange) return false;

        float actualDistToPlayer = Vector3.Distance(transform.position, PlayerHealth.Transform.position);

        if (actualDistToPlayer <= AggroRadius)
        {
            return true;
        }

        Vector3 eye = transform.position + Vector3.up * 1.5f;
        return EnemyVision.HasLineOfSight(eye, PlayerHealth.Transform, RadarRange, LOSBlockingLayers);
    }

    public void SetState(EnemyState next)
    {
        if (State == next) return;
        State = next;

        if (next == EnemyState.Aggro) _timeSinceLastSeen = 0f;

        OnStateChanged?.Invoke(next);
    }

    private void OnDrawGizmosSelected()
    {
        // Both rings hang off the spawn point at runtime — that's the anchor the
        // radar actually measures from, and it drifts away from the body once the
        // enemy starts moving, which is exactly what you want to see.
        Vector3 anchor = Application.isPlaying ? _spawnPos : transform.position;
        Vector3 center = EnemyGizmos.Ground(anchor);

        // Radar: solid outer ring, coloured by whether the brain is aggroed now.
        Color radar = Application.isPlaying
            ? (State == EnemyState.Aggro ? EnemyGizmos.Fail : EnemyGizmos.Radar)
            : EnemyGizmos.Radar;
        EnemyGizmos.GroundRing(center, RadarRange, radar, $"radar {RadarRange:0.#}m", 0f);

        // Aggro: dashed, because it's a soft boundary — inside it the player is
        // seen regardless of line of sight.
        EnemyGizmos.GroundRing(center, AggroRadius, radar * 0.7f,
                               $"aggro {AggroRadius:0.#}m", 15f, dashed: true);

        if (Application.isPlaying) EnemyGizmos.DropLine(transform.position, center, radar * 0.6f);
    }
}