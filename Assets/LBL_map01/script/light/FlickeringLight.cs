using UnityEngine;

public class FlickeringLight : MonoBehaviour
{
    public Light targetLight;
    public Renderer bulbRenderer;       // model bóng đèn
    public Color emissionColor = Color.white;
    public float minIntensity = 0.1f;
    public float maxIntensity = 5f;
    public float flickerSpeed = 0.1f;

    private float timer;
    private Material bulbMaterial;

    void Start()
    {
        if (targetLight == null) targetLight = GetComponent<Light>();

        if (bulbRenderer != null)
        {
            bulbMaterial = bulbRenderer.material; // instance riêng, không sửa material chung
            bulbMaterial.EnableKeyword("_EMISSION");
        }
    }

    void Update()
    {
        timer -= Time.deltaTime;
        if (timer <= 0)
        {
            float intensity = Random.Range(minIntensity, maxIntensity);
            targetLight.intensity = intensity;

            if (bulbMaterial != null)
            {
                bulbMaterial.SetColor("_EmissionColor", emissionColor * intensity);
            }

            timer = Random.Range(0.05f, flickerSpeed);
        }
    }
}