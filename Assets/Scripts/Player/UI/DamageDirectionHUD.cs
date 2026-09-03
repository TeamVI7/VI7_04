using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The classic FPS damage arc: a wedge appears around the crosshair at the compass
/// bearing of whatever just hit you, then fades out.
///
/// Driven by PlayerHealth.OnDamagedFrom, which only fires for damage that has a real
/// world source. Killzones, the wire-puzzle shock, bleed damage-over-time and debug
/// damage go through the sourceless TakeDamage overload and deliberately raise nothing —
/// an arc pointing at a hazard you cannot turn to face is worse than no arc.
///
/// SETUP:
///   1. Put this on === GameController === / UIController, beside CrosshairController.
///   2. Under === PlayerUI ===, make a RectTransform centred on the crosshair and sized
///      to the arc ring (e.g. 400x400). Assign it to arcContainer.
///   3. Make the arc prefab: a single Image, stretched to fill its parent, whose sprite
///      draws the wedge at the TOP of the square with empty space below. Rotating the
///      RectTransform then sweeps the wedge around the crosshair. Pivot must be 0.5,0.5 —
///      an off-centre pivot makes the arc orbit rather than rotate in place.
///
/// EXTENDING:
///   - Scale arc alpha or width by damage amount by adding a float to OnDamagedFrom.
///   - For a hit-direction knockback shake, read the same bearing from a camera shake
///     driver rather than recomputing it.
/// </summary>
public class DamageDirectionHUD : MonoBehaviour
{
    // ─────────────────────────────────────────────────────────────────────────
    #region Inspector
    // ─────────────────────────────────────────────────────────────────────────

    [Header("Core References")]
    [Tooltip("Image prefab of the arc wedge, pointing UP, pivot centred, stretched to fill.")]
    public Image arcPrefab;
    [Tooltip("RectTransform centred on the crosshair that arcs are instantiated inside.")]
    public RectTransform arcContainer;

    [Header("Appearance")]
    public Color arcColor = new Color(1f, 0.15f, 0.1f, 0.85f);
    [Tooltip("Seconds an arc takes to fade from full to invisible.")]
    public float fadeDuration = 1.5f;
    [Tooltip("Shapes the fade. Left edge is the moment of the hit, right edge is the end.")]
    public AnimationCurve fadeCurve = AnimationCurve.EaseInOut(0f, 1f, 1f, 0f);
    [Tooltip("Degrees added to every arc's rotation, to line the art up with the bearing. 0 suits " +
             "a wedge drawn centred at the top of the sprite. If you fake the wedge with a Filled " +
             "Image (Radial 360, origin Top), the fill sweeps clockwise from the top instead of " +
             "straddling it — set this to minus half the fill angle, e.g. -15 for a 30° wedge.")]
    public float bearingOffsetDegrees = 0f;

    [Header("Stacking")]
    [Tooltip("How many arcs can be on screen at once. Beyond this the oldest is recycled.")]
    public int maxArcs = 5;
    [Tooltip("A new hit within this many degrees of a live arc refreshes that arc instead of " +
             "adding another. This is what stops per-tick sources like an enemy Laser from " +
             "stacking dozens of identical overlapping wedges.")]
    public float mergeAngle = 25f;

    [Header("Debug")]
    [SerializeField] private bool debugLog = false;

    #endregion

    // ─────────────────────────────────────────────────────────────────────────
    #region Arc State
    // ─────────────────────────────────────────────────────────────────────────

    private class Arc
    {
        public Image Image;
        public RectTransform Rect;
        public Vector3 Source;      // world position, re-projected every frame
        public float Remaining;     // seconds of fade left; <= 0 means free
    }

    private readonly List<Arc> _arcs = new List<Arc>();
    private Camera _cam;
    private bool _warnedUnconfigured;

