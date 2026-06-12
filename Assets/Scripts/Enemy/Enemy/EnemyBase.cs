using System;
using UnityEngine;
using Enemy; // RagdollController namespace

public enum EnemyTier  { Fodder, Heavy }
public enum EnemyState { Idle, Aggro, Staggered, Ragdoll }
public enum StaggerPotency { Light, Moderate, Heavy }

public abstract class EnemyBase : MonoBehaviour, IDamageable
{
    [Header("Identity")]
    public EnemyTier Tier = EnemyTier.Fodder;

    [Header("Stats")]
    public float MaxHP           = 100f;
    // Heal amount is now a flat value per execution, not MaxHP-fraction based
    public float ExecuteHealAmount = 25f;

    [Header("Detection")]
    public float    AggroRadius = 15f;
    public LayerMask PlayerLayer;

    [Header("Stagger — Heavy")]
    [Range(0.1f, 1f)]
    public float HeavyStaggerThresholdFraction = 0.4f;
    public float HeavyStaggerWindow            = 4f;

    [Header("Stagger — Fodder")]
    [Range(0.1f, 1f)]
    public float FodderStaggerThresholdFraction = 0.25f;  // lower threshold so fodder staggers more easily
    public float FodderStaggerWindow            = 3f;

    [Header("Death")]
    [Tooltip("Multiplies velocity when seeding ragdoll physics.")]
    public float DeathImpulseScale = 8f;

    [Header("Death VFX")]
    [Tooltip("Explosion / dissolve VFX spawned on death.")]
    public GameObject DeathExplosionPrefab;
    public float DeathVFXLifetime = 3f;

    [Header("Drop on Death")]
    [Tooltip("Prefab that spawns on execute (ammo pack, health orb, etc.).")]
    public GameObject DropPrefab;
    public float DropUpwardForce = 4f;

    [Header("VFX & UI")]
    public GameObject DamagePopupPrefab;
    public float      SpawnPopupHeight = 2f;

    // ── Public state ─────────────────────────────────────────────────────────
    public float     CurrentHP  { get; private set; }
    public bool      IsAlive    => CurrentHP > 0f && _state != EnemyState.Ragdoll;
    public bool      IsStaggered => _state == EnemyState.Staggered;
    public EnemyState State     => _state;

    // ── Events ───────────────────────────────────────────────────────────────
    public Action<float> OnHealthChanged;
    /// <summary>Fires when the enemy enters the Staggered state. Grapple/melee system hooks here.</summary>
    public Action OnStaggerEntered;
    /// <summary>Fires on execute. Subscribe to spawn drop, play VFX, etc.</summary>
    public Action<Vector3> OnExecuted;

    // ── Internal ─────────────────────────────────────────────────────────────
    protected EnemyState _state = EnemyState.Idle;
    private float _staggerTimer;
    private float _damageAccumulator;
    private float _damageWindowTimer;

    private RagdollController _ragdollController;
    private Rigidbody         _mainRb;  // used to read velocity for execute impulse

    // ── Unity ────────────────────────────────────────────────────────────────
    protected virtual void Awake()
    {
        CurrentHP           = MaxHP;
        _ragdollController  = GetComponent<RagdollController>();
        _mainRb             = GetComponent<Rigidbody>();
    }

    protected virtual void Update()
    {
        if (!IsAlive) return;
        TickStateMachine();
        TickDamageWindow();
    }

    // ── State machine ────────────────────────────────────────────────────────
    private void TickStateMachine()
    {
        switch (_state)
        {
            case EnemyState.Idle:      TickIdle();      break;
            case EnemyState.Aggro:     TickAggro();     break;
            case EnemyState.Staggered: TickStaggered(); break;
        }
    }

    protected virtual void TickIdle()
    {
        if (Physics.CheckSphere(transform.position, AggroRadius, PlayerLayer))
            TransitionTo(EnemyState.Aggro);
    }

    protected virtual void TickAggro() { }

    protected virtual void TickStaggered()
    {
        _staggerTimer -= Time.deltaTime;
        if (_staggerTimer <= 0f)
        {
            OnStaggerExpired();
            TransitionTo(EnemyState.Aggro);
        }
    }

    protected void TransitionTo(EnemyState next)
    {
        if (_state == next) return;
        _state = next;
        OnStateEntered(next);
    }

    protected virtual void OnStateEntered(EnemyState newState) { }
    protected virtual void OnStaggerExpired() { }

