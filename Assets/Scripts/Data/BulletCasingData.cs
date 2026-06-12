using UnityEngine;

/// <summary>
/// Per-weapon-type casing configuration. Create via Assets > Create > FPS > Bullet Casing Data.
/// </summary>
[CreateAssetMenu(menuName = "FPS/Bullet Casing Data", fileName = "CasingData_New")]
public class BulletCasingData : ScriptableObject
{
    [Header("Prefab")]
    [Tooltip("The casing GameObject to pool and spawn.")]
    public GameObject casingPrefab;

    [Header("Physics")]
    [Tooltip("Base ejection force applied at spawn.")]
    public float ejectionForce = 3f;

    [Tooltip("Upward kick added to ejection force.")]
    public float ejectionUpwardForce = 1.5f;

    [Tooltip("Random torque range applied on all axes.")]
    public Vector2 torqueRange = new Vector2(5f, 15f);

    [Tooltip("Random spread cone half-angle in degrees applied to ejection direction.")]
    [Range(0f, 45f)]
    public float ejectionSpreadAngle = 10f;

    [Header("Lifetime")]
    [Tooltip("Seconds before casing is returned to pool.")]
    public float lifetime = 4f;

    [Tooltip("Seconds before casing mesh fades out (0 = instant despawn).")]
    public float fadeDuration = 0.5f;

    [Header("Audio")]
    [Tooltip("Clips played on first bounce. Randomly selected.")]
    public AudioClip[] bounceClips;

    [Tooltip("Volume of bounce sounds.")]
    [Range(0f, 1f)]
    public float bounceVolume = 0.4f;

    [Tooltip("Minimum velocity magnitude required to trigger bounce sound.")]
    public float minBounceVelocity = 0.5f;

    [Tooltip("Max number of bounce sounds per casing lifetime.")]
    public int maxBounceSounds = 2;

    [Header("Pool")]
    [Tooltip("How many casings of this type to pre-warm in the pool.")]
    public int prewarmCount = 10;

    [Header("Debug")]
    public bool debugDrawEjection = false;
}