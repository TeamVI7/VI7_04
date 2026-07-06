using System;
using System.Collections;
using UnityEngine;

/// <summary>
/// Owns HP, stagger accumulation, death. Nothing else.
/// All other systems subscribe to the events.
/// </summary>
[DisallowMultipleComponent]
public class EnemyHealth : MonoBehaviour, IDamageable
{
    [Header("Stats")]
    public float MaxHP                       = 100f;
    public float ExecuteHealAmount           = 25f;

    [Header("Stagger")]
    [Range(0.01f, 1f)]
    public float StaggerThreshold            = 0.25f;   // fraction of MaxHP to accumulate before stagger
    public float StaggerDuration             = 1.5f;
    public float StaggerWindow               = 3f;      // accumulator resets if no hit within this time

    [Header("Death Sequence")]
    public float DeathImpulseScale           = 8f;
    [Tooltip("Number of explosions to play before despawning.")]
    public int   ExplosionCount              = 3;
    [Tooltip("Time to wait between each explosion.")]
    public float DelayBetweenExplosions      = 0.4f;
    [Tooltip("How far apart the explosions can randomly scatter.")]
    public float ExplosionScatterRadius      = 1.2f;

    [Header("Drop")]
    public GameObject DropPrefab;
    public float      DropUpwardForce        = 4f;

    [Header("VFX")]
    public GameObject DeathVFXPrefab;
    public float      DeathVFXLifetime       = 3f;
    public GameObject DamagePopupPrefab;
    public float      PopupSpawnHeight       = 2f;

    // ── Public state ─────────────────────────────────────────────────────────
    public float CurrentHP  { get; private set; }
    public bool  IsAlive    => CurrentHP > 0f && !_dead;

    // ── Events — everything else hooks here ──────────────────────────────────
    public event Action<float, float> OnDamaged;        // (currentHP, maxHP)
    public event Action<float>        OnHealed;         // (currentHP)
    public event Action               OnStaggerEntered;
    public event Action               OnStaggerExpired;
    public event Action<Vector3>      OnDied;           // (impulse direction)
    public event Action               OnExecuted;

    // ── Internal ─────────────────────────────────────────────────────────────
    private bool  _dead;
    private bool  _staggered;
    private float _staggerTimer;
    private float _damageAccumulator;
    private float _damageWindowTimer;

    private void Awake() => CurrentHP = MaxHP;

    private void Update()
    {
        if (!IsAlive) return;

        if (_staggered)
        {
            _staggerTimer -= Time.deltaTime;
            if (_staggerTimer <= 0f) ExitStagger();
        }

        if (_damageWindowTimer > 0f)
        {
            _damageWindowTimer -= Time.deltaTime;
            if (_damageWindowTimer <= 0f) _damageAccumulator = 0f;
        }
    }

    // ── IDamageable ──────────────────────────────────────────────────────────
    public void TakeDamage(float amount, Vector3 hitDirection, Vector3 hitPoint)
    {
        if (!IsAlive) return;

        CurrentHP = Mathf.Max(0f, CurrentHP - amount);
        OnDamaged?.Invoke(CurrentHP, MaxHP);

        SoundManager.Instance?.PlaySFX(SFXType.Pain, transform.position);

        AccumulateStagger(amount);

        if (CurrentHP <= 0f) Die(hitDirection);
    }

    public void Heal(float amount)
    {
        if (!IsAlive) return;
        CurrentHP = Mathf.Min(MaxHP, CurrentHP + amount);
        OnHealed?.Invoke(CurrentHP);
    }

    public void Execute(Transform playerTransform)
    {
        if (!IsAlive) return;

        PlayerHealth.Instance?.Heal(ExecuteHealAmount);

        Vector3 impulse = playerTransform.forward * DeathImpulseScale;
        if (playerTransform.TryGetComponent(out Rigidbody rb))
            impulse = rb.linearVelocity;

        OnExecuted?.Invoke();   
        Die(impulse); 
    }

    // ── Stagger ──────────────────────────────────────────────────────────────
    private void AccumulateStagger(float amount)
    {
        if (_staggered) return;

        _damageAccumulator += amount;
        _damageWindowTimer  = StaggerWindow;

        if (_damageAccumulator >= MaxHP * StaggerThreshold)
        {
            _damageAccumulator = 0f;
            EnterStagger();
        }
    }

    public void EnterStagger()
    {
        if (!IsAlive || _staggered) return;
        _staggered    = true;
        _staggerTimer = StaggerDuration;
        OnStaggerEntered?.Invoke();
    }

    private void ExitStagger()
    {
        _staggered = false;
        OnStaggerExpired?.Invoke();
    }

    // ── Death ────────────────────────────────────────────────────────────────
    private void Die(Vector3 impulse)
    {
        if (_dead) return;
        _dead = true;

        OnDied?.Invoke(impulse);
        StartCoroutine(DeathSequenceRoutine());
    }

    private IEnumerator DeathSequenceRoutine()
    {
        if (DeathVFXPrefab != null)
        {
            for (int i = 0; i < ExplosionCount; i++)
            {
                Vector3 randomOffset = UnityEngine.Random.insideUnitSphere * ExplosionScatterRadius;
                randomOffset.y = Mathf.Abs(randomOffset.y); 

                var vfx = Instantiate(DeathVFXPrefab, transform.position + Vector3.up + randomOffset, Quaternion.identity);
                Destroy(vfx, DeathVFXLifetime);

                yield return new WaitForSeconds(DelayBetweenExplosions);
            }
        }

        // Cache the position before doing anything destructive
        Vector3 finalPosition = transform.position;

        SpawnDrop(finalPosition);

        // If this enemy has a ragdoll, IT owns despawn timing (LifetimeAfterDeath) —
        // destroying the GameObject here would cut the ragdoll physics off before it
        // ever gets to play out. Only self-destroy for enemies with no ragdoll to hand off to.
        if (!TryGetComponent(out EnemyRagdoll ragdoll))
            Destroy(gameObject);
    }

    private void SpawnDrop(Vector3 spawnPosition)
    {
        if (DropPrefab == null) return;
        
        var drop = Instantiate(DropPrefab, spawnPosition + Vector3.up, Quaternion.identity);
        if (drop.TryGetComponent(out Rigidbody dropRb))
            dropRb.AddForce(Vector3.up * DropUpwardForce, ForceMode.Impulse);
    }
}