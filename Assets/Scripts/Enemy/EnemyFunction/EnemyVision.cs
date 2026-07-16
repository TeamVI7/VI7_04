using UnityEngine;

public static class EnemyVision
{
    private static readonly float[] HeightOffsets = { 0.2f, 1.0f, 1.6f };

    public static bool HasLineOfSight(Vector3 eyeOrigin, Transform target, float maxDistance, LayerMask obstacleLayers)
    {
        if (target == null) return false;

        for (int i = 0; i < HeightOffsets.Length; i++)
        {
            Vector3 point = target.position + Vector3.up * HeightOffsets[i];
            Vector3 toPoint = point - eyeOrigin;
            float dist = toPoint.magnitude;
            if (dist > maxDistance) continue;

            if (!Physics.Raycast(eyeOrigin, toPoint.normalized, dist, obstacleLayers))
                return true;
        }

        return false;
    }
}