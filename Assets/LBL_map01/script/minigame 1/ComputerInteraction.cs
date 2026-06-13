using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// Nhìn vào computer + bấm E → mở UI World Space.
/// Click chuột hoạt động nhờ WorldSpaceUISetup + PhysicsRaycaster.
/// </summary>
public class ComputerInteraction : MonoBehaviour
{
    [Header("Raycast")]
    [SerializeField] private Transform  playerCameraTransform;
    [SerializeField] private float      interactionDistance = 2.5f;
    [SerializeField] private LayerMask  interactableLayer;

    [Header("References")]
    [SerializeField] private Canvas               minigameCanvas;
    [SerializeField] private MorseMinigameManager gameManager;
    [SerializeField] private UIInputBlocker       inputBlocker;

    public static bool UIOpen { get; private set; } = false;

    private bool _isInteracting = false;
    private bool _solved        = false;
    private Camera _cam;

    private void Start()
    {
        if (minigameCanvas != null)
            minigameCanvas.gameObject.SetActive(false);

        if (playerCameraTransform == null && Camera.main != null)
            playerCameraTransform = Camera.main.transform;

        _cam = playerCameraTransform != null
            ? playerCameraTransform.GetComponent<Camera>()
            : Camera.main;

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

        if (minigameCanvas != null)
        {
            minigameCanvas.gameObject.SetActive(true);

            // Gán lại worldCamera mỗi lần mở (đề phòng camera thay đổi)
            if (_cam != null) minigameCanvas.worldCamera = _cam;
        }

        inputBlocker?.BlockInput();
        gameManager?.StartNewRound();
    }

    public void ExitComputer()
    {
        _isInteracting = false;
        UIOpen         = false;

        if (minigameCanvas != null) minigameCanvas.gameObject.SetActive(false);
        inputBlocker?.UnblockInput();
    }

    private void HandleSolved()
    {
        _solved = true;
        ExitComputer();
    }

    private void OnDrawGizmosSelected()
    {
        if (playerCameraTransform == null) return;
        Gizmos.color = Color.cyan;
        Gizmos.DrawRay(playerCameraTransform.position,
                       playerCameraTransform.forward * interactionDistance);
    }
}