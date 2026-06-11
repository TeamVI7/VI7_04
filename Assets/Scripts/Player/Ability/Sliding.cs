using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Sliding : MonoBehaviour
{
    [Header("References")]
    public Transform orientation;
    public Transform playerObj;
    public PlayerCam cam;
    private Rigidbody rb;
    private PlayerMovement pm;
    private CapsuleCollider col;

    [Header("Sliding")]
    public float maxSlideTime;
    public float slideForce;
    public float slideStopSpeed = 4f;
    public float slideDrag = 4f;
    private float slideTimer;
    private float originalDrag;

    public float slideYScale;
    private float startYScale;
    private float originalColHeight;
    private Vector3 originalColCenter;

    [Header("Input")]
    public KeyCode slideKey = KeyCode.LeftControl;
    private float horizontalInput;
    private float verticalInput;

    private void Start()
    {
        rb = GetComponent<Rigidbody>();
        pm = GetComponent<PlayerMovement>();
        col = GetComponent<CapsuleCollider>();

        startYScale = playerObj.localScale.y;
        originalColHeight = col.height;
        originalColCenter = col.center;
    }

    private void Update()
    {
        horizontalInput = Input.GetAxisRaw("Horizontal");
        verticalInput = Input.GetAxisRaw("Vertical");

        if (Input.GetKeyDown(slideKey) && (horizontalInput != 0 || verticalInput != 0))
            StartSlide();

        if (Input.GetKeyUp(slideKey) && pm.sliding)
            StopSlide();
    }

    private void FixedUpdate()
    {
        if (pm.sliding)
            SlidingMovement();
    }

    private void StartSlide()
    {
        pm.sliding = true;
        originalDrag = rb.linearDamping;
        rb.linearDamping = slideDrag;
        col.height = originalColHeight * 0.5f;
        col.center = new Vector3(originalColCenter.x, originalColCenter.y * 0.5f, originalColCenter.z);
        cam.DoSlideOffset(true);
        rb.AddForce(Vector3.down * 5f, ForceMode.Impulse);
        slideTimer = maxSlideTime;
    }

    private void SlidingMovement()
    {
        Vector3 inputDirection = orientation.forward * verticalInput + orientation.right * horizontalInput;

        if (!pm.OnSlope() || rb.linearVelocity.y > -0.1f)
        {
            rb.AddForce(inputDirection.normalized * slideForce, ForceMode.Force);
            slideTimer -= Time.deltaTime;
        }
        else
        {
            rb.AddForce(pm.GetSlopeMoveDirection(inputDirection) * slideForce, ForceMode.Force);
        }

        float flatSpeed = new Vector3(rb.linearVelocity.x, 0f, rb.linearVelocity.z).magnitude;
        if (slideTimer <= 0 || flatSpeed <= slideStopSpeed)
            StopSlide();
    }

    private void StopSlide()
    {
        pm.sliding = false;
        rb.linearDamping = originalDrag;
        col.height = originalColHeight;
        col.center = originalColCenter;
        cam.DoSlideOffset(false);
    }

}