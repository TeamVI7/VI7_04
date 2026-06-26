using System;
using System.Collections.Generic;
using UnityEngine;

public class ElevatorController : MonoBehaviour
{
    public enum State { Idle, OpeningDoor, WaitingForSelection, ClosingDoor, Moving }

    [Header("Danh sách tầng")]
    [Tooltip("Thêm/sửa tầng tại đây. Index trong list = ID của tầng, dùng để chọn tầng")]
    public List<ElevatorFloorData> floors = new List<ElevatorFloorData>()
    {
        new ElevatorFloorData{ floorName = "Tầng trệt", displayNumber = 0, floorY = 0f,  isLocked = false },
        new ElevatorFloorData{ floorName = "Hầm",       displayNumber = -1, floorY = -5f, isLocked = false },
    };

    [Header("Cửa")]
    public ElevatorDoor doorLeft;
    public ElevatorDoor doorRight;

    [Header("Hint UI")]
    public GameObject hintInsideUI;

    [Header("Cài đặt")]
    public float moveSpeed = 2f;
    public float doorOpenWaitTime = 8f;

    [Header("Âm thanh")]
    public AudioClip moveSound;
    public AudioClip arriveSound;
    public AudioClip deniedSound; // tiếng báo từ chối khi không đủ thẻ
    public AudioSource audioSource;

    // ===== Events cho UI (màn hình chỉ hướng + số tầng, panel chọn tầng) =====
    public event Action<int> OnFloorSelectionOpened;     // gọi khi state vào WaitingForSelection (kèm currentFloorIndex)
    public event Action OnFloorSelectionClosed;           // gọi khi rời WaitingForSelection
    public event Action<int> OnFloorSelected;             // tầng được chọn thành công
    public event Action<int> OnAccessDenied;               // chọn tầng bị khóa mà không đủ thẻ
    public event Action<bool> OnMoveStarted;               // true = đang đi lên, false = đang đi xuống
    public event Action<int, bool> OnPassingFloor;         // (index tầng gần nhất hiện tại, đang đi lên hay xuống) - cập nhật liên tục khi di chuyển
    public event Action<int> OnArrivedFloor;               // tầng đã đến

    private State state = State.Idle;
    private int currentFloorIndex = 0;
    private int targetFloorIndex = 0;
    private bool hasTarget = false;
    private float waitTimer = 0f;
    private bool playerInsideElevator = false;
    private PlayerCardHolder currentCardHolder;
    private int lastReportedPassingFloor = -1;

    void Start()
    {
        if (floors.Count == 0)
        {
            Debug.LogError("ElevatorController: chưa có tầng nào trong danh sách floors!", this);
            return;
        }

        SetFloorY(floors[currentFloorIndex].floorY);

        if (hintInsideUI) hintInsideUI.SetActive(false);

        if (audioSource == null)
            audioSource = GetComponent<AudioSource>() ?? gameObject.AddComponent<AudioSource>();

        audioSource.loop = false;
        audioSource.playOnAwake = false;
    }

    void OnTriggerEnter(Collider other)
    {
        if (!other.CompareTag("Player")) return;
        playerInsideElevator = true;
        currentCardHolder = other.GetComponent<PlayerCardHolder>();
    }

    void OnTriggerExit(Collider other)
    {
        if (!other.CompareTag("Player")) return;
        playerInsideElevator = false;
        currentCardHolder = null;
    }

    void Update()
    {
        UpdateHints();

        switch (state)
        {
            case State.Idle:
                break;

            case State.OpeningDoor:
                if (doorLeft.IsFullyOpen() && doorRight.IsFullyOpen())
                {
                    waitTimer = doorOpenWaitTime;
                    hasTarget = false;
                    state = State.WaitingForSelection;
                    OnFloorSelectionOpened?.Invoke(currentFloorIndex);
                }
                break;

            case State.WaitingForSelection:
                // Việc chọn tầng đến từ UI gọi TrySelectFloor(index), không cần xử lý input ở đây
                waitTimer -= Time.deltaTime;
                if (waitTimer <= 0f)
                {
                    if (!playerInsideElevator)
                    {
                        OnFloorSelectionClosed?.Invoke();
                        CloseDoors();
                        state = State.ClosingDoor;
                    }
                    else
                    {
                        // người chơi vẫn còn trong cabin, chưa chọn tầng -> reset hẹn giờ, đứng chờ tiếp
                        waitTimer = doorOpenWaitTime;
                    }
                }
                break;

            case State.ClosingDoor:
                if (doorLeft.IsClosed() && doorRight.IsClosed())
                {
                    if (hasTarget)
                    {
                        StartMoving();
                        state = State.Moving;
                    }
                    else
                    {
                        state = State.Idle;
                    }
                }
                break;

            case State.Moving:
                {
                    float destY = floors[targetFloorIndex].floorY;
                    bool isGoingUp = destY > transform.position.y;

                    float newY = Mathf.MoveTowards(transform.position.y, destY, moveSpeed * Time.deltaTime);
                    transform.position = new Vector3(transform.position.x, newY, transform.position.z);

                    ReportPassingFloor(isGoingUp);

                    if (Mathf.Abs(transform.position.y - destY) < 0.01f)
                    {
                        SetFloorY(destY);
                        currentFloorIndex = targetFloorIndex;
                        hasTarget = false;
                        StopMoving();
                        PlayOneShot(arriveSound);
                        OnArrivedFloor?.Invoke(currentFloorIndex);
                        OpenDoors();
                        state = State.OpeningDoor;
                    }
                }
                break;
        }
    }

