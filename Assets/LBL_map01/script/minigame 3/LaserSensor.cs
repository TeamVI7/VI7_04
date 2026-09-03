using UnityEngine;
using UnityEngine.Events;

// Gắn vào cảm biến. Đặt cảm biến ở Layer nằm trong "blockMask" của LaserSource
// (không cần nằm trong "mirrorMask").
public class LaserSensor : MonoBehaviour
{
    [Tooltip("Laser phải chiếu liên tục bao nhiêu giây thì mới kích hoạt")]
    public float requiredHoldTime = 1f;

    public UnityEvent OnActivated;   // kéo hàm mở cửa (vd: AutoDoor / LockedDoor) vào đây
    public UnityEvent OnDeactivated; // (tuỳ chọn) khi laser bị ngắt

    private bool isHit = false;
    private float hitTimer = 0f;
    private bool activated = false;
    private int lastHitFrame = -10;

    // Được LaserSource gọi mỗi frame
    public void SetHit(bool hit)
    {
        isHit = hit;
        if (hit) lastHitFrame = Time.frameCount;
    }

    void OnDisable()
    {
        isHit = false;
        hitTimer = 0f;
        activated = false;
    }

    void Update()
    {
        // Trạng thái này do LaserSource ĐẨY xuống mỗi frame. Nếu nguồn laser bị tắt
        // hoặc bị huỷ ngay lúc tia đang chiếu vào đây thì sẽ không còn ai gọi
        // SetHit(false) nữa — cảm biến sẽ tự chạy hết giờ rồi kích hoạt dù chẳng còn
        // tia nào. Nên coi như mất tín hiệu nếu quá 1 frame không được cập nhật.
        if (isHit && Time.frameCount - lastHitFrame > 1)
            isHit = false;

        if (isHit)
        {
            hitTimer += Time.deltaTime;
            if (hitTimer >= requiredHoldTime && !activated)
            {
                activated = true;
                OnActivated.Invoke();
            }
        }
        else
        {
            hitTimer = 0f;
            if (activated)
            {
                activated = false;
                OnDeactivated.Invoke();
            }
        }
    }
}