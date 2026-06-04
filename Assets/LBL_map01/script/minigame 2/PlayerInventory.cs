using System.Collections.Generic;
using UnityEngine;
using TMPro;
 
/// <summary>
/// Gắn vào Player. Quản lý danh sách SSD đang mang.
/// </summary>
public class PlayerInventory : MonoBehaviour
{
    [Header("UI (tuỳ chọn)")]
    [Tooltip("Text hiện số SSD đang mang: '2 / 6 SSD'")]
    public TMP_Text ssdCountText;
 
    // Danh sách SSD đã nhặt
    private List<SSDItem> _ssds = new List<SSDItem>();
 
    public int SSDCount => _ssds.Count;
    public bool HasSSD  => _ssds.Count > 0;
 
    /// <summary>Nhặt SSD. Trả về true nếu thành công.</summary>
    public bool PickupSSD(SSDItem item)
    {
        if (_ssds.Contains(item)) return false;
        _ssds.Add(item);
        Debug.Log($"[Inventory] Nhặt: {item.itemName} | Tổng: {_ssds.Count}");
        UpdateUI();
        return true;
    }
 
    /// <summary>Dùng 1 SSD (khi gắn vào server). Trả về true nếu còn SSD.</summary>
    public bool UseSSD()
    {
        if (_ssds.Count == 0) return false;
        SSDItem used = _ssds[_ssds.Count - 1];
        _ssds.RemoveAt(_ssds.Count - 1);
        Debug.Log($"[Inventory] Dùng SSD. Còn lại: {_ssds.Count}");
        UpdateUI();
        return true;
    }
 
    private void UpdateUI()
    {
        if (ssdCountText != null)
            ssdCountText.text = $"SSD: {_ssds.Count}";
    }
}