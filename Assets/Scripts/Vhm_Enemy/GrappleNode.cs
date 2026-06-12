// ============================================================
//  GrappleNode.cs  —  Out of Bullet
//  GDD §7.2.2 — Static environmental grapple points.
//  Placed on hanging machinery, girders, suspended containers.
//  Must be visually distinct (neon highlights / active lighting).
//  Nodes are for gaining elevation — not lateral movement.
// ============================================================
using UnityEngine;

namespace OutOfBullet.Enemy  // Shared namespace with grapple consumer
{
    public class GrappleNode : MonoBehaviour
    {
        [Header("Node Config")]
        [Tooltip("If false, node is temporarily disabled (e.g. destroyed environment).")]
        public bool IsActive = true;

        [Tooltip("Visual indicator — assign a neon light/emissive mesh (GDD §7.2.2).")]
        public GameObject ActiveIndicator;

        [Tooltip("Minimum height above arena floor — nodes must be above mid-height (GDD §7.2.2).")]
        public float MinHeightAboveFloor = 3f;

        private void OnEnable()  => SetActiveVisual(IsActive);
        private void OnDisable() => SetActiveVisual(false);

        public void SetActive(bool active)
        {
            IsActive = active;
            SetActiveVisual(active);
        }

        private void SetActiveVisual(bool on)
        {
            if (ActiveIndicator != null)
                ActiveIndicator.SetActive(on);
        }

#if UNITY_EDITOR
        private void OnDrawGizmosSelected()
        {
            Gizmos.color = IsActive ? Color.cyan : Color.grey;
            Gizmos.DrawSphere(transform.position, 0.3f);
            Gizmos.DrawRay(transform.position, Vector3.down * MinHeightAboveFloor);
        }
#endif
    }
}
