using UnityEngine;

/// <summary>
/// Which idle/hold animation set an enemy uses. Values map directly to the
/// Animator's "WeaponType" Int parameter (0-3) on the L/R Hands layer —
/// see EnemyAnimatorController.
/// </summary>
public enum EnemyWeaponAnimType
{
    SMG      = 0,
    Sniper   = 1,
    Handgun  = 2,
    Punching = 3, // unarmed — MeleeAttackBehaviour only, fists-idle pose
}

/// <summary>
/// One asset per enemy type. Drag onto EnemySetup to configure the whole enemy.
/// Create via: Right-click > Create > Enemy > Stats
/// </summary>
[CreateAssetMenu(menuName = "Enemy/Stats", fileName = "EnemyStats_New")]
public class EnemyStatsSO : ScriptableObject
{
    [Header("Identity")]
    public string EnemyName = "Enemy";
    public EnemyWeaponAnimType WeaponAnimType = EnemyWeaponAnimType.SMG;

    [Header("Health")]
    public float MaxHP               = 100f;
    public float ExecuteHealAmount   = 25f;

    [Header("Stagger")]
    [Range(0.01f, 1f)]
    public float StaggerThreshold    = 0.25f;
    public float StaggerDuration     = 1.5f;
    public float StaggerWindow       = 3f;

    [Header("Death")]
    public float DeathImpulseScale   = 8f;
    public float DeathVFXLifetime    = 3f;
    public float DropUpwardForce     = 4f;
    [Range(0f, 1f)]
    public float DropChance          = 1f;

    [Header("Detection")]
    public float AggroRadius         = 15f;
    public float RadarRange          = 40f;

    [Header("Movement")]
    public float PatrolSpeed         = 2f;
    public float ChaseSpeed          = 10f;
    public float PreferredRange      = 10f;
    public float PatrolRadius        = 12f;
    public float WaypointWaitTime    = 2.5f;

    [Header("Combat Movement Style")]
    [Tooltip("How this enemy behaves once in combat range. Standoff = old behaviour (walk to PreferredRange, stop). Strafe = holds range but sidesteps. Aggressive = never stops closing.")]
    public PatrolBehaviour.CombatMovementStyle MovementStyle = PatrolBehaviour.CombatMovementStyle.Standoff;
    public float StrafeRadius        = 3f;
    public float StrafeInterval      = 1.2f;

    [Header("Armor")]
    public float MaxArmor            = 0f;     // 0 = no shield
    public float ShieldActivateRange = 10f;
    [Range(0f, 1f)]
    public float LowArmorThreshold   = 0.3f;
    public float DissolveDuration    = 1.5f;
    public float ShieldBreakFlashDuration = 0.3f;
    public float ShieldStunDuration  = 1.2f;
    public float ShieldSpeedBoost    = 1.15f;
    public float ShieldFireRateBoost = 1.2f;

    [Header("Grenade Burst")]
    public float AttackRange         = 35f;
    public int   BurstCount          = 4;
    public float BurstInterval       = 0.25f;
    public float BurstCooldown       = 2.5f;
    [Range(10f, 45f)]
    public float ArcAngle            = 25f;

    [Header("Laser")]
    public float LaserRange          = 30f;
    public float LaserDPS            = 15f;

    [Header("Melee")]
    public float MeleeDamage         = 10f;
    public float MeleeCooldown       = 1f;
    public float MeleeRange          = 2f;
    public float MeleeLungeDistance  = 1.5f;
    public float MeleeLungeSpeed     = 14f;

    [Header("Stealth")]
    public float VisibleDuration     = 3f;
    public float CloakedDuration     = 20f;
    public float StealthFadeSpeed    = 2f;
    public float BleedDuration       = 5f;
    public float BleedDamagePerTick  = 10f;
    public float BleedInterval       = 1f;

    [Header("Ragdoll")]
    public float VelocitySeedScale   = 1.4f;
    public float UpwardKick          = 2f;
    public float RagdollLifetime     = 8f;

    [Header("New Weapons — Shotgun Setup")]
    public float ShotgunAttackRange  = 15f;
    public float ShotgunDamagePerPellet = 3f;
    public int   ShotgunPelletsPerShot  = 6;
    public float ShotgunSpreadAngle  = 0.12f;
    public float ShotgunFireRate     = 2f;
    public float ShotgunTelegraphTime = 0.25f;

    [Header("New Weapons — SMG Setup")]
    public float SMGAttackRange      = 20f;
    public float SMGDamagePerShot    = 2f;
    public float SMGFireRate         = 0.1f;
    public float SMGTelegraphTime    = 0.12f;

    [Header("New Weapons — Pistol Setup")]
    public float PistolAttackRange   = 18f;
    public float PistolDamagePerShot = 6f;
    public float PistolFireRate      = 0.5f;
    public float PistolTelegraphTime = 0.15f;

    [Header("New Weapons — Sniper Setup")]
    public float SniperAttackRange   = 40f;
    public float SniperFireCooldown  = 3.5f;
    public float SniperDamage        = 25f;

    [Header("Hit Reaction")]
    public float HitReactionMaxKickDistance = 0.06f;
    public float HitReactionKickDuration    = 0.15f;
    public float HitReactionDamageFractionForMaxKick = 0.35f;
    [Range(0f, 0.2f)]
    public float HitReactionMinDamageFraction = 0.01f;
    public float HitReactionFlashDuration   = 0.08f;
    public float HitReactionHeadshotRadius  = 0.35f;
    public float HitReactionHeadshotFlashDurationMultiplier = 1.75f;
    public float HitReactionStaggerFlashDuration = 0.25f;
    public float HitReactionLimbDismemberFraction = 0.5f;
    public float HitReactionLimbScaleDownDuration  = 0.15f;

    [Header("Impact VFX")]
    public float ImpactVFXLifetime = 2f;
    [Range(0f, 0.2f)]
    public float ImpactVFXMinDamageFraction = 0.01f;

    [Header("Torso Reaction")]
    public float TorsoMaxTwistAngle = 12f;
    public float TorsoTwistDuration = 0.18f;

    [Header("Look At")]
    [Range(0f, 1f)] public float LookHeadWeight = 0.6f;
    [Range(0f, 1f)] public float LookTorsoWeight = 0.25f;
    public float LookMaxYawAngle = 70f;
    public float LookMaxPitchAngle = 35f;
    public float LookTurnSpeed = 8f;
}