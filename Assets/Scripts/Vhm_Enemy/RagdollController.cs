// ============================================================
//  RagdollController.cs  —  Out of Bullet
//  GDD §7.4 — ALL death states are physics-resolved.
//  No static death animations. Ragdoll trajectory determined
//  by player's velocity at moment of execution.
//  Fast arrival = violent ragdoll impact.
// ============================================================
using UnityEngine;
using UnityEngine.AI;

namespace OutOfBullet.Enemy
{
    public class RagdollController : MonoBehaviour
    {
        // ── Inspector ────────────────────────────────────────────
        [Header("Ragdoll Config")]
        [Tooltip("Scale applied to player velocity when seeding ragdoll (higher = more dramatic).")]
        public float VelocitySeedScale = 1.4f;

        [Tooltip("Upward force added to root bone on death (for visual lift).")]
        public float UpwardKick = 2f;

        [Tooltip("If true, destroys the GO after LifetimeAfterDeath.")]
        public bool AutoDespawn = true;
        public float LifetimeAfterDeath = 8f;

        // ── Runtime ──────────────────────────────────────────────
        public bool IsRagdolling { get; private set; }

        private Rigidbody[]  _ragdollBodies;
        private Collider[]   _ragdollColliders;
        private Animator     _animator;
        private NavMeshAgent _nav;
        private Collider     _mainCollider;
        private Rigidbody    _mainRb;

        // ── Unity ────────────────────────────────────────────────
        private void Awake()
        {
            // Collect all child Rigidbodies — these form the ragdoll joints
            _ragdollBodies    = GetComponentsInChildren<Rigidbody>();
            _ragdollColliders = GetComponentsInChildren<Collider>();
            _animator         = GetComponent<Animator>();
            _nav              = GetComponent<NavMeshAgent>();
            _mainCollider     = GetComponent<Collider>();
            _mainRb           = GetComponent<Rigidbody>();

            // Ragdoll starts disabled — enemy is alive and animated
            SetRagdollState(false);
        }

        // ── Activate ─────────────────────────────────────────────
        /// <summary>
        /// Called by EnemyBase.Die(). Seeds velocity from player.
        /// GDD §7.4: fast arrival = violent ragdoll impact.
        /// </summary>
        public void ActivateRagdoll(Vector3 playerVelocity)
        {
            if (IsRagdolling) return;
            IsRagdolling = true;

            SetRagdollState(true);

            // Disable main mover components
            if (_nav  != null) _nav.enabled  = false;
            if (_mainCollider != null) _mainCollider.enabled = false;
            if (_mainRb       != null) _mainRb.isKinematic   = true;

            // Disable animator — no canned animations ever (GDD §7.4)
            if (_animator != null) _animator.enabled = false;

            // Seed velocity into root bone
            Rigidbody rootBone = GetRootBone();
            if (rootBone != null)
            {
                Vector3 seededVelocity = playerVelocity * VelocitySeedScale
                                       + Vector3.up * UpwardKick;
                rootBone.linearVelocity = seededVelocity;

                // Apply to all bones for full-body reaction
                foreach (var rb in _ragdollBodies)
                {
                    if (rb == rootBone) continue;
                    // Slightly randomized per-bone to avoid rigid slab look
                    rb.linearVelocity = seededVelocity * Random.Range(0.6f, 1.0f)
                                + Random.insideUnitSphere * 2f;
                }
            }

            if (AutoDespawn)
                Destroy(gameObject, LifetimeAfterDeath);
        }

        // ── Helpers ──────────────────────────────────────────────
        private void SetRagdollState(bool ragdollOn)
        {
            foreach (var rb in _ragdollBodies)
            {
                rb.isKinematic = !ragdollOn;
            }

            foreach (var col in _ragdollColliders)
            {
                // Don't disable the main capsule — let it handle ground contact
                if (col == _mainCollider) continue;
                col.enabled = ragdollOn;
            }
        }

        private Rigidbody GetRootBone()
        {
            // Prefer 'Hips' or 'Root' named bone; fall back to first body
            foreach (var rb in _ragdollBodies)
            {
                string boneName = rb.name.ToLower();
                if (boneName.Contains("hip") || boneName.Contains("root") || boneName.Contains("pelvis"))
                    return rb;
            }
            return _ragdollBodies.Length > 0 ? _ragdollBodies[0] : null;
        }

#if UNITY_EDITOR
        private void OnDrawGizmosSelected()
        {
            if (!IsRagdolling) return;
            Gizmos.color = Color.magenta;
            foreach (var rb in _ragdollBodies)
            {
                if (rb != null)
                    Gizmos.DrawWireSphere(rb.position, 0.1f);
            }
        }
#endif
    }
}
