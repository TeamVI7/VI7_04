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

    [Header("Last Stand")]
    [Tooltip("When HP runs out, don't die — drop to a DOWNED state (a held stagger) and wait " +
             "for the player to land a finishing melee blow. Built for the boss: it turns the " +
             "kill into an execute, which is what triggers the katana bisect.")]
    public bool DownedOnLethalDamage = false;
    [Tooltip("How long the downed state holds. If the player doesn't finish it in time, see " +
             "DieWhenDownedExpires.")]
    public float DownedDuration = 8f;
    [Tooltip("On timeout: true = it dies anyway (no finisher, no bisect), false = it gets back " +
             "up on a sliver of HP and the fight continues until the next lethal hit downs it again.")]
    public bool DieWhenDownedExpires = true;

    [Header("Death Sequence")]
    public float DeathImpulseScale           = 8f;
    [Tooltip("Number of explosions to play before despawning.")]
    public int   ExplosionCount              = 3;
    [Tooltip("Time to wait between each explosion.")]
    public float DelayBetweenExplosions      = 0.4f;
    [Tooltip("How far apart the explosions can randomly scatter.")]
    public float ExplosionScatterRadius      = 1.2f;
    [Tooltip("Seconds the body stays in the world after the last explosion, before it's removed. 0 despawns it on the spot — fine for rank-and-file, far too abrupt for a boss, which should leave a wreck standing for a few seconds. Ignored when something else owns despawn timing (an EnemyRagdoll, or a corpse that's been sliced apart).")]
    [Min(0f)] public float CorpseLifetime     = 0f;

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

    /// <summary>
    /// Out of HP but not dead — kneeling, waiting to be finished. Kept "alive" on a sliver of
    /// HP on purpose, because Execute() and the melee hit path both refuse a dead target.
    /// </summary>
    public bool  IsDowned   { get; private set; }

    // ── Events — everything else hooks here ──────────────────────────────────
    public event Action<float, float, float, Vector3, Vector3> OnDamaged; // (amount, currentHP, maxHP, hitDirection, hitPoint)
    public event Action<float>        OnHealed;         // (currentHP)
    public event Action               OnStaggerEntered;
    public event Action               OnStaggerExpired;
    public event Action<Vector3>      OnDied;           // (impulse direction)
    public event Action               OnExecuted;
    /// <summary>Fired when HP runs out but the last stand begins instead of death. Hook UI
    /// ("FINISH IT"), audio, or a camera push here.</summary>
    public event Action               OnDowned;
    /// <summary>Fired if a downed enemy gets back up (DieWhenDownedExpires off).</summary>
    public event Action               OnDownedRecovered;

    // ── Internal ─────────────────────────────────────────────────────────────
    private bool  _dead;
    private bool  _staggered;
    private Vector3 _lastHitDirection;
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

        _lastHitDirection = hitDirection;

        // Already on its knees — nothing but the finisher resolves this. Shooting a downed
        // enemy does nothing except run out its clock (see ExitStagger).
        if (IsDowned) return;

        CurrentHP = Mathf.Max(0f, CurrentHP - amount);
        OnDamaged?.Invoke(amount, CurrentHP, MaxHP, hitDirection, hitPoint);

        SoundManager.Instance?.PlaySFX(SFXType.Pain, transform.position);

        AccumulateStagger(amount);

        if (CurrentHP > 0f) return;

        if (DownedOnLethalDamage) EnterDowned();
        else                      Die(hitDirection);
    }

    // ── Last stand ───────────────────────────────────────────────────────────
    /// <summary>
    /// Out of HP, but held one hit short of death. Enters a stagger — which is exactly what
    /// PlayerMelee's execute check looks for — and holds it for DownedDuration.
    /// </summary>
    private void EnterDowned()
    {
        IsDowned = true;

        // A sliver, not zero: IsAlive gates both TakeDamage and Execute, and a "dead" enemy
        // can't be executed, which would make the finisher impossible.
        CurrentHP = Mathf.Max(0.01f, MaxHP * 0.001f);

        // Set directly rather than through EnterStagger(), which refuses to re-enter a stagger
        // that's already running and would cap the hold at StaggerDuration.
        _staggered    = true;
        _staggerTimer = DownedDuration;

        OnDowned?.Invoke();
        OnStaggerEntered?.Invoke();   // EnemyBrain and MechBossBrain both switch to Staggered here
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

        if (IsDowned)
        {
            IsDowned = false;

            // Nobody came to finish it. Either it bleeds out on its own — no execute, so no
            // bisect — or it gets back up on its sliver of HP and the fight continues.
            if (DieWhenDownedExpires) { Die(_lastHitDirection); return; }

            OnDownedRecovered?.Invoke();
        }

        OnStaggerExpired?.Invoke();
    }

    // ── Death ────────────────────────────────────────────────────────────────
    private void Die(Vector3 impulse)
    {
        if (_dead) return;
        _dead    = true;
        IsDowned = false;

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

        // Despawn belongs to whichever component owns the corpse, and this one only
        // takes it when nothing else has.
        //
        // A ragdoll owns it via LifetimeAfterDeath — destroying the root here would
        // cut the physics off before it ever plays out.
        if (TryGetComponent(out EnemyRagdoll _)) yield break;

        // A sliced body owns it too: EnemySliceable has already scheduled its own
        // removal around the pieces it left behind (RootDespawnMargin). This used to
        // destroy the root first and win, which is what made a sliced boss — no
        // ragdoll to defer to — disappear almost the moment it was cut, instead of
        // leaving a wreck behind.
        if (TryGetComponent(out EnemySliceable sliceable)
            && sliceable.HasBeenSliced
            && sliceable.DespawnRootAfterSlice) yield break;

        if (CorpseLifetime > 0f) Destroy(gameObject, CorpseLifetime);
        else Destroy(gameObject);
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