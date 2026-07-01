using UnityEngine;

/// <summary>
/// Drop one of these on a trigger volume covering a single floor/level of a
/// multi-storey building. When the player enters, this floor becomes the
/// "active" minimap floor; when they leave, the registry falls back to
/// whichever other zone (if any) the player is still standing in.
///
/// SETUP:
///   1. Create a BoxCollider (or any collider) covering the floor's footprint,
///      tall enough to cover that floor's vertical slice (e.g. floor-to-ceiling).
///   2. Set isTrigger = true.
///   3. Attach this script, set floorHeight to the world Y the minimap camera
///      should sit above for this floor, and floorCullingMask to whichever
///      Layer that floor's geometry/props live on (so the minimap camera only
///      sees this floor, not the ones above/below it bleeding through).
///   4. Either drag this zone into TabletController.minimapFloorZones manually,
///      or leave it — MinimapFloorRegistry auto-registers every zone in the
///      scene on Awake via FindObjectsByType, so manual wiring is optional.
///
/// OVERLAPPING ZONES:
///   If two zones overlap (e.g. an open stairwell visible from both floors),
///   the registry keeps whichever zone was entered most recently — see
///   MinimapFloorRegistry.RegisterEnter for the exact tie-break rule.
///
/// EXTENDING:
///   - Add a floorLabel string here if you want the tablet UI to show "FLOOR 2"
///     text alongside the minimap — read it via MinimapFloorRegistry.ActiveZone.
///   - Add a floorIndex int if floors need strict ordering for a floor-select
///     dropdown rather than purely trigger-driven switching.
/// </summary>
[RequireComponent(typeof(Collider))]
public class MinimapFloorZone : MonoBehaviour
{
    // ─────────────────────────────────────────────────────────────────────────
    #region Inspector
    // ─────────────────────────────────────────────────────────────────────────

    [Header("Identity")]
    [Tooltip("Shown in debug logs / can be read by UI for a floor label.")]
    public string floorName = "Floor";

    [Header("Minimap Camera Settings For This Floor")]
    [Tooltip("World-space Y height the minimap camera sits above the player while this zone is active.")]
    public float floorHeight = 30f;
    [Tooltip("Culling mask so the shared minimap camera only renders this floor's geometry — prevents floors above/below from bleeding through the top-down view.")]
    public LayerMask floorCullingMask;
    [Tooltip("Orthographic size for this floor. Leave at 0 to use TabletController's default minimapZoom instead of overriding per-floor.")]
    public float floorZoomOverride = 0f;

    [Header("Floor Geometry Toggle")]
    [Tooltip("GameObjects to disable when the player enters this zone and re-enable when they leave. Typical use: disable this floor's ceiling/roof mesh so the minimap top-down camera isn't occluded, or hide geometry on floors above/below that would bleed through.")]
    public GameObject[] objectsToDisable;

    [Header("Debug")]
    [SerializeField] private bool debugLog = false;

    #endregion

    // ─────────────────────────────────────────────────────────────────────────
    #region Unity Lifecycle
    // ─────────────────────────────────────────────────────────────────────────

    private void Awake()
    {
        var col = GetComponent<Collider>();
        if (!col.isTrigger)
            LogWarning("Collider is not set to isTrigger — zone will physically block the player instead of detecting them.");
    }

    private void OnTriggerEnter(Collider other)
    {
        if (!other.CompareTag("Player")) return;
        Log("Player entered.");
        MinimapFloorRegistry.Instance?.RegisterEnter(this);
        SetFloorObjects(false);
    }

    private void OnTriggerExit(Collider other)
    {
        if (!other.CompareTag("Player")) return;
        Log("Player exited.");
        MinimapFloorRegistry.Instance?.RegisterExit(this);
        SetFloorObjects(true);
    }

    private void SetFloorObjects(bool active)
    {
        if (objectsToDisable == null) return;
        foreach (var go in objectsToDisable)
            if (go != null) go.SetActive(active);
    }

    #endregion

    // ─────────────────────────────────────────────────────────────────────────
    #region Debug
    // ─────────────────────────────────────────────────────────────────────────

    [System.Diagnostics.Conditional("UNITY_EDITOR")]
    private void Log(string msg)
    {
        if (debugLog) Debug.Log($"[MinimapFloorZone:{floorName}] {msg}", this);
    }

    [System.Diagnostics.Conditional("UNITY_EDITOR")]
    private void LogWarning(string msg) => Debug.LogWarning($"[MinimapFloorZone:{floorName}] ⚠ {msg}", this);

#if UNITY_EDITOR
    private void OnDrawGizmos()
    {
        var col = GetComponent<Collider>();
        if (col == null) return;

        bool isActive = MinimapFloorRegistry.Instance != null && MinimapFloorRegistry.Instance.ActiveZone == this;
        Gizmos.color = isActive ? new Color(0.2f, 1f, 0.4f, 0.35f) : new Color(0.3f, 0.6f, 1f, 0.2f);
        Gizmos.matrix = transform.localToWorldMatrix;

        if (col is BoxCollider box)
            Gizmos.DrawCube(box.center, box.size);
        else
            Gizmos.DrawWireSphere(Vector3.zero, 1f);
    }
#endif

    #endregion
}