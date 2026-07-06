using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Tự động tìm và tắt (disable) TẤT CẢ 3D Collider nằm giữa camera minigame
/// và Canvas UI, để raycast luôn xuyên qua tới UI thay vì bị chặn bởi vật
/// thể 3D (như 'Cube (1)' trong log RaycastDebugger).
///
/// Cách dùng:
/// 1. Gắn script này vào cùng object với Camera dùng để raycast minigame
///    (camera có PhysicsRaycaster / MinigameCameraRaycaster).
/// 2. Kéo Canvas (world space) của minigame vào ô "uiCanvas".
/// 3. Gọi DisableBlockers() khi EnterComputer(), gọi RestoreBlockers() khi
///    ExitComputer() (hoặc để tự động theo OnEnable/OnDisable).
/// </summary>
public class AutoDisableBlockingColliders : MonoBehaviour
{
    [Header("Camera dùng để raycast vào UI minigame")]
    public Camera raycastCamera;

    [Header("Canvas UI của minigame (World Space)")]
    public Canvas uiCanvas;

    [Tooltip("Bỏ qua các collider có tag này (vd: vật cần giữ nguyên tương tác)")]
    public string ignoreTag = "KeepCollider";

    [Tooltip("Nới rộng khoảng cách quét thêm (m) để chắc chắn bắt hết vật cản")]
    public float extraPadding = 0.2f;

    private readonly List<Collider> _disabled = new List<Collider>();

    private void OnEnable()  => DisableBlockers();
    private void OnDisable() => RestoreBlockers();

    /// <summary>
    /// Bắn ray từ camera tới từng góc + tâm của Canvas, tắt mọi Collider 3D
    /// chặn đường trước khi chạm UI.
    /// </summary>
    public void DisableBlockers()
    {
        if (raycastCamera == null || uiCanvas == null)
        {
            Debug.LogWarning("[AutoDisableBlockingColliders] Thiếu raycastCamera hoặc uiCanvas.");
            return;
        }

        RestoreBlockers(); // tránh double-disable nếu gọi lại

        RectTransform rt = uiCanvas.GetComponent<RectTransform>();
        Vector3[] corners = new Vector3[4];
        rt.GetWorldCorners(corners); // 4 góc world-space của canvas

        // Danh sách điểm cần raycast tới: tâm + 4 góc canvas
        Vector3 center = (corners[0] + corners[2]) * 0.5f;
        Vector3[] targets = { center, corners[0], corners[1], corners[2], corners[3] };

        foreach (var target in targets)
        {
            Vector3 origin = raycastCamera.transform.position;
            Vector3 dir = (target - origin);
            float dist = dir.magnitude + extraPadding;
            dir.Normalize();

            // Lấy TẤT CẢ collider trên đường đi (không chỉ cái đầu tiên)
            RaycastHit[] hits = Physics.RaycastAll(origin, dir, dist);
            foreach (var hit in hits)
            {
                var col = hit.collider;
                if (col == null) continue;
                if (!col.enabled) continue;
                if (!string.IsNullOrEmpty(ignoreTag) && col.CompareTag(ignoreTag)) continue;
                if (_disabled.Contains(col)) continue;

                col.enabled = false;
                _disabled.Add(col);
                Debug.Log($"[AutoDisableBlockingColliders] Đã tắt collider chắn UI: '{col.gameObject.name}'");
            }
        }
    }

    /// <summary>Bật lại tất cả collider đã tắt (gọi khi thoát minigame).</summary>
    public void RestoreBlockers()
    {
        foreach (var col in _disabled)
            if (col != null) col.enabled = true;

        _disabled.Clear();
    }
}