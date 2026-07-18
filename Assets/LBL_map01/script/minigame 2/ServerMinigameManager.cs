using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class ServerMinigameManager : MonoBehaviour
{
    [Header("Server Blocks")]
    [Tooltip("Kéo 6 khối server vào đây. Thứ tự trong mảng KHÔNG cần trùng số hiển thị — mỗi ServerBlock có field 'serverNumber' riêng là số CỐ ĐỊNH.")]
    public ServerBlock[] serverBlocks;

    [Header("Rise Animation")]
    [Tooltip("Server sẽ bị thụt xuống bao nhiêu đơn vị so với vị trí gốc (nhập số dương, VD: 5)")]
    public float hiddenOffset = 5f;
    [Tooltip("Thời gian 1 khối trồi lên (giây)")]
    public float riseDuration = 1.2f;
    [Tooltip("Độ trễ giữa mỗi khối trồi lên")]
    public float riseDelay = 0.2f;

    [Header("Door")]
    public SlidingDoorController door;
    [Tooltip("Bật nếu muốn manager này tự mở cửa khi tắt xong toàn bộ server. Tắt nếu việc mở cửa/đi tiếp do MinigameFlowController xử lý (dùng callback OnAllServersShutdown).")]
    public bool openDoorOnComplete = true;

    [Header("UI (tuỳ chọn)")]
    public GameObject puzzleUI;
    public TMPro.TMP_Text progressText;

    [Header("Màn hình thông báo (DÙNG CHUNG với AI Shutdown Notice)")]
    [Tooltip("Panel hiển thị 'TẮT SERVER X' / 'SAI THỨ TỰ'. Có thể để trống ở đây và để MinigameFlowController gán vào lúc runtime qua SetNoticeUI().")]
    public GameObject noticePanel;
    public TMPro.TMP_Text noticeText;
    [Tooltip("Thời gian giữ thông báo SAI trước khi random lại thứ tự mới.")]
    public float wrongResetDelay = 1.2f;

    [Header("Âm thanh")]
    [Tooltip("AudioSource để phát âm thanh server trồi lên (nếu để trống sẽ tự thêm 1 cái lúc runtime).")]
    public AudioSource audioSource;
    [Tooltip("Âm thanh phát mỗi lần 1 khối server bắt đầu trồi lên.")]
    public AudioClip riseSound;
    [Range(0f, 1f)] public float riseSfxVolume = 1f;

    [Tooltip("Âm thanh phát khi ĐÃ TẮT XONG toàn bộ server đúng thứ tự (hoàn thành minigame).")]
    public AudioClip allDoneSound;
    [Range(0f, 1f)] public float allDoneSfxVolume = 1f;

    [Header("VFX khi trồi lên")]
    [Tooltip("Prefab Particle System dùng chung, tự Instantiate tại chân mỗi khối khi nó trồi lên " +
             "(dùng khi bạn KHÔNG muốn đặt sẵn ParticleSystem thủ công cho từng ServerBlock). " +
             "Nếu để trống, sẽ dùng riseVFX đã gán sẵn trên từng ServerBlock (nếu có).")]
    public GameObject riseVFXPrefab;
    [Tooltip("Thời gian tồn tại của VFX prefab trước khi tự huỷ (giây).")]
    public float riseVFXLifetime = 2f;

    /// <summary>Được gọi khi player đã tắt ĐÚNG toàn bộ server theo đúng thứ tự random (sau khi SolveSequence chạy xong).</summary>
    public System.Action OnAllServersShutdown;

    private bool _triggered = false;
    private bool _solved = false;
    private float[] _originalY;

    // Danh sách index (trong serverBlocks) theo ĐÚNG thứ tự phải tắt — được random mỗi lần bắt đầu / mỗi lần bấm sai
    private List<int> _shutdownOrder = new List<int>();
    private int _currentStep = 0;
    private bool _puzzleActive = false;

    private void Start()
    {
        if (audioSource == null)
        {
            audioSource = GetComponent<AudioSource>();
            if (audioSource == null)
            {
                audioSource = gameObject.AddComponent<AudioSource>();
                audioSource.playOnAwake = false;
            }
        }
        _originalY = new float[serverBlocks.Length];

        for (int i = 0; i < serverBlocks.Length; i++)
        {
            if (serverBlocks[i] == null) continue;

            _originalY[i] = serverBlocks[i].transform.position.y;

            Vector3 pos = serverBlocks[i].transform.position;
            pos.y = _originalY[i] - hiddenOffset;
            serverBlocks[i].transform.position = pos;
        }

        if (puzzleUI) puzzleUI.SetActive(false);
        if (noticePanel) noticePanel.SetActive(false);
        UpdateProgressUI();
    }

    /// <summary>Cho phép nơi khác (VD: MinigameFlowController) gán màn hình thông báo dùng chung lúc runtime, thay vì gán tay trong Inspector.</summary>
    public void SetNoticeUI(GameObject panel, TMPro.TMP_Text text)
    {
        noticePanel = panel;
        noticeText = text;
    }

    public void OnPlayerEnterTrigger()
    {
        if (_triggered) return;
        _triggered = true;
        StartCoroutine(RiseThenStartPuzzle());
        if (puzzleUI) puzzleUI.SetActive(true);
    }

    private IEnumerator RiseThenStartPuzzle()
    {
        yield return StartCoroutine(RiseAllBlocks());
        StartShutdownPuzzle();
    }

    private IEnumerator RiseAllBlocks()
    {
        for (int i = 0; i < serverBlocks.Length; i++)
        {
            if (serverBlocks[i] == null) continue;

            PlayRiseSound();
            PlayRiseVFX(serverBlocks[i]);
            StartCoroutine(RiseBlock(serverBlocks[i].transform, _originalY[i]));
            yield return new WaitForSeconds(riseDelay);
        }
        // Đợi thêm cho khối cuối cùng trồi lên xong hẳn trước khi bắt đầu puzzle
        yield return new WaitForSeconds(riseDuration);
    }

    private void PlayRiseSound()
    {
        if (audioSource != null && riseSound != null)
            audioSource.PlayOneShot(riseSound, riseSfxVolume);
    }

    private void PlayRiseVFX(ServerBlock block)
    {
        if (block.riseVFX != null)
        {
            block.PlayRiseVFX();
            return;
        }

        if (riseVFXPrefab != null)
        {
            GameObject vfx = Instantiate(riseVFXPrefab, block.transform.position, Quaternion.identity);
            Destroy(vfx, riseVFXLifetime);
        }
    }

    private IEnumerator RiseBlock(Transform t, float targetY)
    {
        Vector3 start = t.position;
        Vector3 target = new Vector3(t.position.x, targetY, t.position.z);
        float elapsed = 0f;

        while (elapsed < riseDuration)
        {
            elapsed += Time.deltaTime;
            float curve = Mathf.SmoothStep(0f, 1f, elapsed / riseDuration); // mượt
            t.position = Vector3.Lerp(start, target, curve);
            yield return null;
        }

        t.position = target;
    }

    public float GetRiseSequenceDuration()
    {
        int count = serverBlocks != null ? serverBlocks.Length : 0;
        if (count <= 0) return riseDuration;
        return (count - 1) * riseDelay + riseDuration;
    }

    // ==================== PUZZLE: TẮT SERVER THEO THỨ TỰ RANDOM ====================

    private void StartShutdownPuzzle()
    {
        _puzzleActive = true;
        if (noticePanel) noticePanel.SetActive(true);
        GenerateNewOrder();
    }

    /// <summary>Random lại thứ tự phải tắt (đáp án mới), reset toàn bộ đèn/trạng thái về ban đầu.</summary>
    private void GenerateNewOrder()
    {
        _shutdownOrder.Clear();
        for (int i = 0; i < serverBlocks.Length; i++)
            _shutdownOrder.Add(i);

        // Fisher-Yates shuffle
        for (int i = _shutdownOrder.Count - 1; i > 0; i--)
        {
            int j = Random.Range(0, i + 1);
            (_shutdownOrder[i], _shutdownOrder[j]) = (_shutdownOrder[j], _shutdownOrder[i]);
        }

        _currentStep = 0;

        foreach (var block in serverBlocks)
            if (block != null) block.ResetState();

        UpdateProgressUI();
        ShowCurrentTarget();
    }

    private void ShowCurrentTarget()
    {
        if (!_puzzleActive || _currentStep >= _shutdownOrder.Count) return;

        var target = serverBlocks[_shutdownOrder[_currentStep]];
        if (noticeText != null && target != null)
            noticeText.text = $"TẮT SERVER {target.serverNumber}";
    }

    /// <summary>Gọi bởi ServerBlock khi player bấm F ở gần nó.</summary>
    public void TryShutdownServer(ServerBlock block)
    {
        if (!_puzzleActive || _solved || block == null) return;

        int blockIndex = System.Array.IndexOf(serverBlocks, block);
        if (blockIndex < 0) return;

        int expectedIndex = _shutdownOrder[_currentStep];

        if (blockIndex == expectedIndex)
        {
            block.SetShutdown(true);
            _currentStep++;
            UpdateProgressUI();

            if (_currentStep >= _shutdownOrder.Count)
            {
                if (noticeText != null) noticeText.text = "✓ TẤT CẢ SERVER ĐÃ TẮT";
                StartCoroutine(SolveSequence());
            }
            else
            {
                ShowCurrentTarget();
            }
        }
        else
        {
            block.PlayWrongFeedback();
            StartCoroutine(WrongOrderThenReset());
        }
    }

    private IEnumerator WrongOrderThenReset()
    {
        _puzzleActive = false;
        if (noticeText != null) noticeText.text = "✗ SAI THỨ TỰ! Đang random lại...";

        yield return new WaitForSeconds(wrongResetDelay);

        _puzzleActive = true;
        GenerateNewOrder();
    }

    private IEnumerator SolveSequence()
    {
        _solved = true;
        _puzzleActive = false;

        if (progressText)
            progressText.text = "✓ GIẢI MÃ HOÀN TẤT — Cửa đang mở...";

        if (audioSource != null && allDoneSound != null)
            audioSource.PlayOneShot(allDoneSound, allDoneSfxVolume);

        yield return new WaitForSeconds(1.5f);

        if (openDoorOnComplete)
            door?.UnlockAndOpen();

        yield return new WaitForSeconds(1f);

        if (puzzleUI) puzzleUI.SetActive(false);
        if (noticePanel) noticePanel.SetActive(false);

        OnAllServersShutdown?.Invoke();
    }

    private void UpdateProgressUI()
    {
        if (progressText == null) return;
        int total = serverBlocks != null ? serverBlocks.Length : 6;
        progressText.text = $"Server đã tắt: {_currentStep} / {total}";
    }
}