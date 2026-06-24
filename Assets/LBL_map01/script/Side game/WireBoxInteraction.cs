using System.Collections;
using UnityEngine;

/// <summary>
/// Đứng trong vùng Trigger + bấm E → mở UI nối dây (World Space Canvas với camera riêng).
/// Khi xong: mở khoá computer, bật đèn Morse.
/// </summary>
public class WireBoxInteraction : MonoBehaviour
{
    [Header("Trigger detect")]
    [Tooltip("Tag của Player để nhận vùng trigger. Để trống = chấp nhận mọi object.")]
    [SerializeField] private string playerTag = "Player";

    [Header("World Space Canvas")]
    [Tooltip("Canvas World Space chứa UI nối dây.")]
    [SerializeField] private Canvas wirePuzzleCanvas;

    [Tooltip("Root Panel/GameObject bên trong Canvas (con trực tiếp). SetActive để ẩn/hiện UI.")]
    [SerializeField] private GameObject wirePuzzleUIRoot;

    [Tooltip("Camera riêng nhìn vào bảng nối dây. Tắt sẵn trong scene.")]
    [SerializeField] private Camera wirePuzzleCamera;

    [Header("References")]
    [SerializeField] private WirePuzzleManager  puzzleManager;
    [SerializeField] private UIInputBlocker     inputBlocker;

    [Header("Khoá Computer cho tới khi xong dây")]
    [Tooltip("Kéo COMPONENT ComputerInteraction (không phải GameObject) vào đây.")]
    [SerializeField] private ComputerInteraction computerInteractionToLock;

    [Header("Đèn Morse — bật sau khi xong dây")]
    [SerializeField] private MorseLightController[] morseLightsToActivate;
    [SerializeField] private MorseLightSequencer    morseSequencerToActivate;

    [Header("Camera 'khoe' đèn Morse sau khi giải xong")]
    [Tooltip("Camera đặt sẵn trong scene, nhìn vào cụm đèn Morse. Tắt sẵn từ đầu.")]
    [SerializeField] private Camera morseShowcaseCamera;

    [Tooltip("Đợi bao lâu sau khi giải xong rồi mới chuyển cam qua đèn (giây).")]
    [SerializeField] private float morseShowcaseStartDelay = 0.5f;

    [Tooltip("Giữ camera ở đèn Morse trong bao lâu rồi trả về (giây).")]
    [SerializeField] private float morseShowcaseDuration = 2f;

    [Header("Âm thanh")]
    [Tooltip("AudioSource để phát SFX. Để trống = tự thêm AudioSource lên chính object này.")]
    [SerializeField] private AudioSource sfxSource;

    [Tooltip("Tiếng phát ra khi MỞ hộp điện (bấm E vào, lúc EnterWireBox).")]
    [SerializeField] private AudioClip sfxOpenBox;

    public static bool UIOpen        { get; private set; } = false;
    public static bool WireBoxSolved { get; private set; } = false;

    private bool _isInteracting = false;
    private bool _solved        = false;
    private bool _playerInRange = false;

    private Camera _playerCam; // camera player (tắt khi mở wire UI)

    // ────────────────────────────────────────────────────────────────

    private void Start()
    {
        // Tự thêm AudioSource nếu chưa gán
        if (sfxSource == null)
        {
            sfxSource = GetComponent<AudioSource>();
            if (sfxSource == null)
                sfxSource = gameObject.AddComponent<AudioSource>();
            sfxSource.playOnAwake = false;
        }

        // Tắt canvas ngay từ đầu
        if (wirePuzzleCanvas != null)
            wirePuzzleCanvas.gameObject.SetActive(false);
        if (wirePuzzleUIRoot != null)
            wirePuzzleUIRoot.SetActive(false);

        // Tắt wire camera
        if (wirePuzzleCamera != null)
            wirePuzzleCamera.gameObject.SetActive(false);

        // Tắt camera khoe đèn Morse từ đầu
        if (morseShowcaseCamera != null)
            morseShowcaseCamera.gameObject.SetActive(false);

        // Tìm player camera (Camera.main)
        _playerCam = Camera.main;

        // Khoá computer ngay từ đầu
        SetComputerLocked(true);

        // Tắt đèn Morse ban đầu — MẤT ĐIỆN HẲN (đen), không chỉ về idle đỏ tối
        if (morseLightsToActivate != null)
            foreach (var light in morseLightsToActivate)
                if (light != null) light.SetPowerOn(false);

        if (puzzleManager != null)
            puzzleManager.OnPuzzleCompleted += HandleSolved;

        // Validate
        if (wirePuzzleCanvas == null)
            Debug.LogError("[WireBoxInteraction] Chưa gán 'Wire Puzzle Canvas'!", this);
        if (wirePuzzleCamera == null)
            Debug.LogError("[WireBoxInteraction] Chưa gán 'Wire Puzzle Camera'!", this);

        var col = GetComponent<Collider>();
        if (col == null || !col.isTrigger)
            Debug.LogError("[WireBoxInteraction] Cần Collider với Is Trigger = true!", this);
    }

    private void OnDestroy()
    {
        if (puzzleManager != null)
            puzzleManager.OnPuzzleCompleted -= HandleSolved;
    }

    private void Update()
    {
        if (_solved) return;

        if (_isInteracting)
        {
            if (Input.GetKeyDown(KeyCode.E) || Input.GetKeyDown(KeyCode.Escape))
                ExitWireBox();
            return;
        }

        if (_playerInRange && Input.GetKeyDown(KeyCode.E))
            EnterWireBox();
    }

