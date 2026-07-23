using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

public class WirePuzzleManager : MonoBehaviour
{
    [Header("Điểm nối dây")]
    [Tooltip("Để trống = tự động tìm tất cả WireConnectionPoint trong children.")]
    public WireConnectionPoint[] connectionPoints;

    [Header("Vẽ dây (3D Capsule)")]
    [Tooltip("RectTransform cha chứa các dây. Tạo Empty GO tên 'LineContainer' trong Canvas.")]
    public RectTransform lineContainer;

    [Tooltip("Prefab 3D Capsule làm đoạn dây. (GameObject > 3D Object > Capsule).")]
    public Transform wirePrefab3D;

    [Tooltip("Độ dày dây (Scale trục X và Z của Capsule).")]
    public float wireThickness = 15f;

    [Tooltip("Bán kính dot tròn (pixel). Dùng để tham khảo.")]
    public float dotRadius = 0f;

    [Tooltip("Rút ngắn 2 đầu dây một khoảng RẤT NHỎ. Để 0 nếu muốn dây chạm thẳng vào đúng tâm.")]
    public float lineEndTrim = 0f;

    [Header("Model dây thật (nếu KHÔNG dùng Capsule mặc định)")]
    [Tooltip("Tên GameObject CON bên trong wirePrefab3D đại diện cho phần VỎ dây (phần sẽ đổi màu theo wireId). " +
             "VD prefab có cấu trúc: WireModel > Vo (Mesh phần vỏ nhựa), DauNoi (đầu jack kim loại, KHÔNG đổi màu). " +
             "Nhập đúng tên 'Vo' vào đây. Để TRỐNG = giữ hành vi cũ: tự lấy Renderer đầu tiên tìm thấy và tô màu cả nó.")]
    public string wireShellChildName = "";

    [Tooltip("Dùng khi phần vỏ và các phần khác (đầu nối, chữ in...) nằm CHUNG 1 Renderer/1 Mesh nhưng " +
             "KHÁC material slot (Element 0, Element 1...). Nhập số thứ tự slot của vật liệu VỎ (0, 1, 2...). " +
             "Để -1 nếu không dùng cách này (chỉ áp dụng khi wireShellChildName để trống).")]
    public int wireShellMaterialIndex = -1;

    [Tooltip("Chiều dài GỐC của model dây theo trục Y (world units, lúc Scale = 1), tính từ đầu này sang đầu kia. " +
             "Capsule mặc định của Unity = 2. Nếu dùng model thật, dùng công cụ đo trong Scene (hoặc xem Size Z/Y " +
             "trong Mesh Filter > Bounds khi Scale=1) rồi nhập đúng số đó vào, nếu không dây sẽ bị kéo dài/ngắn sai tỉ lệ.")]
    public float wireModelBaseLength = 2f;

    [Header("Màu sắc theo wireId")]
    public WireColorEntry[] wireColors =
    {
        new WireColorEntry { wireId = "red",    color = Color.red    },
        new WireColorEntry { wireId = "yellow", color = Color.yellow },
        new WireColorEntry { wireId = "blue",   color = new Color(0.2f,0.5f,1f) },
        new WireColorEntry { wireId = "green",  color = Color.green  },
    };

    [Header("Camera riêng của minigame (World Space)")]
    public Camera minigameCamera;

    [Header("Canvas tham chiếu")]
    public Canvas parentCanvas;

    [Header("UI phụ trợ")]
    public GameObject completePanel;

    [Header("Âm thanh")]
    public AudioSource sfxSource;
    public AudioClip sfxDragStart;
    public AudioClip sfxConnectCorrect;
    public AudioClip sfxConnectWrong;
    public AudioClip sfxComplete;

    [Header("Trí nhớ màu — ẩn màu sau X giây")]
    public bool enableMemoryMode = false;

    public enum RevealSide { Both, SourceOnly, TargetOnly }

    public RevealSide hideSide = RevealSide.Both;
    public float revealDuration = 3f;
    public Color hiddenColor = new Color(0.8f, 0.8f, 0.8f, 1f);

