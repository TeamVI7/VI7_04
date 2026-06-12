using System;
using System.Collections;
using UnityEngine;
using UnityEngine.AI;

public class EnemyShielder : EnemyFodder
{
    [Header("Shielder — Armor")]
    public float MaxArmor = 100f;
    public float ShieldActivateRange = 10f;

    [Header("Shield Visual")]
    public GameObject ShieldObject;
    public Material ShieldMaterial;
    public GameObject ShieldBreakEffectPrefab;
    public GameObject ShieldBreakStaggerEffect;
    public Color ShieldColorFull = new Color(0.2f, 0.6f, 1f, 0.4f);
    public Color ShieldColorLow  = new Color(1f, 0.2f, 0.2f, 0.6f);

    [Header("Shield FX")]
    [Range(0f, 1f)] public float LowArmorThreshold = 0.3f;
    public float BlinkSpeed = 10f;
    public float DissolveDuration = 1.5f;

    [Header("Shield Break Recovery")]
    public float ShieldBreakStunDuration = 1.2f;
    public float AggressionSpeedMultiplier = 1.15f;
    public float AggressionFireRateMultiplier = 1.2f;

    [Header("Heavy — Grenade Burst")]
    public GameObject GrenadePrefab;
    public Transform GrenadeFirePoint;
    public float GrenadeRange = 35f;
    public int BurstCount = 4;
    public float BurstInterval = 0.25f;
    public float BurstCooldown = 2.5f;
    [Range(10f, 45f)] public float GrenadeArcAngle = 25f;
    public float GrenadeSpeed = 14f;

    [Header("Heavy — Melee Mode")]
    public bool isMeleeMode = true;
    public float MeleeDamage = 10f;
    public float MeleeAttackCooldown = 1.0f;

    public float CurrentArmor { get; private set; }
    public bool HasShield => CurrentArmor > 0f;
    public float ArmorFraction => CurrentArmor / MaxArmor;
    public bool IsShieldBreakRecovering => _shieldBreakRecovering;

    public Action<float> OnArmorChanged;

    // FIX: C# event replaces reflection polling in EnemySoundController
    public event Action OnBurstStarted;

    private bool _shieldVisible;
    private bool _isDissolving;
    private bool _shieldBreakRecovering;
    private bool _aggressionBoostApplied;
    private bool _isBursting;

    private float _grenadeFireTimer;
    private float _nextMeleeAttackTime;

    private Coroutine _blinkRoutine;
    private Coroutine _burstRoutine;
    private Coroutine _shieldBreakRoutine;
    private Coroutine _dissolveRoutine;
    private Coroutine _hitFlashRoutine;

    private static readonly int _baseColorID     = Shader.PropertyToID("_BaseColor");
    private static readonly int _colorID         = Shader.PropertyToID("_Color");
    private static readonly int _dissolveAmountID = Shader.PropertyToID("_DissolveAmount");

    // ── Lifecycle ────────────────────────────────────────────────────────────

    protected override void Awake()
    {
        Tier = EnemyTier.Heavy;
        MaxHP = 250f;
        base.Awake();

        CurrentArmor = MaxArmor;

        if (ShieldObject != null)
        {
            Renderer rend = ShieldObject.GetComponentInChildren<Renderer>();
            if (rend != null) ShieldMaterial = rend.material;
            ShieldObject.SetActive(false);
        }
    }

    protected override void Update()
    {
        base.Update();
        TickShieldVisibility();
    }

    protected override void Die(Vector3 deathVelocityDirection)
    {
        StopTrackedCoroutines();

        _isBursting            = false;
        _shieldBreakRecovering = false;
        HideShield();

        base.Die(deathVelocityDirection);
    }

    private void StopTrackedCoroutines()
    {
        if (_blinkRoutine      != null) StopCoroutine(_blinkRoutine);
        if (_burstRoutine      != null) StopCoroutine(_burstRoutine);
        if (_shieldBreakRoutine != null) StopCoroutine(_shieldBreakRoutine);
        if (_dissolveRoutine   != null) StopCoroutine(_dissolveRoutine);
        if (_hitFlashRoutine   != null) StopCoroutine(_hitFlashRoutine);

        _blinkRoutine       = null;
        _burstRoutine       = null;
        _shieldBreakRoutine = null;
        _dissolveRoutine    = null;
        _hitFlashRoutine    = null;
    }

    // ── AI overrides ─────────────────────────────────────────────────────────

    protected override void TryFireProjectile(float dist) { }

    protected override void TickIdle()
    {
        if (_nav == null || !_nav.enabled || !_nav.isOnNavMesh) return;

        if (TryRadarDetectPlayer(out float radarDist))
        {
            if (radarDist <= AggroRadius)
            {
                _nav.ResetPath();
                TransitionTo(EnemyState.Aggro);
                return;
            }

            if (radarDist <= GrenadeRange)
            {
                FacePlayer();
                if (!_isBursting) TryStartGrenadeBurst(radarDist);
                _nav.speed = PatrolSpeed;
            }
        }

        base.TickIdle();
    }

