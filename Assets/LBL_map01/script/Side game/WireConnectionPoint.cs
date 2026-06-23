using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// Gắn vào MỖI điểm nối dây (UI Image hình tròn).
///
/// FIX: OnPointerUp bị xoá — EndDrag() nay chạy trong Update() của WirePuzzleManager
/// dùng Raycast tìm target, tránh bug "kéo đúng màu nhưng không dính".
/// </summary>
[RequireComponent(typeof(RectTransform))]
public class WireConnectionPoint : MonoBehaviour, IPointerDownHandler, IPointerEnterHandler, IPointerExitHandler
{
    [Header("Định danh")]
    [Tooltip("ID màu dây. Trái và phải cùng wireId = 1 cặp đúng. VD: 'red', 'blue', 'green', 'yellow'")]
    public string wireId = "red";

    [Tooltip("True = điểm BÊN TRÁI (nguồn, nơi bắt đầu kéo). False = điểm BÊN PHẢI (đích, nơi thả vào).")]
    public bool isSourceSide = true;

    [HideInInspector] public bool isConnected = false;

    private WirePuzzleManager _manager;
    private RectTransform     _rect;

    public RectTransform RectTransform => _rect;

    private void Awake()
    {
        _rect = GetComponent<RectTransform>();
    }

    public void Init(WirePuzzleManager manager)
    {
        _manager = manager;
    }

    // Bắt đầu kéo dây từ điểm TRÁI
    public void OnPointerDown(PointerEventData eventData)
    {
        if (isConnected || !isSourceSide) return;
        _manager?.BeginDrag(this);
    }

    // Hint hover (không dùng để xác định kết nối nữa)
    public void OnPointerEnter(PointerEventData eventData)
    {
        if (!isSourceSide && !isConnected)
            _manager?.SetHoverTarget(this);
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        if (!isSourceSide)
            _manager?.ClearHoverTarget(this);
    }

    // OnPointerUp đã XOÁ — EndDrag() chạy ở Update() của Manager qua Raycast
}