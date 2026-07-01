using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// Nhìn vào computer + bấm E → mở UI World Space.
/// Khi mở, switch sang minigameCamera riêng.
/// Tự tắt collider 3D chặn UI (kể cả PF_EQP_Control_SmallPanelMonitor).
/// </summary>
public class ComputerInteraction : MonoBehaviour
{
    [Header("Raycast detect (camera player)")]
    [SerializeField] private Transform playerCameraTransform;
    [SerializeField] private float     interactionDistance = 2.5f;
    [SerializeField] private LayerMask interactableLayer;

    [Header("Phím tương tác")]
    [SerializeField] private KeyCode interactKey = KeyCode.F;

    [Header("Minigame Camera")]
    [SerializeField] private Camera minigameCamera;

    [Header("References")]
    [SerializeField] private Canvas               minigameCanvas;
    [SerializeField] private MorseMinigameManager gameManager;
    [SerializeField] private UIInputBlocker       inputBlocker;

    [Header("Colliders cần tắt khi UI mở")]
    [Tooltip("Kéo vào đây TẤT CẢ collider 3D nằm trước Canvas:\n" +
             "- PF_EQP_Control_SmallPanelMonitor\n- Computer\n- Table\n- v.v.\n\n" +
             "Để trống = chỉ tắt children của object này (không đủ nếu monitor là object riêng).")]
    [SerializeField] private Collider[] collidersToDisable;

    [Tooltip("Tự tắt tất cả Collider trong children của Computer object này.")]
    [SerializeField] private bool autoDisableChildColliders = true;

    [Tooltip("Tên chứa chuỗi này sẽ bị tắt collider tự động trong toàn scene.\n" +
             "Mặc định: 'PF_EQP_Control_SmallPanelMonitor'")]
    [SerializeField] private string[] autoFindByNameContains = { "PF_EQP_Control_SmallPanelMonitor" };

    public static bool UIOpen { get; private set; } = false;

    private bool      _isInteracting = false;
    private bool      _solved        = false;
    private Camera    _playerCam;
    private Collider[] _runtimeDisabled; // lưu để bật lại

    private void Start()
    {
        if (minigameCanvas != null)
            minigameCanvas.gameObject.SetActive(false);

        if (playerCameraTransform == null && Camera.main != null)
            playerCameraTransform = Camera.main.transform;

        _playerCam = playerCameraTransform != null
            ? playerCameraTransform.GetComponent<Camera>()
            : Camera.main;

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

        if (Input.GetKeyDown(interactKey))
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

        // Dò lại camera player ĐANG ACTIVE ngay lúc này, phòng khi có script
        // khác (vd WireBoxInteraction) cũng đang toggle chung camera này.
        if (playerCameraTransform != null)
            _playerCam = playerCameraTransform.GetComponent<Camera>();
        if (_playerCam == null)
            _playerCam = Camera.main;

        // Tắt collider TRƯỚC khi mở UI
        DisableColliders();

        if (minigameCamera != null)
            minigameCamera.gameObject.SetActive(true);

        if (_playerCam != null)
            _playerCam.gameObject.SetActive(false);

        if (minigameCanvas != null)
        {
            minigameCanvas.gameObject.SetActive(true);
            Camera activeCam = minigameCamera != null ? minigameCamera : _playerCam;
            minigameCanvas.worldCamera = activeCam;
            EnsureGraphicRaycaster(minigameCanvas);
            EnsureEventSystem(activeCam);
        }

        inputBlocker?.BlockInput();
        gameManager?.StartNewRound();

        // Mở khoá con trỏ chuột để bấm được numpad — QUAN TRỌNG vì minigame
        // này chơi hoàn toàn bằng chuột. PlayerCam bị tắt (SetActive false)
        // nên nó không tự lo cursor được nữa, phải tự set ở đây.
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible   = true;
    }

    public void ExitComputer()
    {
        _isInteracting = false;
        UIOpen         = false;

        if (minigameCanvas != null)
            minigameCanvas.gameObject.SetActive(false);

        if (minigameCamera != null)
            minigameCamera.gameObject.SetActive(false);

        if (_playerCam != null)
            _playerCam.gameObject.SetActive(true);

        // Bật lại collider
        EnableColliders();

        inputBlocker?.UnblockInput();

        // Khoá lại con trỏ chuột về chế độ chơi game (FPS look). PlayerCam
        // vừa được SetActive(true) lại nhưng Start() của nó KHÔNG chạy lại
        // (Unity chỉ gọi Start 1 lần), nên nếu PlayerCam chỉ lock cursor
        // trong Start() thì phải tự khoá lại ở đây, không thì camera "tắc"
        // (chuột không xoay camera được nữa) sau khi thoát computer.
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible   = false;
    }

    private void HandleSolved()
    {
        _solved = true;
        ExitComputer();
    }

    // ─── Collider management ─────────────────────────────────────────

    private void DisableColliders()
    {
        var toDisable = new System.Collections.Generic.List<Collider>();

        // 1. Danh sách thủ công
        if (collidersToDisable != null)
            foreach (var c in collidersToDisable)
                if (c != null && c.enabled) toDisable.Add(c);

        // 2. Children của object này
        if (autoDisableChildColliders)
            foreach (var c in GetComponentsInChildren<Collider>(true))
                if (c.enabled && !toDisable.Contains(c)) toDisable.Add(c);

        // 3. Tìm theo tên trong toàn scene
        if (autoFindByNameContains != null)
        {
            var allColliders = FindObjectsOfType<Collider>(true);
            foreach (var c in allColliders)
            {
                if (!c.enabled) continue;
                foreach (var keyword in autoFindByNameContains)
                {
                    if (!string.IsNullOrEmpty(keyword) &&
                        c.gameObject.name.Contains(keyword) &&
                        !toDisable.Contains(c))
                    {
                        toDisable.Add(c);
                        Debug.Log($"[ComputerInteraction] Auto-found & disabled: '{c.gameObject.name}'");
                        break;
                    }
                }
            }
        }

        _runtimeDisabled = toDisable.ToArray();
        foreach (var c in _runtimeDisabled)
            c.enabled = false;
    }

    private void EnableColliders()
    {
        if (_runtimeDisabled == null) return;
        foreach (var c in _runtimeDisabled)
            if (c != null) c.enabled = true;
        _runtimeDisabled = null;
    }

    // ─── Raycaster / EventSystem ─────────────────────────────────────

    private void EnsureGraphicRaycaster(Canvas canvas)
    {
        var existing = canvas.GetComponent<GraphicRaycaster>();
        if (existing != null && !(existing is MinigameCameraRaycaster))
        {
            Destroy(existing);
            canvas.gameObject.AddComponent<MinigameCameraRaycaster>();
        }
        else if (existing == null)
        {
            canvas.gameObject.AddComponent<MinigameCameraRaycaster>();
        }
    }

    private void EnsureEventSystem(Camera cam)
    {
        if (EventSystem.current == null)
        {
            var esGO = new GameObject("EventSystem");
            esGO.AddComponent<EventSystem>();
            esGO.AddComponent<StandaloneInputModule>();
        }

        if (cam != null && cam.GetComponent<PhysicsRaycaster>() == null)
            cam.gameObject.AddComponent<PhysicsRaycaster>();
    }

    private void OnDrawGizmosSelected()
    {
        if (playerCameraTransform == null) return;
        Gizmos.color = Color.cyan;
        Gizmos.DrawRay(playerCameraTransform.position,
                       playerCameraTransform.forward * interactionDistance);
    }
}