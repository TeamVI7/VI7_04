using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Marks a world object as a waypoint destination. WaypointHUD draws every enabled
/// target: a billboard icon while the target is on screen, a screen-edge arrow when
/// it is off screen or behind the camera.
///
/// Targets self-register into a static list, so WaypointHUD never searches the scene.
/// Same pattern the tablet's floor zones use with MinimapFloorRegistry.
///
/// TWO WAYS TO USE IT:
///   1. Leave missionId empty — an always-on marker (extraction point, elevator,
///      ammo cache). Toggle it yourself with SetVisible().
///   2. Fill in missionId — the marker shows only while that mission is on the
///      objective list, and disappears when MissionManager completes it. Because this
///      tracks MissionManager.OnMissionsChanged, checkpoint restore works for free:
///      RestoreActiveMissions raises the same event.
///
/// SETUP:
///   Drop this on the objective's actual world object — the generator, the door, the
///   terminal. MissionCompleteTrigger is usually already on exactly that object, so
///   putting this beside it and copying the missionId is the normal wiring.
///   Use worldOffset to lift the icon off the pivot to somewhere readable (head height
///   on a machine, above the frame on a door).
///
/// EXTENDING:
///   - Add a reachedIcon sprite and swap on arrival if you want a distinct "you're
///     here" state instead of hideWithinRadius simply hiding the marker.
///   - Tint by objective priority (primary vs optional) by reading the MissionData
///     rather than the per-instance tint field.
/// </summary>
public class WaypointTarget : MonoBehaviour
{
    // ─────────────────────────────────────────────────────────────────────────
    #region Static Registry
    // ─────────────────────────────────────────────────────────────────────────

    private static readonly List<WaypointTarget> _active = new List<WaypointTarget>();

    /// <summary>Every enabled target in the scene. Read-only — do not mutate.</summary>
    public static IReadOnlyList<WaypointTarget> Active => _active;

    #endregion

    // ─────────────────────────────────────────────────────────────────────────
    #region Inspector
    // ─────────────────────────────────────────────────────────────────────────

    [Header("Mission Link")]
    [Tooltip("Leave empty for an always-on marker. Set it to a MissionData's missionId and the " +
             "marker appears only while that mission is active, and vanishes on completion.")]
    public string missionId;

    [Header("Appearance")]
    [Tooltip("Icon drawn on the billboard and the edge arrow. Falls back to WaypointHUD's default.")]
    public Sprite icon;
    public Color tint = Color.white;
    [Tooltip("Short text under the icon, e.g. GENERATOR. Leave empty to hide the label.")]
    public string label;

    [Header("Placement")]
    [Tooltip("Offset from this object's pivot to the icon anchor. Lift it to head height or above " +
             "a door frame so the marker does not sit inside the geometry.")]
    public Vector3 worldOffset = new Vector3(0f, 2f, 0f);

    [Header("Distance")]
    [Tooltip("Show metres-to-target under the icon.")]
    public bool showDistance = true;
    [Tooltip("Hide the marker beyond this distance. 0 = always visible however far away.")]
    public float maxVisibleDistance = 0f;
    [Tooltip("Hide the marker once the player is this close — stops it sitting on top of them on " +
             "arrival. 0 = never hide.")]
    public float hideWithinRadius = 0f;

    [Header("Ordering")]
    [Tooltip("Higher draws on top when markers overlap.")]
    public int sortPriority = 0;

    [Header("Visibility")]
    [Tooltip("Starting state for markers with no missionId. Ignored when missionId is set — the " +
             "mission decides.")]
    public bool visibleByDefault = true;

    [Header("Debug")]
    [SerializeField] private bool debugLog = false;

    #endregion

    // ─────────────────────────────────────────────────────────────────────────
    #region State
    // ─────────────────────────────────────────────────────────────────────────

    private bool _manualVisible;
    private bool _missionActive;

    private bool IsMissionDriven => !string.IsNullOrEmpty(missionId);

    /// <summary>Where the icon sits in world space.</summary>
    public Vector3 AnchorPosition => transform.position + worldOffset;

