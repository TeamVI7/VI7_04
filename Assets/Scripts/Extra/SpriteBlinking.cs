using UnityEngine;

[RequireComponent(typeof(SpriteRenderer))]
public class BlinkingSprite : MonoBehaviour
{
    [Tooltip("Time in seconds between state toggles.")]
    public float blinkInterval = 0.5f;

    private SpriteRenderer spriteRenderer;
    private float timer;

    private void Awake()
    {
        spriteRenderer = GetComponent<SpriteRenderer>();
    }

    private void Update()
    {
        timer += Time.deltaTime;

        if (timer >= blinkInterval)
        {
            spriteRenderer.enabled = !spriteRenderer.enabled;
            timer = 0f;
        }
    }
}