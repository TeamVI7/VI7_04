using System;
using System.Collections;
using UnityEngine;

/// <summary>
/// Tactical FPS player movement controller.
///
/// STATES (priority order): freeze → dashing → vaulting → wallrunning →
///   climbing → wallSliding → sliding → leaning → crouching → sprinting →
///   walking → standing → air
///
/// EXTENDING:
///   - Subscribe to events (OnLanded, OnJumped, OnStateChanged, OnStaminaChanged, etc.)
///   - Add new MovementState values and a corresponding else-if block in StateHandler()
///   - New ability flags go in the "State flags" region — keep them [HideInInspector]
///   - Footstep sounds: subscribe to OnFootstep
///
/// DEBUG:
///   - Enable debugLog in Inspector to see state transitions
///   - Use PlayerStateDisplay component for on-screen overlay
/// </summary>
public class PlayerMovement : MonoBehaviour
{
    // ─────────────────────────────────────────────────────────────────────────
    #region Inspector
    // ─────────────────────────────────────────────────────────────────────────

    [Header("Movement Speeds")]
    public float walkSpeed             = 5f;
    public float sprintSpeed           = 8f;
    public float crouchSpeed           = 2.5f;
    public float slideSpeed            = 12f;
    public float wallrunSpeed          = 10f;
    public float climbSpeed            = 3f;
    public float dashSpeed             = 20f;
    public float dashSpeedChangeFactor = 5f;
    public float airMultiplier         = 0.3f;
    public float groundDrag            = 6f;

    [Header("Speed Smoothing")]
    public float speedIncreaseMultiplier = 1.5f;
    public float slopeIncreaseMultiplier = 2.5f;

    [Header("Stamina")]
    [Tooltip("Maximum stamina. Sprinting drains it continuously; abilities (dash, slide) spend it " +
             "per use via TryConsumeStamina. Standing/walking restores it after staminaRegenDelay.")]
    public float maxStamina            = 100f;
    [Tooltip("Stamina drained per second while sprinting.")]
    public float staminaDrainRate      = 20f;
    [Tooltip("Stamina restored per second while not draining it.")]
    public float staminaRegenRate      = 15f;
    [Tooltip("Delay after the last stamina use (sprint or ability) before regen begins.")]
    public float staminaRegenDelay     = 1.5f;
    [Tooltip("Minimum stamina required to start a new sprint.")]
    public float minStaminaToSprint    = 10f;

    [Header("Jumping")]
    public float jumpForce    = 11f;
    public float jumpCooldown = 0.25f;

    [Header("Crouching")]
    public float crouchYScale = 0.5f;

    [Header("Leaning")]
    [Tooltip("How far (world units) the camera holder shifts sideways when leaning.")]
    public float leanAmount        = 0.4f;
    [Tooltip("Roll angle applied to camera when leaning.")]
    public float leanRollDeg       = 5f;
    public float leanLerpSpeed     = 10f;
    public KeyCode leanLeftKey     = KeyCode.Q;
    public KeyCode leanRightKey    = KeyCode.Z; // FIX: was KeyCode.E — conflicted with ComputerInteraction

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

    [Header("Keybinds")]
    public KeyCode jumpKey    = KeyCode.Space;
    public KeyCode crouchKey  = KeyCode.LeftControl;
    public KeyCode sprintKey  = KeyCode.LeftShift;

    [Header("References")]
    public Transform   orientation;
    [Tooltip("The camera holder Transform for lean offset (usually the same one PlayerCam uses).")]
    public Transform   cameraHolder;

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

    /// <summary>
    /// Fired each time the bob cycle crosses zero while walking/sprinting.
    /// Subscribe from a FootstepAudio component to play sounds.
    /// Args: (isSprinting)
    /// </summary>
    public event Action<bool> OnFootstep;

    /// <summary>
    /// Fired when stamina value changes. Args: (current, max)
    /// Subscribe from a UI HUD component.
    /// </summary>
    public event Action<float, float> OnStaminaChanged;

    #endregion

