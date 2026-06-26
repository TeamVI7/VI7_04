using System;
using UnityEngine;

/// <summary>
/// Wall-climbing and wall-slide ability.
/// 
/// EXTENDING:
///   - Subscribe to OnClimbStart / OnClimbEnd / OnWallSlideStart / OnWallSlideEnd for VFX, audio, etc.
///   - Override ClimbingMovement() logic by toggling climbing flag externally if needed.
/// 
/// DEBUG:
///   - Enable debugLog in Inspector for state transition logs.
/// </summary>
public class Climbing : MonoBehaviour
{
    // ─────────────────────────────────────────────────────────────────────────
    #region Inspector
    // ─────────────────────────────────────────────────────────────────────────

    [Header("References")]
    public Transform     orientation;
    public Rigidbody     rb;
    public PlayerMovement pm;
    public LayerMask     whatIsWall;

    [Header("Climbing")]
    public float climbSpeed      = 5f;
    public float wallSlideSpeed  = 2f;
    public float maxClimbTime    = 1f;
    public float minWallAngle    = 70f;

    [Header("Climb Jump")]
    public float   climbJumpUpForce   = 10f;
    public float   climbJumpBackForce = 8f;
    public KeyCode jumpKey            = KeyCode.Space;
    public int     climbJumps         = 1;

    [Header("Detection")]
    public float detectionLength       = 1f;
    public float sphereCastRadius      = 0.5f;
    public float maxWallLookAngle      = 30f;
    public float minWallNormalAngleChange = 5f;

    [Header("Exiting")]
    public float exitWallTime = 0.2f;

    [Header("Debug")]
    [SerializeField] private bool debugLog = false;

    #endregion

    // ─────────────────────────────────────────────────────────────────────────
    #region Events
    // ─────────────────────────────────────────────────────────────────────────

    public event Action OnClimbStart;
    public event Action OnClimbEnd;
    public event Action OnWallSlideStart;
    public event Action OnWallSlideEnd;

    /// <summary>Fired when a climb jump executes. Arg: force vector applied.</summary>
    public event Action<Vector3> OnClimbJump;

    #endregion

    // ─────────────────────────────────────────────────────────────────────────
    #region Public Read-only State
    // ─────────────────────────────────────────────────────────────────────────

    public bool IsClimbing    => climbing;
    public bool IsWallSliding => wallSliding;
    public bool IsExiting     => exitingWall;

    #endregion

    // ─────────────────────────────────────────────────────────────────────────
    #region Private
    // ─────────────────────────────────────────────────────────────────────────

    private float      climbTimer;
    private bool       wallSliding;
    private bool       climbing;
    private bool       exitingWall;
    private float      exitWallTimer;
    private int        climbJumpsLeft;

    private float      wallLookAngle;
    private RaycastHit frontWallHit;
    private bool       wallFront;
    private Transform  lastWall;
    private Vector3    lastWallNormal;

    #endregion

    // ─────────────────────────────────────────────────────────────────────────
    #region Unity Lifecycle
    // ─────────────────────────────────────────────────────────────────────────

    private void Update()
    {
        WallCheck();
        StateMachine();
    }

    private void FixedUpdate()
    {
        if (climbing && !exitingWall)
            ClimbingMovement();
        else if (wallSliding)
            WallSlideMovement();
    }

    #endregion

    // ─────────────────────────────────────────────────────────────────────────
    #region State Machine
    // ─────────────────────────────────────────────────────────────────────────

    private void StateMachine()
    {
        // ── Climbing ──────────────────────────────────────────────────────────
        if (wallFront && Input.GetKey(KeyCode.W) && wallLookAngle < maxWallLookAngle && !exitingWall)
        {
            if (!climbing && climbTimer > 0) StartClimbing();
            if (wallSliding) StopWallSlide();

            climbTimer -= Time.deltaTime;
            if (climbTimer <= 0f) StopClimbing();

            // FIX: jump previously only worked while wall-sliding, not while
            // actively climbing — you couldn't push off a wall mid-climb.
            if (Input.GetKeyDown(jumpKey) && climbJumpsLeft > 0) ClimbJump();
        }
        // ── Wall sliding ──────────────────────────────────────────────────────
        else if (wallFront && !pm.grounded && !exitingWall)
        {
            if (climbing) StopClimbing();
            if (!wallSliding) StartWallSlide();

            if (Input.GetKeyDown(jumpKey) && climbJumpsLeft > 0) ClimbJump();
        }
        // ── Exiting ───────────────────────────────────────────────────────────
        else if (exitingWall)
        {
            if (climbing)    StopClimbing();
            if (wallSliding) StopWallSlide();

            exitWallTimer -= Time.deltaTime;
            if (exitWallTimer <= 0f) exitingWall = false;
        }
        // ── None ──────────────────────────────────────────────────────────────
        else
        {
            if (climbing)    StopClimbing();
            if (wallSliding) StopWallSlide();

            // FIX: climb jump used to also be checked here unconditionally on
            // "wallFront" (touching ANY wall, climbing or not), which meant a
            // single GetKeyDown(jumpKey) frame during wall-sliding satisfied
            // BOTH this check and the one inside the wall-sliding branch above,
            // firing ClimbJump() twice (double impulse, double charge spend,
            // double event invoke). Climb jump now fires from exactly one
            // place per state — see the wall-sliding branch and the
            // "while clinging to wall but not yet sliding/climbing" case below.
        }

        // Allow a climb jump even if not currently climbing/sliding this frame
        // (e.g. you just touched a wall this frame) — but only once, and only
        // if neither branch above already consumed the input.
        if (wallFront && !climbing && !wallSliding && !exitingWall
            && Input.GetKeyDown(jumpKey) && climbJumpsLeft > 0)
        {
            ClimbJump();
        }
    }

