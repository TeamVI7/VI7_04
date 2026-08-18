using System;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Decides where an enemy is actually allowed to appear. Three constraints, in
/// order of how much they matter:
///
///   1. On the NavMesh — a spawn off it produces an enemy that stands still
///      forever, since every behaviour guards on isOnNavMesh.
///   2. Far enough from the player that it doesn't materialise in their face.
///   3. Not somewhere the player is currently looking. This is the one that sells
///      it — an enemy that fades into existence on-screen reads as a spawner,
///      an enemy that walks around a corner reads as reinforcements.
///
/// Constraint 3 is best-effort by design: in a small room with the player
/// sweeping the camera there may be no unobserved point at all, and refusing to
/// spawn would stall the encounter. When every attempt fails this returns the
/// least-bad candidate (furthest from the player) rather than giving up.
///
/// Serialized as a field on <see cref="EnemySpawnArea"/> rather than a component,
/// so placement tuning lives next to the encounter it belongs to.
/// </summary>
[Serializable]
public class EnemySpawnPlacement
{
    [Header("Fixed Spawn Points")]
    [Tooltip("Optional hand-placed markers. Leave empty to sample the area's region procedurally.\n\nWhen assigned, these are tried FIRST — the spawner picks whichever marker is currently unobserved. Use them where you care about the exact staging (a doorway, a balcony, behind a specific pillar) and let everything else sample.")]
    public Transform[] SpawnPoints;

    [Tooltip("If every marker is currently visible or invalid, fall back to sampling the region anyway. Turn this off to make the area spawn ONLY at your markers, even if that means waiting.")]
    public bool FallBackToSampling = true;

    [Header("Rules")]
    [Tooltip("Never spawn closer than this to the player, even if the point is out of sight — around a corner but two metres away still reads as cheating.")]
    public float MinDistanceFromPlayer = 8f;

    [Tooltip("Reject points the player can currently see. Costs one raycast per candidate; worth it.")]
    public bool RejectVisibleToPlayer = true;

    [Tooltip("Layers that count as blocking the player's view — walls, terrain, props. A point inside the camera frustum is only rejected if nothing on these layers stands between the camera and it.")]
    public LayerMask SightBlockingLayers;

    [Tooltip("Extra viewport margin on the visibility test, as a fraction of screen size. 0.1 also rejects points just off the edge of the screen, so enemies don't pop in as the player turns.")]
    [Range(0f, 0.5f)] public float ScreenEdgeMargin = 0.1f;

    [Header("Sampling")]
    [Tooltip("How many random points to try before settling for the least-bad one. Higher = better placement in cluttered areas, at the cost of a few more raycasts per spawn.")]
    [Min(1)] public int SampleAttempts = 12;

    [Tooltip("How far a sampled point may be snapped to reach the NavMesh. Too large and enemies teleport across the room to find floor; too small and sampling fails in sparse areas.")]
    public float NavMeshSnapRadius = 4f;

    [Tooltip("Height above the resolved NavMesh point that the enemy is actually placed. Keep it small — it exists to avoid spawning a capsule half inside the floor.")]
    public float SpawnHeightOffset = 0.1f;

    private Camera _camera;

    /// <summary>Why a candidate point was or wasn't used. Recorded only in the
    /// editor (see <see cref="RecordCandidate"/>) and drawn by the spawn area's
    /// gizmos, so you can see at a glance why an enemy landed where it did.</summary>
    public enum CandidateVerdict { Accepted, TooCloseToPlayer, VisibleToPlayer, SettledFor }

    public readonly struct DebugCandidate
    {
        public readonly Vector3 Position;
        public readonly CandidateVerdict Verdict;

        public DebugCandidate(Vector3 position, CandidateVerdict verdict)
        {
            Position = position;
            Verdict  = verdict;
        }
    }

    /// <summary>Candidates considered during the most recent resolution. Populated
    /// in the editor only — in a build the recording calls compile away and this
    /// stays empty.</summary>
    [NonSerialized] public readonly System.Collections.Generic.List<DebugCandidate> LastCandidates = new System.Collections.Generic.List<DebugCandidate>();

    [System.Diagnostics.Conditional("UNITY_EDITOR")]
    private void RecordCandidate(Vector3 p, CandidateVerdict v) => LastCandidates.Add(new DebugCandidate(p, v));

    [System.Diagnostics.Conditional("UNITY_EDITOR")]
    private void ClearCandidates() => LastCandidates.Clear();

