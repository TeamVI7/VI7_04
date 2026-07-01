using UnityEngine;
using UnityEngine.UI;

// Gắn vào panel UI bên trong cabin thang máy.
// Panel cần dùng World Space Canvas (đặt trong không gian 3D của cabin) để
// raycast theo hướng nhìn của Player (PlayerElevatorInteractor.cs) có thể trúng nút.
//
// KHÔNG còn tự sinh nút bằng prefab nữa. Bạn tự đặt sẵn từng nút (đã có sẵn
// component ElevatorFloorButtonLookable + Collider) ngay trên tấm panel trong
// Scene view, canh cho khớp mắt, rồi kéo từng nút đó vào danh sách "floorButtons"
// bên dưới. Mỗi nút tự khai "floorIndex" của chính nó trong Inspector của nút đó.
public class ElevatorFloorSelectionUI : MonoBehaviour
{
    [Header("Tham chiếu")]
    public ElevatorController elevator;
    public GameObject panelRoot;          // toàn bộ panel, ẩn/hiện theo state

    [Header("Các nút đã đặt sẵn thủ công trong Scene")]
    [Tooltip("Kéo từng nút (đã đặt sẵn lên panel trong Scene view, có component ElevatorFloorButtonLookable) vào đây. " +
             "Không cần prefab, không cần layout group.")]
    public ElevatorFloorButtonLookable[] floorButtons;

    void Start()
    {
        if (elevator == null)
        {
            Debug.LogError("ElevatorFloorSelectionUI: chưa gán elevator!", this);
            return;
        }

        // Đảm bảo mỗi nút có tham chiếu tới đúng elevator (phòng khi quên gán tay ở từng nút)
        foreach (var btn in floorButtons)
        {
            if (btn != null && btn.elevator == null)
                btn.elevator = elevator;
        }

        if (panelRoot) panelRoot.SetActive(false);

        elevator.OnFloorSelectionOpened += HandleOpened;
        elevator.OnFloorSelectionClosed += HandleClosed;
        elevator.OnAccessDenied += HandleAccessDenied;
    }

    void OnDestroy()
    {
        if (elevator == null) return;
        elevator.OnFloorSelectionOpened -= HandleOpened;
        elevator.OnFloorSelectionClosed -= HandleClosed;
        elevator.OnAccessDenied -= HandleAccessDenied;
    }

    void HandleOpened(int currentFloorIndex)
    {
        if (panelRoot) panelRoot.SetActive(true);
        RefreshButtons(currentFloorIndex);
    }

    void HandleClosed()
    {
        if (panelRoot) panelRoot.SetActive(false);
    }

    void HandleAccessDenied(int floorIndex)
    {
        // TODO: có thể nhấp nháy đỏ nút hoặc hiện thông báo "Cần thẻ nhân viên" ở đây
        if (elevator != null && floorIndex >= 0 && floorIndex < elevator.floors.Count)
            Debug.Log($"Không thể vào tầng {elevator.floors[floorIndex].floorName}: cần thẻ nhân viên đúng mã.");
    }

    void RefreshButtons(int currentFloorIndex)
    {
        if (floorButtons == null) return;

        foreach (var btn in floorButtons)
        {
            if (btn == null) continue;

            int idx = btn.floorIndex;
            if (idx < 0 || idx >= elevator.floors.Count)
            {
                Debug.LogWarning($"Nút '{btn.name}' có floorIndex={idx} không hợp lệ (ngoài danh sách floors).", btn);
                continue;
            }

            bool isCurrent = (idx == currentFloorIndex);
            bool canAccess = elevator.CanAccessFloor(idx);

            btn.SetSelectable(!isCurrent);

            var lockIcon = btn.transform.Find("LockIcon");
            if (lockIcon) lockIcon.gameObject.SetActive(elevator.floors[idx].isLocked && !canAccess);
        }
    }
}