    /// <summary>
    /// Whether WaypointHUD should draw this at all, before any distance or on-screen
    /// tests. Mission-driven targets follow the objective list; the rest follow SetVisible.
    /// </summary>
    public bool ShouldDraw => IsMissionDriven ? _missionActive : _manualVisible;

    #endregion

    // ─────────────────────────────────────────────────────────────────────────
    #region Unity Lifecycle
    // ─────────────────────────────────────────────────────────────────────────

    private void Awake() => _manualVisible = visibleByDefault;

    private void OnEnable()
    {
        if (!_active.Contains(this)) _active.Add(this);

        if (IsMissionDriven) MissionManager.OnMissionsChanged += HandleMissionsChanged;
    }

    /// <summary>
    /// Pulls the initial mission state once every Awake has run.
    ///
    /// OnEnable alone is not enough: script execution order is not guaranteed, so a
    /// target that wakes before MissionManager.Awake would subscribe to an event that
    /// already fired and sit stale until the player next picked up or completed
    /// something. Start runs after every Awake, so Instance is resolvable by then.
    /// </summary>
    private void Start()
    {
        if (IsMissionDriven) RefreshMissionState();
    }

    private void OnDisable()
    {
        _active.Remove(this);

        if (IsMissionDriven) MissionManager.OnMissionsChanged -= HandleMissionsChanged;
    }

    #endregion

    // ─────────────────────────────────────────────────────────────────────────
    #region Mission Tracking
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Caches whether the linked mission is active.
    ///
    /// Cached rather than queried per frame on purpose: MissionManager.IsActive is
    /// activeMissions.Any(m => m.missionId == missionId), and that lambda captures
    /// missionId — so every call allocates a closure and a delegate. Once per mission
    /// change instead of once per target per frame keeps the HUD hot path allocation-free.
    /// </summary>
    private void HandleMissionsChanged(List<MissionData> missions)
    {
        bool wasActive = _missionActive;
        _missionActive = false;

        if (missions != null)
        {
            foreach (MissionData m in missions)
            {
                if (m == null || m.missionId != missionId) continue;
                _missionActive = true;
                break;
            }
        }

        if (_missionActive != wasActive)
            Log(_missionActive ? "Mission active — showing." : "Mission cleared — hiding.");
    }

    private void RefreshMissionState()
    {
        MissionManager mgr = MissionManager.Instance;
        if (mgr == null)
        {
            Log("No MissionManager in the scene — mission-driven marker stays hidden.");
            _missionActive = false;
            return;
        }

        _missionActive = mgr.IsActive(missionId);
    }

    #endregion

    // ─────────────────────────────────────────────────────────────────────────
    #region Public API
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Shows or hides a manually-driven marker. No effect on mission-driven ones —
    /// those follow the objective list, and letting a script fight the mission state
    /// would leave markers stuck on for completed objectives.
    /// </summary>
    public void SetVisible(bool value)
    {
        if (IsMissionDriven)
        {
            Debug.LogWarning($"[WaypointTarget] '{name}' is driven by mission '{missionId}' — " +
                             "SetVisible ignored. Clear missionId to control it manually.", this);
            return;
        }
        _manualVisible = value;
    }

    #endregion

    // ─────────────────────────────────────────────────────────────────────────
    #region Debug
    // ─────────────────────────────────────────────────────────────────────────

    [System.Diagnostics.Conditional("UNITY_EDITOR")]
    private void Log(string msg)
    {
        if (debugLog) Debug.Log($"[WaypointTarget:{name}] {msg}", this);
    }

#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        Gizmos.color = tint;
        Gizmos.DrawWireSphere(AnchorPosition, 0.35f);
        Gizmos.DrawLine(transform.position, AnchorPosition);

        if (hideWithinRadius > 0f)
        {
            Gizmos.color = new Color(tint.r, tint.g, tint.b, 0.25f);
            Gizmos.DrawWireSphere(transform.position, hideWithinRadius);
        }
    }
#endif

    #endregion
}