    /// <summary>
    /// False when this component is missing what it needs to draw.
    ///
    /// Guarded rather than assumed for the same reason MissionListUI does it: these are
    /// static events, so a second unwired copy of this component in the scene would
    /// receive every damage event too and throw on Instantiate(null) — taking down
    /// whatever raised the event, which here is PlayerHealth.TakeDamage.
    /// </summary>
    private bool IsConfigured => arcPrefab != null && arcContainer != null;

    #endregion

    // ─────────────────────────────────────────────────────────────────────────
    #region Unity Lifecycle
    // ─────────────────────────────────────────────────────────────────────────

    private void OnEnable()
    {
        PlayerHealth.OnDamagedFrom += HandleDamagedFrom;
        PlayerHealth.OnDied        += ClearAll;
    }

    private void OnDisable()
    {
        PlayerHealth.OnDamagedFrom -= HandleDamagedFrom;
        PlayerHealth.OnDied        -= ClearAll;
        ClearAll();
    }

    private void LateUpdate()
    {
        if (!IsConfigured) return;

        if (IsSuppressed() || !ResolveCamera() || PlayerHealth.Transform == null)
        {
            ClearAll();
            return;
        }

        TickArcs();
    }

    #endregion

    // ─────────────────────────────────────────────────────────────────────────
    #region Gating
    // ─────────────────────────────────────────────────────────────────────────

    private bool IsSuppressed()
    {
        if (MissionPanelController.AnyOpen) return true;
        if (PauseMenuController.IsPaused)   return true;
        if (ComputerInteraction.UIOpen)     return true;
        return false;
    }

    /// <summary>
    /// Re-resolved every frame — the player camera is deactivated or reparented at
    /// runtime by ComputerInteraction, Rappeldown, the mech intro cutscene and
    /// DeathCamera, so a reference cached once in Awake goes stale.
    /// </summary>
    private bool ResolveCamera()
    {
        if (_cam == null || !_cam.isActiveAndEnabled) _cam = Camera.main;
        return _cam != null;
    }

    #endregion

    // ─────────────────────────────────────────────────────────────────────────
    #region Damage Handling
    // ─────────────────────────────────────────────────────────────────────────

    private void HandleDamagedFrom(Vector3 sourcePosition)
    {
        if (!IsConfigured)
        {
            if (!_warnedUnconfigured)
            {
                _warnedUnconfigured = true;
                Debug.LogWarning($"[DamageDirectionHUD] '{name}' has no " +
                                 $"{(arcPrefab == null ? "Arc Prefab" : "Arc Container")} assigned, so it " +
                                 "cannot show damage direction. Ignoring damage events. If this is a " +
                                 "leftover duplicate component, delete it.", this);
            }
            return;
        }

        if (IsSuppressed() || !ResolveCamera() || PlayerHealth.Transform == null) return;

        float incoming = BearingTo(sourcePosition);

        // Merge into a live arc pointing roughly the same way, rather than stacking a
        // second wedge on top of it. Without this, Laser's per-frame damage would spawn
        // an arc every single frame.
        for (int i = 0; i < _arcs.Count; i++)
        {
            Arc a = _arcs[i];
            if (a.Remaining <= 0f) continue;

            float delta = Mathf.Abs(Mathf.DeltaAngle(BearingTo(a.Source), incoming));
            if (delta > mergeAngle) continue;

            a.Source    = sourcePosition;
            a.Remaining = fadeDuration;
            Log($"Merged hit into existing arc {i} ({delta:0.#}° apart).");
            return;
        }

        Arc arc = AcquireArc();
        arc.Source    = sourcePosition;
        arc.Remaining = fadeDuration;
        Log($"New arc at bearing {incoming:0.#}°.");
    }

