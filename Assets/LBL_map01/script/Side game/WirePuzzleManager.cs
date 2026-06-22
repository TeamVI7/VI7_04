using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

/// <summary>
/// Quản lý toàn bộ puzzle nối dây kiểu Among Us.
/// Gắn vào Canvas (Screen Space) chứa UI nối dây, hoặc 1 panel con của Canvas đó.
///
/// SETUP TRONG EDITOR (xem chi tiết ở cuối file / note đi kèm):
///   1. Tạo Canvas Screen Space - Overlay (hoặc Camera), đặt tên "WirePuzzleCanvas"
///   2. Trong Canvas, tạo Panel "WirePanel" làm container chính, gắn script này vào Panel
///   3. Tạo các điểm nối trái/phải bằng UI Image (hình tròn), gắn WireConnectionPoint vào mỗi điểm
///   4. Tạo 1 RectTransform rỗng tên "LinePreviewContainer" làm cha chứa các dây được vẽ ra (UI.Image kéo dài)
///   5. Kéo tất cả vào các field bên dưới trong Inspector
/// </summary>
public class WirePuzzleManager : MonoBehaviour
{
    [Header("Điểm nối dây")]
    [Tooltip("Để trống = tự động tìm tất cả WireConnectionPoint trong children")]
    public WireConnectionPoint[] connectionPoints;

    [Header("Vẽ dây")]
    [Tooltip("RectTransform cha để chứa các Image dây được vẽ ra (kéo dài theo kiểu line).")]
    public RectTransform lineContainer;

    [Tooltip("Prefab UI Image dùng làm 1 đoạn dây (Image đơn giản, pivot 0,0.5, sẽ bị stretch theo chiều dài).")]
    public RectTransform wireLinePrefab;

    [Tooltip("Độ dày của dây vẽ ra (pixel).")]
    public float wireThickness = 8f;

    [Header("Màu sắc theo wireId")]
    public WireColorEntry[] wireColors =
    {
        new WireColorEntry { wireId = "red",    color = Color.red },
        new WireColorEntry { wireId = "yellow", color = Color.yellow },
        new WireColorEntry { wireId = "blue",   color = Color.blue },
        new WireColorEntry { wireId = "green",  color = Color.green },
    };

    [Header("UI phụ trợ")]
    public GameObject completePanel; // bảng "Hoàn thành!" hiện khi xong (tuỳ chọn)

    [Header("Canvas tham chiếu (để chuyển toạ độ chuột)")]
    [Tooltip("Để trống sẽ tự tìm Canvas cha gần nhất.")]
    public Canvas parentCanvas;

    public event Action OnPuzzleCompleted;

    [Serializable]
    public struct WireColorEntry
    {
        public string wireId;
        public Color  color;
    }

    // ── State ────────────────────────────────────────────────────
    private readonly Dictionary<string, Color> _colorMap = new();
    private readonly Dictionary<string, RectTransform> _activeLines = new(); // wireId -> line đang vẽ (đã nối xong)

    private WireConnectionPoint _dragSource;     // điểm trái đang kéo
    private WireConnectionPoint _hoverTarget;    // điểm phải đang hover trong lúc kéo
    private RectTransform       _previewLine;    // dây đang kéo (chưa thả)

    private int  _totalWires;
    private int  _connectedCount;
    private bool _completed = false;

    // ─────────────────────────────────────────────────────────────

    private void Awake()
    {
        if (parentCanvas == null)
            parentCanvas = GetComponentInParent<Canvas>();

        foreach (var entry in wireColors)
            _colorMap[entry.wireId] = entry.color;

        if (connectionPoints == null || connectionPoints.Length == 0)
            connectionPoints = GetComponentsInChildren<WireConnectionPoint>(true);

        foreach (var p in connectionPoints)
            p.Init(this);

        _totalWires = 0;
        foreach (var p in connectionPoints)
            if (p.isSourceSide) _totalWires++;

        ApplyColors();
    }

    private void OnEnable()
    {
        ResetPuzzle();
    }

    private void Update()
    {
        // Cập nhật vị trí dây đang kéo (preview) theo chuột mỗi frame
        if (_dragSource != null && _previewLine != null)
        {
            Vector2 endPos = ScreenToLocalPointInLineContainer(Input.mousePosition);
            DrawLine(_previewLine, GetLocalPos(_dragSource.RectTransform), endPos, _colorMap.GetValueOrDefault(_dragSource.wireId, Color.white));
        }
    }

    // ── Public API cho WireConnectionPoint gọi ─────────────────────

