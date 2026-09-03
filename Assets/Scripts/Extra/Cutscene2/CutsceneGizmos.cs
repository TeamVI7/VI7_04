// CutsceneGizmos.cs — shared scene-view drawing for the cutscene 2 shots.
//
// Every shot in this sequence is authored blind: waypoints whose rotation
// matters as much as their position, an aircraft placed by an offset the
// inspector shows as three numbers, a missile arc that only exists at runtime.
// Without gizmos the only way to check framing is to hit play and watch 30
// seconds of cutscene to see the last four of them.
//
// So each shot draws its own path, its camera poses, and the distances that
// decide whether the framing reads. All five components live on one GameObject,
// which means selecting the manager fires all five OnDrawGizmosSelected at once
// — same pile-up EnemyGizmos deals with. Handled the same way: one colour per
// shot, kept in the palette below so two can't drift into the same shade, and a
// drawGizmos toggle on each component so a shot can be muted while another is
// being tuned.
//
// All drawing here is [Conditional("UNITY_EDITOR")] — the calls compile out of
// player builds entirely, so components can call them unguarded.
using UnityEngine;

public static class CutsceneGizmos
{
    #region Palette

    // One colour per shot, in sequence order.
    public static readonly Color Shot1 = new Color(0.40f, 0.85f, 1.00f); // extraction
    public static readonly Color Shot2 = new Color(1.00f, 0.80f, 0.25f); // bomber ascent
    public static readonly Color Shot3 = new Color(0.65f, 1.00f, 0.45f); // payload
    public static readonly Color Shot4 = new Color(1.00f, 0.50f, 0.20f); // missile flight
    public static readonly Color Shot5 = new Color(1.00f, 0.30f, 0.30f); // detonation

    /// <summary>Where a shot ends and hands over to the next one.</summary>
    public static readonly Color Handoff = new Color(1.00f, 0.35f, 0.85f);

    #endregion

    #region Cameras

    /// <summary>A camera pose as a stubby frustum plus its aim line. This is the
    /// primitive that matters most here — half the fields in these shots are
    /// "where is the camera and what is it looking at", and a position gizmo
    /// alone shows the first half of that.</summary>
    [System.Diagnostics.Conditional("UNITY_EDITOR")]
    public static void CameraPose(Vector3 position, Quaternion rotation, Color color,
                                  string label = null, float size = 2f)
    {
        Matrix4x4 prev = Gizmos.matrix;
        Gizmos.matrix = Matrix4x4.TRS(position, rotation, Vector3.one);
        Gizmos.color = color;
        Gizmos.DrawFrustum(Vector3.zero, 45f, size, 0f, 1.6f);
        Gizmos.matrix = prev;

        // Aim line runs well past the frustum so the direction is readable when
        // the camera is a speck at scene-view zoom.
        Gizmos.color = color * 0.55f;
        Gizmos.DrawLine(position, position + rotation * Vector3.forward * (size * 6f));

        if (!string.IsNullOrEmpty(label)) Label(position, label, color);
    }

    /// <summary>Camera pose taken from a transform, for the anchor/marker empties
    /// these shots snap the camera to.</summary>
    [System.Diagnostics.Conditional("UNITY_EDITOR")]
    public static void CameraPose(Transform t, Color color, string label = null, float size = 2f)
    {
        if (t == null) return;
        CameraPose(t.position, t.rotation, color, label, size);
    }

    #endregion

    #region Paths

    /// <summary>Polyline through a set of world points, with a diamond at each one.</summary>
    [System.Diagnostics.Conditional("UNITY_EDITOR")]
    public static void Path(Vector3[] points, Color color, float markerSize = 0.35f)
    {
        if (points == null || points.Length == 0) return;

        Gizmos.color = color;
        for (int i = 1; i < points.Length; i++)
            Gizmos.DrawLine(points[i - 1], points[i]);

        if (markerSize > 0f)
            foreach (Vector3 p in points) Marker(p, markerSize, color);
    }

    // Curved paths aren't drawn from a helper here: the missile's trajectory is
    // owned by MissileFlightCutscene.PathPoint, and the shot samples that into
    // Path() above. A parabola primitive living here would be a second
    // definition of the same curve, free to drift from the flown one.

    #endregion

    #region Markers

