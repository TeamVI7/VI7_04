using System;
using UnityEngine;

/// <summary>
/// Wall-running and wall-sliding ability.
/// 
/// EXTENDING:
///   - Subscribe to events for camera tilt audio, VFX, particle trails, etc.
///   - Modify WallRunningMovement() to add momentum or wall-kick variations.
///   - WallNormal is public — use it from WallHandIK or other systems.
/// 
/// DEBUG:
///   - Enable debugLog in Inspector.
///   - wallRunTimer is shown in Inspector (add [SerializeField] if needed).
/// </summary>
public class WallRunning : MonoBehaviour
{
    // ─────────────────────────────────────────────────────────────────────────
    #region Inspector
    // ─────────────────────────────────────────────────────────────────────────

    [Header("Wall Run Settings")]
    public LayerMask whatIsWall;
    public LayerMask whatIsGround;
    public float wallRunForce       = 12f;
    public float wallJumpUpForce    = 7f;
    public float wallJumpSideForce  = 12f;
    public float wallClimbSpeed     = 3f;
    public float wallSlideSpeed     = 2f;
    public float maxWallRunTime     = 1f;
    public float minWallAngle       = 60f;

    [Header("Input")]
    public KeyCode jumpKey          = KeyCode.Space;
    public KeyCode upwardsRunKey    = KeyCode.LeftShift;
    public KeyCode downwardsRunKey  = KeyCode.LeftControl;

    [Header("Detection")]
    public float wallCheckDistance  = 0.7f;
    public float minJumpHeight      = 1.5f;

    [Header("Exiting")]
    public float exitWallTime       = 0.2f;

    [Header("Gravity")]
    public bool  useGravity         = false;
    public float gravityCounterForce = 0f;

    [Header("Camera FOV")]
    public float wallRunFov  = 90f;
    public float normalFov   = 80f;
    public float wallTiltAngle = 5f;

    [Header("References")]
    public Transform          orientation;
    public PlayerCam          cam;
    public WeaponsController  activeWeapon;

    [Header("Debug")]
    [SerializeField] private bool debugLog = false;

    #endregion

    // ─────────────────────────────────────────────────────────────────────────
    #region Animation Hashes
    // ─────────────────────────────────────────────────────────────────────────

    private static readonly int AnimGrapple = Animator.StringToHash("Grapple");
    private static readonly int AnimStopGrapple = Animator.StringToHash("StopGrapple");

    #endregion

    // ─────────────────────────────────────────────────────────────────────────
    #region Events
    // ─────────────────────────────────────────────────────────────────────────

    public event Action OnWallRunStart;
    public event Action OnWallRunEnd;
    public event Action OnWallSlideStart;
    public event Action OnWallSlideEnd;

    /// <summary>Fired when a wall jump executes. Arg: force applied.</summary>
    public event Action<Vector3> OnWallJump;

    #endregion

    // ─────────────────────────────────────────────────────────────────────────
    #region Public Read-only State
    // ─────────────────────────────────────────────────────────────────────────

    public bool     IsWallRunning  => pm != null && pm.wallrunning;
    public bool     IsWallSliding  => wallSliding;
    public Vector3  WallNormal     => wallRight ? rightWallhit.normal : leftWallhit.normal;
    public bool     WallOnLeft     => wallLeft;
    public bool     WallOnRight    => wallRight;

    #endregion

    // ─────────────────────────────────────────────────────────────────────────
    #region Private
    // ─────────────────────────────────────────────────────────────────────────

    private PlayerMovement pm;
    private Rigidbody      rb;

    private bool       wallSliding;
    private bool       exitingWall;
    private float      wallRunTimer;
    private float      exitWallTimer;

    private float      horizontalInput;
    private float      verticalInput;
    private bool       upwardsRunning;
    private bool       downwardsRunning;

