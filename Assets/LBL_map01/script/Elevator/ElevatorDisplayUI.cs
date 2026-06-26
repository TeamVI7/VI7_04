using UnityEngine;
using UnityEngine.UI;

// Gắn vào màn hình hiển thị trong cabin (vd: 1 Canvas nhỏ phía trên cửa).
public class ElevatorDisplayUI : MonoBehaviour
{
    [Header("Tham chiếu")]
    public ElevatorController elevator;

    [Header("UI con")]
    public GameObject arrowUp;      // object mũi tên chỉ lên
    public GameObject arrowDown;    // object mũi tên chỉ xuống
    public Text floorNumberText;    // text hiển thị số tầng (đổi sang TMP_Text nếu dùng TextMeshPro)

    void Start()
    {
        if (elevator == null)
        {
            Debug.LogError("ElevatorDisplayUI: chưa gán elevator!", this);
            return;
        }

        elevator.OnMoveStarted += HandleMoveStarted;
        elevator.OnPassingFloor += HandlePassingFloor;
        elevator.OnArrivedFloor += HandleArrived;

        SetArrows(false, false);
        UpdateFloorText(elevator.GetCurrentFloorIndex());
    }

    void OnDestroy()
    {
        if (elevator == null) return;
        elevator.OnMoveStarted -= HandleMoveStarted;
        elevator.OnPassingFloor -= HandlePassingFloor;
        elevator.OnArrivedFloor -= HandleArrived;
    }

    void HandleMoveStarted(bool isGoingUp)
    {
        SetArrows(isGoingUp, !isGoingUp);
    }

    void HandlePassingFloor(int floorIndex, bool isGoingUp)
    {
        SetArrows(isGoingUp, !isGoingUp);
        UpdateFloorText(floorIndex);
    }

    void HandleArrived(int floorIndex)
    {
        SetArrows(false, false);
        UpdateFloorText(floorIndex);
    }

    void SetArrows(bool up, bool down)
    {
        if (arrowUp) arrowUp.SetActive(up);
        if (arrowDown) arrowDown.SetActive(down);
    }

    void UpdateFloorText(int floorIndex)
    {
        if (floorNumberText == null) return;
        if (floorIndex < 0 || floorIndex >= elevator.floors.Count) return;
        floorNumberText.text = elevator.floors[floorIndex].displayNumber.ToString();
    }
}