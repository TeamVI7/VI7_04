using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Screen-space weapon HUD utilizing a UI Slider and overlay image for the ammo bar.
///
/// ── Companion script ──────────────────────────────────────────────────────
///  Add WeaponDisplayInfo.cs to each weapon GameObject for display name,
///  caliber, and ammo type.  Falls back to gameObject.name / "FMJ" if absent.
/// </summary>
public class WeaponHUD : MonoBehaviour
{
    // ─────────────────────────────────────────────────────────────────────────
    #region Inspector
    // ─────────────────────────────────────────────────────────────────────────

    [Header("References")]
    public WeaponSwitcherProcedural weaponSwitcher;
    public GameObject hudPanel;

    [Header("Weapon Name + Mode")]
    [Tooltip("'GLOCK G17 // 9MM'")]
    public TextMeshProUGUI weaponNameText;
    [Tooltip("Fire mode label: S / B / A")]
    public TextMeshProUGUI modeText;
    [Tooltip("Ammo type label: FMJ / HP / AP")]
    public TextMeshProUGUI ammoTypeText;

    [Header("Ammo Bar (Slider)")]
    [Tooltip("Standard UI Slider. Interactable should be false. Handle removed.")]
    public Slider ammoSlider;
    [Tooltip("Optional: The Fill image of the slider, used to change color when low.")]
    public Image ammoFillImage;
    public Color ammoNormalColor = new Color(0.75f, 0.70f, 0.60f, 1f);
    public Color ammoLowColor    = new Color(0.82f, 0.28f, 0.22f, 1f);

    [Header("Ammo Count")]
    public TextMeshProUGUI ammoClipText;
    public int lowAmmoThreshold = 3;

    [Header("Magazine Reserve")]
    [Tooltip("Shows spare magazine count (reserve ÷ clipSize).")]
    public TextMeshProUGUI magCountText;

    [Header("Weapon Image")]
    [Tooltip("One Image per weapon slot, same order as weaponSwitcher.weapons.")]
    public Image[] weaponImages;

    [Header("Player Health")]
    public TextMeshProUGUI healthText;
    public Image healthBarFill; 
    public Color healthHighColor = new Color(0.2f, 0.8f, 0.2f, 1f);
    public Color healthLowColor  = new Color(0.8f, 0.2f, 0.2f, 1f);

    #endregion

    // ─────────────────────────────────────────────────────────────────────────
    #region Private State
    // ─────────────────────────────────────────────────────────────────────────

    private WeaponsController _tracked;
    private int _maxClip;

    #endregion

    // ─────────────────────────────────────────────────────────────────────────
    #region Unity Lifecycle
    // ─────────────────────────────────────────────────────────────────────────

    private void OnEnable()
    {
        PlayerHealth.OnHealthChanged += HandleHealthChanged;
    }

    private void OnDisable()
    {
        PlayerHealth.OnHealthChanged -= HandleHealthChanged;
    }

    private void Start()
    {
        if (weaponSwitcher == null)
        {
            Debug.LogWarning("[WeaponHUD] WeaponSwitcher not assigned.", this);
            return;
        }

        weaponSwitcher.OnSwitchStart    += HandleSwitchStart;
        weaponSwitcher.OnSwitchComplete += HandleSwitchComplete;

        if (weaponSwitcher.weapons.Count > 0)
            Bind(weaponSwitcher.weapons[0]);
    }

    private void OnDestroy()
    {
        if (weaponSwitcher == null) return;
        weaponSwitcher.OnSwitchStart    -= HandleSwitchStart;
        weaponSwitcher.OnSwitchComplete -= HandleSwitchComplete;
        Unbind();
    }

    #endregion

    // ─────────────────────────────────────────────────────────────────────────
    #region Event Handlers
    // ─────────────────────────────────────────────────────────────────────────

    private void HandleSwitchStart(WeaponsController o, WeaponsController n)
    {
        // Don't hide — just unbind old and pre-bind new immediately
        Unbind();
        if (n != null) Bind(n);
    }

