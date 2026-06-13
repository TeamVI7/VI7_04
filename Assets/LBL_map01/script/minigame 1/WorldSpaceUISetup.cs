using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

/// <summary>
/// Gắn vào Canvas (World Space).
/// Tự động gán Event Camera và xử lý raycast để click được UI trong World Space.
/// Không cần sửa bất kỳ script nhân vật nào.
/// </summary>
[RequireComponent(typeof(Canvas))]
public class WorldSpaceUISetup : MonoBehaviour
{
    [Header("Tự động tìm nếu để trống")]
    public Camera uiCamera;

    private Canvas          _canvas;
    private GraphicRaycaster _raycaster; // cần có trên Canvas

    private void Awake()
    {
        _canvas = GetComponent<Canvas>();

        // Tìm camera nếu chưa gán
        if (uiCamera == null)
            uiCamera = Camera.main;

        // Gán Event Camera cho Canvas
        if (_canvas != null && uiCamera != null)
            _canvas.worldCamera = uiCamera;

        // Đảm bảo có GraphicRaycaster trên Canvas
        if (GetComponent<GraphicRaycaster>() == null)
            gameObject.AddComponent<GraphicRaycaster>();

        // Đảm bảo Camera có PhysicsRaycaster
        if (uiCamera != null && uiCamera.GetComponent<PhysicsRaycaster>() == null)
            uiCamera.gameObject.AddComponent<PhysicsRaycaster>();
    }
}