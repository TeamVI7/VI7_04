// EarthSpike.cs
// One rock pillar torn up out of the ground by MechEarthSpikeAttack. Owns its
// whole lifecycle — rise, hold, sink, destroy — so the attack that spawned it
// doesn't have to track anything after the slam.
using System.Collections;
using UnityEngine;
using DG.Tweening;

public class EarthSpike : MonoBehaviour
{
    [Header("Rise")]
    [Tooltip("How far the pillar travels up out of the ground. The mesh should be modelled so it's fully buried when sunk by this much.")]
    public float riseHeight = 2.5f;
    public float riseTime = 0.15f;
    [Tooltip("OutBack gives the little overshoot that sells rock cracking upward. OutQuad is the calmer option.")]
    public Ease riseEase = Ease.OutBack;

    [Header("Life")]
    public float holdTime = 1.2f;
    public float sinkTime = 0.5f;
    public Ease sinkEase = Ease.InQuad;

    [Header("Damage")]
    public float damage = 22f;
    [Tooltip("Radius checked once, at the top of the rise. The pillar isn't a persistent hitbox — it hits as it erupts, then it's just scenery in the way.")]
    public float hitRadius = 1.6f;
    [Tooltip("Upward impulse on the player. Being tossed is the readable part of getting caught by one of these.")]
    public float launchForce = 9f;
    public LayerMask playerLayer;

    [Header("FX")]
    [Tooltip("Optional debris/dust burst spawned at the base as the pillar breaks through.")]
    public GameObject burstVfxPrefab;
    [Tooltip("Optional rock-crack one-shot as this pillar erupts. Played by the pillar itself rather than the boss, so a line of them crackles outward from the mech towards the player instead of all sounding from its feet.")]
    public AudioClip eruptClip;
    [Tooltip("Kept well below 1 by default — a pattern is eight of these inside half a second, and they stack.")]
    [Range(0f, 1f)] public float eruptVolume = 0.5f;

    private Vector3 _surfacePoint;
    private float _rise;

    /// <summary>Drives the pillar up out of the given ground point.</summary>
    /// <param name="heightScale">Multiplies how tall this pillar is. The mesh is
    /// scaled on Y and the travel distance scales with it, so a short pillar is a
    /// genuinely smaller rock rather than a full-size one that only half emerges —
    /// the pillar always finishes flush with the ground either way.</param>
    public void Erupt(Vector3 groundPoint, Vector3 groundNormal, float heightScale = 1f)
    {
        _surfacePoint = groundPoint;

        heightScale = Mathf.Max(0.05f, heightScale);
        _rise = riseHeight * heightScale;
        if (!Mathf.Approximately(heightScale, 1f))
        {
            Vector3 s = transform.localScale;
            transform.localScale = new Vector3(s.x, s.y * heightScale, s.z);
        }

        transform.rotation = Quaternion.FromToRotation(Vector3.up, groundNormal)
                             * Quaternion.Euler(0f, Random.Range(0f, 360f), 0f); // vary the silhouette
        transform.position = groundPoint - groundNormal * _rise;

        if (burstVfxPrefab != null) Destroy(Instantiate(burstVfxPrefab, groundPoint, transform.rotation), 3f);
        if (eruptClip != null) AudioSource.PlayClipAtPoint(eruptClip, groundPoint, eruptVolume);

        StartCoroutine(Co_Life(groundPoint, groundNormal));
    }

    private IEnumerator Co_Life(Vector3 groundPoint, Vector3 groundNormal)
    {
        yield return transform
            .DOMove(groundPoint, riseTime)
            .SetEase(riseEase)
            .WaitForCompletion();

        DealHit();

        yield return new WaitForSeconds(holdTime);

        yield return transform
            .DOMove(groundPoint - groundNormal * _rise, sinkTime)
            .SetEase(sinkEase)
            .WaitForCompletion();

        Destroy(gameObject);
    }

    private void DealHit()
    {
        foreach (var h in Physics.OverlapSphere(_surfacePoint, hitRadius, playerLayer))
        {
            if (!h.TryGetComponent(out PlayerHealth ph)) continue;
            ph.TakeDamage(damage);

            if (h.attachedRigidbody != null)
                h.attachedRigidbody.AddForce(Vector3.up * launchForce, ForceMode.Impulse);
        }
    }

    private void OnDestroy() => transform.DOKill();

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(0.6f, 0.4f, 0.2f);
        Gizmos.DrawWireSphere(transform.position, hitRadius);
    }
}
