using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Climbing : MonoBehaviour
{
    [Header("References")]
    public Transform orientation;
    public Rigidbody rb;
    public PlayerMovement pm;
    public LayerMask whatIsWall;

    [Header("Climbing")]
    public float climbSpeed;
    public float wallSlideSpeed = 2f;
    public float maxClimbTime;
    public float minWallAngle = 70f;
    private float climbTimer;
    private bool wallSliding;

    private bool climbing;

    [Header("ClimbJumping")]
    public float climbJumpUpForce;
    public float climbJumpBackForce;

    public KeyCode jumpKey = KeyCode.Space;
    public int climbJumps;
    private int climbJumpsLeft;

    [Header("Detection")]
    public float detectionLength;
    public float sphereCastRadius;
    public float maxWallLookAngle;
    private float wallLookAngle;

    private RaycastHit frontWallHit;
    private bool wallFront;

    private Transform lastWall;
    private Vector3 lastWallNormal;
    public float minWallNormalAngleChange;

    [Header("Exiting")]
    public bool exitingWall;
    public float exitWallTime;
    private float exitWallTimer;

    private void Update()
    {
        WallCheck();
        StateMachine();

        if (climbing && !exitingWall)
            ClimbingMovement();
        else if (wallSliding)
            WallSlideMovement();
    }

    private void StateMachine()
    {
        // State 1 - Climbing
        if (wallFront && Input.GetKey(KeyCode.W) && wallLookAngle < maxWallLookAngle && !exitingWall)
        {
            if (!climbing && climbTimer > 0) StartClimbing();

            if (wallSliding) StopWallSlide();

            // timer
            if (climbTimer > 0) climbTimer -= Time.deltaTime;
            if (climbTimer < 0) StopClimbing();
        }

        // State 2 - Wall sliding
        else if (wallFront && !pm.grounded && !exitingWall)
        {
            if (climbing) StopClimbing();

            if (!wallSliding) StartWallSlide();

            if (Input.GetKeyDown(jumpKey) && climbJumpsLeft > 0) ClimbJump();
        }

        // State 3 - Exiting
        else if (exitingWall)
        {
            if (climbing) StopClimbing();
            if (wallSliding) StopWallSlide();

            if (exitWallTimer > 0) exitWallTimer -= Time.deltaTime;
            if (exitWallTimer < 0) exitingWall = false;
        }

        // State 4 - None
        else
        {
            if (climbing) StopClimbing();
            if (wallSliding) StopWallSlide();
        }

        if (wallFront && Input.GetKeyDown(jumpKey) && climbJumpsLeft > 0) ClimbJump();
    }

    private void WallCheck()
    {
        bool hitWall = Physics.SphereCast(transform.position, sphereCastRadius, orientation.forward, out frontWallHit, detectionLength, whatIsWall);
        wallFront = hitWall && IsWallNormal(frontWallHit.normal);

        if (wallFront)
            wallLookAngle = Vector3.Angle(orientation.forward, -frontWallHit.normal);
        else
            wallLookAngle = 180f;

        bool newWall = wallFront && (frontWallHit.transform != lastWall || Mathf.Abs(Vector3.Angle(lastWallNormal, frontWallHit.normal)) > minWallNormalAngleChange);

        if ((wallFront && newWall) || pm.grounded)
        {
            climbTimer = maxClimbTime;
            climbJumpsLeft = climbJumps;
        }
    }

    private bool IsWallNormal(Vector3 normal)
    {
        return Vector3.Angle(normal, Vector3.up) > minWallAngle;
    }

    private void StartClimbing()
    {
        climbing = true;
        pm.climbing = true;
        pm.wallrunning = false;

        lastWall = frontWallHit.transform;
        lastWallNormal = frontWallHit.normal;

        /// idea - camera fov change
    }

    private void ClimbingMovement()
    {
        rb.linearVelocity = new Vector3(rb.linearVelocity.x, climbSpeed, rb.linearVelocity.z);

        /// idea - sound effect
    }

    private void StopClimbing()
    {
        climbing = false;
        pm.climbing = false;

        /// idea - particle effect
        /// idea - sound effect
    }

    private void StartWallSlide()
    {
        wallSliding = true;
        pm.wallSliding = true;
    }

    private void WallSlideMovement()
    {
        rb.useGravity = true;
        float slideY = Mathf.Max(rb.linearVelocity.y, -wallSlideSpeed);
        rb.linearVelocity = new Vector3(rb.linearVelocity.x, slideY, rb.linearVelocity.z);
    }

    private void StopWallSlide()
    {
        wallSliding = false;
        pm.wallSliding = false;
    }

    private void ClimbJump()
    {
        exitingWall = true;
        exitWallTimer = exitWallTime;

        Vector3 forceToApply = transform.up * climbJumpUpForce + frontWallHit.normal * climbJumpBackForce;

        rb.linearVelocity = new Vector3(rb.linearVelocity.x, 0f, rb.linearVelocity.z);
        rb.AddForce(forceToApply, ForceMode.Impulse);

        climbJumpsLeft--;
    }
}