using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Drives world-space IK foot targets for a heavy mech walk.
/// Keep the foot targets outside the moving mech hierarchy — they must NOT
/// be parented to any bone, or deleting/renaming a bone later (e.g. removing
/// a toe bone) silently breaks the reference and can hang the whole gait.
/// This script does not move bones directly; your Animation Rigging constraint does that.
///
/// NOTE: after re-parenting anything in the rig (footTarget, poleTarget, tipBone),
/// click "Rebuild Rig" on the RigBuilder component (or toggle it off/on) — Animation
/// Rigging bakes constraint data into a PlayableGraph and won't pick up hierarchy
/// changes until it rebuilds.
/// </summary>
public class ProceduralMechWalk : MonoBehaviour
{
    // ─────────────────────────────────────────────────────────────────────────
    #region Inspector
    // ─────────────────────────────────────────────────────────────────────────

    [Serializable]
    public class Leg
    {
        public string legName = "Leg";

        [Header("Skeleton")]
        [Tooltip("First bone the IK solver is allowed to rotate.")]
        public Transform legRoot;

        [Tooltip("Actual LAST bone of this leg chain (an armature bone, not the target helper). " +
                 "If you remove a terminal bone (e.g. a toe/Foot bone), update this to the new last bone.")]
        public Transform tipBone;

        [Header("IK Helpers")]
        [Tooltip("Standalone world-space empty assigned as the IK constraint Target. Must NOT be parented under any bone.")]
        public Transform footTarget;

        [Tooltip("Optional fixed knee-hint object. Must also be standalone, not parented under a bone.")]
        public Transform poleTarget;

        [Header("Stance")]
        public Vector3 restLocalPosition;

        [Tooltip("Distance from ground to the IK tip. Increase this if you removed a foot/toe bone, " +
                 "since tipBone now sits higher above the ground than before.")]
        [Min(0f)] public float tipHeightAboveGround;

        [HideInInspector] public bool stepping;
        [HideInInspector] public float maxReach;
        [HideInInspector] public float stepStartTime;

        [HideInInspector] public Vector3 debugDesired;
        [HideInInspector] public Vector3 debugGroundPoint;
        [HideInInspector] public bool debugHasGround;

        /// <summary>True only if every reference this leg needs is valid right now.</summary>
        public bool IsValid => legRoot != null && tipBone != null && footTarget != null;
    }

    [Header("Legs")]
    public List<Leg> legs = new List<Leg>();

    [Header("Ground")]
    public LayerMask groundMask = ~0;
    [Min(0.01f)] public float groundRayHeight = 2f;
    [Min(0.01f)] public float groundRayDistance = 5f;

    [Header("Gait")]
    [Min(0.01f)] public float stepDistance = 0.55f;
    [Min(0f)] public float strideForwardOffset = 0.3f;
    [Min(0f)] public float stepHeight = 0.08f;
    [Min(0.01f)] public float stepDuration = 0.45f;
    [Range(0.05f, 0.45f)] public float liftPhaseEnd = 0.25f;
    [Range(0.55f, 0.95f)] public float swingPhaseEnd = 0.75f;
    [Range(0.5f, 0.99f)] public float reachSafetyFactor = 0.9f;

    [Header("Heavy Mech Gait")]
    [Tooltip("Ignore tiny body motion so the mech does not tap-dance while nearly idle.")]
    [Min(0f)] public float minimumMoveSpeed = 0.08f;
    [Tooltip("Each moving step advances at least this far in the direction of travel.")]
    [Min(0f)] public float minimumForwardStep = 0.45f;
    [Tooltip("Brief planted pause after a step. Gives the walk a heavy, deliberate rhythm.")]
    [Min(0f)] public float plantedPause = 0.08f;

    [Header("Safety")]
    [Tooltip("If a step coroutine hasn't finished after this many seconds, force-reset it so a single " +
             "broken leg (missing target, exception, destroyed object mid-step, etc.) can never " +
             "permanently freeze the whole gait.")]
    [Min(0.5f)] public float stepWatchdogSeconds = 3f;

