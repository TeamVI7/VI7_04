using System;
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
    }

    private void OnEnable()
    {
        ResetPuzzle();
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

    // ── Public API ──────────────────────────────────────────────────

    public void BeginDrag(WireConnectionPoint source)
    {
        if (_completed || source.isConnected) return;
        _dragSource  = source;
        _previewLine = CreateLineInstance();
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
                Debug.Log($"[WirePuzzle] Sai màu: {_dragSource.wireId} → {target.wireId}");
            }
        }

        if (!success && _previewLine != null)
            Destroy(_previewLine.gameObject);

        _previewLine = null;
        _dragSource  = null;
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
        return line;
    }

    private void DrawLine(RectTransform line, Vector2 from, Vector2 to, Color color)
    {
        if (line == null) return;

        Vector2 dir      = to - from;
        float   distance = dir.magnitude;
        float   angle    = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;

        line.anchoredPosition = from;
        line.sizeDelta        = new Vector2(distance, wireThickness);
        line.localRotation    = Quaternion.Euler(0f, 0f, angle);

        var img = line.GetComponent<Image>();
        if (img != null) img.color = color;
    }

    // ── Coordinate helpers ───────────────────────────────────────────


    private static readonly Vector3[] _cornersBuffer = new Vector3[4];

    private Vector2 GetLocalPos(RectTransform target)
    {
        // QUAN TRỌNG: không dùng lineContainer.InverseTransformPoint(target.position) —
        // hàm đó trả toạ độ theo PIVOT của lineContainer, còn anchoredPosition mà
        // DrawLine() set lại được tính theo ANCHOR. Nếu anchor != pivot của lineContainer,
        // mọi line bị lệch một khoảng cố định (chính là bug "dây không dính chỗ kéo").
        // Dùng World -> Screen -> Local giống ScreenToLocal() để luôn ra đúng hệ
        // anchoredPosition, bất kể anchor/pivot của lineContainer là gì.
        //
        // FIX LỆCH TÂM: không dùng target.position (vị trí PIVOT của điểm nối) vì
        // nếu pivot của hình tròn không set đúng tuyệt đối (0.5, 0.5) thì pivot sẽ
        // không trùng tâm hình học của vòng tròn → dây luôn lệch tâm một khoảng cố định.
        // Thay vào đó lấy TÂM HÌNH HỌC THẬT bằng GetWorldCorners (trung điểm 2 góc chéo),
        // luôn đúng tâm bất kể pivot của object đó đặt sai.
        target.GetWorldCorners(_cornersBuffer);
        Vector3 worldCenter = (_cornersBuffer[0] + _cornersBuffer[2]) * 0.5f;

        Camera cam = GetEventCamera();
        Vector3 screenPos = RectTransformUtility.WorldToScreenPoint(cam, worldCenter);
        return ScreenToLocal(screenPos);
    }

   
    private Vector2 ScreenToLocal(Vector3 screenPos)
    {
        Camera cam = GetEventCamera();
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