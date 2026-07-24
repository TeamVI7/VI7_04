// HomingMissile.cs
using UnityEngine;

public class HomingMissile : MonoBehaviour
{
    public float speed = 18f;
    public float turnRate = 3f;
    public float damage = 25f;
    public float explosionRadius = 3f;
    public float lifeTime = 6f;
    public GameObject hitEffectPrefab;

    private Transform _target;
    private Rigidbody _rb;

    public void Init(Transform target)
    {
        _target = target;
        _rb = GetComponent<Rigidbody>();
        _rb.linearVelocity = transform.forward * speed;
        Destroy(gameObject, lifeTime);
    }

    private void FixedUpdate()
    {
        if (_target == null) return;
        Vector3 desired = (_target.position + Vector3.up - transform.position).normalized * speed;
        _rb.linearVelocity = Vector3.Lerp(_rb.linearVelocity, desired, turnRate * Time.fixedDeltaTime);
        transform.rotation = Quaternion.LookRotation(_rb.linearVelocity);
    }

    private void OnCollisionEnter(Collision col)
    {
        foreach (var h in Physics.OverlapSphere(transform.position, explosionRadius))
            if (h.TryGetComponent(out PlayerHealth ph)) ph.TakeDamage(damage);

        if (hitEffectPrefab != null) Destroy(Instantiate(hitEffectPrefab, transform.position, Quaternion.identity), 2f);
        Destroy(gameObject);
    }
}