using System;
using UnityEngine;

[Serializable]
public class ElevatorFloorData
{
    [Tooltip("Tên hiển thị, ví dụ: 'Tầng 1', 'Hầm B1'...")]
    public string floorName = "Tầng 1";

    [Tooltip("Số tầng hiển thị trên màn hình (VD: 1, 2, -1 cho hầm...)")]
    public int displayNumber = 1;

    [Tooltip("Vị trí Y của cabin khi dừng ở tầng này")]
    public float floorY = 0f;

    [Header("Khóa tầng")]
    [Tooltip("Tầng này có bị khóa không. Nếu khóa thì cần thẻ nhân viên đúng mã mới chọn được")]
    public bool isLocked = false;

    [Tooltip("Mã thẻ nhân viên cần có để mở tầng này (mỗi tầng có thể khác nhau). Để trống = không cần thẻ dù isLocked = true")]
    public string requiredCardId = "";
}