    // ─────────────────────────────────────────────────────────────────────────
    #region State Flags  (set by ability scripts)
    // ─────────────────────────────────────────────────────────────────────────
    [HideInInspector] public bool  groundStick;
    [HideInInspector] public bool  sliding;
    [HideInInspector] public bool  wallrunning;
    [HideInInspector] public bool  climbing;
    [HideInInspector] public bool  vaulting;
    [HideInInspector] public bool  dashing;
    [HideInInspector] public bool  freeze;
    [HideInInspector] public float maxYSpeed;
    [HideInInspector] public bool  grounded;

    // ── Wall sliding: two independent owners ─────────────────────────────────
    // WallRunning (side walls) and Climbing (front walls) can both be wall-sliding,
    // and both used to write a single shared bool. Whichever stopped LAST cleared the
    // flag out from under the other, dropping the player out of the wallSliding state
    // while it was still physically sliding. The flag is now derived from a source
    // mask, so it only clears once EVERY owner has released it.

    [Flags]
    public enum WallSlideSource
    {
        None    = 0,
        WallRun = 1 << 0,
        Climb   = 1 << 1,
    }

    private WallSlideSource _wallSlideSources;

    /// <summary>True while at least one system reports the player is wall sliding.</summary>
    public bool wallSliding => _wallSlideSources != WallSlideSource.None;

    /// <summary>Raise or release one owner's claim on the wall-slide state.</summary>
    public void SetWallSliding(WallSlideSource source, bool active)
    {
        _wallSlideSources = active
            ? _wallSlideSources | source
            : _wallSlideSources & ~source;
    }

    #endregion

    // ─────────────────────────────────────────────────────────────────────────
    #region Public Read-Only State
    // ─────────────────────────────────────────────────────────────────────────

    public float Stamina    => _stamina;
    public float StaminaPct => _stamina / maxStamina;
    public bool  IsSprinting => state == MovementState.sprinting;
    public bool  IsLeaning   => state == MovementState.leaning;
    public float LeanDirection => _leanDir; // -1 left, 0 none, +1 right

    #endregion

    // ─────────────────────────────────────────────────────────────────────────
    #region State Machine
    // ─────────────────────────────────────────────────────────────────────────

    public MovementState state { get; private set; }

    public enum MovementState
    {
        freeze,
        dashing,
        vaulting,
        wallrunning,
        climbing,
        wallSliding,
        sliding,
        leaning,
        crouching,
        sprinting,
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

    // Vaulting
    private Vector3 vaultStartPos;
    private Vector3 vaultEndPos;
    private float   vaultTimer;

    // Slope / ground
    private RaycastHit slopeHit;
    private RaycastHit groundHit;
    private bool       exitingSlope;
    private bool       readyToJump = true;

    // Scale/collider at start
    private float   startYScale;
    private float   startColHeight;
    private Vector3 startColCenter;

    // Stamina
    private float _stamina;
    private float _lastStaminaUse;   // sprint drain OR ability spend — both delay regen
    private bool  _canSprint;

    // Leaning
    private float   _leanDir;
    private Vector3 _leanCurrentOffset;
    private float   _leanCurrentRoll;

    // Footstep
    private float _footstepTimer;
    private bool  _lastFootstepPositive;

    // Landing detection
    private bool _wasGrounded;

    #endregion

    // ─────────────────────────────────────────────────────────────────────────
    #region Unity Lifecycle
    // ─────────────────────────────────────────────────────────────────────────

    private void Start()
    {
        col = GetComponent<CapsuleCollider>();
        rb  = GetComponent<Rigidbody>();
        rb.freezeRotation = true;

        startYScale    = transform.localScale.y;
        startColHeight = col.height;
        startColCenter = col.center;

        _stamina   = maxStamina;
        _canSprint = true;
    }

