using UnityEngine;

public class SimpleMovement : MonoBehaviour
{
    [Header("Cấu hình di chuyển")]
    public float speed = 5.0f;
    public CharacterController controller;

    [Header("Cấu hình Trọng lực & Nhảy")]
    public float gravity = -9.81f;       // Gia tốc trọng lực chuẩn vật lý
    public float jumpHeight = 2.0f;     // Chiều cao bước nhảy (tính bằng mét)

    private Vector3 velocity;           // Biến lưu vận tốc rơi/nhảy riêng
    private bool isGrounded;            // Kiểm tra nhân vật có đang chạm đất không

    void Start()
    {
        // Tự động lấy CharacterController nếu bạn quên kéo thả trong Inspector
        if (controller == null)
        {
            controller = GetComponent<CharacterController>();
        }
    }

    void Update()
    {
        // --- 1. KIỂM TRA MẶT ĐẤT ---
        isGrounded = controller.isGrounded;

        // Nếu đã chạm đất và vận tốc Y đang âm (đang rơi xuống) thì ghì nhẹ nhân vật xuống sàn
        if (isGrounded && velocity.y < 0)
        {
            velocity.y = -2f; // Giữ ở mức -2f để nhân vật đi xuống dốc không bị khựng hoặc nảy góc nhìn
        }


        // --- 2. DI CHUYỂN TRÊN MẶT PHẲNG (CODE CŨ CỦA BẠN) ---
        float moveHorizontal = Input.GetAxis("Horizontal");
        float moveVertical = Input.GetAxis("Vertical");

        Vector3 move = transform.right * moveHorizontal + transform.forward * moveVertical;
        
        // Di chuyển ngang/dọc trước
        controller.Move(move * speed * Time.deltaTime);


        // --- 3. XỬ LÝ LỆNH NHẢY ---
        // Phím nhảy mặc định trong Unity là Space (Phím cách)
        if (Input.GetButtonDown("Jump") && isGrounded)
        {
            // Công thức vật lý chuẩn để tính lực đẩy lên dựa theo chiều cao mong muốn: v = sqrt(h * -2 * g)
            velocity.y = Mathf.Sqrt(jumpHeight * -2f * gravity);
        }


        // --- 4. TÍNH TOÁN TRỌNG LỰC THEO THỜI GIAN ---
        // Vận tốc rơi tăng dần theo thời gian (Rơi tự do)
        velocity.y += gravity * Time.deltaTime;

        // Di chuyển nhân vật theo trục dọc (Lên/Xuống)
        controller.Move(velocity * Time.deltaTime);
    }
}