    private void HandleSwitchComplete(WeaponsController o, WeaponsController n)
    {
        // Bind again in case n changed, ensure visible
        Unbind();
        if (n != null) Bind(n);
        SetVisible(true);
    }
    
    private void HandleAmmoChanged(int clip, int reserve) => RefreshAmmo(clip, reserve);

    private void HandleHealthChanged(float current, float max)
    {
        if (healthText != null)
        {
            healthText.text = Mathf.CeilToInt(current).ToString("D3");
        }

        if (healthBarFill != null && max > 0f)
        {
            float pct = current / max;
            healthBarFill.fillAmount = pct;
            
            if (pct > 0.5f)
            {
                healthBarFill.color = Color.Lerp(Color.yellow, healthHighColor, (pct - 0.5f) * 2f);
            }
            else
            {
                healthBarFill.color = Color.Lerp(healthLowColor, Color.yellow, pct * 2f);
            }
        }
    }

    #endregion

    // ─────────────────────────────────────────────────────────────────────────
    #region Bind / Unbind
    // ─────────────────────────────────────────────────────────────────────────

    private void Bind(WeaponsController w)
    {
        if (w == null) return;
        _tracked = w;
        _tracked.OnAmmoChanged += HandleAmmoChanged;


        _maxClip = w.weaponData != null ? w.weaponData.clipSize : w.CurrentAmmo;
        
        if (ammoSlider != null)
        {
            ammoSlider.maxValue = _maxClip;
            ammoSlider.minValue = 0;
        }

        string displayName = w.weaponData != null ? w.weaponData.displayName : w.gameObject.name.ToUpper();
        string caliber     = w.weaponData?.caliber  ?? "";
        string ammoType    = w.weaponData?.ammoType ?? "FMJ";
        RefreshName(displayName, caliber);

        if (ammoTypeText != null) ammoTypeText.text = ammoType.ToUpper();

        RefreshWeaponImage(weaponSwitcher.weapons.IndexOf(w));
        RefreshAmmo(w.CurrentAmmo, w.ReserveAmmo);
        SetVisible(true);
        StartCoroutine(Co_LateRefresh());
    }
    private IEnumerator Co_LateRefresh()
    {
        yield return null;
        if (_tracked != null)
            RefreshAmmo(_tracked.CurrentAmmo, _tracked.ReserveAmmo);
    }
    
    private void Unbind()
    {
        if (_tracked == null) return;
        _tracked.OnAmmoChanged -= HandleAmmoChanged;
        _tracked = null;
    }

    #endregion

    // ─────────────────────────────────────────────────────────────────────────
    #region Display Refresh
    // ─────────────────────────────────────────────────────────────────────────

    private void RefreshWeaponImage(int index)
    {
        if (weaponImages == null) return;
        for (int i = 0; i < weaponImages.Length; i++)
            if (weaponImages[i] != null)
                weaponImages[i].gameObject.SetActive(i == index);
    }

    private void RefreshName(string name, string caliber)
    {
        if (weaponNameText == null) return;
        weaponNameText.text = string.IsNullOrEmpty(caliber)
            ? name
            : $"{name} // {caliber.ToUpper()}";
    }

    private void RefreshAmmo(int clip, int reserve)
    {
        bool isLow = clip <= lowAmmoThreshold;

        if (ammoClipText != null)
        {
            ammoClipText.text  = clip.ToString("D3");
        }

        if (magCountText != null)
        {
            int spares = _maxClip > 0 ? reserve / _maxClip : 0;
            magCountText.text = spares.ToString("D3");
        }

        if (ammoSlider != null)
        {
            ammoSlider.value = clip;
        }

        if (ammoFillImage != null)
        {
            ammoFillImage.color = isLow ? ammoLowColor : ammoNormalColor;
        }
    }

    private void SetVisible(bool v)
    {
        if (hudPanel != null) hudPanel.SetActive(v);
    }

    #endregion
}