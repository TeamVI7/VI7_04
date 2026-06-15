using UnityEngine;

public class FlickeringLight : MonoBehaviour
{
    public Light targetLight;
    public float minIntensity = 0.1f;
    public float maxIntensity = 5f;
    public float flickerSpeed = 0.1f;
    private float timer;

    void Start()
    {
        if (targetLight == null) targetLight = GetComponent<Light>();
    }

    void Update()
    {
        timer -= Time.deltaTime;
        if (timer <= 0)
        {
            targetLight.intensity = Random.Range(minIntensity, maxIntensity);
            timer = Random.Range(0.05f, flickerSpeed);
        }
    }
}