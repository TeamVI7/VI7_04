// ============================================================
//  KatanaController.cs  —  Out of Bullet
//  GDD §3.1 — Permanent primary weapon. Always in view model.
//  Melee: ~2m range, instant kill on Fodder, fixed dmg on Heavy.
//  Recovery window: ~0.2s to prevent spam.
//  Forward momentum preserved on swing (does not stop player).
// ============================================================
using System.Collections;
using UnityEngine;
using OutOfBullet.Core;
using OutOfBullet.Enemy;

namespace OutOfBullet.Player
{
    public class KatanaController : MonoBehaviour
    {
        // ── Inspector ────────────────────────────────────────────
        [Header("Melee")]
        [Tooltip("GDD §3.1.1: effective range ~2m.")]
        public float MeleeRange = 2f;

        [Tooltip("Melee hit cone half-angle (degrees).")]
        public float MeleeAngle = 45f;

        [Tooltip("Recovery window between swings (GDD: ~0.2s).")]
        public float RecoveryTime = 0.2f;

        [Tooltip("Fixed damage contribution to Heavies per swing.")]
        public float HeavyDamagePerSwing = 15f;

        [Header("Layers")]
        public LayerMask EnemyLayers;

        [Header("Input")]
        public KeyCode MeleeKey = KeyCode.Mouse0;  // Left-click (when no weapon / force melee)

        [Header("Feedback")]
        [Tooltip("Camera punch magnitude on hit — pairs with CameraShake system.")]
        public float SwingCameraPunch = 0.05f;

        // ── Runtime ──────────────────────────────────────────────
        public bool IsInRecovery { get; private set; }

        private PlayerController _pc;
        private Camera           _cam;

        // ── Unity ────────────────────────────────────────────────
        private void Awake()
        {
            _pc  = GetComponent<PlayerController>();
            _cam = Camera.main;
        }

        private void Update()
        {
            HandleInput();
        }

        // ── Input ────────────────────────────────────────────────
        private void HandleInput()
        {
            if (Input.GetKeyDown(MeleeKey) && !IsInRecovery)
                Swing();
        }

        // ── Swing ────────────────────────────────────────────────
        public void Swing()
        {
            if (IsInRecovery) return;

            bool hitAny = false;

            // Sphere overlap in front of camera
            Collider[] hits = Physics.OverlapSphere(
                _cam.transform.position + _cam.transform.forward * (MeleeRange * 0.5f),
                MeleeRange * 0.5f,
                EnemyLayers
            );

            foreach (var col in hits)
            {
                Vector3 dirToEnemy = (col.transform.position - _cam.transform.position).normalized;
                float   angle      = Vector3.Angle(_cam.transform.forward, dirToEnemy);
                if (angle > MeleeAngle) continue;

                EnemyBase enemy = col.GetComponentInParent<EnemyBase>();
                if (enemy == null || !enemy.IsAlive) continue;

                HitEnemy(enemy);
                hitAny = true;
            }

            EventBus.Publish(new KatanaSwingEvent { HitEnemy = hitAny });
            StartCoroutine(RecoveryRoutine());
        }

        // ── Hit Logic ────────────────────────────────────────────
        private void HitEnemy(EnemyBase enemy)
        {
            if (enemy.Tier == EnemyTier.Fodder)
            {
                // Instant kill — no stagger needed (GDD §3.1.1)
                enemy.ApplyDamage(enemy.MaxHP + 1f, _pc);
                GameManager.Instance?.DebugLog($"[Katana] Instant kill on Fodder: {enemy.name}");
            }
            else
            {
                // Fixed damage contribution toward stagger threshold (GDD §3.1.1)
                enemy.ApplyDamage(HeavyDamagePerSwing, _pc);
                GameManager.Instance?.DebugLog(
                    $"[Katana] Hit Heavy {enemy.name} for {HeavyDamagePerSwing} dmg");
            }
        }

        // ── Execute (called by GrappleSystem on arrival) ─────────
        /// <summary>
        /// Performs execution on an enemy upon grapple arrival.
        /// GDD §2 Beat 4 — steal weapon + health, vault past enemy.
        /// </summary>
        public void Execute(EnemyBase enemy)
        {
            if (enemy == null || !enemy.IsAlive) return;

            // Vault: redirect player velocity past enemy
            Vector3 throughDir = (_pc.transform.position - enemy.transform.position).normalized;
            _pc.RedirectVelocity(throughDir);

            // Siphon + weapon steal happen inside EnemyBase.Die()
            enemy.ApplyDamage(enemy.MaxHP + 1f, _pc);

            EventBus.Publish(new KatanaExecuteEvent { TargetEnemy = enemy.gameObject });
            GameManager.Instance?.DebugLog($"[Katana] EXECUTE on {enemy.name}");
        }

        // ── Recovery ─────────────────────────────────────────────
        private IEnumerator RecoveryRoutine()
        {
            IsInRecovery = true;
            yield return new WaitForSeconds(RecoveryTime);
            IsInRecovery = false;
        }

#if UNITY_EDITOR
        private void OnDrawGizmosSelected()
        {
            if (_cam == null) _cam = Camera.main;
            if (_cam == null) return;

            Gizmos.color = Color.red;
            Gizmos.DrawWireSphere(
                _cam.transform.position + _cam.transform.forward * (MeleeRange * 0.5f),
                MeleeRange * 0.5f);
        }
#endif
    }
}
