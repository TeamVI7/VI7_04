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
        // 1. CHỈNH SỬA: Đo khoảng cách từ Player đến TỔ của quái thay vì vị trí di động hiện tại
        if (!TryDetectPlayerFromSpawn(out float distToSpawn)) return;

        // Nếu Player bước vào bán kính Aggro tính từ tâm tổ, quái sẽ hú còi dí đánh
        if (distToSpawn <= AggroRadius) SetState(EnemyState.Aggro);
    }

    private void TickAggro()
    {
        if (PlayerHealth.Transform == null) { SetState(EnemyState.Idle); return; }

        // FIX: giving up used to be based purely on distance-from-SPAWN, which fired
        // even while the player was standing right in front of the enemy (just far
        // from the original nest) — enemies would snap back to patrol mid-fight.
        // Now: the enemy keeps chasing as long as it can actually see the player.
        // Only once sight has been lost for LoseSightGracePeriod does it give up.
        if (TryDetectPlayerFromCurrentPosition())
        {
            _timeSinceLastSeen = 0f;
            return;
        }

        _timeSinceLastSeen += Time.deltaTime;
        if (_timeSinceLastSeen >= LoseSightGracePeriod)
            SetState(EnemyState.Idle);
    }

    /// <summary>
    /// Same idea as TryDetectPlayerFromSpawn, but measured from where the enemy
    /// actually is right now (not its spawn/nest) — used to decide whether an
    /// already-Aggro enemy still has eyes on the player mid-chase.
    /// </summary>
    private bool TryDetectPlayerFromCurrentPosition()
    {
        if (PlayerHealth.Transform == null) return false;

        float actualDist = Vector3.Distance(transform.position, PlayerHealth.Transform.position);
        if (actualDist > RadarRange) return false;

        // Close enough to just "sense" the player regardless of raycast LOS,
        // same shortcut TryDetectPlayerFromSpawn uses.
        if (actualDist <= AggroRadius) return true;

        Vector3 eye = transform.position + Vector3.up * 1.5f;
        Vector3 target = PlayerHealth.Transform.position + Vector3.up * 1f;
        if (Physics.Raycast(eye, (target - eye).normalized,
                            Vector3.Distance(eye, target), LOSBlockingLayers))
            return false;

        return true;
    }

    /// <summary>
    /// Kiểm tra xem Player có đang nằm trong tầm nhìn và khoảng cách bảo vệ tính từ Tổ hay không.
    /// </summary>
    public bool TryDetectPlayerFromSpawn(out float distanceToSpawn)
    {
        distanceToSpawn = float.MaxValue;
        if (PlayerHealth.Transform == null) return false;

        // 1. Tính khoảng cách từ Player tới Tổ cố định
        distanceToSpawn = Vector3.Distance(_spawnPos, PlayerHealth.Transform.position);
        if (distanceToSpawn > RadarRange) return false;

        // 2. Tính khoảng cách thực tế giữa Quái và Player lúc này
        float actualDistToPlayer = Vector3.Distance(transform.position, PlayerHealth.Transform.position);

        // THỦ THUẬT: Nếu cậu đã áp sát vào trong phạm vi AggroRadius (ví dụ < 10m-12m), 
        // quái sẽ tự động phát hiện bằng "Radar/Cảm quan" ngay lập tức, bất kể đang quay lưng đi về tổ!
        if (actualDistToPlayer <= AggroRadius)
        {
            return true;
        }

        // Nếu ở khoảng cách xa hơn (giữa AggroRadius và RadarRange) thì mới cần quét tầm nhìn Raycast
        Vector3 eye = transform.position + Vector3.up * 1.5f;
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

        // Reset the "how long since I saw the player" timer whenever we (re)enter
        // Aggro, so a stale timer from a previous chase can't instantly expire it.
        if (next == EnemyState.Aggro) _timeSinceLastSeen = 0f;

        OnStateChanged?.Invoke(next);
    }

    // Vẽ vòng tròn Gizmos màu vàng thể hiện ranh giới bảo vệ (RadarRange) của từng con quái ngoài Scene
    private void OnDrawGizmosSelected()
    {
        Vector3 center = Application.isPlaying ? _spawnPos : transform.position;
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(center, RadarRange);
    }
}