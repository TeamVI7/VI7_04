using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// Gắn vào Canvas. Khi UI mở, tự động:
/// - Mở cursor
/// - Tắt các GameObject player (súng, ability...)
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

        Cursor.lockState = CursorLockMode.None;
        Cursor.visible   = true;

        foreach (var obj in objectsToDisable)
            if (obj != null) obj.SetActive(false);
    }

    public void UnblockInput()
    {
        IsBlocking = false;

        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible   = false;

        foreach (var obj in objectsToDisable)
            if (obj != null) obj.SetActive(true);
    }
}