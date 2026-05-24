using UnityEngine;

[DisallowMultipleComponent]
public class PlayerStateDisplay : MonoBehaviour
{
    [Header("References")]
    public PlayerMovement playerMovement;
    public Rigidbody rb;

    [Header("Display")]
    public bool showOverlay = true;
    public KeyCode toggleOverlayKey = KeyCode.F1;
    public Color textColor = Color.white;
    public int fontSize = 16;

    private GUIStyle _labelStyle;

    public Vector3 CurrentVelocity => rb ? rb.linearVelocity : Vector3.zero;
    public float CurrentSpeed => CurrentVelocity.magnitude;
    public float CurrentFlatSpeed => new Vector3(CurrentVelocity.x, 0f, CurrentVelocity.z).magnitude;
    public float VerticalSpeed => CurrentVelocity.y;
    public string CurrentState => playerMovement ? playerMovement.state.ToString() : "None";
    public bool IsGrounded => playerMovement ? playerMovement.grounded : false;

    private void Awake()
    {
        if (!playerMovement)
            playerMovement = GetComponent<PlayerMovement>();

        if (!rb)
            rb = GetComponent<Rigidbody>();

        _labelStyle = new GUIStyle
        {
            normal = { textColor = textColor },
            fontSize = fontSize,
            richText = false
        };
    }

    private void Update()
    {
        if (Input.GetKeyDown(toggleOverlayKey))
            showOverlay = !showOverlay;
    }

    private void OnGUI()
    {
        if (!showOverlay) return;

        _labelStyle.normal.textColor = textColor;

        float x = 12f;
        float y = 12f;
        float lineHeight = fontSize + 6f;
        GUI.Label(new Rect(x, y, 500f, lineHeight), $"State: {CurrentState}", _labelStyle);
        GUI.Label(new Rect(x, y += lineHeight, 500f, lineHeight), $"Grounded: {IsGrounded}", _labelStyle);
        GUI.Label(new Rect(x, y += lineHeight, 500f, lineHeight), $"Velocity: {CurrentVelocity:F2}", _labelStyle);
        GUI.Label(new Rect(x, y += lineHeight, 500f, lineHeight), $"Speed: {CurrentSpeed:F2}", _labelStyle);
        GUI.Label(new Rect(x, y += lineHeight, 500f, lineHeight), $"Flat Speed: {CurrentFlatSpeed:F2}", _labelStyle);
        GUI.Label(new Rect(x, y += lineHeight, 500f, lineHeight), $"Vertical Speed: {VerticalSpeed:F2}", _labelStyle);
    }
}
