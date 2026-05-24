using UnityEngine;
 
public class MouseLook : MonoBehaviour
{
    public float sensitivity = 2f;
    public Transform playerBody;
 
    private float _pitch;
 
    void Start()
    {
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible   = false;
    }
 
    void Update()
    {
        // ── Khi UI minigame đang mở → dừng hết, không xoay camera ──
        if (ComputerInteraction.UIOpen)
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible   = true;
            return;
        }
 
        // ── Bình thường → khoá cursor, xoay camera ───────────────
        float mouseX = Input.GetAxisRaw("Mouse X") * sensitivity;
        float mouseY = Input.GetAxisRaw("Mouse Y") * sensitivity;
 
        playerBody.Rotate(Vector3.up * mouseX);
 
        _pitch -= mouseY;
        _pitch  = Mathf.Clamp(_pitch, -85f, 85f);
        transform.localRotation = Quaternion.Euler(_pitch, 0f, 0f);
 
        // Re-lock on click after Escape
        if (Input.GetKeyDown(KeyCode.Escape))
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible   = true;
        }
        if (Input.GetMouseButtonDown(0) && Cursor.lockState != CursorLockMode.Locked)
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible   = false;
        }
    }
}