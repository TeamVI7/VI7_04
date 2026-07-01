using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

/// <summary>
/// Quản lý puzzle nối dây — hỗ trợ World Space Canvas với camera riêng.
///
/// FIX BUG "không dính":
///   Thay vì dùng OnPointerEnter/Exit (bị mất khi Image dây chặn event),
///   EndDrag() tự raycast tại vị trí chuột để tìm điểm đích — chắc chắn 100%.
/// </summary>
public class WirePuzzleManager : MonoBehaviour
{
    [Header("Điểm nối dây")]
    [Tooltip("Để trống = tự động tìm tất cả WireConnectionPoint trong children.")]
    public WireConnectionPoint[] connectionPoints;

    [Header("Vẽ dây")]
    [Tooltip("RectTransform cha chứa các Image dây. Tạo Empty GO tên 'LineContainer' trong Canvas.")]
    public RectTransform lineContainer;

    [Tooltip("Prefab UI Image làm đoạn dây. Pivot = (0, 0.5). Anchor = top-left.")]
    public RectTransform wireLinePrefab;

    [Tooltip("Độ dày dây (pixel trong Canvas space).")]
    public float wireThickness = 8f;

    [Tooltip("Bán kính dot tròn (pixel) — CHỈ dùng để tham khảo/debug, KHÔNG còn dùng để " +
             "rút 2 đầu dây vào trong nữa (trước đây dùng làm inset khiến dây dừng cách " +
             "tâm hình tròn đúng bằng cả bán kính, nhìn như 'không vào giữa'). " +
             "Để 0 = tự động đọc từ sizeDelta của điểm nối đầu tiên lúc Awake.")]
    public float dotRadius = 0f;

    [Tooltip("Rút ngắn 2 đầu dây một khoảng RẤT NHỎ (px) — chỉ để giấu góc vuông của hình " +
             "chữ nhật bên dưới viền tròn mỏng, KHÔNG phải bán kính dot. Để 0 nếu muốn dây " +
             "chạm thẳng vào đúng tâm hình tròn (khuyến nghị nếu viền dot mỏng/trong suốt).")]
    public float lineEndTrim = 0f;

    [Header("Màu sắc theo wireId")]
    public WireColorEntry[] wireColors =
    {
        new WireColorEntry { wireId = "red",    color = Color.red    },
        new WireColorEntry { wireId = "yellow", color = Color.yellow },
        new WireColorEntry { wireId = "blue",   color = new Color(0.2f,0.5f,1f) },
        new WireColorEntry { wireId = "green",  color = Color.green  },
    };

    [Header("Camera riêng của minigame (World Space)")]
    [Tooltip("Kéo minigame camera vào. Dùng để chuyển toạ độ chuột đúng trong World Space Canvas.")]
    public Camera minigameCamera;

    [Header("Canvas tham chiếu")]
    [Tooltip("Để trống = tự tìm Canvas cha. Cần gán đúng Canvas chứa wire puzzle.")]
    public Canvas parentCanvas;

    [Header("UI phụ trợ")]
    [Tooltip("Panel 'Hoàn thành!' hiện khi xong (tuỳ chọn).")]
    public GameObject completePanel;

    [Header("Âm thanh")]
    [Tooltip("AudioSource để phát SFX. Để trống = tự thêm AudioSource lên chính object này.")]
    public AudioSource sfxSource;

    [Tooltip("Tiếng phát ra khi BẮT ĐẦU kéo 1 dây.")]
    public AudioClip sfxDragStart;

    [Tooltip("Tiếng phát ra khi thả dây vào ĐÚNG điểm (đúng màu).")]
    public AudioClip sfxConnectCorrect;

    [Tooltip("Tiếng phát ra khi thả dây vào SAI điểm (sai màu).")]
    public AudioClip sfxConnectWrong;

    [Tooltip("Tiếng phát ra khi HOÀN THÀNH toàn bộ puzzle.")]
    public AudioClip sfxComplete;

