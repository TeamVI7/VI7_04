using System.Collections;
using UnityEngine;

/// <summary>
/// Semi-auto single-shot hitscan. Aggro/range/cooldown/aim gating lives in
/// EnemyRangedAttackBehaviour — this only owns damage-per-shot and the tracer VFX.
/// Distinguishes itself from SMG by a slower FireRate and higher DamagePerShot —
/// same weapon shape, different pacing.
/// </summary>
public class PistolAttackBehaviour : EnemyRangedAttackBehaviour
{
    [Header("Pistol Setup")]
    public float DamagePerShot = 6f;

    [Header("Visual Effects")]
    public TrailRenderer BulletTrailPrefab;
    public float TracerSpeed = 300f;

    public event System.Action OnPistolFired
    {
        add    => OnFired += value;
        remove => OnFired -= value;
    }

    protected override void Fire()
    {
        Vector3 targetPos     = GetTargetPoint();
        Vector3 aimDirection  = (targetPos - FirePoint.position).normalized;
        Vector3 fireDirection = ApplySpread(aimDirection);

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
        Destroy(trail.gameObject, trail.time);
    }
}