// EnemyGizmos.cs — shared scene-view drawing for every enemy component.
//
// An enemy carries a stack of siblings on one GameObject — brain, patrol, range
// or melee, dodge, avenge — and selecting it fires all of their
// OnDrawGizmosSelected at once. Drawn as wire spheres that's half a dozen
// concentric balls centred on the same point: technically all the information,
// practically unreadable.
//
// So: rings on the GROUND PLANE instead of spheres. Every range an enemy cares
// about is a flat XZ distance, the rings sit where the player actually stands,
// and coplanar circles of different radii read as a target diagram rather than a
// ball of wire. Each ring carries a label naming what it is and which component
// owns it, each system owns a colour, and each gets its own label angle so the
// text doesn't pile up in one spot.
//
// All drawing here is [Conditional("UNITY_EDITOR")] — the calls compile out of
// player builds entirely, so components can call them unguarded. MechGizmos
// keeps the boss palette and forwards to these same primitives.
using UnityEngine;

public static class EnemyGizmos
{
    /// <summary>Segments per full circle. 64 is smooth at the radii enemies use
    /// without flooding the scene view's line budget when a whole stack draws.</summary>
    private const int Segments = 64;

    #region Palette

    // One colour per system, kept here rather than inline so two components
    // can't drift into the same shade and become impossible to tell apart.
    public static readonly Color Radar  = new Color(1.00f, 0.85f, 0.20f); // EnemyBrain detection
    public static readonly Color Patrol = new Color(0.35f, 0.80f, 1.00f); // positioning / hover
    public static readonly Color Ranged = new Color(1.00f, 0.55f, 0.15f); // gun ranges
    public static readonly Color Melee  = new Color(1.00f, 0.30f, 0.30f); // contact ranges
    public static readonly Color Dodge  = new Color(0.60f, 0.45f, 1.00f); // evasion band
    public static readonly Color Avenge = new Color(1.00f, 0.35f, 0.75f); // ally-death aggro
    public static readonly Color Sniper = new Color(0.55f, 1.00f, 0.85f); // long-range shots
    public static readonly Color Blast  = new Color(1.00f, 0.20f, 0.10f); // explosion radii

    /// <summary>Anywhere the player is safe — dead zones, inner radii.</summary>
    public static readonly Color Safe = new Color(0.25f, 1.00f, 0.45f);
    /// <summary>Live state: a check that currently passes.</summary>
    public static readonly Color Pass = new Color(0.30f, 1.00f, 0.35f);
    /// <summary>Live state: a check that currently fails.</summary>
    public static readonly Color Fail = new Color(1.00f, 0.30f, 0.30f);

    /// <summary>Picks between the live colours so a ring shows whether its gate is
    /// open right now, and falls back to the system colour outside play mode.</summary>
    public static Color Live(bool passing, Color idle) =>
        Application.isPlaying ? (passing ? Pass : Fail) : idle;

    #endregion

    #region Ground Plane

    /// <summary>Drops a point onto the floor beneath it so rings from different
    /// components end up coplanar and comparable. Falls back to the point itself
    /// when there's nothing under it (prefab view, flyer over a gap).</summary>
    public static Vector3 Ground(Vector3 from, float probeHeight = 3f, float probeDepth = 60f)
    {
        return Physics.Raycast(from + Vector3.up * probeHeight, Vector3.down,
                               out RaycastHit hit, probeHeight + probeDepth,
                               ~0, QueryTriggerInteraction.Ignore)
            ? hit.point + Vector3.up * 0.02f // float a hair so the ring isn't z-fighting the floor
            : from;
    }

    /// <summary>Vertical tether from an airborne unit down to the ring plane, so a
    /// flyer's rings read as "below me" instead of floating loose in the scene.</summary>
    [System.Diagnostics.Conditional("UNITY_EDITOR")]
    public static void DropLine(Vector3 from, Vector3 groundPoint, Color color, string label = null)
    {
        Gizmos.color = color * 0.6f;
        Gizmos.DrawLine(from, groundPoint);

        Gizmos.color = color;
        Gizmos.DrawLine(groundPoint + Vector3.left * 0.25f, groundPoint + Vector3.right * 0.25f);
        Gizmos.DrawLine(groundPoint + Vector3.back * 0.25f, groundPoint + Vector3.forward * 0.25f);

        if (!string.IsNullOrEmpty(label))
            Label(Vector3.Lerp(from, groundPoint, 0.5f), label, color);
    }

    #endregion

    #region Rings

