using UnityEngine;
using UnityEngine.EventSystems;
using System.Collections.Generic;

/// <summary>
/// Khi UI minigame mở:
/// - Mở cursor
/// - Tắt các GameObject chỉ định (gun, ability...)
/// - Tắt tất cả Canvas Screen Space để không block raycast World Space
/// </summary>
public class UIInputBlocker : MonoBehaviour
{
    [Tooltip("Tắt các GameObject này khi UI mở (súng, ability scripts...)")]
    public GameObject[] objectsToDisable;

    [Tooltip("Kéo thủ công các Canvas HUD vào đây để kiểm soát chính xác.\n" +
             "Để trống = tự động tìm tất cả Canvas Screen Space.")]
    public Canvas[] canvasesToHide;

    [Tooltip("Nếu true: tự động tắt tất cả Canvas Screen Space Overlay/Camera khi minigame mở.")]
    public bool autoHideScreenSpaceCanvas = true;

    public static bool IsBlocking { get; private set; } = false;

    private readonly List<Canvas> _hiddenCanvases = new List<Canvas>();

    public void BlockInput()
    {
        IsBlocking = true;

        Cursor.lockState = CursorLockMode.None;
        Cursor.visible   = true;

        foreach (var obj in objectsToDisable)
            if (obj != null) obj.SetActive(false);

        HideScreenSpaceCanvases();
    }

    public void UnblockInput()
    {
        IsBlocking = false;

        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible   = false;

        foreach (var obj in objectsToDisable)
            if (obj != null) obj.SetActive(true);

        RestoreScreenSpaceCanvases();
    }

    private void HideScreenSpaceCanvases()
    {
        _hiddenCanvases.Clear();

        // Dùng danh sách thủ công nếu được điền
        if (canvasesToHide != null && canvasesToHide.Length > 0)
        {
            foreach (var c in canvasesToHide)
            {
                if (c != null && c.gameObject.activeInHierarchy)
                {
                    c.gameObject.SetActive(false);
                    _hiddenCanvases.Add(c);
                }
            }
            return;
        }

        if (!autoHideScreenSpaceCanvas) return;

        // Tự động tìm tất cả Canvas Screen Space đang active
        var allCanvases = FindObjectsOfType<Canvas>(true);
        foreach (var c in allCanvases)
        {
            if (!c.gameObject.activeInHierarchy) continue;
            // Bỏ qua World Space canvas (minigame canvas)
            if (c.renderMode == RenderMode.WorldSpace) continue;
            // Bỏ qua nếu là con của object này
            if (c.transform.IsChildOf(transform)) continue;

            c.gameObject.SetActive(false);
            _hiddenCanvases.Add(c);
            Debug.Log($"[UIInputBlocker] Hidden Canvas: '{c.name}'");
        }
    }

    private void RestoreScreenSpaceCanvases()
    {
        foreach (var c in _hiddenCanvases)
            if (c != null) c.gameObject.SetActive(true);

        _hiddenCanvases.Clear();
    }
}