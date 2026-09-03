// MechMissileAttack.cs — special attack
using System;
using System.Collections;
using UnityEngine;

public class MechMissileAttack : MechAttackBehaviour
{
    public Animator animator;
    public GameObject missilePrefab;
    public Transform launchPoint;
    public int missileCount = 1;
    public float delayBetweenMissiles = 0.3f;
    public float telegraphTime = 0.6f;
    [Tooltip("How long to wait for the clip's AnimEvent_LaunchMissile before firing anyway. Keep it short — this is dead time the boss spends standing still if the event never arrives.")]
    public float animEventTimeout = 1f;

    /// <summary>Fires per missile leaving the tube.</summary>
    public event Action OnMissileLaunched;

    private bool _animSignal_Launch;
    private static readonly int AnimMissile = Animator.StringToHash("MissileLaunch");

    public void AnimEvent_LaunchMissile() => _animSignal_Launch = true;

    protected override IEnumerator Run()
    {
        if (animator != null) animator.SetTrigger(AnimMissile);

        // Was the only attack that never raised these, so a MechAttackTelegraphAdapter
        // pointed at it sat inert and the missiles came out with no visible wind-up.
        RaiseTelegraphStart(telegraphTime);
        yield return new WaitForSeconds(telegraphTime);
        RaiseTelegraphResolved();

        for (int i = 0; i < missileCount; i++)
        {
            _animSignal_Launch = false;
            float waited = 0f;
            while (!_animSignal_Launch && waited < animEventTimeout)
            {
                waited += Time.deltaTime;
                yield return null;
            }
            FireMissile();
            yield return new WaitForSeconds(delayBetweenMissiles);
        }
    }

    private void FireMissile()
    {
        if (missilePrefab == null || launchPoint == null || PlayerHealth.Transform == null) return;
        var go = Instantiate(missilePrefab, launchPoint.position, launchPoint.rotation);
        go.GetComponent<HomingMissile>()?.Init(PlayerHealth.Transform);
        OnMissileLaunched?.Invoke();
    }

    private void OnDrawGizmosSelected()
    {
        if (!drawGizmos) return;

        MechGizmos.GroundBand(MechGizmos.Ground(transform.position), minRange, maxRange,
                              MechGizmos.Missile * 0.7f, "Missile range", 210f);

        if (launchPoint == null) return;
        Gizmos.color = MechGizmos.Missile * 0.7f;
        Gizmos.DrawWireSphere(launchPoint.position, 0.25f);
        Gizmos.DrawRay(launchPoint.position, launchPoint.forward * 2f);
    }
}
