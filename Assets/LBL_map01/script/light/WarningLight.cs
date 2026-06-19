using UnityEngine;

public class WarningLight : MonoBehaviour
{
    // === XOAY ===
    [Header("=== XOAY ===")]
    public float RotationSpeed = 200f;

    public enum RotationAxis { World_X, World_Y, World_Z }
    public RotationAxis Direction = RotationAxis.World_Y;

    public bool ReverseDirection = false;

    // === ÁNH SÁNG ===
    [Header("=== ÁNH SÁNG ===")]
    public Light SpotLight;
    public Color LightColor = Color.red;
    [Range(0f, 10f)] public float MaxIntensity = 5f;
    public float FlashSpeed = 4f;

    // === VẬT LIỆU ===
    [Header("=== VẬT LIỆU ===")]
    public MeshRenderer BulbRenderer;
    public string EmissionColorProperty = "_EmissionColor";
    [Range(0f, 10f)] public float EmissionIntensity = 3f;

    // === TRẠNG THÁI ===
    [Header("=== TRẠNG THÁI ===")]
    public bool IsActive = true;

    // --- Private ---
    private Material _bulbMat;
    private float _flashTimer;

    void Start()
    {
        if (BulbRenderer != null)
            _bulbMat = BulbRenderer.material;

        ApplyColor();
    }

    void Update()
    {
        if (!IsActive)
        {
            SetLight(0f);
            SetEmission(Color.black);
            return;
        }

        Rotate();
        Flash();
    }

    // ── Xoay ──────────────────────────────────────────────
    // ── Xoay ──────────────────────────────────────────────
    void Rotate()
    {
        if (SpotLight == null) return;

        float speed = RotationSpeed * (ReverseDirection ? -1f : 1f) * Time.deltaTime;

        // SỬ DỤNG TRỤC LOCAL CỦA MODEL (transform.up/right/forward)
        Vector3 axis = Direction switch
        {
            RotationAxis.World_X => transform.right,
            RotationAxis.World_Z => transform.forward,
            _ => transform.up,   // Mặc định xoay quanh trục Y của chính cục model
        };

        // Ép đèn xoay quanh tâm model, theo đúng góc nghiêng của model
        SpotLight.transform.RotateAround(transform.position, axis, speed);
    }

    // ── Nhấp nháy ─────────────────────────────────────────
    void Flash()
    {
        _flashTimer += Time.deltaTime * FlashSpeed;
        float t = Mathf.Abs(Mathf.Sin(_flashTimer));

        float intensity = t * MaxIntensity;
        SetLight(intensity);

        Color emitColor = LightColor * (t * EmissionIntensity);
        SetEmission(emitColor);
    }

    // ── Helpers ───────────────────────────────────────────
    void ApplyColor()
    {
        if (SpotLight != null)
            SpotLight.color = LightColor;
    }

    void SetLight(float intensity)
    {
        if (SpotLight != null)
            SpotLight.intensity = intensity;
    }

    void SetEmission(Color color)
    {
        if (_bulbMat != null)
            _bulbMat.SetColor(EmissionColorProperty, color);
    }

    public void SetActive(bool active)
    {
        IsActive = active;
    }

#if UNITY_EDITOR
    void OnValidate()
    {
        ApplyColor();
    }
#endif
}