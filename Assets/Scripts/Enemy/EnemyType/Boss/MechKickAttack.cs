// MechKickAttack.cs — close-range moveset
using System;
using System.Collections;
using UnityEngine;

public class MechKickAttack : MechAttackBehaviour
{
    public Animator animator;
    public Transform footPoint;
    public float hitRadius = 2f;
    public float damage = 40f;
    public float knockback = 15f;
    public float windup = 0.4f;
    public float hitWindow = 0.3f;
    public LayerMask playerLayer;

    private bool _animSignal_Hit;
    private static readonly int AnimKick = Animator.StringToHash("Kick");

    public void AnimEvent_KickHit() => _animSignal_Hit = true;

    public override void Execute(Action onComplete) => StartCoroutine(Co_Execute(onComplete));

    private IEnumerator Co_Execute(Action onComplete)
    {
        IsExecuting = true;
        _animSignal_Hit = false;
        if (animator != null) animator.SetTrigger(AnimKick);

        float elapsed = 0f;
        while (!_animSignal_Hit && elapsed < windup + hitWindow)
        {
            elapsed += Time.deltaTime;
            yield return null;
        }

        Vector3 origin = footPoint != null ? footPoint.position : transform.position;
        foreach (var h in Physics.OverlapSphere(origin, hitRadius, playerLayer))
        {
            if (!h.TryGetComponent(out PlayerHealth ph)) continue;
            ph.TakeDamage(damage);
            if (h.attachedRigidbody != null)
                h.attachedRigidbody.AddForce((h.transform.position - transform.position).normalized * knockback, ForceMode.Impulse);
        }

        yield return new WaitForSeconds(0.3f);
        IsExecuting = false;
        onComplete?.Invoke();
    }

    private void OnDrawGizmosSelected()
    {
        if (footPoint == null) return;
        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(footPoint.position, hitRadius);
    }
}