using UnityEngine;
using System.Collections;

public class CeilingExplosion : MonoBehaviour
{
    [Header("Hiệu ứng hạt")]
    public ParticleSystem debrisParticles;   // Mảnh vỡ
    public ParticleSystem dustParticles;     // Lớp bụi bay nhẹ rồi mờ dần

    [Header("Âm thanh")]
    [Tooltip("Cách 1: Nếu đã có sẵn AudioSource (kéo clip vào AudioSource đó từ trước) thì gán vào đây.")]
    public AudioSource explosionSound;
    [Tooltip("Cách 2: Đơn giản hơn — chỉ cần kéo file âm thanh (.wav/.mp3) vào đây, không cần tạo AudioSource thủ công. Script sẽ tự phát tại vị trí trần.")]
    public AudioClip explosionClip;
    [Range(0f, 1f)] public float explosionVolume = 1f;

    [Header("Hai trạng thái trần nhà")]
    public GameObject unbrokenCeiling;
    public GameObject brokenCeiling;

    [Header("Rung trần")]
    public Transform ceilingToShake;         // Kéo trần vào đây
    public float ceilingShakeDuration = 0.4f;
    public float ceilingShakeMagnitude = 0.15f;
    public float ceilingShakeFrequency = 25f;

    [Header("Rung camera (riêng, độc lập với trần)")]
    public Transform cameraToShake;          // Kéo Main Camera vào đây (để trống sẽ tự lấy Camera.main)
    public float cameraShakeDuration = 0.5f;
    public float cameraShakeMagnitude = 0.25f;
    public float cameraShakeFrequency = 20f;

    [Header("Test nhanh (chỉ hoạt động lúc Play)")]
    public bool enableTestKey = true;
    public KeyCode testKey = KeyCode.T;

    private bool hasExploded = false;

    void Awake()
    {
        // Nếu không kéo camera thủ công, tự động lấy Main Camera trong scene
        if (cameraToShake == null && Camera.main != null)
            cameraToShake = Camera.main.transform;
    }

    void Update()
    {
        if (enableTestKey && Input.GetKeyDown(testKey))
        {
            TriggerExplosion();
        }
    }

    void Start()
    {
        if (unbrokenCeiling != null) unbrokenCeiling.SetActive(true);
        if (brokenCeiling != null) brokenCeiling.SetActive(false);
    }

    public void TriggerExplosion()
    {
        if (hasExploded) return;
        hasExploded = true;

        // 1. Tráo đổi trần nhà
        if (unbrokenCeiling != null) unbrokenCeiling.SetActive(false);
        if (brokenCeiling != null) brokenCeiling.SetActive(true);

        // 2. Bắn đá vụn
        if (debrisParticles != null) debrisParticles.Play();

        // 3. Bụi bay nhẹ, mờ dần rồi mất
        if (dustParticles != null) dustParticles.Play();

        // 4. Phát tiếng nổ
        PlayExplosionSound();

        // 5. Rung trần
        if (ceilingToShake != null)
            StartCoroutine(ShakeRoutine(ceilingToShake, ceilingShakeDuration, ceilingShakeMagnitude, ceilingShakeFrequency));

        // 6. Rung camera (riêng, có thể mạnh/lâu hơn trần)
        if (cameraToShake != null)
            StartCoroutine(ShakeRoutine(cameraToShake, cameraShakeDuration, cameraShakeMagnitude, cameraShakeFrequency));
    }

    private void PlayExplosionSound()
    {
        // Ưu tiên AudioSource đã setup sẵn (nếu có gán)
        if (explosionSound != null)
        {
            explosionSound.volume = explosionVolume;
            explosionSound.Play();
            return;
        }

        // Nếu không có AudioSource, nhưng có kéo sẵn AudioClip -> tự phát luôn, không cần setup gì thêm
        if (explosionClip != null)
        {
            AudioSource.PlayClipAtPoint(explosionClip, transform.position, explosionVolume);
        }
    }

    private IEnumerator ShakeRoutine(Transform target, float duration, float magnitude, float frequency)
    {
        Vector3 originalPos = target.localPosition;
        float elapsed = 0f;

        // Seed ngẫu nhiên riêng cho mỗi lần rung để trần và camera không rung y hệt nhau (dù cùng gọi hàm chung)
        float seedOffset = Random.Range(0f, 100f);

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;

            float damper = 1f - Mathf.Clamp01(elapsed / duration);

            float t = Time.time * frequency + seedOffset;
            float offsetX = (Mathf.PerlinNoise(t, 0f) - 0.5f) * 2f;
            float offsetY = (Mathf.PerlinNoise(0f, t) - 0.5f) * 2f;
            float offsetZ = (Mathf.PerlinNoise(t, t) - 0.5f) * 2f;

            Vector3 offset = new Vector3(offsetX, offsetY, offsetZ) * magnitude * damper;
            target.localPosition = originalPos + offset;

            yield return null;
        }

        target.localPosition = originalPos; // trả object về đúng vị trí gốc
    }

    [ContextMenu("TEST VỤ NỔ NGAY BÂY GIỜ !")]
    private void TestExplosion()
    {
        TriggerExplosion();
    }
}