    /// <summary>
    /// Resolves a spawn position. <paramref name="randomPointInRegion"/> supplies raw
    /// candidates — the spawn area owns the region shape, this class only judges
    /// the points it's handed. Returns false only when nothing could be placed on
    /// the NavMesh at all, which means the region is badly configured.
    /// </summary>
    public bool TryGetPoint(Func<Vector3> randomPointInRegion, out Vector3 point)
    {
        point = default;
        ClearCandidates();

        if (TryGetMarkerPoint(out point)) return true;
        if (SpawnPoints != null && SpawnPoints.Length > 0 && !FallBackToSampling) return false;
        if (randomPointInRegion == null) return false;

        Vector3 fallback = default;
        float fallbackScore = float.NegativeInfinity;
        bool haveFallback = false;

        for (int i = 0; i < SampleAttempts; i++)
        {
            if (!TryResolveOnNavMesh(randomPointInRegion(), out Vector3 candidate)) continue;

            CandidateVerdict verdict = Judge(candidate);
            if (verdict == CandidateVerdict.Accepted)
            {
                RecordCandidate(candidate, CandidateVerdict.Accepted);
                point = candidate;
                return true;
            }

            RecordCandidate(candidate, verdict);

            // Kept as the consolation prize — the furthest-from-player point we
            // managed to land on the NavMesh, used only if nothing legal turns up.
            float score = DistanceFromPlayer(candidate);
            if (score > fallbackScore)
            {
                fallbackScore = score;
                fallback      = candidate;
                haveFallback  = true;
            }
        }

        if (haveFallback)
        {
            RecordCandidate(fallback, CandidateVerdict.SettledFor);
            point = fallback;
            return true;
        }
        return false;
    }

    // Markers are already authored placements, so they skip the region check —
    // but they still have to pass distance and visibility, otherwise a marker in
    // the player's line of sight would defeat the whole point of having rules.
    private bool TryGetMarkerPoint(out Vector3 point)
    {
        point = default;
        if (SpawnPoints == null || SpawnPoints.Length == 0) return false;

        Vector3 best = default;
        float bestScore = float.NegativeInfinity;
        bool haveAny = false;

        // Random start index so repeated spawns don't always drain the same marker first.
        int offset = UnityEngine.Random.Range(0, SpawnPoints.Length);

        for (int i = 0; i < SpawnPoints.Length; i++)
        {
            Transform marker = SpawnPoints[(i + offset) % SpawnPoints.Length];
            if (marker == null) continue;
            if (!TryResolveOnNavMesh(marker.position, out Vector3 candidate)) continue;

            CandidateVerdict verdict = Judge(candidate);
            RecordCandidate(candidate, verdict);
            if (verdict == CandidateVerdict.Accepted) { point = candidate; return true; }

            float score = DistanceFromPlayer(candidate);
            if (score > bestScore) { bestScore = score; best = candidate; haveAny = true; }
        }

        // Every marker was observed or too close. Only settle for one of them if
        // we aren't allowed to sample instead.
        if (haveAny && !FallBackToSampling) { point = best; return true; }
        return false;
    }

    /// <summary>Snaps a raw point onto the NavMesh, or fails if there's no floor
    /// within NavMeshSnapRadius of it.</summary>
    public bool TryResolveOnNavMesh(Vector3 raw, out Vector3 resolved)
    {
        if (NavMesh.SamplePosition(raw, out NavMeshHit hit, NavMeshSnapRadius, NavMesh.AllAreas))
        {
            resolved = hit.position + Vector3.up * SpawnHeightOffset;
            return true;
        }

        resolved = default;
        return false;
    }

    /// <summary>Runs a candidate past both rules and reports which one it failed —
    /// the verdict is what the gizmos colour-code, so "no valid spawn point" is
    /// diagnosable in the scene view instead of a guess.</summary>
    private CandidateVerdict Judge(Vector3 candidate)
    {
        if (DistanceFromPlayer(candidate) < MinDistanceFromPlayer) return CandidateVerdict.TooCloseToPlayer;
        if (RejectVisibleToPlayer && IsVisibleToPlayer(candidate)) return CandidateVerdict.VisibleToPlayer;
        return CandidateVerdict.Accepted;
    }

    private static float DistanceFromPlayer(Vector3 p) =>
        PlayerHealth.Transform != null
            ? Vector3.Distance(p, PlayerHealth.Transform.position)
            : float.MaxValue;

    /// <summary>Frustum test first (cheap), then a single LOS raycast to confirm —
    /// a point inside the frustum but behind a wall isn't actually visible, and
    /// rejecting it would throw away perfectly good spawn positions.</summary>
    private bool IsVisibleToPlayer(Vector3 candidate)
    {
        if (_camera == null) _camera = Camera.main;
        if (_camera == null) return false; // no camera to be seen by

        // Test at roughly chest height — the enemy's feet being off-screen while
        // its head is on-screen still counts as visible.
        Vector3 testPoint = candidate + Vector3.up * 1f;

        Vector3 vp = _camera.WorldToViewportPoint(testPoint);
        if (vp.z <= 0f) return false; // behind the camera

        float min = -ScreenEdgeMargin;
        float max = 1f + ScreenEdgeMargin;
        if (vp.x < min || vp.x > max || vp.y < min || vp.y > max) return false;

        Vector3 eye = _camera.transform.position;
        Vector3 toPoint = testPoint - eye;
        float dist = toPoint.magnitude;
        if (dist < 0.01f) return true;

        // Nothing blocking between camera and point => the player can see it.
        return !Physics.Raycast(eye, toPoint / dist, dist, SightBlockingLayers, QueryTriggerInteraction.Ignore);
    }
}
