using UnityEngine;

public class PickUpDropSystem : MonoBehaviour
{
    [Header("Cấu hình Khoảng cách & Vị trí")]
    public Transform cameraTransform;   // Kéo Main Camera vào đây
    public Transform holdArea;          // Vị trí giữ vật phẩm (HoldArea con của Camera)
    
    [Tooltip("Bán kính vùng hình cầu quét xung quanh người để nhặt đồ")]
    public float pickUpRadius = 4f;     // Thay thế cho tia pickUpRange cũ
    public float dropForwardForce = 4f; // Lực đẩy vật phẩm ra xa khi thả

    [Header("Phím chức năng")]
    public KeyCode pickUpKey = KeyCode.F; // Nhấn F để nhặt
    public KeyCode dropKey = KeyCode.Q;   // Nhấn Q để thả

    private GameObject heldItem;
    private Rigidbody heldItemRb;
    private Collider heldItemCollider;

    void Update()
    {
        // 1. Kiểm tra bấm phím Nhặt Đồ (F)
        if (Input.GetKeyDown(pickUpKey))
        {
            if (heldItem == null) // Nếu tay đang trống thì mới cho nhặt
            {
                TryPickUpItemWithRadius();
            }
        }

        // 2. Kiểm tra bấm phím Thả Đồ (Q)
        if (Input.GetKeyDown(dropKey))
        {
            if (heldItem != null) // Nếu đang cầm đồ thì mới cho thả
            {
                DropItem();
            }
        }
    }

    void TryPickUpItemWithRadius()
    {
        // Tạo một vùng hình cầu vô hình quét xung quanh vị trí của Player
        Collider[] hitColliders = Physics.OverlapSphere(transform.position, pickUpRadius);
        
        GameObject closestItem = null;
        float closestDistance = Mathf.Infinity;

        // Duyệt qua tất cả các vật thể lọt vào vùng quét
        foreach (var hitCollider in hitColliders)
        {
            // Kiểm tra xem vật thể đó có Tag là "Item" hay không
            if (hitCollider.CompareTag("Item"))
            {
                // Tính khoảng cách từ món đồ đến chân Player
                float distance = Vector3.Distance(transform.position, hitCollider.transform.position);
                
                // Thuật toán: Ưu tiên chọn món đồ nằm gần sát người bạn nhất
                if (distance < closestDistance)
                {
                    closestDistance = distance;
                    closestItem = hitCollider.gameObject;
                }
            }
        }

        // Nếu tìm thấy món đồ hợp lệ trong vùng quét, tiến hành nhặt nó lên
        if (closestItem != null)
        {
            PickUpItem(closestItem);
        }
        else
        {
            Debug.LogWarning("==> Bảng Console: Không tìm thấy vật phẩm 'Item' nào trong bán kính " + pickUpRadius + " mét quanh bạn.");
        }
    }

    void PickUpItem(GameObject item)
    {
        // In thông báo ra Console ngay khi nhặt thành công để bạn dễ theo dõi
        Debug.Log("==> Bảng Console: ĐÃ NHẶT THÀNH CÔNG VẬT PHẨM: " + item.name);

        heldItem = item;
        heldItemRb = item.GetComponent<Rigidbody>();
        heldItemCollider = item.GetComponent<Collider>();

        if (heldItemRb != null)
        {
            heldItemRb.isKinematic = true; // Tắt vật lý để vật phẩm găm cố định trên tay
        }

        if (heldItemCollider != null)
        {
            heldItemCollider.enabled = false; // Tắt va chạm tạm thời để không đẩy lệch Player
        }

        // Đưa vật phẩm vào làm con của HoldArea và reset tọa độ về tâm tay cầm
        item.transform.SetParent(holdArea);
        item.transform.localPosition = Vector3.zero;
        item.transform.localRotation = Quaternion.identity;
    }

    void DropItem()
    {
        Debug.Log("==> Bảng Console: Đang thực hiện lệnh thả vật phẩm: " + heldItem.name);

        // 1. Tháo vật phẩm ra khỏi tay cầm (HoldArea)
        heldItem.transform.SetParent(null);

        // Đẩy vị trí vật phẩm ra trước mặt Camera 1 mét để tuyệt đối không bị kẹt vào người
        heldItem.transform.position = cameraTransform.position + cameraTransform.forward * 1.0f;

        // 2. Xử lý Vật lý khi ném
        if (heldItemRb != null)
        {
            heldItemRb.isKinematic = false; // Bật lại trọng lực
            heldItemRb.AddForce(cameraTransform.forward * dropForwardForce, ForceMode.Impulse); // Ném ra xa
            Debug.Log("=> Đã bật lại Rigidbody và ném vật phẩm.");
        }
        else
        {
            Debug.LogError("=> LỖI: Vật phẩm không có thành phần Rigidbody!");
        }

        // 3. Bật lại va chạm để vật phẩm rơi trúng sàn nhà
        if (heldItemCollider != null)
        {
            heldItemCollider.enabled = true;
            Debug.Log("=> Đã bật lại Collider.");
        }

        // Reset dữ liệu biến tạm về trống rỗng
        heldItem = null;
        heldItemRb = null;
        heldItemCollider = null;
    }

    // ĐOẠN MẸO: Vẽ một vòng tròn màu xanh lá trong tab Scene khi bạn click vào Player để bạn thấy rõ tầm nhặt
    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.green;
        Gizmos.DrawWireSphere(transform.position, pickUpRadius);
    }
}