// ============================================================
//  WeaponThrow.cs  &  ThrownWeapon.cs  —  Out of Bullet
//  GDD §4.2 — Highest-risk implementation item.
//  Physics projectile with CCD + hitscan fallback.
//  Velocity scales with player speed (GDD §4.2.1).
//  Aim assist: ~15-degree magnetism cone toward nearest enemy.
//  Stagger window on hit: 2.5s (Fodder = instant kill).
// ============================================================
using UnityEngine;
using OutOfBullet.Core;
using OutOfBullet.Data;
using OutOfBullet.Enemy;

namespace OutOfBullet.Projectile
{
    // ── WeaponThrow (component on player) ───────────────────────
    public class WeaponThrow : MonoBehaviour
    {
        [Header("Throw Config")]
        [Tooltip("Base throw force in m/s.")]
        public float BaseThrowSpeed = 25f;

        [Tooltip("Player speed bonus multiplier (GDD: faster player = faster throw).")]
        public float SpeedBonusMultiplier = 0.4f;

        [Tooltip("Aim assist cone half-angle degrees (GDD: ~15 degrees).")]
        public float AimAssistAngle = 15f;

        [Tooltip("Enemy layer for aim assist search.")]
        public LayerMask EnemyLayers;

        [Tooltip("Thrown weapon prefab — must have ThrownWeapon component + Rigidbody.")]
        public GameObject ThrownWeaponPrefab;

        [Header("Aim Assist")]
        [Tooltip("Can be disabled in accessibility settings (GDD §4.2.1).")]
        public bool AimAssistEnabled = true;

        // ── Throw ─────────────────────────────────────────────────
        public void Throw(WeaponData weaponData, Vector3 origin, Vector3 aimDir, float playerSpeed)
        {
            if (ThrownWeaponPrefab == null)
            {
                Debug.LogWarning("[WeaponThrow] No ThrownWeaponPrefab assigned!");
                return;
            }

            // Aim assist — deflect toward nearest valid target in cone
            Vector3 launchDir = AimAssistEnabled
                ? ApplyAimAssist(origin, aimDir)
                : aimDir;

            // Velocity scaling (GDD §4.2.1)
            float throwSpeed = BaseThrowSpeed + playerSpeed * SpeedBonusMultiplier;

            // Spawn projectile
            var go = Instantiate(ThrownWeaponPrefab, origin + aimDir * 0.5f, Quaternion.LookRotation(launchDir));
            var proj = go.GetComponent<ThrownWeapon>();

            if (proj == null)
            {
                Debug.LogError("[WeaponThrow] ThrownWeaponPrefab is missing ThrownWeapon component!");
                Destroy(go);
                return;
            }

            proj.Launch(launchDir * throwSpeed, weaponData);

            EventBus.Publish(new WeaponThrownEvent
            {
                Origin      = origin,
                Direction   = launchDir,
                LaunchSpeed = throwSpeed
            });

            GameManager.Instance?.DebugLog(
                $"[Throw] Launched {weaponData.WeaponName} — speed: {throwSpeed:F1}  dir: {launchDir:F2}");
        }

        private Vector3 ApplyAimAssist(Vector3 origin, Vector3 aimDir)
        {
            Collider[] nearby = Physics.OverlapSphere(origin, 30f, EnemyLayers);
            float      bestAngle = AimAssistAngle;
            Vector3    bestDir   = aimDir;

            foreach (var col in nearby)
            {
                var enemy = col.GetComponentInParent<EnemyBase>();
                if (enemy == null || !enemy.IsAlive) continue;

                Vector3 toEnemy = (col.transform.position - origin).normalized;
                float   angle   = Vector3.Angle(aimDir, toEnemy);

                if (angle < bestAngle)
                {
                    bestAngle = angle;
                    bestDir   = toEnemy;
                }
            }

            return bestDir;
        }
    }

