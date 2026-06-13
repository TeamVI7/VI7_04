using UnityEngine;

/// <summary>
/// Optional display metadata for WeaponHUD.
/// Add to each weapon GameObject alongside WeaponsController.
/// If absent, WeaponHUD falls back to gameObject.name and "FMJ".
/// </summary>
public class WeaponDisplayInfo : MonoBehaviour
{
    [Tooltip("e.g. GLOCK G17  (shown before the // caliber)")]
    public string displayName = "WEAPON";

    [Tooltip("e.g. 9MM  (shown after //)")]
    public string caliber     = "";

    [Tooltip("Ammo type label shown in the FMJ box: FMJ / HP / AP / FRAG")]
    public string ammoType    = "FMJ";

    [Tooltip("Icon shown in the weapon HUD image slot.")]
    public Sprite weaponSprite;
}