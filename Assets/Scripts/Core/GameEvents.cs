// ============================================================
//  GameEvents.cs  —  Out of Bullet
//  All EventBus payload structs live here.
//  Add new events here. Never scatter them across systems.
// ============================================================
using UnityEngine;

namespace OutOfBullet.Core
{
    // ── Player ───────────────────────────────────────────────────

    public struct PlayerDiedEvent { }

    public struct PlayerHealthChangedEvent
    {
        public float CurrentHP;
        public float MaxHP;
        public float Delta;         // negative = damage, positive = heal
    }

    public struct PlayerDashUsedEvent
    {
        public int ChargesRemaining;
        public int MaxCharges;
    }

    public struct PlayerDashChargeRestoredEvent
    {
        public int ChargesRemaining;
        public int MaxCharges;
    }

    public struct PlayerVelocityChangedEvent
    {
        public Vector3 Velocity;
        public float Speed;
    }

    // ── Weapon / Combat ──────────────────────────────────────────

    public struct WeaponFiredEvent
    {
        public int RemainingAmmo;
        public string WeaponName;
    }

    public struct WeaponEmptyEvent
    {
        public string WeaponName;       // triggers throw prompt audio
    }

    public struct WeaponAcquiredEvent
    {
        public string WeaponName;
        public int Ammo;
    }

    public struct WeaponThrownEvent
    {
        public Vector3 Origin;
        public Vector3 Direction;
        public float LaunchSpeed;
    }

    public struct ThrownWeaponHitEvent
    {
        public GameObject HitObject;
        public Vector3 HitPoint;
    }

    // ── Grapple ──────────────────────────────────────────────────

    public struct GrappleFiredEvent
    {
        public Vector3 Target;
    }

    public struct GrappleLandedEvent
    {
        public Transform AnchorTransform;
        public bool IsEnemy;            // enemy vs environmental node
    }

    public struct GrappleMissedEvent { }

    public struct GrappleCooldownStartedEvent
    {
        public float Duration;
    }

    public struct GrappleCooldownEndedEvent { }

    // ── Enemy ────────────────────────────────────────────────────

    public struct EnemyStaggeredEvent
    {
        public GameObject Enemy;
        public Vector3 Position;
    }

    public struct EnemyExecutedEvent
    {
        public GameObject Enemy;
        public float HealthSiphonAmount;
        public string WeaponDropped;
    }

    public struct EnemyKilledEvent
    {
        public GameObject Enemy;
        public Vector3 DeathPosition;
        public Vector3 PlayerVelocityAtKill;    // seeds ragdoll
    }

    public struct EnemyStaggerExpiredEvent
    {
        public GameObject Enemy;
    }

    // ── Katana ───────────────────────────────────────────────────

    public struct KatanaSwingEvent
    {
        public bool HitEnemy;
    }

    public struct KatanaExecuteEvent
    {
        public GameObject TargetEnemy;
    }

    // ── Arena / Level ────────────────────────────────────────────

    public struct WaveStartedEvent
    {
        public int WaveIndex;
        public string WaveType;         // e.g. "ChainOpener", "EscalationWave"
    }

    public struct WaveClearedEvent
    {
        public int WaveIndex;
        public float ClearTimeSeconds;
    }

    public struct ArenaResetEvent { }
}
