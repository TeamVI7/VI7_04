using System;
using System.Collections;
using UnityEngine;

/// <summary>
/// Owns HP, stagger accumulation, death. Nothing else.
/// All other systems subscribe to the events.
/// </summary>
[System.Serializable]
public class LootDrop
{
    public GameObject Prefab;
    [Range(0f, 1f)]
    [Tooltip("Independent chance THIS entry drops — rolled separately from every other entry, so any number of entries can succeed from the same kill, not just one.")]
    public float Chance = 1f;
    [Tooltip("How many copies to spawn if this entry's roll succeeds.")]
    public int MinCount = 1;
    public int MaxCount = 1;
}

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
    [Tooltip("Legacy single-drop field — used when DropTable is empty. Leave set for enemies that always drop exactly one specific thing.")]
    public GameObject DropPrefab;
    [Range(0f, 1f)]
    [Tooltip("Overall chance this enemy drops anything at all on death. 1 = always drops (if DropPrefab or DropTable has something to drop).")]
    public float DropChance = 1f;
    [Tooltip("Independent-roll drop pool — every entry is rolled separately, so a single kill can drop none, one, or several of these at once. Leave empty to just use DropPrefab.")]
    public LootDrop[] DropTable;
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
    public event Action<float, float, float, Vector3, Vector3> OnDamaged; // (amount, currentHP, maxHP, hitDirection, hitPoint)
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
        OnDamaged?.Invoke(amount, CurrentHP, MaxHP, hitDirection, hitPoint);

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
        if (UnityEngine.Random.value > DropChance) return;

        if (DropTable != null && DropTable.Length > 0)
        {
            foreach (var entry in DropTable)
            {
                if (entry == null || entry.Prefab == null) continue;
                if (UnityEngine.Random.value > entry.Chance) continue;

                int count = UnityEngine.Random.Range(entry.MinCount, entry.MaxCount + 1);
                for (int i = 0; i < count; i++)
                    SpawnOneDrop(entry.Prefab, spawnPosition);
            }
            return;
        }

        if (DropPrefab != null) SpawnOneDrop(DropPrefab, spawnPosition);
    }

    private void SpawnOneDrop(GameObject prefab, Vector3 spawnPosition)
    {
        // Small horizontal scatter so multiple drops from the same kill don't spawn
        // stacked exactly on top of each other.
        Vector3 scatter = new Vector3(UnityEngine.Random.Range(-0.3f, 0.3f), 0f, UnityEngine.Random.Range(-0.3f, 0.3f));

        var drop = Instantiate(prefab, spawnPosition + Vector3.up + scatter, Quaternion.identity);
        if (drop.TryGetComponent(out Rigidbody dropRb))
        {
            Vector3 force = Vector3.up * DropUpwardForce
                          + new Vector3(UnityEngine.Random.Range(-1f, 1f), 0f, UnityEngine.Random.Range(-1f, 1f));
            dropRb.AddForce(force, ForceMode.Impulse);
        }
    }
}