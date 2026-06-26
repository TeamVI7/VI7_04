using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Gắn vào WirePanel cùng chỗ với WirePuzzleManager.
/// Nhấn D trong lúc minigame mở → log toàn bộ thông tin dây đang vẽ.
/// Xoá sau khi debug xong.
/// </summary>
public class WireDrawDebugger : MonoBehaviour
{
    public RectTransform lineContainer;

    private void Update()
    {
        if (!Input.GetKeyDown(KeyCode.D)) return;

        Debug.Log("=== WIRE DRAW DEBUG ===");

        if (lineContainer == null)
        {
            Debug.LogError("lineContainer chưa gán!");
            return;
        }

        // Thông tin LineContainer
        Debug.Log($"[LineContainer] " +
                  $"anchoredPos={lineContainer.anchoredPosition} | " +
                  $"size={lineContainer.sizeDelta} | " +
                  $"pivot={lineContainer.pivot} | " +
                  $"worldPos={lineContainer.position} | " +
                  $"childCount={lineContainer.childCount}");

        // Thông tin từng dây con
        for (int i = 0; i < lineContainer.childCount; i++)
        {
            var child = lineContainer.GetChild(i) as RectTransform;
            if (child == null) continue;
            var img = child.GetComponent<Image>();
            Debug.Log($"  [Wire {i}] name={child.name} | " +
                      $"active={child.gameObject.activeSelf} | " +
                      $"anchoredPos={child.anchoredPosition} | " +
                      $"size={child.sizeDelta} | " +
                      $"pivot={child.pivot} | " +
                      $"rotation={child.localRotation.eulerAngles} | " +
                      $"color={img?.color} | " +
                      $"imgEnabled={img?.enabled}");
        }

        // Kiểm tra Canvas và camera
        var canvas = GetComponentInParent<Canvas>();
        Debug.Log($"[Canvas] renderMode={canvas?.renderMode} | worldCamera={canvas?.worldCamera?.name ?? "NULL"} | " +
                  $"sortOrder={canvas?.sortingOrder}");

        // Kiểm tra GraphicRaycaster
        var gr = GetComponentInParent<UnityEngine.UI.GraphicRaycaster>();
        Debug.Log($"[GraphicRaycaster] exists={gr != null} | enabled={gr?.enabled}");

        Debug.Log("=== END ===");
    }
}