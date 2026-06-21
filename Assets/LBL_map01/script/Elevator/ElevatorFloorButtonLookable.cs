using UnityEngine;
using UnityEngine.UI;

// Gắn vào prefab nút chọn tầng (cùng cấp với Label, LockIcon).
// Nút này không cần Button.onClick nữa — được chọn bằng cách nhìn vào + nhấn E,
// xử lý bởi PlayerElevatorInteractor.cs (raycast từ camera).
[RequireComponent(typeof(Collider))]
public class ElevatorFloorButtonLookable : MonoBehaviour
{
    [Header("Gán sẵn trong prefab")]
    [Tooltip("Image nền của nút, đổi màu khi được nhìn vào")]
    public Image targetGraphic;
    public Color normalColor = Color.white;
    public Color highlightColor = Color.yellow;

    // Được ElevatorFloorSelectionUI gán lúc spawn, không cần điền tay
    [HideInInspector] public int floorIndex;
    [HideInInspector] public ElevatorController elevator;

    private bool isHighlighted = false;
    private bool isSelectable = true;

    public bool IsSelectable => isSelectable;

    public void SetSelectable(bool value)
    {
        isSelectable = value;
    }

    public void SetHighlighted(bool value)
    {
        if (isHighlighted == value) return;
        isHighlighted = value;

        if (targetGraphic != null)
            targetGraphic.color = value ? highlightColor : normalColor;
    }

    // Gọi khi người chơi nhấn nút tương tác (E) lúc đang nhìn vào nút này
    public void Select()
    {
        if (!isSelectable) return;
        if (elevator == null) return;
        elevator.TrySelectFloor(floorIndex);
    }
}