    #endregion

    // ─────────────────────────────────────────────────────────────────────────
    #region Wall Detection
    // ─────────────────────────────────────────────────────────────────────────

    private void WallCheck()
    {
        bool hitWall = Physics.SphereCast(transform.position, sphereCastRadius,
                           orientation.forward, out frontWallHit, detectionLength, whatIsWall);
        wallFront = hitWall && IsWallNormal(frontWallHit.normal);

        wallLookAngle = wallFront
            ? Vector3.Angle(orientation.forward, -frontWallHit.normal)
            : 180f;

        bool newWall = wallFront
            && (frontWallHit.transform != lastWall
                || Mathf.Abs(Vector3.Angle(lastWallNormal, frontWallHit.normal)) > minWallNormalAngleChange);

        if ((wallFront && newWall) || pm.grounded)
        {
            climbTimer     = maxClimbTime;
            climbJumpsLeft = climbJumps;
        }
    }

    private bool IsWallNormal(Vector3 normal) =>
        Vector3.Angle(normal, Vector3.up) > minWallAngle;

    #endregion

    // ─────────────────────────────────────────────────────────────────────────
    #region Ability Actions
    // ─────────────────────────────────────────────────────────────────────────

    private void StartClimbing()
    {
        climbing       = true;
        pm.climbing    = true;
        pm.wallrunning = false;
        rb.useGravity  = false;   // FIX: gravity was fighting climbSpeed every FixedUpdate
        lastWall       = frontWallHit.transform;
        lastWallNormal = frontWallHit.normal;

        OnClimbStart?.Invoke();
        Log("Climb start.");

        // Hook: add camera FOV change, audio here or via OnClimbStart event
    }

    private void ClimbingMovement()
    {
        rb.linearVelocity = new Vector3(rb.linearVelocity.x, climbSpeed, rb.linearVelocity.z);

        // Hook: add climbing sound / particles via OnClimbStart event
    }

    private void StopClimbing()
    {
        climbing      = false;
        pm.climbing   = false;
        rb.useGravity = true;   // FIX: must restore — StartClimbing turns it off

        OnClimbEnd?.Invoke();
        Log("Climb end.");

        // Hook: add particle effect, audio via OnClimbEnd event
    }

    private void StartWallSlide()
    {
        wallSliding    = true;
        pm.wallSliding = true;

        OnWallSlideStart?.Invoke();
        Log("Wall slide start.");
    }

    private void WallSlideMovement()
    {
        rb.useGravity     = true;
        float slideY      = Mathf.Max(rb.linearVelocity.y, -wallSlideSpeed);
        rb.linearVelocity = new Vector3(rb.linearVelocity.x, slideY, rb.linearVelocity.z);
    }

    private void StopWallSlide()
    {
        wallSliding    = false;
        pm.wallSliding = false;

        OnWallSlideEnd?.Invoke();
        Log("Wall slide end.");
    }

    private void ClimbJump()
    {
        exitingWall   = true;
        exitWallTimer = exitWallTime;
        climbJumpsLeft--;
        rb.useGravity = true;   // FIX: climbing disables gravity — must restore before the impulse,
                                 // otherwise the jump arc is floaty until next frame's StopClimbing().

        Vector3 force = transform.up * climbJumpUpForce + frontWallHit.normal * climbJumpBackForce;
        rb.linearVelocity = new Vector3(rb.linearVelocity.x, 0f, rb.linearVelocity.z);
        rb.AddForce(force, ForceMode.Impulse);

        OnClimbJump?.Invoke(force);
        Log($"Climb jump. Jumps left: {climbJumpsLeft}");
    }

    #endregion

    // ─────────────────────────────────────────────────────────────────────────
    #region Debug
    // ─────────────────────────────────────────────────────────────────────────

    [System.Diagnostics.Conditional("UNITY_EDITOR")]
    private void Log(string msg)
    {
        if (debugLog) Debug.Log($"[Climbing] {msg}", this);
    }

    #endregion
}