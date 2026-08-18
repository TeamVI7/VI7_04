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
            health.DropChance        = Stats.DropChance;
        }

        if (TryGetComponent(out EnemyBrain brain))
        {
            brain.AggroRadius = Stats.AggroRadius;
            brain.RadarRange  = Stats.RadarRange;
        }

        if (TryGetComponent(out EnemyAnimatorController animController))
        {
            animController.SetWeaponType(Stats.WeaponAnimType);
        }

        if (TryGetComponent(out PatrolBehaviour patrol))
        {
            patrol.PatrolSpeed       = Stats.PatrolSpeed;
            patrol.ChaseSpeed        = Stats.ChaseSpeed;
            patrol.PreferredRange    = Stats.PreferredRange;
            patrol.MinRange          = Stats.MinRange;
            patrol.PatrolRadius      = Stats.PatrolRadius;
            patrol.WaypointWaitTime  = Stats.WaypointWaitTime;
            patrol.MovementStyle     = Stats.MovementStyle;
            patrol.StrafeRadius      = Stats.StrafeRadius;
            patrol.StrafeInterval    = Stats.StrafeInterval;
        }

        // Airborne enemies carry FlyingMovement instead of PatrolBehaviour — same
        // role (patrol + combat positioning), NavMesh-free.
        if (TryGetComponent(out FlyingMovement flight))
        {
            flight.HoverAltitude             = Stats.FlyHoverAltitude;
            flight.CombatAltitudeAbovePlayer = Stats.FlyCombatAltitudeAbovePlayer;
            flight.PatrolSpeed               = Stats.FlyPatrolSpeed;
            flight.ChaseSpeed                = Stats.FlyChaseSpeed;
            flight.Acceleration              = Stats.FlyAcceleration;
            flight.Style                     = Stats.FlyStyle;
            flight.PreferredRange            = Stats.FlyPreferredRange;
            flight.MinRange                  = Stats.FlyMinRange;
            flight.OrbitRadius               = Stats.FlyOrbitRadius;
            flight.OrbitInterval             = Stats.FlyOrbitInterval;
            flight.PatrolRadius              = Stats.FlyPatrolRadius;
        }

        if (TryGetComponent(out KamikazeDroneBehaviour drone))
        {
            drone.LockRange           = Stats.DroneLockRange;
            drone.LockTime            = Stats.DroneLockTime;
            drone.DiveSpeed           = Stats.DroneDiveSpeed;
            drone.DiveTurnRate        = Stats.DroneDiveTurnRate;
            drone.MaxDiveTime         = Stats.DroneMaxDiveTime;
            drone.RecoverTime         = Stats.DroneRecoverTime;
            drone.ProximityFuseRadius = Stats.DroneProximityFuseRadius;
            drone.ExplosionRadius     = Stats.DroneExplosionRadius;
            drone.MaxDamage           = Stats.DroneMaxDamage;
            drone.MinDamage           = Stats.DroneMinDamage;
            drone.KnockbackForce      = Stats.DroneKnockbackForce;
        }

        if (TryGetComponent(out FlyingSniperBehaviour flyingSniper))
        {
            flyingSniper.AttackRange  = Stats.FlyingSniperRange;
            flyingSniper.Damage       = Stats.FlyingSniperDamage;
            flyingSniper.FireCooldown = Stats.FlyingSniperFireCooldown;
            flyingSniper.ChargeTime   = Stats.FlyingSniperChargeTime;
            flyingSniper.LockTime     = Stats.FlyingSniperLockTime;
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

        // MeleeAttackBehaviour (EnemyMelee.cs) supersedes the old tick-damage
        // MeleeBehaviour — proper windup/cooldown instead of OnTriggerStay damage.
        if (TryGetComponent(out MeleeAttackBehaviour melee))
        {
            melee.Damage        = Stats.MeleeDamage;
            melee.Cooldown      = Stats.MeleeCooldown;
            melee.AttackRange   = Stats.MeleeRange;
            melee.LungeDistance = Stats.MeleeLungeDistance;
            melee.LungeSpeed    = Stats.MeleeLungeSpeed;
            melee.KnockbackForce = Stats.MeleeKnockbackForce;
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

        if (TryGetComponent(out SMGAttackBehaviour smg))
        {
            smg.AttackRange    = Stats.SMGAttackRange;
            smg.DamagePerShot  = Stats.SMGDamagePerShot;
            smg.FireRate       = Stats.SMGFireRate;
            smg.TelegraphTime  = Stats.SMGTelegraphTime;
            ApplyRangedAiming(smg);
        }

        if (TryGetComponent(out PistolAttackBehaviour pistol))
        {
            pistol.AttackRange    = Stats.PistolAttackRange;
            pistol.DamagePerShot  = Stats.PistolDamagePerShot;
            pistol.FireRate       = Stats.PistolFireRate;
            pistol.TelegraphTime  = Stats.PistolTelegraphTime;
            ApplyRangedAiming(pistol);
        }

        if (TryGetComponent(out SniperAttackBehaviour sniper))
        {
            sniper.AttackRange   = Stats.SniperAttackRange;
            sniper.FireCooldown  = Stats.SniperFireCooldown;
            sniper.Damage        = Stats.SniperDamage;
        }

        // RiotShieldBehaviour lives on the shield prop (a child of the hand bone), not
        // on this root object, so it needs a child search rather than TryGetComponent.
        if (GetComponentInChildren<RiotShieldBehaviour>() is RiotShieldBehaviour riotShield)
        {
            riotShield.BlockAngle = Stats.ShieldBlockAngle;
        }

        if (TryGetComponent(out EnemyHitReaction hitReaction))
        {
            hitReaction.MaxKickDistance          = Stats.HitReactionMaxKickDistance;
            hitReaction.KickDuration             = Stats.HitReactionKickDuration;
            hitReaction.DamageFractionForMaxKick = Stats.HitReactionDamageFractionForMaxKick;
            hitReaction.MinDamageFraction        = Stats.HitReactionMinDamageFraction;
            hitReaction.FlashDuration            = Stats.HitReactionFlashDuration;
            hitReaction.HeadshotRadius           = Stats.HitReactionHeadshotRadius;
            hitReaction.HeadshotFlashDurationMultiplier = Stats.HitReactionHeadshotFlashDurationMultiplier;
            hitReaction.StaggerFlashDuration     = Stats.HitReactionStaggerFlashDuration;
            hitReaction.LimbDismemberFraction    = Stats.HitReactionLimbDismemberFraction;
            hitReaction.LimbScaleDownDuration    = Stats.HitReactionLimbScaleDownDuration;
            hitReaction.MaxTorsoTwistAngle       = Stats.TorsoMaxTwistAngle;
            hitReaction.TorsoTwistDuration       = Stats.TorsoTwistDuration;
        }

        if (TryGetComponent(out EnemyAvengeReaction avenge))
        {
            avenge.AvengeRadius          = Stats.AvengeRadius;
            avenge.EnrageSpeedMultiplier = Stats.AvengeSpeedMultiplier;
            avenge.EnrageDuration        = Stats.AvengeDuration;
        }

        if (TryGetComponent(out EnemyDodgeBehaviour dodge))
        {
            dodge.DodgeChance       = Stats.DodgeChance;
            dodge.DodgeCooldown     = Stats.DodgeCooldown;
            dodge.MinTriggerRange   = Stats.DodgeMinTriggerRange;
            dodge.MaxTriggerRange   = Stats.DodgeMaxTriggerRange;
            dodge.DodgeDistance     = Stats.DodgeDistance;
            dodge.DodgeSpeed        = Stats.DodgeSpeed;
        }

        if (TryGetComponent(out EnemyLookAt lookAt))
        {
            lookAt.HeadWeight    = Stats.LookHeadWeight;
            lookAt.TorsoWeight   = Stats.LookTorsoWeight;
            lookAt.MaxYawAngle   = Stats.LookMaxYawAngle;
            lookAt.MaxPitchAngle = Stats.LookMaxPitchAngle;
            lookAt.TurnSpeed     = Stats.LookTurnSpeed;
        }

        if (TryGetComponent(out EnemyImpactVFX impactVFX))
        {
            impactVFX.ImpactLifetime     = Stats.ImpactVFXLifetime;
            impactVFX.MinDamageFraction  = Stats.ImpactVFXMinDamageFraction;
        }
    }

    // Shared aiming/spread push for every EnemyRangedAttackBehaviour subtype (SMG,
    // Pistol) — one place to tune "how well do my ranged enemies aim" across the roster.
    private void ApplyRangedAiming(EnemyRangedAttackBehaviour ranged)
    {
        ranged.AimTurnSpeed = Stats.RangedAimTurnSpeed;
        ranged.MaxFireAngle = Stats.RangedMaxFireAngle;
        ranged.Spread       = Stats.RangedSpread;
    }

#if UNITY_EDITOR
    // Preview the SO name in the hierarchy for quick identification
    private void OnValidate()
    {
        if (Stats != null) gameObject.name = $"Enemy_{Stats.EnemyName}";
    }
#endif
}