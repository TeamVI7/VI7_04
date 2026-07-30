using UnityEngine;

public class ServerBlock : MonoBehaviour
{
    [Header("Settings")]
    [Tooltip("Chỉ dùng để vẽ Gizmo tham khảo, việc phát hiện Player thực tế dựa vào Collider (Is Trigger) gắn trên object này.")]
    public float interactRange = 2f;

    [Header("Số thứ tự Server (CỐ ĐỊNH)")]
    [Tooltip("Số hiển thị/ghi trên thân server (VD: 1, 2, 3...). Đây là số CỐ ĐỊNH gắn liền với vị trí vật lý của khối này trong scene — KHÔNG bị random. Cái bị random là THỨ TỰ phải tắt các số này.")]
    public int serverNumber = 1;

    [Header("Đèn báo trạng thái (nhiều đèn)")]
    [Tooltip("Kéo TẤT CẢ Renderer của các đèn báo trên thân server vào đây (mỗi đèn 1 phần tử).")]
    public Renderer[] statusLights;

    [Tooltip("Màu đèn khi server CÒN HOẠT ĐỘNG (chưa tắt)")]
    public Color emptyColor = Color.red;

    [Tooltip("Màu đèn khi server ĐÃ TẮT đúng thứ tự")]
    public Color filledColor = Color.green;

    [Tooltip("Màu đèn nhấp nháy báo lỗi khi bấm SAI thứ tự")]
    public Color wrongColor = new Color(1f, 0.6f, 0f); // cam

    [Tooltip("Độ sáng phát quang (emission) của đèn. Tăng lên nếu muốn đèn sáng/glow rõ hơn.")]
    public float emissionIntensity = 2.5f;

    [Header("Hint")]
    [Tooltip("Hiện khi player đứng gần server này (bất kể đúng/sai lượt).")]
    public GameObject interactHint;

    [Header("Outline khi player đứng gần")]
    public Outline outline;

    [Header("Âm thanh")]
    [Tooltip("AudioSource để phát âm thanh (nếu để trống sẽ tự thêm 1 cái lúc runtime).")]
    public AudioSource audioSource;
    [Tooltip("Âm thanh phát khi tắt server ĐÚNG thứ tự.")]
    public AudioClip shutdownSound;
    [Range(0f, 1f)] public float sfxVolume = 1f;
    [Tooltip("Âm thanh phát khi bấm SAI thứ tự.")]
    public AudioClip wrongSound;
    [Range(0f, 1f)] public float wrongSfxVolume = 1f;

    [Header("VFX khi trồi lên")]
    [Tooltip("Particle System phát khi khối NÀY trồi lên (VD: khói, tia điện, bụi sàn). Đặt sẵn trong Scene ở gần chân server, để chế độ Play On Awake = OFF. Để trống nếu không dùng.")]
    public ParticleSystem riseVFX;

    /// <summary>True khi server này đã được tắt đúng thứ tự.</summary>
    public bool IsShutdown { get; private set; } = false;

    private ServerMinigameManager _manager;
    private bool _playerNearby = false;
    private MaterialPropertyBlock _mpb;

    private void Start()
    {
        _manager = FindFirstObjectByType<ServerMinigameManager>();

        if (audioSource == null)
        {
            audioSource = GetComponent<AudioSource>();
            if (audioSource == null)
            {
                audioSource = gameObject.AddComponent<AudioSource>();
                audioSource.playOnAwake = false;
                audioSource.spatialBlend = 1f;
            }
        }

        var col = GetComponent<Collider>();
        if (col == null)
            Debug.LogWarning($"[ServerBlock] {gameObject.name} chưa có Collider nào — player sẽ không thể tương tác. Hãy thêm Collider (BoxCollider/SphereCollider) và tick 'Is Trigger'.");
        else if (!col.isTrigger)
            Debug.LogWarning($"[ServerBlock] {gameObject.name} có Collider nhưng chưa tick 'Is Trigger' — OnTriggerEnter sẽ không hoạt động.");

        _mpb = new MaterialPropertyBlock();

        SetLightsColor(emptyColor);
        if (interactHint) interactHint.SetActive(false);

        if (outline == null) outline = GetComponent<Outline>();
        if (outline != null) outline.enabled = false;
    }

    private void Update()
    {
        if (IsShutdown) return;

        if (interactHint) interactHint.SetActive(_playerNearby);

        if (_playerNearby && Input.GetKeyDown(KeyCode.F))
            _manager?.TryShutdownServer(this);
    }

    private void OnTriggerEnter(Collider other)
    {
        if (!other.CompareTag("Player")) return;
        _playerNearby = true;
        if (outline != null && !IsShutdown) outline.enabled = true;
    }

    private void OnTriggerStay(Collider other)
    {
        if (!other.CompareTag("Player")) return;
        _playerNearby = true;
    }

    private void OnTriggerExit(Collider other)
    {
        if (!other.CompareTag("Player")) return;
        _playerNearby = false;
        if (interactHint) interactHint.SetActive(false);
        if (outline != null) outline.enabled = false;
    }

    /// <summary>Gọi bởi ServerMinigameManager khi player bấm ĐÚNG thứ tự tại server này.</summary>
    public void SetShutdown(bool value)
    {
        IsShutdown = value;
        SetLightsColor(value ? filledColor : emptyColor);

        if (value)
        {
            if (interactHint) interactHint.SetActive(false);
            if (outline != null) outline.enabled = false;
            if (audioSource != null && shutdownSound != null)
                audioSource.PlayOneShot(shutdownSound, sfxVolume);

            Debug.Log($"[Server] {gameObject.name} (Số {serverNumber}) đã TẮT đúng thứ tự!");
        }
    }

    /// <summary>Gọi bởi ServerMinigameManager khi player bấm SAI thứ tự — nhấp đèn cam + phát âm báo lỗi.</summary>
    public void PlayWrongFeedback()
    {
        SetLightsColor(wrongColor);
        if (audioSource != null && wrongSound != null)
            audioSource.PlayOneShot(wrongSound, wrongSfxVolume);

        Debug.Log($"[Server] Bấm SAI thứ tự tại {gameObject.name} (Số {serverNumber})!");
    }

    /// <summary>Reset khối này về trạng thái ban đầu (dùng khi thứ tự bị random lại sau khi bấm sai).</summary>
    public void ResetState()
    {
        IsShutdown = false;
        SetLightsColor(emptyColor);
        if (interactHint) interactHint.SetActive(_playerNearby);
    }

    public void PlayRiseVFX()
    {
        if (riseVFX != null)
            riseVFX.Play();
    }

    private void SetLightsColor(Color c)
    {
        if (statusLights == null) return;

        Color emissive = c * emissionIntensity;

        foreach (var r in statusLights)
        {
            if (r == null) continue;

            r.GetPropertyBlock(_mpb);
            _mpb.SetColor("_BaseColor", c);
            _mpb.SetColor("_Color", c);
            _mpb.SetColor("_EmissionColor", emissive);
            r.SetPropertyBlock(_mpb);
        }
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = IsShutdown ? Color.green : Color.cyan;
        Gizmos.DrawWireSphere(transform.position, interactRange);
    }
}