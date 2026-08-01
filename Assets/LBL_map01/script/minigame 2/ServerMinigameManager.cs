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

    [Header("Hiệu ứng khi TẮT XONG toàn bộ server (trụ giữa tối đi + đèn báo đỏ chớp)")]
    [Tooltip("Renderer(s) của cái trụ ở giữa map. Khi tắt xong hết server, trụ sẽ đổi màu tối đi và tắt emission (giống như mất điện).")]
    public Renderer[] centerPillarRenderers;
    [Tooltip("Màu thân trụ sau khi tối đi (lúc còn hoạt động thì giữ nguyên material gốc, cái này chỉ áp dụng SAU KHI tắt xong).")]
    public Color pillarDimColor = new Color(0.05f, 0.05f, 0.05f);
    [Tooltip("Độ sáng emission của trụ sau khi tối đi. Để 0 = tắt hẳn glow.")]
    public float pillarDimEmissionIntensity = 0f;

    [Tooltip("Các đèn báo hiệu (Light) đặt rải rác quanh map, sẽ chớp màu đỏ liên tục sau khi tắt xong toàn bộ server.")]
    public Light[] redWarningLights;
    public Color warningLightColor = Color.red;
    [Tooltip("Thời gian giữa mỗi lần chớp (giây).")]
    public float warningBlinkInterval = 0.5f;

    private MaterialPropertyBlock _pillarMpb;
    private Coroutine _warningBlinkCoroutine;

    [Header("Nổ trần + TẤT CẢ server chui xuống đất (khi tắt xong toàn bộ)")]
    [Tooltip("Kéo component CeilingExplosion vào đây. Sẽ tự gọi TriggerExplosion() ngay khi tắt xong server cuối cùng.")]
    public CeilingExplosion ceilingExplosion;
    [Tooltip("Các server TRANG TRÍ THÊM — KHÔNG tham gia puzzle (không có script ServerBlock, không cần tắt đúng thứ tự). " +
             "Chỉ cần kéo Transform gốc của chúng vào đây để chúng chui xuống đất CÙNG LÚC với các server chính lúc kết thúc.")]
    public Transform[] decorativeServers;
    [Tooltip("Thời gian 1 khối (chính hoặc trang trí) chui xuống đất.")]
    public float sinkDuration = 1f;
    [Tooltip("Độ trễ giữa mỗi khối khi chui xuống (so le). Để 0 nếu muốn tất cả chui xuống CÙNG LÚC.")]
    public float sinkDelay = 0f;

    private float[] _decorativeOriginalY;

    /// <summary>Được gọi khi player đã tắt ĐÚNG toàn bộ server theo đúng thứ tự random (sau khi SolveSequence chạy xong).</summary>
    public System.Action OnAllServersShutdown;

    /// <summary>Được gọi ĐÚNG lúc trần nổ (trước khi server chui xuống đất) — dùng để chuyển camera đúng thời điểm.</summary>
    public System.Action OnCeilingExplosionTriggered;

    /// <summary>Được gọi khi player CẦN quyền điều khiển lại (đi bộ tới từng server để bấm F) — sau khi animation trồi lên xong.</summary>
    public System.Action OnPuzzleInteractionReady;

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

        _decorativeOriginalY = new float[decorativeServers != null ? decorativeServers.Length : 0];
        for (int i = 0; i < _decorativeOriginalY.Length; i++)
        {
            if (decorativeServers[i] == null) continue;
            _decorativeOriginalY[i] = decorativeServers[i].position.y;
        }
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
        OnPuzzleInteractionReady?.Invoke();
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

        DimCenterPillar();
        StartWarningLights();

        if (ceilingExplosion != null) ceilingExplosion.TriggerExplosion();
        OnCeilingExplosionTriggered?.Invoke();
        yield return StartCoroutine(SinkAllBlocks());

        if (openDoorOnComplete)
            door?.UnlockAndOpen();

        yield return new WaitForSeconds(1f);

        if (puzzleUI) puzzleUI.SetActive(false);
        if (noticePanel) noticePanel.SetActive(false);

        OnAllServersShutdown?.Invoke();
    }

    /// <summary>Cho toàn bộ server (chính lẫn trang trí) chui xuống đất — gọi khi tắt xong toàn bộ server (kèm nổ trần).</summary>
    private IEnumerator SinkAllBlocks()
    {
        for (int i = 0; i < serverBlocks.Length; i++)
        {
            if (serverBlocks[i] == null) continue;
            float targetY = _originalY[i] - hiddenOffset;
            StartCoroutine(SinkBlock(serverBlocks[i].transform, targetY));
            if (sinkDelay > 0f) yield return new WaitForSeconds(sinkDelay);
        }

        if (decorativeServers != null)
        {
            for (int i = 0; i < decorativeServers.Length; i++)
            {
                if (decorativeServers[i] == null) continue;
                float targetY = _decorativeOriginalY[i] - hiddenOffset;
                StartCoroutine(SinkBlock(decorativeServers[i], targetY));
                if (sinkDelay > 0f) yield return new WaitForSeconds(sinkDelay);
            }
        }

        // Đợi cho khối cuối cùng chui xuống xong hẳn trước khi cửa mở
        yield return new WaitForSeconds(sinkDuration);
    }

    private IEnumerator SinkBlock(Transform t, float targetY)
    {
        Vector3 start = t.position;
        Vector3 target = new Vector3(t.position.x, targetY, t.position.z);
        float elapsed = 0f;

        while (elapsed < sinkDuration)
        {
            elapsed += Time.deltaTime;
            float curve = Mathf.SmoothStep(0f, 1f, elapsed / sinkDuration);
            t.position = Vector3.Lerp(start, target, curve);
            yield return null;
        }

        t.position = target;
    }

    /// <summary>Làm tối trụ giữa map (tắt/giảm emission) — gọi khi đã tắt xong toàn bộ server.</summary>
    private void DimCenterPillar()
    {
        if (centerPillarRenderers == null) return;

        if (_pillarMpb == null) _pillarMpb = new MaterialPropertyBlock();
        Color emissive = pillarDimColor * pillarDimEmissionIntensity;

        foreach (var r in centerPillarRenderers)
        {
            if (r == null) continue;

            r.GetPropertyBlock(_pillarMpb);
            _pillarMpb.SetColor("_BaseColor", pillarDimColor);
            _pillarMpb.SetColor("_Color", pillarDimColor);
            _pillarMpb.SetColor("_EmissionColor", emissive);
            r.SetPropertyBlock(_pillarMpb);
        }
    }

    /// <summary>Bắt đầu chớp đèn báo đỏ khắp map — gọi khi đã tắt xong toàn bộ server.</summary>
    public void StartWarningLights()
    {
        if (redWarningLights == null || redWarningLights.Length == 0) return;

        if (_warningBlinkCoroutine != null) StopCoroutine(_warningBlinkCoroutine);
        _warningBlinkCoroutine = StartCoroutine(BlinkWarningLights());
    }

    /// <summary>Tắt hẳn đèn báo đỏ (VD: gọi lúc bật lại đèn thường ở cuối chuỗi minigame).</summary>
    public void StopWarningLights()
    {
        if (_warningBlinkCoroutine != null)
        {
            StopCoroutine(_warningBlinkCoroutine);
            _warningBlinkCoroutine = null;
        }

        if (redWarningLights == null) return;
        foreach (var l in redWarningLights)
            if (l != null) l.enabled = false;
    }

    private IEnumerator BlinkWarningLights()
    {
        bool on = false;
        while (true)
        {
            on = !on;
            foreach (var l in redWarningLights)
            {
                if (l == null) continue;
                l.color = warningLightColor;
                l.enabled = on;
            }
            yield return new WaitForSeconds(warningBlinkInterval);
        }
    }

    private void UpdateProgressUI()
    {
        if (progressText == null) return;
        int total = serverBlocks != null ? serverBlocks.Length : 6;
        progressText.text = $"Server đã tắt: {_currentStep} / {total}";
    }
}