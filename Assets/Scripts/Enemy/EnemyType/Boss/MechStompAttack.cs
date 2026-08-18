// MechStompAttack.cs — close-range AOE moveset
using System;
using System.Collections;
using UnityEngine;

public class MechStompAttack : MechAttackBehaviour
{
    public Animator animator;
    public Transform groundPoint;
    public float radius = 5f;
    public float damage = 35f;
    public float knockback = 12f;
    public float launchUpForce = 6f;
    public float windup = 0.5f;
    public float hitWindow = 0.3f;
    public LayerMask playerLayer;
    public GameObject shockwavePrefab;

    private bool _animSignal_Hit;
    private static readonly int AnimStomp = Animator.StringToHash("Kick");

    // Named to match the existing animation clip's event hookup (the clip was built for the old Kick attack).
    public void AnimEvent_KickHit() => _animSignal_Hit = true;

    public override void Execute(Action onComplete)
    {
        if (IsExecuting) return; // guard against a stray double-Execute (e.g. duplicate component on the boss)
        StartCoroutine(Co_Execute(onComplete));
    }

    private IEnumerator Co_Execute(Action onComplete)
    {
        IsExecuting = true;
        _animSignal_Hit = false;
        if (animator != null) animator.SetTrigger(AnimStomp);
        RaiseTelegraphStart(windup);

        float elapsed = 0f;
        while (!_animSignal_Hit && elapsed < windup + hitWindow)
        {
            elapsed += Time.deltaTime;
            yield return null;
        }

        RaiseTelegraphResolved();

        Vector3 origin = groundPoint != null ? groundPoint.position : transform.position;

        if (shockwavePrefab != null)
            Destroy(Instantiate(shockwavePrefab, origin, Quaternion.identity), 3f);

        foreach (var h in Physics.OverlapSphere(origin, radius, playerLayer))
        {
            if (!h.TryGetComponent(out PlayerHealth ph)) continue;
            ph.TakeDamage(damage);

            if (h.attachedRigidbody != null)
            {
                Vector3 outward = h.transform.position - origin;
                outward.y = 0f;
                outward = outward.sqrMagnitude > 0.01f ? outward.normalized : Vector3.forward;
                Vector3 force = outward * knockback + Vector3.up * launchUpForce;
                h.attachedRigidbody.AddForce(force, ForceMode.Impulse);
            }
        }

        yield return new WaitForSeconds(0.4f);
        IsExecuting = false;
        onComplete?.Invoke();
    }

    private void OnDrawGizmosSelected()
    {
        Vector3 origin = groundPoint != null ? groundPoint.position : transform.position;
        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(origin, radius);
    }
}
