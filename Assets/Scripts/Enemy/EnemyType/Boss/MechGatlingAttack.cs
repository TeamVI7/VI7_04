// MechGatlingAttack.cs — sustained hitscan
using System;
using System.Collections;
using UnityEngine;

public class MechGatlingAttack : MechAttackBehaviour
{
    public Animator animator;
    public Transform muzzle;
    public float spinUpTime = 0.8f;
    public float fireDuration = 3f;
    [Tooltip("Seconds between shots. Clamped to a sensible floor — a rate of 0 would fire an unbounded number of shots without the attack ever ending.")]
    public float fireRate = 0.08f;
    public float damagePerShot = 6f;
    public float spread = 2f;
    public LayerMask hitMask;
    public GameObject muzzleFlashPrefab;

    [Header("Tracer")]
    public TrailRenderer bulletTrailPrefab;
    public float tracerSpeed = 300f;

    [Header("Turret Aim")]
    [Tooltip("The rotating gun bone/gimbal. Rotates to track the player whenever they're in range; muzzle should be a child of this so it follows automatically.")]
    public Transform gunBone;
    [Tooltip("Degrees/sec the bone can turn. Lower = more visible lag catching up to a moving player, so shots can miss.")]
    public float aimTurnSpeed = 120f;

    [Header("Break Off")]
    [Tooltip("If the player closes inside this range mid-attack, stop firing early and hand control back to the brain (should be >= Stomp's Max Range so the selector picks Stomp next).")]
    public float breakOffRange = 6f;

    [Header("Facing Requirement")]
    [Tooltip("The mech's body (this transform's forward) must be within this many degrees of the player for the selector to consider Gatling.")]
    public float facingAngleThreshold = 45f;

    // Sustained ranged attack, so it benefits from the mech kiting/repositioning
    // instead of standing rooted for the whole spin-up + fire duration.
    public override bool AllowsMovementDuringExecution => true;

    [Header("Line of Sight")]
    [Tooltip("Layers that block sight to the player (walls, cover, etc.) — must NOT include the player's own layer. Leave as Nothing to disable LOS gating entirely (old behaviour: always 'sees' the player).")]
    public LayerMask losBlockMask;
    [Tooltip("Point sight is checked and aimed from. Defaults to the muzzle if unset.")]
    public Transform sightOrigin;

    /// <summary>Fires as the barrels start turning — hook the spin-up whine here.</summary>
    public event Action OnSpinUpStarted;
    /// <summary>Fires once when the gun actually opens up.</summary>
    public event Action OnFireStarted;
    /// <summary>Fires per shot, so audio can rattle in step with the real fire rate.</summary>
    public event Action OnShotFired;
    /// <summary>Fires when the gun stops, however it stopped — finished, broken off,
    /// or aborted by a stagger. Audio uses it to kill the loop.</summary>
    public event Action OnFireStopped;

    // Updated only when the player is actually visible, so the turret/shots keep
    // tracking the last place we saw them instead of snapping through walls.
    private Vector3 _lastSeenPlayerPos;
    private bool _hasSeenPlayer;

    public override bool IsAvailable(float distanceToPlayer)
    {
        if (!base.IsAvailable(distanceToPlayer)) return false;
        if (!IsFacingPlayer()) return false;
        if (PlayerHealth.Transform == null) return false;
        // Don't start spinning up on a player we can't currently see.
        return HasLineOfSight(SightOrigin, PlayerHealth.Transform.position + Vector3.up);
    }

    private bool IsFacingPlayer()
    {
        if (PlayerHealth.Transform == null) return false;

        Vector3 toPlayer = PlayerHealth.Transform.position - transform.position;
        toPlayer.y = 0f;
        if (toPlayer.sqrMagnitude < 0.01f) return true;

        return Vector3.Angle(transform.forward, toPlayer) <= facingAngleThreshold;
    }

    private Vector3 SightOrigin => sightOrigin != null ? sightOrigin.position : (muzzle != null ? muzzle.position : transform.position);

