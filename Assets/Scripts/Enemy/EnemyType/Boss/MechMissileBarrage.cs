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

    /// <summary>Fires as the pods open, before the first tube empties.</summary>
    public event Action OnVolleyStarted;
    /// <summary>Fires per missile leaving the rack, so audio can stutter with the launches.</summary>
    public event Action OnMissileLaunched;
    /// <summary>Fires when the rack is empty (or the volley was aborted).</summary>
    public event Action OnVolleyEnded;

    private int _animTriggerHash;
    private int _nextLaunchPoint;
    private bool _volleyLive;

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

    protected override IEnumerator Run()
    {
        if (animator != null && _animTriggerHash != 0) animator.SetTrigger(_animTriggerHash);
        RaiseTelegraphStart(telegraphTime);
        _volleyLive = true;
        OnVolleyStarted?.Invoke();

        yield return new WaitForSeconds(telegraphTime);

        if (PlayerHealth.Transform == null || barrageMissilePrefab == null)
        {
            if (barrageMissilePrefab == null)
                Debug.LogWarning("[MechMissileBarrageAttack] No barrageMissilePrefab assigned — volley skipped.", this);

            RaiseTelegraphCancelled();
            EndVolley();
            yield break;
        }

        RaiseTelegraphResolved();

        int count = UnityEngine.Random.Range(minMissiles, maxMissiles + 1);
        for (int i = 0; i < count; i++)
        {
            LaunchOne();
            yield return new WaitForSeconds(delayBetweenLaunches);
        }

        EndVolley();

        yield return new WaitForSeconds(recoveryTime);
    }

    protected override void OnAborted() => EndVolley();

    // Idempotent: the rack empties before the recovery tail, so a stagger during
    // that tail must not fire a second "volley over" cue.
    private void EndVolley()
    {
        if (!_volleyLive) return;
        _volleyLive = false;
        OnVolleyEnded?.Invoke();
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

        OnMissileLaunched?.Invoke();
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
        if (!drawGizmos) return;

        Vector3 origin = MechGizmos.Ground(transform.position);
        MechGizmos.GroundBand(origin, minRange, maxRange, MechGizmos.Missile, "Barrage range", 195f);

        // Where the volley will actually land — the only part of this attack the
        // player has to read, so it gets drawn on the ground under them.
        if (Application.isPlaying && PlayerHealth.Transform != null)
        {
            MechGizmos.GroundRing(MechGizmos.Ground(PlayerHealth.Transform.position), scatterRadius,
                                  MechGizmos.Missile, $"scatter ×{minMissiles}-{maxMissiles}", 0f);
        }

        // The pods themselves, so an unassigned or mis-parented launch point is
        // obvious rather than showing up as missiles spawning inside the mech.
        if (launchPoints == null) return;
        Gizmos.color = MechGizmos.Missile;
        foreach (var pod in launchPoints)
        {
            if (pod == null) continue;
            Gizmos.DrawWireSphere(pod.position, 0.25f);
            Gizmos.DrawRay(pod.position, pod.forward * 1.5f);
        }
    }
}
