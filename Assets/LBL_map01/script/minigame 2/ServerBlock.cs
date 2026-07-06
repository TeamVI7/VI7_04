using UnityEngine;

/// <summary>
/// Gắn vào mỗi khối server.
/// Player lại gần + bấm E + có SSD trong tay → gắn SSD vào.
/// Khi đủ SSD → báo cho ServerMinigameManager.
///
/// Bản cập nhật: KHÔNG đổi màu nguyên khối server nữa.
/// Thay vào đó điều khiển nhiều "đèn báo" (statusLights) trên thân máy:
/// - Đỏ khi chưa gắn SSD
/// - Xanh khi đã gắn SSD
/// Kèm âm thanh khi gắn SSD.
/// </summary>
public class ServerBlock : MonoBehaviour
{
    [Header("Settings")]
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
    public GameObject interactHint; // "[E] Gắn SSD"
    public GameObject noSSDHint;    // "[!] Cần SSD"

    [Header("Âm thanh")]
    [Tooltip("AudioSource để phát âm thanh gắn SSD (nếu để trống sẽ tự thêm 1 cái lúc runtime).")]
    public AudioSource audioSource;
    [Tooltip("Âm thanh phát khi gắn SSD thành công.")]
    public AudioClip insertSSDSound;
    [Range(0f, 1f)] public float sfxVolume = 1f;

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

        if (_playerNearby && hasSSD && Input.GetKeyDown(KeyCode.E))
            InsertSSD();
    }

    private void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag("Player"))
        {
            _playerNearby = true;
            _inventory = other.GetComponent<PlayerInventory>(); // lấy trực tiếp từ collider
        }
    }

    private void OnTriggerExit(Collider other)
    {
        if (other.CompareTag("Player"))
        {
            _playerNearby = false;
            if (interactHint) interactHint.SetActive(false);
            if (noSSDHint)    noSSDHint.SetActive(false);
        }
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