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

    /// <summary>Fires the instant the mech launches, after the wind-up. Audio hooks
    /// the thruster/charge burst here.</summary>
    public event Action OnDashStarted;
    /// <summary>Fires when the dash actually connects with the player.</summary>
    public event Action OnDashImpact;
    /// <summary>Fires when the dash stops, whether it landed or not.</summary>
    public event Action OnDashEnded;

    private static readonly int AnimDash = Animator.StringToHash("Dash");

    private NavMeshAgent _agent;
    private bool _hasHitPlayer;
    private bool _agentOverridden;
    private bool _wasStopped;

    protected override void Awake()
    {
        base.Awake();
        _agent = GetComponent<NavMeshAgent>();
    }

    protected override IEnumerator Run()
    {
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
            yield break;
        }

        RaiseTelegraphResolved();
        OnDashStarted?.Invoke();

        TakeOverAgent();

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

        ReleaseAgent();
        OnDashEnded?.Invoke();
    }

    // Death or a stagger mid-dash used to leave the agent detached from its
    // transform for good — the boss would survive the stagger and then be unable
    // to move again, with no error to explain why.
    protected override void OnAborted()
    {
        if (TelegraphVFX != null) TelegraphVFX.SetActive(false);
        ReleaseAgent();
        OnDashEnded?.Invoke();
    }

    #region Agent Handover

    private void TakeOverAgent()
    {
        if (_agentOverridden) return;
        _agentOverridden = true;

        // isStopped throws if the agent isn't on a NavMesh, so it's only safe to
        // read (and worth restoring) when the agent is actually live.
        _wasStopped = _agent.isOnNavMesh && _agent.isStopped;

        _agent.updatePosition = false;
        _agent.updateRotation = false;
        if (_agent.isOnNavMesh) _agent.isStopped = true;
    }

    private void ReleaseAgent()
    {
        if (!_agentOverridden) return;
        _agentOverridden = false;

        // The dash drives the transform directly, with no NavMesh clamping — it can
        // finish over a gap, past a ledge or inside geometry. Warping to a point
        // that isn't on the mesh silently fails and takes the agent offline, and
        // both MechChaseBehaviour and MechAttackSelector no-op forever after that
        // (isOnNavMesh guards) with nothing in the console to say why.
        Vector3 landing = transform.position;
        if (!NavMesh.SamplePosition(landing, out NavMeshHit hit, 5f, NavMesh.AllAreas))
        {
            Debug.LogWarning($"[MechDashAttack] Dash ended {landing} with no NavMesh within 5m — " +
                             "the boss would have been stranded. Check the NavMesh bake along the dash path.", this);
        }
        else
        {
            landing = hit.position;
        }

        _agent.Warp(landing);
        _agent.updatePosition = true;
        _agent.updateRotation = true;
        if (_agent.isOnNavMesh) _agent.isStopped = _wasStopped;
    }

    #endregion

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
            OnDashImpact?.Invoke();
        }
    }

    private void OnDrawGizmosSelected()
    {
        if (!drawGizmos) return;

        Vector3 origin = MechGizmos.Ground(transform.position);

        // Where the dash can be launched from, where it stops, and what it sweeps.
        MechGizmos.GroundBand(origin, minRange, maxRange, MechGizmos.Dash, "Dash range", 45f);
        MechGizmos.GroundRing(origin, standoffDistance, MechGizmos.Dash * 0.75f, "standoff", 60f, dashed: true);
        MechGizmos.GroundRing(origin, hitRadius, MechGizmos.Dash, "hit", 75f);
    }
}