    private void OnTriggerEnter(Collider other)
    {
        if (!IsPlayer(other)) return;
        _playerInRange = true;
        Debug.Log("[WireBoxInteraction] Player trong vùng hộp điện. Bấm E để mở.");
    }

    private void OnTriggerExit(Collider other)
    {
        if (!IsPlayer(other)) return;
        _playerInRange = false;
        if (_isInteracting) ExitWireBox();
    }

    private bool IsPlayer(Collider other)
    {
        return string.IsNullOrEmpty(playerTag) || other.CompareTag(playerTag);
    }

    // ── Mở UI ───────────────────────────────────────────────────────

    private void EnterWireBox()
    {
        _isInteracting = true;
        UIOpen         = true;

        if (sfxSource != null && sfxOpenBox != null)
            sfxSource.PlayOneShot(sfxOpenBox);

        // 1. Tắt player camera
        if (_playerCam != null)
            _playerCam.gameObject.SetActive(false);

        // 2. Bật wire puzzle camera
        if (wirePuzzleCamera != null)
        {
            wirePuzzleCamera.gameObject.SetActive(true);

            // Gán đúng camera vào Canvas và WirePuzzleManager
            if (wirePuzzleCanvas != null)
                wirePuzzleCanvas.worldCamera = wirePuzzleCamera;

            if (puzzleManager != null)
                puzzleManager.minigameCamera = wirePuzzleCamera;
        }

        // 3. Bật Canvas & UI (sau khi BlockInput để tránh UIInputBlocker quét thấy Canvas này)
        inputBlocker?.BlockInput();

        if (wirePuzzleCanvas != null)
            wirePuzzleCanvas.gameObject.SetActive(true);
        if (wirePuzzleUIRoot != null)
            wirePuzzleUIRoot.SetActive(true);

        if (puzzleManager != null)
            puzzleManager.ResetPuzzle();

        Debug.Log("[WireBoxInteraction] Mở UI nối dây (World Space).");
    }

    // ── Đóng UI ─────────────────────────────────────────────────────

    public void ExitWireBox()
    {
        _isInteracting = false;
        UIOpen         = false;

        if (wirePuzzleUIRoot != null)
            wirePuzzleUIRoot.SetActive(false);
        if (wirePuzzleCanvas != null)
            wirePuzzleCanvas.gameObject.SetActive(false);

        // Tắt wire camera, bật lại player camera
        if (wirePuzzleCamera != null)
            wirePuzzleCamera.gameObject.SetActive(false);
        if (_playerCam != null)
            _playerCam.gameObject.SetActive(true);

        inputBlocker?.UnblockInput();
    }

    // ── Hoàn thành puzzle ────────────────────────────────────────────

    private void HandleSolved()
    {
        _solved       = true;
        WireBoxSolved = true;

        SetComputerLocked(false);

        // Bật đèn Morse
        // Mở điện cho từng đèn TRƯỚC khi cho Sequencer chạy — PlayOnce() sẽ
        // bị return ngay (coi như "xong luôn") nếu đèn chưa có điện.
        if (morseLightsToActivate != null)
            foreach (var light in morseLightsToActivate)
                if (light != null) light.SetPowerOn(true);

        // Sequencer tự điều phối phát LẦN LƯỢT qua PlayOnce() — không gọi
        // PlayMessage() trực tiếp trên từng đèn ở đây, kẻo tất cả nháy cùng lúc
        // đè lên coroutine của Sequencer.
        if (morseSequencerToActivate != null)
            morseSequencerToActivate.BeginSequence();

        // Đợi 1 chút rồi chuyển cam qua đèn Morse để báo hiệu đã kích hoạt,
        // sau đó tự trả về (đóng UI nối dây + bật lại cam người chơi).
        StartCoroutine(ShowcaseMorseLightsThenExit());
    }

    private IEnumerator ShowcaseMorseLightsThenExit()
    {
        // Giữ UI nối dây 1 chút để người chơi thấy "Hoàn thành" trước khi chuyển cam
        yield return new WaitForSeconds(morseShowcaseStartDelay);

        // Tắt wire puzzle camera, bật camera khoe đèn Morse
        if (wirePuzzleCamera != null)
            wirePuzzleCamera.gameObject.SetActive(false);
        if (morseShowcaseCamera != null)
            morseShowcaseCamera.gameObject.SetActive(true);

        yield return new WaitForSeconds(morseShowcaseDuration);

        // Tắt camera khoe đèn, rồi đóng UI + trả lại cam người chơi như cũ
        if (morseShowcaseCamera != null)
            morseShowcaseCamera.gameObject.SetActive(false);

        ExitWireBox();
    }

    // ── Helpers ──────────────────────────────────────────────────────

    private void SetComputerLocked(bool locked)
    {
        if (computerInteractionToLock == null) return;
        computerInteractionToLock.enabled = !locked;
        Debug.Log($"[WireBoxInteraction] Computer {(locked ? "KHOÁ" : "MỞ KHOÁ")}.");
    }

    private void OnDrawGizmosSelected()
    {
        var col = GetComponent<Collider>();
        if (col == null) return;
        Gizmos.color = new Color(1f, 0.85f, 0f, 0.35f);
        if (col is BoxCollider box)
        {
            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.DrawCube(box.center, box.size);
        }
        else
        {
            Gizmos.DrawWireCube(col.bounds.center, col.bounds.size);
        }
    }
}