    public void BeginDrag(WireConnectionPoint source)
    {
        if (_completed) return;
        if (source.isConnected) return;

        _dragSource  = source;
        _hoverTarget = null;

        _previewLine = CreateLineInstance();
    }

    public void SetHoverTarget(WireConnectionPoint target)
    {
        if (_dragSource == null) return;
        _hoverTarget = target;
    }

    public void ClearHoverTarget(WireConnectionPoint target)
    {
        if (_hoverTarget == target)
            _hoverTarget = null;
    }

    public void EndDrag()
    {
        if (_dragSource == null) return;

        bool success = false;

        if (_hoverTarget != null && !_hoverTarget.isConnected)
        {
            if (_hoverTarget.wireId == _dragSource.wireId)
            {
                // ĐÚNG: chốt dây cố định
                ConfirmConnection(_dragSource, _hoverTarget);
                success = true;
            }
            else
            {
                // SAI: nháy đỏ rồi huỷ
                Debug.Log($"[WirePuzzle] Sai dây: {_dragSource.wireId} -> {_hoverTarget.wireId}");
            }
        }

        if (!success && _previewLine != null)
            Destroy(_previewLine.gameObject);

        _previewLine = null;
        _dragSource  = null;
        _hoverTarget = null;
    }

    // ── Logic chính ──────────────────────────────────────────────

    private void ConfirmConnection(WireConnectionPoint source, WireConnectionPoint target)
    {
        source.isConnected = true;
        target.isConnected = true;

        // Cố định vị trí dây preview thành dây hoàn chỉnh
        DrawLine(_previewLine, GetLocalPos(source.RectTransform), GetLocalPos(target.RectTransform),
                 _colorMap.GetValueOrDefault(source.wireId, Color.white));

        _activeLines[source.wireId] = _previewLine;
        _previewLine = null; // đã "chốt", không bị Destroy ở EndDrag nữa

        _connectedCount++;
        Debug.Log($"[WirePuzzle] Nối đúng dây '{source.wireId}' ({_connectedCount}/{_totalWires})");

        if (_connectedCount >= _totalWires)
            CompletePuzzle();
    }

    private void CompletePuzzle()
    {
        if (_completed) return;
        _completed = true;

        if (completePanel != null)
            completePanel.SetActive(true);

        Debug.Log("[WirePuzzle] HOÀN THÀNH! Tất cả dây đã nối đúng.");
        OnPuzzleCompleted?.Invoke();
    }

    /// <summary>Reset toàn bộ puzzle về trạng thái ban đầu (gọi khi mở lại UI).</summary>
    public void ResetPuzzle()
    {
        _completed      = false;
        _connectedCount = 0;
        _dragSource     = null;
        _hoverTarget    = null;

        foreach (var line in _activeLines.Values)
            if (line != null) Destroy(line.gameObject);
        _activeLines.Clear();

        if (_previewLine != null) Destroy(_previewLine.gameObject);
        _previewLine = null;

        foreach (var p in connectionPoints)
            if (p != null) p.isConnected = false;

        if (completePanel != null)
            completePanel.SetActive(false);
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

    // ── Vẽ dây bằng UI Image kéo dài (stretch theo chiều dài + góc xoay) ──

    private RectTransform CreateLineInstance()
    {
        RectTransform line = Instantiate(wireLinePrefab, lineContainer);
        line.gameObject.SetActive(true);
        return line;
    }

    private void DrawLine(RectTransform line, Vector2 from, Vector2 to, Color color)
    {
        if (line == null) return;

        Vector2 dir = to - from;
        float   distance = dir.magnitude;
        float   angle    = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;

        line.anchoredPosition = from;
        line.sizeDelta        = new Vector2(distance, wireThickness);
        line.localRotation    = Quaternion.Euler(0, 0, angle);

        var img = line.GetComponent<Image>();
        if (img != null) img.color = color;
    }

    private Vector2 GetLocalPos(RectTransform target)
    {
        // Chuyển vị trí world của 1 điểm nối thành local position trong lineContainer
        Vector3 worldPos = target.position;
        Vector2 localPos = lineContainer.InverseTransformPoint(worldPos);
        return localPos;
    }

    private Vector2 ScreenToLocalPointInLineContainer(Vector3 screenPos)
    {
        Camera cam = (parentCanvas != null && parentCanvas.renderMode != RenderMode.ScreenSpaceOverlay)
            ? parentCanvas.worldCamera
            : null; // null = đúng cho Screen Space - Overlay

        Vector3 worldPoint;
        RectTransformUtility.ScreenPointToWorldPointInRectangle(lineContainer, screenPos, cam, out worldPoint);
        return lineContainer.InverseTransformPoint(worldPoint);
    }
}