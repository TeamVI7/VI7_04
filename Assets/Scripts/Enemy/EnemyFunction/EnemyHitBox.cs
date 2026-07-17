using UnityEngine;

[RequireComponent(typeof(Collider))]
public class EnemyLimbHitbox : MonoBehaviour, IDamageable
{
    [Tooltip("Defaults to this transform if left empty.")]
    public Transform LimbBone;
    [Tooltip("1 = normal. Set higher on a head hitbox for headshot bonus damage, if you want that.")]
    public float DamageMultiplier = 1f;

    [Header("Debug")]
    [SerializeField] private bool debugLog = false;

    private EnemyHealth     _health;
    private ShieldBehaviour _shield;

    private void Awake()
    {
        if (LimbBone == null) LimbBone = transform;

        _health = GetComponentInParent<EnemyHealth>();
        _shield = GetComponentInParent<ShieldBehaviour>();

        // Do NOT set isTrigger here — WeaponsController's hitscan raycast uses
        // QueryTriggerInteraction.Ignore, so a trigger collider is invisible to it.
        // This collider must stay solid to be hit at all.
    }

    public void TakeDamage(float amount, Vector3 hitDirection, Vector3 hitPoint)
    {
        float scaled = amount * DamageMultiplier;

        if (debugLog)
            Debug.Log($"[EnemyLimbHitbox] {name} (bone: {LimbBone.name}) took {scaled:F1} dmg at {hitPoint}, routing to {(_shield != null ? "ShieldBehaviour" : "EnemyHealth")}.", this);

        // Route through the shield first if this enemy has one — a limb hitbox isn't
        // a way to bypass armor, it should behave exactly like a direct hit would.
        if (_shield != null)      _shield.TakeDamage(scaled, hitDirection, hitPoint);
        else if (_health != null) _health.TakeDamage(scaled, hitDirection, hitPoint);
    }
}