    [Header("Optional")]
    [Tooltip("Capture the targets' initial positions as the standing stance when Play begins.")]
    public bool captureRestPositionOnStart = true;

    [Tooltip("Leave off until the foot axes are verified. Use IK Target Rotation Weight = 0 too.")]
    public bool alignTargetRotationToGround;

    [Header("Debug")]
    [Tooltip("Select the mech root in the Scene view to show foot-placement gizmos.")]
    public bool showDebugGizmos = true;
    [SerializeField] private bool showDebugLogs = false;
    [Tooltip("Logs moveSpeed and each leg's stepping flag every frame. Use to diagnose " +
             "'not moving' or 'stuck stepping' issues, then turn back off.")]
    [SerializeField] private bool showPerFrameDiagnostics = false;

    #endregion

    // ─────────────────────────────────────────────────────────────────────────
    #region Events
    // ─────────────────────────────────────────────────────────────────────────

    public event Action<Leg> OnStepStart;
    public event Action<Leg, Vector3> OnStepEnd;
    public event Action<Leg, string> OnLegInvalid;

    #endregion

    // ─────────────────────────────────────────────────────────────────────────
    #region Private State
    // ─────────────────────────────────────────────────────────────────────────

    private Rigidbody _body;
    private UnityEngine.AI.NavMeshAgent _agent;
    private Vector3 _previousPosition;
    private float _nextStepAllowedTime;

    #endregion

    // ─────────────────────────────────────────────────────────────────────────
    #region Unity Lifecycle
    // ─────────────────────────────────────────────────────────────────────────

    private void Awake()
    {
        _body = GetComponent<Rigidbody>();
        _agent = GetComponent<UnityEngine.AI.NavMeshAgent>();
        _previousPosition = transform.position;
    }

    private void Start()
    {
        foreach (Leg leg in legs)
            InitialiseLeg(leg);
    }

    private void Update()
    {
        // Watchdog runs every frame regardless of movement, so a stuck leg gets
        // released even while the mech is standing still.
        TickWatchdog();

        Vector3 velocity = GetBodyVelocity();
        _previousPosition = transform.position;

        Vector3 moveDirection = Vector3.ProjectOnPlane(velocity, Vector3.up);
        float moveSpeed = moveDirection.magnitude;

        if (showPerFrameDiagnostics)
            LogDiagnostics(moveSpeed);

        if (moveSpeed < minimumMoveSpeed) return;

        moveDirection /= moveSpeed;

        EvaluateAndStartBestStep(moveDirection);
    }

    /// <summary>
    /// Priority: NavMeshAgent.velocity (already smoothed/computed by the agent, most
    /// reliable for NavMesh-driven movement) → non-kinematic Rigidbody.linearVelocity
    /// → raw transform-position delta as a last resort. A kinematic Rigidbody's velocity
    /// is always (0,0,0) unless set manually, so it must never be trusted on its own.
    /// </summary>
    private Vector3 GetBodyVelocity()
    {
        if (_agent != null && _agent.enabled && _agent.isOnNavMesh)
            return _agent.velocity;

        if (_body != null && !_body.isKinematic)
            return _body.linearVelocity;

        return (transform.position - _previousPosition) / Mathf.Max(Time.deltaTime, 0.0001f);
    }

    #endregion

    // ─────────────────────────────────────────────────────────────────────────
    #region Setup & Validation
    // ─────────────────────────────────────────────────────────────────────────

    private void InitialiseLeg(Leg leg)
    {
        if (leg == null) return;

        if (!leg.IsValid)
        {
            ReportInvalid(leg);
            return;
        }

        if (captureRestPositionOnStart)
            leg.restLocalPosition = transform.InverseTransformPoint(leg.footTarget.position);

        leg.maxReach = MeasureChainLength(leg.legRoot, leg.tipBone) * reachSafetyFactor;
        Log($"Leg '{leg.legName}' initialised. maxReach={leg.maxReach:F2}");
    }

