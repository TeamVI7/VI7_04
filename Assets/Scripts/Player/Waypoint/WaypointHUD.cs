using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Draws every enabled WaypointTarget: a world-space billboard while the target is on
/// screen, a screen-edge arrow when it leaves the view or goes behind the camera.
///
/// SETUP:
///   1. Put this on === GameController === / UIController, beside WeaponHUD and
///      CrosshairController — not under === PlayerUI ===. MissionPanelController
///      deactivates that canvas wholesale while the tablet is open, and a driver
///      parented under it would stop running instead of cleanly hiding its markers.
///   2. Make a stretched empty RectTransform under === PlayerUI === for the arrows and
///      assign it to arrowContainer.
///   3. Make an empty scene GameObject for pooled billboards and assign billboardRoot.
///   4. Assign the two prefabs (see WaypointBillboardView / WaypointArrowView).
///
/// EXTENDING:
///   - For a compass strip instead of edge arrows, reuse the bearing from
///     DamageDirectionHUD-style SignedAngle math and drive an anchored X offset.
///   - To show off-floor objectives differently, compare the target's Y against the
///     player's and swap in an up/down variant of the arrow sprite.
/// </summary>
public class WaypointHUD : MonoBehaviour
{
    // ─────────────────────────────────────────────────────────────────────────
    #region Inspector
    // ─────────────────────────────────────────────────────────────────────────

    [Header("Billboards (world space)")]
    [Tooltip("Prefab shown floating over on-screen objectives.")]
    public WaypointBillboardView billboardPrefab;
    [Tooltip("Plain scene transform that pooled billboards are parented under. Leave empty to " +
             "pool them under this object.")]
    public Transform billboardRoot;
    [Tooltip("Keep billboards roughly the same size on screen regardless of distance. Off = they " +
             "shrink with distance like ordinary world geometry.")]
    public bool constantScreenSize = true;
    [Tooltip("Screen-size scaling factor. Only used when Constant Screen Size is on.")]
    public float screenSizeScale = 0.05f;

    [Header("Edge Arrows (screen space)")]
    [Tooltip("Prefab shown at the screen edge for off-screen objectives.")]
    public WaypointArrowView arrowPrefab;
    [Tooltip("Stretched RectTransform under the PlayerUI canvas that arrows are placed inside.")]
    public RectTransform arrowContainer;
    [Tooltip("Pixels of inset from the container border, so arrows are not clipped in half.")]
    public float edgeMargin = 48f;
    [Tooltip("Degrees added to the arrow's rotation. 0 suits a sprite that points UP at zero " +
             "rotation. Use -90 if yours points right, 90 if it points left, 180 if it points down.")]
    public float arrowRotationOffset = 0f;

    [Header("Defaults")]
    [Tooltip("Icon used for targets that do not specify their own.")]
    public Sprite defaultIcon;
    [Tooltip("Cap on markers drawn at once. Nearest targets win.")]
    public int maxSimultaneous = 16;

    [Header("Occlusion")]
    [Tooltip("Dim billboards whose line of sight to the camera is blocked. Off = markers always " +
             "render at full opacity through walls.")]
    public bool dimWhenOccluded = true;
    [Tooltip("Layers that count as blocking line of sight. Exclude the player and triggers.")]
    public LayerMask obstructionMask = ~0;
    [Tooltip("Alpha applied to an occluded billboard.")]
    [Range(0f, 1f)] public float occludedAlpha = 0.35f;
    [Tooltip("Line-of-sight checks per frame, spread round-robin across visible markers. Keeps " +
             "the cost flat no matter how many waypoints are active.")]
    public int raycastsPerFrame = 4;

    [Header("Debug")]
    [SerializeField] private bool debugLog = false;

    #endregion

    // ─────────────────────────────────────────────────────────────────────────
    #region Private State
    // ─────────────────────────────────────────────────────────────────────────

    private Camera _cam;

    private readonly List<WaypointBillboardView> _billboardPool = new List<WaypointBillboardView>();
    private readonly List<WaypointArrowView>     _arrowPool     = new List<WaypointArrowView>();

    // Reused every frame so the steady state allocates nothing.
    private readonly List<WaypointTarget> _visible = new List<WaypointTarget>();

