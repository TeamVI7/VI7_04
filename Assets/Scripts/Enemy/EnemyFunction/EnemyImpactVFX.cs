using UnityEngine;

[RequireComponent(typeof(EnemyHealth))]
public class EnemyImpactVFX : MonoBehaviour
{
    [Header("Impact Particles")]
    public GameObject ImpactPrefab;
    public float ImpactLifetime = 2f;
    [Range(0f, 0.2f)]
    public float MinDamageFraction = 0.01f;

    [Header("Headshot Variant")]
    [Tooltip("Optional — bigger/different burst for headshots. Falls back to ImpactPrefab if left empty.")]
    public GameObject HeadshotImpactPrefab;
    public Transform HeadTransform;
    public float HeadshotRadius = 0.35f;

    private EnemyHealth _health;

    private void Awake() => _health = GetComponent<EnemyHealth>();

    private void OnEnable()  => _health.OnDamaged += HandleDamaged;
    private void OnDisable() => _health.OnDamaged -= HandleDamaged;

    private void HandleDamaged(float amount, float currentHP, float maxHP, Vector3 hitDirection, Vector3 hitPoint)
    {
        if (!_health.IsAlive) return;
        if (ImpactPrefab == null) return;

        float damageFraction = maxHP > 0f ? amount / maxHP : 0f;
        if (damageFraction < MinDamageFraction) return;

        bool headshot = HeadTransform != null &&
                         (hitPoint - HeadTransform.position).sqrMagnitude <= HeadshotRadius * HeadshotRadius;

        GameObject prefab = headshot && HeadshotImpactPrefab != null ? HeadshotImpactPrefab : ImpactPrefab;
        SpawnImpact(prefab, hitPoint, hitDirection);
    }

    private void SpawnImpact(GameObject prefab, Vector3 hitPoint, Vector3 hitDirection)
    {
        // Spray back toward the shooter (opposite the travel direction of the shot) so the
        // burst is actually facing the player's camera instead of away from it.
        Vector3 sprayDir = hitDirection.sqrMagnitude > 0.0001f ? -hitDirection.normalized : Vector3.up;

        var vfx = Instantiate(prefab, hitPoint, Quaternion.LookRotation(sprayDir));
        Destroy(vfx, ImpactLifetime);
    }
}