    [Header("Trí nhớ màu — ẩn màu sau X giây")]
    [Tooltip("Bật chế độ 'nhớ vị trí màu': hiện màu thật trong revealDuration giây, " +
             "sau đó các điểm thuộc nhóm bị ẩn chuyển về 1 màu trung tính.")]
    public bool enableMemoryMode = false;

    public enum RevealSide { Both, SourceOnly, TargetOnly }

    [Tooltip("Bên nào sẽ bị ẩn màu sau khi hết revealDuration. Bên còn lại vẫn giữ màu thật luôn.")]
    public RevealSide hideSide = RevealSide.Both;

    [Tooltip("Số giây hiện màu thật trước khi ẩn (đổi về màu trung tính).")]
    public float revealDuration = 3f;

    [Tooltip("Màu trung tính dùng để 'che' tất cả điểm nối thuộc nhóm bị ẩn (trắng/xám...).")]
    public Color hiddenColor = new Color(0.8f, 0.8f, 0.8f, 1f);

    [Header("Phạt khi nối sai")]
    [Tooltip("Số máu trừ khi nối sai màu. Gọi PlayerHealth.Instance.TakeDamage(...).")]
    public float wrongConnectDamage = 10f;

    [Tooltip("Tick = nối sai sẽ RESET toàn bộ puzzle (xoá hết dây đã nối, làm lại từ đầu). " +
             "Không tick = chỉ trừ máu, giữ nguyên các dây đã nối đúng, cho thử lại ngay.")]
    public bool resetOnWrongConnect = false;

    [Tooltip("Tiếng 'giật điện' phát khi nối sai (tách riêng khỏi sfxConnectWrong để có thể " +
             "dùng cả 2 cùng lúc — ví dụ 1 tiếng 'tách' lỗi + 1 tiếng giật điện riêng).")]
    public AudioClip sfxElectricShock;

    [Header("Hiệu ứng giật điện khi nối sai")]
    [Tooltip("Bật rung camera minigame khi nối sai.")]
    public bool enableShakeOnWrongConnect = true;

    [Tooltip("Camera sẽ bị rung. Để trống = tự dùng minigameCamera.")]
    public Camera shakeCamera;

    [Tooltip("Biên độ rung camera (độ lệch local position tối đa, đơn vị world unit).")]
    public float shakeMagnitude = 0.05f;

    [Tooltip("Thời gian rung camera (giây).")]
    public float shakeDuration = 0.25f;

    [Tooltip("Bật chớp màu (flash) toàn màn hình puzzle khi nối sai, mô phỏng giật điện.")]
    public bool enableFlashOnWrongConnect = true;

    [Tooltip("Image phủ toàn Canvas dùng để chớp màu (kéo 1 UI Image full-screen, alpha=0 sẵn). " +
             "Để trống = bỏ qua flash dù có tick enableFlashOnWrongConnect.")]
    public Image electricFlashOverlay;

    [Tooltip("Màu chớp khi giật điện (mặc định trắng-xanh điện).")]
    public Color electricFlashColor = new Color(0.7f, 0.9f, 1f, 0.85f);

    [Tooltip("Số lần chớp (nhấp nháy) trong 1 lần giật điện.")]
    public int flashBlinkCount = 3;

    [Tooltip("Tổng thời gian toàn bộ hiệu ứng chớp (giây).")]
    public float flashDuration = 0.3f;

    [Tooltip("Particle System hiệu ứng tia điện/spark — đặt sẵn trong scene (ví dụ ngay trên " +
             "bảng nối dây), để OFF/không Play sẵn. Để trống = bỏ qua. Có thể dùng cùng lúc " +
             "với electricFlashOverlay (2 hiệu ứng độc lập, không loại trừ nhau).")]
    public ParticleSystem electricSparkParticles;

