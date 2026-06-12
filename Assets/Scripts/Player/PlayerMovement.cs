using System;
using System.Collections;
using UnityEngine;

/// <summary>
/// Core player movement controller.
/// 
/// EXTENDING:
///   - Subscribe to events (OnLanded, OnJumped, OnStateChanged, etc.) from ability scripts
///   - Add new MovementState values and a corresponding else-if block in StateHandler()
///   - New ability flags go in the "State flags" region — keep them [HideInInspector]
/// 
/// DEBUG:
///   - Set debugLog = true in Inspector to see state transitions
///   - PlayerStateDisplay component gives an on-screen overlay
/// </summary>
public class PlayerMovement : MonoBehaviour
{
    // ─────────────────────────────────────────────────────────────────────────
    #region Inspector
    // ─────────────────────────────────────────────────────────────────────────

    [Header("Movement")]
    public float walkSpeed              = 7f;
    public float crouchSpeed            = 3.5f;
    public float slideSpeed             = 14f;
    public float wallrunSpeed           = 12f;
    public float climbSpeed             = 3f;
    public float swingSpeed             = 12f;
    public float dashSpeed              = 20f;
    public float dashSpeedChangeFactor  = 5f;
    public float airMultiplier          = 0.4f;
    public float groundDrag             = 5f;

    [Header("Speed Smoothing")]
    public float speedIncreaseMultiplier = 1.5f;
    public float slopeIncreaseMultiplier = 2.5f;

    [Header("Jumping")]
    public float jumpForce    = 12f;
    public float jumpCooldown = 0.25f;

    [Header("Crouching")]
    public float crouchYScale = 0.5f;

    [Header("Ground Check")]
    public float     playerHeight = 2f;
    public LayerMask whatIsGround;

    [Header("Vaulting")]
    public LayerMask whatIsWall;
    public float     vaultCheckDistance = 1.2f;
    public float     vaultCheckHeight   = 0.9f;
    public float     vaultHeight        = 1.3f;
    public float     vaultDuration      = 0.24f;
    public float     vaultClearRadius   = 0.5f;
    public float     vaultClearHeight   = 1.1f;

    [Header("Slope Handling")]
    public float maxSlopeAngle = 40f;

    [Header("Grapple FOV")]
    public PlayerCam cam;
    public float grappleFov = 95f;

    [Header("Keybinds")]
    public KeyCode jumpKey   = KeyCode.Space;
    public KeyCode crouchKey = KeyCode.LeftControl;

    [Header("References")]
    public Transform orientation;

    [Header("Debug")]
    [SerializeField] private bool debugLog = false;

    #endregion

    // ─────────────────────────────────────────────────────────────────────────
    #region Events
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>Fired every time MovementState changes. Args: (previous, next)</summary>
    public event Action<MovementState, MovementState> OnStateChanged;

    /// <summary>Fired on the frame the player leaves the ground.</summary>
    public event Action OnJumped;

    /// <summary>Fired on the frame the player first touches the ground after being airborne.</summary>
    public event Action OnLanded;

    /// <summary>Fired when vaulting starts.</summary>
    public event Action OnVaultStart;

    /// <summary>Fired when vaulting ends.</summary>
    public event Action OnVaultEnd;

    #endregion

    // ─────────────────────────────────────────────────────────────────────────
    #region State Flags  (set by ability scripts)
    // ─────────────────────────────────────────────────────────────────────────

    [HideInInspector] public bool  sliding;
    [HideInInspector] public bool  wallrunning;
    [HideInInspector] public bool  climbing;
    [HideInInspector] public bool  wallSliding;
    [HideInInspector] public bool  vaulting;
    [HideInInspector] public bool  dashing;
    [HideInInspector] public bool  swinging;
    [HideInInspector] public bool  activeGrapple;
    [HideInInspector] public bool  freeze;
    [HideInInspector] public float maxYSpeed;
    [HideInInspector] public bool  grounded;

    #endregion

    // ─────────────────────────────────────────────────────────────────────────
    #region State Machine
    // ─────────────────────────────────────────────────────────────────────────

    public MovementState state { get; private set; }

    public enum MovementState
    {
        freeze,
        dashing,
        grappling,
        swinging,
        vaulting,
        wallrunning,
        climbing,
        wallSliding,
        sliding,
        crouching,
        walking,
        standing,
        air
    }

    private void SetState(MovementState next)
    {
        if (state == next) return;
        var prev = state;
        state = next;
        OnStateChanged?.Invoke(prev, next);
        Log($"State: {prev} → {next}");
    }

    #endregion

    // ─────────────────────────────────────────────────────────────────────────
    #region Private
    // ─────────────────────────────────────────────────────────────────────────