    protected override void TickAggro()
    {
        if (_shieldBreakRecovering)
        {
            if (_nav != null && _nav.enabled && _nav.isOnNavMesh)
            {
                _nav.ResetPath();
                _nav.velocity  = Vector3.zero;
                _nav.isStopped = true;
            }
            return;
        }

        if (_nav != null && _nav.enabled && _nav.isStopped) _nav.isStopped = false;
        if (_player == null) return;

        float dist = Vector3.Distance(transform.position, _player.position);

        if (dist > RadarRange)
        {
            _nav.ResetPath();
            // FIX: direct field access — _hasPatrolTarget is protected in EnemyFodder, no reflection needed
            _hasPatrolTarget = false;
            TransitionTo(EnemyState.Idle);
            return;
        }

        if (!_nav.enabled || !_nav.isOnNavMesh) return;

        TickHeavyCombat(dist);
    }

    // ── Combat ───────────────────────────────────────────────────────────────

    private void TickHeavyCombat(float dist)
    {
        if (dist > GrenadeRange)
        {
            _nav.speed = ChaseSpeed;
            _nav.SetDestination(_player.position);
        }
        else
        {
            _nav.speed = MoveSpeed;
            _nav.SetDestination(_player.position);
            FacePlayer();
            if (!_isBursting) TryStartGrenadeBurst(dist);
        }
    }

    private void TryStartGrenadeBurst(float dist)
    {
        float cd = _aggressionBoostApplied
            ? BurstCooldown / AggressionFireRateMultiplier
            : BurstCooldown;

        _grenadeFireTimer += Time.deltaTime;
        if (_grenadeFireTimer < cd) return;

        _grenadeFireTimer = 0f;

        if (!_isBursting && IsAlive && !_shieldBreakRecovering && !IsStaggered)
        {
            if (_burstRoutine != null) StopCoroutine(_burstRoutine);
            _burstRoutine = StartCoroutine(GrenadeBurstCoroutine());
        }
    }

    private IEnumerator GrenadeBurstCoroutine()
    {
        _isBursting = true;
        OnBurstStarted?.Invoke(); // FIX: event-based — EnemySoundController subscribes here

        for (int i = 0; i < BurstCount; i++)
        {
            if (_player == null || !IsAlive || _shieldBreakRecovering || IsStaggered) break;

            FireGrenadeArc();

            float interval = _aggressionBoostApplied
                ? BurstInterval / AggressionFireRateMultiplier
                : BurstInterval;

            yield return new WaitForSeconds(interval);
        }

        _isBursting   = false;
        _burstRoutine = null;
    }

    private void FireGrenadeArc()
    {
        GameObject prefab = GrenadePrefab;
        if (prefab == null) return;

        Transform fp     = GrenadeFirePoint != null ? GrenadeFirePoint : FirePoint;
        Vector3   origin = fp != null ? fp.position : transform.position + Vector3.up * 1.6f;
        Vector3   target = _player.position + Vector3.up * 1.0f;

        Vector3 flatDir = new Vector3(target.x - origin.x, 0f, target.z - origin.z).normalized;
        if (flatDir == Vector3.zero) flatDir = transform.forward;

        Vector3   rightAxis = Vector3.Cross(Vector3.up, flatDir).normalized;
        Vector3   fireDir   = Quaternion.AngleAxis(-GrenadeArcAngle, rightAxis) * flatDir;

        GameObject go = Instantiate(prefab, origin, Quaternion.LookRotation(fireDir));
        go.GetComponent<Enemy.Projectile>()?.Init(fireDir);
    }

    // ── Damage ───────────────────────────────────────────────────────────────

    public override void TakeDamage(float amount, Vector3 hitDirection, Vector3 hitPoint)
    {
        if (!IsAlive) return;

        if (HasShield)
        {
            float armorBefore = CurrentArmor;
            CurrentArmor = Mathf.Max(0f, CurrentArmor - amount);
            OnArmorChanged?.Invoke(CurrentArmor);

            FlashShield();

            if (ArmorFraction <= LowArmorThreshold && _blinkRoutine == null)
                _blinkRoutine = StartCoroutine(BlinkShield());

            if (CurrentArmor <= 0f)
            {
                if (_dissolveRoutine    != null) StopCoroutine(_dissolveRoutine);
                _dissolveRoutine    = StartCoroutine(DissolveShield());

                SpawnShieldBreakEffect();

                if (_shieldBreakRoutine != null) StopCoroutine(_shieldBreakRoutine);
                _shieldBreakRoutine = StartCoroutine(ShieldBreakRecovery());

                ApplyAggressionBoost();

                // Pass overflow damage through to HP
                float overflow = amount - armorBefore;
                if (overflow > 0f)
                    base.TakeDamage(overflow, hitDirection, hitPoint);
            }
            return;
        }

        base.TakeDamage(amount, hitDirection, hitPoint);
    }

    // ── Shield visuals ───────────────────────────────────────────────────────

