using System;
using System.Collections;
using UnityEngine;

/// <summary>
/// Hành vi bắn tỉa tầm xa cho Enemy Stealth (Sniper).
/// Bắn đạn thẳng tắp với vận tốc cao dựa trên cấu trúc Modular của GrenadeBurst.
/// </summary>
[RequireComponent(typeof(EnemyBrain))]
public class SniperAttackBehaviour : MonoBehaviour
{
    [Header("Sniper Rifle")]
    public GameObject SniperProjectilePrefab; // Kéo Prefab đạn Sniper (có gắn SniperProjectile) vào đây
    public Transform FirePoint;                // Điểm nòng súng
    public float AttackRange      = 40f;       // Tầm bắn tỉa (thường xa hơn lựu đạn)
    public float FireCooldown     = 3.5f;      // Thời gian giãn cách giữa các phát bắn tỉa

    public event Action OnSniperShot;          // Sự kiện để kích hoạt hiệu ứng flash hoặc âm thanh bắn

    private EnemyBrain _brain;
    private float      _cooldownTimer;
    private bool       _isShooting;
    private Coroutine  _shootRoutine;

    [HideInInspector] public float CooldownMultiplier = 1f;

    private void Awake()
    {
        _brain = GetComponent<EnemyBrain>();
        _brain.OnStateChanged += OnStateChanged;
    }

    private void OnDestroy() => _brain.OnStateChanged -= OnStateChanged;

    private void Update()
    {
        // Chỉ bắn khi đang phát hiện mục tiêu (Aggro) và không trong trạng thái bận bắn
        if (_brain.State != EnemyState.Aggro || _isShooting) return;
        if (PlayerHealth.Transform == null) return;

        // Kiểm tra khoảng cách với Player
        float dist = Vector3.Distance(transform.position, PlayerHealth.Transform.position);
        if (dist > AttackRange) return;

        // Đếm ngược hồi chiêu
        _cooldownTimer += Time.deltaTime;
        if (_cooldownTimer >= FireCooldown * CooldownMultiplier)
        {
            _cooldownTimer = 0f;
            _shootRoutine = StartCoroutine(ShootSequence());
        }
    }

    private IEnumerator ShootSequence()
    {
        _isShooting = true;

        if (_brain.State != EnemyState.Dead && PlayerHealth.Transform != null)
        {
            FireSniperBullet();
        }

        // Chờ 1 khung hình hoặc một khoảng ngắn để reset trạng thái bắn
        yield return null; 

        _isShooting = false;
        _shootRoutine = null;
    }

    private void FireSniperBullet()
    {
        if (SniperProjectilePrefab == null) return;

        // Xác định vị trí nòng súng (nếu không kéo vào thì tự lấy ngang tầm mắt quái)
        Vector3 origin = FirePoint != null ? FirePoint.position : transform.position + Vector3.up * 1.5f;
        Vector3 target = PlayerHealth.Transform.position + Vector3.up * 1f; // Nhắm vào giữa thân Player
        
        // Tính hướng bắn thẳng tắp từ nòng súng đến Player
        Vector3 fireDir = (target - origin).normalized;

        // Sinh viên đạn và hướng nó về phía Player
        var go = Instantiate(SniperProjectilePrefab, origin, Quaternion.LookRotation(fireDir));
        
        // Lấy damage từ Stats SO thông qua EnemySetup nếu có, không thì lấy damage mặc định của đạn
        float dmg = GetComponent<EnemySetup>()?.Stats?.MeleeDamage ?? 20f; // Cậu có thể tùy biến biến lấy damage tùy ý

        // Kích hoạt viên đạn bay thẳng tắp
        go.GetComponent<Enemy.SniperProjectile>()?.Init(fireDir, dmg);

        // Phát sự kiện (ví dụ để EnemyAudio nghe thấy phát tiếng súng bắn tỉa)
        OnSniperShot?.Invoke();
    }

    private void OnStateChanged(EnemyState state)
    {
        if (state == EnemyState.Dead || state == EnemyState.Staggered)
        {
            if (_shootRoutine != null) { StopCoroutine(_shootRoutine); _shootRoutine = null; }
            _isShooting = false;
        }
    }
}