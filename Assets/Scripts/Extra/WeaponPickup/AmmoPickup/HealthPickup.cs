using UnityEngine;

/// <summary>
/// Heals the player on pickup via PlayerHealth.Instance.Heal(). Uses your
/// existing static PlayerHealth.Instance reference, so no scene wiring needed.
/// </summary>
public class HealthPickup : PickupBase
{
    [Header("Health")]
    [SerializeField] private float healAmount = 25f;
    [Tooltip("If true, the pickup is left uncollected when the player is already at full HP.")]
    [SerializeField] private bool skipIfFullHP = true;

    protected override bool TryPickup(Collider player)
    {
        var health = PlayerHealth.Instance;
        if (health == null) return false;

        if (skipIfFullHP && health.HP >= health.MaxHP) return false;

        health.Heal(healAmount);
        return true;
    }
}
