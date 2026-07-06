using System;
using System.Collections;
using UnityEngine;

/// <summary>
/// Thợ săn bắn tỉa tầm xa (Sniper Mode).
/// Cải tiến: Ép quái luôn tự động quay mặt/nòng súng chính diện về phía Player khi nhắm bắn từ xa.
/// </summary>
[RequireComponent(typeof(EnemyBrain))]
public class SniperAttackBehaviour : MonoBehaviour
{
    [Header("Sniper Rifle")]
    public Transform FirePoint;                
    public float FireCooldown     = 3.5f;      
    public float Damage           = 25f;

    [Header("Hit Effects")]
    public GameObject HitVFXPrefab;
    public float      HitVFXLifetime = 2f;

    [Header("Radar Ranges")]
    [Tooltip("Bán kính tự quét tìm Player tầm xa (100m).")]
    public float AttackRange      = 100f;      

    [Header("Rotation Setup")]
    [Tooltip("Tốc độ xoay người của quái hướng về Player khi nhắm bắn tầm xa.")]
    public float RotationSpeed    = 8f;

    [Header("Laser Setup (Explicit Reference)")]
    public LineRenderer LaserRenderer;         

    [Header("Laser Height Tuning")]
    [Tooltip("Độ cao điểm nhắm trên thân Player (Hạ thấp xuống chân để tránh lệch lên trời).")]
    public float TargetHeightOffset = 0.2f;    

    [Header("Line of Sight")]
    [Tooltip("Layers that block the sniper's raycast scan for the player (walls, terrain, props). Should NOT include the player's own layer.")]
    public LayerMask ObstacleLayers;

    [Header("Combine Sniper Settings")]
    public float LaserChargeTime  = 2.0f;      
    public float LaserLockTime    = 0.7f;      
    public Color AimColor         = Color.red; 
    public Color LockColor        = new Color(1f, 0.3f, 0f); 

    public event Action OnSniperShot;          

    private EnemyBrain _brain;
    private bool       _isShooting;            
    private bool       _isLocking;
    private bool       _isCooldown;            
    private float      _cooldownTimer;
    private Coroutine  _shootRoutine;
    private Vector3    _lockedTargetPos;       
    private Transform  _targetPlayer;          

    [HideInInspector] public float CooldownMultiplier = 1f;

    private void Awake()
    {
        _brain = GetComponent<EnemyBrain>();

        if (LaserRenderer != null)
        {
            LaserRenderer.startWidth = 0.04f;
            LaserRenderer.endWidth = 0.04f;
            LaserRenderer.positionCount = 2;
            LaserRenderer.enabled = false;
        }
        else
        {
            Debug.LogError($"<color=red>[Sniper Attack]</color> Thiếu LineRenderer trên {gameObject.name}!");
        }

        _brain.OnStateChanged += OnStateChanged;
    }

    private void OnDestroy() => _brain.OnStateChanged -= OnStateChanged;

    private void Update()
    {
        if (_brain.State == EnemyState.Dead || _brain.State == EnemyState.Staggered)
        {
            ResetLaserImmediate();
            return;
        }

        // 1. Quét tìm Player bằng raycast (thay cho OverlapSphere) — vừa rẻ hơn, vừa
        //    tôn trọng che khuất: quái không còn "thấy" Player xuyên tường trong tầm bắn nữa.
        ScanForPlayerTarget();

        if (_targetPlayer == null)
        {
            ResetLaserImmediate();
            return;
        }

        // CHỐT CHẶN XOAY NGƯỜI: Khi quái đang nhắm bắn (_isShooting), ép nó phải xoay mặt về Player
        if (_isShooting)
        {
            RotateTowardsPlayer();
        }

        // 2. Đếm thời gian hồi chiêu
        if (_isCooldown)
        {
            _cooldownTimer += Time.deltaTime;
            if (_cooldownTimer >= FireCooldown * CooldownMultiplier)
            {
                _isCooldown = false; 
                _cooldownTimer = 0f;
            }
            return; 
        }

        // 3. Khởi động chu kỳ ngắm bắn nếu sẵn sàng
        if (!_isShooting)
        {
            _shootRoutine = StartCoroutine(CombineSniperSequence());
        }
    }

