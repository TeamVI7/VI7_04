using UnityEngine;

public class Mouse_Lock : MonoBehaviour
{
    public float mouseSensitivity = 100f;

    [Header("Kéo thả đúng đối tượng vào đây:")]
    public Transform cameraTransform; // Ô kéo thả dành riêng cho Main Camera
    public Transform playerBody;     // Ô kéo thả dành riêng cho Capsule (Player)

    private float xRotation = 0f;
    private float yRotation = 0f; 

    void Start()
    {
        // Khóa con chuột vào giữa màn hình
        Cursor.lockState = CursorLockMode.Locked;

        // Lấy góc xoay ban đầu của nhân vật để tránh bị giật góc nhìn khi bấm Play
        if (playerBody != null)
        {
            yRotation = playerBody.localEulerAngles.y;
        }
        
        // Phòng trường hợp bạn quên không kéo Camera, script sẽ tự lấy đối tượng hiện tại
        if (cameraTransform == null)
        {
            cameraTransform = transform;
        }
    }

    void Update()
    {
        if (playerBody == null || cameraTransform == null) return;

        float mouseX = Input.GetAxis("Mouse X") * mouseSensitivity * Time.deltaTime;
        float mouseY = Input.GetAxis("Mouse Y") * mouseSensitivity * Time.deltaTime;

        // 1. Xoay lên/xuống (Chỉ áp dụng riêng cho đối tượng Camera)
        xRotation -= mouseY;
        xRotation = Mathf.Clamp(xRotation, -90f, 90f);
        cameraTransform.localRotation = Quaternion.Euler(xRotation, 0f, 0f);

        // 2. Xoay trái/phải (Chỉ áp dụng riêng cho đối tượng Thân nhân vật)
        yRotation += mouseX;
        playerBody.localRotation = Quaternion.Euler(0f, yRotation, 0f);
    }
}