using UnityEngine;
using System; // Bắt buộc phải có để dùng Action

/// <summary>
/// Thành phần bắn SMG tự động liên tục cho Enemy 4.
/// Chỉ bắn khi EnemyBrain ở trạng thái Aggro và Player trong tầm bắn.
/// </summary>
[RequireComponent(typeof(EnemyBrain))]
public class SMGAttackBehaviour : MonoBehaviour
{
    [Header("SMG Setup")]
    public Transform FirePoint;          
    public float AttackRange = 20f;      
    public float DamagePerShot = 2f;     
    public float FireRate = 0.1f;        
    public LayerMask ObstacleLayers;     

    [Header("Visual Effects")]
    public LineRenderer BulletTrailPrefab;

    // ── AUDIO EVENT ───────────────────────────────────────────
    // Event kích hoạt mỗi khi súng nổ để bên EnemyAudio nghe thấy và phát âm thanh
    public event Action OnSMGFired;

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
            FireSMG();
        }
    }

    private void FireSMG()
    {
        if (FirePoint == null) return;

        // Kích hoạt Event báo hiệu cho script âm thanh hoạt động
        OnSMGFired?.Invoke();

        Vector3 targetPos = PlayerHealth.Transform.position + Vector3.up * 0.5f; 
        Vector3 fireDirection = (targetPos - FirePoint.position).normalized;

        if (Physics.Raycast(FirePoint.position, fireDirection, out RaycastHit hit, AttackRange, ObstacleLayers))
        {
            SpawnBulletTrail(FirePoint.position, hit.point);

            if (hit.collider.CompareTag("Player"))
            {
                DealDamage();
            }
        }
        else
        {
            Vector3 endPoint = FirePoint.position + fireDirection * AttackRange;
            SpawnBulletTrail(FirePoint.position, endPoint);
        }
    }

    private void DealDamage()
    {
        PlayerHealth.Instance?.TakeDamage(DamagePerShot);
    }

    private void SpawnBulletTrail(Vector3 start, Vector3 end)
    {
        if (BulletTrailPrefab == null) return;

        LineRenderer trail = Instantiate(BulletTrailPrefab);
        trail.useWorldSpace = true;
        trail.SetPosition(0, start);
        trail.SetPosition(1, end);

        Color yellowColor = new Color(1f, 0.9f, 0f, 1f); 
        trail.startColor = yellowColor;
        trail.endColor = new Color(1f, 0.6f, 0f, 0f); 

        trail.material = new Material(Shader.Find("Sprites/Default"));
        Destroy(trail.gameObject, 0.03f);
    }
}