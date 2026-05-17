using System.Collections;
using UnityEngine;

public class PlayerMovement : MonoBehaviour
{
    // -------------------------------------------------------------------------
    // Inspector
    // -------------------------------------------------------------------------

    [Header("Movement")]
    public float walkSpeed       = 7f;
    public float sprintSpeed     = 12f;
    public float crouchSpeed     = 3.5f;
    public float slideSpeed      = 14f;
    public float wallrunSpeed    = 12f;
    public float climbSpeed      = 3f;
    public float swingSpeed      = 12f;
    public float dashSpeed       = 20f;
    public float dashSpeedChangeFactor = 5f;
    public float airMultiplier   = 0.4f;
    public float groundDrag      = 5f;

    [Header("Speed Smoothing")]
    public float speedIncreaseMultiplier  = 1.5f;
    public float slopeIncreaseMultiplier  = 2.5f;

    [Header("Jumping")]
    public float jumpForce    = 12f;
    public float jumpCooldown = 0.25f;

    [Header("Crouching")]
    public float crouchYScale = 0.5f;

    [Header("Ground Check")]
    public float      playerHeight  = 2f;
    public LayerMask  whatIsGround;

    [Header("Slope Handling")]
    public float maxSlopeAngle = 40f;

    [Header("Grapple FOV")]
    public PlayerCam cam;
    public float grappleFov = 95f;

    [Header("Keybinds")]
    public KeyCode jumpKey   = KeyCode.Space;
    public KeyCode sprintKey = KeyCode.LeftShift;
    public KeyCode crouchKey = KeyCode.LeftControl;

    [Header("References")]
    public Transform orientation;

    // -------------------------------------------------------------------------
    // State flags set by ability scripts
    // -------------------------------------------------------------------------

    [HideInInspector] public bool sliding;
    [HideInInspector] public bool wallrunning;
    [HideInInspector] public bool climbing;
    [HideInInspector] public bool dashing;
    [HideInInspector] public bool swinging;
    [HideInInspector] public bool activeGrapple;   // mid-arc grapple launch
    [HideInInspector] public bool freeze;           // freeze during grapple delay
    [HideInInspector] public float maxYSpeed;       // clamped by dash ability
    [HideInInspector] public bool grounded;

    // -------------------------------------------------------------------------
    // State machine
    // -------------------------------------------------------------------------

    public MovementState state { get; private set; }
    public enum MovementState
    {
        freeze,
        dashing,
        grappling,
        swinging,
        wallrunning,
        climbing,
        sliding,
        crouching,
        sprinting,
        walking,
        air
    }

    // -------------------------------------------------------------------------
    // Private
    // -------------------------------------------------------------------------

    private Rigidbody  rb;
    private float      moveSpeed;
    private float      desiredMoveSpeed;
    private float      lastDesiredMoveSpeed;
    private MovementState lastState;
    private bool       keepMomentum;
    private float      speedChangeFactor;

    private float      horizontalInput;
    private float      verticalInput;
    private Vector3    moveDirection;

    private RaycastHit slopeHit;
    private bool       exitingSlope;
    private bool       readyToJump = true;

    private float      startYScale;

    // grapple arc helpers
    private Vector3    velocityToSet;
    private bool       enableMovementOnNextTouch;

    // -------------------------------------------------------------------------
    // Unity lifecycle
    // -------------------------------------------------------------------------

    private void Start()
    {
        rb = GetComponent<Rigidbody>();
        rb.freezeRotation = true;
        startYScale = transform.localScale.y;
    }

    private void Update()
    {
        grounded = Physics.Raycast(transform.position, Vector3.down,
                                   playerHeight * 0.5f + 0.2f, whatIsGround);
        ReadInput();
        StateHandler();
        SpeedControl();

        // drag — grapple arc bypasses drag entirely
        if (!activeGrapple)
        {
            bool onGround = state == MovementState.walking
                         || state == MovementState.sprinting
                         || state == MovementState.crouching;
            rb.linearDamping = onGround ? groundDrag : 0f;
        }
        else
        {
            rb.linearDamping = 0f;
        }
    }

    private void FixedUpdate()
    {
        MovePlayer();
    }

    // -------------------------------------------------------------------------
    // Input
    // -------------------------------------------------------------------------

    private void ReadInput()
    {
        horizontalInput = Input.GetAxisRaw("Horizontal");
        verticalInput   = Input.GetAxisRaw("Vertical");

        // jump
        if (Input.GetKey(jumpKey) && readyToJump && grounded)
        {
            readyToJump = false;
            Jump();
            Invoke(nameof(ResetJump), jumpCooldown);
        }

        // crouch start
        if (Input.GetKeyDown(crouchKey))
        {
            transform.localScale = new Vector3(transform.localScale.x,
                                               crouchYScale,
                                               transform.localScale.z);
            rb.AddForce(Vector3.down * 5f, ForceMode.Impulse);
        }

        // crouch stop
        if (Input.GetKeyUp(crouchKey))
        {
            transform.localScale = new Vector3(transform.localScale.x,
                                               startYScale,
                                               transform.localScale.z);
        }
    }

    // -------------------------------------------------------------------------
    // State machine — highest-priority state wins
    // -------------------------------------------------------------------------