    /// <summary>
    /// DeathCamera deactivates and reactivates the player GameObject across a respawn, so
    /// OnEnable is the respawn hook. Without this the player comes back with whatever
    /// stamina they died on — including 0 with sprint locked out.
    /// </summary>
    private void OnEnable()
    {
        _stamina        = maxStamina;
        _canSprint      = true;
        _lastStaminaUse = -999f;
        OnStaminaChanged?.Invoke(_stamina, maxStamina);

        // Ability flags are owned by the ability scripts, which clear them in their own
        // OnDisable — but clear them here too so a flag can never survive a respawn and
        // permanently early-return MovePlayer().
        sliding = wallrunning = climbing = vaulting = dashing = freeze = false;
        _wallSlideSources = WallSlideSource.None;
        maxYSpeed = 0f;

        // StartVault() and the climb/wallrun abilities all turn gravity off; dying mid-move
        // would otherwise respawn a weightless player. (null on the very first enable —
        // Start() hasn't fetched it yet, and nothing has turned gravity off at that point.)
        if (rb != null) rb.useGravity = true;
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
        TickStamina();
        TickLean();
        TickFootstep();

        // Drag
        bool onGround = state == MovementState.walking
                     || state == MovementState.standing
                     || state == MovementState.crouching
                     || state == MovementState.sprinting
                     || state == MovementState.leaning;
        rb.linearDamping = onGround ? groundDrag : 0f;
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
        // FIX: zero out input and bail when UI is open.
        // PlayerCam already guards with UIOpen; PlayerMovement was missing this,
        // letting the player move/jump/crouch freely during the minigame.
        if (ComputerInteraction.UIOpen || UIInputBlocker.IsBlocking)
        {
            horizontalInput = 0f;
            verticalInput   = 0f;
            _leanDir        = 0f;
            return;
        }

        horizontalInput = Input.GetAxisRaw("Horizontal");
        verticalInput   = Input.GetAxisRaw("Vertical");

        // Jump
        if (Input.GetKey(jumpKey) && readyToJump && grounded)
        {
            readyToJump = false;
            Jump();
            Invoke(nameof(ResetJump), jumpCooldown);
        }

        // Crouch down
        if (Input.GetKeyDown(crouchKey))
        {
            col.height = startColHeight * crouchYScale;
            col.center = new Vector3(startColCenter.x, startColCenter.y * crouchYScale, startColCenter.z);
            rb.AddForce(Vector3.down * 5f, ForceMode.Impulse);
        }

        // Crouch release
        if (Input.GetKeyUp(crouchKey))
        {
            col.height = startColHeight;
            col.center = startColCenter;
        }

        // Lean direction (-1 / 0 / +1)
        bool leanL = Input.GetKey(leanLeftKey);
        bool leanR = Input.GetKey(leanRightKey);
        _leanDir = leanL && !leanR ? -1f : (!leanL && leanR ? 1f : 0f);
    }

    #endregion

    // ─────────────────────────────────────────────────────────────────────────
    #region State Handler
    // ─────────────────────────────────────────────────────────────────────────

