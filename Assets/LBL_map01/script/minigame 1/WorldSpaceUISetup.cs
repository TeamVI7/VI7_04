using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

/// <summary>
/// Gắn vào Canvas (World Space).
/// Hỗ trợ switch camera động — dùng minigameCamera khi UI mở,
/// dùng playerCamera khi bình thường.
/// </summary>
[RequireComponent(typeof(Canvas))]
public class WorldSpaceUISetup : MonoBehaviour
{
    [Header("Tự động tìm nếu để trống")]
    public Camera uiCamera;

    private Canvas           _canvas;
    private PhysicsRaycaster _currentPhysicsRaycaster;

    private void Awake()
    {
        _canvas = GetComponent<Canvas>();

        if (uiCamera == null)
            uiCamera = Camera.main;

        ApplyCamera(uiCamera);

        // Đảm bảo có GraphicRaycaster trên Canvas
        if (GetComponent<GraphicRaycaster>() == null)
            gameObject.AddComponent<GraphicRaycaster>();
    }

    /// <summary>
    /// Gọi từ ComputerInteraction khi switch sang camera mới.
    /// Tự động gỡ PhysicsRaycaster khỏi camera cũ và gắn vào camera mới.
    /// </summary>
    public void SwitchCamera(Camera newCam)
    {
        if (newCam == null || newCam == uiCamera) return;

        // Gỡ PhysicsRaycaster khỏi camera cũ
        if (uiCamera != null)
        {
            var oldRaycaster = uiCamera.GetComponent<PhysicsRaycaster>();
            if (oldRaycaster != null)
                Destroy(oldRaycaster);
        }

        uiCamera = newCam;
        ApplyCamera(uiCamera);
    }

    private void ApplyCamera(Camera cam)
    {
        if (cam == null) return;

        // Gán Event Camera cho Canvas
        if (_canvas != null)
            _canvas.worldCamera = cam;

        // Đảm bảo camera mới có PhysicsRaycaster
        if (cam.GetComponent<PhysicsRaycaster>() == null)
            cam.gameObject.AddComponent<PhysicsRaycaster>();
    }
}