    [Tooltip("Thời gian particle được phép chạy trước khi TỰ ĐỘNG bị Stop (giây). " +
             "Lưới an toàn: dù Particle System có bật 'Looping' (khiến Play() chạy mãi không " +
             "tự tắt), script vẫn ép Stop sau khoảng này. Khuyến nghị vẫn nên tắt Looping trên " +
             "Particle System trong Inspector cho gọn; field này chỉ là dự phòng.")]
    public float sparkParticlesMaxDuration = 0.3f;

    public event Action OnPuzzleCompleted;

    [Serializable]
    public struct WireColorEntry
    {
        public string wireId;
        public Color  color;
    }

    // ── Internal state ──────────────────────────────────────────────
    private readonly Dictionary<string, Color>         _colorMap    = new();
    private readonly Dictionary<string, RectTransform> _activeLines = new();

    private WireConnectionPoint _dragSource;
    private RectTransform       _previewLine;

    private int  _totalWires;
    private int  _connectedCount;
    private bool _completed;

    // ───────────────────────────────────────────────────────────────

    private void Awake()
    {
        if (sfxSource == null)
        {
            sfxSource = GetComponent<AudioSource>();
            if (sfxSource == null)
                sfxSource = gameObject.AddComponent<AudioSource>();
            sfxSource.playOnAwake = false;
        }

        if (parentCanvas == null)
            parentCanvas = GetComponentInParent<Canvas>();

        // Tự tìm minigame camera từ Canvas nếu chưa gán
        if (minigameCamera == null && parentCanvas != null)
            minigameCamera = parentCanvas.worldCamera;

        foreach (var entry in wireColors)
            _colorMap[entry.wireId] = entry.color;

        if (connectionPoints == null || connectionPoints.Length == 0)
            connectionPoints = GetComponentsInChildren<WireConnectionPoint>(true);

        foreach (var p in connectionPoints)
            p.Init(this);

        var seenIds = new HashSet<string>();
        _totalWires = 0;
        foreach (var p in connectionPoints)
        {
            if (p == null || !p.isSourceSide) continue;
            // Đếm theo wireId duy nhất — nếu có điểm nguồn bị trùng/dư (object cũ
            // còn sót lại trong scene, đang ẩn) thì cũng KHÔNG bị tính dư, tránh bug "4/5".
            if (seenIds.Add(p.wireId))
                _totalWires++;
        }

        ApplyColors();

        // Tự động đọc bán kính dot từ điểm nối đầu tiên nếu chưa set
        if (dotRadius <= 0f && connectionPoints.Length > 0)
        {
            var firstRect = connectionPoints[0].GetComponent<RectTransform>();
            if (firstRect != null)
                dotRadius = firstRect.sizeDelta.x * 0.5f;
        }

        // Đảm bảo LineContainer luôn render DƯỚI tất cả các dot tròn —
        // để viền tròn của điểm nối tự che 2 đầu chữ nhật của dây.
        if (lineContainer != null)
            lineContainer.SetAsFirstSibling();
    }

    private Coroutine _revealCoroutine;
    private Coroutine _shakeCoroutine;
    private Coroutine _flashCoroutine;
    private Coroutine _sparkAutoStopCoroutine;
    private Vector3   _shakeCamOriginalLocalPos;
    private bool      _isShaking;

    private void OnEnable()
    {
        ResetPuzzle();
    }

    private void OnDisable()
    {
        // Đóng UI giữa lúc đang rung/chớp — Unity tự dừng coroutine khi object bị
        // disable, nhưng camera/overlay có thể bị "kẹt" giữa hiệu ứng. Trả về sạch.
        if (_isShaking)
        {
            Camera cam = shakeCamera != null ? shakeCamera : minigameCamera;
            if (cam != null) cam.transform.localPosition = _shakeCamOriginalLocalPos;
            _isShaking = false;
        }

        if (electricFlashOverlay != null)
        {
            Color c = electricFlashOverlay.color;
            electricFlashOverlay.color = new Color(c.r, c.g, c.b, 0f);
        }

        if (electricSparkParticles != null)
            electricSparkParticles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        if (_sparkAutoStopCoroutine != null)
        {
            StopCoroutine(_sparkAutoStopCoroutine);
            _sparkAutoStopCoroutine = null;
        }

        _shakeCoroutine = null;
        _flashCoroutine = null;
    }

