using UnityEngine;

public class MouseLook : MonoBehaviour
{
    public float sensitivity = 2f;
    public Transform playerBody;   // drag the Player root GO here

    private float _pitch;

    void Start()
    {
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible   = false;
    }

    void Update()
    {
        float mouseX = Input.GetAxisRaw("Mouse X") * sensitivity;
        float mouseY = Input.GetAxisRaw("Mouse Y") * sensitivity;

        // Rotate player body left/right
        playerBody.Rotate(Vector3.up * mouseX);

        // Tilt camera up/down, clamped
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