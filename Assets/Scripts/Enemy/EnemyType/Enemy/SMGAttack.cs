using System.Collections;
using UnityEngine;

/// <summary>
/// Automatic SMG fire. All Aggro/range/cooldown gating lives in
/// EnemyRangedAttackBehaviour — this only owns damage-per-shot and the trail VFX.
/// </summary>
public class SMGAttackBehaviour : EnemyRangedAttackBehaviour
{
    [Header("SMG Setup")]
    public float DamagePerShot = 2f;

    [Header("Visual Effects")]
    public TrailRenderer BulletTrailPrefab;
    [Tooltip("How fast the tracer visually travels from muzzle to impact (units/sec). Not the hit-detection speed — damage is still instant hitscan.")]
    public float TracerSpeed = 300f;

    // Kept as a pass-through alias so EnemySoundController's existing
    // `_smg.OnSMGFired += PlaySMG;` wiring keeps compiling unchanged.
    public event System.Action OnSMGFired
    {
        add    => OnFired += value;
        remove => OnFired -= value;
    }

    protected override void Fire()
    {
        Vector3 targetPos     = GetTargetPoint();
        Vector3 fireDirection = (targetPos - FirePoint.position).normalized;

        if (Physics.Raycast(FirePoint.position, fireDirection, out RaycastHit hit, AttackRange, FireHitMask))
        {
            SpawnBulletTrail(FirePoint.position, hit.point);
            if (hit.collider.CompareTag("Player"))
                PlayerHealth.Instance?.TakeDamage(DamagePerShot);
        }
        else
        {
            SpawnBulletTrail(FirePoint.position, FirePoint.position + fireDirection * AttackRange);
        }
    }

    private void SpawnBulletTrail(Vector3 start, Vector3 end)
    {
        if (BulletTrailPrefab == null) return;
        StartCoroutine(AnimateTracer(start, end));
    }

    /// <summary>
    /// TrailRenderer needs an object that actually moves — the trail is drawn behind
    /// whatever the component is attached to, over the object's own Time-window of
    /// history. So this spawns the tracer at the muzzle and moves it to the impact
    /// point over TracerSpeed, instead of just drawing two fixed points like the old
    /// LineRenderer version did.
    /// </summary>
    private IEnumerator AnimateTracer(Vector3 start, Vector3 end)
    {
        TrailRenderer trail = Instantiate(BulletTrailPrefab, start, Quaternion.identity);

        float distance = Vector3.Distance(start, end);
        float duration = distance / Mathf.Max(1f, TracerSpeed);
        float elapsed  = 0f;

        while (elapsed < duration)
        {
            trail.transform.position = Vector3.Lerp(start, end, elapsed / duration);
            elapsed += Time.deltaTime;
            yield return null;
        }

        trail.transform.position = end;

        // Let the trail's own fade-out (its Time setting) finish before destroying it
        Destroy(trail.gameObject, trail.time);
    }
}