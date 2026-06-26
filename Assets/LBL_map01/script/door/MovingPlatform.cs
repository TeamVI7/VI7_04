using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Platform di chuyển giữa các điểm là GameObject (waypoint) thay vì
/// nhập tay tọa độ Vector3 -> kéo thả GameObject vào Inspector là xong.
/// 
/// CÁCH CÀI ĐẶT:
/// 1. Tạo các GameObject rỗng (Empty) đặt tại các vị trí mong muốn, đặt tên
///    ví dụ: Point_A, Point_B, Point_C... (không cần Mesh/Collider gì cả).
/// 2. Gắn script này vào Platform (object cần di chuyển, phải có Collider + Rigidbody nếu cần va chạm).
/// 3. Kéo các GameObject điểm trên vào mảng "Waypoints" theo đúng thứ tự di chuyển.
/// 4. Tùy chọn Mode: Loop (lặp vòng), PingPong (đi tới rồi quay lại), Once (chạy 1 lần rồi dừng).
/// 5. (Tùy chọn) Nếu muốn player đứng trên platform bị kéo theo, gắn thêm
///    component "PlatformPassenger" (bên dưới) cho Player, hoặc dùng cơ chế Parent ở đây.
/// </summary>
[AddComponentMenu("Custom/Moving Platform")]
public class MovingPlatform : MonoBehaviour
{
    public enum PlatformMode { Loop, PingPong, Once }

    [Header("Waypoints (kéo GameObject vào đây theo thứ tự)")]
    [Tooltip("Danh sách các GameObject làm điểm mốc cho platform di chuyển qua")]
    public List<Transform> waypoints = new List<Transform>();

    [Header("Thông số di chuyển")]
    [Tooltip("Tốc độ di chuyển (đơn vị/giây)")]
    public float speed = 2f;

    [Tooltip("Thời gian dừng lại tại mỗi điểm (giây)")]
    public float waitTimeAtPoint = 0f;

    [Tooltip("Cách di chuyển: Loop = lặp vòng A->B->C->A...; PingPong = A->B->C->B->A...; Once = chạy 1 lần rồi dừng")]
    public PlatformMode mode = PlatformMode.Loop;

    [Tooltip("Bắt đầu di chuyển ngay khi Play")]
    public bool autoStart = true;

    [Header("Hành khách (đẩy player/object đứng trên theo)")]
    [Tooltip("Nếu bật, các object có tag dưới đây đứng trên platform sẽ bị kéo theo")]
    public bool carryPassengers = true;
    public string passengerTag = "Player";

    private int currentIndex = 0;
    private int direction = 1; // dùng cho PingPong
    private float waitTimer = 0f;
    private bool isMoving = false;
    private bool finished = false;
    private Vector3 lastPosition;

    void Start()
    {
        if (waypoints.Count < 2)
        {
            Debug.LogWarning($"[MovingPlatform] '{name}' cần ít nhất 2 waypoint để di chuyển.");
            return;
        }

        // Đưa platform về đúng vị trí điểm đầu tiên khi bắt đầu
        transform.position = waypoints[0].position;
        lastPosition = transform.position;
        isMoving = autoStart;
    }

    void Update()
    {
        if (!isMoving || finished || waypoints.Count < 2) return;

        // Đang chờ tại điểm
        if (waitTimer > 0f)
        {
            waitTimer -= Time.deltaTime;
            return;
        }

        Transform target = waypoints[currentIndex];
        Vector3 newPos = Vector3.MoveTowards(transform.position, target.position, speed * Time.deltaTime);
        transform.position = newPos;

        // Đến điểm đích chưa?
        if (Vector3.Distance(transform.position, target.position) < 0.001f)
        {
            waitTimer = waitTimeAtPoint;
            AdvanceIndex();
        }
    }

    void LateUpdate()
    {
        // Tính độ dịch chuyển trong frame này để đẩy hành khách theo (nếu không dùng Parent)
        if (carryPassengers)
        {
            Vector3 delta = transform.position - lastPosition;
            if (delta != Vector3.zero)
            {
                MovePassengers(delta);
            }
        }
        lastPosition = transform.position;
    }

    void AdvanceIndex()
    {
        switch (mode)
        {
            case PlatformMode.Loop:
                currentIndex = (currentIndex + 1) % waypoints.Count;
                break;

            case PlatformMode.PingPong:
                if (currentIndex + direction >= waypoints.Count || currentIndex + direction < 0)
                {
                    direction *= -1; // đảo hướng khi chạm 2 đầu
                }
                currentIndex += direction;
                break;

            case PlatformMode.Once:
                if (currentIndex >= waypoints.Count - 1)
                {
                    finished = true;
                    isMoving = false;
                }
                else
                {
                    currentIndex++;
                }
                break;
        }
    }

    // Đẩy các object đứng trên platform di chuyển cùng (cách đơn giản, không cần Parent)
    void MovePassengers(Vector3 delta)
    {
        Collider[] hits = Physics.OverlapBox(
            transform.position + Vector3.up * 0.5f,
            transform.localScale * 0.5f,
            transform.rotation
        );

        foreach (var hit in hits)
        {
            if (hit.CompareTag(passengerTag))
            {
                hit.transform.position += delta;
            }
        }
    }

    // --- Hàm public để điều khiển platform từ script khác (ví dụ: kích hoạt bằng lever, nút bấm) ---
    public void StartMoving() => isMoving = true;
    public void StopMoving() => isMoving = false;
    public void ResetPlatform()
    {
        currentIndex = 0;
        direction = 1;
        finished = false;
        if (waypoints.Count > 0)
            transform.position = waypoints[0].position;
    }

    // Vẽ đường đi giữa các waypoint trong Scene view để dễ canh chỉnh
    void OnDrawGizmos()
    {
        if (waypoints == null || waypoints.Count < 2) return;

        Gizmos.color = Color.green;
        for (int i = 0; i < waypoints.Count; i++)
        {
            if (waypoints[i] == null) continue;
            Gizmos.DrawSphere(waypoints[i].position, 0.15f);

            int nextIndex = (i + 1) % waypoints.Count;
            if (mode == PlatformMode.Once && nextIndex == 0) continue;
            if (waypoints[nextIndex] != null)
                Gizmos.DrawLine(waypoints[i].position, waypoints[nextIndex].position);
        }
    }
}