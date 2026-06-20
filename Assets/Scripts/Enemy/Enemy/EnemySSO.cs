using UnityEngine;

/// <summary>
/// One asset per enemy type. Drag onto EnemySetup to configure the whole enemy.
/// Create via: Right-click > Create > Enemy > Stats
/// </summary>
[CreateAssetMenu(menuName = "Enemy/Stats", fileName = "EnemyStats_New")]
public class EnemyStatsSO : ScriptableObject
{
    [Header("Identity")]
    public string EnemyName = "Enemy";

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

    [Header("Detection")]
    public float AggroRadius         = 15f;
    public float RadarRange          = 40f;

    [Header("Movement")]
    public float PatrolSpeed         = 2f;
    public float ChaseSpeed          = 10f;
    public float PreferredRange      = 10f;
    public float PatrolRadius        = 12f;
    public float WaypointWaitTime    = 2.5f;

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

    [Header("New Weapons — SMG Setup")]
    public float SMGAttackRange      = 20f;
    public float SMGDamagePerShot    = 2f;
    public float SMGFireRate         = 0.1f;

    [Header("New Weapons — Sniper Setup")]
    public float SniperAttackRange   = 40f;
    public float SniperFireCooldown  = 3.5f;
}