    private bool HasLineOfSight(Vector3 from, Vector3 to)
    {
        Vector3 dir = to - from;
        float dist = dir.magnitude;
        if (dist < 0.01f) return true;
        return !Physics.Raycast(from, dir.normalized, dist, losBlockMask);
    }

    // Refreshes the last-seen position whenever the player is actually visible and
    // returns the point to aim/fire at: the player's real position when seen, or
    // the last place we saw them otherwise. Returns false only if we've never seen
    // the player at all (nothing to aim at yet).
    private bool TryGetAimPoint(out Vector3 aimPoint)
    {
        aimPoint = _lastSeenPlayerPos;
        if (PlayerHealth.Transform == null) return _hasSeenPlayer;

        Vector3 targetPos = PlayerHealth.Transform.position + Vector3.up;
        if (HasLineOfSight(SightOrigin, targetPos))
        {
            _lastSeenPlayerPos = targetPos;
            _hasSeenPlayer = true;
            aimPoint = targetPos;
        }

        return _hasSeenPlayer;
    }

    private static readonly int AnimSpin = Animator.StringToHash("GatlingSpin");
    private static readonly int AnimFire = Animator.StringToHash("GatlingFire");

    // Runs after the Animator has applied this frame's pose, so the gun-bone
    // rotation is a procedural overlay on top of the animation instead of
    // being fought/overwritten by it (Animator evaluates between Update and LateUpdate).
    private void LateUpdate() => AimAtPlayer();

    private void AimAtPlayer()
    {
        if (gunBone == null || PlayerHealth.Transform == null) return;
        if (brain.State == MechBossState.Dead) return;

        float dist = Vector3.Distance(transform.position, PlayerHealth.Transform.position);
        if (dist > maxRange) return;

        if (!TryGetAimPoint(out Vector3 aimPoint)) return;

        Vector3 dir = (aimPoint - gunBone.position).normalized;
        Quaternion targetRot = Quaternion.LookRotation(dir);
        gunBone.rotation = Quaternion.RotateTowards(gunBone.rotation, targetRot, aimTurnSpeed * Time.deltaTime);
    }

    private void OnValidate()
    {
        // At 0 the fire loop advances its clock by 0 per iteration while
        // WaitForSeconds(0) yields a single frame — an attack that never ends.
        fireRate = Mathf.Max(0.01f, fireRate);
    }

    protected override IEnumerator Run()
    {
        if (animator != null) animator.SetBool(AnimSpin, true);
        RaiseTelegraphStart(spinUpTime);
        OnSpinUpStarted?.Invoke();

        float spunUp = 0f;
        bool interrupted = false;
        while (spunUp < spinUpTime)
        {
            if (PlayerTooClose()) { interrupted = true; break; }
            spunUp += Time.deltaTime;
            yield return null;
        }

        if (!interrupted && !PlayerTooClose())
        {
            RaiseTelegraphResolved();

            if (animator != null) animator.SetBool(AnimFire, true);
            OnFireStarted?.Invoke();

            float step = Mathf.Max(0.01f, fireRate);
            float elapsed = 0f;
            while (elapsed < fireDuration)
            {
                if (PlayerTooClose()) break;
                FireShot();
                elapsed += step;
                yield return new WaitForSeconds(step);
            }
        }
        else
        {
            RaiseTelegraphCancelled();
        }

        StopFiring();
    }

    protected override void OnAborted() => StopFiring();

    private void StopFiring()
    {
        if (animator != null)
        {
            animator.SetBool(AnimFire, false);
            animator.SetBool(AnimSpin, false);
        }

        OnFireStopped?.Invoke();
    }

    private bool PlayerTooClose()
    {
        if (PlayerHealth.Transform == null) return false;
        return Vector3.Distance(transform.position, PlayerHealth.Transform.position) <= breakOffRange;
    }

