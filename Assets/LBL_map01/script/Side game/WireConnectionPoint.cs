using UnityEngine;
using UnityEngine.EventSystems;


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

    public void OnPointerDown(PointerEventData eventData)
    {
        if (isConnected || !isSourceSide) return;
        _manager?.BeginDrag(this);
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