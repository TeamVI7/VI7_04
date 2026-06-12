using UnityEngine;

/// <summary>
/// Implement this on any GameObject that can receive weapon damage.
/// WeaponsController calls TakeDamage() on every successful hitscan hit.
///
/// Example:
/// <code>
/// public class EnemyHealth : MonoBehaviour, IDamageable
/// {
///     public void TakeDamage(int amount, Vector3 direction, Vector3 hitPoint)
///     {
///         health -= amount;
///         SpawnBloodVFX(hitPoint);
///     }
/// }
/// </code>
/// </summary>
public interface IDamageable
{
    /// <summary>
    /// Called by the weapon system when this object is hit by a bullet.
    /// </summary>
    /// <param name="amount">Raw damage value from <see cref="WeaponData.weaponDamage"/>.</param>
    /// <param name="direction">Normalised world-space bullet travel direction.</param>
    /// <param name="hitPoint">Exact world-space surface contact point.</param>
    void TakeDamage(int amount, Vector3 direction, Vector3 hitPoint);
}