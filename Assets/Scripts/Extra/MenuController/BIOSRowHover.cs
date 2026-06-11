using UnityEngine;
using UnityEngine.EventSystems;
using TMPro;

/// <summary>
/// BIOSRowHover — Attach to any TextMeshProUGUI row GameObject that also has
/// a Graphic Raycaster on the Canvas and an EventSystem in the scene.
///
/// Gives mouse hover + click behaviour to a single BIOS row:
///   - Hover  → text turns cyan, plays navigate sound
///   - Click  → invokes onClickAction, plays confirm sound
///
/// Usage:
///   1. Add a transparent Image component to the row GameObject (for hit detection)
///   2. Attach this script
///   3. Set onClickAction in the Inspector via UnityEvent, or assign in code:
///        GetComponent<BIOSRowHover>().onClickAction = () => DoSomething();
/// </summary>
public class BIOSRowHover : MonoBehaviour,
    IPointerEnterHandler, IPointerExitHandler, IPointerClickHandler
{
    [Header("References")]
    [SerializeField] private TextMeshProUGUI label;

    [Header("Colors")]
    [SerializeField] private Color normalColor   = new Color(0.67f, 0.67f, 0.67f);
    [SerializeField] private Color hoverColor    = Color.cyan;

    [Header("Action")]
    public System.Action onClickAction;

    private bool _isSelected = false;

    void Start()
    {
        if (label == null) label = GetComponent<TextMeshProUGUI>();
        ResetColor();
    }

    public void SetSelected(bool selected)
    {
        _isSelected = selected;
        if (label != null)
            label.color = selected ? hoverColor : normalColor;
    }

    public void ResetColor()
    {
        if (!_isSelected && label != null)
            label.color = normalColor;
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        if (label != null) label.color = hoverColor;
        MenuAudio.Instance?.PlayNavigate();
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        if (!_isSelected && label != null)
            label.color = normalColor;
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        MenuAudio.Instance?.PlayConfirm();
        onClickAction?.Invoke();
    }
}
