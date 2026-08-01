using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Attach to any moving platform (elevator, moving deck, Sea-of-Thieves-style ship).
/// Fixes riders drifting/floating relative to a moving platform, in BOTH directions.
/// Physics collision can PUSH a resting rigidbody when the platform moves up into it,
/// but it can't PULL one down — there's no suction — so a descending platform leaves
/// the rider behind/floating unless carried manually. This carries every registered
/// rider by the platform's own delta position/rotation every FixedUpdate, unconditionally,
/// while they're inside the trigger zone — whether grounded, jumping, or mid-air.
///
/// SETUP:
///   1. Add a trigger Collider covering the platform's floor + headroom for a jump
///      (assign to riderTrigger, or leave empty to use this GameObject's own Collider —
///      just make sure isTrigger is checked).
///   2. Move the platform however you like (DOTween, waypoints, Animator, physics) —
///      this component only reads transform delta, it doesn't drive movement itself.
///   3. Works with any Rigidbody rider — player or otherwise.
///
/// EXTEND:
///   - Toggle carryRotation for turning platforms (ship decks) — carries riders around
///     the platform's pivot, not just straight-line translation.
///   - Subscribe to OnRiderEnter/OnRiderExit for footstep-on-metal SFX, camera shake, etc.
/// </summary>
[DefaultExecutionOrder(150)] // after player/physics movement scripts, so delta capture happens post-move
public class MovingPlatformCarrier : MonoBehaviour
{
    // ─────────────────────────────────────────────────────────────────────────
    #region Inspector
    // ─────────────────────────────────────────────────────────────────────────

    [Header("Detection")]
    [Tooltip("Trigger collider covering standing area PLUS jump headroom. Leave empty to use this GameObject's own Collider (must be isTrigger).")]
    [SerializeField] private Collider riderTrigger;
    [SerializeField] private LayerMask riderMask = ~0;
    [SerializeField] private string riderTag = "Player";

    [Header("Rotation Carry")]
    [Tooltip("Rotate/orbit riders with the platform (turning ship deck). Leave off for a simple vertical elevator.")]
    [SerializeField] private bool carryRotation = false;

    [Header("Debug")]
    [SerializeField] private bool debugLog = false;
    [SerializeField] private bool drawGizmos = true;

    #endregion

    // ─────────────────────────────────────────────────────────────────────────
    #region Events
    // ─────────────────────────────────────────────────────────────────────────

    public event Action<Rigidbody> OnRiderEnter;
    public event Action<Rigidbody> OnRiderExit;

    #endregion

    // ─────────────────────────────────────────────────────────────────────────
    #region Private State
    // ─────────────────────────────────────────────────────────────────────────

    private readonly HashSet<Rigidbody> _riders = new HashSet<Rigidbody>();
    private Vector3    _prevPos;
    private Quaternion _prevRot;
    private bool       _initialised;

    #endregion

    // ─────────────────────────────────────────────────────────────────────────
    #region Unity Lifecycle
    // ─────────────────────────────────────────────────────────────────────────

    private void Awake()
    {
        if (riderTrigger == null) riderTrigger = GetComponent<Collider>();
        if (riderTrigger != null && !riderTrigger.isTrigger)
            LogWarning("Assigned rider trigger is not marked isTrigger — riders won't be detected.");
    }

    private void Start()
    {
        _prevPos     = transform.position;
        _prevRot     = transform.rotation;
        _initialised = true;
    }

    private void FixedUpdate()
    {
        if (!_initialised) return;

        Vector3    deltaPos = transform.position - _prevPos;
        Quaternion deltaRot = transform.rotation * Quaternion.Inverse(_prevRot);

        if (_riders.Count > 0 && (deltaPos.sqrMagnitude > 1e-8f || !RotationsApproxEqual(deltaRot, Quaternion.identity)))
            CarryRiders(deltaPos, deltaRot);

        _prevPos = transform.position;
        _prevRot = transform.rotation;
    }

    private void OnTriggerEnter(Collider other)
    {
        if (!IsValidRider(other, out Rigidbody rb)) return;
        if (!_riders.Add(rb)) return;

        OnRiderEnter?.Invoke(rb);
        Log($"Rider entered: {rb.name}");
    }

    private void OnTriggerExit(Collider other)
    {
        var rb = other.attachedRigidbody;
        if (rb == null || !_riders.Remove(rb)) return;

        OnRiderExit?.Invoke(rb);
        Log($"Rider exited: {rb.name}");
    }

    #endregion

    // ─────────────────────────────────────────────────────────────────────────
    #region Carry Logic
    // ─────────────────────────────────────────────────────────────────────────

    private void CarryRiders(Vector3 deltaPos, Quaternion deltaRot)
    {
        // Always carry, regardless of grounded state. Collision can PUSH a resting
        // rigidbody when the platform moves up into it, but it can't PULL one down —
        // there's no suction. Skipping grounded riders here (relying on collision to
        // "already handle it") works for ascent but leaves the player floating the
        // instant the platform descends faster than gravity would. Manual carry is
        // simpler and correct in both directions, so it's the only path now.
        foreach (var rb in _riders)
        {
            if (rb == null) continue;

            if (carryRotation)
            {
                Vector3 toRider   = rb.position - _prevPos;
                Vector3 rotOffset = (deltaRot * toRider) - toRider;
                rb.position += deltaPos + rotOffset;
                rb.rotation  = deltaRot * rb.rotation;
            }
            else
            {
                rb.position += deltaPos;
            }
        }
    }

    #endregion

    // ─────────────────────────────────────────────────────────────────────────
    #region Helpers
    // ─────────────────────────────────────────────────────────────────────────

    private bool IsValidRider(Collider other, out Rigidbody rb)
    {
        rb = other.attachedRigidbody;
        if (rb == null) return false;
        if (((1 << other.gameObject.layer) & riderMask) == 0) return false;
        if (!string.IsNullOrEmpty(riderTag) && !other.CompareTag(riderTag)) return false;
        return true;
    }

    private static bool RotationsApproxEqual(Quaternion a, Quaternion b) =>
        Mathf.Abs(Quaternion.Dot(a, b)) > 0.99999f;

    #endregion

    // ─────────────────────────────────────────────────────────────────────────
    #region Debug
    // ─────────────────────────────────────────────────────────────────────────

    [System.Diagnostics.Conditional("UNITY_EDITOR")]
    private void Log(string msg)
    {
        if (debugLog) Debug.Log($"[MovingPlatformCarrier] {name}: {msg}", this);
    }

    private void LogWarning(string msg) => Debug.LogWarning($"[MovingPlatformCarrier] {name}: {msg}", this);

#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        if (!drawGizmos) return;
        var col = riderTrigger != null ? riderTrigger : GetComponent<Collider>();
        if (col == null) return;

        Gizmos.color = new Color(0.2f, 1f, 0.4f, 0.25f);
        Gizmos.matrix = col.transform.localToWorldMatrix;

        switch (col)
        {
            case BoxCollider box:
                Gizmos.DrawCube(box.center, box.size);
                break;
            case SphereCollider sph:
                Gizmos.DrawSphere(sph.center, sph.radius);
                break;
            case CapsuleCollider cap:
                Gizmos.DrawCube(cap.center, new Vector3(cap.radius * 2f, cap.height, cap.radius * 2f));
                break;
        }
    }
#endif

    #endregion
}