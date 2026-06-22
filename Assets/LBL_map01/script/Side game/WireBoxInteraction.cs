using UnityEngine;

/// <summary>
/// Nhìn vào hộp điện + bấm E → mở UI nối dây (Screen Space, không cần camera riêng).
/// Tương tự ComputerInteraction nhưng đơn giản hơn vì UI là Screen Space Overlay/Camera,
/// không cần World Space Canvas, không cần switch camera.
///
/// SETUP: Gắn script này vào GameObject "hộp điện" có Collider (isTrigger không quan trọng,
/// vì dùng raycast chứ không dùng trigger).
/// </summary>
public class WireBoxInteraction : MonoBehaviour
{
    [Header("Raycast detect (camera player)")]
    [SerializeField] private Transform playerCameraTransform;
    [SerializeField] private float     interactionDistance = 2.5f;
    [SerializeField] private LayerMask interactableLayer;

    [Header("References")]
    [Tooltip("Canvas/Panel chứa UI nối dây (Screen Space).")]
    [SerializeField] private GameObject       wirePuzzleUIRoot;
    [SerializeField] private WirePuzzleManager puzzleManager;
    [SerializeField] private UIInputBlocker    inputBlocker;

    [Header("Khoá tương tác computer minigame cho tới khi xong dây")]
    [Tooltip("Kéo ComputerInteraction vào đây. Script này sẽ tự enable/disable nó.")]
    [SerializeField] private MonoBehaviour computerInteractionToLock;

    [Tooltip("Kéo các đèn Morse vào đây để giữ chúng ở trạng thái tắt/idle cho tới khi xong dây.")]
    [SerializeField] private MorseLightController[] morseLightsToActivate;

    [Tooltip("Hoặc dùng Sequencer thay vì từng đèn riêng (nếu có dùng MorseLightSequencer).")]
    [SerializeField] private MorseLightSequencer morseSequencerToActivate;

    public static bool UIOpen { get; private set; } = false;
    public static bool WireBoxSolved { get; private set; } = false;

    private bool   _isInteracting = false;
    private bool   _solved        = false;

    private void Start()
    {
        if (wirePuzzleUIRoot != null)
            wirePuzzleUIRoot.SetActive(false);

        if (playerCameraTransform == null && Camera.main != null)
            playerCameraTransform = Camera.main.transform;

        // Khoá computer minigame ngay từ đầu cho tới khi nối dây xong
        SetComputerLocked(true);

        // Đảm bảo đèn Morse ở trạng thái idle/tắt ngay từ đầu.
        // QUAN TRỌNG: nếu dùng morseSequencerToActivate, hãy tick "activateOnStart = false"
        // trong Inspector của MorseLightSequencer đó — KHÔNG dựa vào việc set enabled=false ở đây,
        // vì Sequencer tự StartCoroutine() trong Start() của chính nó bất kể enabled.
        if (morseLightsToActivate != null)
        {
            foreach (var light in morseLightsToActivate)
            {
                if (light == null) continue;
                light.StopMorse(); // về trạng thái idle (tắt/đỏ tối)
                light.enabled = false;
            }
        }

        if (puzzleManager != null)
            puzzleManager.OnPuzzleCompleted += HandleSolved;
    }

    private void OnDestroy()
    {
        if (puzzleManager != null)
            puzzleManager.OnPuzzleCompleted -= HandleSolved;
    }

    private void Update()
    {
        if (_solved) return; // đã giải xong thì không cần tương tác hộp điện nữa

        if (Input.GetKeyDown(KeyCode.E))
        {
            if (_isInteracting) ExitWireBox();
            else                TryInteract();
        }

        if (_isInteracting && Input.GetKeyDown(KeyCode.Escape))
            ExitWireBox();
    }

    private void TryInteract()
    {
        if (playerCameraTransform == null) return;
        Ray ray = new Ray(playerCameraTransform.position, playerCameraTransform.forward);
        if (Physics.Raycast(ray, out RaycastHit hit, interactionDistance, interactableLayer))
            EnterWireBox();
    }

    private void EnterWireBox()
    {
        _isInteracting = true;
        UIOpen          = true;

        if (wirePuzzleUIRoot != null)
            wirePuzzleUIRoot.SetActive(true);

        if (puzzleManager != null)
            puzzleManager.ResetPuzzle();

        // UI Screen Space -> chỉ cần mở cursor + tắt input gameplay,
        // KHÔNG cần tắt collider 3D hay đổi camera (khác ComputerInteraction)
        inputBlocker?.BlockInput();
    }

    public void ExitWireBox()
    {
        _isInteracting = false;
        UIOpen          = false;

        if (wirePuzzleUIRoot != null)
            wirePuzzleUIRoot.SetActive(false);

        inputBlocker?.UnblockInput();
    }

    private void HandleSolved()
    {
        _solved        = true;
        WireBoxSolved  = true;

        // Mở khoá computer minigame
        SetComputerLocked(false);

        // Bật đèn Morse (kích hoạt minigame chính)
        if (morseSequencerToActivate != null)
            morseSequencerToActivate.BeginSequence();

        if (morseLightsToActivate != null)
        {
            foreach (var light in morseLightsToActivate)
            {
                if (light == null) continue;
                light.enabled = true;
                light.PlayMessage(light.messageToEncode);
            }
        }

        // Tự thoát UI sau một nhịp ngắn cho người chơi thấy kết quả
        Invoke(nameof(ExitWireBox), 1.0f);
    }

    private void SetComputerLocked(bool locked)
    {
        if (computerInteractionToLock != null)
            computerInteractionToLock.enabled = !locked;
    }

    private void OnDrawGizmosSelected()
    {
        if (playerCameraTransform == null) return;
        Gizmos.color = Color.yellow;
        Gizmos.DrawRay(playerCameraTransform.position,
                       playerCameraTransform.forward * interactionDistance);
    }
}