    private void TickShieldVisibility()
    {
        if (!IsAlive)
        {
            HideShield();
            return;
        }

        if (ShieldObject == null || _player == null) return;

        float dist      = Vector3.Distance(transform.position, _player.position);
        bool shouldShow = dist <= ShieldActivateRange && HasShield;

        if (shouldShow && !_shieldVisible)
        {
            _shieldVisible = true;
            ShieldObject.SetActive(true);
        }
        else if (!shouldShow && _shieldVisible && !_isDissolving)
        {
            HideShield();
        }

        if (_shieldVisible) UpdateShieldColor();
    }

    private void HideShield()
    {
        if (!_shieldVisible) return;
        _shieldVisible = false;
        if (ShieldObject != null) ShieldObject.SetActive(false);
    }

    private void UpdateShieldColor()
    {
        if (ShieldMaterial == null) return;

        Color c = Color.Lerp(ShieldColorLow, ShieldColorFull, ArmorFraction);
        c.a = Mathf.Lerp(0.6f, 0.3f, ArmorFraction);

        ShieldMaterial.SetColor(_baseColorID, c);
        ShieldMaterial.SetColor(_colorID, c);
        ShieldMaterial.color = c;
    }

    private void FlashShield()
    {
        if (ShieldMaterial == null) return;
        if (_hitFlashRoutine != null) StopCoroutine(_hitFlashRoutine);
        _hitFlashRoutine = StartCoroutine(HitFlashRoutine());
    }

    private IEnumerator HitFlashRoutine()
    {
        ShieldMaterial.SetColor(_baseColorID, Color.white);
        yield return new WaitForSeconds(0.08f);
        UpdateShieldColor();
        _hitFlashRoutine = null;
    }

    private IEnumerator BlinkShield()
    {
        while (HasShield && ArmorFraction <= LowArmorThreshold)
        {
            ShieldMaterial.SetColor(_baseColorID, Color.red);
            yield return new WaitForSeconds(0.08f);
            UpdateShieldColor();
            yield return new WaitForSeconds(0.08f);
        }
        _blinkRoutine = null;
    }

    private IEnumerator DissolveShield()
    {
        if (_isDissolving) yield break;

        _isDissolving = true;
        float timer   = 0f;

        while (timer < DissolveDuration)
        {
            timer += Time.deltaTime;
            if (ShieldMaterial != null)
                ShieldMaterial.SetFloat(_dissolveAmountID, Mathf.Lerp(0f, 1f, timer / DissolveDuration));
            yield return null;
        }

        HideShield();
        _isDissolving    = false;
        _dissolveRoutine = null;
    }

    private void SpawnShieldBreakEffect()
    {
        if (ShieldBreakEffectPrefab == null || ShieldObject == null) return;
        Instantiate(ShieldBreakEffectPrefab, ShieldObject.transform.position, Quaternion.identity);
    }

    private IEnumerator ShieldBreakRecovery()
    {
        _shieldBreakRecovering = true;

        NavMeshAgent nav  = GetComponent<NavMeshAgent>();
        Rigidbody    rb   = GetComponent<Rigidbody>();
        Animator     anim = GetComponent<Animator>();

        if (nav != null && nav.isOnNavMesh)
        {
            nav.ResetPath();
            nav.isStopped = true;
            nav.enabled   = false;
        }

        if (rb != null)
        {
            rb.linearVelocity  = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
            rb.isKinematic     = true;
        }

        if (anim != null) anim.enabled = false;

        if (ShieldBreakStaggerEffect != null)
        {
            GameObject fx = Instantiate(ShieldBreakStaggerEffect,
                                        transform.position + Vector3.up * 1.5f,
                                        Quaternion.identity);
            Destroy(fx, 2.5f);
        }

        yield return new WaitForSeconds(ShieldBreakStunDuration);

        if (!IsAlive)
        {
            _shieldBreakRecovering = false;
            yield break;
        }

        if (rb   != null) rb.isKinematic = false;
        if (nav  != null) { nav.enabled = true; if (nav.isOnNavMesh) nav.isStopped = false; }
        if (anim != null) anim.enabled = true;

        _shieldBreakRecovering = false;
        _shieldBreakRoutine    = null;
    }

    private void ApplyAggressionBoost()
    {
        if (_aggressionBoostApplied) return;
        ChaseSpeed     *= AggressionSpeedMultiplier;
        _aggressionBoostApplied = true;
    }

    // ── Melee ────────────────────────────────────────────────────────────────

    private void OnTriggerStay(Collider other)
    {
        if (!isMeleeMode || !IsAlive || _shieldBreakRecovering) return;
        if (!other.CompareTag("Player")) return;
        if (Time.time < _nextMeleeAttackTime) return;

        if (other.TryGetComponent(out PlayerHealth health))
        {
            _nextMeleeAttackTime = Time.time + MeleeAttackCooldown;
            health.TakeDamage(MeleeDamage);

            if (ShieldBreakStaggerEffect != null)
            {
                GameObject fx = Instantiate(ShieldBreakStaggerEffect,
                                            other.transform.position + Vector3.up * 1f,
                                            Quaternion.identity);
                Destroy(fx, 1f);
            }
        }
    }
}