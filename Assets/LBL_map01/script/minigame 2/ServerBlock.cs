using UnityEngine;

/// <summary>
/// Gắn vào mỗi khối server.
/// Player lại gần + bấm F + có SSD trong tay → gắn SSD vào.
/// Khi đủ SSD → báo cho ServerMinigameManager.
///
/// Đèn báo trạng thái (statusLights): đỏ khi chưa gắn SSD, xanh khi đã gắn SSD.
/// Kèm âm thanh khi gắn SSD.
///
/// Bản cập nhật: sửa lại phần nhận diện Player bằng Collider cho chắc chắn hơn
/// (giống cách SSDItem đang làm) + đổi phím tương tác sang F.
/// </summary>
public class ServerBlock : MonoBehaviour
{
    [Header("Settings")]
    [Tooltip("Chỉ dùng để vẽ Gizmo tham khảo, việc phát hiện Player thực tế dựa vào Collider (Is Trigger) gắn trên object này.")]
    public float interactRange = 2f;

    [Header("Đèn báo trạng thái (nhiều đèn)")]
    [Tooltip("Kéo TẤT CẢ Renderer của các đèn báo trên thân server vào đây (mỗi đèn 1 phần tử).")]
    public Renderer[] statusLights;

    [Tooltip("Màu đèn khi CHƯA gắn SSD")]
    public Color emptyColor = Color.red;

    [Tooltip("Màu đèn khi ĐÃ gắn SSD")]
    public Color filledColor = Color.green;

    [Tooltip("Độ sáng phát quang (emission) của đèn. Tăng lên nếu muốn đèn sáng/glow rõ hơn.")]
    public float emissionIntensity = 2.5f;

    [Header("Hint")]
    public GameObject interactHint; // "[F] Gắn SSD"
    public GameObject noSSDHint;    // "[!] Cần SSD"

    [Header("Âm thanh")]
    [Tooltip("AudioSource để phát âm thanh gắn SSD (nếu để trống sẽ tự thêm 1 cái lúc runtime).")]
    public AudioSource audioSource;
    [Tooltip("Âm thanh phát khi gắn SSD thành công.")]
    public AudioClip insertSSDSound;
    [Range(0f, 1f)] public float sfxVolume = 1f;

    [Header("VFX khi trồi lên")]
    [Tooltip("Particle System phát khi khối NÀY trồi lên (VD: khói, tia điện, bụi sàn). Đặt sẵn trong Scene ở gần chân server, để chế độ Play On Awake = OFF. Để trống nếu không dùng.")]
    public ParticleSystem riseVFX;

    public bool IsFilled { get; private set; } = false;

    private PlayerInventory       _inventory;
    private ServerMinigameManager _manager;
    private bool                  _playerNearby = false;
    private MaterialPropertyBlock _mpb;

    private void Start()
    {
        var p = GameObject.FindGameObjectWithTag("Player");
        if (p != null) _inventory = p.GetComponent<PlayerInventory>();

        _manager = FindFirstObjectByType<ServerMinigameManager>();

        if (audioSource == null)
        {
            audioSource = GetComponent<AudioSource>();
            if (audioSource == null)
            {
                audioSource = gameObject.AddComponent<AudioSource>();
                audioSource.playOnAwake = false;
                audioSource.spatialBlend = 1f; // âm thanh 3D theo vị trí server
            }
        }

        // Kiểm tra xem object có Collider dạng Trigger chưa, để dễ debug khi
        // player không tương tác được (lỗi phổ biến nhất là thiếu/ chưa tick "Is Trigger").
        var col = GetComponent<Collider>();
        if (col == null)
            Debug.LogWarning($"[ServerBlock] {gameObject.name} chưa có Collider nào — player sẽ không thể tương tác. Hãy thêm Collider (BoxCollider/SphereCollider) và tick 'Is Trigger'.");
        else if (!col.isTrigger)
            Debug.LogWarning($"[ServerBlock] {gameObject.name} có Collider nhưng chưa tick 'Is Trigger' — OnTriggerEnter sẽ không hoạt động.");

        _mpb = new MaterialPropertyBlock();

        SetLightsColor(emptyColor);
        if (interactHint) interactHint.SetActive(false);
        if (noSSDHint)    noSSDHint.SetActive(false);
    }

    private void Update()
    {
        if (IsFilled) return;

        bool hasSSD = _inventory != null && _inventory.HasSSD;

        if (interactHint) interactHint.SetActive(_playerNearby && hasSSD);
        if (noSSDHint)    noSSDHint.SetActive(_playerNearby && !hasSSD);

        if (_playerNearby && hasSSD && Input.GetKeyDown(KeyCode.F))
            InsertSSD();
    }

    private void OnTriggerEnter(Collider other)
    {
        if (!other.CompareTag("Player")) return;

        _playerNearby = true;
        // GetComponentInParent phòng trường hợp Collider nằm ở object con của Player
        _inventory = other.GetComponent<PlayerInventory>()
                     ?? other.GetComponentInParent<PlayerInventory>();

        if (_inventory == null)
            Debug.LogWarning($"[ServerBlock] {gameObject.name}: Player vào gần nhưng không tìm thấy PlayerInventory!");
    }

    // OnTriggerStay dự phòng: nếu vì lý do nào đó OnTriggerEnter bị bỏ lỡ
    // (VD player đã đứng sẵn trong vùng trigger trước khi script chạy),
    // vẫn tự khôi phục lại trạng thái nearby + inventory.
    private void OnTriggerStay(Collider other)
    {
        if (!other.CompareTag("Player")) return;

        if (!_playerNearby) _playerNearby = true;
        if (_inventory == null)
            _inventory = other.GetComponent<PlayerInventory>()
                         ?? other.GetComponentInParent<PlayerInventory>();
    }

    private void OnTriggerExit(Collider other)
    {
        if (!other.CompareTag("Player")) return;

        _playerNearby = false;
        if (interactHint) interactHint.SetActive(false);
        if (noSSDHint)    noSSDHint.SetActive(false);
    }

    private void InsertSSD()
    {
        if (_inventory == null || !_inventory.UseSSD()) return;

        IsFilled = true;
        SetLightsColor(filledColor);

        if (interactHint) interactHint.SetActive(false);
        if (noSSDHint)    noSSDHint.SetActive(false);

        if (audioSource != null && insertSSDSound != null)
            audioSource.PlayOneShot(insertSSDSound, sfxVolume);

        Debug.Log($"[Server] {gameObject.name} đã gắn SSD!");
        _manager?.CheckAllFilled();
    }

    /// <summary>Được ServerMinigameManager gọi ngay lúc khối này bắt đầu trồi lên.</summary>
    public void PlayRiseVFX()
    {
        if (riseVFX != null)
            riseVFX.Play();
    }

    /// <summary>Đổi màu toàn bộ đèn báo (không đụng vào màu thân server).</summary>
    private void SetLightsColor(Color c)
    {
        if (statusLights == null) return;

        Color emissive = c * emissionIntensity;

        foreach (var r in statusLights)
        {
            if (r == null) continue;

            r.GetPropertyBlock(_mpb);
            _mpb.SetColor("_BaseColor", c);       // URP Lit
            _mpb.SetColor("_Color", c);           // Standard / Built-in
            _mpb.SetColor("_EmissionColor", emissive); // đèn phát sáng (glow)
            r.SetPropertyBlock(_mpb);
        }
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = IsFilled ? Color.green : Color.cyan;
        Gizmos.DrawWireSphere(transform.position, interactRange);
    }
}