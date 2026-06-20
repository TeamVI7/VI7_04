using System;
using UnityEngine;

/// <summary>
/// Sticky surface system. Player auto-sticks when standing on a layer
/// assigned to stickyLayer. Can move freely, cannot jump, pulled down if airborne.
/// Inherits moving platform velocity to prevent bouncing.
///
/// SETUP:
///   1. Add to same GameObject as PlayerMovement.
///   2. Create a Unity Layer (e.g. "StickyGround").
///   3. Assign it to stickyLayer in Inspector.
///   4. Tag your sticky floor objects with that layer.
///
/// EXTENDING:
///   - Subscribe OnStickEnter / OnStickExit for VFX, audio, HUD.
///
/// DEBUG:
///   - Enable debugLog in Inspector.
/// </summary>
public class GroundStick : MonoBehaviour
{
    // ─────────────────────────────────────────────────────────────────────────
    #region Inspector
    // ─────────────────────────────────────────────────────────────────────────

    [Header("Layer")]
    [Tooltip("Objects on this layer will cause player to stick.")]
    public LayerMask stickyLayer;

    [Header("Settings")]
    public float rayDistance     = 1.2f;
    public float groundPullForce = 25f;

    [Header("Debug")]
    [SerializeField] private bool debugLog = false;

    #endregion

    // ─────────────────────────────────────────────────────────────────────────
    #region Events
    // ─────────────────────────────────────────────────────────────────────────

    public event Action OnStickEnter;
    public event Action OnStickExit;

    #endregion

    // ─────────────────────────────────────────────────────────────────────────
    #region Public Read-only State
    // ─────────────────────────────────────────────────────────────────────────

    public bool IsStuck => _isOnStickyLayer;

    #endregion

    // ─────────────────────────────────────────────────────────────────────────
    #region Private
    // ─────────────────────────────────────────────────────────────────────────

    private Rigidbody      rb;
    private PlayerMovement pm;
    private bool           _isOnStickyLayer;
    private Rigidbody      _platformRb;   // rigidbody of the platform under player (null if static)

    #endregion

    // ─────────────────────────────────────────────────────────────────────────
    #region Unity Lifecycle
    // ─────────────────────────────────────────────────────────────────────────

    private void Start()
    {
        rb = GetComponent<Rigidbody>();
        pm = GetComponent<PlayerMovement>();
    }

    private void Update()
    {
        bool onSticky = Physics.Raycast(transform.position, Vector3.down,
                            out RaycastHit hit, rayDistance, stickyLayer);

        if (onSticky)
        {
            // Track whatever Rigidbody is underfoot (null for static geometry)
            _platformRb = hit.collider.attachedRigidbody;

            if (!_isOnStickyLayer)
            {
                _isOnStickyLayer = true;
                pm.groundStick   = true;
                OnStickEnter?.Invoke();
                Log("Entered sticky layer.");
            }
        }
        else
        {
            _platformRb = null;

            if (_isOnStickyLayer)
            {
                _isOnStickyLayer = false;
                pm.groundStick   = false;
                OnStickExit?.Invoke();
                Log("Left sticky layer.");
            }
        }
    }

    private void FixedUpdate()
    {
        if (!_isOnStickyLayer) return;

        // Kill upward velocity
        if (rb.linearVelocity.y > 0f)
            rb.linearVelocity = new Vector3(rb.linearVelocity.x, 0f, rb.linearVelocity.z);

        if (_platformRb != null)
        {
            // Inherit platform Y velocity — prevents bounce on moving platforms
            float platformY = _platformRb.linearVelocity.y;
            rb.linearVelocity = new Vector3(rb.linearVelocity.x, platformY, rb.linearVelocity.z);
        }
        else if (!pm.grounded)
        {
            // Static geometry — pull down if walked off edge
            rb.AddForce(Vector3.down * groundPullForce, ForceMode.Acceleration);
        }
    }

    #endregion

    // ─────────────────────────────────────────────────────────────────────────
    #region Debug
    // ─────────────────────────────────────────────────────────────────────────

    [System.Diagnostics.Conditional("UNITY_EDITOR")]
    private void Log(string msg)
    {
        if (debugLog) Debug.Log($"[GroundStick] {msg}", this);
    }

    #endregion
}