    private void Update()
    {
        // Cập nhật dây preview theo chuột
        if (_dragSource != null && _previewLine != null)
        {
            Vector2 mouseLocal = ScreenToLocal(Input.mousePosition);
            Color   col        = _colorMap.GetValueOrDefault(_dragSource.wireId, Color.white);
            DrawLine(_previewLine, GetLocalPos(_dragSource.RectTransform), mouseLocal, col);
        }

        // Thả chuột (phát hiện ở Update để không bị block bởi Image dây che)
        if (_dragSource != null && Input.GetMouseButtonUp(0))
            EndDrag();
    }

    // ── Helpers ──────────────────────────────────────────────────────

    private void PlaySfx(AudioClip clip)
    {
        if (clip != null && sfxSource != null)
            sfxSource.PlayOneShot(clip);
    }

    public void BeginDrag(WireConnectionPoint source)
    {
        if (_completed || source.isConnected) return;
        _dragSource  = source;
        _previewLine = CreateLineInstance();
        PlaySfx(sfxDragStart);
        Debug.Log($"[WirePuzzle] Bắt đầu kéo dây '{source.wireId}'");
    }

    // OnPointerEnter/Exit giữ nguyên làm hint nhưng KHÔNG dùng để xác định kết nối
    public void SetHoverTarget(WireConnectionPoint target)  { /* hint only */ }
    public void ClearHoverTarget(WireConnectionPoint target) { /* hint only */ }

    /// <summary>
    /// Gọi từ Update khi nhả chuột. Tự raycast tìm điểm đích — tránh bug hover bị clear.
    /// </summary>
    private void EndDrag()
    {
        if (_dragSource == null) return;

        WireConnectionPoint target = FindTargetUnderMouse();
        bool success = false;

        if (target != null && !target.isConnected && !target.isSourceSide)
        {
            if (target.wireId == _dragSource.wireId)
            {
                ConfirmConnection(_dragSource, target);
                success = true;
            }
            else
            {
                HandleWrongConnect(_dragSource, target);
            }
        }

        if (!success && _previewLine != null)
            Destroy(_previewLine.gameObject);

        _previewLine = null;
        _dragSource  = null;
    }

    /// <summary>
    /// Nối sai màu: trừ máu người chơi, rồi tuỳ resetOnWrongConnect mà
    /// xoá hết puzzle làm lại từ đầu, hoặc chỉ huỷ dây đang kéo và cho thử lại ngay.
    /// </summary>
    private void HandleWrongConnect(WireConnectionPoint source, WireConnectionPoint target)
    {
        PlaySfx(sfxConnectWrong);
        PlaySfx(sfxElectricShock);
        Debug.Log($"[WirePuzzle] ❌ Sai màu: {source.wireId} → {target.wireId}");

        if (PlayerHealth.Instance != null)
            PlayerHealth.Instance.TakeDamage(wrongConnectDamage);
        else
            Debug.LogWarning("[WirePuzzle] Không tìm thấy PlayerHealth.Instance — không trừ máu được.");

        if (enableShakeOnWrongConnect)
            TriggerCameraShake();

        if (enableFlashOnWrongConnect)
            TriggerElectricFlash();

        TriggerSparkParticles();

        if (resetOnWrongConnect)
        {
            ResetPuzzle();
        }
    }