    private Rigidbody       rb;
    private CapsuleCollider col;

    private float moveSpeed;
    private float desiredMoveSpeed;
    private float lastDesiredMoveSpeed;
    private float speedChangeFactor;
    private bool  keepMomentum;
    private MovementState lastState;

    private float   horizontalInput;
    private float   verticalInput;
    private Vector3 moveDirection;

    private Vector3 vaultStartPos;
    private Vector3 vaultEndPos;
    private float   vaultTimer;

    private RaycastHit slopeHit;
    private RaycastHit groundHit;
    private bool       exitingSlope;
    private bool       readyToJump = true;

    private float   startYScale;
    private float   startColHeight;
    private Vector3 startColCenter;

    // grapple arc helpers
    private Vector3 velocityToSet;
    private bool    enableMovementOnNextTouch;

    // landing detection
    private bool _wasGrounded;

    #endregion

    // ─────────────────────────────────────────────────────────────────────────
    #region Unity Lifecycle
    // ─────────────────────────────────────────────────────────────────────────

    private void Start()
    {
        col   = GetComponent<CapsuleCollider>();
        rb    = GetComponent<Rigidbody>();
        rb.freezeRotation = true;

        startYScale    = transform.localScale.y;
        startColHeight = col.height;
        startColCenter = col.center;
    }

    private void Update()
    {
        // Ground check
        grounded = Physics.Raycast(transform.position, Vector3.down,
                       out groundHit, playerHeight * 0.5f + 0.2f, whatIsGround)
                && Vector3.Angle(groundHit.normal, Vector3.up) <= maxSlopeAngle;

        // Landing event
        if (grounded && !_wasGrounded) OnLanded?.Invoke();
        _wasGrounded = grounded;

        ReadInput();
        TryVault();
        StateHandler();
        SpeedControl();

        // Drag
        if (!activeGrapple)
        {
            bool onGround = state == MovementState.walking || state == MovementState.crouching;
            rb.linearDamping = onGround ? groundDrag : 0f;
        }
        else
        {
            rb.linearDamping = 0f;
        }
    }

    private void FixedUpdate()
    {
        if (vaulting)
            VaultMovement();
        else
            MovePlayer();
    }

    #endregion

    // ─────────────────────────────────────────────────────────────────────────
    #region Input
    // ─────────────────────────────────────────────────────────────────────────

    private void ReadInput()
    {
        horizontalInput = Input.GetAxisRaw("Horizontal");
        verticalInput   = Input.GetAxisRaw("Vertical");

        if (Input.GetKey(jumpKey) && readyToJump && grounded)
        {
            readyToJump = false;
            Jump();
            Invoke(nameof(ResetJump), jumpCooldown);
        }

        if (Input.GetKeyDown(crouchKey))
        {
            col.height = col.height * crouchYScale;
            col.center = new Vector3(col.center.x, col.center.y * crouchYScale, col.center.z);
            rb.AddForce(Vector3.down * 5f, ForceMode.Impulse);
        }

        if (Input.GetKeyUp(crouchKey))
        {
            col.height = startColHeight;
            col.center = startColCenter;
        }
    }

    #endregion

    // ─────────────────────────────────────────────────────────────────────────
    #region State Handler
    // ─────────────────────────────────────────────────────────────────────────

    private void StateHandler()
    {
        // Priority order — highest to lowest.
        // EXTEND: Add new ability states as else-if blocks before the walking/air block.

        if (freeze)
        {
            SetState(MovementState.freeze);
            rb.linearVelocity = Vector3.zero;
            desiredMoveSpeed  = 0f;
        }
        else if (dashing)
        {
            SetState(MovementState.dashing);
            desiredMoveSpeed  = dashSpeed;
            speedChangeFactor = dashSpeedChangeFactor;
        }
        else if (activeGrapple)
        {
            SetState(MovementState.grappling);
            desiredMoveSpeed = walkSpeed;
        }
        else if (swinging)
        {
            SetState(MovementState.swinging);
            desiredMoveSpeed = swingSpeed;
        }
        else if (vaulting)
        {
            SetState(MovementState.vaulting);
            desiredMoveSpeed = walkSpeed;
        }
        else if (wallrunning)
        {
            SetState(MovementState.wallrunning);
            desiredMoveSpeed = wallrunSpeed;
        }
        else if (climbing)
        {
            SetState(MovementState.climbing);
            desiredMoveSpeed = climbSpeed;
        }
        else if (wallSliding)
        {
            SetState(MovementState.wallSliding);
            desiredMoveSpeed = slideSpeed;
        }
        else if (sliding)
        {
            SetState(MovementState.sliding);
            desiredMoveSpeed = slideSpeed;
        }
        else if (Input.GetKey(crouchKey))
        {
            SetState(MovementState.crouching);
            desiredMoveSpeed = crouchSpeed;
        }
        else if (grounded && horizontalInput == 0f && verticalInput == 0f)
        {
            SetState(MovementState.standing);
            desiredMoveSpeed = 0f;
        }
        else if (grounded)
        {
            SetState(MovementState.walking);
            desiredMoveSpeed = walkSpeed;
        }
        else
        {
            SetState(MovementState.air);
            desiredMoveSpeed = walkSpeed;
        }

        bool speedChanged = desiredMoveSpeed != lastDesiredMoveSpeed;
        if (lastState == MovementState.dashing) keepMomentum = true;

        if (speedChanged)
        {
            StopAllCoroutines();
            if (keepMomentum)
                StartCoroutine(SmoothlyLerpMoveSpeed());
            else
                moveSpeed = desiredMoveSpeed;
        }

        lastDesiredMoveSpeed = desiredMoveSpeed;
        lastState            = state;
        if (keepMomentum && Mathf.Abs(moveSpeed - desiredMoveSpeed) < 0.1f)
            keepMomentum = false;
    }