    // Occlusion results keyed by target, refreshed a few per frame rather than all at once.
    private readonly Dictionary<WaypointTarget, bool> _occluded = new Dictionary<WaypointTarget, bool>();
    private int _raycastCursor;

    // Sort state held in fields with a cached comparison delegate. An inline lambda over
    // a local camera position would capture it into a fresh closure every single frame.
    private Vector3 _sortOrigin;
    private System.Comparison<WaypointTarget> _comparison;

    private int _billboardsUsed;
    private int _arrowsUsed;

    #endregion

    // ─────────────────────────────────────────────────────────────────────────
    #region Unity Lifecycle
    // ─────────────────────────────────────────────────────────────────────────

    private void Awake()
    {
        if (billboardRoot == null) billboardRoot = transform;
        _comparison = CompareTargets;
    }

    /// <summary>
    /// Higher sortPriority draws on top; nearest wins ties, so the closest objective is
    /// the one that survives the maxSimultaneous cut.
    /// </summary>
    private int CompareTargets(WaypointTarget a, WaypointTarget b)
    {
        int byPriority = b.sortPriority.CompareTo(a.sortPriority);
        if (byPriority != 0) return byPriority;

        float da = Vector3.SqrMagnitude(a.AnchorPosition - _sortOrigin);
        float db = Vector3.SqrMagnitude(b.AnchorPosition - _sortOrigin);
        return da.CompareTo(db);
    }

    /// <summary>
    /// LateUpdate so the player and camera have finished moving this frame. Running in
    /// Update makes every marker lag the world by one frame, which is very visible on a
    /// billboard while the player mouse-turns.
    /// </summary>
    private void LateUpdate()
    {
        if (IsSuppressed() || !ResolveCamera())
        {
            HideAll();
            return;
        }

        CollectVisible();
        UpdateOcclusion();
        DrawMarkers();
        ParkUnused();
    }

    #endregion

    // ─────────────────────────────────────────────────────────────────────────
    #region Gating
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Markers must not sit on top of a menu, the tablet, or a terminal minigame.
    /// The arrowContainer check also covers the tablet case indirectly, since
    /// MissionPanelController deactivates === PlayerUI === while it is open — but the
    /// explicit flags catch it a frame earlier and cover the world-space billboards too.
    /// </summary>
    private bool IsSuppressed()
    {
        if (MissionPanelController.AnyOpen) return true;
        if (PauseMenuController.IsPaused)   return true;
        if (ComputerInteraction.UIOpen)     return true;

        if (arrowContainer != null && !arrowContainer.gameObject.activeInHierarchy) return true;

        return false;
    }

    /// <summary>
    /// Re-resolves the camera every frame instead of caching it in Awake.
    ///
    /// The player camera is deactivated or reparented at runtime by several systems —
    /// ComputerInteraction swaps to a minigame camera, Rappeldown does the same for its
    /// cutscene, and DeathCamera reparents it to null on death. A once-cached reference
    /// goes stale or null in all three cases, and the markers would freeze mid-screen.
    /// </summary>
    private bool ResolveCamera()
    {
        if (_cam == null || !_cam.isActiveAndEnabled) _cam = Camera.main;
        return _cam != null;
    }

    #endregion

    // ─────────────────────────────────────────────────────────────────────────
    #region Collection
    // ─────────────────────────────────────────────────────────────────────────

    private void CollectVisible()
    {
        _visible.Clear();

        Vector3 camPos = _cam.transform.position;
        IReadOnlyList<WaypointTarget> all = WaypointTarget.Active;

        for (int i = 0; i < all.Count; i++)
        {
            WaypointTarget t = all[i];
            if (t == null || !t.ShouldDraw) continue;

            float dist = Vector3.Distance(camPos, t.AnchorPosition);
            if (t.maxVisibleDistance > 0f && dist > t.maxVisibleDistance) continue;
            if (t.hideWithinRadius   > 0f && dist < t.hideWithinRadius)   continue;

            _visible.Add(t);
        }

        _sortOrigin = camPos;
        _visible.Sort(_comparison);

        if (_visible.Count > maxSimultaneous)
            _visible.RemoveRange(maxSimultaneous, _visible.Count - maxSimultaneous);
    }