    [Header("Phạt khi nối sai")]
    public float wrongConnectDamage = 10f;
    public bool resetOnWrongConnect = false;
    public AudioClip sfxElectricShock;

    [Header("Hiệu ứng giật điện khi nối sai")]
    public bool enableShakeOnWrongConnect = true;
    public Camera shakeCamera;
    public float shakeMagnitude = 0.05f;
    public float shakeDuration = 0.25f;

    public bool enableFlashOnWrongConnect = true;
    public Image electricFlashOverlay;
    public Color electricFlashColor = new Color(0.7f, 0.9f, 1f, 0.85f);
    public int flashBlinkCount = 3;
    public float flashDuration = 0.3f;

    public ParticleSystem electricSparkParticles;
    public float sparkParticlesMaxDuration = 0.3f;

    public event Action OnPuzzleCompleted;

    [Serializable]
    public struct WireColorEntry
    {
        public string wireId;
        public Color  color;
    }

    private readonly Dictionary<string, Color>     _colorMap    = new();
    private readonly Dictionary<string, Transform> _activeLines = new();

    private WireConnectionPoint _dragSource;
    private Transform           _previewLine;

    private int  _totalWires;
    private int  _connectedCount;
    private bool _completed;

    private Coroutine _revealCoroutine;
    private Coroutine _shakeCoroutine;
    private Coroutine _flashCoroutine;
    private Coroutine _sparkAutoStopCoroutine;
    private Vector3   _shakeCamOriginalLocalPos;
    private bool      _isShaking;


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
            if (seenIds.Add(p.wireId))
                _totalWires++;
        }

        ApplyColors();

        if (dotRadius <= 0f && connectionPoints.Length > 0)
        {
            var firstRect = connectionPoints[0].GetComponent<RectTransform>();
            if (firstRect != null)
                dotRadius = firstRect.sizeDelta.x * 0.5f;
        }

        if (lineContainer != null)
            lineContainer.SetAsFirstSibling();
    }

    private void OnEnable()
    {
        ResetPuzzle();
    }

    private void OnDisable()
    {
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
        if (_dragSource != null && _previewLine != null)
        {
            Vector3 mouseLocal = ScreenToLocal(Input.mousePosition);
            Color   col        = _colorMap.GetValueOrDefault(_dragSource.wireId, Color.white);
            DrawLine3D(_previewLine, GetLocalPos(_dragSource.RectTransform), mouseLocal, col);
        }

        if (_dragSource != null && Input.GetMouseButtonUp(0))
            EndDrag();
    }

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
    }

    public void SetHoverTarget(WireConnectionPoint target)  { }
    public void ClearHoverTarget(WireConnectionPoint target) { }

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

    private void HandleWrongConnect(WireConnectionPoint source, WireConnectionPoint target)
    {
        PlaySfx(sfxConnectWrong);
        PlaySfx(sfxElectricShock);

        if (PlayerHealth.Instance != null)
            PlayerHealth.Instance.TakeDamage(wrongConnectDamage);

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

    private void ConfirmConnection(WireConnectionPoint source, WireConnectionPoint target)
    {
        source.isConnected = true;
        target.isConnected = true;

        DrawLine3D(_previewLine,
                 GetLocalPos(source.RectTransform),
                 GetLocalPos(target.RectTransform),
                 _colorMap.GetValueOrDefault(source.wireId, Color.white));

        _activeLines[source.wireId] = _previewLine;
        _previewLine = null; 

        _connectedCount++;

        PlaySfx(sfxConnectCorrect);

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

        ApplyColors();

        if (_revealCoroutine != null)
        {
            StopCoroutine(_revealCoroutine);
            _revealCoroutine = null;
        }

        if (enableMemoryMode)
            _revealCoroutine = StartCoroutine(RevealThenHideRoutine());
    }

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


    private Transform CreateLineInstance()
    {
        var line = Instantiate(wirePrefab3D, lineContainer);
        line.gameObject.SetActive(true);
        line.SetAsFirstSibling();
        return line;
    }

    private void DrawLine3D(Transform line, Vector3 from, Vector3 to, Color color)
    {
        if (line == null) return;

        Vector3 dir = to - from;
        float fullDist = dir.magnitude;
        if (fullDist < 0.0001f) return;

        Vector3 dirNorm = dir / fullDist;
        
        float inset = Mathf.Min(lineEndTrim, fullDist * 0.45f);
        Vector3 adjustedFrom = from + dirNorm * inset;
        float distance = Mathf.Max(0f, fullDist - inset * 2f);

        Vector3 midPoint = adjustedFrom + dirNorm * (distance * 0.5f);
        line.localPosition = midPoint;

        line.localRotation = Quaternion.FromToRotation(Vector3.up, dirNorm);

        float baseLength = Mathf.Max(0.0001f, wireModelBaseLength);
        line.localScale = new Vector3(wireThickness, distance / baseLength, wireThickness);

        ApplyWireColor(line, color);
    }

    /// <summary>
    /// Tô màu cho instance dây vừa dựng. Ưu tiên tìm đúng GameObject con tên
    /// wireShellChildName (phần vỏ) để CHỈ tô riêng phần đó. Nếu không tìm thấy
    /// (hoặc không cấu hình), fallback về Renderer đầu tiên tìm được — có thể
    /// tô theo material slot cụ thể (wireShellMaterialIndex) nếu vỏ và các phần
    /// khác dùng chung 1 Renderer nhưng khác slot vật liệu.
    /// </summary>
    private void ApplyWireColor(Transform line, Color color)
    {
        Renderer targetRenderer = null;
        bool     useSlotIndex   = false;

        if (!string.IsNullOrEmpty(wireShellChildName))
        {
            var shellChild = FindChildRecursive(line, wireShellChildName);
            if (shellChild != null)
                targetRenderer = shellChild.GetComponent<Renderer>();

            if (targetRenderer == null)
                Debug.LogWarning($"[WirePuzzleManager] Không tìm thấy child '{wireShellChildName}' " +
                                  $"(hoặc child đó không có Renderer) trong '{line.name}'. " +
                                  $"Đang fallback về Renderer đầu tiên.", line);
        }

        if (targetRenderer == null)
        {
            targetRenderer = line.GetComponentInChildren<Renderer>();
            useSlotIndex   = wireShellMaterialIndex >= 0;
        }

        if (targetRenderer == null) return;

        if (useSlotIndex && wireShellMaterialIndex < targetRenderer.materials.Length)
        {
            var mats = targetRenderer.materials; // tạo bản instance riêng cho renderer này
            var mat  = mats[wireShellMaterialIndex];
            mat.color = color;
            mat.EnableKeyword("_EMISSION");
            mat.SetColor("_EmissionColor", color * 2.5f);
            targetRenderer.materials = mats;
        }
        else
        {
            var mat = targetRenderer.material; // instance của slot 0
            mat.color = color;
            mat.EnableKeyword("_EMISSION");
            mat.SetColor("_EmissionColor", color * 2.5f);
        }
    }

    /// <summary>Tìm đệ quy 1 child theo tên chính xác (không phân biệt cấp con/cháu).</summary>
    private Transform FindChildRecursive(Transform parent, string name)
    {
        foreach (Transform child in parent)
        {
            if (child.name == name) return child;
            var found = FindChildRecursive(child, name);
            if (found != null) return found;
        }
        return null;
    }

    private static readonly Vector3[] _cornersBuffer = new Vector3[4];

    private Vector3 GetLocalPos(RectTransform target)
    {
        target.GetWorldCorners(_cornersBuffer);
        Vector3 worldCenter = (_cornersBuffer[0] + _cornersBuffer[2]) * 0.5f;
        
  
        return lineContainer.InverseTransformPoint(worldCenter);
    }

    private Vector3 ScreenToLocal(Vector3 screenPos)
    {
        Camera cam = GetEventCamera();

        if (RectTransformUtility.ScreenPointToWorldPointInRectangle(
                lineContainer, screenPos, cam, out Vector3 worldPt))
        {
            return lineContainer.InverseTransformPoint(worldPt);
        }

        RectTransformUtility.ScreenPointToLocalPointInRectangle(
            lineContainer, screenPos, cam, out Vector2 localPt);
        return (Vector3)localPt;
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