    /// <summary>A flat circle on the ground, optionally labelled. <paramref name="labelAngle"/>
    /// is where on the ring the text sits, in degrees clockwise from world +Z — give
    /// each component its own angle and the labels stop stacking on top of each other.</summary>
    [System.Diagnostics.Conditional("UNITY_EDITOR")]
    public static void GroundRing(Vector3 center, float radius, Color color,
                                  string label = null, float labelAngle = 0f, bool dashed = false)
    {
        if (radius <= 0.01f) return;

        Gizmos.color = color;

        Vector3 prev = center + Ring(0f) * radius;
        for (int i = 1; i <= Segments; i++)
        {
            Vector3 next = center + Ring(360f * i / Segments) * radius;
            // Dashed = every other segment skipped. Used for "soft" boundaries
            // (min ranges, break-off rings) so they read differently from the
            // solid outer reach of a behaviour.
            if (!dashed || i % 2 == 0) Gizmos.DrawLine(prev, next);
            prev = next;
        }

        if (!string.IsNullOrEmpty(label))
            Label(center + Ring(labelAngle) * radius, label, color);
    }

    /// <summary>The band a behaviour is actually active in — min range to max range.
    /// Drawn as a dashed inner ring, a solid outer one, and a few spokes joining
    /// them, which is what makes it read as one annulus instead of two unrelated
    /// circles that happen to share a centre.</summary>
    [System.Diagnostics.Conditional("UNITY_EDITOR")]
    public static void GroundBand(Vector3 center, float minRadius, float maxRadius, Color color,
                                  string label = null, float labelAngle = 0f)
    {
        GroundRing(center, maxRadius, color, label, labelAngle);

        if (minRadius <= 0.01f) return;

        GroundRing(center, minRadius, color * 0.7f, null, 0f, dashed: true);

        Gizmos.color = color * 0.7f;
        for (int i = 0; i < 8; i++)
        {
            Vector3 dir = Ring(360f * i / 8f);
            Gizmos.DrawLine(center + dir * minRadius, center + dir * maxRadius);
        }
    }

    /// <summary>A facing cone flat on the ground — two boundary spokes and the arc
    /// between them. For angle gates like a shooter's fire-arc requirement.</summary>
    [System.Diagnostics.Conditional("UNITY_EDITOR")]
    public static void GroundCone(Vector3 center, Vector3 forward, float halfAngle, float radius,
                                  Color color, string label = null)
    {
        forward.y = 0f;
        if (forward.sqrMagnitude < 0.001f) return;
        forward.Normalize();

        Gizmos.color = color;

        Vector3 left  = Quaternion.AngleAxis(-halfAngle, Vector3.up) * forward;
        Vector3 right = Quaternion.AngleAxis( halfAngle, Vector3.up) * forward;
        Gizmos.DrawLine(center, center + left  * radius);
        Gizmos.DrawLine(center, center + right * radius);

        int steps = Mathf.Max(2, Mathf.CeilToInt(halfAngle * 2f / 5f));
        Vector3 prev = center + left * radius;
        for (int i = 1; i <= steps; i++)
        {
            Vector3 dir = Quaternion.AngleAxis(Mathf.Lerp(-halfAngle, halfAngle, i / (float)steps), Vector3.up) * forward;
            Vector3 next = center + dir * radius;
            Gizmos.DrawLine(prev, next);
            prev = next;
        }

        if (!string.IsNullOrEmpty(label))
            Label(center + forward * radius, label, color);
    }

    #endregion

    #region Heights

    /// <summary>A horizontal bar at a given world height, with a drop line to the
    /// ground — for anything whose vertical position matters as much as its reach.</summary>
    [System.Diagnostics.Conditional("UNITY_EDITOR")]
    public static void HeightMarker(Vector3 groundPoint, float worldY, Vector3 direction, float length,
                                    Color color, string label = null)
    {
        direction.y = 0f;
        if (direction.sqrMagnitude < 0.001f) direction = Vector3.forward;
        direction.Normalize();

        Vector3 from = new Vector3(groundPoint.x, worldY, groundPoint.z);
        Vector3 to = from + direction * length;

        Gizmos.color = color;
        Gizmos.DrawLine(from, to);

        // Drop line so the height reads against the floor rather than floating.
        Gizmos.color = color * 0.5f;
        Gizmos.DrawLine(from, new Vector3(from.x, groundPoint.y, from.z));

        if (!string.IsNullOrEmpty(label)) Label(to, label, color);
    }

    #endregion

    #region Labels

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

    #endregion

    /// <summary>Unit vector on the XZ plane at the given clockwise angle from +Z.</summary>
    private static Vector3 Ring(float degrees)
    {
        float rad = degrees * Mathf.Deg2Rad;
        return new Vector3(Mathf.Sin(rad), 0f, Mathf.Cos(rad));
    }
}
