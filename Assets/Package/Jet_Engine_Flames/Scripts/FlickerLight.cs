using UnityEngine;

/// <summary>
/// Randomises a Light's intensity every frame between lightMin and lightMax
/// to give the engine glow a flickering, combusting feel.
/// </summary>
public class FlickerLight : MonoBehaviour
{
    [Tooltip("Light to flicker. Falls back to a Light on this GameObject.")]
    public new Light light;

    [Tooltip("Lowest intensity of the flicker range.")]
    public float lightMin = 3f;

    [Tooltip("Highest intensity of the flicker range.")]
    public float lightMax = 6f;

    void Awake()
    {
        if (light == null)
            light = GetComponent<Light>();
    }

    void Update()
    {
        if (light == null)
            return;

        light.intensity = Random.Range(lightMin, lightMax);
    }
}