    /// <summary>
    /// Phát hiệu ứng tia điện (Particle System) nếu có gán. Dùng Stop + Clear
    /// trước khi Play để đảm bảo phát lại trọn vẹn dù nối sai liên tiếp nhanh.
    /// Sau đó LUÔN lên lịch tự Stop sau sparkParticlesMaxDuration giây — đây là
    /// lưới an toàn cho trường hợp Particle System bị bật "Looping" trong
    /// Inspector (khiến nó phát mãi không tự tắt nếu không ai gọi Stop()).
    /// </summary>
    private void TriggerSparkParticles()
    {
        if (electricSparkParticles == null) return;

        if (_sparkAutoStopCoroutine != null)
        {
            StopCoroutine(_sparkAutoStopCoroutine);
            _sparkAutoStopCoroutine = null;
        }

        electricSparkParticles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        electricSparkParticles.Play(true);

        _sparkAutoStopCoroutine = StartCoroutine(AutoStopSparkRoutine());
    }

    private IEnumerator AutoStopSparkRoutine()
    {
        yield return new WaitForSeconds(Mathf.Max(0.01f, sparkParticlesMaxDuration));

        if (electricSparkParticles != null)
            electricSparkParticles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        _sparkAutoStopCoroutine = null;
    }

    /// <summary>
    /// Rung nhẹ camera minigame để mô phỏng cú giật điện. Lưu lại vị trí gốc
    /// lần đầu chạy (hoặc khi không còn coroutine nào đang chạy) để tránh
    /// camera bị "trôi" dần qua nhiều lần nối sai liên tiếp.
    /// </summary>
    private void TriggerCameraShake()
    {
        Camera cam = shakeCamera != null ? shakeCamera : minigameCamera;
        if (cam == null) return;

        if (!_isShaking)
            _shakeCamOriginalLocalPos = cam.transform.localPosition;

        if (_shakeCoroutine != null)
            StopCoroutine(_shakeCoroutine);

        _shakeCoroutine = StartCoroutine(ShakeRoutine(cam));
    }

    private IEnumerator ShakeRoutine(Camera cam)
    {
        _isShaking = true;
        float elapsed = 0f;

        while (elapsed < shakeDuration)
        {
            elapsed += Time.deltaTime;
            float damper = 1f - Mathf.Clamp01(elapsed / shakeDuration);

            Vector2 offset = UnityEngine.Random.insideUnitCircle * shakeMagnitude * damper;
            cam.transform.localPosition = _shakeCamOriginalLocalPos + new Vector3(offset.x, offset.y, 0f);

            yield return null;
        }

        cam.transform.localPosition = _shakeCamOriginalLocalPos;
        _isShaking       = false;
        _shakeCoroutine  = null;
    }

    /// <summary>
    /// Chớp màu (flash) toàn màn hình minigame vài lần liên tiếp để mô phỏng
    /// hiệu ứng "giật điện" — tăng/giảm alpha của electricFlashOverlay.
    /// </summary>
    private void TriggerElectricFlash()
    {
        if (electricFlashOverlay == null) return;

        if (_flashCoroutine != null)
        {
            StopCoroutine(_flashCoroutine);
            _flashCoroutine = null;
        }

        _flashCoroutine = StartCoroutine(FlashRoutine());
    }

    private IEnumerator FlashRoutine()
    {
        int   blinks   = Mathf.Max(1, flashBlinkCount);
        float perBlink = flashDuration / blinks;
        float halfStep = perBlink * 0.5f;

        Color onColor  = electricFlashColor;
        Color offColor = new Color(electricFlashColor.r, electricFlashColor.g, electricFlashColor.b, 0f);

        for (int i = 0; i < blinks; i++)
        {
            electricFlashOverlay.color = onColor;
            yield return new WaitForSeconds(halfStep);

            electricFlashOverlay.color = offColor;
            yield return new WaitForSeconds(halfStep);
        }

        electricFlashOverlay.color = offColor;
        _flashCoroutine = null;
    }

    // ── Core logic ──────────────────────────────────────────────────

