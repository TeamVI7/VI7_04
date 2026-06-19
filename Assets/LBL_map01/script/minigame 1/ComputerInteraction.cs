using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// Nhìn vào computer + bấm E → mở UI World Space.
/// Khi mở, chuyển sang minigameCamera riêng thay vì dùng camera player.
/// Click chuột hoạt động nhờ WorldSpaceUISetup + PhysicsRaycaster.
/// </summary>
public class ComputerInteraction : MonoBehaviour
{
    [Header("Raycast (dùng camera player để detect)")]
    [SerializeField] private Transform  playerCameraTransform;
    [SerializeField] private float      interactionDistance = 2.5f;
    [SerializeField] private LayerMask  interactableLayer;

    [Header("Minigame Camera")]
    [Tooltip("Camera riêng của minigame. Nếu để trống sẽ fallback về camera player.")]
    [SerializeField] private Camera minigameCamera;

    [Header("References")]
    [SerializeField] private Canvas               minigameCanvas;
    [SerializeField] private MorseMinigameManager gameManager;
    [SerializeField] private UIInputBlocker       inputBlocker;

    public static bool UIOpen { get; private set; } = false;

    private bool   _isInteracting = false;
    private bool   _solved        = false;
    private Camera _playerCam;

    private void Start()
    {
        if (minigameCanvas != null)
            minigameCanvas.gameObject.SetActive(false);

        if (playerCameraTransform == null && Camera.main != null)
            playerCameraTransform = Camera.main.transform;

        _playerCam = playerCameraTransform != null
            ? playerCameraTransform.GetComponent<Camera>()
            : Camera.main;

        // Tắt minigame camera ngay từ đầu
        if (minigameCamera != null)
            minigameCamera.gameObject.SetActive(false);

        if (gameManager != null)
            gameManager.OnPasswordSolved += HandleSolved;
    }

    private void OnDestroy()
    {
        if (gameManager != null)
            gameManager.OnPasswordSolved -= HandleSolved;
    }

    private void Update()
    {
        if (_solved) return;

        if (Input.GetKeyDown(KeyCode.E))
        {
            if (_isInteracting) ExitComputer();
            else                TryInteract();
        }

        if (_isInteracting && Input.GetKeyDown(KeyCode.Escape))
            ExitComputer();
    }

    // Dùng camera player để raycast detect (player chưa vào UI)
    private void TryInteract()
    {
        if (playerCameraTransform == null) return;
        Ray ray = new Ray(playerCameraTransform.position, playerCameraTransform.forward);
        if (Physics.Raycast(ray, out RaycastHit hit, interactionDistance, interactableLayer))
            EnterComputer();
    }

    private void EnterComputer()
    {
        _isInteracting = true;
        UIOpen         = true;

        // Bật minigame camera, tắt player camera
        Camera activeCam = GetMinigameCamera();
        SetPlayerCameraActive(false);
        if (minigameCamera != null)
            minigameCamera.gameObject.SetActive(true);

        if (minigameCanvas != null)
        {
            minigameCanvas.gameObject.SetActive(true);
            // Gán Event Camera là minigame camera
            minigameCanvas.worldCamera = activeCam;
        }

        // Đồng bộ WorldSpaceUISetup nếu có
        var wsSetup = minigameCanvas != null
            ? minigameCanvas.GetComponent<WorldSpaceUISetup>()
            : null;
        if (wsSetup != null)
            wsSetup.SwitchCamera(activeCam);

        inputBlocker?.BlockInput();
        gameManager?.StartNewRound();
    }

    public void ExitComputer()
    {
        _isInteracting = false;
        UIOpen         = false;

        if (minigameCanvas != null)
            minigameCanvas.gameObject.SetActive(false);

        // Tắt minigame camera, bật lại player camera
        if (minigameCamera != null)
            minigameCamera.gameObject.SetActive(false);
        SetPlayerCameraActive(true);

        inputBlocker?.UnblockInput();
    }

    private void HandleSolved()
    {
        _solved = true;
        ExitComputer();
    }

    /// <summary>
    /// Trả về minigameCamera nếu có, fallback về player camera.
    /// </summary>
    private Camera GetMinigameCamera()
    {
        return (minigameCamera != null) ? minigameCamera : _playerCam;
    }

    private void SetPlayerCameraActive(bool active)
    {
        if (_playerCam != null)
            _playerCam.gameObject.SetActive(active);
    }

    private void OnDrawGizmosSelected()
    {
        if (playerCameraTransform == null) return;
        Gizmos.color = Color.cyan;
        Gizmos.DrawRay(playerCameraTransform.position,
                       playerCameraTransform.forward * interactionDistance);
    }
}