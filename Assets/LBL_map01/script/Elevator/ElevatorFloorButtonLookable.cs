using UnityEngine;
using UnityEngine.UI;

// Gắn vào từng nút chọn tầng ĐẶT SẴN THỦ CÔNG trong Scene (vd: dán trực tiếp lên
// tấm panel trong cabin), không còn sinh ra bằng prefab + Instantiate nữa.
// Nút được chọn bằng cách nhìn vào + nhấn E, xử lý bởi PlayerElevatorInteractor.cs
// (raycast từ camera).
[RequireComponent(typeof(Collider))]
public class ElevatorFloorButtonLookable : MonoBehaviour
{
    [Header("Gán sẵn trong Inspector")]
    [Tooltip("Image nền của nút, đổi màu khi được nhìn vào")]
    public Image targetGraphic;
    public Color normalColor = Color.white;
    public Color highlightColor = Color.yellow;

    [Header("Tầng mà nút này đại diện")]
    [Tooltip("Index tầng trong list 'floors' của ElevatorController mà NÚT NÀY sẽ đưa tới. " +
             "VD: nút số 1 trên panel -> điền đúng index của tầng đó trong danh sách floors.")]
    public int floorIndex;

    [Tooltip("Kéo object cabin (có ElevatorController) vào đây. " +
             "Có thể để trống nếu nút là con/cháu của object cabin — script sẽ tự tìm.")]
    public ElevatorController elevator;

    private bool isHighlighted = false;
    private bool isSelectable = true;

    public bool IsSelectable => isSelectable;

    void Awake()
    {
        // Nếu quên kéo elevator, thử tự tìm ở object cha (phòng khi nút là con của cabin)
        if (elevator == null)
            elevator = GetComponentInParent<ElevatorController>();
    }

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
        if (elevator == null)
        {
            Debug.LogWarning($"ElevatorFloorButtonLookable trên '{name}' chưa gán 'elevator'!", this);
            return;
        }
        elevator.TrySelectFloor(floorIndex);
    }
}