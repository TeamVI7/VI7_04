using UnityEngine;


public class WireDrawDebugger : MonoBehaviour
{
    public RectTransform lineContainer;

    private void Update()
    {
        if (!Input.GetKeyDown(KeyCode.D)) return;

        Debug.Log("=== WIRE DRAW DEBUG (3D CAPSULE) ===");

        if (lineContainer == null)
        {
            Debug.LogError("lineContainer chưa gán!");
            return;
        }

        Debug.Log($"[LineContainer] " +
                  $"anchoredPos={lineContainer.anchoredPosition} | " +
                  $"size={lineContainer.sizeDelta} | " +
                  $"pivot={lineContainer.pivot} | " +
                  $"worldPos={lineContainer.position} | " +
                  $"childCount={lineContainer.childCount}");

        for (int i = 0; i < lineContainer.childCount; i++)
        {
            var child = lineContainer.GetChild(i); 
            if (child == null) continue;
            
            var meshRenderer = child.GetComponentInChildren<Renderer>();
            
            Debug.Log($"  [Wire {i}] name={child.name} | " +
                      $"active={child.gameObject.activeSelf} | " +
                      $"localPos={child.localPosition} | " +
                      $"localScale={child.localScale} | " +
                      $"rotation={child.localRotation.eulerAngles} | " +
                      $"color={(meshRenderer != null ? meshRenderer.material.color.ToString() : "No Material")}");
        }

        var canvas = GetComponentInParent<Canvas>();
        Debug.Log($"[Canvas] renderMode={canvas?.renderMode} | worldCamera={canvas?.worldCamera?.name ?? "NULL"} | " +
                  $"sortOrder={canvas?.sortingOrder}");

        var gr = GetComponentInParent<UnityEngine.UI.GraphicRaycaster>();
        Debug.Log($"[GraphicRaycaster] exists={gr != null} | enabled={gr?.enabled}");

        Debug.Log("=== END ===");
    }
}