    private void StateHandler()
    {
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
        else if (CanSprint() && Input.GetKey(sprintKey) && grounded
                 && (horizontalInput != 0f || verticalInput > 0f))
        {
            SetState(MovementState.sprinting);
            desiredMoveSpeed = sprintSpeed;
        }
        else if (_leanDir != 0f && grounded
                 && horizontalInput == 0f && verticalInput == 0f)
        {
            SetState(MovementState.leaning);
            desiredMoveSpeed = 0f;
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
        if (freeze || dashing || vaulting || wallrunning || climbing || wallSliding)
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

            if (horizontalInput == 0f && verticalInput == 0f)
            {
                Vector3 flatVel = new Vector3(rb.linearVelocity.x, 0f, rb.linearVelocity.z);
                rb.AddForce(-flatVel * 18f, ForceMode.Force);
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
        if (vaulting || !grounded || sliding || wallrunning || climbing || dashing || freeze)
            return;
        if (verticalInput <= 0f) return;

        LayerMask obstacleMask = whatIsWall.value == 0 ? whatIsGround : whatIsWall;
        Vector3   origin       = transform.position + Vector3.up * vaultCheckHeight;
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
    #region Stamina
    // ─────────────────────────────────────────────────────────────────────────

    private bool CanSprint()
    {
        if (!_canSprint) return false;
        return _stamina > 0f;
    }

    /// <summary>
    /// True if the player currently has at least <paramref name="amount"/> stamina.
    /// Use this to grey out an ability without spending anything.
    /// </summary>
    public bool HasStamina(float amount) => _stamina >= amount;

    /// <summary>
    /// Spend stamina for a one-shot ability (dash, slide kick-off, …).
    /// Returns false and spends nothing if the player can't afford it.
    /// Also restarts the regen delay and applies the same depletion lock sprinting uses,
    /// so an ability can't be spammed to keep the player permanently out of stamina.
    /// </summary>
    public bool TryConsumeStamina(float amount)
    {
        if (amount <= 0f) return true;
        if (_stamina < amount) return false;

        _stamina        = Mathf.Max(_stamina - amount, 0f);
        _lastStaminaUse = Time.time;

        if (_stamina <= 0f)
        {
            _canSprint = false;
            Log("Stamina depleted — sprint locked.");
        }

        OnStaminaChanged?.Invoke(_stamina, maxStamina);
        Log($"Stamina spent: {amount} → {_stamina:0.#}/{maxStamina}");
        return true;
    }

    private void TickStamina()
    {
        bool isSprinting = state == MovementState.sprinting;

        if (isSprinting)
        {
            _lastStaminaUse = Time.time;
            float prev = _stamina;
            _stamina = Mathf.Max(_stamina - staminaDrainRate * Time.deltaTime, 0f);

            if (_stamina <= 0f)
            {
                _canSprint = false;
                Log("Stamina depleted — sprint locked.");
            }

            if (!Mathf.Approximately(_stamina, prev))
                OnStaminaChanged?.Invoke(_stamina, maxStamina);
        }
        else if (Time.time - _lastStaminaUse >= staminaRegenDelay)
        {
            float prev = _stamina;
            _stamina = Mathf.Min(_stamina + staminaRegenRate * Time.deltaTime, maxStamina);

            if (!_canSprint && _stamina >= minStaminaToSprint)
            {
                _canSprint = true;
                Log("Sprint re-enabled.");
            }

            if (!Mathf.Approximately(_stamina, prev))
                OnStaminaChanged?.Invoke(_stamina, maxStamina);
        }
    }

    #endregion

    // ─────────────────────────────────────────────────────────────────────────
    #region Lean
    // ─────────────────────────────────────────────────────────────────────────

    private void TickLean()
    {
        if (cameraHolder == null) return;

        Vector3 targetOffset = orientation.right * (_leanDir * leanAmount);
        float   targetRoll   = -_leanDir * leanRollDeg;

        _leanCurrentOffset = Vector3.Lerp(_leanCurrentOffset, targetOffset,
                                          Time.deltaTime * leanLerpSpeed);
        _leanCurrentRoll   = Mathf.Lerp(_leanCurrentRoll, targetRoll,
                                         Time.deltaTime * leanLerpSpeed);

        cameraHolder.localPosition = _leanCurrentOffset;

        Vector3 euler = cameraHolder.localEulerAngles;
        euler.z = _leanCurrentRoll;
        cameraHolder.localEulerAngles = euler;
    }

    #endregion

    // ─────────────────────────────────────────────────────────────────────────
    #region Footstep
    // ─────────────────────────────────────────────────────────────────────────

    private void TickFootstep()
    {
        bool moving = (state == MovementState.walking || state == MovementState.sprinting)
                   && grounded;

        if (!moving) return;

        float freq = state == MovementState.sprinting ? 12f : 7f;
        _footstepTimer += Time.deltaTime * freq;
        bool positive = Mathf.Sin(_footstepTimer) > 0f;

        if (_lastFootstepPositive && !positive)
            OnFootstep?.Invoke(state == MovementState.sprinting);

        _lastFootstepPositive = positive;
    }

    #endregion

    // ─────────────────────────────────────────────────────────────────────────
    #region Slope & Physics Helpers
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