    private RaycastHit leftWallhit;
    private RaycastHit rightWallhit;
    private bool       wallLeft;
    private bool       wallRight;

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
        CheckForWall();
        StateMachine();
    }

    private void FixedUpdate()
    {
        if (pm.wallrunning) WallRunningMovement();
        else if (wallSliding) WallSlidingMovement();
    }

    #endregion

    // ─────────────────────────────────────────────────────────────────────────
    #region Wall Detection
    // ─────────────────────────────────────────────────────────────────────────

    private void CheckForWall()
    {
        wallRight = Physics.Raycast(transform.position,  orientation.right, out rightWallhit, wallCheckDistance, whatIsWall)
                    && IsWallNormal(rightWallhit.normal);
        wallLeft  = Physics.Raycast(transform.position, -orientation.right, out leftWallhit,  wallCheckDistance, whatIsWall)
                    && IsWallNormal(leftWallhit.normal);
    }

    private bool IsWallNormal(Vector3 normal) =>
        Vector3.Angle(normal, Vector3.up) > minWallAngle;

    private bool AboveGround() =>
        !Physics.Raycast(transform.position, Vector3.down, minJumpHeight, whatIsGround);

    #endregion

    // ─────────────────────────────────────────────────────────────────────────
    #region State Machine
    // ─────────────────────────────────────────────────────────────────────────

    private void StateMachine()
    {
        horizontalInput  = Input.GetAxisRaw("Horizontal");
        verticalInput    = Input.GetAxisRaw("Vertical");
        upwardsRunning   = Input.GetKey(upwardsRunKey);
        downwardsRunning = Input.GetKey(downwardsRunKey);

        // ── Wall running ──────────────────────────────────────────────────────
        if ((wallLeft || wallRight) && verticalInput > 0 && AboveGround() && !exitingWall)
        {
            if (!pm.wallrunning) StartWallRun();
            if (wallSliding)     StopWallSlide();

            wallRunTimer -= Time.deltaTime;
            if (wallRunTimer <= 0f && pm.wallrunning)
            {
                exitingWall   = true;
                exitWallTimer = exitWallTime;
            }

            if (Input.GetKeyDown(jumpKey)) WallJump();
        }
        // ── Wall sliding ──────────────────────────────────────────────────────
        else if ((wallLeft || wallRight) && !pm.grounded && !exitingWall)
        {
            if (pm.wallrunning) StopWallRun();
            if (!wallSliding)   StartWallSlide();

            if (Input.GetKeyDown(jumpKey)) WallJump();
        }
        // ── Exiting ───────────────────────────────────────────────────────────
        else if (exitingWall)
        {
            if (pm.wallrunning) StopWallRun();
            if (wallSliding)    StopWallSlide();

            exitWallTimer -= Time.deltaTime;
            if (exitWallTimer <= 0f) exitingWall = false;
        }
        // ── None ──────────────────────────────────────────────────────────────
        else
        {
            if (pm.wallrunning) StopWallRun();
            if (wallSliding)    StopWallSlide();
        }
    }

    #endregion

    // ─────────────────────────────────────────────────────────────────────────
    #region Ability Actions
    // ─────────────────────────────────────────────────────────────────────────

    private void StartWallRun()
    {
        cam.disableMoveTilt = true;
        pm.wallrunning      = true;
        pm.climbing         = false;
        wallRunTimer        = maxWallRunTime;
        rb.linearVelocity   = new Vector3(rb.linearVelocity.x, 0f, rb.linearVelocity.z);

        if (activeWeapon != null)
        {
            if (activeWeapon.IsReloading)  activeWeapon.CancelReload();
            if (activeWeapon.IsInspecting) activeWeapon.CancelInspect();

            if (activeWeapon.gunAnimator != null)
            {
                activeWeapon.gunAnimator.ResetTrigger(AnimStopGrapple);
                activeWeapon.gunAnimator.SetTrigger(AnimGrapple);
            }
        }

        cam.DoFov(wallRunFov);
        cam.DoTilt(wallLeft ? -wallTiltAngle : wallTiltAngle);

        OnWallRunStart?.Invoke();
        Log($"Wall run start — {'L' + (wallLeft ? "" : "R")} wall.");
    }

    private void WallRunningMovement()
    {
        rb.useGravity = useGravity;

        Vector3 wallNormal  = WallNormal;
        Vector3 wallForward = Vector3.Cross(wallNormal, transform.up);
        if ((orientation.forward - wallForward).magnitude > (orientation.forward + wallForward).magnitude)
            wallForward = -wallForward;

        float currentY = rb.linearVelocity.y;
        if (upwardsRunning)       currentY = wallClimbSpeed;
        else if (downwardsRunning) currentY = -wallClimbSpeed;

        rb.linearVelocity = new Vector3(wallForward.normalized.x * wallRunForce,
                                        currentY,
                                        wallForward.normalized.z * wallRunForce);

        // Keep player pressed against wall
        if (!(wallLeft && horizontalInput > 0) && !(wallRight && horizontalInput < 0))
            rb.AddForce(-wallNormal * 30f, ForceMode.Acceleration);

        if (useGravity)
            rb.AddForce(transform.up * gravityCounterForce, ForceMode.Acceleration);
    }

    private void StopWallRun()
    {
        cam.disableMoveTilt = false;
        pm.wallrunning      = false;

        cam.DoFov(normalFov);
        cam.DoTilt(0f);

        OnWallRunEnd?.Invoke();
        Log("Wall run end.");
    }

    private void StartWallSlide()
    {
        wallSliding    = true;
        pm.wallSliding = true;

        OnWallSlideStart?.Invoke();
        Log("Wall slide start.");
    }

    private void WallSlidingMovement()
    {
        rb.useGravity     = true;
        Vector3 wallNormal = WallNormal;
        float slideY       = Mathf.Max(rb.linearVelocity.y, -wallSlideSpeed);
        rb.linearVelocity  = new Vector3(rb.linearVelocity.x, slideY, rb.linearVelocity.z);

        if (!(wallLeft && horizontalInput > 0) && !(wallRight && horizontalInput < 0))
            rb.AddForce(-wallNormal * 15f, ForceMode.Acceleration);
    }

    private void StopWallSlide()
    {
        wallSliding    = false;
        pm.wallSliding = false;

        OnWallSlideEnd?.Invoke();
        Log("Wall slide end.");
    }

    private void WallJump()
    {
        exitingWall   = true;
        exitWallTimer = exitWallTime;

        Vector3 wallNormal = WallNormal;
        Vector3 force      = transform.up * wallJumpUpForce + wallNormal * wallJumpSideForce;

        rb.linearVelocity = new Vector3(rb.linearVelocity.x, 0f, rb.linearVelocity.z);
        rb.AddForce(force, ForceMode.Impulse);

        OnWallJump?.Invoke(force);
        Log("Wall jump.");
    }

    #endregion

    // ─────────────────────────────────────────────────────────────────────────
    #region Debug
    // ─────────────────────────────────────────────────────────────────────────

    [System.Diagnostics.Conditional("UNITY_EDITOR")]
    private void Log(string msg)
    {
        if (debugLog) Debug.Log($"[WallRunning] {msg}", this);
    }

    #endregion
}