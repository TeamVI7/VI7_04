// MechEarthSpikeAttack.cs — earthbending-style stomp
// The mech slams the ground and a run of rock pillars tears up out of it. Three
// patterns: a line marching at the player, a ring bursting out around the mech,
// or a cluster erupting under the player's feet.
//
// Each pillar owns its own rise/hold/sink lifecycle (see EarthSpike), so this
// attack only decides *where* and *when* they come up.
using System;
using System.Collections;
using UnityEngine;

public class MechEarthSpikeAttack : MechAttackBehaviour
{
    public enum SpikePattern
    {
        [Tooltip("A run of pillars marching outward from the mech straight at the player. The iconic version — dodge sideways, not backwards.")]
        LineTowardPlayer,
        [Tooltip("Pillars burst out in a circle around the mech. Punishes standing in melee range.")]
        RingAroundMech,
        [Tooltip("Pillars erupt scattered around wherever the player is standing.")]
        ClusterUnderPlayer,
    }

    [Header("Animation")]
    public Animator animator;
    [Tooltip("Animator trigger for the slam. Defaults to the existing stomp clip's trigger so this works without new animation — point it at your own clip if you have one.")]
    public string animatorTrigger = "Kick";
    [Tooltip("Fallback timing if the clip has no AnimEvent_SpikeSlam event on its impact frame.")]
    public float windup = 0.5f;
    [Tooltip("Extra grace after windup for the animation event to land before giving up and erupting anyway.")]
    public float hitWindow = 0.3f;

    [Header("Pattern")]
    [Tooltip("Pick the pattern from how far the player is at cast time instead of always using the one below. Close in it rings, mid it lines, far out it clusters — so the same attack reads differently depending on how the player is playing the fight.")]
    public bool patternByDistance = true;
    [Tooltip("Player closer than this gets the ring — they're inside melee range and shouldn't be able to just stand there.")]
    public float ringDistance = 6f;
    [Tooltip("Player past this gets the cluster under their feet — a line that far out would take too long to travel and be trivially sidestepped.")]
    public float clusterDistance = 12f;
    [Tooltip("Used directly when Pattern By Distance is off, and as the fallback if the player can't be found.")]
    public SpikePattern pattern = SpikePattern.LineTowardPlayer;
    public GameObject spikePrefab;
    public int spikeCount = 8;
    [Tooltip("Seconds between consecutive pillars. This is what makes the line visibly travel — set it to 0 and the whole pattern erupts at once.")]
    public float delayBetweenSpikes = 0.06f;

    [Header("Line")]
    [Tooltip("Distance between pillars along the line. spikeCount * spacing is the reach — keep Max Range at or above that.")]
    public float spacing = 2f;
    [Tooltip("Random sideways wobble per pillar, so the line looks torn open rather than laid out on a ruler.")]
    public float lineJitter = 0.4f;
    [Tooltip("Height multiplier for the first pillar of the line, right at the mech's feet. Below 1 makes the line start as low rubble.")]
    public float lineStartHeightScale = 0.4f;
    [Tooltip("Height multiplier for the last pillar. Above 1 makes the line build to a wall by the time it reaches the player — the ramp is what sells the fissure as travelling outward rather than a row of identical rocks appearing.")]
    public float lineEndHeightScale = 1.6f;
    [Tooltip("Shapes the ramp between the two. Linear is a steady climb; try EaseInOut for a line that stays low then rears up at the end.")]
    public AnimationCurve lineHeightCurve = AnimationCurve.Linear(0f, 0f, 1f, 1f);

    [Header("Ring")]
    public float ringRadius = 5f;

    [Header("Cluster")]
    public float clusterRadius = 4f;

    [Header("Telegraph")]
    [Tooltip("Optional. The transform a MechAttackTelegraphAdapter uses as its Origin Override — e.g. an empty child at the mech's feet. At the START of the wind-up this is moved to the slam origin and aimed flat at the player, and the pillars then commit to exactly that direction. That's what makes the decal honest: where it points is where the rocks come up, even if the player runs during the wind-up.")]
    public Transform telegraphOrigin;

