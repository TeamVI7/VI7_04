using UnityEngine;

/// <summary>
/// Picking this up unlocks ("turns on") a weapon slot on the player's
/// WeaponSwitcherProcedural and, by default, switches to it immediately —
/// classic FPS weapon-crate feel.
///
/// If the player already owns that weapon, the pickup falls back to granting
/// bonusReserveAmmo (if set) instead of doing nothing, so re-walking over a
/// weapon crate you already have isn't wasted.
///
/// SETUP:
///   weaponIndex must match the slot's position in WeaponSwitcherProcedural.weapons
///   in the Inspector — e.g. if the shotgun is element 2 in that list, set weaponIndex = 2.
/// </summary>
public class WeaponPickup : PickupBase
{
    [Header("Weapon")]
    [Tooltip("Index into WeaponSwitcherProcedural.weapons — must match the slot to unlock.")]
    [SerializeField] private int weaponIndex;
    [SerializeField] private bool switchToItImmediately = true;

    [Header("Optional Ammo Bonus")]
    [Tooltip("Extra reserve ammo granted for this weapon on pickup, on top of unlocking it. " +
             "Also used as the fallback grant if the player already owns the weapon.")]
    [SerializeField] private int bonusReserveAmmo = 0;

    protected override bool TryPickup(Collider player)
    {
        var switcher = WeaponSwitcherProcedural.Instance;
        if (switcher == null) return false;

        bool justUnlocked = switcher.PickupWeapon(weaponIndex, switchToItImmediately);

        WeaponsController weapon = (weaponIndex >= 0 && weaponIndex < switcher.weapons.Count)
            ? switcher.weapons[weaponIndex]
            : null;

        if (bonusReserveAmmo > 0 && weapon != null)
        {
            bool grantedAmmo = weapon.AddReserveAmmo(bonusReserveAmmo);
            // Already owned the gun -> ammo grant is what decides if the pickup gets collected.
            if (!justUnlocked) return grantedAmmo;
        }

        return justUnlocked;
    }
}