    /// <summary>
    /// Pinpoints exactly which field is missing so a deleted/renamed bone (e.g. removing
    /// a Foot bone from the chain) shows up as a clear, actionable error instead of a
    /// silent NullReferenceException deep inside the step coroutine.
    /// </summary>
    private void ReportInvalid(Leg leg)
    {
        string missing =
            (leg.legRoot == null ? "legRoot " : "") +
            (leg.tipBone == null ? "tipBone " : "") +
            (leg.footTarget == null ? "footTarget " : "");

        LogError($"Leg '{leg.legName}' is missing: {missing}— this leg will be skipped entirely until fixed. " +
                  "If you removed a terminal bone (e.g. Foot_L), reassign tipBone to the new last bone " +
                  "in the chain and make sure footTarget is a standalone object, not a child of that bone.");
        OnLegInvalid?.Invoke(leg, missing);
    }

    #endregion

    // ─────────────────────────────────────────────────────────────────────────
    #region Step Logic
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Evaluates every leg's desired step target in a single pass, then starts a coroutine
    /// for ONLY the leg with the largest positional error (furthest from where it needs to be).
    ///
    /// This replaces a naive "loop through legs and start the first one that qualifies"
    /// approach. That approach is biased: whichever leg happens to be checked first in
    /// the list gets to step every time it qualifies, and since AnyLegIsStepping() flips
    /// true the instant that leg's coroutine starts (synchronously, within the same frame),
    /// every leg checked afterward gets blocked for that entire frame. With continuous
    /// forward movement, the first leg in the list re-qualifies almost every cycle, so
    /// later legs' positional error accumulates without bound — they visibly drag/lag
    /// and only catch up in odd timing windows (e.g. right as the mech decelerates and
    /// the first leg briefly fails to re-qualify).
    ///
    /// Picking the leg with the largest error each cycle enforces natural alternation:
    /// once a leg steps, its error resets near zero, so the OTHER leg (whose error kept
    /// growing while it waited) is guaranteed to win the next cycle.
    /// </summary>
    private void EvaluateAndStartBestStep(Vector3 moveDirection)
    {
        if (Time.time < _nextStepAllowedTime) return;
        if (AnyLegIsStepping()) return;

        Leg bestLeg = null;
        Vector3 bestTarget = Vector3.zero;
        Vector3 bestNormal = Vector3.up;
        float bestDistance = -1f;

        foreach (Leg leg in legs)
        {
            if (leg == null || leg.stepping || !leg.IsValid) continue;

            Vector3 desired = transform.TransformPoint(leg.restLocalPosition);
            desired += moveDirection * strideForwardOffset;

            // Do not allow tiny shuffling steps. Once a foot commits, make it travel a
            // meaningful distance ahead of where it was planted.
            Vector3 toDesired = desired - leg.footTarget.position;
            float forwardAmount = Vector3.Dot(toDesired, moveDirection);
            if (forwardAmount < minimumForwardStep)
                desired += moveDirection * (minimumForwardStep - forwardAmount);

            leg.debugDesired = desired;
            leg.debugHasGround = false;

            if (!Physics.Raycast(desired + Vector3.up * groundRayHeight, Vector3.down,
                    out RaycastHit hit, groundRayDistance, groundMask, QueryTriggerInteraction.Ignore))
            {
                Log($"Leg '{leg.legName}': ground raycast missed at {desired}.");
                continue;
            }

            leg.debugHasGround = true;
            leg.debugGroundPoint = hit.point;

            // An IK target controls the tip bone, not necessarily the visible sole.
            Vector3 target = hit.point + hit.normal * leg.tipHeightAboveGround;
            target = ClampToReach(leg, target);

            float distance = Vector3.Distance(leg.footTarget.position, target);
            if (distance < stepDistance) continue;

            if (distance > bestDistance)
            {
                bestDistance = distance;
                bestLeg = leg;
                bestTarget = target;
                bestNormal = hit.normal;
            }
        }

        if (bestLeg != null)
            StartCoroutine(Step(bestLeg, bestTarget, bestNormal));
    }

