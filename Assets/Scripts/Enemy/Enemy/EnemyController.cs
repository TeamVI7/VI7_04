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

    public EnemyState State { get; private set; } = EnemyState.Idle;

    // ── Events ───────────────────────────────────────────────────────────────
    public event Action<EnemyState> OnStateChanged;

    // ── Internal ─────────────────────────────────────────────────────────────
    private EnemyHealth _health;
    private Vector3 _spawnPos; // Lưu vị trí xuất phát gốc để làm tâm vùng bảo vệ cố định

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

        // 2. CHỈNH SỬA TOÁN HỌC: Tính toán khoảng cách từ Player tới TỔ cố định của quái
        float distToSpawn = Vector3.Distance(_spawnPos, PlayerHealth.Transform.position);

        // Nếu Player chạy thoát ra khỏi RadarRange tính từ tâm tổ, ép quái về Idle để tự đi bộ quay về
        if (distToSpawn > RadarRange) SetState(EnemyState.Idle);
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