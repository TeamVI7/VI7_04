using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// Gắn vào MỖI điểm nối dây (cả bên trái "nguồn" và bên phải "đích").
/// Là 1 UI Image hình tròn nhỏ, có Raycast Target = true.
///
/// Cách dùng:
///   - Bên trái (nguồn): kéo SwireColor, isSourceSide = true
///   - Bên phải (đích):  kéo wireColor,  isSourceSide = false
///   - WirePuzzleManager sẽ tự tìm tất cả điểm này qua GetComponentsInChildren
/// </summary>
[RequireComponent(typeof(RectTransform))]
public class WireConnectionPoint : MonoBehaviour,
    IPointerDownHandler, IPointerUpHandler, IPointerEnterHandler, IPointerExitHandler
{
    [Header("Định danh dây")]
    [Tooltip("Màu/ID của dây này. Bên trái và bên phải có cùng wireId sẽ là 1 cặp đúng.")]
    public string wireId = "red";

    [Tooltip("True = đây là điểm BÊN TRÁI (nơi bắt đầu kéo). False = điểm BÊN PHẢI (nơi thả vào).")]
    public bool isSourceSide = true;

    [HideInInspector] public bool isConnected = false;

    private WirePuzzleManager _manager;
    private RectTransform     _rect;

    public RectTransform RectTransform => _rect;

    private void Awake()
    {
        _rect = GetComponent<RectTransform>();
    }

    /// <summary>Gọi bởi WirePuzzleManager khi khởi tạo.</summary>
    public void Init(WirePuzzleManager manager)
    {
        _manager = manager;
    }

    public void OnPointerDown(PointerEventData eventData)
    {
        if (isConnected) return;

        // Chỉ bắt đầu kéo dây từ điểm BÊN TRÁI (nguồn)
        if (isSourceSide)
            _manager?.BeginDrag(this);
    }

    public void OnPointerUp(PointerEventData eventData)
    {
        // Thả chuột ở bất kỳ đâu -> báo cho manager xử lý (manager tự kiểm tra đang thả lên điểm nào)
        _manager?.EndDrag();
    }

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
}