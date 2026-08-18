using System;
using UnityEngine;

/// <summary>
/// Ground slide ability gated on momentum rather than on the sprint state alone.
///
/// GATE MODEL:
///   A slide starts when the player is grounded, giving movement input, and EITHER
///   sprinting OR carrying at least minMomentumToSlide of flat speed. The momentum
///   path is what lets a dash (or a wallrun exit / downhill run) feed into a slide —
///   PlayerMovement.state is 'dashing'/'air'/'walking' in those cases, never
///   'sprinting', so a sprint-only gate silently rejects them.
///
///   Presses are buffered for slideBufferTime seconds, so hitting the slide key
///   mid-dash starts the slide the moment the dash releases the movement state
///   instead of being dropped.
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
    public float slideYScale   = 0.5f; 

    [Header("Restrictions")]
    [Tooltip("Minimum horizontal velocity magnitude required to initiate a slide.")]
    public float minMomentumToSlide = 6f;

    [Tooltip("Allow sliding out of any fast state (dash, wallrun exit, downhill), not just sprinting. " +
             "Off = legacy sprint-only behaviour.")]
    public bool allowSlideFromMomentum = true;

    [Tooltip("How long a slide press is remembered while the gate is closed — covers the dash window " +
             "and the few frames after landing. 0 = no buffering.")]
    public float slideBufferTime = 0.2f;

    [Header("Stamina")]
    [Tooltip("Stamina spent per slide, charged on start. 0 = free.")]
    public float slideStaminaCost = 0f;

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
    private float   slideBufferTimer;

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
        // Sliding reads raw input directly, so it needs the same UI/death guard
        // PlayerMovement has — otherwise Ctrl still slides the player during a
        // minigame or on the death screen.
        if (PlayerActionLock.InputBlocked)
        {
            horizontalInput  = 0f;
            verticalInput    = 0f;
            slideBufferTimer = 0f;
            if (pm.sliding) StopSlide();
            return;
        }

        horizontalInput = Input.GetAxisRaw("Horizontal");
        verticalInput   = Input.GetAxisRaw("Vertical");

        // Arm the buffer on press; it retries the gate for slideBufferTime seconds
        // so a press made mid-dash isn't thrown away.
        if (Input.GetKeyDown(slideKey) && !pm.sliding)
            slideBufferTimer = Mathf.Max(slideBufferTime, Time.deltaTime);

        if (slideBufferTimer > 0f)
        {
            if (CanInitiateSlide() && pm.TryConsumeStamina(slideStaminaCost))
            {
                slideBufferTimer = 0f;
                StartSlide();
            }
            else
            {
                slideBufferTimer -= Time.deltaTime;
                if (slideBufferTimer <= 0f)
                    Log("Slide blocked: needs grounded + movement input + (sprinting or enough momentum).");
            }
        }

        if (Input.GetKeyUp(slideKey))
        {
            slideBufferTimer = 0f;
            if (pm.sliding) StopSlide();
        }
    }

    private void FixedUpdate()
    {
        if (pm.sliding) SlidingMovement();
    }

    /// <summary>
    /// Safety net: a component disabled mid-slide would otherwise leave pm.sliding
    /// stuck true (movement state permanently 'sliding') and the reload lock raised.
    /// </summary>
    private void OnDisable()
    {
        slideBufferTimer = 0f;
        if (pm != null && pm.sliding) StopSlide();
    }

    #endregion

    // ─────────────────────────────────────────────────────────────────────────
    #region Slide Logic
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Evaluates whether the player is moving fast enough — by sprint state or by raw
    /// momentum — to start a slide right now.
    /// </summary>
    private bool CanInitiateSlide()
    {
        if (pm == null || rb == null) return false;

        // 1. Not already sliding
        if (pm.sliding) return false;

        // 2. Dash owns the movement state and velocity while it runs. Don't fight it —
        //    the buffer re-checks once the dash ends, which is when the slide should fire.
        if (pm.dashing) return false;

        // 3. Ground slide only. (Sprinting used to imply this; the momentum path doesn't.)
        if (!pm.grounded) return false;

        // 4. Must have input movement — SlidingMovement steers off it.
        if (horizontalInput == 0f && verticalInput == 0f) return false;

        // 5. Sprinting always qualifies, regardless of the momentum number.
        if (pm.state == PlayerMovement.MovementState.sprinting) return true;

        if (!allowSlideFromMomentum) return false;

        // 6. Otherwise carry-over speed decides: dash exit, wallrun exit, downhill run.
        Vector3 flatVelocity = new Vector3(rb.linearVelocity.x, 0f, rb.linearVelocity.z);
        return flatVelocity.magnitude >= minMomentumToSlide;
    }

    private void StartSlide()
    {
        pm.sliding       = true;
        originalDrag     = rb.linearDamping;
        rb.linearDamping = slideDrag;

        // PlayerActionLock.CanReload already consults LockReason.Sliding, but nothing
        // was raising it — so a reload could be restarted the frame after StartSlide
        // cancelled it. Raise it for the duration of the slide.
        PlayerActionLock.Instance.SetLock(PlayerActionLock.LockReason.Sliding, true);

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
        if (cam != null) cam.DoSlideOffset(false); // reachable from OnDisable during teardown

        PlayerActionLock.Instance.SetLock(PlayerActionLock.LockReason.Sliding, false);

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