// MechGizmos.cs — the mech boss's scene-view palette.
//
// The drawing itself lives in EnemyGizmos, which every enemy shares; this file
// is the boss's own colour set plus thin forwarders so the attack scripts keep
// calling MechGizmos.GroundRing/GroundBand/GroundCone as before.
//
// Why ground rings rather than wire spheres: every attack lives as a sibling
// component on one GameObject, so selecting the boss fires every
// OnDrawGizmosSelected at once, and a dozen concentric balls centred on the same
// point is technically all the information and practically unreadable. Ranges in
// this fight are flat XZ distances anyway, so coplanar rings sit where the player
// actually stands and read as a target diagram.
//
// All drawing is [Conditional("UNITY_EDITOR")] — the calls compile out of player
// builds entirely, so attacks can call them unguarded.
using UnityEngine;

public static class MechGizmos
{
    #region Palette

    // One colour per attack, kept here rather than inline so two attacks can't
    // drift into the same shade and become impossible to tell apart.
    public static readonly Color Stomp   = new Color(1.00f, 0.35f, 0.25f);
    public static readonly Color Dash    = new Color(1.00f, 0.25f, 0.85f);
    public static readonly Color Gatling = new Color(0.30f, 0.85f, 1.00f);
    public static readonly Color Spike   = new Color(0.85f, 0.60f, 0.25f);
    public static readonly Color Missile = new Color(1.00f, 0.60f, 0.10f);
    public static readonly Color Laser   = new Color(1.00f, 0.20f, 0.15f);

    /// <summary>Anywhere the player is safe — dead zones, inner radii.</summary>
    public static readonly Color Safe = EnemyGizmos.Safe;
    /// <summary>Live state: a check that currently passes.</summary>
    public static readonly Color Pass = EnemyGizmos.Pass;
    /// <summary>Live state: a check that currently fails.</summary>
    public static readonly Color Fail = EnemyGizmos.Fail;

    #endregion

    #region Forwarders

    /// <inheritdoc cref="EnemyGizmos.Ground"/>
    public static Vector3 Ground(Vector3 from, float probeHeight = 3f, float probeDepth = 30f) =>
        EnemyGizmos.Ground(from, probeHeight, probeDepth);

    /// <inheritdoc cref="EnemyGizmos.GroundRing"/>
    [System.Diagnostics.Conditional("UNITY_EDITOR")]
    public static void GroundRing(Vector3 center, float radius, Color color,
                                  string label = null, float labelAngle = 0f, bool dashed = false) =>
        EnemyGizmos.GroundRing(center, radius, color, label, labelAngle, dashed);

    /// <inheritdoc cref="EnemyGizmos.GroundBand"/>
    [System.Diagnostics.Conditional("UNITY_EDITOR")]
    public static void GroundBand(Vector3 center, float minRadius, float maxRadius, Color color,
                                  string label = null, float labelAngle = 0f) =>
        EnemyGizmos.GroundBand(center, minRadius, maxRadius, color, label, labelAngle);

    /// <inheritdoc cref="EnemyGizmos.GroundCone"/>
    [System.Diagnostics.Conditional("UNITY_EDITOR")]
    public static void GroundCone(Vector3 center, Vector3 forward, float halfAngle, float radius,
                                  Color color, string label = null) =>
        EnemyGizmos.GroundCone(center, forward, halfAngle, radius, color, label);

    /// <inheritdoc cref="EnemyGizmos.HeightMarker"/>
    [System.Diagnostics.Conditional("UNITY_EDITOR")]
    public static void HeightMarker(Vector3 groundPoint, float worldY, Vector3 direction, float length,
                                    Color color, string label = null) =>
        EnemyGizmos.HeightMarker(groundPoint, worldY, direction, length, color, label);

    /// <inheritdoc cref="EnemyGizmos.Label"/>
    [System.Diagnostics.Conditional("UNITY_EDITOR")]
    public static void Label(Vector3 at, string text, Color color) =>
        EnemyGizmos.Label(at, text, color);

    #endregion
}
