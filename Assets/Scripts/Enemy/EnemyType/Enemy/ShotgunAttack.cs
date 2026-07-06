using System.Collections;
using UnityEngine;

/// <summary>
/// Shotgun blast — multiple pellets per shot, fading trails. Aggro/range/cooldown
/// gating lives in EnemyRangedAttackBehaviour.
/// </summary>
public class ShotgunAttackBehaviour : EnemyRangedAttackBehaviour
{
    [Header("Shotgun Setup")]
    public float DamagePerPellet = 4f;
    public int   PelletsPerShot  = 8;
    public float SpreadAngle     = 0.14f;

    [Header("Visual Effects")]
    public TrailRenderer BulletTrailPrefab;
    [Tooltip("Thời gian tia đạn lưu lại và mờ dần trên màn hình")]
    public float TrailDuration = 0.2f;

    // Kept as a pass-through alias so EnemySoundController's reflection-based
    // lookup of "OnShotgunFired" keeps working unchanged.
    public event System.Action OnShotgunFired
    {
        add    => OnFired += value;
        remove => OnFired -= value;
    }

    protected override void Fire()
    {
        Vector3 targetPos     = GetTargetPoint();
        Vector3 baseDirection = (targetPos - FirePoint.position).normalized;

        for (int i = 0; i < PelletsPerShot; i++)
        {
            Vector3 spread         = Random.insideUnitCircle * SpreadAngle;
            Vector3 finalDirection = (baseDirection + spread).normalized;

            if (Physics.Raycast(FirePoint.position, finalDirection, out RaycastHit hit, AttackRange, ObstacleLayers))
            {
                StartCoroutine(SpawnFadingTrail(FirePoint.position, hit.point));
                if (hit.collider.CompareTag("Player"))
                    PlayerHealth.Instance?.TakeDamage(DamagePerPellet);
            }
            else
            {
                StartCoroutine(SpawnFadingTrail(FirePoint.position, FirePoint.position + finalDirection * AttackRange));
            }
        }
    }

    private IEnumerator SpawnFadingTrail(Vector3 start, Vector3 end)
    {
        if (BulletTrailPrefab == null) yield break;

        TrailRenderer trail = Instantiate(BulletTrailPrefab);
        trail.SetPosition(0, start);
        trail.SetPosition(1, end);
        trail.material = new Material(Shader.Find("Sprites/Default"));

        Color startColorOpen = new Color(1f, 0.5f, 0.1f, 1f);
        Color endColorOpen   = new Color(1f, 0.2f, 0f, 0.3f);

        float elapsed = 0f;
        while (elapsed < TrailDuration)
        {
            elapsed += Time.deltaTime;
            float pct = elapsed / TrailDuration;

            trail.startColor = Color.Lerp(startColorOpen, new Color(1f, 0.3f, 0f, 0f), pct);
            trail.endColor   = Color.Lerp(endColorOpen, new Color(0.5f, 0.1f, 0f, 0f), pct);

            yield return null;
        }

        Destroy(trail.gameObject);
    }
}