using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Gắn vào cùng object với Canvas (World Space) của minigame.
/// Tự động ép Event Camera + Render Camera của Canvas luôn khớp với
/// camera minigame đang dùng, MỖI FRAME. Fix lỗi bấm bị lệch do
/// Canvas đang trỏ nhầm/ trỏ camera cũ trong lúc camera transition
/// (zoom vào máy tính) hoặc do Canvas Event Camera bị để trống / sai.
///
/// Cách dùng:
/// 1. Kéo script này vào GameObject chứa Canvas (Panel_Numpad...).
/// 2. Kéo camera minigame (VD: "minigame1 camera") vào ô targetCamera.
/// 3. Nếu Player còn Camera khác đang active song song (VD: FPS camera),
///    kéo nó vào playerCameraToDisable để tự tắt component Camera/AudioListener
///    của nó trong lúc chơi minigame (tránh 2 camera raycast chồng nhau).
/// </summary>
[RequireComponent(typeof(Canvas))]
public class FixCanvasCameraSync : MonoBehaviour
{
    [Header("Camera THẬT SỰ đang dùng để nhìn/raycast minigame")]
    public Camera targetCamera;

    [Header("(Tuỳ chọn) Camera của Player cần tắt khi vào minigame")]
    public Camera playerCameraToDisable;

    [Header("Canvas Scaler world space - nếu muốn ép luôn dynamic pixel size")]
    public bool forceDynamicPixelsPerUnit = false;

    private Canvas _canvas;
    private GraphicRaycaster _raycaster;

    private void Awake()
    {
        _canvas = GetComponent<Canvas>();
        _raycaster = GetComponent<GraphicRaycaster>();
    }

    private void OnEnable()
    {
        Sync();
        if (playerCameraToDisable != null)
        {
            playerCameraToDisable.enabled = false;
            var listener = playerCameraToDisable.GetComponent<AudioListener>();
            if (listener != null) listener.enabled = false;
        }
    }

    private void OnDisable()
    {
        if (playerCameraToDisable != null)
        {
            playerCameraToDisable.enabled = true;
            var listener = playerCameraToDisable.GetComponent<AudioListener>();
            if (listener != null) listener.enabled = true;
        }
    }

    // Chạy ở LateUpdate để bắt được camera SAU khi mọi animation/transition
    // trong Update() đã chạy xong trong frame đó -> không bao giờ bị lệch 1 frame.
    private void LateUpdate()
    {
        Sync();
    }

    private void Sync()
    {
        if (_canvas == null || targetCamera == null) return;

        if (_canvas.renderMode != RenderMode.WorldSpace)
        {
            Debug.LogWarning($"[FixCanvasCameraSync] Canvas '{_canvas.name}' không phải World Space, script này chỉ dành cho World Space Canvas.");
            return;
        }

        // Đây là dòng quan trọng nhất: ép Canvas dùng ĐÚNG camera hiện tại
        if (_canvas.worldCamera != targetCamera)
            _canvas.worldCamera = targetCamera;

        // Đảm bảo camera này thực sự đang bật, không thôi raycast sẽ vô nghĩa
        if (!targetCamera.gameObject.activeInHierarchy || !targetCamera.enabled)
        {
            Debug.LogWarning($"[FixCanvasCameraSync] targetCamera '{targetCamera.name}' đang bị tắt nhưng Canvas vẫn đang cố dùng nó để raycast!");
        }
    }
}