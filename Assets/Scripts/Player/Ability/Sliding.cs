using System;
using UnityEngine;

/// <summary>
/// Ground slide ability.
/// 
/// EXTENDING:
///   - Subscribe to OnSlideStart / OnSlideEnd for dust VFX, audio, screen shake, etc.
///   - Add a charge/cooldown system by wrapping TrySlide().
///   - Increase slideForce based on current speed for speed-boosted slides.
/// 
/// DEBUG:
///   - Enable debugLog in Inspector.
///   - slideTimer is shown in Inspector (SerializeField optional — add [SerializeField] if needed).
/// </summary>
public class Sliding : MonoBehaviour
{
    // ─────────────────────────────────────────────────────────────────────────
    #region Inspector
    // ─────────────────────────────────────────────────────────────────────────

    [Header("References")]
    public Transform          orientation;
    public Transform          playerObj;
    public PlayerCam          cam;
    public WeaponsController  activeWeapon;

    [Header("Slide Settings")]
    public float maxSlideTime  = 1f;
    public float slideForce    = 200f;
    public float slideStopSpeed = 4f;
    public float slideDrag     = 4f;
    public float slideYScale   = 0.5f;  // kept for reference — collider used in practice

    [Header("Input")]
    public KeyCode slideKey = KeyCode.LeftControl;

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

    public event Action OnSlideStart;
    public event Action OnSlideEnd;

    #endregion

    // ─────────────────────────────────────────────────────────────────────────
    #region Public Read-only State
    // ─────────────────────────────────────────────────────────────────────────

    public bool  IsSliding        => pm != null && pm.sliding;
    public float SlideTimeRemaining => Mathf.Max(0f, slideTimer);

    #endregion

    // ─────────────────────────────────────────────────────────────────────────
    #region Private
    // ─────────────────────────────────────────────────────────────────────────

    private Rigidbody       rb;
    private PlayerMovement  pm;
    private CapsuleCollider col;

    private float   slideTimer;
    private float   originalDrag;
    private float   originalColHeight;
    private Vector3 originalColCenter;

    private float   horizontalInput;
    private float   verticalInput;

    #endregion

    // ─────────────────────────────────────────────────────────────────────────
    #region Unity Lifecycle
    // ─────────────────────────────────────────────────────────────────────────

    private void Start()
    {
        rb  = GetComponent<Rigidbody>();
        pm  = GetComponent<PlayerMovement>();
        col = GetComponent<CapsuleCollider>();

        originalColHeight = col.height;
        originalColCenter = col.center;
    }

    private void Update()
    {
        horizontalInput = Input.GetAxisRaw("Horizontal");
        verticalInput   = Input.GetAxisRaw("Vertical");

        if (Input.GetKeyDown(slideKey) && (horizontalInput != 0 || verticalInput != 0))
            StartSlide();

        if (Input.GetKeyUp(slideKey) && pm.sliding)
            StopSlide();
    }

    private void FixedUpdate()
    {
        if (pm.sliding) SlidingMovement();
    }

    #endregion

    // ─────────────────────────────────────────────────────────────────────────
    #region Slide Logic
    // ─────────────────────────────────────────────────────────────────────────

    private void StartSlide()
    {
        pm.sliding       = true;
        originalDrag     = rb.linearDamping;
        rb.linearDamping = slideDrag;

        col.height = originalColHeight * 1f;
        col.center = new Vector3(originalColCenter.x, originalColCenter.y * 0.75f, originalColCenter.z);

        cam.DoSlideOffset(true);
        rb.AddForce(Vector3.down * 5f, ForceMode.Impulse);
        slideTimer = maxSlideTime;

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

        OnSlideStart?.Invoke();
        Log("Slide start.");
    }

    private void SlidingMovement()
    {
        Vector3 dir = orientation.forward * verticalInput + orientation.right * horizontalInput;

        if (!pm.OnSlope() || rb.linearVelocity.y > -0.1f)
        {
            rb.AddForce(dir.normalized * slideForce, ForceMode.Force);
            slideTimer -= Time.deltaTime;
        }
        else
        {
            rb.AddForce(pm.GetSlopeMoveDirection(dir) * slideForce, ForceMode.Force);
        }

        float flatSpeed = new Vector3(rb.linearVelocity.x, 0f, rb.linearVelocity.z).magnitude;
        if (slideTimer <= 0f || flatSpeed <= slideStopSpeed)
            StopSlide();
    }

    private void StopSlide()
    {
        pm.sliding       = false;
        rb.linearDamping = originalDrag;
        col.height       = originalColHeight;
        col.center       = originalColCenter;
        cam.DoSlideOffset(false);

        OnSlideEnd?.Invoke();
        Log("Slide end.");
    }

    #endregion

    // ─────────────────────────────────────────────────────────────────────────
    #region Debug
    // ─────────────────────────────────────────────────────────────────────────

    [System.Diagnostics.Conditional("UNITY_EDITOR")]
    private void Log(string msg)
    {
        if (debugLog) Debug.Log($"[Sliding] {msg}", this);
    }

    #endregion
}