    /// <summary>
    /// Tính toán và ép trục Y của quái quay chính xác hướng về phía mục tiêu thực tế
    /// </summary>
    private void RotateTowardsPlayer()
    {
        Vector3 targetPos = _isLocking && _lockedTargetPos != Vector3.zero
            ? _lockedTargetPos 
            : _targetPlayer.position;

        // Chỉ tính hướng xoay trên mặt phẳng ngang (Trục Y), tránh làm quái bị chúi đầu xuống đất
        Vector3 lookDirection = (targetPos - transform.position);
        lookDirection.y = 0f; 

        if (lookDirection != Vector3.zero)
        {
            Quaternion targetRotation = Quaternion.LookRotation(lookDirection);
            // Xoay mượt mà theo thời gian dựa vào RotationSpeed
            transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, RotationSpeed * Time.deltaTime);
        }
    }

    /// <summary>
    /// Raycast straight at PlayerHealth.Transform — no more OverlapSphere + tag scan.
    /// Fails (no target) if out of range OR if something in ObstacleLayers blocks the shot,
    /// so the sniper can no longer lock through walls.
    /// </summary>
    private void ScanForPlayerTarget()
    {
        _targetPlayer = null;

        if (PlayerHealth.Transform == null) return;

        Vector3 origin      = GetFireOrigin();
        Vector3 targetPoint = PlayerHealth.Transform.position + Vector3.up * TargetHeightOffset;
        Vector3 toTarget    = targetPoint - origin;
        float   distance    = toTarget.magnitude;

        if (distance > AttackRange) return;

        Vector3 dir = toTarget / distance;

        // Blocked by an obstacle before reaching the player → no line of sight, no target.
        if (Physics.Raycast(origin, dir, out RaycastHit hit, distance, ObstacleLayers))
            return;

        _targetPlayer = PlayerHealth.Transform;
    }

    private IEnumerator CombineSniperSequence()
    {
        if (LaserRenderer == null || _targetPlayer == null) yield break;

        _isShooting = true;
        _isLocking  = false;
        LaserRenderer.enabled = true;

        // =================================================================
        // GIAI ĐOẠN 1: NHẮM MỤC TIÊU (AIMING) - LASER ĐỎ BÁM THEO PLAYER TỪ XA
        // =================================================================
        float timer = 0f;
        LaserRenderer.startColor = AimColor;
        LaserRenderer.endColor = AimColor;

        while (timer < LaserChargeTime)
        {
            if (_targetPlayer == null) yield break;

            Vector3 origin = GetFireOrigin();
            Vector3 currentTarget = _targetPlayer.position + Vector3.up * TargetHeightOffset; 
            
            Vector3 laserDir = (currentTarget - origin).normalized;
            Vector3 laserEndPoint = origin + laserDir * AttackRange;

            if (Physics.Raycast(origin, laserDir, out RaycastHit hit, AttackRange))
            {
                laserEndPoint = hit.point;
            }

            LaserRenderer.SetPosition(0, origin);
            LaserRenderer.SetPosition(1, laserEndPoint);

            timer += Time.deltaTime;
            yield return null;
        }

        // =================================================================
        // GIAI ĐOẠN 2: KHÓA VỊ TRÍ (LOCKING) - LASER CAM CHỐT CHẶN CỐ ĐỊNH TẠI CHỖ
        // =================================================================
        if (_targetPlayer == null) yield break;
        
        timer = 0f;
        LaserRenderer.startColor = LockColor;
        LaserRenderer.endColor = LockColor;
        
        _lockedTargetPos = _targetPlayer.position + Vector3.up * TargetHeightOffset;
        _isLocking = true;

        while (timer < LaserLockTime)
        {
            Vector3 origin = GetFireOrigin();
            Vector3 laserDir = (_lockedTargetPos - origin).normalized;
            Vector3 laserEndPoint = origin + laserDir * AttackRange;

            if (Physics.Raycast(origin, laserDir, out RaycastHit hit, AttackRange))
            {
                laserEndPoint = hit.point;
            }

            LaserRenderer.SetPosition(0, origin);
            LaserRenderer.SetPosition(1, laserEndPoint);

            timer += Time.deltaTime;
            yield return null;
        }

        // =================================================================
        // GIAI ĐOẠN 3: KHAI HỎA (FIRING) - BẮN ĐẠN THẲNG VÀO ĐIỂM KHÓA
        // =================================================================
        FireSniperBulletAtLockedPosition();

        ResetLaserAfterShot();
    }

    private void FireSniperBulletAtLockedPosition()
    {
        Vector3 origin  = GetFireOrigin();
        Vector3 fireDir = (_lockedTargetPos - origin).normalized;

        // Instant hitscan — the shot was already locked to a fixed point during
        // GIAI ĐOẠN 2, so there's nothing a travelling projectile adds here.
        //
        // NOTE: PlayerHealth does not implement IDamageable — it's always the single,
        // known target for enemy attacks, so every other enemy weapon (SMG, Shotgun)
        // hits it via a tag check + PlayerHealth.Instance directly, not GetComponentInParent.
        if (Physics.Raycast(origin, fireDir, out RaycastHit hit, AttackRange))
        {
            if (hit.collider.CompareTag("Player"))
                PlayerHealth.Instance?.TakeDamage(Damage);

            SpawnHitVFX(hit.point, hit.normal);
        }

        OnSniperShot?.Invoke();
    }

    private void SpawnHitVFX(Vector3 point, Vector3 normal)
    {
        if (HitVFXPrefab == null) return;
        Quaternion rot = normal.sqrMagnitude > 0.001f ? Quaternion.LookRotation(normal) : Quaternion.identity;
        Destroy(Instantiate(HitVFXPrefab, point, rot), HitVFXLifetime);
    }

    private Vector3 GetFireOrigin()
    {
        return FirePoint != null ? FirePoint.position : transform.position + Vector3.up * 1.5f;
    }

    private void ResetLaserImmediate()
    {
        if (_shootRoutine != null) { StopCoroutine(_shootRoutine); _shootRoutine = null; }
        if (LaserRenderer != null) LaserRenderer.enabled = false;
        _isShooting = false;
        _isLocking  = false;
        _isCooldown = false;
        _lockedTargetPos = Vector3.zero;
    }

    private void ResetLaserAfterShot()
    {
        if (LaserRenderer != null) LaserRenderer.enabled = false;
        _isShooting = false;
        _isLocking  = false;
        _lockedTargetPos = Vector3.zero;
        _shootRoutine = null;
        _isCooldown = true; 
        _cooldownTimer = 0f;
    }

    private void OnStateChanged(EnemyState state)
    {
        if (state == EnemyState.Dead || state == EnemyState.Staggered)
        {
            ResetLaserImmediate();
        }
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.cyan;
        Gizmos.DrawWireSphere(transform.position, AttackRange);
    }
}