    #endregion

    // ─────────────────────────────────────────────────────────────────────────
    #region Occlusion
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Refreshes a fixed budget of line-of-sight checks per frame, cycling through the
    /// visible list. Casting every marker every frame would scale badly with waypoint
    /// count for a purely cosmetic dim; a stale result for a few frames is invisible.
    /// </summary>
    private void UpdateOcclusion()
    {
        if (!dimWhenOccluded || _visible.Count == 0) return;

        // Keys are target references, so entries for destroyed targets would accumulate
        // forever in a level that spawns and despawns objectives. Cheaper to drop the
        // whole cache occasionally than to track lifetimes — the worst case is a few
        // frames of every marker reading as unoccluded while it refills.
        if (_occluded.Count > 64) _occluded.Clear();

        Vector3 camPos = _cam.transform.position;
        int budget = Mathf.Min(raycastsPerFrame, _visible.Count);

        for (int n = 0; n < budget; n++)
        {
            if (_raycastCursor >= _visible.Count) _raycastCursor = 0;

            WaypointTarget t = _visible[_raycastCursor];
            _raycastCursor++;

            Vector3 anchor = t.AnchorPosition;
            bool blocked = Physics.Linecast(camPos, anchor, obstructionMask, QueryTriggerInteraction.Ignore);
            _occluded[t] = blocked;
        }
    }

    private bool IsOccluded(WaypointTarget t)
    {
        if (!dimWhenOccluded) return false;
        return _occluded.TryGetValue(t, out bool blocked) && blocked;
    }

    #endregion

    // ─────────────────────────────────────────────────────────────────────────
    #region Drawing
    // ─────────────────────────────────────────────────────────────────────────

    private void DrawMarkers()
    {
        _billboardsUsed = 0;
        _arrowsUsed     = 0;

        Vector3 camPos = _cam.transform.position;
        Vector2 screenCentre = new Vector2(Screen.width, Screen.height) * 0.5f;

        for (int i = 0; i < _visible.Count; i++)
        {
            WaypointTarget t = _visible[i];
            Vector3 anchor = t.AnchorPosition;

            Vector3 sp = _cam.WorldToScreenPoint(anchor);
            bool behind = sp.z <= 0f;
            bool onScreen = !behind
                            && sp.x >= 0f && sp.x <= Screen.width
                            && sp.y >= 0f && sp.y <= Screen.height;

            Sprite sprite = t.icon != null ? t.icon : defaultIcon;
            string distanceText = t.showDistance
                ? Mathf.RoundToInt(Vector3.Distance(camPos, anchor)) + "m"
                : null;

            if (onScreen) DrawBillboard(t, anchor, sprite, distanceText);
            else          DrawArrow(sp, behind, screenCentre, sprite, t.tint, distanceText);
        }
    }

    private void DrawBillboard(WaypointTarget t, Vector3 anchor, Sprite sprite, string distanceText)
    {
        WaypointBillboardView view = GetBillboard(_billboardsUsed);
        _billboardsUsed++;

        // Activate before touching the transform. BaseScale is captured in the view's
        // Awake, and Awake does not run until the object is first enabled — writing a
        // scaled-down localScale first would let Awake capture that scaled value as the
        // baseline and shrink the marker to nothing permanently.
        if (!view.gameObject.activeSelf) view.gameObject.SetActive(true);

        view.transform.position = anchor;

        // Match the camera's forward rather than looking at the camera position: this
        // keeps every billboard on screen mutually parallel, so they don't visibly fan
        // outwards toward the edges of a wide FOV.
        view.transform.forward = _cam.transform.forward;

        if (constantScreenSize)
        {
            float dist = Vector3.Distance(_cam.transform.position, anchor);
            view.transform.localScale = view.BaseScale * Mathf.Max(dist * screenSizeScale, 0.0001f);
        }
        else
        {
            view.transform.localScale = view.BaseScale;
        }

        view.Apply(sprite, t.tint, distanceText, t.label);
        view.SetAlpha(IsOccluded(t) ? occludedAlpha : t.tint.a);
    }

