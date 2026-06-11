using UnityEngine;

/// <summary>
/// Attach to a weapon. Call Eject() from WeaponController fire logic
/// or wire to an Animation Event via AnimationEventReceiver.
///
/// Ejection point Transform should face the direction casings fly out —
/// +Z = forward ejection, so rotate it to face the ejection port opening.
/// </summary>
public class CasingEjector : MonoBehaviour
{
    // ─────────────────────────────────────────────────────────────────────────
    [Header("References")]
    [Tooltip("Child Transform at the ejection port. Its forward = ejection direction.")]
    [SerializeField] private Transform ejectionPoint;

    [Tooltip("Casing configuration for this weapon.")]
    [SerializeField] private BulletCasingData casingData;

    [Header("Ejection Override (optional)")]
    [Tooltip("Leave at zero to use casingData values.")]
    [SerializeField] private float forceOverride   = 0f;
    [SerializeField] private float upwardOverride  = 0f;

    [Header("Debug")]
    [SerializeField] private bool drawGizmos = true;

    // ── Cached ────────────────────────────────────────────────────────────────
    private BulletCasingPool _pool;

    // ─────────────────────────────────────────────────────────────────────────
    private void Awake()
    {
        ValidateSetup();
    }

    private void Start()
    {
        // Lazy pool fetch — pool may be created after this weapon
        _pool = BulletCasingPool.Instance;
        if (_pool == null)
            Debug.LogWarning("[CasingEjector] BulletCasingPool not found in scene. Casings will not spawn.");
        else
            _pool.Prewarm(casingData);
    }

    // ─────────────────────────────────────────────────────────────────────────
    /// <summary>
    /// Call this on every shot (or pump/bolt cycle for delayed ejection).
    /// Safe to call when pool is missing — fails silently with a warning.
    /// </summary>
    public void Eject()
    {
        if (!CanEject()) return;

        var casing = _pool.Get(casingData);
        if (casing == null) return;

        Vector3 vel   = BuildEjectionVelocity();
        Vector3 torque = BuildTorque();

        casing.Initialise(casingData, ejectionPoint.position, ejectionPoint.rotation, vel, torque);

#if UNITY_EDITOR
        if (drawGizmos)
            Debug.DrawRay(ejectionPoint.position, vel * 0.25f, Color.cyan, 0.5f);
#endif
    }

    // ─────────────────────────────────────────────────────────────────────────
    /// <summary>Swap casing type at runtime (e.g. ammo swap, debug menu).</summary>
    public void SetCasingData(BulletCasingData data)
    {
        casingData = data;
        if (_pool != null) _pool.Prewarm(casingData);
    }

    // ─────────────────────────────────────────────────────────────────────────
    private Vector3 BuildEjectionVelocity()
    {
        float force  = forceOverride  > 0f ? forceOverride  : casingData.ejectionForce;
        float upward = upwardOverride > 0f ? upwardOverride : casingData.ejectionUpwardForce;

        // Spread cone
        Vector3 dir = ejectionPoint.forward;
        if (casingData.ejectionSpreadAngle > 0f)
            dir = Quaternion.AngleAxis(Random.Range(-casingData.ejectionSpreadAngle,
                                                    casingData.ejectionSpreadAngle), ejectionPoint.up)
                  * Quaternion.AngleAxis(Random.Range(-casingData.ejectionSpreadAngle,
                                                    casingData.ejectionSpreadAngle), ejectionPoint.right)
                  * dir;

        return dir * force + Vector3.up * upward;
    }

    private Vector3 BuildTorque()
    {
        float t = Random.Range(casingData.torqueRange.x, casingData.torqueRange.y);
        return new Vector3(
            Random.Range(-t, t),
            Random.Range(-t, t),
            Random.Range(-t, t)
        );
    }

    // ─────────────────────────────────────────────────────────────────────────
    private bool CanEject()
    {
        if (_pool == null)
        {
            Debug.LogWarning("[CasingEjector] No pool — casing skipped.");
            return false;
        }
        if (ejectionPoint == null)
        {
            Debug.LogError("[CasingEjector] Ejection point not assigned.");
            return false;
        }
        if (casingData == null)
        {
            Debug.LogError("[CasingEjector] CasingData not assigned.");
            return false;
        }
        return true;
    }

    private void ValidateSetup()
    {
        if (ejectionPoint == null)
            Debug.LogError($"[CasingEjector] '{name}': ejectionPoint not assigned.", this);
        if (casingData == null)
            Debug.LogError($"[CasingEjector] '{name}': casingData not assigned.", this);
    }

    // ─────────────────────────────────────────────────────────────────────────
#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        if (!drawGizmos || ejectionPoint == null) return;

        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(ejectionPoint.position, 0.02f);
        Gizmos.color = Color.cyan;
        Gizmos.DrawRay(ejectionPoint.position, ejectionPoint.forward * 0.15f);
    }
#endif
}