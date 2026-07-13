using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Gắn vào Canvas. Tự động bật Raycast Target cho tất cả
/// Graphic (Image, TMP_InputField...) trong Canvas khi được enable.
/// Dùng để debug / fix InputField không click được.
/// </summary>
public class ForceRaycastTarget : MonoBehaviour
{
    [Tooltip("Nếu true: chạy lại mỗi frame (debug). Tắt sau khi fix xong.")]
    public bool debugEveryFrame = false;

    private void OnEnable()
    {
        FixAll();
    }

    private void Update()
    {
        if (debugEveryFrame) FixAll();
    }

    public void FixAll()
    {
        // Fix tất cả Graphic (Image, RawImage...)
        var graphics = GetComponentsInChildren<Graphic>(includeInactive: false);
        foreach (var g in graphics)
        {
            if (!g.raycastTarget)
            {
                g.raycastTarget = true;
                Debug.Log($"[ForceRaycastTarget] Enabled raycastTarget on: {g.gameObject.name} ({g.GetType().Name})");
            }
        }

        // Fix TMP_InputField riêng (nó có nhiều child component)
        var inputFields = GetComponentsInChildren<TMP_InputField>(includeInactive: false);
        foreach (var field in inputFields)
        {
            field.interactable = true;

            // Bật raycast target trên Image của InputField
            var img = field.GetComponent<Image>();
            if (img != null && !img.raycastTarget)
            {
                img.raycastTarget = true;
                Debug.Log($"[ForceRaycastTarget] InputField Image raycastTarget fixed: {field.name}");
            }
        }
    }
}