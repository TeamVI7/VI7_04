using System;
using System.Collections;
using UnityEngine;

[RequireComponent(typeof(EnemyBrain))]
public class SniperAttackBehaviour : MonoBehaviour, IEnemyAimController
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

    public bool IsAimingOrLocking => _isShooting;

    private EnemyBrain _brain;
    private bool       _isShooting;            
    private bool       _isLocking;
    private bool       _isCooldown;            
    private float      _cooldownTimer;
    private Coroutine  _shootRoutine;
    private Vector3    _lockedTargetPos;       
    private Transform  _targetPlayer;          

    [HideInInspector] public float CooldownMultiplier = 1f;

    // ── IEnemyAimController ─────────────────────────────────────────────────
    public int AimPriority => 20;
    public bool WantsAim => _isShooting;

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

        ScanForPlayerTarget();

        if (_targetPlayer == null)
        {
            ResetLaserImmediate();
            return;
        }

        // Rotation itself is driven by EnemyAimCoordinator (LateUpdate) via TickAim
        // below, whenever WantsAim (_isShooting) is true.

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

        if (!_isShooting)
        {
            _shootRoutine = StartCoroutine(CombineSniperSequence());
        }
    }

    // Called by EnemyAimCoordinator whenever WantsAim (_isShooting) is true.
    public void TickAim(float deltaTime)
    {
        if (_targetPlayer == null) return;

        Vector3 targetPos = _isLocking && _lockedTargetPos != Vector3.zero
            ? _lockedTargetPos
            : _targetPlayer.position;

        Vector3 lookDirection = (targetPos - transform.position);
        lookDirection.y = 0f;

        if (lookDirection != Vector3.zero)
        {
            Quaternion targetRotation = Quaternion.LookRotation(lookDirection);
            transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, RotationSpeed * deltaTime);
        }
    }

    private void ScanForPlayerTarget()
    {
        _targetPlayer = null;

        if (PlayerHealth.Transform == null) return;

        Vector3 origin = GetFireOrigin();
        float distance = Vector3.Distance(origin, PlayerHealth.Transform.position);
        if (distance > AttackRange) return;

        if (EnemyVision.HasLineOfSight(origin, PlayerHealth.Transform, AttackRange, ObstacleLayers))
            _targetPlayer = PlayerHealth.Transform;
    }

    private IEnumerator CombineSniperSequence()
    {
        if (LaserRenderer == null || _targetPlayer == null) yield break;

        _isShooting = true;
        _isLocking  = false;
        LaserRenderer.enabled = true;

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

        FireSniperBulletAtLockedPosition();

        ResetLaserAfterShot();
    }

    private void FireSniperBulletAtLockedPosition()
    {
        Vector3 origin  = GetFireOrigin();
        Vector3 fireDir = (_lockedTargetPos - origin).normalized;

        if (Physics.Raycast(origin, fireDir, out RaycastHit hit, AttackRange))
        {
            if (hit.collider.CompareTag("Player"))
                PlayerHealth.Instance?.TakeDamage(Damage, origin);

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
        Vector3 center = EnemyGizmos.Ground(transform.position);
        EnemyGizmos.GroundRing(center, AttackRange, EnemyGizmos.Sniper,
                               $"snipe {AttackRange:0.#}m", 250f);
    }
}