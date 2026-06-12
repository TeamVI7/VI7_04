using System;
using UnityEngine;

/// <summary>
/// Directional dash ability with cooldown and FOV effect.
/// 
/// EXTENDING:
///   - Subscribe to OnDashStart / OnDashEnd for VFX, trail renderers, sound, etc.
///   - Add afterimage / shadow clone effect in OnDashStart.
///   - Expose charges (multi-dash) by adding a charge counter and modifying CanDash.
/// 
/// DEBUG:
///   - Enable debugLog in Inspector.
///   - dashCdTimer is displayed in the Inspector automatically (it's private but you can watch it).
/// </summary>
public class Dashing : MonoBehaviour
{
    // ─────────────────────────────────────────────────────────────────────────
    #region Inspector
    // ─────────────────────────────────────────────────────────────────────────

    [Header("References")]
    public Transform   orientation;
    public Transform   playerCam;
    public PlayerCam   cam;

    [Header("Dash Force")]
    public float dashForce       = 20f;
    public float dashUpwardForce = 0f;
    public float maxDashYSpeed   = 0f;
    public float dashDuration    = 0.25f;

    [Header("Camera")]
    public float dashFov     = 95f;
    public float normalFov   = 85f;

    [Header("Settings")]
    public bool useCameraForward  = true;
    public bool allowAllDirections = true;
    public bool disableGravity    = false;
    public bool resetVel          = true;

    [Header("Cooldown")]
    public float dashCooldown = 1f;

    [Header("Input")]
    public KeyCode dashKey = KeyCode.E;

    [Header("Debug")]
    [SerializeField] private bool debugLog = false;

    #endregion

    // ─────────────────────────────────────────────────────────────────────────
    #region Events
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>Fired when dash starts. Arg: world-space direction.</summary>
    public event Action<Vector3> OnDashStart;

    /// <summary>Fired when dash ends (duration elapsed).</summary>
    public event Action OnDashEnd;

    #endregion

    // ─────────────────────────────────────────────────────────────────────────
    #region Public Read-only State
    // ─────────────────────────────────────────────────────────────────────────

    public bool  IsDashing         => pm != null && pm.dashing;
    public float CooldownRemaining => Mathf.Max(0f, _dashCdTimer);
    public bool  CanDash           => _dashCdTimer <= 0f;

    #endregion

    // ─────────────────────────────────────────────────────────────────────────
    #region Private
    // ─────────────────────────────────────────────────────────────────────────

    private Rigidbody     rb;
    private PlayerMovement pm;
    private float         _dashCdTimer;
    private Vector3       _delayedForce;

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
        if (Input.GetKeyDown(dashKey)) TryDash();

        if (_dashCdTimer > 0f)
            _dashCdTimer -= Time.deltaTime;
    }

    #endregion

    // ─────────────────────────────────────────────────────────────────────────
    #region Dash Logic
    // ─────────────────────────────────────────────────────────────────────────

    private void TryDash()
    {
        if (!CanDash) { Log("Dash blocked — on cooldown."); return; }
        Dash();
    }

    private void Dash()
    {
        _dashCdTimer  = dashCooldown;
        pm.dashing    = true;
        pm.maxYSpeed  = maxDashYSpeed;

        cam.DoFov(dashFov);

        Transform forwardT = useCameraForward ? playerCam : orientation;
        Vector3 direction  = GetDirection(forwardT);
        _delayedForce      = direction * dashForce + orientation.up * dashUpwardForce;

        if (disableGravity) rb.useGravity = false;

        OnDashStart?.Invoke(direction);
        Log($"Dash start → {direction}");

        Invoke(nameof(ApplyDashForce), 0.025f);
        Invoke(nameof(EndDash), dashDuration);
    }

    private void ApplyDashForce()
    {
        if (resetVel) rb.linearVelocity = Vector3.zero;
        rb.AddForce(_delayedForce, ForceMode.Impulse);
    }

    private void EndDash()
    {
        pm.dashing   = false;
        pm.maxYSpeed = 0f;

        cam.DoFov(normalFov);

        if (disableGravity) rb.useGravity = true;

        OnDashEnd?.Invoke();
        Log("Dash end.");
    }

    private Vector3 GetDirection(Transform forwardT)
    {
        float h = Input.GetAxisRaw("Horizontal");
        float v = Input.GetAxisRaw("Vertical");

        Vector3 dir = allowAllDirections
            ? forwardT.forward * v + forwardT.right * h
            : forwardT.forward;

        if (h == 0f && v == 0f) dir = forwardT.forward;

        return dir.normalized;
    }

    #endregion

    // ─────────────────────────────────────────────────────────────────────────
    #region Debug
    // ─────────────────────────────────────────────────────────────────────────

    [System.Diagnostics.Conditional("UNITY_EDITOR")]
    private void Log(string msg)
    {
        if (debugLog) Debug.Log($"[Dashing] {msg}", this);
    }

    #endregion
}