using UnityEngine;

/// <summary>
/// Gắn vào GameObject SSD nằm trong map.
/// Player lại gần + bấm E để nhặt vào inventory.
/// Dùng trigger collider thay vì distance check.
/// </summary>
public class SSDItem : MonoBehaviour
{
    [Header("Settings")]
    public string itemName = "SSD Drive";

    [Header("Hint UI (tuỳ chọn)")]
    public GameObject interactHint;

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

    if (_playerNearby && Input.GetKeyDown(KeyCode.E))
        TryPickup();
}

    private void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag("Player"))
        {
            _playerNearby = true;
            _inventory = other.GetComponent<PlayerInventory>();
            Debug.Log("[SSD] Player vào vùng nhặt");
        }
    }

    private void OnTriggerExit(Collider other)
    {
        if (other.CompareTag("Player"))
        {
            _playerNearby = false;
            _inventory = null;
            if (interactHint) interactHint.SetActive(false);
        }
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