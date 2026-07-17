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

    // Được LaserSource gọi mỗi frame
    public void SetHit(bool hit)
    {
        isHit = hit;
    }

    void Update()
    {
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