    private IEnumerator SmoothlyLerpMoveSpeed()
    {
        float time       = 0f;
        float difference = Mathf.Abs(desiredMoveSpeed - moveSpeed);
        float startValue = moveSpeed;

        while (time < difference)
        {
            moveSpeed = Mathf.Lerp(startValue, desiredMoveSpeed, time / difference);

            if (OnSlope())
            {
                float angle         = Vector3.Angle(Vector3.up, slopeHit.normal);
                float slopeIncrease = 1f + angle / 90f;
                time += Time.deltaTime * speedIncreaseMultiplier * slopeIncreaseMultiplier * slopeIncrease;
            }
            else
            {
                time += Time.deltaTime * speedChangeFactor;
            }

            yield return null;
        }

        moveSpeed         = desiredMoveSpeed;
        speedChangeFactor = 1f;
        keepMomentum      = false;
    }

    #endregion

    // ─────────────────────────────────────────────────────────────────────────
    #region Movement
    // ─────────────────────────────────────────────────────────────────────────

    private void MovePlayer()
    {
        if (freeze || activeGrapple || swinging || dashing || vaulting ||
            wallrunning || climbing || wallSliding)
            return;

        moveDirection = orientation.forward * verticalInput
                      + orientation.right   * horizontalInput;

        if (OnSlope() && !exitingSlope)
        {
            rb.AddForce(GetSlopeMoveDirection(moveDirection) * moveSpeed * 20f, ForceMode.Force);
            if (rb.linearVelocity.y > 0)
                rb.AddForce(Vector3.down * 80f, ForceMode.Force);
        }
        else if (grounded)
        {
            rb.AddForce(moveDirection.normalized * moveSpeed * 10f, ForceMode.Force);

            if (horizontalInput == 0 && verticalInput == 0)
            {
                Vector3 flatVel = new Vector3(rb.linearVelocity.x, 0f, rb.linearVelocity.z);
                rb.AddForce(-flatVel * 15f, ForceMode.Force);
            }
        }
        else
        {
            rb.AddForce(moveDirection.normalized * moveSpeed * 10f * airMultiplier, ForceMode.Force);
        }

        rb.useGravity = !OnSlope();
    }

    private void SpeedControl()
    {
        if (activeGrapple) return;

        if (OnSlope() && !exitingSlope)
        {
            if (rb.linearVelocity.magnitude > moveSpeed)
                rb.linearVelocity = rb.linearVelocity.normalized * moveSpeed;
        }
        else
        {
            Vector3 flatVel = new Vector3(rb.linearVelocity.x, 0f, rb.linearVelocity.z);
            if (flatVel.magnitude > moveSpeed)
            {
                Vector3 capped = flatVel.normalized * moveSpeed;
                rb.linearVelocity = new Vector3(capped.x, rb.linearVelocity.y, capped.z);
            }
        }

        if (maxYSpeed != 0 && rb.linearVelocity.y > maxYSpeed)
            rb.linearVelocity = new Vector3(rb.linearVelocity.x, maxYSpeed, rb.linearVelocity.z);
    }

    #endregion

    // ─────────────────────────────────────────────────────────────────────────
    #region Jump
    // ─────────────────────────────────────────────────────────────────────────

    private void Jump()
    {
        exitingSlope = true;
        rb.linearVelocity = new Vector3(rb.linearVelocity.x, 0f, rb.linearVelocity.z);
        rb.AddForce(transform.up * jumpForce, ForceMode.Impulse);
        OnJumped?.Invoke();
        Log("Jumped.");
    }