    // ===== Gọi từ ElevatorProximityZone =====
    public void OnPlayerEnterZone()
    {
        if (state == State.Idle)
        {
            OpenDoors();
            state = State.OpeningDoor;
        }
    }

    public void OnPlayerExitZone()
    {
        // cửa tự đóng theo timer, không cần làm gì
    }

    // ===== Gọi từ UI chọn tầng =====
    // floorIndex là vị trí trong list "floors". Trả về true nếu chọn thành công.
    public bool TrySelectFloor(int floorIndex)
    {
        if (state != State.WaitingForSelection) return false;
        if (floorIndex < 0 || floorIndex >= floors.Count) return false;
        if (floorIndex == currentFloorIndex) return false;

        if (!CanAccessFloor(floorIndex))
        {
            PlayOneShot(deniedSound);
            OnAccessDenied?.Invoke(floorIndex);
            return false;
        }

        targetFloorIndex = floorIndex;
        hasTarget = true;
        OnFloorSelectionClosed?.Invoke();
        OnFloorSelected?.Invoke(floorIndex);
        CloseDoors();
        state = State.ClosingDoor;
        return true;
    }

    // Tầng có khóa và người chơi hiện tại có đủ thẻ để mở không
    public bool CanAccessFloor(int floorIndex)
    {
        if (floorIndex < 0 || floorIndex >= floors.Count) return false;
        var floor = floors[floorIndex];
        if (!floor.isLocked) return true;
        if (string.IsNullOrEmpty(floor.requiredCardId)) return true;
        return currentCardHolder != null && currentCardHolder.HasCard(floor.requiredCardId);
    }

    public State GetState() => state;
    public int GetCurrentFloorIndex() => currentFloorIndex;
    public bool IsPlayerInside() => playerInsideElevator;

    void SetFloorY(float y) =>
        transform.position = new Vector3(transform.position.x, y, transform.position.z);

    void OpenDoors()  { doorLeft.Open();  doorRight.Open();  }
    void CloseDoors() { doorLeft.Close(); doorRight.Close(); }

    void StartMoving()
    {
        lastReportedPassingFloor = -1;
        bool isGoingUp = floors[targetFloorIndex].floorY > transform.position.y;
        OnMoveStarted?.Invoke(isGoingUp);

        if (moveSound != null && audioSource != null)
        {
            audioSource.clip = moveSound;
            audioSource.loop = true;
            audioSource.Play();
        }
    }

    void StopMoving()
    {
        if (audioSource != null && audioSource.isPlaying)
        {
            audioSource.Stop();
            audioSource.loop = false;
        }
    }

    void PlayOneShot(AudioClip clip)
    {
        if (clip != null && audioSource != null)
            audioSource.PlayOneShot(clip);
    }

    // Trong lúc đang di chuyển, tìm tầng có Y gần nhất với vị trí hiện tại
    // để màn hình hiển thị đúng số tầng "đang đi qua"
    void ReportPassingFloor(bool isGoingUp)
    {
        int nearest = currentFloorIndex;
        float bestDist = Mathf.Infinity;
        for (int i = 0; i < floors.Count; i++)
        {
            float dist = Mathf.Abs(transform.position.y - floors[i].floorY);
            if (dist < bestDist)
            {
                bestDist = dist;
                nearest = i;
            }
        }

        if (nearest != lastReportedPassingFloor)
        {
            lastReportedPassingFloor = nearest;
            OnPassingFloor?.Invoke(nearest, isGoingUp);
        }
    }

    void UpdateHints()
    {
        if (hintInsideUI)
            hintInsideUI.SetActive(playerInsideElevator && state == State.WaitingForSelection);
    }
}