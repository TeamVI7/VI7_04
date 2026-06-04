using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// World-space weapon HUD — canvas follows the camera in 3D space.
///
/// ── Canvas Setup ──────────────────────────────────────────────────────────
///  1. Create a Canvas.
///       Render Mode  →  World Space
///       Event Camera →  your Camera
///  2. Set canvas scale to ~0.001 on all axes (converts pixel units → meters).
///  3. Set RectTransform Width/Height to e.g. 400 × 200.
///  4. Do NOT parent the Canvas to the Camera — this script moves it each frame.
///  5. Inside the canvas build your layout (same as screen-space):
///       WeaponName, AmmoClip, AmmoSlash, AmmoReserve, LowAmmoObject, SlotsParent
///
/// ── World-Space Offset ────────────────────────────────────────────────────
///  positionOffset is in camera-local space.
///  Default (0.13, -0.08, 0.25) puts it bottom-right, 25 cm in front of lens.
///  Tweak until it sits where you want in the viewport.
///
/// ── Slots ─────────────────────────────────────────────────────────────────
///  slotImages — one Image per weapon, same order as WeaponSwitcher.weapons.
///  slotKeyLabels — optional TMP labels inside each slot ("1", "2", …).
/// </summary>
public class WeaponHUD : MonoBehaviour
{
    // ─────────────────────────────────────────────────────────────────────────
    #region Inspector
    // ─────────────────────────────────────────────────────────────────────────

    [Header("References")]
    public WeaponSwitcher weaponSwitcher;

    [Header("World Space")]
    [Tooltip("The Canvas component (Render Mode must be World Space).")]
    public Canvas canvas;
    [Tooltip("Camera the canvas follows and faces.")]
    public Camera targetCamera;
    [Tooltip("Position in camera-local space. Z = distance in front of lens.")]
    public Vector3 positionOffset = new Vector3(0.13f, -0.08f, 0.25f);
    [Tooltip("Optional extra rotation offset on the canvas.")]
    public Vector3 rotationOffset = Vector3.zero;

    [Header("Ammo Text")]
    public TextMeshProUGUI weaponNameText;
    public TextMeshProUGUI ammoClipText;
    public TextMeshProUGUI ammoReserveText;

    [Header("Low Ammo Warning")]
    [Tooltip("Any GameObject (Text, Image…). Shown when clip ≤ lowAmmoThreshold.")]
    public GameObject lowAmmoIndicator;
    [Tooltip("Clip count at or below which the warning activates.")]
    public int   lowAmmoThreshold = 3;
    public Color normalAmmoColor  = Color.white;
    public Color lowAmmoColor     = new Color(1f, 0.25f, 0.25f);

    [Header("Weapon Slots")]
    [Tooltip("One Image per slot, same order as WeaponSwitcher.weapons.")]
    public List<Image> slotImages = new();
    [Tooltip("Optional TMP key labels inside each slot (auto-fills '1','2'… if empty).")]
    public List<TextMeshProUGUI> slotKeyLabels = new();
    public Color slotActiveColor   = new Color(0.91f, 0.79f, 0.48f);
    public Color slotInactiveColor = new Color(0.29f, 0.28f, 0.27f);

    [Header("Misc")]
    [Tooltip("Root panel — hidden while switching weapons.")]
    public GameObject hudPanel;

    #endregion

    // ─────────────────────────────────────────────────────────────────────────
    #region Private State
    // ─────────────────────────────────────────────────────────────────────────

    private WeaponsController _trackedWeapon;
    private Transform         _camTransform;

    #endregion

    // ─────────────────────────────────────────────────────────────────────────
    #region Unity Lifecycle
    // ─────────────────────────────────────────────────────────────────────────

