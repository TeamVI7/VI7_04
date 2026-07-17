using UnityEngine;

public class PlayerElevatorInteractor : MonoBehaviour
{
    [Header("Tham chiếu")]
    [Tooltip("Để trống sẽ tự lấy Camera.main")]
    public Camera playerCamera;

    [Header("Cài đặt")]
    public float interactDistance = 3f;
    public KeyCode interactKey = KeyCode.E;
    [Tooltip("Layer của các nút thang máy (để trống = quét tất cả layer)")]
    public LayerMask interactLayerMask = ~0;

    [Header("Hint UI (tùy chọn)")]
    [Tooltip("Object hiện chữ 'Nhấn E để chọn' khi đang nhìn vào 1 nút hợp lệ")]
    public GameObject interactHintUI;

    private ElevatorFloorButtonLookable currentLookedButton;

    void Start()
    {
        if (playerCamera == null) playerCamera = Camera.main;
        if (interactHintUI) interactHintUI.SetActive(false);
    }

    void Update()
    {
        UpdateLookTarget();

        if (currentLookedButton != null && currentLookedButton.IsSelectable
            && Input.GetKeyDown(interactKey))
        {
            currentLookedButton.Select();
        }
    }

    void UpdateLookTarget()
    {
        ElevatorFloorButtonLookable hitButton = null;

        if (playerCamera != null)
        {
            Ray ray = new Ray(playerCamera.transform.position, playerCamera.transform.forward);
            if (Physics.Raycast(ray, out RaycastHit hit, interactDistance, interactLayerMask))
            {
                hitButton = hit.collider.GetComponent<ElevatorFloorButtonLookable>();
            }
        }

        if (hitButton != currentLookedButton)
        {
            if (currentLookedButton != null) currentLookedButton.SetHighlighted(false);
            currentLookedButton = hitButton;
            if (currentLookedButton != null) currentLookedButton.SetHighlighted(true);
        }

        if (interactHintUI)
            interactHintUI.SetActive(currentLookedButton != null && currentLookedButton.IsSelectable);
    }
}