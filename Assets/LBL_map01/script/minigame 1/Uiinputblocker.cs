using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// Gắn vào Canvas. Khi UI mở, tự động:
/// - Mở cursor
/// - Chặn tất cả input từ player bằng cách dùng EventSystem + TimeScale trick
/// Không đụng vào bất kỳ script nhân vật nào.
/// </summary>
public class UIInputBlocker : MonoBehaviour
{
    [Tooltip("Tắt các GameObject này khi UI mở (súng, ability scripts...)")]
    public GameObject[] objectsToDisable;

    public static bool IsBlocking { get; private set; } = false;

    public void BlockInput()
    {
        IsBlocking = true;

        // Mở cursor
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible   = true;

        // Tắt các object cần tắt (VD: GunHolder, Ability scripts)
        foreach (var obj in objectsToDisable)
            if (obj != null) obj.SetActive(false);
    }

    public void UnblockInput()
    {
        IsBlocking = false;

        // Khoá cursor lại
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible   = false;

        // Bật lại các object
        foreach (var obj in objectsToDisable)
            if (obj != null) obj.SetActive(true);
    }
}