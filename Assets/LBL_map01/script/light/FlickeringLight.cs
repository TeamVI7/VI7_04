using UnityEngine;

/// <summary>
/// Hiệu ứng đèn chập chờn kiểu "đèn sắp hỏng":
/// - Hỗ trợ NHIỀU đèn cùng nhấp nháy đồng bộ (gắn vào 1 component duy nhất).
/// - Có 2 trạng thái xen kẽ:
///     + Stable   : đèn sáng tương đối ổn định (vẫn rung nhẹ cho tự nhiên), đóng góp chiếu sáng đầy đủ.
///     + Flicker  : đèn chập chờn mạnh (giống sắp hỏng) trong một khoảng thời gian ngắn.
/// - Cường độ tối thiểu (minIntensity) luôn > 0 nên đèn không bao giờ tắt hẳn,
///   vẫn luôn đóng góp ánh sáng cho scene dù đang ở pha chập.
/// </summary>
public class FlickeringLight : MonoBehaviour
{
    [System.Serializable]
    public class LightUnit
    {
        [Tooltip("Đèn Light cần điều khiển")]
        public Light light;

        [Tooltip("Renderer của bóng đèn (model) để chỉnh emission, có thể bỏ trống")]
        public Renderer bulbRenderer;

        [HideInInspector] public Material bulbMaterial;
    }

    [Header("Danh sách đèn (kéo nhiều đèn vào đây)")]
    public LightUnit[] lights;

    [Header("Màu emission của bóng đèn")]
    public Color emissionColor = Color.white;

    [Header("Pha chập chờn (Flicker)")]
    [Tooltip("Cường độ thấp nhất khi chập (luôn > 0 để vẫn còn sáng)")]
    public float minIntensity = 0.15f;
    [Tooltip("Cường độ cao nhất khi chập")]
    public float maxIntensity = 5f;
    [Tooltip("Tốc độ nhấp nháy tối thiểu/tối đa trong pha chập (giây giữa mỗi lần đổi)")]
    public float flickerSpeedMin = 0.02f;
    public float flickerSpeedMax = 0.1f;
    [Tooltip("Pha chập kéo dài bao lâu (giây)")]
    public float flickerDurationMin = 0.4f;
    public float flickerDurationMax = 1.5f;

    [Header("Pha sáng ổn định (Stable)")]
    [Tooltip("Cường độ sáng khi ở trạng thái ổn định")]
    public float stableIntensity = 4.5f;
    [Tooltip("Độ rung nhẹ quanh stableIntensity để không bị cứng")]
    public float stableJitter = 0.3f;
    [Tooltip("Pha ổn định kéo dài bao lâu (giây) trước khi chập lại")]
    public float stableDurationMin = 3f;
    public float stableDurationMax = 8f;

    private enum State { Stable, Flicker }
    private State state = State.Stable;

    private float phaseTimer;   // đếm thời gian còn lại của pha hiện tại (Stable/Flicker)
    private float stepTimer;    // đếm thời gian tới lần đổi intensity kế tiếp

    void Start()
    {
        // Nếu chưa kéo gì vào lights[] mà có Light trên chính GameObject này -> tự thêm vào
        if ((lights == null || lights.Length == 0))
        {
            var selfLight = GetComponent<Light>();
            if (selfLight != null)
            {
                lights = new LightUnit[] { new LightUnit { light = selfLight } };
            }
        }

        foreach (var unit in lights)
        {
            if (unit.bulbRenderer != null)
            {
                unit.bulbMaterial = unit.bulbRenderer.material; // instance riêng, không sửa material chung
                unit.bulbMaterial.EnableKeyword("_EMISSION");
            }
        }

        EnterStable();
    }

    void Update()
    {
        phaseTimer -= Time.deltaTime;
        if (phaseTimer <= 0f)
        {
            if (state == State.Stable) EnterFlicker();
            else EnterStable();
        }

        stepTimer -= Time.deltaTime;
        if (stepTimer <= 0f)
        {
            float intensity;
            float nextStep;

            if (state == State.Flicker)
            {
                intensity = Random.Range(minIntensity, maxIntensity);
                nextStep = Random.Range(flickerSpeedMin, flickerSpeedMax);
            }
            else
            {
                intensity = Mathf.Max(minIntensity, stableIntensity + Random.Range(-stableJitter, stableJitter));
                nextStep = Random.Range(0.1f, 0.25f);
            }

            ApplyIntensity(intensity);
            stepTimer = nextStep;
        }
    }

    private void EnterStable()
    {
        state = State.Stable;
        phaseTimer = Random.Range(stableDurationMin, stableDurationMax);
        stepTimer = 0f; // áp dụng ngay
    }

    private void EnterFlicker()
    {
        state = State.Flicker;
        phaseTimer = Random.Range(flickerDurationMin, flickerDurationMax);
        stepTimer = 0f; // áp dụng ngay
    }

    private void ApplyIntensity(float intensity)
    {
        foreach (var unit in lights)
        {
            if (unit.light != null)
                unit.light.intensity = intensity;

            if (unit.bulbMaterial != null)
                unit.bulbMaterial.SetColor("_EmissionColor", emissionColor * intensity);
        }
    }
}