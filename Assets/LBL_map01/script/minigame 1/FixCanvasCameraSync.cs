using UnityEngine;
using UnityEngine.UI;
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
    private void LateUpdate()
    {
        Sync();
    }

    private void Sync()
    {
        if (_canvas == null || targetCamera == null) return;

        if (_canvas.renderMode != RenderMode.WorldSpace)
        {
            return;
        }

        if (_canvas.worldCamera != targetCamera)
            _canvas.worldCamera = targetCamera;
        if (!targetCamera.gameObject.activeInHierarchy || !targetCamera.enabled)
        {
        }
    }
}