    private void Start()
    {
        if (weaponSwitcher == null)
        {
            Debug.LogWarning("[WeaponHUD] WeaponSwitcher not assigned.", this);
            return;
        }

        // Cache camera transform; fall back to Camera.main.
        if (targetCamera == null) targetCamera = Camera.main;
        if (targetCamera != null)
        {
            _camTransform = targetCamera.transform;
            if (canvas != null) canvas.worldCamera = targetCamera;
        }

        weaponSwitcher.OnSwitchStart    += HandleSwitchStart;
        weaponSwitcher.OnSwitchComplete += HandleSwitchComplete;

        AutoFillSlotLabels();

        if (weaponSwitcher.weapons.Count > 0)
            BindToWeapon(weaponSwitcher.weapons[0]);

        RefreshSlots(0);
    }

    private void LateUpdate()
    {
        // Move and orient the canvas so it always sits at positionOffset
        // relative to the camera — same effect as parenting but lets you
        // adjust the offset at runtime without messing up the canvas hierarchy.
        if (_camTransform == null || canvas == null) return;

        canvas.transform.position = _camTransform.TransformPoint(positionOffset);
        canvas.transform.rotation = _camTransform.rotation * Quaternion.Euler(rotationOffset);
    }

    private void OnDestroy()
    {
        if (weaponSwitcher == null) return;
        weaponSwitcher.OnSwitchStart    -= HandleSwitchStart;
        weaponSwitcher.OnSwitchComplete -= HandleSwitchComplete;
        UnbindCurrentWeapon();
    }

    #endregion

    // ─────────────────────────────────────────────────────────────────────────
    #region Event Handlers
    // ─────────────────────────────────────────────────────────────────────────

    private void HandleSwitchStart(WeaponsController outgoing, WeaponsController incoming)
    {
        SetHudVisible(false);
        UnbindCurrentWeapon();
    }

    private void HandleSwitchComplete(WeaponsController outgoing, WeaponsController incoming)
    {
        BindToWeapon(incoming);
        RefreshSlots(weaponSwitcher.CurrentIndex);
        SetHudVisible(true);
    }

    private void HandleAmmoChanged(int clip, int reserve) => RefreshAmmo(clip, reserve);

    #endregion

    // ─────────────────────────────────────────────────────────────────────────
    #region Bind / Unbind
    // ─────────────────────────────────────────────────────────────────────────

    private void BindToWeapon(WeaponsController weapon)
    {
        if (weapon == null) return;
        _trackedWeapon = weapon;
        _trackedWeapon.OnAmmoChanged += HandleAmmoChanged;
        RefreshName(weapon.gameObject.name);
        RefreshAmmo(weapon.CurrentAmmo, weapon.ReserveAmmo);
        SetHudVisible(true);
    }

    private void UnbindCurrentWeapon()
    {
        if (_trackedWeapon == null) return;
        _trackedWeapon.OnAmmoChanged -= HandleAmmoChanged;
        _trackedWeapon = null;
    }

    #endregion

    // ─────────────────────────────────────────────────────────────────────────
    #region Display
    // ─────────────────────────────────────────────────────────────────────────

    private void RefreshName(string weaponName)
    {
        if (weaponNameText != null)
            weaponNameText.text = weaponName.ToUpper();
    }

    private void RefreshAmmo(int clip, int reserve)
    {
        bool isLow = clip <= lowAmmoThreshold;

        if (ammoClipText != null)
        {
            ammoClipText.text  = clip.ToString();
            ammoClipText.color = isLow ? lowAmmoColor : normalAmmoColor;
        }

        if (ammoReserveText != null)
            ammoReserveText.text = reserve.ToString();

        if (lowAmmoIndicator != null)
            lowAmmoIndicator.SetActive(isLow);
    }

    private void RefreshSlots(int activeIndex)
    {
        for (int i = 0; i < slotImages.Count; i++)
        {
            if (slotImages[i] == null) continue;
            slotImages[i].color = i == activeIndex ? slotActiveColor : slotInactiveColor;
        }
    }

    private void SetHudVisible(bool visible)
    {
        if (hudPanel != null) hudPanel.SetActive(visible);
    }

    private void AutoFillSlotLabels()
    {
        for (int i = 0; i < slotKeyLabels.Count; i++)
        {
            if (slotKeyLabels[i] != null && string.IsNullOrEmpty(slotKeyLabels[i].text))
                slotKeyLabels[i].text = (i + 1).ToString();
        }
    }

    #endregion
}