    private bool AnyLegIsStepping()
    {
        foreach (Leg other in legs)
            if (other != null && other.stepping)
                return true;
        return false;
    }

    /// <summary>
    /// Force-clears any leg whose step has run longer than stepWatchdogSeconds.
    /// This is the safety net: even if a leg's target is destroyed mid-swing and the
    /// coroutine throws, this guarantees the gait recovers instead of freezing forever.
    /// </summary>
    private void TickWatchdog()
    {
        foreach (Leg leg in legs)
        {
            if (leg == null || !leg.stepping) continue;
            if (Time.time - leg.stepStartTime < stepWatchdogSeconds) continue;

            LogError($"Leg '{leg.legName}' step watchdog triggered — forcing reset. " +
                      "Check for a missing footTarget/tipBone or an exception in the Console.");
            leg.stepping = false;
        }
    }

    private IEnumerator Step(Leg leg, Vector3 end, Vector3 groundNormal)
    {
        leg.stepping = true;
        leg.stepStartTime = Time.time;
        OnStepStart?.Invoke(leg);
        Log($"Leg '{leg.legName}': step start → {end}");

        try
        {
            Vector3 start = leg.footTarget.position;
            Vector3 liftedStart = start + Vector3.up * stepHeight;
            Vector3 liftedEnd = end + Vector3.up * stepHeight;
            Quaternion startRotation = leg.footTarget.rotation;
            Quaternion endRotation = Quaternion.FromToRotation(Vector3.up, groundNormal) * transform.rotation;

            float elapsed = 0f;
            while (elapsed < stepDuration)
            {
                // Bail cleanly if the target is destroyed mid-swing instead of throwing
                // (Unity's fake-null: a destroyed Transform still isn't a real C# null,
                // but member access on it throws, so we must check == null explicitly).
                if (leg.footTarget == null)
                {
                    LogError($"Leg '{leg.legName}': footTarget became null mid-step — aborting step safely.");
                    yield break;
                }

                float t = elapsed / stepDuration;
                Vector3 position;

                if (t < liftPhaseEnd)
                    position = Vector3.Lerp(start, liftedStart, t / liftPhaseEnd);
                else if (t < swingPhaseEnd)
                    position = Vector3.Lerp(liftedStart, liftedEnd,
                        (t - liftPhaseEnd) / (swingPhaseEnd - liftPhaseEnd));
                else
                    position = Vector3.Lerp(liftedEnd, end,
                        (t - swingPhaseEnd) / (1f - swingPhaseEnd));

                leg.footTarget.position = position;

                if (alignTargetRotationToGround && t > 0.8f)
                    leg.footTarget.rotation = Quaternion.Slerp(startRotation, endRotation, (t - 0.8f) / 0.2f);

                elapsed += Time.deltaTime;
                yield return null;
            }

            if (leg.footTarget != null)
            {
                leg.footTarget.position = end;
                if (alignTargetRotationToGround)
                    leg.footTarget.rotation = endRotation;
            }
        }
        finally
        {
            // Guaranteed to run even if something above throws or yield breaks early —
            // this is what actually prevents a single broken leg from freezing the whole gait.
            leg.stepping = false;
            _nextStepAllowedTime = Time.time + plantedPause;
            OnStepEnd?.Invoke(leg, end);
            Log($"Leg '{leg.legName}': step end.");
        }
    }

    private Vector3 ClampToReach(Leg leg, Vector3 target)
    {
        if (leg.legRoot == null || float.IsInfinity(leg.maxReach))
            return target;

        Vector3 fromRoot = target - leg.legRoot.position;
        if (fromRoot.magnitude <= leg.maxReach)
            return target;

        return leg.legRoot.position + fromRoot.normalized * leg.maxReach;
    }

