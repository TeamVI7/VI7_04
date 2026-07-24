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

    private bool _animSignal_Launch;
    private static readonly int AnimMissile = Animator.StringToHash("MissileLaunch");

    public void AnimEvent_LaunchMissile() => _animSignal_Launch = true;

    public override void Execute(Action onComplete) => StartCoroutine(Co_Execute(onComplete));

    private IEnumerator Co_Execute(Action onComplete)
    {
        IsExecuting = true;
        if (animator != null) animator.SetTrigger(AnimMissile);
        yield return new WaitForSeconds(telegraphTime);

        for (int i = 0; i < missileCount; i++)
        {
            _animSignal_Launch = false;
            float waited = 0f;
            while (!_animSignal_Launch && waited < 1f)
            {
                waited += Time.deltaTime;
                yield return null;
            }
            FireMissile();
            yield return new WaitForSeconds(delayBetweenMissiles);
        }

        IsExecuting = false;
        onComplete?.Invoke();
    }

    private void FireMissile()
    {
        if (missilePrefab == null || launchPoint == null || PlayerHealth.Transform == null) return;
        var go = Instantiate(missilePrefab, launchPoint.position, launchPoint.rotation);
        go.GetComponent<HomingMissile>()?.Init(PlayerHealth.Transform);
    }
}