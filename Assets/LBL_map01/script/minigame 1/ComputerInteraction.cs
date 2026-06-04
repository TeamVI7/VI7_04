using UnityEngine;
 
public class ComputerInteraction : MonoBehaviour
{
    [Header("Raycast Settings")]
    [SerializeField] private Transform playerCameraTransform;
    [SerializeField] private float interactionDistance = 2.5f;
    [SerializeField] private LayerMask interactableLayer;
 
    [Header("References")]
    [SerializeField] private Canvas               minigameCanvas;
    [SerializeField] private MorseMinigameManager gameManager;
    [SerializeField] private MonoBehaviour        playerMovementScript;
 
    public static bool UIOpen { get; private set; } = false;
 
    private bool _isInteracting = false;
    private bool _solved        = false; // true = cửa đã mở, khoá E vĩnh viễn
 
    private void Start()
    {
        if (minigameCanvas != null)
            minigameCanvas.gameObject.SetActive(false);
 
        if (playerCameraTransform == null && Camera.main != null)
            playerCameraTransform = Camera.main.transform;
 
        // Lắng nghe sự kiện đúng mật khẩu từ GameManager
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
        // Đã giải → không làm gì nữa
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
 
        if (minigameCanvas != null) minigameCanvas.gameObject.SetActive(true);
        if (playerMovementScript != null) playerMovementScript.enabled = false;
 
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible   = true;
 
        gameManager?.StartNewRound();
    }
 
    public void ExitComputer()
    {
        _isInteracting = false;
        UIOpen         = false;
 
        if (minigameCanvas != null) minigameCanvas.gameObject.SetActive(false);
        if (playerMovementScript != null) playerMovementScript.enabled = true;
 
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible   = false;
    }
 
    /// <summary>Gọi bởi GameManager khi đúng mật khẩu và cửa mở.</summary>
    private void HandleSolved()
    {
        _solved = true;
 
        // Đóng UI, trả cursor về tay player
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