    private void ResetJump()
    {
        readyToJump  = true;
        exitingSlope = false;
    }

    #endregion

    // ─────────────────────────────────────────────────────────────────────────
    #region Vault
    // ─────────────────────────────────────────────────────────────────────────

    private void TryVault()
    {
        if (vaulting || !grounded || activeGrapple || sliding || wallrunning || climbing || dashing || freeze)
            return;

        if (verticalInput <= 0f) return;

        LayerMask obstacleMask = whatIsWall.value == 0 ? whatIsGround : whatIsWall;
        Vector3 origin = transform.position + Vector3.up * vaultCheckHeight;
        if (!Physics.Raycast(origin, orientation.forward, out RaycastHit hit, vaultCheckDistance, obstacleMask))
            return;

        Vector3 clearanceOrigin = hit.point + Vector3.up * vaultClearHeight + orientation.forward * 0.2f;
        if (Physics.CheckSphere(clearanceOrigin, vaultClearRadius, obstacleMask))
            return;

        StartVault(hit.point);
    }

    private void StartVault(Vector3 hitPoint)
    {
        vaulting      = true;
        vaultTimer    = 0f;
        vaultStartPos = transform.position;
        vaultEndPos   = hitPoint + orientation.forward * (vaultCheckDistance * 0.75f) + Vector3.up * vaultHeight;
        rb.linearVelocity = Vector3.zero;
        rb.useGravity     = false;
        OnVaultStart?.Invoke();
        Log("Vault start.");
    }

    private void VaultMovement()
    {
        vaultTimer += Time.deltaTime;
        float t = Mathf.Clamp01(vaultTimer / vaultDuration);
        rb.MovePosition(Vector3.Lerp(vaultStartPos, vaultEndPos, t));

        if (t >= 1f)
        {
            vaulting      = false;
            rb.useGravity = true;
            OnVaultEnd?.Invoke();
            Log("Vault end.");
        }
    }

    #endregion

    // ─────────────────────────────────────────────────────────────────────────
    #region Grapple Arc
    // ─────────────────────────────────────────────────────────────────────────

    public void JumpToPosition(Vector3 targetPosition, float trajectoryHeight)
    {
        activeGrapple = true;
        velocityToSet = CalculateJumpVelocity(transform.position, targetPosition, trajectoryHeight);
        Invoke(nameof(SetVelocity), 0.1f);
        Invoke(nameof(ResetRestrictions), 3f);
    }

    private void SetVelocity()
    {
        enableMovementOnNextTouch = true;
        rb.linearVelocity = velocityToSet;
        if (cam != null) cam.DoFov(grappleFov);
    }

    public void ResetRestrictions()
    {
        activeGrapple = false;
        if (cam != null) cam.DoFov(85f);
    }

    private void OnCollisionEnter(Collision collision)
    {
        if (enableMovementOnNextTouch)
        {
            enableMovementOnNextTouch = false;
            ResetRestrictions();
            GetComponent<Grappling>()?.StopGrapple();
        }
    }

    #endregion

    // ─────────────────────────────────────────────────────────────────────────
    #region Slope & Physics Helpers  (public — used by ability scripts)
    // ─────────────────────────────────────────────────────────────────────────

    public bool OnSlope()
    {
        if (Physics.Raycast(transform.position, Vector3.down,
                            out slopeHit, playerHeight * 0.5f + 0.3f))
        {
            float angle = Vector3.Angle(Vector3.up, slopeHit.normal);
            return angle < maxSlopeAngle && angle != 0;
        }
        return false;
    }

    public Vector3 GetSlopeMoveDirection(Vector3 direction) =>
        Vector3.ProjectOnPlane(direction, slopeHit.normal).normalized;

    public Vector3 CalculateJumpVelocity(Vector3 start, Vector3 end, float height)
    {
        float   gravity        = Physics.gravity.y;
        float   displacementY  = end.y - start.y;
        Vector3 displacementXZ = new Vector3(end.x - start.x, 0f, end.z - start.z);

        Vector3 velocityY  = Vector3.up * Mathf.Sqrt(-2f * gravity * height);
        Vector3 velocityXZ = displacementXZ /
                             (Mathf.Sqrt(-2f * height / gravity)
                            + Mathf.Sqrt(2f * (displacementY - height) / gravity));
        return velocityXZ + velocityY;
    }

    #endregion

    // ─────────────────────────────────────────────────────────────────────────
    #region Debug
    // ─────────────────────────────────────────────────────────────────────────

    [System.Diagnostics.Conditional("UNITY_EDITOR")]
    private void Log(string msg)
    {
        if (debugLog) Debug.Log($"[PlayerMovement] {msg}", this);
    }

    #endregion
}