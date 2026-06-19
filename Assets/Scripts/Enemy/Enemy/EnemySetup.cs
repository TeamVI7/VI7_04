using UnityEngine;

/// <summary>
/// Drop this on every enemy. Assign a Stats asset.
/// Runs in Awake (before any other component) and pushes all values out.
/// Individual behaviour components never need to be touched in the Inspector
/// for tuning — everything flows from the SO.
/// </summary>
[DefaultExecutionOrder(-100)] // runs before all other enemy components
public class EnemySetup : MonoBehaviour
{
    public EnemyStatsSO Stats;

    private void Awake()
    {
        if (Stats == null)
        {
            Debug.LogError($"[EnemySetup] No Stats asset assigned on {gameObject.name}!", this);
            return;
        }

        Apply();
    }

    private void Apply()
    {
        if (TryGetComponent(out EnemyHealth health))
        {
            health.MaxHP             = Stats.MaxHP;
            health.ExecuteHealAmount = Stats.ExecuteHealAmount;
            health.StaggerThreshold  = Stats.StaggerThreshold;
            health.StaggerDuration   = Stats.StaggerDuration;
            health.StaggerWindow     = Stats.StaggerWindow;
            health.DeathImpulseScale = Stats.DeathImpulseScale;
            health.DeathVFXLifetime  = Stats.DeathVFXLifetime;
            health.DropUpwardForce   = Stats.DropUpwardForce;
        }

        if (TryGetComponent(out EnemyBrain brain))
        {
            brain.AggroRadius = Stats.AggroRadius;
            brain.RadarRange  = Stats.RadarRange;
        }

        if (TryGetComponent(out PatrolBehaviour patrol))
        {
            patrol.PatrolSpeed       = Stats.PatrolSpeed;
            patrol.ChaseSpeed        = Stats.ChaseSpeed;
            patrol.PreferredRange    = Stats.PreferredRange;
            patrol.PatrolRadius      = Stats.PatrolRadius;
            patrol.WaypointWaitTime  = Stats.WaypointWaitTime;
        }

        if (TryGetComponent(out ShieldBehaviour shield))
        {
            shield.MaxArmor            = Stats.MaxArmor;
            shield.ActivateRange       = Stats.ShieldActivateRange;
            shield.LowArmorThreshold   = Stats.LowArmorThreshold;
            shield.DissolveDuration    = Stats.DissolveDuration;
            shield.BreakFlashDuration  = Stats.ShieldBreakFlashDuration;
            shield.StunDuration        = Stats.ShieldStunDuration;
            shield.SpeedMultiplier     = Stats.ShieldSpeedBoost;
            shield.FireRateMultiplier  = Stats.ShieldFireRateBoost;
        }

        if (TryGetComponent(out GrenadeBurstBehaviour grenades))
        {
            grenades.AttackRange   = Stats.AttackRange;
            grenades.BurstCount    = Stats.BurstCount;
            grenades.BurstInterval = Stats.BurstInterval;
            grenades.BurstCooldown = Stats.BurstCooldown;
            grenades.ArcAngle      = Stats.ArcAngle;
        }

        if (TryGetComponent(out LaserBehaviour laser))
        {
            laser.AttackRange    = Stats.LaserRange;
            laser.DamagePerSecond = Stats.LaserDPS;
        }

        if (TryGetComponent(out MeleeBehaviour melee))
        {
            melee.Damage   = Stats.MeleeDamage;
            melee.Cooldown = Stats.MeleeCooldown;
        }

        if (TryGetComponent(out StealthBehaviour stealth))
        {
            stealth.VisibleDuration    = Stats.VisibleDuration;
            stealth.CloakedDuration    = Stats.CloakedDuration;
            stealth.FadeSpeed          = Stats.StealthFadeSpeed;
            stealth.BleedDuration      = Stats.BleedDuration;
            stealth.BleedDamagePerTick = Stats.BleedDamagePerTick;
            stealth.BleedInterval      = Stats.BleedInterval;
        }

        if (TryGetComponent(out EnemyRagdoll ragdoll))
        {
            ragdoll.VelocitySeedScale  = Stats.VelocitySeedScale;
            ragdoll.UpwardKick         = Stats.UpwardKick;
            ragdoll.LifetimeAfterDeath = Stats.RagdollLifetime;
        }

        // ── ĐẨY THÔNG SỐ KHỚP TOÀN BỘ VỚI SHOTGUN ──────────────────
        if (TryGetComponent(out ShotgunAttackBehaviour shotgun))
        {
            shotgun.AttackRange     = Stats.ShotgunAttackRange;
            shotgun.DamagePerPellet = Stats.ShotgunDamagePerPellet;
            shotgun.PelletsPerShot  = Stats.ShotgunPelletsPerShot;
            shotgun.SpreadAngle     = Stats.ShotgunSpreadAngle;
            shotgun.FireRate        = Stats.ShotgunFireRate;
        }

        // ── ĐẨY THÔNG SỐ KHỚP TOÀN BỘ VỚI SMG ──────────────────────
        if (TryGetComponent(out SMGAttackBehaviour smg))
        {
            smg.AttackRange    = Stats.SMGAttackRange;
            smg.DamagePerShot  = Stats.SMGDamagePerShot;
            smg.FireRate       = Stats.SMGFireRate;
        }

        // ── ĐẨY THÔNG SỐ KHỚP TOÀN BỘ VỚI SNIPER ───────────────────
        if (TryGetComponent(out SniperAttackBehaviour sniper))
        {
            sniper.AttackRange   = Stats.SniperAttackRange;
            sniper.FireCooldown  = Stats.SniperFireCooldown;
        }
    }

#if UNITY_EDITOR
    // Preview the SO name in the hierarchy for quick identification
    private void OnValidate()
    {
        if (Stats != null) gameObject.name = $"Enemy_{Stats.EnemyName}";
    }
#endif
}