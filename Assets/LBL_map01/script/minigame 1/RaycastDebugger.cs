using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using TMPro;
using System.Collections.Generic;


public class RaycastDebugger : MonoBehaviour
{
    private void Update()
    {
        if (!ComputerInteraction.UIOpen) return;
        if (!Input.GetMouseButtonDown(0)) return;

        Debug.Log("=== RAYCAST DEBUG ===");

        var pointer = new PointerEventData(EventSystem.current)
        {
            position = Input.mousePosition
        };

        var results = new List<RaycastResult>();
        EventSystem.current?.RaycastAll(pointer, results);

        if (results.Count == 0)
        {
            Debug.LogWarning("❌ Không hit gì cả!");
            return;
        }

        var first = results[0];
        bool firstIsUI = first.module is GraphicRaycaster;

        Debug.Log($"FIRST HIT [{(firstIsUI ? "✅ UI" : "❌ 3D COLLIDER")}]: " +
                  $"'{first.gameObject.name}' | {first.module?.GetType().Name} | dist={first.distance:F3}");

        if (!firstIsUI)
        {
            Debug.LogWarning($"⚠️ '{first.gameObject.name}' đang CHẶN UI! Cần tắt collider này khi minigame mở.");
            for (int i = 1; i < results.Count; i++)
            {
                if (results[i].module is GraphicRaycaster)
                    Debug.Log($"  → UI bị chặn: '{results[i].gameObject.name}' tại dist={results[i].distance:F3}");
            }
        }

        var allInputFields = FindObjectsOfType<TMP_InputField>();
        foreach (var field in allInputFields)
        {
            if (!field.gameObject.activeInHierarchy) continue;
            var img = field.GetComponent<Image>();
            Debug.Log($"[InputField] '{field.name}'" +
                      $" | interactable={field.interactable}" +
                      $" | raycastTarget={img?.raycastTarget}" +
                      $" | active={field.gameObject.activeInHierarchy}");
        }

        Debug.Log("=== END ===");
    }
}