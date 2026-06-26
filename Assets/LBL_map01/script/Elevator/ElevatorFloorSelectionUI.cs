using UnityEngine;
using UnityEngine.UI;

// Gắn vào panel UI bên trong cabin thang máy.
// Panel cần dùng World Space Canvas (đặt trong không gian 3D của cabin) để
// raycast theo hướng nhìn của Player (PlayerElevatorInteractor.cs) có thể trúng nút.
//
// Prefab nút (buttonPrefab) cần có sẵn:
//  - Component ElevatorFloorButtonLookable (đã gán targetGraphic, normalColor, highlightColor)
//  - 1 Collider (BoxCollider) bao trùm vùng nút
//  - object con tên "Label" (Text) hiển thị tên tầng
//  - object con tên "LockIcon" (Image) hiện icon khóa, mặc định tắt
public class ElevatorFloorSelectionUI : MonoBehaviour
{
    [Header("Tham chiếu")]
    public ElevatorController elevator;
    public GameObject panelRoot;          // toàn bộ panel, ẩn/hiện theo state
    public Transform buttonContainer;     // nơi chứa các nút tầng (vd: Vertical/Grid Layout Group)
    public GameObject buttonPrefab;       // prefab 1 nút tầng (xem yêu cầu ở trên)

    private ElevatorFloorButtonLookable[] spawnedButtons;

    void Start()
    {
        if (elevator == null)
        {
            Debug.LogError("ElevatorFloorSelectionUI: chưa gán elevator!", this);
            return;
        }

        BuildButtons();
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

    void BuildButtons()
    {
        spawnedButtons = new ElevatorFloorButtonLookable[elevator.floors.Count];

        for (int i = 0; i < elevator.floors.Count; i++)
        {
            int floorIndex = i;
            GameObject btnObj = Instantiate(buttonPrefab, buttonContainer);
            btnObj.SetActive(true);

            var label = btnObj.transform.Find("Label")?.GetComponent<Text>();
            if (label) label.text = elevator.floors[i].floorName;

            var lookable = btnObj.GetComponent<ElevatorFloorButtonLookable>();
            if (lookable == null)
            {
                Debug.LogError("Prefab nút thiếu component ElevatorFloorButtonLookable!", btnObj);
                continue;
            }

            lookable.floorIndex = floorIndex;
            lookable.elevator = elevator;
            spawnedButtons[i] = lookable;
        }
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
        Debug.Log($"Không thể vào tầng {elevator.floors[floorIndex].floorName}: cần thẻ nhân viên đúng mã.");
    }

    void RefreshButtons(int currentFloorIndex)
    {
        for (int i = 0; i < spawnedButtons.Length; i++)
        {
            if (spawnedButtons[i] == null) continue;

            bool isCurrent = (i == currentFloorIndex);
            bool canAccess = elevator.CanAccessFloor(i);

            spawnedButtons[i].SetSelectable(!isCurrent);

            var lockIcon = spawnedButtons[i].transform.Find("LockIcon");
            if (lockIcon) lockIcon.gameObject.SetActive(elevator.floors[i].isLocked && !canAccess);
        }
    }
}