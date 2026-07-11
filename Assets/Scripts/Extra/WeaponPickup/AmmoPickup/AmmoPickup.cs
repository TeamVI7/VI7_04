using UnityEngine;

/// <summary>
/// Grants reserve ammo to the player's WeaponSwitcherProcedural.
/// Either targets one weapon type (matched by WeaponData asset reference) or,
/// with matchAllWeapons on, tops up every weapon the player currently carries —
/// useful for a generic "ammo crate" that isn't gun-specific.
/// </summary>
public class AmmoPickup : PickupBase
{
    [Header("Ammo")]
    [Tooltip("Which weapon's reserve this refills. Ignored if matchAllWeapons is on.")]
    [SerializeField] private WeaponData targetWeaponData;
    [SerializeField] private bool matchAllWeapons = false;
    [SerializeField] private int  ammoAmount = 30;

    protected override bool TryPickup(Collider player)
    {
        var switcher = player.GetComponentInParent<WeaponSwitcherProcedural>();
        if (switcher == null) return false;

        bool grantedAny = false;

        foreach (var weapon in switcher.weapons)
        {
            if (weapon == null || weapon.weaponData == null) continue;
            if (!matchAllWeapons && weapon.weaponData != targetWeaponData) continue;

            if (weapon.AddReserveAmmo(ammoAmount)) grantedAny = true;
        }

        return grantedAny;
    }
}
