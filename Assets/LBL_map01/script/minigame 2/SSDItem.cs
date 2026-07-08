using UnityEngine;

/// <summary>
/// Gắn vào GameObject SSD nằm trong map.
/// Player lại gần + bấm F để nhặt vào inventory.
/// Dùng trigger collider thay vì distance check.
/// </summary>
public class SSDItem : MonoBehaviour
{
    [Header("Settings")]
    public string itemName = "SSD Drive";

    [Header("Hint UI (tuỳ chọn)")]
    public GameObject interactHint;

    [Header("Âm thanh")]
    [Tooltip("Âm thanh phát khi nhặt SSD thành công.")]
    public AudioClip pickupSound;
    [Range(0f, 1f)] public float sfxVolume = 1f;

    [Header("VFX khi nhặt")]
    [Tooltip("Prefab Particle System phát khi nhặt SSD (VD: tia sáng, hạt lấp lánh bay lên). " +
             "Dùng Prefab + Instantiate (không phải object con có sẵn), vì gameObject SSD sẽ bị " +
             "SetActive(false) ngay sau khi nhặt nên VFX gắn sẵn trên nó cũng sẽ bị tắt theo.")]
    public GameObject pickupVFXPrefab;
    [Tooltip("Thời gian tồn tại của VFX trước khi tự huỷ (giây).")]
    public float pickupVFXLifetime = 2f;

    private bool _pickedUp = false;
    private bool _playerNearby = false;
    private PlayerInventory _inventory;

    private void Start()
    {
        if (interactHint) interactHint.SetActive(false);
    }

    private void Update()
    {
        if (_pickedUp) return;

        if (interactHint) interactHint.SetActive(_playerNearby);

        if (_playerNearby && Input.GetKeyDown(KeyCode.F))
            TryPickup();
    }

    private void OnTriggerEnter(Collider other)
    {
        if (!other.CompareTag("Player")) return;

        _playerNearby = true;
        // GetComponentInParent phòng trường hợp Collider nằm ở object con của Player
        _inventory = other.GetComponent<PlayerInventory>()
                     ?? other.GetComponentInParent<PlayerInventory>();

        if (_inventory == null)
            Debug.LogWarning("[SSDItem] Player vào vùng nhặt nhưng không tìm thấy PlayerInventory!");
    }

    private void OnTriggerExit(Collider other)
    {
        if (!other.CompareTag("Player")) return;

        _playerNearby = false;
        _inventory = null;
        if (interactHint) interactHint.SetActive(false);
    }

    private void TryPickup()
    {
        if (_inventory == null)
        {
            Debug.LogWarning("[SSDItem] Không tìm thấy PlayerInventory!");
            return;
        }

        if (_inventory.PickupSSD(this))
        {
            _pickedUp = true;
            if (interactHint) interactHint.SetActive(false);

            // Phát âm thanh tại vị trí SSD bằng object tạm (PlayClipAtPoint),
            // vì gameObject sẽ bị SetActive(false) ngay sau đó nên không thể
            // dùng AudioSource gắn trên chính object này.
            if (pickupSound != null)
                AudioSource.PlayClipAtPoint(pickupSound, transform.position, sfxVolume);

            // Tương tự với VFX: Instantiate 1 bản mới tại vị trí SSD rồi tự huỷ,
            // vì object gốc sắp bị tắt nên không thể dùng ParticleSystem con có sẵn trên nó.
            if (pickupVFXPrefab != null)
            {
                GameObject vfx = Instantiate(pickupVFXPrefab, transform.position, Quaternion.identity);
                Destroy(vfx, pickupVFXLifetime);
            }

            gameObject.SetActive(false);
            Debug.Log($"[SSDItem] Đã nhặt: {itemName}");
        }
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.yellow;
        // Vẽ sphere theo size của collider nếu có
        var col = GetComponent<SphereCollider>();
        if (col != null)
            Gizmos.DrawWireSphere(transform.position, col.radius);
        else
            Gizmos.DrawWireSphere(transform.position, 2f);
    }
}