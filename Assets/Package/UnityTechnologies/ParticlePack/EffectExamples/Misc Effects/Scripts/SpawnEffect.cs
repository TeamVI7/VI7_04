using UnityEngine;

public class SpawnEffect : MonoBehaviour
{
    public float spawnEffectTime = 2;

    public AnimationCurve fadeIn;

    private ParticleSystem ps;
    private float timer = 0;
    private Renderer _renderer;

    private int shaderProperty;

    void Start()
    {
        shaderProperty = Shader.PropertyToID("_cutoff");

        _renderer = GetComponent<Renderer>();

        ps = GetComponentInChildren<ParticleSystem>();

        var main = ps.main;
        main.duration = spawnEffectTime;

        ps.Play();
    }

    void Update()
    {
        timer += Time.deltaTime;

        _renderer.material.SetFloat(
            shaderProperty,
            fadeIn.Evaluate(
                Mathf.InverseLerp(0, spawnEffectTime, timer)
            )
        );

        // Phát xong -> tự hủy
        if (timer >= spawnEffectTime)
        {
            Destroy(gameObject, 1f);
        }
    }
}