    [Header("Ground")]
    [Tooltip("What counts as ground for placing pillars. Set this to your Ground/Wall layers — pillars are skipped where there's no ground to erupt from.")]
    public LayerMask groundMask = ~0;
    [Tooltip("Optional shockwave/dust VFX spawned at the mech's feet on the slam.")]
    public GameObject slamVfxPrefab;
    public Transform groundPoint;

    private bool _animSignal_Slam;
    private int _animTriggerHash;
    private SpikePattern _activePattern;

    /// <summary>Hook this on the slam's impact frame for exact timing. Optional —
    /// the attack falls back to <see cref="windup"/> if it never arrives.</summary>
    public void AnimEvent_SpikeSlam() => _animSignal_Slam = true;

    private void Reset()
    {
        minPhase = 2;
        minRange = 0f;
        maxRange = 18f;
        cooldown = 8f;
        weight = 1.5f;
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
        _animSignal_Slam = false;

        // Everything about where this attack lands is decided here, before the
        // wind-up, so the telegraph shown during the wind-up is the truth. Aiming
        // at slam instead would mean the decal points one way and the rocks come
        // up another whenever the player moves — an unfair telegraph is worse
        // than none at all.
        Vector3 aimOrigin = groundPoint != null ? groundPoint.position : transform.position;
        Vector3 lockedForward = FlatDirectionToPlayer(aimOrigin);
        Vector3 lockedPlayerAnchor = PlayerHealth.Transform != null ? PlayerHealth.Transform.position : aimOrigin;

        // Locked here with the aim, for the same reason: the telegraph shown during
        // the wind-up has to match the pattern that actually erupts, so the player
        // running out of ring range mid-wind-up doesn't retroactively change the
        // attack out from under the decal.
        _activePattern = ResolvePattern(aimOrigin, lockedPlayerAnchor);

        if (telegraphOrigin != null)
            telegraphOrigin.SetPositionAndRotation(aimOrigin, Quaternion.LookRotation(lockedForward, Vector3.up));

        if (animator != null && _animTriggerHash != 0) animator.SetTrigger(_animTriggerHash);
        RaiseTelegraphStart(windup);

        float elapsed = 0f;
        while (!_animSignal_Slam && elapsed < windup + hitWindow)
        {
            elapsed += Time.deltaTime;
            yield return null;
        }

        RaiseTelegraphResolved();

        Vector3 origin = groundPoint != null ? groundPoint.position : transform.position;
        if (slamVfxPrefab != null) Destroy(Instantiate(slamVfxPrefab, origin, Quaternion.identity), 3f);

        if (spikePrefab == null)
        {
            Debug.LogWarning("[MechEarthSpikeAttack] No spikePrefab assigned — the slam plays but nothing erupts.", this);
        }
        else
        {
            for (int i = 0; i < spikeCount; i++)
            {
                EruptAt(PatternPoint(i, origin, lockedForward, lockedPlayerAnchor), HeightScaleFor(i));
                if (delayBetweenSpikes > 0f) yield return new WaitForSeconds(delayBetweenSpikes);
            }
        }

        yield return new WaitForSeconds(0.4f);
        IsExecuting = false;
        onComplete?.Invoke();
    }

    private Vector3 FlatDirectionToPlayer(Vector3 from)
    {
        if (PlayerHealth.Transform == null) return transform.forward;

        Vector3 toPlayer = PlayerHealth.Transform.position - from;
        toPlayer.y = 0f;
        return toPlayer.sqrMagnitude > 0.01f ? toPlayer.normalized : transform.forward;
    }

    /// <summary>Flat (XZ) distance from the slam origin to the player, ignoring
    /// height so a player on a walkway above the mech still counts as "close".</summary>
    private float FlatDistanceToPlayer(Vector3 from, Vector3 playerAnchor)
    {
        Vector3 delta = playerAnchor - from;
        delta.y = 0f;
        return delta.magnitude;
    }

    private SpikePattern ResolvePattern(Vector3 origin, Vector3 playerAnchor)
    {
        if (!patternByDistance || PlayerHealth.Transform == null) return pattern;

        float distance = FlatDistanceToPlayer(origin, playerAnchor);
        if (distance <= ringDistance) return SpikePattern.RingAroundMech;
        if (distance >= clusterDistance) return SpikePattern.ClusterUnderPlayer;
        return SpikePattern.LineTowardPlayer;
    }