    private void DrawArrow(Vector3 screenPos, bool behind, Vector2 screenCentre,
                           Sprite sprite, Color tint, string distanceText)
    {
        if (arrowContainer == null || arrowPrefab == null) return;

        // WorldToScreenPoint mirrors the result through the screen centre for anything
        // behind the camera. Without this negation an objective directly behind the
        // player produces an arrow pointing the exact wrong way — the single most
        // common bug in off-screen indicator code.
        Vector2 centred = (Vector2)screenPos - screenCentre;
        if (behind) centred = -centred;

        Vector2 corrected = centred + screenCentre;

        // Overlay canvas, so the camera argument is null. Going through the container's
        // local space means canvas scale factor and reference resolution are handled for
        // us — no manual scaleFactor division that would silently break if the canvas
        // scaler mode changed.
        RectTransformUtility.ScreenPointToLocalPointInRectangle(
            arrowContainer, corrected, null, out Vector2 local);

        Rect r = arrowContainer.rect;
        float halfW = Mathf.Max(r.width  * 0.5f - edgeMargin, 1f);
        float halfH = Mathf.Max(r.height * 0.5f - edgeMargin, 1f);

        // Scale the vector until it lands exactly on the inset border. Anything behind
        // the camera is always pushed out, even if the mirrored point happened to land
        // inside the rect.
        float m = Mathf.Max(Mathf.Abs(local.x) / halfW, Mathf.Abs(local.y) / halfH);
        if (behind || m > 1f) local /= Mathf.Max(m, 0.0001f);

        WaypointArrowView view = GetArrow(_arrowsUsed);
        _arrowsUsed++;

        view.Rect.anchoredPosition = local;

        // Atan2 gives 0 = +X (right). The arrow sprite points UP at zero rotation, so
        // subtract 90 to bring the two conventions into line.
        view.SetBearing(Mathf.Atan2(local.y, local.x) * Mathf.Rad2Deg - 90f + arrowRotationOffset);
        view.Apply(sprite, tint, distanceText);

        if (!view.gameObject.activeSelf) view.gameObject.SetActive(true);
    }

    #endregion

    // ─────────────────────────────────────────────────────────────────────────
    #region Pooling
    // ─────────────────────────────────────────────────────────────────────────

    private WaypointBillboardView GetBillboard(int index)
    {
        while (_billboardPool.Count <= index)
        {
            WaypointBillboardView inst = Instantiate(billboardPrefab, billboardRoot);
            inst.gameObject.name = $"WaypointBillboard_{_billboardPool.Count}";
            inst.gameObject.SetActive(false);
            _billboardPool.Add(inst);
        }
        return _billboardPool[index];
    }

    private WaypointArrowView GetArrow(int index)
    {
        while (_arrowPool.Count <= index)
        {
            WaypointArrowView inst = Instantiate(arrowPrefab, arrowContainer);
            inst.gameObject.name = $"WaypointArrow_{_arrowPool.Count}";
            inst.gameObject.SetActive(false);
            _arrowPool.Add(inst);
        }
        return _arrowPool[index];
    }

    /// <summary>Parks the unused tail of both pools. Never destroys — markers churn every frame.</summary>
    private void ParkUnused()
    {
        for (int i = _billboardsUsed; i < _billboardPool.Count; i++)
            if (_billboardPool[i] != null && _billboardPool[i].gameObject.activeSelf)
                _billboardPool[i].gameObject.SetActive(false);

        for (int i = _arrowsUsed; i < _arrowPool.Count; i++)
            if (_arrowPool[i] != null && _arrowPool[i].gameObject.activeSelf)
                _arrowPool[i].gameObject.SetActive(false);
    }

    private void HideAll()
    {
        _billboardsUsed = 0;
        _arrowsUsed     = 0;
        ParkUnused();
    }

    #endregion

    // ─────────────────────────────────────────────────────────────────────────
    #region Debug
    // ─────────────────────────────────────────────────────────────────────────

    [System.Diagnostics.Conditional("UNITY_EDITOR")]
    private void Log(string msg)
    {
        if (debugLog) Debug.Log($"[WaypointHUD] {msg}", this);
    }

    #endregion
}
