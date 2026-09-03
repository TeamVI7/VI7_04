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

    [Tooltip("Các đèn báo hiệu (Light) đặt rải rác quanh map, sẽ chớp màu đỏ liên tục sau khi tắt xong toàn bộ server. " +
             "CÓ THỂ để GameObject đèn tắt sẵn (SetActive false) trong scene — script sẽ tự bật lên khi cần.")]
    public Light[] redWarningLights;
    public Color warningLightColor = Color.red;
    [Tooltip("Cường độ sáng ép cho đèn báo lúc chớp. Để <= 0 nếu muốn giữ nguyên intensity đã set sẵn trên đèn.")]
    public float warningLightIntensity = 6f;
    [Tooltip("Thời gian giữa mỗi lần chớp (giây).")]
    public float warningBlinkInterval = 0.5f;

    [Header("Đèn vòng (ring light) trên trụ — đổi màu KHÁC với thân trụ")]
    [Tooltip("Renderer của cái đèn vòng / vành sáng trên trụ. KHÁC với centerPillarRenderers: thân trụ thì tối đi, " +
             "còn mấy cái này thì chuyển sang màu báo động và chớp theo đèn đỏ.")]
    public Renderer[] pillarRingLightRenderers;
    [Tooltip("Màu của ring light sau khi tắt xong server (lúc chớp SÁNG).")]
    public Color ringLightOnColor = new Color(1f, 0.05f, 0.05f);
    [Tooltip("Màu của ring light lúc chớp TỐI (giữa 2 nhịp chớp).")]
    public Color ringLightOffColor = new Color(0.15f, 0.01f, 0.01f);
    [Tooltip("Độ sáng emission của ring light lúc chớp SÁNG. Muốn cháy sáng/bloom mạnh thì để 5-15.")]
    public float ringLightEmissionIntensity = 5f;
    [Tooltip("Độ sáng emission lúc chớp TỐI. Để 0 = tắt glow hẳn giữa 2 nhịp; để ~0.2 nếu muốn còn âm ỉ đỏ.")]
    public float ringLightOffEmissionIntensity = 0f;
    [Tooltip("Bật = ring light chạy/chớp theo đèn đỏ. Tắt = ring light đứng yên ở màu ON.")]
    public bool ringLightBlinks = true;

    public enum WarningPattern
    {
        [Tooltip("Xung sáng CHẠY từ đèn index 0 → đèn cuối rồi quay lại đầu.")]
        Chase = 0,
        [Tooltip("Tất cả đèn cùng chớp on/off một lúc (kiểu cũ).")]
        BlinkAll = 1,
    }

    [Header("Kiểu báo động")]
    [Tooltip("Chase = xung sáng chạy lần lượt từ index đầu tới index cuối rồi lặp lại. " +
             "THỨ TỰ CHẠY = đúng thứ tự bạn kéo đèn vào mảng redWarningLights / pillarRingLightRenderers.")]
    public WarningPattern warningPattern = WarningPattern.Chase;
    [Tooltip("Chase: số 'ô đèn' mà cái đuôi xung kéo dài. 1 = chỉ 1 đèn sáng mỗi lúc; " +
             "2-3 = đèn phía sau còn sáng mờ dần cho mượt.")]
    public float chaseTrailLength = 1.5f;
    [Tooltip("Chase: bật = xung sáng mờ dần mượt mà. Tắt = bật/tắt dứt khoát từng đèn.")]
    public bool chaseSmoothFade = true;

    [Tooltip("Độ sáng TỐI THIỂU (0..1) của đèn báo — đèn KHÔNG BAO GIỜ tắt hẳn, xung chạy chỉ làm nó " +
             "sáng bùng lên rồi rơi về mức này. Để 0 nếu muốn đèn tắt hẳn ngoài vùng xung.")]
    [Range(0f, 1f)] public float warningMinLevel = 0.2f;
    [Tooltip("Bật = đèn báo đỏ chạy xung VĨNH VIỄN: StopWarningLights() sẽ bị BỎ QUA hoàn toàn " +
             "(MinigameFlowController gọi nó lúc bật lại đèn thường — chính chỗ này làm xung chạy 1 lượt rồi đứng im). " +
             "Tắt = StopWarningLights() tắt hẳn đèn đỏ như cũ. Muốn tắt bằng tay thì gọi ForceStopWarningLights().")]
    public bool keepWarningLightsOnForever = true;

    // Đánh dấu báo động ĐANG bật, để tự chạy lại xung nếu GameObject bị tắt/bật lại
    // (coroutine chết vĩnh viễn khi GameObject disable — đây là lý do thứ hai làm xung chỉ chạy được 1 lượt).
    private bool _warningActive = false;

    private Material[][] _pillarMats;
    private Material[][] _ringMats;
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

    /// <summary>True khi chuỗi trồi lên + puzzle đã được kích hoạt (bởi bất kỳ script nào).</summary>
    public bool IsTriggered => _triggered;

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
        if (serverBlocks == null) serverBlocks = new ServerBlock[0];
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

    /// <summary>Bắt đầu chuỗi trồi lên + puzzle tắt server.
    /// Trả về FALSE nếu đã được kích hoạt trước đó (bởi script khác). Script gọi BẮT BUỘC phải
    /// xử lý trường hợp này: các callback (OnPuzzleInteractionReady / OnCeilingExplosionTriggered /
    /// OnAllServersShutdown) sẽ KHÔNG bao giờ chạy nữa, nên nếu cứ chờ callback là kẹt cứng.</summary>
    public bool OnPlayerEnterTrigger()
    {
        if (_triggered)
        {
            Debug.LogWarning("[ServerMinigameManager] OnPlayerEnterTrigger() bị gọi lần thứ hai — bỏ qua. " +
                             "Chỉ nên có MỘT script sở hữu manager này trong scene.", this);
            return false;
        }

        _triggered = true;
        StartCoroutine(RiseThenStartPuzzle());
        if (puzzleUI) puzzleUI.SetActive(true);
        return true;
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
        int count = 0;
        if (serverBlocks != null)
            foreach (var b in serverBlocks)
                if (b != null) count++;

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
        // Chỉ đưa các slot ĐÃ GÁN vào thứ tự. Nếu để lọt index của 1 slot trống, bước đó sẽ
        // không hiển thị được tên server và TryShutdownServer không bao giờ khớp được —
        // puzzle đứng im vĩnh viễn mà không báo gì.
        for (int i = 0; i < serverBlocks.Length; i++)
            if (serverBlocks[i] != null) _shutdownOrder.Add(i);

        if (_shutdownOrder.Count == 0)
        {
            Debug.LogError("[ServerMinigameManager] Mảng 'serverBlocks' không có khối hợp lệ nào — " +
                           "bỏ qua puzzle và chạy thẳng phần kết để không kẹt người chơi.", this);
            _currentStep = 0;
            _puzzleActive = false;
            StartCoroutine(SolveSequence());
            return;
        }

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

        Color emissive = pillarDimColor * pillarDimEmissionIntensity;

        foreach (var group in GetMaterials(centerPillarRenderers, ref _pillarMats))
            foreach (var mat in group)
                ApplyEmission(mat, pillarDimColor, emissive);
    }

    // ===== Emission helpers =====
    // Ghi thẳng vào MATERIAL chứ không dùng MaterialPropertyBlock: MPB không bật được keyword
    // _EMISSION, và với nhiều shader thì _EmissionColor set qua MPB bị bỏ qua hoàn toàn
    // => đèn/trụ không đổi sáng gì cả. Material instance được cache sẵn nên chớp không tạo rác.

    private static readonly int _BaseColorID = Shader.PropertyToID("_BaseColor");
    private static readonly int _ColorID = Shader.PropertyToID("_Color");
    private static readonly int _EmissionColorID = Shader.PropertyToID("_EmissionColor");

    private static readonly Material[] _emptyMats = new Material[0];

    /// <summary>Lấy (và cache) material instance của từng renderer, GIỮ NGUYÊN chỉ số của mảng renderer
    /// (groups[i] = các material của renderers[i]) để hiệu ứng chạy xung địa chỉ được từng đèn một.</summary>
    private static Material[][] GetMaterials(Renderer[] renderers, ref Material[][] cache)
    {
        if (cache != null) return cache;

        int count = renderers != null ? renderers.Length : 0;
        var groups = new Material[count][];

        for (int i = 0; i < count; i++)
        {
            var r = renderers[i];
            if (r == null) { groups[i] = _emptyMats; continue; }

            // r.materials trả về bản sao riêng của renderer này — chỉnh thoải mái, không đụng asset gốc.
            var mats = r.materials;
            var list = new List<Material>(mats.Length);
            foreach (var m in mats)
                if (m != null) list.Add(m);

            groups[i] = list.ToArray();
        }

        cache = groups;
        return cache;
    }

    private static void ApplyEmission(Material mat, Color baseColor, Color emissive)
    {
        if (mat == null) return;

        if (mat.HasProperty(_BaseColorID)) mat.SetColor(_BaseColorID, baseColor);
        if (mat.HasProperty(_ColorID)) mat.SetColor(_ColorID, baseColor);
        if (!mat.HasProperty(_EmissionColorID)) return;

        bool wantsGlow = emissive.maxColorComponent > 0f;

        if (wantsGlow)
        {
            mat.EnableKeyword("_EMISSION");
            mat.globalIlluminationFlags &= ~MaterialGlobalIlluminationFlags.EmissiveIsBlack;
        }
        else
        {
            // Tắt hẳn glow: giữ keyword bật cũng được nhưng cờ GI phải báo "đen" thì bake/realtime GI mới hết sáng.
            mat.globalIlluminationFlags |= MaterialGlobalIlluminationFlags.EmissiveIsBlack;
        }

        mat.SetColor(_EmissionColorID, emissive);
    }

    /// <summary>Bắt đầu chớp đèn báo đỏ khắp map — gọi khi đã tắt xong toàn bộ server.</summary>
    public void StartWarningLights()
    {
        bool hasLight = false;
        if (redWarningLights != null)
        {
            for (int i = 0; i < redWarningLights.Length; i++)
            {
                if (redWarningLights[i] == null)
                {
                    Debug.LogWarning($"[ServerMinigameManager] redWarningLights[{i}] đang TRỐNG (null) — " +
                                     "nhiều khả năng prefab 'warinng light' bị đổi tên thành 'warning light' " +
                                     "nên tham chiếu trong scene bị đứt. Kéo lại đèn vào slot này.", this);
                    continue;
                }
                hasLight = true;

                // Bật cả GameObject: nếu object cha đang tắt thì dù có set l.enabled = true đèn vẫn KHÔNG sáng.
                // Đây chính là lý do đèn đỏ "không chạy" khi để sẵn đèn tắt trong scene.
                if (!redWarningLights[i].gameObject.activeSelf)
                    redWarningLights[i].gameObject.SetActive(true);

                if (warningLightIntensity > 0f)
                    redWarningLights[i].intensity = warningLightIntensity;
            }
        }

        bool hasRing = HasAnyRingRenderer();

        if (!hasLight && !hasRing)
        {
            Debug.LogWarning("[ServerMinigameManager] Không có đèn báo đỏ NÀO hợp lệ trong 'redWarningLights' " +
                             "và cũng không có 'pillarRingLightRenderers' — bỏ qua hiệu ứng báo động.", this);
            return;
        }

        _warningActive = true;

        if (_warningBlinkCoroutine != null) StopCoroutine(_warningBlinkCoroutine);
        _warningBlinkCoroutine = StartCoroutine(
            warningPattern == WarningPattern.Chase ? ChaseWarningLights() : BlinkWarningLights());
    }

    private void OnEnable()
    {
        // Nếu GameObject này từng bị tắt đi bật lại, coroutine cũ đã chết — chạy lại xung để đèn không đứng im.
        if (_warningActive && _warningBlinkCoroutine == null)
            StartWarningLights();
    }

    private void OnDisable()
    {
        // Unity đã tự huỷ coroutine rồi, chỉ xoá handle để OnEnable biết đường chạy lại.
        _warningBlinkCoroutine = null;
    }

    /// <summary>Dừng hiệu ứng chạy xung của đèn báo đỏ (VD: gọi lúc bật lại đèn thường ở cuối chuỗi minigame).
    /// Nếu 'keepWarningLightsOnForever' bật thì đèn KHÔNG tắt — chỉ đứng yên ở mức sáng đầy và cháy vĩnh viễn.</summary>
    public void StopWarningLights()
    {
        if (keepWarningLightsOnForever)
        {
            // Không dừng gì hết: xung phải chạy mãi. Chỉ bảo đảm nó vẫn đang chạy phòng khi bị chết giữa chừng.
            if (_warningActive && _warningBlinkCoroutine == null) StartWarningLights();
            return;
        }

        ForceStopWarningLights();
    }

    /// <summary>Tắt HẲN đèn báo đỏ, kể cả khi 'keepWarningLightsOnForever' đang bật.</summary>
    public void ForceStopWarningLights()
    {
        _warningActive = false;

        if (_warningBlinkCoroutine != null)
        {
            StopCoroutine(_warningBlinkCoroutine);
            _warningBlinkCoroutine = null;
        }

        if (redWarningLights != null)
        {
            foreach (var l in redWarningLights)
                if (l != null) l.enabled = false;
        }

        SetRingLights(false);
    }

    /// <summary>Xung sáng chạy lần lượt từ index ĐẦU tới index CUỐI rồi lặp lại từ đầu (không tắt hết giữa chừng).
    /// Mỗi 'warningBlinkInterval' giây thì xung đi được 1 bậc index.</summary>
    private IEnumerator ChaseWarningLights()
    {
        int lightCount = redWarningLights != null ? redWarningLights.Length : 0;
        int ringCount = pillarRingLightRenderers != null ? pillarRingLightRenderers.Length : 0;

        // Vị trí đầu xung, chạy liên tục theo thời gian: 0 -> 1 -> 2 ... -> (n-1) -> quay lại 0.
        float lightHead = 0f;
        float ringHead = 0f;
        float step = Mathf.Max(0.0001f, warningBlinkInterval);
        float trail = Mathf.Max(0.01f, chaseTrailLength);

        // Đèn Light phải bật component sẵn, việc sáng/tối do intensity lo — bật/tắt enabled mỗi frame
        // sẽ làm đèn giật cục và huỷ cả bóng đổ realtime.
        for (int i = 0; i < lightCount; i++)
            if (redWarningLights[i] != null) redWarningLights[i].enabled = true;

        // Ring light đứng yên ở màu ON nếu không cho chạy theo — set 1 lần rồi thôi.
        if (!ringLightBlinks) SetRingLights(true);

        while (true)
        {
            float delta = Time.deltaTime / step;

            if (lightCount > 0)
            {
                lightHead = Mathf.Repeat(lightHead + delta, lightCount);
                for (int i = 0; i < lightCount; i++)
                {
                    var l = redWarningLights[i];
                    if (l == null) continue;

                    float level = ChaseLevel(i, lightHead, lightCount, trail);
                    l.color = warningLightColor;
                    l.intensity = (warningLightIntensity > 0f ? warningLightIntensity : 1f) * level;
                }
            }

            if (ringCount > 0 && ringLightBlinks)
            {
                ringHead = Mathf.Repeat(ringHead + delta, ringCount);
                for (int i = 0; i < ringCount; i++)
                    SetRingLightLevel(i, ChaseLevel(i, ringHead, ringCount, trail));
            }

            yield return null;
        }
    }

    /// <summary>Độ sáng (0..1) của đèn thứ 'index' khi đầu xung đang ở vị trí 'head'.
    /// Xung chạy theo chiều index tăng dần, nên đèn nằm NGAY SAU đầu xung là sáng nhất.</summary>
    private float ChaseLevel(int index, float head, int count, float trail)
    {
        // Khoảng cách ngược về phía sau từ đầu xung tới đèn này (bọc vòng quanh mảng).
        float dist = Mathf.Repeat(head - index, count);
        if (dist >= trail) return warningMinLevel;

        float level = 1f - (dist / trail);
        if (chaseSmoothFade) level = Mathf.SmoothStep(0f, 1f, level);
        else level = 1f;

        // Xung chỉ NÂNG đèn từ mức nền lên tối đa — không bao giờ kéo xuống dưới warningMinLevel.
        return Mathf.Lerp(warningMinLevel, 1f, level);
    }

    private IEnumerator BlinkWarningLights()
    {
        bool on = false;

        // Ring light đứng yên ở màu ON nếu không cho chớp — set 1 lần rồi thôi.
        if (!ringLightBlinks) SetRingLights(true);

        while (true)
        {
            on = !on;

            // Nhịp TỐI vẫn giữ đèn sáng ở mức nền 'warningMinLevel' (chỉ tắt hẳn khi mức nền = 0).
            float level = on ? 1f : warningMinLevel;

            if (redWarningLights != null)
            {
                foreach (var l in redWarningLights)
                {
                    if (l == null) continue;
                    l.color = warningLightColor;
                    l.intensity = (warningLightIntensity > 0f ? warningLightIntensity : 1f) * level;
                    l.enabled = level > 0f;
                }
            }

            if (ringLightBlinks)
            {
                for (int i = 0; i < (pillarRingLightRenderers?.Length ?? 0); i++)
                    SetRingLightLevel(i, level);
            }

            yield return new WaitForSeconds(warningBlinkInterval);
        }
    }

    private bool HasAnyRingRenderer()
    {
        if (pillarRingLightRenderers == null) return false;
        foreach (var r in pillarRingLightRenderers)
            if (r != null) return true;
        return false;
    }

    /// <summary>Đổi màu + emission của đèn vòng trên trụ (khác hẳn thân trụ: thân trụ tối đi, ring light đỏ chớp).</summary>
    private void SetRingLights(bool on)
    {
        if (pillarRingLightRenderers == null) return;

        float level = on ? 1f : 0f;
        for (int i = 0; i < pillarRingLightRenderers.Length; i++)
            SetRingLightLevel(i, level);
    }

    /// <summary>Đặt độ sáng (0 = màu OFF, 1 = màu ON) cho RIÊNG 1 ring light — dùng cho hiệu ứng chạy xung.</summary>
    private void SetRingLightLevel(int index, float level)
    {
        var groups = GetMaterials(pillarRingLightRenderers, ref _ringMats);
        if (index < 0 || index >= groups.Length) return;

        level = Mathf.Clamp01(level);
        Color baseColor = Color.Lerp(ringLightOffColor, ringLightOnColor, level);
        Color emissive = Color.Lerp(
            ringLightOffColor * ringLightOffEmissionIntensity,
            ringLightOnColor * ringLightEmissionIntensity,
            level);

        foreach (var mat in groups[index])
            ApplyEmission(mat, baseColor, emissive);
    }

    private void UpdateProgressUI()
    {
        if (progressText == null) return;
        int total = _shutdownOrder.Count > 0
            ? _shutdownOrder.Count
            : (serverBlocks != null ? serverBlocks.Length : 0);
        progressText.text = $"Server đã tắt: {_currentStep} / {total}";
    }
}