    private Vector3 PatternPoint(int index, Vector3 origin, Vector3 forward, Vector3 playerAnchor)
    {
        switch (_activePattern)
        {
            case SpikePattern.RingAroundMech:
            {
                float angle = (360f / Mathf.Max(1, spikeCount)) * index;
                Vector3 dir = Quaternion.AngleAxis(angle, Vector3.up) * forward;
                return origin + dir * ringRadius;
            }

            case SpikePattern.ClusterUnderPlayer:
            {
                Vector2 offset = UnityEngine.Random.insideUnitCircle * clusterRadius;
                return playerAnchor + new Vector3(offset.x, 0f, offset.y);
            }

            default:
            {
                Vector3 right = Vector3.Cross(Vector3.up, forward);
                float jitter = UnityEngine.Random.Range(-lineJitter, lineJitter);
                return origin + forward * (spacing * (index + 1)) + right * jitter;
            }
        }
    }

    /// <summary>Height multiplier for the pillar at this index. Only the line ramps —
    /// a ring or cluster has no "along the line" to ramp over, and scaling those by
    /// index would just look like random rocks of random sizes.</summary>
    private float HeightScaleFor(int index)
    {
        if (_activePattern != SpikePattern.LineTowardPlayer) return 1f;
        if (spikeCount <= 1) return lineEndHeightScale;

        float t = index / (float)(spikeCount - 1);
        return Mathf.LerpUnclamped(lineStartHeightScale, lineEndHeightScale, lineHeightCurve.Evaluate(t));
    }

    private void EruptAt(Vector3 approxPoint, float heightScale)
    {
        // Probe from above so pillars sit on the actual floor — a stomp on a
        // walkway shouldn't erupt rocks at the walkway's height out in mid-air.
        Vector3 probeStart = approxPoint + Vector3.up * 5f;
        if (!Physics.Raycast(probeStart, Vector3.down, out RaycastHit hit, 20f, groundMask, QueryTriggerInteraction.Ignore))
            return; // no floor here — skip this pillar rather than float one

        GameObject go = Instantiate(spikePrefab, hit.point, Quaternion.identity);
        if (go.TryGetComponent(out EarthSpike spike)) spike.Erupt(hit.point, hit.normal, heightScale);
    }

    private void OnValidate()
    {
        // A cluster band that starts inside the ring band would leave no room for
        // the line at all — nudge it out rather than silently dropping a pattern.
        if (clusterDistance < ringDistance) clusterDistance = ringDistance;
    }

    private void OnDrawGizmosSelected()
    {
        Vector3 origin = groundPoint != null ? groundPoint.position : transform.position;

        if (patternByDistance)
        {
            // The two thresholds that decide which pattern the player gets.
            Gizmos.color = new Color(0.9f, 0.5f, 0.2f, 0.6f);
            Gizmos.DrawWireSphere(origin, ringDistance);
            Gizmos.color = new Color(0.4f, 0.6f, 0.9f, 0.6f);
            Gizmos.DrawWireSphere(origin, clusterDistance);
        }

        Gizmos.color = new Color(0.6f, 0.4f, 0.2f);
        switch (Application.isPlaying && patternByDistance ? _activePattern : pattern)
        {
            case SpikePattern.RingAroundMech:
                Gizmos.DrawWireSphere(origin, ringRadius);
                break;

            case SpikePattern.ClusterUnderPlayer:
                if (Application.isPlaying && PlayerHealth.Transform != null)
                    Gizmos.DrawWireSphere(PlayerHealth.Transform.position, clusterRadius);
                break;

            default:
                for (int i = 0; i < spikeCount; i++)
                {
                    // Height drawn to scale so the ramp is tunable without pressing play.
                    float h = spikeCount > 1
                        ? Mathf.LerpUnclamped(lineStartHeightScale, lineEndHeightScale, lineHeightCurve.Evaluate(i / (float)(spikeCount - 1)))
                        : lineEndHeightScale;
                    Vector3 at = origin + transform.forward * (spacing * (i + 1));
                    Gizmos.DrawWireCube(at + Vector3.up * (h * 0.3f), new Vector3(0.6f, 0.6f * h, 0.6f));
                }
                break;
        }
    }
}