    /// <summary>
    /// Bearing from where the player is looking to a world position, flattened to the XZ
    /// plane. 0 means dead ahead, +90 means directly to the right.
    /// </summary>
    private float BearingTo(Vector3 worldPosition)
    {
        Vector3 toSource = worldPosition - PlayerHealth.Transform.position;
        toSource.y = 0f;

        Vector3 forward = _cam.transform.forward;
        forward.y = 0f;

        // Looking straight up or down collapses the flattened forward vector; fall back
        // to the player's own facing so the arc still points somewhere sane.
        if (forward.sqrMagnitude < 0.0001f) forward = PlayerHealth.Transform.forward;
        if (toSource.sqrMagnitude < 0.0001f) return 0f;

        return Vector3.SignedAngle(forward, toSource, Vector3.up);
    }

    #endregion

    // ─────────────────────────────────────────────────────────────────────────
    #region Arc Update
    // ─────────────────────────────────────────────────────────────────────────

    private void TickArcs()
    {
        // Unscaled so an arc still finishes fading if something briefly slows time.
        float dt = Time.unscaledDeltaTime;

        for (int i = 0; i < _arcs.Count; i++)
        {
            Arc a = _arcs[i];
            if (a.Remaining <= 0f) continue;

            a.Remaining -= dt;
            if (a.Remaining <= 0f)
            {
                a.Image.gameObject.SetActive(false);
                continue;
            }

            // Re-derive the bearing from the stored world position every frame rather
            // than freezing it at the moment of the hit. A static arc would drift off
            // target the instant the player turns — which is exactly when they are
            // looking at it to decide which way to turn.
            // UI Z runs counter-clockwise, world Y clockwise — hence the negation.
            float bearing = BearingTo(a.Source);
            a.Rect.localEulerAngles = new Vector3(0f, 0f, -bearing + bearingOffsetDegrees);

            float t = 1f - Mathf.Clamp01(a.Remaining / Mathf.Max(fadeDuration, 0.0001f));
            Color c = arcColor;
            c.a = arcColor.a * Mathf.Clamp01(fadeCurve.Evaluate(t));
            a.Image.color = c;
        }
    }

    #endregion

    // ─────────────────────────────────────────────────────────────────────────
    #region Pooling
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns a free arc, growing the pool up to maxArcs. Once the pool is full the
    /// arc closest to finishing is recycled — taking the freshest hit over the stalest
    /// one, which is what a player under fire from several sides needs.
    /// </summary>
    private Arc AcquireArc()
    {
        for (int i = 0; i < _arcs.Count; i++)
            if (_arcs[i].Remaining <= 0f) return Activate(_arcs[i]);

        if (_arcs.Count < maxArcs)
        {
            Image inst = Instantiate(arcPrefab, arcContainer);
            inst.gameObject.name = $"DamageArc_{_arcs.Count}";

            var arc = new Arc
            {
                Image = inst,
                Rect  = (RectTransform)inst.transform,
            };
            _arcs.Add(arc);
            return Activate(arc);
        }

        Arc oldest = _arcs[0];
        for (int i = 1; i < _arcs.Count; i++)
            if (_arcs[i].Remaining < oldest.Remaining) oldest = _arcs[i];

        return Activate(oldest);
    }

    private Arc Activate(Arc arc)
    {
        arc.Image.color = arcColor;
        if (!arc.Image.gameObject.activeSelf) arc.Image.gameObject.SetActive(true);
        return arc;
    }

    private void ClearAll()
    {
        for (int i = 0; i < _arcs.Count; i++)
        {
            _arcs[i].Remaining = 0f;
            if (_arcs[i].Image != null && _arcs[i].Image.gameObject.activeSelf)
                _arcs[i].Image.gameObject.SetActive(false);
        }
    }

    #endregion

    // ─────────────────────────────────────────────────────────────────────────
    #region Debug
    // ─────────────────────────────────────────────────────────────────────────

    [System.Diagnostics.Conditional("UNITY_EDITOR")]
    private void Log(string msg)
    {
        if (debugLog) Debug.Log($"[DamageDirectionHUD] {msg}", this);
    }

    #endregion
}