    private float MeasureChainLength(Transform root, Transform tip)
    {
        if (root == null || tip == null)
            return float.PositiveInfinity;

        float total = 0f;
        Transform current = tip;
        int safety = 0;

        // Walk upward from the real tip bone. This works even when helper targets live elsewhere.
        while (current != null && current != root && safety < 32)
        {
            Transform parent = current.parent;
            if (parent == null) return float.PositiveInfinity;

            total += Vector3.Distance(current.position, parent.position);
            current = parent;
            safety++;
        }

        return current == root && total > 0.001f ? total : float.PositiveInfinity;
    }

    #endregion

    // ─────────────────────────────────────────────────────────────────────────
    #region Debug
    // ─────────────────────────────────────────────────────────────────────────

    [System.Diagnostics.Conditional("UNITY_EDITOR")]
    private void Log(string msg)
    {
        if (showDebugLogs) Debug.Log($"[ProceduralMechWalk] {msg}", this);
    }

    private void LogError(string msg) => Debug.LogError($"[ProceduralMechWalk] {msg}", this);

    [System.Diagnostics.Conditional("UNITY_EDITOR")]
    private void LogDiagnostics(float moveSpeed)
    {
        string agentInfo = "";
        if (_agent != null)
            agentInfo = $"| agent: enabled={_agent.enabled} onMesh={_agent.isOnNavMesh} " +
                        $"hasPath={_agent.hasPath} remaining={_agent.remainingDistance:F2} " +
                        $"rawVel={_agent.velocity} ";

        string status = $"moveSpeed={moveSpeed:F3} {agentInfo}| ";
        foreach (Leg leg in legs)
        {
            if (leg == null) continue;
            status += $"{leg.legName}:stepping={leg.stepping} ";
        }
        Debug.Log($"[ProceduralMechWalk] {status}", this);
    }

#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        if (!showDebugGizmos || legs == null) return;

        foreach (Leg leg in legs)
        {
            if (leg == null) continue;

            if (leg.legRoot == null || leg.tipBone == null || leg.footTarget == null)
            {
                // Flag broken legs directly in the Scene view — no need to check Console.
                Gizmos.color = Color.red;
                if (leg.legRoot != null)
                    Gizmos.DrawWireSphere(leg.legRoot.position, 0.15f);
                continue;
            }

            Vector3 home = transform.TransformPoint(leg.restLocalPosition);
            Gizmos.color = Color.yellow;
            Gizmos.DrawWireSphere(home, 0.08f);

            Vector3 desired = Application.isPlaying ? leg.debugDesired : home;
            Gizmos.color = Color.cyan;
            Gizmos.DrawWireSphere(desired, 0.06f);
            Gizmos.color = new Color(1f, 0.5f, 0f, 0.8f);
            Gizmos.DrawLine(desired + Vector3.up * groundRayHeight,
                desired + Vector3.up * (groundRayHeight - groundRayDistance));

            if (Application.isPlaying && leg.debugHasGround)
            {
                Gizmos.color = Color.magenta;
                Gizmos.DrawSphere(leg.debugGroundPoint, 0.05f);
            }

            Gizmos.color = leg.stepping ? Color.red : Color.green;
            Gizmos.DrawSphere(leg.footTarget.position, 0.07f);
            Gizmos.DrawLine(leg.legRoot.position, leg.footTarget.position);

            if (!float.IsInfinity(leg.maxReach))
            {
                Gizmos.color = new Color(0.2f, 0.5f, 1f, 0.18f);
                Gizmos.DrawWireSphere(leg.legRoot.position, leg.maxReach);
            }

            if (leg.poleTarget != null)
            {
                Gizmos.color = Color.white;
                Gizmos.DrawWireSphere(leg.poleTarget.position, 0.05f);
            }
        }
    }
#endif

    #endregion
}