    private void ConfirmConnection(WireConnectionPoint source, WireConnectionPoint target)
    {
        source.isConnected = true;
        target.isConnected = true;

        DrawLine(_previewLine,
                 GetLocalPos(source.RectTransform),
                 GetLocalPos(target.RectTransform),
                 _colorMap.GetValueOrDefault(source.wireId, Color.white));

        _activeLines[source.wireId] = _previewLine;
        _previewLine = null; // chốt — không bị Destroy

        _connectedCount++;
        PlaySfx(sfxConnectCorrect);
        Debug.Log($"[WirePuzzle] ✅ Nối đúng '{source.wireId}' ({_connectedCount}/{_totalWires})");

        if (_connectedCount >= _totalWires)
            CompletePuzzle();
    }

    private void CompletePuzzle()
    {
        if (_completed) return;
        _completed = true;

        if (completePanel != null)
            completePanel.SetActive(true);

        PlaySfx(sfxComplete);
        Debug.Log("[WirePuzzle] 🎉 HOÀN THÀNH!");
        OnPuzzleCompleted?.Invoke();
    }

    public void ResetPuzzle()
    {
        _completed      = false;
        _connectedCount = 0;
        _dragSource     = null;

        foreach (var line in _activeLines.Values)
            if (line != null) Destroy(line.gameObject);
        _activeLines.Clear();

        if (_previewLine != null) { Destroy(_previewLine.gameObject); _previewLine = null; }

        foreach (var p in connectionPoints)
            if (p != null) p.isConnected = false;

        if (completePanel != null)
            completePanel.SetActive(false);

        // Trả tất cả điểm nối về màu thật, rồi (nếu bật memory mode) đếm lại
        // thời gian reveal từ đầu — mỗi lần reset là 1 lần "học thuộc lại".
        ApplyColors();

        if (_revealCoroutine != null)
        {
            StopCoroutine(_revealCoroutine);
            _revealCoroutine = null;
        }

        if (enableMemoryMode)
            _revealCoroutine = StartCoroutine(RevealThenHideRoutine());
    }

    /// <summary>
    /// Hiện màu thật trong revealDuration giây, sau đó đổi điểm nối thuộc
    /// nhóm hideSide (Source/Target/Both) sang hiddenColor. Không khoá tương
    /// tác — người chơi vẫn kéo/thả được như thường, chỉ là không còn thấy màu.
    /// </summary>
    private IEnumerator RevealThenHideRoutine()
    {
        yield return new WaitForSeconds(revealDuration);

        foreach (var p in connectionPoints)
        {
            if (p == null || p.isConnected) continue;

            bool shouldHide =
                hideSide == RevealSide.Both ||
                (hideSide == RevealSide.SourceOnly && p.isSourceSide) ||
                (hideSide == RevealSide.TargetOnly && !p.isSourceSide);

            if (!shouldHide) continue;

            var img = p.GetComponent<Image>();
            if (img != null) img.color = hiddenColor;
        }

        _revealCoroutine = null;
    }

    // ── Raycast tìm điểm đích dưới chuột ───────────────────────────

    /// <summary>
    /// Dùng EventSystem.RaycastAll để tìm WireConnectionPoint ngay dưới con trỏ.
    /// Hoạt động đúng với cả Screen Space và World Space Canvas.
    /// </summary>
    private WireConnectionPoint FindTargetUnderMouse()
    {
        if (EventSystem.current == null) return null;

        var pointer = new PointerEventData(EventSystem.current)
        {
            position = Input.mousePosition
        };

        var results = new List<RaycastResult>();
        EventSystem.current.RaycastAll(pointer, results);

        foreach (var r in results)
        {
            var pt = r.gameObject.GetComponent<WireConnectionPoint>();
            if (pt != null) return pt;
        }
        return null;
    }

    // ── Màu điểm nối ────────────────────────────────────────────────

