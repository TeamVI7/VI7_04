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
 
    private List<SSDItem> _ssds = new List<SSDItem>();
 
    public int SSDCount => _ssds.Count;
    public bool HasSSD  => _ssds.Count > 0;
 
    public bool PickupSSD(SSDItem item)
    {
        if (_ssds.Contains(item)) return false;
        _ssds.Add(item);
        Debug.Log($"[Inventory] Nhặt: {item.itemName} | Tổng: {_ssds.Count}");
        UpdateUI();
        return true;
    }
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