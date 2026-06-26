using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// Gắn vào Canvas (World Space) thay thế GraphicRaycaster mặc định.
/// Tự xử lý raycast từ minigameCamera → Canvas, không bị block bởi collider 3D.
/// </summary>
[RequireComponent(typeof(Canvas))]
[DisallowMultipleComponent]
public class MinigameCameraRaycaster : GraphicRaycaster
{
    private Canvas _canvas;

    protected override void Awake()
    {
        base.Awake();
        _canvas = GetComponent<Canvas>();
    }

    public override Camera eventCamera
    {
        get
        {
            if (_canvas != null && _canvas.worldCamera != null)
                return _canvas.worldCamera;
            return Camera.main;
        }
    }
}