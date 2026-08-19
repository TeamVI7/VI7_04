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
        elevator.OnFloorSelected += HandleFloorSelected;
        elevator.OnArrivedFloor += HandleArrived;
    }

    void OnDestroy()
    {
        if (elevator == null) return;
        elevator.OnFloorSelectionOpened -= HandleOpened;
        elevator.OnFloorSelectionClosed -= HandleClosed;
        elevator.OnAccessDenied -= HandleAccessDenied;
        elevator.OnFloorSelected -= HandleFloorSelected;
        elevator.OnArrivedFloor -= HandleArrived;
    }

    void Update()
    {
        if (elevator == null || panelRoot == null) return;

        // Panel (và collider của các nút) phải còn bật khi người chơi đang ở trong
        // cabin, kể cả lúc cửa đã đóng — nếu tắt theo OnFloorSelectionClosed thì
        // raycast của PlayerElevatorInteractor không còn trúng nút nào nữa.
        bool shouldShow = elevator.IsPlayerInside()
                          || elevator.GetState() == ElevatorController.State.WaitingForSelection;

        if (panelRoot.activeSelf != shouldShow)
            panelRoot.SetActive(shouldShow);
    }

    void HandleOpened(int currentFloorIndex)
    {
        if (panelRoot) panelRoot.SetActive(true);
        RefreshButtons(currentFloorIndex);
    }

    void HandleClosed()
    {
        // Chỉ ẩn khi người chơi không ở trong cabin; Update() sẽ quyết định phần còn lại.
        if (panelRoot && !elevator.IsPlayerInside()) panelRoot.SetActive(false);
    }

    // Đã chốt tầng: khoá toàn bộ nút cho tới khi cabin tới nơi.
    void HandleFloorSelected(int floorIndex)
    {
        if (floorButtons == null) return;
        foreach (var btn in floorButtons)
            if (btn != null) btn.SetSelectable(false);
    }

    void HandleArrived(int floorIndex)
    {
        RefreshButtons(floorIndex);
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

            btn.SetSelectable(!isCurrent && !elevator.IsBusy);

            var lockIcon = btn.transform.Find("LockIcon");
            if (lockIcon) lockIcon.gameObject.SetActive(elevator.floors[idx].isLocked && !canAccess);
        }
    }
}