    private void StateHandler()
    {
        // Freeze (grapple windup)
        if (freeze)
        {
            state = MovementState.freeze;
            rb.linearVelocity = Vector3.zero;
            desiredMoveSpeed  = 0f;
        }

        // Dashing
        else if (dashing)
        {
            state             = MovementState.dashing;
            desiredMoveSpeed  = dashSpeed;
            speedChangeFactor = dashSpeedChangeFactor;
        }

        // Grapple arc
        else if (activeGrapple)
        {
            state            = MovementState.grappling;
            desiredMoveSpeed = sprintSpeed;
        }

        // Swinging
        else if (swinging)
        {
            state            = MovementState.swinging;
            desiredMoveSpeed = swingSpeed;
        }

        // Wallrunning
        else if (wallrunning)
        {
            state            = MovementState.wallrunning;
            desiredMoveSpeed = wallrunSpeed;
        }

        // Climbing
        else if (climbing)
        {
            state            = MovementState.climbing;
            desiredMoveSpeed = climbSpeed;
        }

        // Sliding
        else if (sliding)
        {
            state = MovementState.sliding;
            desiredMoveSpeed = (OnSlope() && rb.linearVelocity.y < 0.1f)
                             ? slideSpeed
                             : sprintSpeed;
        }

        // Crouching
        else if (Input.GetKey(crouchKey))
        {
            state            = MovementState.crouching;
            desiredMoveSpeed = crouchSpeed;
        }

        // Sprinting
        else if (grounded && Input.GetKey(sprintKey))
        {
            state            = MovementState.sprinting;
            desiredMoveSpeed = sprintSpeed;
        }

        // Walking
        else if (grounded)
        {
            state            = MovementState.walking;
            desiredMoveSpeed = walkSpeed;
        }

        // Air
        else
        {
            state            = MovementState.air;
            desiredMoveSpeed = (desiredMoveSpeed < sprintSpeed) ? walkSpeed : sprintSpeed;
        }

        // Speed lerp — keep momentum after dash, use slope-aware lerp otherwise
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
        float factor     = speedChangeFactor;

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
                time += Time.deltaTime * factor;
            }

            yield return null;
        }

        moveSpeed         = desiredMoveSpeed;
        speedChangeFactor = 1f;
        keepMomentum      = false;
    }

    // -------------------------------------------------------------------------
    // Movement
    // -------------------------------------------------------------------------

    private void MovePlayer()
    {
        // these states handle their own velocity
        if (freeze || activeGrapple || swinging || dashing) return;

        moveDirection = orientation.forward * verticalInput
                      + orientation.right   * horizontalInput;

        if (OnSlope() && !exitingSlope)
        {
            rb.AddForce(GetSlopeMoveDirection(moveDirection) * moveSpeed * 20f,
                        ForceMode.Force);
            if (rb.linearVelocity.y > 0)
                rb.AddForce(Vector3.down * 80f, ForceMode.Force);
        }
        else if (grounded)
        {
            rb.AddForce(moveDirection.normalized * moveSpeed * 10f, ForceMode.Force);
        }
        else
        {
            rb.AddForce(moveDirection.normalized * moveSpeed * 10f * airMultiplier,
                        ForceMode.Force);
        }

        // disable gravity on slopes to avoid sliding down
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

        // clamp Y speed (used by dash ability)
        if (maxYSpeed != 0 && rb.linearVelocity.y > maxYSpeed)
            rb.linearVelocity = new Vector3(rb.linearVelocity.x, maxYSpeed, rb.linearVelocity.z);
    }

    // -------------------------------------------------------------------------
    // Jump
    // -------------------------------------------------------------------------

    private void Jump()
    {
        exitingSlope = true;
        rb.linearVelocity = new Vector3(rb.linearVelocity.x, 0f, rb.linearVelocity.z);
        rb.AddForce(transform.up * jumpForce, ForceMode.Impulse);
    }

    private void ResetJump()
    {
        readyToJump  = true;
        exitingSlope = false;
    }

    // -------------------------------------------------------------------------
    // Grapple arc — called by Grappling.cs
    // -------------------------------------------------------------------------

    public void JumpToPosition(Vector3 targetPosition, float trajectoryHeight)
    {
        activeGrapple  = true;
        velocityToSet  = CalculateJumpVelocity(transform.position, targetPosition, trajectoryHeight);
        Invoke(nameof(SetVelocity), 0.1f);
        Invoke(nameof(ResetRestrictions), 3f);
    }

    private void SetVelocity()
    {
        enableMovementOnNextTouch = true;
        rb.linearVelocity         = velocityToSet;
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

    // -------------------------------------------------------------------------
    // Slope helpers — public so Sliding.cs can use GetSlopeMoveDirection
    // -------------------------------------------------------------------------

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

    public Vector3 GetSlopeMoveDirection(Vector3 direction)
    {
        return Vector3.ProjectOnPlane(direction, slopeHit.normal).normalized;
    }

    // -------------------------------------------------------------------------
    // Physics helpers
    // -------------------------------------------------------------------------

    public Vector3 CalculateJumpVelocity(Vector3 start, Vector3 end, float height)
    {
        float   gravity       = Physics.gravity.y;
        float   displacementY = end.y - start.y;
        Vector3 displacementXZ = new Vector3(end.x - start.x, 0f, end.z - start.z);

        Vector3 velocityY  = Vector3.up * Mathf.Sqrt(-2f * gravity * height);
        Vector3 velocityXZ = displacementXZ /
                             (Mathf.Sqrt(-2f * height / gravity)
                            + Mathf.Sqrt(2f * (displacementY - height) / gravity));

        return velocityXZ + velocityY;
    }
}