using UnityEngine;

/// <summary>
/// Deals damage on trigger contact. Drop on any melee enemy.
/// </summary>
[RequireComponent(typeof(EnemyHealth))]
public class MeleeBehaviour : MonoBehaviour
{
    public float Damage    = 10f;
    public float Cooldown  = 1f;
    public GameObject HitFXPrefab;

    private EnemyHealth _health;
    private float       _nextHit;

    private void Awake() => _health = GetComponent<EnemyHealth>();

    private void OnTriggerStay(Collider other)
    {
        if (!_health.IsAlive || Time.time < _nextHit) return;
        if (!other.CompareTag("Player")) return;

        if (other.TryGetComponent(out PlayerHealth ph))
        {
            _nextHit = Time.time + Cooldown;
            ph.TakeDamage(Damage);

            if (HitFXPrefab != null)
            {
                var fx = Instantiate(HitFXPrefab, other.transform.position + Vector3.up, Quaternion.identity);
                Destroy(fx, 1f);
            }
        }
    }
}