    private void ApplyColors()
    {
        foreach (var p in connectionPoints)
        {
            if (p == null) continue;
            var img = p.GetComponent<Image>();
            if (img != null && _colorMap.TryGetValue(p.wireId, out var c))
                img.color = c;
        }
    }

    // ── Vẽ dây ──────────────────────────────────────────────────────

    private RectTransform CreateLineInstance()
    {
        var line = Instantiate(wireLinePrefab, lineContainer);
        line.pivot         = new Vector2(0f, 0.5f);
        line.anchorMin     = Vector2.zero;
        line.anchorMax     = Vector2.zero;
        line.localScale    = Vector3.one; // QUAN TRỌNG: prefab có sẵn scale (1, 0.5, 1) —
                                           // nếu không reset, rotate quanh pivot bị méo
                                           // khiến đầu dây lệch khỏi điểm nối một khoảng nhỏ.
        line.gameObject.SetActive(true);

        // FIX "ĐUÔI NHỌN" LÒI RA NGOÀI CHẤM TRÒN:
        // Instantiate() luôn thêm object mới vào CUỐI danh sách con → bị vẽ
        // (render) TRÊN tất cả các điểm nối (dot tròn) đã có sẵn trong scene.
        // Vì đầu dây là hình chữ nhật (góc vuông), khi nằm trên dot tròn thì góc
        // vuông đó sẽ lòi ra ngoài viền tròn — đó chính là cái "đuôi nhọn" thấy được.
        // Đẩy nó về sibling ĐẦU TIÊN (vẽ trước = nằm DƯỚI mọi thứ khác trong cùng
        // parent) để các dot tròn luôn che kín phần đầu dây, không còn lòi ra nữa.
        line.SetAsFirstSibling();

        return line;
    }

    private void DrawLine(RectTransform line, Vector2 from, Vector2 to, Color color)
    {
        if (line == null) return;

        Vector2 dir          = to - from;
        float   fullDist     = dir.magnitude;
        if (fullDist < 0.0001f) return;

        Vector2 dirNorm = dir / fullDist;
        float   angle   = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;

        // Trước đây: inset = dotRadius -> dây luôn dừng cách tâm đúng bằng cả bán kính
        // chấm tròn, khiến đầu dây trông như "lệch khỏi tâm" / không chạm giữa hình tròn.
        // Sửa: chỉ rút một khoảng RẤT NHỎ (lineEndTrim) đủ để giấu góc vuông của hình
        // chữ nhật dưới viền tròn — KHÔNG dùng bán kính dot làm inset nữa.
        float   inset        = Mathf.Min(lineEndTrim, fullDist * 0.45f);
        Vector2 adjustedFrom = from + dirNorm * inset;
        float   distance     = Mathf.Max(0f, fullDist - inset * 2f);

        line.anchoredPosition = adjustedFrom;
        line.sizeDelta        = new Vector2(distance, wireThickness);
        line.localRotation    = Quaternion.Euler(0f, 0f, angle);

        var img = line.GetComponent<Image>();
        if (img != null) img.color = color;
    }

    // ── Coordinate helpers ───────────────────────────────────────────

    private static readonly Vector3[] _cornersBuffer = new Vector3[4];

