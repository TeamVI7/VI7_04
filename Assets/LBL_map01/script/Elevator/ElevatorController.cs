using UnityEngine;

public class ElevatorController : MonoBehaviour
{
    public enum Floor { Ground = 0, Underground = 1 }
    public enum State { Idle, OpeningDoor, WaitingForGo, ClosingDoor, ClosingDoor_Timeout, OpeningDoor_ThenGo, Moving }

    [Header("Tầng - chỉ cần nhập số Y")]
    public float groundFloorY = 0f;
    public float undergroundFloorY = -5f;

    [Header("Cửa")]
    public ElevatorDoor doorLeft;
    public ElevatorDoor doorRight;

    [Header("Hint UI")]
    public GameObject hintInsideUI;
    public GameObject hintOutsideUI;

    [Header("Cài đặt")]
    public float moveSpeed = 2f;
    public float doorOpenWaitTime = 5f;

    public KeyCode callKey = KeyCode.F;
    public KeyCode goKey = KeyCode.G;

    private State state = State.Idle;
    private Floor currentFloor = Floor.Ground;
    private Floor targetFloor;
    private float waitTimer = 0f;
    private Transform player;

    private bool playerInsideElevator = false;

    void Start()
    {
        SetFloorY(groundFloorY);

        GameObject p = GameObject.FindWithTag("Player");
        if (p != null) player = p.transform;

        if (hintInsideUI)  hintInsideUI.SetActive(false);
        if (hintOutsideUI) hintOutsideUI.SetActive(false);
    }

    void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag("Player"))
            playerInsideElevator = true;
    }

    void OnTriggerExit(Collider other)
    {
        if (other.CompareTag("Player"))
            playerInsideElevator = false;
    }

    void Update()
    {
        UpdateHints();

        switch (state)
        {
            case State.Idle:
                // Bấm F mở cửa, không cần điều kiện gì thêm
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
                // Chỉ cho bấm G khi player đang TRONG cabin
                if (Input.GetKeyDown(goKey) && playerInsideElevator)
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
                    if (!playerInsideElevator)
                    {
                        CloseDoors();
                        state = State.ClosingDoor_Timeout;
                    }
                    else
                    {
                        waitTimer = doorOpenWaitTime;
                    }
                }
                break;

            case State.ClosingDoor_Timeout:
                if (Input.GetKeyDown(goKey) && playerInsideElevator)
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
                float destY = (targetFloor == Floor.Ground)
                    ? groundFloorY
                    : undergroundFloorY;

                float newY = Mathf.MoveTowards(
                    transform.position.y, destY, moveSpeed * Time.deltaTime);

                transform.position = new Vector3(
                    transform.position.x,
                    newY,
                    transform.position.z);

                if (Mathf.Abs(transform.position.y - destY) < 0.01f)
                {
                    SetFloorY(destY);
                    currentFloor = targetFloor;
                    OpenDoors();
                    state = State.OpeningDoor;
                }
                break;
        }
    }

    void SetFloorY(float y)
    {
        transform.position = new Vector3(
            transform.position.x,
            y,
            transform.position.z);
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

    void UpdateHints()
    {
        if (player == null) return;

        float dist = Vector3.Distance(player.position, transform.position);
        bool nearElevator = dist < 3f;

        // Hint F: hiện khi đứng gần, chưa vào trong, thang đang Idle
        if (hintOutsideUI)
            hintOutsideUI.SetActive(nearElevator && !playerInsideElevator && state == State.Idle);

        // Hint G: chỉ hiện khi đang TRONG cabin
        if (hintInsideUI)
            hintInsideUI.SetActive(
                playerInsideElevator &&
                (state == State.WaitingForGo || state == State.ClosingDoor_Timeout));
    }
}