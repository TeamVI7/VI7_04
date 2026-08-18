using System.Collections.Generic;
using UnityEngine;
using DynamicMeshCutter;

/// <summary>
/// Implemented by anything that wants to control how a melee swing slices it.
/// MeleeMeshCutter looks for this on (or around) whatever collider the swing hit
/// before falling back to a plain MeshTarget lookup, so an object whose collider
/// and cuttable mesh live on different GameObjects — a riot shield, a prop with a
/// simplified box collider — can still point the cutter at the right mesh and
/// react to the result.
/// </summary>
public interface ISliceable
{
    /// <summary>
    /// Meshes that should be cut for a hit at this point — null or empty to skip slicing.
    /// Every returned target is cut with the SAME plane, which is what lets a character built
    /// from several skinned meshes (the humanoid enemies are three) come apart as one body
    /// instead of one piece splitting while the rest vanishes.
    /// </summary>
    IReadOnlyList<MeshTarget> ResolveSliceTargets(Vector3 hitPoint);

    /// <summary>Called once the cut has been queued (Dynamic Mesh Cutter cuts async).</summary>
    void OnSliceStarted(Vector3 hitPoint, Vector3 planeNormal);

    /// <summary>
    /// Called once when every queued cut has resolved. <paramref name="success"/> is true if
    /// at least one target was actually cut, and <paramref name="pieces"/> holds the halves
    /// from all of them; on total failure it is null and nothing was cut.
    /// </summary>
    void OnSliceFinished(bool success, GameObject[] pieces);
}