    /// <summary>
    /// LẦN SỬA NÀY: bỏ hoàn toàn đường World → Screen → Local cũ.
    /// Lý do: WorldToScreenPoint rồi ScreenPointToLocalPointInRectangle phải
    /// đi qua không gian màn hình (pixel), làm tròn số (rounding) ở đó. Với
    /// Canvas World Space có scale rất nhỏ (vd 0.1), 1 pixel lệch trên màn
    /// hình lại tương ứng với một khoảng RẤT LỚN trong local space của
    /// lineContainer → đó chính là lý do dây bị lệch hẳn xuống đáy hình vuông
    /// trong ảnh thực tế, dù công thức tính tâm (GetWorldCorners) vẫn đúng.
    ///
    /// Cách mới: lấy tâm hình học bằng GetWorldCorners (vẫn đúng, không đổi),
    /// nhưng chuyển sang local space của lineContainer bằng
    /// InverseTransformPoint() — phép biến đổi affine thuần (không qua màn
    /// hình, không qua camera, không mất chính xác), rồi tự cộng offset
    /// pivot/anchor của lineContainer một lần. Kết quả chính xác tuyệt đối,
    /// không phụ thuộc camera hay scale của canvas.
    /// </summary>
    private Vector2 GetLocalPos(RectTransform target)
    {
        target.GetWorldCorners(_cornersBuffer);
        Vector3 worldCenter = (_cornersBuffer[0] + _cornersBuffer[2]) * 0.5f;
        return WorldPointToAnchoredPosition(worldCenter);
    }

    /// <summary>
    /// Chuyển 1 điểm world-space sang anchoredPosition của lineContainer,
    /// KHÔNG đi qua màn hình. anchoredPosition được tính từ vị trí ANCHOR
    /// (không phải pivot), nên sau InverseTransformPoint (vốn trả toạ độ
    /// theo PIVOT) phải cộng thêm độ lệch pivot↔anchor của lineContainer.
    /// </summary>
    private Vector2 WorldPointToAnchoredPosition(Vector3 worldPoint)
    {
        // Toạ độ theo PIVOT của lineContainer (local space, không qua camera).
        Vector3 localFromPivot = lineContainer.InverseTransformPoint(worldPoint);

        // Độ lệch giữa pivot và "điểm gốc anchoredPosition" (anchor min == anchor
        // max, trường hợp thường gặp khi lineContainer không stretch) — tính 1 lần
        // bằng rect.size * pivot, theo đúng định nghĩa anchoredPosition của Unity.
        Rect rect = lineContainer.rect;
        Vector2 pivotOffset = new Vector2(
            rect.width  * lineContainer.pivot.x,
            rect.height * lineContainer.pivot.y);

        return (Vector2)localFromPivot + pivotOffset;
    }

    private Vector2 ScreenToLocal(Vector3 screenPos)
    {
        Camera cam = GetEventCamera();

        // FIX "dây gãy góc khi kéo": trước đây dùng ScreenPointToLocalPointInRectangle,
        // hàm này trả toạ độ theo PIVOT của lineContainer — KHÁC với anchoredPosition mà
        // GetLocalPos()/DrawLine() đang dùng (qua WorldPointToAnchoredPosition). Nếu pivot
        // của lineContainer khác anchor (rất thường gặp), 2 đầu dây bị lệch nhau một
        // khoảng offset cố định → đầu theo chuột không khớp đầu theo điểm nối, nhìn như
        // dây bị gãy/lệch hướng giữa đường.
        //
        // Sửa: lấy world point dưới chuột bằng ScreenPointToWorldPointInRectangle, rồi
        // tái dùng đúng WorldPointToAnchoredPosition() — cùng một phép biến đổi với đầu
        // nguồn, đảm bảo 2 đầu luôn nhất quán, dây luôn là 1 đường thẳng theo đúng chuột.
        if (RectTransformUtility.ScreenPointToWorldPointInRectangle(
                lineContainer, screenPos, cam, out Vector3 worldPt))
        {
            return WorldPointToAnchoredPosition(worldPt);
        }

        // Fallback hiếm gặp (chuột song song mặt phẳng canvas) — giữ cách cũ.
        RectTransformUtility.ScreenPointToLocalPointInRectangle(
            lineContainer, screenPos, cam, out Vector2 localPt);
        return localPt;
    }

   
    private Camera GetEventCamera()
    {
        if (minigameCamera != null) return minigameCamera;
        if (parentCanvas == null)   return null;

        return parentCanvas.renderMode == RenderMode.ScreenSpaceOverlay
            ? null
            : parentCanvas.worldCamera;
    }
}