    // ── ThrownWeapon (component on projectile prefab) ────────────
    [RequireComponent(typeof(Rigidbody))]
    public class ThrownWeapon : MonoBehaviour
    {
        [Header("CCD Config (GDD §4.2.2 — CRITICAL)")]
        [Tooltip("Stagger window on hit (GDD §4.2.1: 2.5s).")]
        public float StaggerDuration = 2.5f;

        [Tooltip("Max lifetime before despawn.")]
        public float Lifetime = 8f;

        [Tooltip("Enemy layer mask for hitscan fallback.")]
        public LayerMask EnemyLayers;

        private Rigidbody  _rb;
        private WeaponData _data;
        private bool       _hasHit;
        private Vector3    _prevPosition;

        // ── Init ─────────────────────────────────────────────────
        private void Awake()
        {
            _rb = GetComponent<Rigidbody>();

            // GDD §4.2.2: CCD on all thrown weapon Rigidbodies — MANDATORY
            _rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            _rb.interpolation          = RigidbodyInterpolation.Interpolate;
        }

        public void Launch(Vector3 velocity, WeaponData data)
        {
            _data          = data;
            _rb.linearVelocity = velocity;
            _prevPosition  = transform.position;
            Destroy(gameObject, Lifetime);
        }

        // ── Physics Update ───────────────────────────────────────
        private void FixedUpdate()
        {
            if (_hasHit) return;

            // GDD §4.2.2 — Hitscan fallback on same frame as CCD miss
            // Checks the swept segment each FixedUpdate
            RunHitscanFallback();
            _prevPosition = transform.position;
        }

        private void RunHitscanFallback()
        {
            Vector3 dir   = transform.position - _prevPosition;
            float   dist  = dir.magnitude;
            if (dist < 0.001f) return;

            if (Physics.Raycast(_prevPosition, dir.normalized, out RaycastHit hit, dist + 0.05f, EnemyLayers))
            {
                GameManager.Instance?.DebugLog(
                    "[ThrownWeapon] Hitscan fallback triggered — CCD would have tunneled!");
                ProcessHit(hit.collider, hit.point);
            }
        }

        // ── Collision ────────────────────────────────────────────
        // Unity CCD handles most cases; fallback above catches edge cases.
        private void OnCollisionEnter(Collision col)
        {
            if (_hasHit) return;
            ProcessHit(col.contacts[0].otherCollider, col.contacts[0].point);
        }

        private void ProcessHit(Collider col, Vector3 point)
        {
            if (_hasHit) return;
            _hasHit = true;

            _rb.linearVelocity = Vector3.zero;
            _rb.isKinematic    = true;

            var enemy = col.GetComponentInParent<EnemyBase>();

            if (enemy != null && enemy.IsAlive)
            {
                ApplyStagger(enemy);
            }

            EventBus.Publish(new ThrownWeaponHitEvent
            {
                HitObject = col.gameObject,
                HitPoint  = point
            });

            // Keep mesh visible briefly then despawn
            Destroy(gameObject, 3f);
        }

        private void ApplyStagger(EnemyBase enemy)
        {
            if (enemy.Tier == EnemyTier.Fodder)
            {
                // Fodder: instant kill from throw (GDD §4.2.1)
                enemy.ApplyDamage(enemy.MaxHP + 1f, null);
                GameManager.Instance?.DebugLog($"[ThrownWeapon] Instant kill on Fodder {enemy.name}");
            }
            else
            {
                // Heavy: enter stagger state
                enemy.EnterStagger(StaggerDuration, _data?.ThrowStagger ?? StaggerPotency.Moderate);
                GameManager.Instance?.DebugLog(
                    $"[ThrownWeapon] Stagger triggered on {enemy.name} for {StaggerDuration}s");
            }

            EventBus.Publish(new EnemyStaggeredEvent
            {
                Enemy    = enemy.gameObject,
                Position = enemy.transform.position
            });
        }
    }
}
