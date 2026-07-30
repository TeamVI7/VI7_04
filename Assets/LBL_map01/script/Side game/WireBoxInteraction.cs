using System.Collections;
using DG.Tweening;
using UnityEngine;

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
    [Tooltip("Camera đặt sẵn trong scene, nhìn vào cụm đèn Morse. TẮT SẴN từ đầu — " +
             "chỉ bật đúng lúc showcase, tắt lại ngay khi xong. Đây là camera RIÊNG, " +
             "hoàn toàn không đụng tới camera người chơi.")]
    [SerializeField] private Camera morseShowcaseCamera;

    [Tooltip("Camera khoe sẽ DI CHUYỂN (tween) lần lượt qua từng waypoint này trong lúc " +
             "đang BẬT. Để trống = tự dùng vị trí gốc của morseShowcaseCamera làm waypoint duy nhất.")]
    [SerializeField] private Transform[] morseShowcaseWaypoints;

    [Tooltip("Đợi bao lâu sau khi giải xong rồi mới bật cam khoe đèn (giây).")]
    [SerializeField] private float morseShowcaseStartDelay = 0.5f;

    [Tooltip("Giữ camera ở waypoint cuối trong bao lâu rồi tắt (giây).")]
    [SerializeField] private float morseShowcaseDuration = 2f;

    [Header("Morse Camera Tween Settings")]
    [SerializeField] private float morseCameraMoveDuration = 0.6f;
    [SerializeField] private Ease morseCameraMoveEase = Ease.InOutSine;

    [Header("Dialogue — plays after the wire puzzle is solved")]
    [Tooltip("Drag a DialogueData asset (lines + voice) here. Leave empty to skip dialogue.")]
    [SerializeField] private DialogueData dialogueOnSolved;

    [Header("Âm thanh")]
    [Tooltip("AudioSource để phát SFX. Để trống = tự thêm AudioSource lên chính object này.")]
    [SerializeField] private AudioSource sfxSource;

    [Tooltip("Tiếng phát ra khi MỞ hộp điện (bấm F vào, lúc EnterWireBox).")]
    [SerializeField] private AudioClip sfxOpenBox;

    [Tooltip("Tiếng máy phát điện kêu lên khi nối dây xong (lúc puzzle hoàn thành, " +
             "trước khi đèn Morse bật).")]
    [SerializeField] private AudioClip sfxGeneratorPowerUp;

    [Header("Outline khi player đứng trong vùng")]
    [SerializeField] private Outline outline;

    public static bool UIOpen        { get; private set; } = false;
    public static bool WireBoxSolved { get; private set; } = false;

    private bool _isInteracting = false;
    private bool _solved        = false;
    private bool _playerInRange = false;

    private Camera _playerCam; 
    private Vector3    _morseCamOriginalPos;     private Quaternion _morseCamOriginalRot;

    private void Start()
    {
        if (sfxSource == null)
        {
            sfxSource = GetComponent<AudioSource>();
            if (sfxSource == null)
                sfxSource = gameObject.AddComponent<AudioSource>();
            sfxSource.playOnAwake = false;
        }

        if (wirePuzzleCanvas != null)
            wirePuzzleCanvas.gameObject.SetActive(false);
        if (wirePuzzleUIRoot != null)
            wirePuzzleUIRoot.SetActive(false);

        if (wirePuzzleCamera != null)
            wirePuzzleCamera.gameObject.SetActive(false);

        if (morseShowcaseCamera != null)
        {
            _morseCamOriginalPos = morseShowcaseCamera.transform.position;
            _morseCamOriginalRot = morseShowcaseCamera.transform.rotation;
            morseShowcaseCamera.gameObject.SetActive(false);
        }

        SetComputerLocked(true);
        if (morseLightsToActivate != null)
            foreach (var light in morseLightsToActivate)
                if (light != null) light.SetPowerOn(false);

        if (puzzleManager != null)
            puzzleManager.OnPuzzleCompleted += HandleSolved;

        if (wirePuzzleCanvas == null)
            Debug.LogError("[WireBoxInteraction] Chưa gán 'Wire Puzzle Canvas'!", this);
        if (wirePuzzleCamera == null)
            Debug.LogError("[WireBoxInteraction] Chưa gán 'Wire Puzzle Camera'!", this);

        var col = GetComponent<Collider>();
        if (col == null || !col.isTrigger)
            Debug.LogError("[WireBoxInteraction] Cần Collider với Is Trigger = true!", this);

        if (outline == null) outline = GetComponent<Outline>();
        if (outline != null) outline.enabled = false;
    }

    private void OnDestroy()
    {
        if (puzzleManager != null)
            puzzleManager.OnPuzzleCompleted -= HandleSolved;

        if (morseShowcaseCamera != null)
            morseShowcaseCamera.transform.DOKill();
    }

    private void Update()
    {
        if (_solved) return;

        if (_isInteracting)
        {
            if (Input.GetKeyDown(KeyCode.F) || Input.GetKeyDown(KeyCode.Escape))
                ExitWireBox();
            return;
        }

        if (_playerInRange && Input.GetKeyDown(KeyCode.F))
            EnterWireBox();
    }

    private void OnTriggerEnter(Collider other)
    {
        if (!IsPlayer(other)) return;
        _playerInRange = true;
        if (outline != null && !_solved) outline.enabled = true;
        Debug.Log("[WireBoxInteraction] Player trong vùng hộp điện. Bấm F để mở.");
    }

    private void OnTriggerExit(Collider other)
    {
        if (!IsPlayer(other)) return;
        _playerInRange = false;
        if (outline != null) outline.enabled = false;
        if (_isInteracting) ExitWireBox();
    }

    private bool IsPlayer(Collider other)
    {
        return string.IsNullOrEmpty(playerTag) || other.CompareTag(playerTag);
    }


    private void EnterWireBox()
    {
        _isInteracting = true;
        UIOpen         = true;

        if (outline != null) outline.enabled = false;

        if (sfxSource != null && sfxOpenBox != null)
            sfxSource.PlayOneShot(sfxOpenBox);

        _playerCam = Camera.main;
        if (_playerCam != null)
            _playerCam.gameObject.SetActive(false);

        if (wirePuzzleCamera != null)
        {
            wirePuzzleCamera.gameObject.SetActive(true);

            if (wirePuzzleCanvas != null)
                wirePuzzleCanvas.worldCamera = wirePuzzleCamera;

            if (puzzleManager != null)
                puzzleManager.minigameCamera = wirePuzzleCamera;
        }

        inputBlocker?.BlockInput();

        if (wirePuzzleCanvas != null)
            wirePuzzleCanvas.gameObject.SetActive(true);
        if (wirePuzzleUIRoot != null)
            wirePuzzleUIRoot.SetActive(true);

        if (puzzleManager != null)
            puzzleManager.ResetPuzzle();

        Debug.Log("[WireBoxInteraction] Mở UI nối dây (World Space).");
    }


    public void ExitWireBox()
    {
        _isInteracting = false;
        UIOpen         = false;

        if (wirePuzzleUIRoot != null)
            wirePuzzleUIRoot.SetActive(false);
        if (wirePuzzleCanvas != null)
            wirePuzzleCanvas.gameObject.SetActive(false);

        if (wirePuzzleCamera != null)
            wirePuzzleCamera.gameObject.SetActive(false);

        if (_playerCam != null)
        {
            _playerCam.gameObject.SetActive(true);
        }
        else
        {
            var fallbackCam = Camera.main;
            if (fallbackCam != null)
                fallbackCam.gameObject.SetActive(true);
        }

        inputBlocker?.UnblockInput();
    }


    private void HandleSolved()
    {
        _solved       = true;
        WireBoxSolved = true;

        if (outline != null) outline.enabled = false;

        if (sfxSource != null && sfxGeneratorPowerUp != null)
            sfxSource.PlayOneShot(sfxGeneratorPowerUp);

        SetComputerLocked(false);

        if (morseLightsToActivate != null)
            foreach (var light in morseLightsToActivate)
                if (light != null) light.SetPowerOn(true);

        if (morseSequencerToActivate != null)
        {
            morseSequencerToActivate.StopSequence();
            morseSequencerToActivate.BeginSequence();
        }

        StartCoroutine(ShowcaseMorseLightsThenExit());
    }

    private IEnumerator ShowcaseMorseLightsThenExit()
    {
        yield return new WaitForSeconds(morseShowcaseStartDelay);

        if (wirePuzzleCamera != null)
            wirePuzzleCamera.gameObject.SetActive(false);

        if (morseShowcaseCamera != null)
        {
            morseShowcaseCamera.transform.DOKill();
            morseShowcaseCamera.transform.position = _morseCamOriginalPos;
            morseShowcaseCamera.transform.rotation = _morseCamOriginalRot;
            morseShowcaseCamera.gameObject.SetActive(true);

            Transform[] waypoints = (morseShowcaseWaypoints != null && morseShowcaseWaypoints.Length > 0)
                ? morseShowcaseWaypoints
                : new[] { morseShowcaseCamera.transform };

            foreach (var wp in waypoints)
            {
                if (wp == null || wp == morseShowcaseCamera.transform) continue;

                Tween moveTween = morseShowcaseCamera.transform
                    .DOMove(wp.position, morseCameraMoveDuration)
                    .SetEase(morseCameraMoveEase);

                morseShowcaseCamera.transform
                    .DORotateQuaternion(wp.rotation, morseCameraMoveDuration)
                    .SetEase(morseCameraMoveEase);

                yield return moveTween.WaitForCompletion();
            }

            yield return new WaitForSeconds(morseShowcaseDuration);

            morseShowcaseCamera.transform.DOKill();
            morseShowcaseCamera.gameObject.SetActive(false);
        }
        else
        {
            yield return new WaitForSeconds(morseShowcaseDuration);
        }

        ExitWireBox();

        if (dialogueOnSolved != null && DialogueManager.Instance != null)
            DialogueManager.Instance.Play(dialogueOnSolved);
    }


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