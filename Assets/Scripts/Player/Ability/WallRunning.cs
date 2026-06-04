using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class WallRunning : MonoBehaviour
{
    [Header("Wallrunning")]
    public LayerMask whatIsWall;
    public LayerMask whatIsGround;
    public float wallRunForce;
    public float wallJumpUpForce;
    public float wallJumpSideForce;
    public float wallClimbSpeed;
    public float wallSlideSpeed = 2f;
    public float maxWallRunTime;
    public float minWallAngle = 60f;
    private float wallRunTimer;
    private bool wallSliding;

    [Header("Input")]
    public KeyCode jumpKey = KeyCode.Space;
    public KeyCode upwardsRunKey = KeyCode.LeftShift;
    public KeyCode downwardsRunKey = KeyCode.LeftControl;
    private bool upwardsRunning;
    private bool downwardsRunning;
    private float horizontalInput;
    private float verticalInput;

    [Header("Detection")]
    public float wallCheckDistance;
    public float minJumpHeight;
    private RaycastHit leftWallhit;
    private RaycastHit rightWallhit;
    private bool wallLeft;
    private bool wallRight;

    [Header("Exiting")]
    private bool exitingWall;
    public float exitWallTime;
    private float exitWallTimer;

    [Header("Gravity")]
    public bool useGravity;
    public float gravityCounterForce;

    [Header("References")]
    public Transform orientation;
    public PlayerCam cam;
    private PlayerMovement pm;
    private Rigidbody rb;

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
        if (pm.wallrunning)
            WallRunningMovement();
        else if (wallSliding)
            WallSlidingMovement();
    }

    private void CheckForWall()
    {
        bool hitRight = Physics.Raycast(transform.position, orientation.right, out rightWallhit, wallCheckDistance, whatIsWall);
        bool hitLeft = Physics.Raycast(transform.position, -orientation.right, out leftWallhit, wallCheckDistance, whatIsWall);

        wallRight = hitRight && IsWallNormal(rightWallhit.normal);
        wallLeft = hitLeft && IsWallNormal(leftWallhit.normal);
    }

    private bool IsWallNormal(Vector3 normal)
    {
        return Vector3.Angle(normal, Vector3.up) > minWallAngle;
    }

    private bool AboveGround()
    {
        return !Physics.Raycast(transform.position, Vector3.down, minJumpHeight, whatIsGround);
    }

    private void StateMachine()
    {
        // Getting Inputs
        horizontalInput = Input.GetAxisRaw("Horizontal");
        verticalInput = Input.GetAxisRaw("Vertical");

        upwardsRunning = Input.GetKey(upwardsRunKey);
        downwardsRunning = Input.GetKey(downwardsRunKey);

        // State 1 - Wallrunning
        if((wallLeft || wallRight) && verticalInput > 0 && AboveGround() && !exitingWall)
        {
            if (!pm.wallrunning)
                StartWallRun();

            if (wallSliding)
                StopWallSlide();

            // wallrun timer
            if (wallRunTimer > 0)
                wallRunTimer -= Time.deltaTime;

            if(wallRunTimer <= 0 && pm.wallrunning)
            {
                exitingWall = true;
                exitWallTimer = exitWallTime;
            }

            // wall jump
            if (Input.GetKeyDown(jumpKey)) WallJump();
        }

        // State 2 - Wall sliding
        else if ((wallLeft || wallRight) && !pm.grounded && !exitingWall)
        {
            if (pm.wallrunning)
                StopWallRun();

            if (!wallSliding)
                StartWallSlide();

            if (Input.GetKeyDown(jumpKey)) WallJump();
        }

        // State 3 - Exiting
        else if (exitingWall)
        {
            if (pm.wallrunning)
                StopWallRun();
            if (wallSliding)
                StopWallSlide();

            if (exitWallTimer > 0)
                exitWallTimer -= Time.deltaTime;

            if (exitWallTimer <= 0)
                exitingWall = false;
        }

        // State 4 - None
        else
        {
            if (pm.wallrunning)
                StopWallRun();
            if (wallSliding)
                StopWallSlide();
        }
    }

    private void StartWallRun()
    {
        cam.disableMoveTilt = true;
        pm.wallrunning = true;
        pm.climbing = false;

        wallRunTimer = maxWallRunTime;

        rb.linearVelocity = new Vector3(rb.linearVelocity.x, 0f, rb.linearVelocity.z);

        // apply camera effects
        cam.DoFov(90f);
        if (wallLeft) cam.DoTilt(-5f);
        if (wallRight) cam.DoTilt(5f);
    }

    private void WallRunningMovement()
    {
        rb.useGravity = useGravity;

        Vector3 wallNormal = wallRight ? rightWallhit.normal : leftWallhit.normal;

        Vector3 wallForward = Vector3.Cross(wallNormal, transform.up);
        if ((orientation.forward - wallForward).magnitude > (orientation.forward - -wallForward).magnitude)
            wallForward = -wallForward;

        // keep a stable wall run speed instead of accelerating indefinitely
        float currentY = rb.linearVelocity.y;
        Vector3 targetVelocity = wallForward.normalized * wallRunForce;
        if (upwardsRunning)
            currentY = wallClimbSpeed;
        else if (downwardsRunning)
            currentY = -wallClimbSpeed;

        rb.linearVelocity = new Vector3(targetVelocity.x, currentY, targetVelocity.z);

        // gently keep the player pressed to the wall
        if (!(wallLeft && horizontalInput > 0) && !(wallRight && horizontalInput < 0))
            rb.AddForce(-wallNormal * 30f, ForceMode.Acceleration);

        // weaken gravity while wallrunning
        if (useGravity)
            rb.AddForce(transform.up * gravityCounterForce, ForceMode.Acceleration);
    }

    private void StopWallRun()
    {
        cam.disableMoveTilt = false;
        pm.wallrunning = false;

        // reset camera effects
        cam.DoFov(80f);
        cam.DoTilt(0f);
    }

    private void StartWallSlide()
    {
        wallSliding = true;
        pm.wallSliding = true;
    }

    private void WallSlidingMovement()
    {
        rb.useGravity = true;
        Vector3 wallNormal = wallRight ? rightWallhit.normal : leftWallhit.normal;
        float slideY = Mathf.Max(rb.linearVelocity.y, -wallSlideSpeed);
        rb.linearVelocity = new Vector3(rb.linearVelocity.x, slideY, rb.linearVelocity.z);

        if (!(wallLeft && horizontalInput > 0) && !(wallRight && horizontalInput < 0))
            rb.AddForce(-wallNormal * 15f, ForceMode.Acceleration);
    }

    private void StopWallSlide()
    {
        wallSliding = false;
        pm.wallSliding = false;
    }

    private void WallJump()
    {
        // enter exiting wall state
        exitingWall = true;
        exitWallTimer = exitWallTime;

        Vector3 wallNormal = wallRight ? rightWallhit.normal : leftWallhit.normal;

        Vector3 forceToApply = transform.up * wallJumpUpForce + wallNormal * wallJumpSideForce;

        // reset y velocity and add force
        rb.linearVelocity = new Vector3(rb.linearVelocity.x, 0f, rb.linearVelocity.z);
        rb.AddForce(forceToApply, ForceMode.Impulse);
    }
}
