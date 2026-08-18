using UnityEngine;

// Implemented by presets that need a line/cone between two points (laser
// sights, sweep arcs). The adapter calls this before Play().
public interface ITelegraphAimable
{
    void SetEndpoints(Transform origin, Transform target);
}

// Implemented by presets that need placing in world space (ground AoE decals,
// impact rings). The adapter calls this before Play().
public interface ITelegraphPlaceable
{
    void SetTransform(Vector3 worldPosition, Quaternion worldRotation);
}
