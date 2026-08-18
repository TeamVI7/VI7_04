// MechDashAttack.cs — long-range gap closer
using System;
using System.Collections;
using UnityEngine;
using UnityEngine.AI;

[RequireComponent(typeof(NavMeshAgent))]
public class MechDashAttack : MechAttackBehaviour
{
    public Animator animator;
    public float dashSpeed = 22f;
    public float windup = 0.3f;
    public float standoffDistance = 4f;
    public float maxDashDuration = 2f;
    public float hitRadius = 1.5f;
    public float damage = 20f;
    public float knockback = 10f;
    public LayerMask playerLayer;

    [Header("Telegraph")]
    [Tooltip("Optional -- enabled for the duration of the windup, disabled the instant the dash starts or is cancelled. Hook a ground-line, glow, or charge-up VFX so the player has a fair chance to see it coming.")]
    public GameObject TelegraphVFX;

    private static readonly int AnimDash = Animator.StringToHash("Dash");

    private NavMeshAgent _agent;
    private bool _hasHitPlayer;

    protected override void Awake()
    {
        base.Awake();
        _agent = GetComponent<NavMeshAgent>();
    }

    public override void Execute(Action onComplete) => StartCoroutine(Co_Execute(onComplete));

    private IEnumerator Co_Execute(Action onComplete)
    {
        IsExecuting = true;
        _hasHitPlayer = false;

        if (animator != null) animator.SetTrigger(AnimDash);
        if (TelegraphVFX != null) TelegraphVFX.SetActive(true);
        RaiseTelegraphStart(windup);

        float telegraphed = 0f;
        bool cancelled = false;
        while (telegraphed < windup)
        {
            if (PlayerHealth.Transform == null) { cancelled = true; break; }
            telegraphed += Time.deltaTime;
            yield return null;
        }

        if (TelegraphVFX != null) TelegraphVFX.SetActive(false);

        if (cancelled || PlayerHealth.Transform == null)
        {
            RaiseTelegraphCancelled();
            IsExecuting = false;
            onComplete?.Invoke();
            yield break;
        }

        RaiseTelegraphResolved();

        bool wasStopped = _agent.isStopped;
        _agent.updatePosition = false;
        _agent.updateRotation = false;
        _agent.isStopped = true;

        float elapsed = 0f;
        while (elapsed < maxDashDuration)
        {
            if (PlayerHealth.Transform == null) break;

            Vector3 toPlayer = PlayerHealth.Transform.position - transform.position;
            toPlayer.y = 0f;
            if (toPlayer.magnitude <= standoffDistance) break;

            Vector3 dir = toPlayer.normalized;
            transform.position += dir * dashSpeed * Time.deltaTime;
            transform.rotation = Quaternion.LookRotation(dir);

            CheckDashHit();

            elapsed += Time.deltaTime;
            yield return null;
        }

        _agent.Warp(transform.position);
        _agent.updatePosition = true;
        _agent.updateRotation = true;
        _agent.isStopped = wasStopped;

        IsExecuting = false;
        onComplete?.Invoke();
    }

    private void CheckDashHit()
    {
        if (_hasHitPlayer) return;

        foreach (var h in Physics.OverlapSphere(transform.position, hitRadius, playerLayer))
        {
            if (!h.TryGetComponent(out PlayerHealth ph)) continue;

            ph.TakeDamage(damage);
            if (h.attachedRigidbody != null)
                h.attachedRigidbody.AddForce((h.transform.position - transform.position).normalized * knockback, ForceMode.Impulse);

            _hasHitPlayer = true;
        }
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.magenta;
        Gizmos.DrawWireSphere(transform.position, standoffDistance);
        Gizmos.color = new Color(1f, 0f, 1f, 0.4f);
        Gizmos.DrawWireSphere(transform.position, maxRange);
    }
}