    // ── Damage / IDamageable ─────────────────────────────────────────────────
    public virtual void TakeDamage(float amount, Vector3 hitDirection, Vector3 hitPoint)
    {
        if (!IsAlive) return;

        CurrentHP = Mathf.Max(0f, CurrentHP - amount);
        OnHealthChanged?.Invoke(CurrentHP);

        SpawnDamagePopup((int)amount);
        PlayPainSound();

        // FIX: Both Fodder and Heavy now accumulate stagger — different thresholds
        float threshold = (Tier == EnemyTier.Heavy) ? HeavyStaggerThresholdFraction : FodderStaggerThresholdFraction;
        float window    = (Tier == EnemyTier.Heavy) ? HeavyStaggerWindow            : FodderStaggerWindow;

        if (_state == EnemyState.Aggro)
        {
            _damageAccumulator += amount;
            _damageWindowTimer  = window;

            if (_damageAccumulator >= MaxHP * threshold)
            {
                float staggerDuration = (Tier == EnemyTier.Heavy) ? 2.5f : 1.2f;
                EnterStagger(staggerDuration, StaggerPotency.Moderate);
                _damageAccumulator = 0f;
            }
        }

        if (CurrentHP <= 0f) Die(hitDirection);
    }

    private void SpawnDamagePopup(int dmg)
    {
        if (DamagePopupPrefab == null) return;
        Vector3    spawnPos = transform.position + Vector3.up * SpawnPopupHeight;
        GameObject popupGO  = Instantiate(DamagePopupPrefab, spawnPos, Quaternion.identity);
        popupGO.GetComponent<DamagePopup>()?.Setup(dmg);
    }

    private void PlayPainSound()
    {
        if (SoundManager.Instance != null)
            SoundManager.Instance.PlaySFX(SFXType.Pain, transform.position);
    }

    // ── Stagger ──────────────────────────────────────────────────────────────
    public void EnterStagger(float duration, StaggerPotency potency)
    {
        if (!IsAlive || _state == EnemyState.Staggered) return;
        _staggerTimer = duration;
        TransitionTo(EnemyState.Staggered);
        OnStaggerEntered?.Invoke();  // FIX: fire event so grapple/melee system can react
    }

    // ── Execute ──────────────────────────────────────────────────────────────
    public virtual void TriggerExecute(Transform playerTransform)
    {
        if (!IsAlive) return;

        // Flat heal reward — consistent regardless of enemy's remaining HP
        if (playerTransform.TryGetComponent(out PlayerHealth playerHealth))
            playerHealth.Heal(ExecuteHealAmount);

        // FIX: use player's actual velocity for ragdoll impulse (GDD §7.4)
        Vector3 impulse = playerTransform.forward * DeathImpulseScale; // fallback
        if (playerTransform.TryGetComponent(out Rigidbody playerRb))
            impulse = playerRb.linearVelocity;

        OnExecuted?.Invoke(transform.position); // FIX: fire drop/VFX event
        SpawnDrop();
        Die(impulse);
    }

    // ── Death ────────────────────────────────────────────────────────────────
    protected virtual void Die(Vector3 deathVelocityDirection)
    {
        // Guard: Die() can be reached from multiple paths in the same frame
        // (e.g. shield overflow damage + direct HP check both resolving at once).
        if (_state == EnemyState.Ragdoll) return;

        TransitionTo(EnemyState.Ragdoll);
        SpawnDeathVFX();
        _ragdollController?.ActivateRagdoll(deathVelocityDirection * DeathImpulseScale);
    }

    private void SpawnDeathVFX()
    {
        if (DeathExplosionPrefab == null) return;
        GameObject vfx = Instantiate(DeathExplosionPrefab,
                                     transform.position + Vector3.up,
                                     Quaternion.identity);
        Destroy(vfx, DeathVFXLifetime);
    }

    private void SpawnDrop()
    {
        if (DropPrefab == null) return;
        GameObject drop = Instantiate(DropPrefab,
                                      transform.position + Vector3.up,
                                      Quaternion.identity);
        // Give it a little pop-up physics impulse if it has a Rigidbody
        if (drop.TryGetComponent(out Rigidbody dropRb))
            dropRb.AddForce(Vector3.up * DropUpwardForce, ForceMode.Impulse);
    }

    // ── Damage window ticker ─────────────────────────────────────────────────
    private void TickDamageWindow()
    {
        if (_damageWindowTimer > 0f)
        {
            _damageWindowTimer -= Time.deltaTime;
            if (_damageWindowTimer <= 0f) _damageAccumulator = 0f;
        }
    }
}