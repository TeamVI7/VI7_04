using System;
using UnityEngine;

/// <summary>
/// Directional dash ability with cooldown, multi-charge support, and FOV effect.
///
/// CHARGE MODEL:
///   maxCharges = 1 behaves exactly like the original single-dash-on-cooldown design.
///   maxCharges > 1 gives a charge bank — each dash consumes one charge, and charges
///   regenerate individually every dashCooldown seconds (like a rechargeable ability,
///   not a single shared cooldown). ChargesAvailable / MaxCharges are read-only and
///   safe to poll from any HUD or debug overlay.
///
/// EXTENDING:
///   - Subscribe to OnDashStart / OnDashEnd for VFX, trail renderers, sound, etc.
///   - Add afterimage / shadow clone effect in OnDashStart.
///   - Subscribe to OnChargesChanged to drive charge-pip UI.
///
/// DEBUG:
///   - Enable debugLog in Inspector.
///   - ChargesAvailable / CooldownRemaining are public — read them from PlayerStateDisplay.
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
    // 'normal' FOV now lives on PlayerCam.baseFov — see PlayerCam.FovLayer. Dash claims
    // the Dash layer and releases it, instead of asserting its own idea of normal.

    [Header("Settings")]
    public bool useCameraForward  = true;
    public bool allowAllDirections = true;
    public bool disableGravity    = false;
    public bool resetVel          = true;

    [Header("Cooldown / Charges")]
    [Tooltip("Seconds for a single spent charge to regenerate.")]
    public float dashCooldown = 1f;
    [Tooltip("1 = classic single-dash-on-cooldown. >1 = charge bank (e.g. double/triple dash).")]
    [Min(1)] public int maxCharges = 1;

    [Header("Stamina")]
    [Tooltip("Stamina spent per dash. 0 = free (charges are the only limit).")]
    public float dashStaminaCost = 25f;

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

    /// <summary>Fired whenever the charge count changes. Args: (current, max).</summary>
    public event Action<int, int> OnChargesChanged;

    #endregion

    // ─────────────────────────────────────────────────────────────────────────
    #region Public Read-only State
    // ─────────────────────────────────────────────────────────────────────────

    public bool  IsDashing         => pm != null && pm.dashing;

    /// <summary>Seconds remaining until the NEXT charge regenerates (0 if already full).</summary>
    public float CooldownRemaining => _charges >= maxCharges ? 0f : Mathf.Max(0f, dashCooldown - _regenTimer);

    /// <summary>True if a dash can be performed right now — charge available AND stamina affordable.</summary>
    public bool  CanDash           => _charges > 0 && HasStamina;

    /// <summary>True if the player can currently afford the dash's stamina cost.</summary>
    public bool  HasStamina        => dashStaminaCost <= 0f
                                   || (pm != null && pm.HasStamina(dashStaminaCost));

    /// <summary>Charges currently available to spend.</summary>
    public int   ChargesAvailable  => _charges;

    /// <summary>Total charge capacity (mirrors maxCharges — exposed for HUD binding).</summary>
    public int   MaxCharges        => maxCharges;

    #endregion

    // ─────────────────────────────────────────────────────────────────────────
    #region Private
    // ─────────────────────────────────────────────────────────────────────────

    private Rigidbody     rb;
    private PlayerMovement pm;
    private int            _charges;     // current available charges
    private float          _regenTimer;  // counts up toward dashCooldown while charges < max
    private Vector3        _delayedForce;

    #endregion

    // ─────────────────────────────────────────────────────────────────────────
    #region Unity Lifecycle
    // ─────────────────────────────────────────────────────────────────────────

    private void Start()
    {
        rb = GetComponent<Rigidbody>();
        pm = GetComponent<PlayerMovement>();
        _charges = maxCharges;
    }

    private void Update()
    {
        if (Input.GetKeyDown(dashKey) && !PlayerActionLock.InputBlocked) TryDash();

        TickChargeRegen();
    }

    /// <summary>
    /// Safety net: pending Invokes die with the component, so a disable mid-dash would
    /// leave pm.dashing stuck true — MovePlayer early-returns on that flag forever.
    /// </summary>
    private void OnDisable()
    {
        CancelInvoke(nameof(ApplyDashForce));
        CancelInvoke(nameof(EndDash));
        if (pm != null && pm.dashing) EndDash();
    }

    #endregion

    // ─────────────────────────────────────────────────────────────────────────
    #region Charge Regen
    // ─────────────────────────────────────────────────────────────────────────

    private void TickChargeRegen()
    {
        if (_charges >= maxCharges) { _regenTimer = 0f; return; }

        _regenTimer += Time.deltaTime;
        if (_regenTimer >= dashCooldown)
        {
            _regenTimer = 0f;
            _charges    = Mathf.Min(_charges + 1, maxCharges);
            OnChargesChanged?.Invoke(_charges, maxCharges);
            Log($"Charge regenerated. {_charges}/{maxCharges}");
        }
    }

    #endregion

    // ─────────────────────────────────────────────────────────────────────────
    #region Dash Logic
    // ─────────────────────────────────────────────────────────────────────────

    private void TryDash()
    {
        if (_charges <= 0) { Log("Dash blocked — no charges."); return; }

        // Spend stamina only once the charge check has passed, so a dash on cooldown
        // never drains the bar. TryConsumeStamina is atomic — it spends nothing on failure.
        if (!pm.TryConsumeStamina(dashStaminaCost))
        {
            Log($"Dash blocked — needs {dashStaminaCost} stamina, has {pm.Stamina:0.#}.");
            return;
        }

        Dash();
    }

    private void Dash()
    {
        _charges--;
        OnChargesChanged?.Invoke(_charges, maxCharges);

        pm.dashing    = true;
        pm.maxYSpeed  = maxDashYSpeed;

        if (cam != null) cam.SetFov(PlayerCam.FovLayer.Dash, dashFov);

        Transform forwardT = useCameraForward ? playerCam : orientation;
        Vector3 direction  = GetDirection(forwardT);
        _delayedForce      = direction * dashForce + orientation.up * dashUpwardForce;

        if (disableGravity) rb.useGravity = false;

        OnDashStart?.Invoke(direction);
        Log($"Dash start → {direction}");

        // Chaining a second charge before the first dash expires would otherwise let the
        // older EndDash timer cut the new dash short. Re-arm both timers from scratch.
        CancelInvoke(nameof(ApplyDashForce));
        CancelInvoke(nameof(EndDash));
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

        if (cam != null) cam.ClearFov(PlayerCam.FovLayer.Dash); // reachable from OnDisable during teardown

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