    /// <summary>A three-axis cross. Reads at any zoom, unlike a wire sphere that
    /// collapses to a dot when the shot spans a kilometre.</summary>
    [System.Diagnostics.Conditional("UNITY_EDITOR")]
    public static void Marker(Vector3 at, float size, Color color, string label = null)
    {
        Gizmos.color = color;
        Gizmos.DrawLine(at + Vector3.left    * size, at + Vector3.right   * size);
        Gizmos.DrawLine(at + Vector3.up      * size, at + Vector3.down    * size);
        Gizmos.DrawLine(at + Vector3.back    * size, at + Vector3.forward * size);

        if (!string.IsNullOrEmpty(label)) Label(at, label, color);
    }

    /// <summary>Arrow from one point to another, with the length in metres on the
    /// label. Used wherever a field is a distance the inspector states abstractly
    /// — lift height, push distance, drop distance.</summary>
    [System.Diagnostics.Conditional("UNITY_EDITOR")]
    public static void Arrow(Vector3 from, Vector3 to, Color color, string label = null)
    {
        Gizmos.color = color;
        Gizmos.DrawLine(from, to);

        Vector3 dir = to - from;
        float len = dir.magnitude;
        if (len < 0.001f) return;
        dir /= len;

        // Head sized off the shaft so it stays visible on a 300 m arrow and does
        // not swallow a 2 m one.
        float head = Mathf.Clamp(len * 0.06f, 0.15f, 12f);
        Vector3 side = Vector3.Cross(dir, Vector3.up);
        if (side.sqrMagnitude < 0.001f) side = Vector3.right;
        side.Normalize();
        Vector3 up = Vector3.Cross(side, dir);

        Gizmos.DrawLine(to, to - dir * head + side * head * 0.4f);
        Gizmos.DrawLine(to, to - dir * head - side * head * 0.4f);
        Gizmos.DrawLine(to, to - dir * head + up   * head * 0.4f);
        Gizmos.DrawLine(to, to - dir * head - up   * head * 0.4f);

        if (!string.IsNullOrEmpty(label))
            Label(Vector3.Lerp(from, to, 0.5f), label, color);
    }

    /// <summary>Sight line between two points labelled with the distance. The
    /// number that decides whether a shot's scale reads is nearly always this
    /// one, and it is the number the inspector never shows.</summary>
    [System.Diagnostics.Conditional("UNITY_EDITOR")]
    public static void SightLine(Vector3 from, Vector3 to, Color color, string what = null)
    {
        Gizmos.color = color * 0.6f;
        Gizmos.DrawLine(from, to);

        float d = Vector3.Distance(from, to);
        string text = string.IsNullOrEmpty(what)
            ? Metres(d)
            : what + "  " + Metres(d);
        Label(Vector3.Lerp(from, to, 0.5f), text, color);
    }

    /// <summary>Flat circle on the XZ plane through the point. For ground zero and
    /// anything else whose footprint matters more than its volume.</summary>
    [System.Diagnostics.Conditional("UNITY_EDITOR")]
    public static void Ring(Vector3 center, float radius, Color color, string label = null)
    {
        if (radius <= 0.01f) return;

        const int segments = 48;
        Gizmos.color = color;

        Vector3 prev = center + Circle(0f) * radius;
        for (int i = 1; i <= segments; i++)
        {
            Vector3 next = center + Circle(360f * i / segments) * radius;
            Gizmos.DrawLine(prev, next);
            prev = next;
        }

        if (!string.IsNullOrEmpty(label)) Label(center + Circle(45f) * radius, label, color);
    }

    #endregion

    #region Text

    /// <summary>Scene-view text. Editor-only — Handles doesn't exist in a player
    /// build, hence the guard as well as the Conditional on the call site.</summary>
    [System.Diagnostics.Conditional("UNITY_EDITOR")]
    public static void Label(Vector3 at, string text, Color color)
    {
#if UNITY_EDITOR
        var style = new GUIStyle(UnityEditor.EditorStyles.miniLabel)
        {
            normal = { textColor = color },
            alignment = TextAnchor.MiddleCenter,
        };
        UnityEditor.Handles.Label(at, text, style);
#endif
    }

    /// <summary>Distances in these shots run from 0.5 m to 3 km, so metres get
    /// dropped to kilometres past 1000 rather than printing six digits.</summary>
    public static string Metres(float d)
    {
        return d >= 1000f
            ? (d / 1000f).ToString("0.00") + " km"
            : d.ToString("0.#") + " m";
    }

    #endregion

    /// <summary>Unit vector on the XZ plane at the given clockwise angle from +Z.</summary>
    private static Vector3 Circle(float degrees)
    {
        float rad = degrees * Mathf.Deg2Rad;
        return new Vector3(Mathf.Sin(rad), 0f, Mathf.Cos(rad));
    }
}
