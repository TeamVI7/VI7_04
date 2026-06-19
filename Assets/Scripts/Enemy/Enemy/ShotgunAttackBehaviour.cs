using UnityEngine;
using System;
using System.Collections;

/// <summary>
/// Thành phần bắn Shotgun thực tế, có lực, hiệu ứng fade đạn mượt mà cho Enemy 5.
/// </summary>
[RequireComponent(typeof(EnemyBrain))]
public class ShotgunAttackBehaviour : MonoBehaviour
{
    [Header("Shotgun Setup")]
    public Transform FirePoint;          
    public float AttackRange = 12f;       // Shotgun ngoài đời hiệu quả nhất ở cự ly gần đến trung bình
    public float DamagePerPellet = 4f;    // Phát bắn tầm gần trúng nhiều viên sẽ cực thốn
    public int PelletsPerShot = 8;        // Tăng lên 8 viên để chùm đạn dày dặn, thực tế hơn
    public float SpreadAngle = 0.14f;     // Độ loang của chùm đạn hình nón
    public float FireRate = 1.8f;         // Khoảng nghỉ lên đạn thực tế (gần 2 giây một phát)
    public LayerMask ObstacleLayers;     

    [Header("Visual Effects")]
    public LineRenderer BulletTrailPrefab;
    [Tooltip("Thời gian tia đạn lưu lại và mờ dần trên màn hình")]
    public float TrailDuration = 0.2f; 

    // ── AUDIO EVENT ───────────────────────────────────────────
    public event Action OnShotgunFired;

    private EnemyBrain _brain;
    private float _nextFireTimer;

    private void Awake()
    {
        _brain = GetComponent<EnemyBrain>();
    }

    private void Update()
    {
        if (_brain.State != EnemyState.Aggro) return;
        if (PlayerHealth.Transform == null) return;

        float distanceToPlayer = Vector3.Distance(transform.position, PlayerHealth.Transform.position);
        if (distanceToPlayer > AttackRange) return;

        _nextFireTimer += Time.deltaTime;
        if (_nextFireTimer >= FireRate)
        {
            _nextFireTimer = 0f;
            FireShotgun();
        }
    }

    private void FireShotgun()
    {
        if (FirePoint == null) return;

        // Kích hoạt âm thanh nổ súng Shotgun (Phát qua EnemyAudio)
        OnShotgunFired?.Invoke(); 

        Vector3 targetPos = PlayerHealth.Transform.position + Vector3.up * 0.5f; 
        Vector3 baseDirection = (targetPos - FirePoint.position).normalized;

        // Vòng lặp khai hỏa đồng loạt các viên đạn con (Pellets)
        for (int i = 0; i < PelletsPerShot; i++)
        {
            // Tạo độ lệch ngẫu nhiên thực tế theo hệ tọa độ góc
            Vector3 spread = UnityEngine.Random.insideUnitCircle * SpreadAngle;
            Vector3 finalDirection = (baseDirection + spread).normalized;

            if (Physics.Raycast(FirePoint.position, finalDirection, out RaycastHit hit, AttackRange, ObstacleLayers))
            {
                StartCoroutine(SpawnRealTableTrail(FirePoint.position, hit.point));

                if (hit.collider.CompareTag("Player"))
                {
                    PlayerHealth.Instance?.TakeDamage(DamagePerPellet);
                }
            }
            else
            {
                Vector3 endPoint = FirePoint.position + finalDirection * AttackRange;
                StartCoroutine(SpawnRealTableTrail(FirePoint.position, endPoint));
            }
        }
    }

    // Coroutine xử lý tia đạn mờ dần (Fade Out) tạo cảm giác thị giác chân thực
    private IEnumerator SpawnRealTableTrail(Vector3 start, Vector3 end)
    {
        if (BulletTrailPrefab == null) yield break;

        LineRenderer trail = Instantiate(BulletTrailPrefab);
        trail.useWorldSpace = true;
        trail.SetPosition(0, start);
        trail.SetPosition(1, end);

        // Tạo Material độc lập để không bị đổi màu lây sang các tia đạn khác
        trail.material = new Material(Shader.Find("Sprites/Default"));

        // Màu cam rực lửa lúc súng vừa nổ
        Color startColorOpen = new Color(1f, 0.5f, 0.1f, 1f);
        Color endColorOpen = new Color(1f, 0.2f, 0f, 0.3f);

        float elapsed = 0f;
        while (elapsed < TrailDuration)
        {
            elapsed += Time.deltaTime;
            float pct = elapsed / TrailDuration;

            // Đậm ở những khung hình đầu và mờ dần (Alpha giảm về 0) ở các khung hình sau
            trail.startColor = Color.Lerp(startColorOpen, new Color(1f, 0.3f, 0f, 0f), pct);
            trail.endColor = Color.Lerp(endColorOpen, new Color(0.5f, 0.1f, 0f, 0f), pct);

            yield return null;
        }

        Destroy(trail.gameObject);
    }
}