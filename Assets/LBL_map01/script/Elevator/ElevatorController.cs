using UnityEngine;

public class ElevatorController : MonoBehaviour
{
    public enum Floor { Ground = 0, Underground = 1 }
    public enum State { Idle, OpeningDoor, WaitingForGo, ClosingDoor, ClosingDoor_Timeout, OpeningDoor_ThenGo, Moving }

    [Header("Tầng")]
    public Transform groundFloorPos;
    public Transform undergroundFloorPos;

    [Header("Cửa")]
    public ElevatorDoor doorLeft;
    public ElevatorDoor doorRight;

    [Header("Hint UI")]
    public GameObject hintInsideUI;
    public GameObject hintOutsideUI;

    [Header("Cài đặt")]
    public float moveSpeed = 2f;
    public float doorOpenWaitTime = 5f; // tăng lên 5 giây

    public KeyCode callKey = KeyCode.F;
    public KeyCode goKey = KeyCode.G;

    private State state = State.Idle;
    private Floor currentFloor = Floor.Ground;
    private Floor targetFloor;
    private float waitTimer = 0f;
    private Transform player;

    void Start()
    {
        transform.position = groundFloorPos.position;

        GameObject p = GameObject.FindWithTag("Player");
        if (p != null) player = p.transform;

        if (hintInsideUI)  hintInsideUI.SetActive(false);
        if (hintOutsideUI) hintOutsideUI.SetActive(false);
    }

    void Update()
    {
        UpdateHints();

        switch (state)
        {
            case State.Idle:
                if (Input.GetKeyDown(callKey))
                {
                    OpenDoors();
                    state = State.OpeningDoor;
                }
                break;

            case State.OpeningDoor:
                if (doorLeft.IsFullyOpen() && doorRight.IsFullyOpen())
                {
                    waitTimer = doorOpenWaitTime;
                    state = State.WaitingForGo;
                }
                break;

            case State.WaitingForGo:
                // Bấm G -> đi luôn
                if (Input.GetKeyDown(goKey))
                {
                    targetFloor = (currentFloor == Floor.Ground)
                        ? Floor.Underground
                        : Floor.Ground;

                    CloseDoors();
                    state = State.ClosingDoor;
                    break;
                }

                waitTimer -= Time.deltaTime;
                if (waitTimer <= 0f)
                {
                    if (!IsPlayerInsideElevator())
                    {
                        CloseDoors();
                        state = State.ClosingDoor_Timeout;
                    }
                    else
                    {
                        // Player trong thang, tiếp tục chờ
                        waitTimer = doorOpenWaitTime;
                    }
                }
                break;

            case State.ClosingDoor_Timeout:
                // Bấm G khi cửa đang tự đóng -> mở lại rồi đi
                if (Input.GetKeyDown(goKey))
                {
                    targetFloor = (currentFloor == Floor.Ground)
                        ? Floor.Underground
                        : Floor.Ground;

                    OpenDoors();
                    state = State.OpeningDoor_ThenGo;
                    break;
                }

                if (doorLeft.IsClosed() && doorRight.IsClosed())
                    state = State.Idle;
                break;

            case State.OpeningDoor_ThenGo:
                // Chờ cửa mở hẳn rồi đóng lại để đi
                if (doorLeft.IsFullyOpen() && doorRight.IsFullyOpen())
                {
                    CloseDoors();
                    state = State.ClosingDoor;
                }
                break;

            case State.ClosingDoor:
                if (doorLeft.IsClosed() && doorRight.IsClosed())
                    state = State.Moving;
                break;

            case State.Moving:
                Vector3 dest = (targetFloor == Floor.Ground)
                    ? groundFloorPos.position
                    : undergroundFloorPos.position;

                transform.position = Vector3.MoveTowards(
                    transform.position, dest, moveSpeed * Time.deltaTime);

                if (Vector3.Distance(transform.position, dest) < 0.01f)
                {
                    transform.position = dest;
                    currentFloor = targetFloor;
                    OpenDoors();
                    state = State.OpeningDoor;
                }
                break;
        }
    }

    void OpenDoors()
    {
        doorLeft.Open();
        doorRight.Open();
    }

    void CloseDoors()
    {
        doorLeft.Close();
        doorRight.Close();
    }

    bool IsPlayerInsideElevator()
    {
        if (player == null) return false;
        return Vector3.Distance(player.position, transform.position) < 1.5f;
    }

    void UpdateHints()
    {
        if (player == null) return;
        float dist = Vector3.Distance(player.position, transform.position);
        bool near = dist < 3f;

        if (hintOutsideUI)
            hintOutsideUI.SetActive(near && state == State.Idle);

        if (hintInsideUI)
            hintInsideUI.SetActive(near && (state == State.WaitingForGo || state == State.ClosingDoor_Timeout));
    }
}