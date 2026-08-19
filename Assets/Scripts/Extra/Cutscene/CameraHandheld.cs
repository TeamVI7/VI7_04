using UnityEngine;

/// Continuous low-amplitude handheld drift shared by the cutscene cameras.
///
/// Deliberately a *provider* rather than something that writes to the transform
/// itself. The takeoff camera smooths from its own previous value, so baking the
/// noise into that value would feed the shake back into the smoothing and let it
/// compound. Each cutscene samples this and composes the offset into its own
/// single transform write instead.
public class CameraHandheld : MonoBehaviour
{
    [Header("Amplitude")]
    public float positionAmplitude = 0.015f;   // meters of sway
    public float rotationAmplitude = 0.25f;    // degrees of sway

    [Header("Character")]
    public float noiseSpeed = 0.6f;            // higher = busier operator
    [Range(0f, 2f)]
    public float intensity = 1f;               // master scale, tweenable per shot

    public Vector3 PositionOffset { get; private set; }
    public Quaternion RotationOffset { get; private set; } = Quaternion.identity;

    // Keeps two cameras sharing this component from drifting in lockstep.
    private float _seed;

    void Awake()
    {
        _seed = Random.Range(0f, 1000f);
    }

    /// Advances the drift to <paramref name="time"/>. Call once per frame before
    /// reading the offsets.
    public void Sample(float time)
    {
        float t = time * noiseSpeed + _seed;

        PositionOffset = new Vector3(Noise(t, 0f), Noise(t, 17f), Noise(t, 31f))
                         * (positionAmplitude * intensity);

        RotationOffset = Quaternion.Euler(
            new Vector3(Noise(t, 53f), Noise(t, 71f), Noise(t, 97f))
            * (rotationAmplitude * intensity));
    }

    public void Reset()
    {
        PositionOffset = Vector3.zero;
        RotationOffset = Quaternion.identity;
    }

    /// Perlin rather than Random.Range: it is continuous, so the camera drifts
    /// like a held rig instead of jittering like static.
    private static float Noise(float t, float offset)
    {
        return (Mathf.PerlinNoise(t + offset, offset) - 0.5f) * 2f;
    }
}
