// MechMissileBarrage.cs — phase 3 special
// Empties the pods in one go: 12-14 missiles fired near-vertically, each one
// picking its own impact point around the player at the top of its arc and
// raining back down. Unlike MechMissileAttack this doesn't wait on animation
// events per missile — the volley fires on a fixed cadence so the count is
// guaranteed regardless of what clip is playing.
using System;
using System.Collections;
using UnityEngine;

public class MechMissileBarrageAttack : MechAttackBehaviour
{
    [Header("Animation")]
    public Animator animator;
    [Tooltip("Animator trigger fired as the pods open. Leave empty to skip the animation entirely — the volley still fires.")]
    public string animatorTrigger = "MissileBarrage";

    [Header("Volley")]
    public GameObject barrageMissilePrefab;
    [Tooltip("Pods the missiles leave from, used round-robin. If empty, they spawn at Fallback Launch Offset relative to the mech.")]
    public Transform[] launchPoints;
    [Tooltip("Inclusive lower bound on missiles per volley.")]
    public int minMissiles = 12;
    [Tooltip("Inclusive upper bound on missiles per volley.")]
    public int maxMissiles = 14;
    public float delayBetweenLaunches = 0.1f;
    public float telegraphTime = 1f;
    [Tooltip("Seconds the mech stays locked in the attack after the last tube empties. The missiles fly on their own from that point, so keep this short — it only covers the pod-close animation.")]
    public float recoveryTime = 0.8f;

    [Header("Targeting")]
    [Tooltip("Radius around the player that impact points scatter within. Each missile picks its own point at apex, so a volley walks toward wherever the player ran.")]
    public float scatterRadius = 6f;
    [Tooltip("Used when no launchPoints are assigned — local-space offset from the mech's origin.")]
    public Vector3 fallbackLaunchOffset = new Vector3(0f, 4f, 0f);

    private int _animTriggerHash;
    private int _nextLaunchPoint;

    // Editor-add defaults. This is a long-range phase-3 finisher, so it wants a
    // very different profile from the base class's generic values.
    private void Reset()
    {
        minPhase = 3;
        minRange = 8f;
        maxRange = 45f;
        cooldown = 14f;
        weight = 2f;
    }

    private void OnValidate()
    {
        minMissiles = Mathf.Max(1, minMissiles);
        maxMissiles = Mathf.Max(minMissiles, maxMissiles);
    }

    protected override void Awake()
    {
        base.Awake();
        if (!string.IsNullOrEmpty(animatorTrigger)) _animTriggerHash = Animator.StringToHash(animatorTrigger);
    }

    public override void Execute(Action onComplete)
    {
        if (IsExecuting) return;
        StartCoroutine(Co_Execute(onComplete));
    }

    private IEnumerator Co_Execute(Action onComplete)
    {
        IsExecuting = true;

        if (animator != null && _animTriggerHash != 0) animator.SetTrigger(_animTriggerHash);
        RaiseTelegraphStart(telegraphTime);

        yield return new WaitForSeconds(telegraphTime);

        if (PlayerHealth.Transform == null || barrageMissilePrefab == null)
        {
            if (barrageMissilePrefab == null)
                Debug.LogWarning("[MechMissileBarrageAttack] No barrageMissilePrefab assigned — volley skipped.", this);

            RaiseTelegraphCancelled();
            IsExecuting = false;
            onComplete?.Invoke();
            yield break;
        }

        RaiseTelegraphResolved();

        int count = UnityEngine.Random.Range(minMissiles, maxMissiles + 1);
        for (int i = 0; i < count; i++)
        {
            LaunchOne();
            yield return new WaitForSeconds(delayBetweenLaunches);
        }

        yield return new WaitForSeconds(recoveryTime);

        IsExecuting = false;
        onComplete?.Invoke();
    }

    private void LaunchOne()
    {
        Transform pod = NextLaunchPoint();

        Vector3 pos = pod != null
            ? pod.position
            : transform.position + transform.TransformVector(fallbackLaunchOffset);
        Quaternion rot = pod != null ? pod.rotation : Quaternion.LookRotation(Vector3.up);

        GameObject go = Instantiate(barrageMissilePrefab, pos, rot);

        if (go.TryGetComponent(out BarrageMissile missile)) missile.Init(PlayerHealth.Transform, scatterRadius);
        else Debug.LogWarning($"[MechMissileBarrageAttack] {go.name} has no BarrageMissile component — it will just sit where it spawned.", this);
    }

    private Transform NextLaunchPoint()
    {
        if (launchPoints == null || launchPoints.Length == 0) return null;

        // Skip over empty slots rather than returning null for them, so one
        // unassigned element in the array doesn't silently drop a missile.
        for (int i = 0; i < launchPoints.Length; i++)
        {
            Transform candidate = launchPoints[_nextLaunchPoint];
            _nextLaunchPoint = (_nextLaunchPoint + 1) % launchPoints.Length;
            if (candidate != null) return candidate;
        }
        return null;
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(1f, 0.4f, 0f, 0.6f);
        Gizmos.DrawWireSphere(transform.position, maxRange);
        Gizmos.color = new Color(1f, 0.4f, 0f, 0.3f);
        Gizmos.DrawWireSphere(transform.position, minRange);

        if (Application.isPlaying && PlayerHealth.Transform != null)
        {
            Gizmos.color = Color.red;
            Gizmos.DrawWireSphere(PlayerHealth.Transform.position, scatterRadius);
        }
    }
}
