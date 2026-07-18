using UnityEngine;
using UnityEngine.UI;

public class ElevatorFloorSelectionUI : MonoBehaviour
{
    [Header("Tham chiếu")]
    public ElevatorController elevator;
    public GameObject panelRoot;          

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