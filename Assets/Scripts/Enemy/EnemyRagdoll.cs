using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Activates ragdoll physics when EnemyHealth reports death.
/// GDD §7.4 — all deaths physics-resolved, fast arrival = violent impact.
/// </summary>
[RequireComponent(typeof(EnemyHealth))]
public class EnemyRagdoll : MonoBehaviour
{
    [Header("Config")]
    public float VelocitySeedScale  = 1.4f;
    public float UpwardKick         = 2f;
    public bool  AutoDespawn        = true;
    public float LifetimeAfterDeath = 8f;

    private Rigidbody[]  _bodies;
    private Collider[]   _colliders;
    private Animator     _animator;
    private NavMeshAgent _nav;
    private Collider     _mainCollider;
    private Rigidbody    _mainRb;
    private EnemyHealth _health;

    private void Awake()
    {
        _health = GetComponent<EnemyHealth>();
        _bodies       = GetComponentsInChildren<Rigidbody>();
        _colliders    = GetComponentsInChildren<Collider>();
        _animator     = GetComponent<Animator>();
        _nav          = GetComponent<NavMeshAgent>();
        _mainCollider = GetComponent<Collider>();
        _mainRb       = GetComponent<Rigidbody>();
        _health.OnDied += Activate;

        SetRagdollActive(false);
    }
    private void OnDestroy() => _health.OnDied -= Activate;

    private void Activate(Vector3 impulse)
    {
        SetRagdollActive(true);

        if (_nav          != null) _nav.enabled          = false;
        if (_mainCollider != null) _mainCollider.enabled  = false;
        if (_mainRb       != null) _mainRb.isKinematic    = true;
        if (_animator     != null) _animator.enabled      = false;

        Rigidbody root = FindRootBone();
        if (root != null)
        {
            Vector3 seed = impulse * VelocitySeedScale + Vector3.up * UpwardKick;
            root.linearVelocity = seed;
            foreach (var rb in _bodies)
            {
                if (rb == root) continue;
                rb.linearVelocity = seed * Random.Range(0.6f, 1f)
                                  + Random.insideUnitSphere * 2f;
            }
        }

        if (AutoDespawn) Destroy(gameObject, LifetimeAfterDeath);
    }

    private void SetRagdollActive(bool on)
    {
        foreach (var rb  in _bodies)    rb.isKinematic = !on;
        foreach (var col in _colliders)
        {
            if (col != _mainCollider) col.enabled = on;
        }
    }

    private Rigidbody FindRootBone()
    {
        foreach (var rb in _bodies)
        {
            string n = rb.name.ToLower();
            if (n.Contains("hip") || n.Contains("root") || n.Contains("pelvis")) return rb;
        }
        return _bodies.Length > 0 ? _bodies[0] : null;
    }
}