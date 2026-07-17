using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using TMPro;

[RequireComponent(typeof(Button))]
public class TerminalWordSlot : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerClickHandler
{
    public TMP_Text label;

    [Header("Row Hover Highlight (optional)")]
    [Tooltip("Ảnh nền của cả dòng, dùng để sáng lên khi rê chuột vào, giống bản Fallout gốc. Để trống nếu không dùng.")]
    public Image rowBackground;
    public Color rowHoverColor = new Color(0.20f, 1f, 0.30f, 0.18f);
    private Color _rowBackgroundOriginalColor;

    public bool isDudRemovalBracket = false;

    [HideInInspector] public string assignedWord;
    [HideInInspector] public bool   isUsed = false;

    /// <summary>Cờ DUY NHẤT quyết định dòng này có bấm/chọn được hay không, do FalloutTerminalManager gán
    /// tường minh mỗi lượt layout. KHÔNG dựa vào Button.interactable để quyết định logic chọn — interactable
    /// chỉ dùng cho hiệu ứng hình (mờ/xám nút), còn việc có tính là 1 lượt chọn hợp lệ hay không luôn đi qua
    /// cờ này, để đảm bảo các dòng rác (garbage-only) không bao giờ chọn được dù có lỡ cấu hình gì khác.</summary>
    [HideInInspector] public bool isSelectable = false;

    /// <summary>Gán bởi FalloutTerminalManager mỗi lượt layout để phát âm thanh hover, không cộng dồn listener.</summary>
    [HideInInspector] public System.Action<TerminalWordSlot, bool> OnHover;

    /// <summary>Gọi khi dòng này được chọn hợp lệ (isSelectable && !isUsed), thay thế hoàn toàn cho Button.onClick.</summary>
    [HideInInspector] public System.Action<TerminalWordSlot> OnSelected;

    /// <summary>Gọi khi người chơi bấm vào 1 dòng KHÔNG chọn được (dòng rác) — dùng để phát tiếng báo hiệu,
    /// không tốn lượt, không có tác dụng gì khác, giống hệt việc bấm vào khoảng trống trong bản gốc.</summary>
    [HideInInspector] public System.Action<TerminalWordSlot> OnInvalidClick;

    private Button _button;
    public Button Button
    {
        get
        {
            if (_button == null) _button = GetComponent<Button>();
            return _button;
        }
    }

    private void Awake()
    {
        if (label == null) label = GetComponentInChildren<TMP_Text>();
        if (rowBackground != null) _rowBackgroundOriginalColor = rowBackground.color;
    }

    public void ResetSlot()
    {
        isUsed = false;
        assignedWord = null;
        isDudRemovalBracket = false;
        isSelectable = false;
        OnHover = null;
        OnSelected = null;
        OnInvalidClick = null;
        SetInteractable(true);
        SetHighlight(TerminalVisualState.Normal);
        SetRowHighlighted(false);
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        SetRowHighlighted(true);
        OnHover?.Invoke(this, true);
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        SetRowHighlighted(false);
        OnHover?.Invoke(this, false);
    }

    /// <summary>Cổng kiểm soát DUY NHẤT cho việc click chọn 1 dòng. Dòng rác (isSelectable = false) sẽ luôn
    /// rơi vào nhánh OnInvalidClick, không bao giờ được tính là 1 lượt chọn hợp lệ, kể cả khi Button vẫn còn
    /// interactable vì lý do gì đó — logic gameplay không phụ thuộc vào Button.interactable nữa.</summary>
    public void OnPointerClick(PointerEventData eventData)
    {
        if (!isSelectable || isUsed)
        {
            OnInvalidClick?.Invoke(this);
            return;
        }
        OnSelected?.Invoke(this);
    }

    /// <summary>Sáng/tắt nền cả dòng. Dùng chung cho hover chuột lẫn điều hướng bàn phím (mũi tên).</summary>
    public void SetRowHighlighted(bool on)
    {
        if (rowBackground == null) return;
        rowBackground.color = on ? rowHoverColor : _rowBackgroundOriginalColor;
    }

    public void SetText(string text)
    {
        if (label != null) label.text = text;
    }

    public void SetInteractable(bool value)
    {
        Button.interactable = value;
    }
    public void SetHighlight(TerminalVisualState state)
    {
        if (label == null) return;

        switch (state)
        {
            case TerminalVisualState.Normal:
                label.color = TerminalTheme.NormalColor;
                break;
            case TerminalVisualState.Correct:
                label.color = TerminalTheme.CorrectColor;
                break;
            case TerminalVisualState.Wrong:
                label.color = TerminalTheme.WrongColor;
                break;
            case TerminalVisualState.Disabled:
                label.color = TerminalTheme.DisabledColor;
                break;
        }
    }
}
public enum TerminalVisualState
{
    Normal,
    Correct,
    Wrong,
    Disabled
}
public static class TerminalTheme
{
    public static Color NormalColor   = new Color(0.20f, 1f, 0.30f);
    public static Color CorrectColor  = new Color(0.6f, 1f, 0.6f);
    public static Color WrongColor    = new Color(1f, 0.25f, 0.2f);
    public static Color DisabledColor = new Color(0.15f, 0.4f, 0.18f);
}