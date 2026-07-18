using UnityEngine;
using UnityEngine.UI;
using System.Collections;

// Gắn vào màn hình hiển thị trong cabin (vd: 1 Canvas nhỏ phía trên cửa).
public class ElevatorDisplayUI : MonoBehaviour
{
    [Header("Tham chiếu")]
    public ElevatorController elevator;

    [Header("UI con")]
    public GameObject arrowUp;      // object mũi tên chỉ lên
    public GameObject arrowDown;    // object mũi tên chỉ xuống
    public Text floorNumberText;    // text hiển thị số tầng (đổi sang TMP_Text nếu dùng TextMeshPro)

    [Header("Hiệu ứng chớp")]
    public float blinkInterval = 0.4f;   // thời gian giữa mỗi lần bật/tắt

    private Coroutine blinkUpRoutine;
    private Coroutine blinkDownRoutine;

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
        SetArrowBlinking(arrowUp, up, ref blinkUpRoutine);
        SetArrowBlinking(arrowDown, down, ref blinkDownRoutine);
    }

    void SetArrowBlinking(GameObject arrow, bool shouldBlink, ref Coroutine routine)
    {
        if (arrow == null) return;

        if (shouldBlink)
        {
            if (routine == null)
                routine = StartCoroutine(BlinkRoutine(arrow));
        }
        else
        {
            if (routine != null)
            {
                StopCoroutine(routine);
                routine = null;
            }
            arrow.SetActive(false);
        }
    }

    IEnumerator BlinkRoutine(GameObject arrow)
    {
        while (true)
        {
            arrow.SetActive(true);
            yield return new WaitForSeconds(blinkInterval);
            arrow.SetActive(false);
            yield return new WaitForSeconds(blinkInterval);
        }
    }

    void UpdateFloorText(int floorIndex)
    {
        if (floorNumberText == null) return;
        if (floorIndex < 0 || floorIndex >= elevator.floors.Count) return;
        floorNumberText.text = elevator.floors[floorIndex].displayNumber.ToString();
    }
}