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
    public float fireRate = 0.08f;
    public float damagePerShot = 6f;
    public float spread = 2f;
    public LayerMask hitMask;
    public GameObject muzzleFlashPrefab;

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

    public override bool IsAvailable(float distanceToPlayer)
    {
        if (!base.IsAvailable(distanceToPlayer)) return false;
        return IsFacingPlayer();
    }

    private bool IsFacingPlayer()
    {
        if (PlayerHealth.Transform == null) return false;

        Vector3 toPlayer = PlayerHealth.Transform.position - transform.position;
        toPlayer.y = 0f;
        if (toPlayer.sqrMagnitude < 0.01f) return true;

        return Vector3.Angle(transform.forward, toPlayer) <= facingAngleThreshold;
    }

    private static readonly int AnimSpin = Animator.StringToHash("GatlingSpin");
    private static readonly int AnimFire = Animator.StringToHash("GatlingFire");

    protected override void Update()
    {
        base.Update();
        AimAtPlayer();
    }

    private void AimAtPlayer()
    {
        if (gunBone == null || PlayerHealth.Transform == null) return;
        if (brain.State == MechBossState.Dead) return;

        float dist = Vector3.Distance(transform.position, PlayerHealth.Transform.position);
        if (dist > maxRange) return;

        Vector3 dir = (PlayerHealth.Transform.position + Vector3.up - gunBone.position).normalized;
        Quaternion targetRot = Quaternion.LookRotation(dir);
        gunBone.rotation = Quaternion.RotateTowards(gunBone.rotation, targetRot, aimTurnSpeed * Time.deltaTime);
    }

    public override void Execute(Action onComplete) => StartCoroutine(Co_Execute(onComplete));

    private IEnumerator Co_Execute(Action onComplete)
    {
        IsExecuting = true;
        if (animator != null) animator.SetBool(AnimSpin, true);

        float spunUp = 0f;
        while (spunUp < spinUpTime)
        {
            if (PlayerTooClose()) break;
            spunUp += Time.deltaTime;
            yield return null;
        }

        if (!PlayerTooClose())
        {
            if (animator != null) animator.SetBool(AnimFire, true);

            float elapsed = 0f;
            while (elapsed < fireDuration)
            {
                if (PlayerTooClose()) break;
                FireShot();
                elapsed += fireRate;
                yield return new WaitForSeconds(fireRate);
            }
        }

        if (animator != null)
        {
            animator.SetBool(AnimFire, false);
            animator.SetBool(AnimSpin, false);
        }

        IsExecuting = false;
        onComplete?.Invoke();
    }

    private bool PlayerTooClose()
    {
        if (PlayerHealth.Transform == null) return false;
        return Vector3.Distance(transform.position, PlayerHealth.Transform.position) <= breakOffRange;
    }

    private void FireShot()
    {
        if (muzzle == null || PlayerHealth.Transform == null) return;

        Vector3 dir = gunBone != null
            ? gunBone.forward
            : (PlayerHealth.Transform.position + Vector3.up - muzzle.position).normalized;
        dir += UnityEngine.Random.insideUnitSphere * (spread * 0.05f);
        dir.Normalize();

        if (muzzleFlashPrefab != null)
            Destroy(Instantiate(muzzleFlashPrefab, muzzle.position, muzzle.rotation), 0.1f);

        if (Physics.Raycast(muzzle.position, dir, out RaycastHit hit, 60f, hitMask))
            if (hit.collider.TryGetComponent(out PlayerHealth ph)) ph.TakeDamage(damagePerShot);
    }

    private void OnDrawGizmosSelected()
    {
        Vector3 origin = transform.position;
        Vector3 forward = transform.forward;

        // Facing cone: yellow boundary lines at +-facingAngleThreshold, cyan center line.
        Gizmos.color = Color.cyan;
        Gizmos.DrawLine(origin, origin + forward * maxRange);

        Vector3 leftDir = Quaternion.AngleAxis(-facingAngleThreshold, Vector3.up) * forward;
        Vector3 rightDir = Quaternion.AngleAxis(facingAngleThreshold, Vector3.up) * forward;
        Gizmos.color = Color.yellow;
        Gizmos.DrawLine(origin, origin + leftDir * maxRange);
        Gizmos.DrawLine(origin, origin + rightDir * maxRange);

        // Break-off range: the ring inside which Gatling aborts.
        Gizmos.color = new Color(1f, 0.5f, 0f);
        Gizmos.DrawWireSphere(origin, breakOffRange);

        // Live line to the player, green if the facing check currently passes.
        if (Application.isPlaying && PlayerHealth.Transform != null)
        {
            Gizmos.color = IsFacingPlayer() ? Color.green : Color.red;
            Gizmos.DrawLine(origin, PlayerHealth.Transform.position);
        }
    }
}