    private void FireShot()
    {
        if (muzzle == null) return;
        if (!TryGetAimPoint(out Vector3 aimPoint)) return;

        // Aim straight at the tracked point from the muzzle rather than trusting
        // gunBone.forward — AimAtPlayer() turns the turret gradually
        // (RotateTowards), so right after spin-up (or if the player is far
        // below the mech) the barrel may not have caught up yet. Using
        // gunBone.forward here fired shots wherever the turret currently was
        // pointed (often still skyward from its resting pose) instead of at
        // the target. The visual turret still tracks via AimAtPlayer; only
        // the actual shot direction is decoupled from its catch-up lag.
        // aimPoint is the player's live position when visible, or the last
        // place they were seen if line of sight is currently broken.
        Vector3 dir = (aimPoint - muzzle.position).normalized;
        dir += UnityEngine.Random.insideUnitSphere * (spread * 0.05f);
        dir.Normalize();

        if (muzzleFlashPrefab != null)
            Destroy(Instantiate(muzzleFlashPrefab, muzzle.position, muzzle.rotation), 0.1f);

        const float range = 60f;
        Vector3 endPoint = muzzle.position + dir * range;

        if (Physics.Raycast(muzzle.position, dir, out RaycastHit hit, range, hitMask))
        {
            endPoint = hit.point;
            if (hit.collider.TryGetComponent(out PlayerHealth ph)) ph.TakeDamage(damagePerShot);
        }

        SpawnBulletTrail(muzzle.position, endPoint);
        OnShotFired?.Invoke();
    }

    private void SpawnBulletTrail(Vector3 start, Vector3 end)
    {
        if (bulletTrailPrefab == null) return;
        StartCoroutine(AnimateTracer(start, end));
    }

    private IEnumerator AnimateTracer(Vector3 start, Vector3 end)
    {
        TrailRenderer trail = Instantiate(bulletTrailPrefab, start, Quaternion.identity);

        float distance = Vector3.Distance(start, end);
        float duration = distance / Mathf.Max(1f, tracerSpeed);
        float elapsed = 0f;

        while (elapsed < duration)
        {
            trail.transform.position = Vector3.Lerp(start, end, elapsed / duration);
            elapsed += Time.deltaTime;
            yield return null;
        }

        trail.transform.position = end;
        Destroy(trail.gameObject, trail.time);
    }

    private void OnDrawGizmosSelected()
    {
        if (!drawGizmos) return;

        Vector3 origin = MechGizmos.Ground(transform.position);

        // Usable band, the cone the body has to be inside, and the ring the gun
        // gives up at — the three gates in the order the selector applies them.
        MechGizmos.GroundBand(origin, minRange, maxRange, MechGizmos.Gatling, "Gatling range", 90f);
        MechGizmos.GroundCone(origin, transform.forward, facingAngleThreshold, maxRange * 0.6f,
                              MechGizmos.Gatling * 0.8f, $"facing ±{facingAngleThreshold:0}°");
        MechGizmos.GroundRing(origin, breakOffRange, MechGizmos.Safe, "break off", 105f, dashed: true);

        if (!Application.isPlaying || PlayerHealth.Transform == null) return;

        // Live read on the two runtime gates, so you can see which one is failing.
        bool hasLos = HasLineOfSight(SightOrigin, PlayerHealth.Transform.position + Vector3.up);
        bool facing = IsFacingPlayer();

        Gizmos.color = (facing && hasLos) ? MechGizmos.Pass : MechGizmos.Fail;
        Gizmos.DrawLine(SightOrigin, PlayerHealth.Transform.position);
        MechGizmos.Label(PlayerHealth.Transform.position + Vector3.up * 2.2f,
                         facing ? (hasLos ? "can fire" : "no line of sight") : "not facing",
                         Gizmos.color);

        if (!hasLos && _hasSeenPlayer)
        {
            Gizmos.color = MechGizmos.Gatling;
            Gizmos.DrawLine(SightOrigin, _lastSeenPlayerPos);
            MechGizmos.Label(_lastSeenPlayerPos, "last seen", MechGizmos.Gatling);
        }
    }
}
