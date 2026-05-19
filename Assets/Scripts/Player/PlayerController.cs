// ============================================================
//  PlayerController.cs  —  Out of Bullet
//  Single MonoBehaviour for ALL player movement.
//  Covers: movement, jump, sprint, crouch, slope,
//          sliding, climbing, dash, grapple mode.
// ============================================================
using System.Collections;
using UnityEngine;
using OutOfBullet.Core;

namespace OutOfBullet.Player
{
    [RequireComponent(typeof(Rigidbody))]
    public class PlayerController : MonoBehaviour
    {
        // ════════════════════════════════════════════════════════
        //  REFERENCES
        // ════════════════════════════════════════════════════════
        [Header("References")]
        public Transform orientation;   // empty child GO — rotates with camera Y
        public Transform playerObj;     // mesh root — scales for crouch/slide

        [HideInInspector] public Rigidbody Rb;

        // ════════════════════════════════════════════════════════
        //  MOVEMENT
        // ════════════════════════════════════════════════════════
        [Header("Movement")]
        public float walkSpeed = 7f;
        public float sprintSpeed = 10f;
        public float groundDrag = 5f;
        public float speedIncreaseMultiplier = 1.5f;
        public float slopeIncreaseMultiplier = 2.5f;

        [Header("Jump")]
        public float jumpForce = 12f;
        public float jumpCooldown = 0.25f;
        public float airMultiplier = 0.4f;

        [Header("Crouch")]
        public float crouchSpeed = 3f;
        public float crouchYScale = 0.5f;

        [Header("Ground Check")]
        public float playerHeight;
        public LayerMask whatIsGround;

        [Header("Slope")]
        public float maxSlopeAngle = 40f;

        [Header("Keybinds")]
        public KeyCode jumpKey = KeyCode.Space;
        public KeyCode sprintKey = KeyCode.LeftShift;
        public KeyCode crouchKey = KeyCode.LeftControl;

        // ════════════════════════════════════════════════════════
        //  SLIDING
        // ════════════════════════════════════════════════════════
        [Header("Slide")]
        public float slideSpeed = 14f;
        public float slideForce = 400f;
        public float maxSlideTime = 0.75f;
        public float slideYScale = 0.5f;
        public KeyCode slideKey = KeyCode.LeftControl;

        // ════════════════════════════════════════════════════════
        //  CLIMBING
        // ════════════════════════════════════════════════════════
        [Header("Climb")]
        public float climbSpeed = 3f;
        public float maxClimbTime = 0.75f;
        public float climbJumpUpForce = 10f;
        public float climbJumpBackForce = 10f;
        public int climbJumps = 1;
        public float detectionLength = 0.7f;
        public float sphereCastRadius = 0.5f;
        public float maxWallLookAngle = 30f;
        public float minWallNormalAngle = 5f;
        public float exitWallTime = 0.2f;
        public LayerMask whatIsWall;

        // ════════════════════════════════════════════════════════
        //  DASH
        // ════════════════════════════════════════════════════════
        [Header("Dash")]
        public float dashForce = 22f;
        public float iFrameDuration = 0.15f;
        public int maxDashCharges = 3;
        public float chargeRegenTime = 4f;
        public KeyCode dashKey = KeyCode.Mouse2;

        // ── Dash public state (read by HUD / PlayerHealth) ───────
        public int DashCharges { get; private set; }
        public bool IsInvincible { get; private set; }
        public float DashRegenProgress { get; private set; }

        // ════════════════════════════════════════════════════════
        //  PUBLIC STATE  (read by enemies, grapple, HUD)
        // ════════════════════════════════════════════════════════
        public bool grounded { get; private set; }
        public bool sliding { get; private set; }
        public bool climbing { get; private set; }
        public Vector3 Velocity => Rb.linearVelocity;
        public float Speed => Rb.linearVelocity.magnitude;
        public bool IsGrounded => grounded;

        public bool GrappleActive { get; private set; }
        public Vector3 GrappleVelocity;

        // ── Movement state enum ──────────────────────────────────
        public enum MoveState { walking, sprinting, crouching, sliding, climbing, air }
        public MoveState state;

        // ════════════════════════════════════════════════════════
        //  PRIVATE
        // ════════════════════════════════════════════════════════

        // -- Movement --
        private float _moveSpeed, _desiredMoveSpeed, _lastDesiredMoveSpeed;
        private float _h, _v;
        private bool _readyToJump = true;
        private bool _exitingSlope;
        private Vector3 _moveDir;
        private RaycastHit _slopeHit;
        private float _startYScale;

        // -- Slide --
        private float _slideTimer;

        // -- Climb --
        private bool _isClimbing;
        private float _climbTimer;
        private float _wallLookAngle;
        private bool _wallFront;
        private RaycastHit _frontWallHit;
        private Transform _lastWall;
        private Vector3 _lastWallNormal;
        private bool _exitingWall;
        private float _exitWallTimer;
        private int _climbJumpsLeft;

        // -- Dash --
        private bool _dashRegenRunning;
        private Coroutine _iFrameCoroutine;

        // -- Health ref for i-frames --
        private PlayerHealth _health;

        // ════════════════════════════════════════════════════════
        //  UNITY LIFECYCLE
        // ════════════════════════════════════════════════════════
        private void Awake()
        {
            Rb = GetComponent<Rigidbody>();
            Rb.freezeRotation = true;
            _health = GetComponent<PlayerHealth>();
            _startYScale = playerObj ? playerObj.localScale.y : 1f;
            DashCharges = maxDashCharges;
        }

        [Header("Attack Settings")]
        [SerializeField] private float attackRange = 3f; // Khoảng cách có thể đánh tới Enemy
        [SerializeField] private LayerMask enemyMask;    // Layer của Enemy để tránh đánh nhầm đất đá
        private void Update()
        {
            if (GrappleActive) return;

            grounded = Physics.Raycast(transform.position, Vector3.down,
                playerHeight * 0.5f + 0.2f, whatIsGround);

            ReadInput();
            SpeedControl();
            StateHandler();
            ClimbWallCheck();
            ClimbStateMachine();
            if (_isClimbing && !_exitingWall) ClimbingMovement();

            Rb.linearDamping = (grounded && !_isClimbing) ? groundDrag : 0f;

            if (Input.GetKeyDown(KeyCode.H))
            {
                TryHitEnemy();
            }
        }

        private void TryHitEnemy()
        {
            // Điểm phát tia từ chính giữa camera (tâm màn hình FPS) hoặc từ tâm Player
            Vector3 raycastOrigin = transform.position;
            Vector3 dir = transform.forward; // Hướng nhìn thẳng phía trước của Player

            // Vẽ tia Debug màu xanh dương trong Scene để dễ quan sát tầm đánh
            Debug.DrawRay(raycastOrigin, dir * attackRange, Color.blue, 0.3f);

            if (Physics.Raycast(raycastOrigin, dir, out RaycastHit hit, attackRange, enemyMask))
            {
                // Tìm component EnemyHealth trên đối tượng vừa bị đánh trúng
                var enemyHealth = hit.collider.GetComponentInParent<OutOfBullet.Enemy.EnemyHealth>();
                if (enemyHealth != null)
                {
                    enemyHealth.TakeDamage(10f); // Mỗi lần hit mất 10 máu
                }
            }
        }

        private void FixedUpdate()
        {
            if (GrappleActive)
            {
                GrappleVelocity += Physics.gravity * Time.fixedDeltaTime;
                if (grounded && GrappleVelocity.y < 0f) GrappleVelocity.y = -2f;
                Rb.linearVelocity = GrappleVelocity;
                return;
            }

            MovePlayer();
            if (sliding) SlidingMovement();
        }

        private void LateUpdate()
        {
            EventBus.Publish(new PlayerVelocityChangedEvent
            {
                Velocity = Rb.linearVelocity,
                Speed = Speed
            });
        }

        // ════════════════════════════════════════════════════════
        //  INPUT
        // ════════════════════════════════════════════════════════
        private void ReadInput()
        {
            _h = Input.GetAxisRaw("Horizontal");
            _v = Input.GetAxisRaw("Vertical");

            // Jump
            if (Input.GetKey(jumpKey) && _readyToJump && grounded)
            {
                _readyToJump = false;
                Jump();
                Invoke(nameof(ResetJump), jumpCooldown);
            }

            // Crouch start
            if (Input.GetKeyDown(crouchKey) && !sliding)
            {
                if (playerObj) playerObj.localScale =
                    new Vector3(playerObj.localScale.x, crouchYScale, playerObj.localScale.z);
                Rb.AddForce(Vector3.down * 5f, ForceMode.Impulse);
            }

            // Crouch end
            if (Input.GetKeyUp(crouchKey) && !sliding)
            {
                if (playerObj) playerObj.localScale =
                    new Vector3(playerObj.localScale.x, _startYScale, playerObj.localScale.z);
            }

            // Slide start
            if (Input.GetKeyDown(slideKey) && (_h != 0 || _v != 0))
                StartSlide();

            // Slide end
            if (Input.GetKeyUp(slideKey) && sliding)
                StopSlide();

            // Dash
            if (Input.GetKeyDown(dashKey))
                TryDash();
        }

        // ════════════════════════════════════════════════════════
        //  STATE MACHINE
        // ════════════════════════════════════════════════════════
        private void StateHandler()
        {
            if (_isClimbing)
            {
                state = MoveState.climbing;
                _desiredMoveSpeed = climbSpeed;
            }
            else if (sliding)
            {
                state = MoveState.sliding;
                _desiredMoveSpeed = (OnSlope() && Rb.linearVelocity.y < 0.1f) ? slideSpeed : sprintSpeed;
            }
            else if (Input.GetKey(crouchKey))
            {
                state = MoveState.crouching;
                _desiredMoveSpeed = crouchSpeed;
            }
            else if (grounded && Input.GetKey(sprintKey))
            {
                state = MoveState.sprinting;
                _desiredMoveSpeed = sprintSpeed;
            }
            else if (grounded)
            {
                state = MoveState.walking;
                _desiredMoveSpeed = walkSpeed;
            }
            else
            {
                state = MoveState.air;
            }

            if (Mathf.Abs(_desiredMoveSpeed - _lastDesiredMoveSpeed) > 4f && _moveSpeed != 0)
            {
                StopAllCoroutines();
                StartCoroutine(SmoothlyLerpMoveSpeed());
                if (!_dashRegenRunning && DashCharges < maxDashCharges)
                    StartCoroutine(DashRegenRoutine());
            }
            else
            {
                _moveSpeed = _desiredMoveSpeed;
            }

            _lastDesiredMoveSpeed = _desiredMoveSpeed;
        }

        private IEnumerator SmoothlyLerpMoveSpeed()
        {
            float time = 0, diff = Mathf.Abs(_desiredMoveSpeed - _moveSpeed), start = _moveSpeed;
            while (time < diff)
            {
                _moveSpeed = Mathf.Lerp(start, _desiredMoveSpeed, time / diff);
                time += OnSlope()
                    ? Time.deltaTime * speedIncreaseMultiplier * slopeIncreaseMultiplier
                      * (1 + Vector3.Angle(Vector3.up, _slopeHit.normal) / 90f)
                    : Time.deltaTime * speedIncreaseMultiplier;
                yield return null;
            }
            _moveSpeed = _desiredMoveSpeed;
        }

        // ════════════════════════════════════════════════════════
        //  MOVEMENT
        // ════════════════════════════════════════════════════════
        private void MovePlayer()
        {
            if (_isClimbing) return;

            _moveDir = orientation.forward * _v + orientation.right * _h;

            if (OnSlope() && !_exitingSlope)
            {
                Rb.AddForce(GetSlopeMoveDirection(_moveDir) * _moveSpeed * 20f, ForceMode.Force);
                if (Rb.linearVelocity.y > 0) Rb.AddForce(Vector3.down * 80f, ForceMode.Force);
            }
            else if (grounded)
                Rb.AddForce(_moveDir.normalized * _moveSpeed * 10f, ForceMode.Force);
            else
                Rb.AddForce(_moveDir.normalized * _moveSpeed * 10f * airMultiplier, ForceMode.Force);

            Rb.useGravity = !OnSlope();
        }

        private void SpeedControl()
        {
            if (_isClimbing) return;

            if (OnSlope() && !_exitingSlope)
            {
                if (Rb.linearVelocity.magnitude > _moveSpeed)
                    Rb.linearVelocity = Rb.linearVelocity.normalized * _moveSpeed;
            }
            else
            {
                Vector3 flat = new Vector3(Rb.linearVelocity.x, 0f, Rb.linearVelocity.z);
                if (flat.magnitude > _moveSpeed)
                {
                    Vector3 limited = flat.normalized * _moveSpeed;
                    Rb.linearVelocity = new Vector3(limited.x, Rb.linearVelocity.y, limited.z);
                }
            }
        }

        private void Jump()
        {
            _exitingSlope = true;
            Rb.linearVelocity = new Vector3(Rb.linearVelocity.x, 0f, Rb.linearVelocity.z);
            Rb.AddForce(transform.up * jumpForce, ForceMode.Impulse);
        }

        private void ResetJump() { _readyToJump = true; _exitingSlope = false; }

        public bool OnSlope()
        {
            if (Physics.Raycast(transform.position, Vector3.down,
                out _slopeHit, playerHeight * 0.5f + 0.3f))
            {
                float a = Vector3.Angle(Vector3.up, _slopeHit.normal);
                return a < maxSlopeAngle && a != 0;
            }
            return false;
        }

        public Vector3 GetSlopeMoveDirection(Vector3 dir)
            => Vector3.ProjectOnPlane(dir, _slopeHit.normal).normalized;

        // ════════════════════════════════════════════════════════
        //  SLIDING
        // ════════════════════════════════════════════════════════
        private void StartSlide()
        {
            sliding = true;
            if (playerObj) playerObj.localScale =
                new Vector3(playerObj.localScale.x, slideYScale, playerObj.localScale.z);
            Rb.AddForce(Vector3.down * 5f, ForceMode.Impulse);
            _slideTimer = maxSlideTime;
        }

        private void SlidingMovement()
        {
            Vector3 inputDir = orientation.forward * _v + orientation.right * _h;

            if (!OnSlope() || Rb.linearVelocity.y > -0.1f)
            {
                Rb.AddForce(inputDir.normalized * slideForce, ForceMode.Force);
                _slideTimer -= Time.deltaTime;
            }
            else
            {
                Rb.AddForce(GetSlopeMoveDirection(inputDir) * slideForce, ForceMode.Force);
            }

            if (_slideTimer <= 0) StopSlide();
        }

        private void StopSlide()
        {
            sliding = false;
            if (playerObj) playerObj.localScale =
                new Vector3(playerObj.localScale.x, _startYScale, playerObj.localScale.z);
        }

        // ════════════════════════════════════════════════════════
        //  CLIMBING
        // ════════════════════════════════════════════════════════
        private void ClimbWallCheck()
        {
            _wallFront = Physics.SphereCast(transform.position, sphereCastRadius,
                orientation.forward, out _frontWallHit, detectionLength, whatIsWall);
            _wallLookAngle = Vector3.Angle(orientation.forward, -_frontWallHit.normal);

            bool newWall = _frontWallHit.transform != _lastWall
                || Mathf.Abs(Vector3.Angle(_lastWallNormal, _frontWallHit.normal)) > minWallNormalAngle;

            if ((_wallFront && newWall) || grounded)
            {
                _climbTimer = maxClimbTime;
                _climbJumpsLeft = climbJumps;
            }
        }

        private void ClimbStateMachine()
        {
            if (_wallFront && Input.GetKey(KeyCode.W)
                && _wallLookAngle < maxWallLookAngle && !_exitingWall)
            {
                if (!_isClimbing && _climbTimer > 0) StartClimbing();
                if (_climbTimer > 0) _climbTimer -= Time.deltaTime;
                if (_climbTimer < 0) StopClimbing();
            }
            else if (_exitingWall)
            {
                if (_isClimbing) StopClimbing();
                if (_exitWallTimer > 0) _exitWallTimer -= Time.deltaTime;
                if (_exitWallTimer < 0) _exitingWall = false;
            }
            else
            {
                if (_isClimbing) StopClimbing();
            }

            if (_wallFront && Input.GetKeyDown(jumpKey) && _climbJumpsLeft > 0)
                ClimbJump();
        }

        private void StartClimbing()
        {
            _isClimbing = true;
            climbing = true;
            _lastWall = _frontWallHit.transform;
            _lastWallNormal = _frontWallHit.normal;
        }

        private void ClimbingMovement()
        {
            Rb.linearVelocity = new Vector3(Rb.linearVelocity.x, climbSpeed, Rb.linearVelocity.z);
        }

        private void StopClimbing()
        {
            _isClimbing = false;
            climbing = false;
        }

        private void ClimbJump()
        {
            _exitingWall = true;
            _exitWallTimer = exitWallTime;
            Vector3 force = transform.up * climbJumpUpForce
                            + _frontWallHit.normal * climbJumpBackForce;
            Rb.linearVelocity = new Vector3(Rb.linearVelocity.x, 0f, Rb.linearVelocity.z);
            Rb.AddForce(force, ForceMode.Impulse);
            _climbJumpsLeft--;
        }

        // ════════════════════════════════════════════════════════
        //  DASH
        // ════════════════════════════════════════════════════════
        private void TryDash()
        {
            if (DashCharges <= 0) return;

            DashCharges--;
            Rb.AddForce(GetDashDirection() * dashForce, ForceMode.VelocityChange);

            if (_iFrameCoroutine != null) StopCoroutine(_iFrameCoroutine);
            _iFrameCoroutine = StartCoroutine(IFrameRoutine());

            if (!_dashRegenRunning) StartCoroutine(DashRegenRoutine());

            EventBus.Publish(new PlayerDashUsedEvent
            {
                ChargesRemaining = DashCharges,
                MaxCharges = maxDashCharges
            });
        }

        private IEnumerator IFrameRoutine()
        {
            IsInvincible = true;
            _health?.SetInvincible(true);
            yield return new WaitForSeconds(iFrameDuration);
            IsInvincible = false;
            _health?.SetInvincible(false);
        }

        private IEnumerator DashRegenRoutine()
        {
            _dashRegenRunning = true;
            while (DashCharges < maxDashCharges)
            {
                float t = 0f;
                while (t < chargeRegenTime)
                {
                    t += Time.deltaTime;
                    DashRegenProgress = t / chargeRegenTime;
                    yield return null;
                }
                DashCharges = Mathf.Min(DashCharges + 1, maxDashCharges);
                DashRegenProgress = 0f;
                EventBus.Publish(new PlayerDashChargeRestoredEvent
                {
                    ChargesRemaining = DashCharges,
                    MaxCharges = maxDashCharges
                });
            }
            _dashRegenRunning = false;
        }

        private Vector3 GetDashDirection()
        {
            float h = Input.GetAxisRaw("Horizontal");
            float v = Input.GetAxisRaw("Vertical");
            Transform cam = Camera.main.transform;
            Vector3 forward = Vector3.ProjectOnPlane(cam.forward, Vector3.up).normalized;
            Vector3 right = Vector3.ProjectOnPlane(cam.right, Vector3.up).normalized;
            Vector3 dir = forward * v + right * h;
            return dir.sqrMagnitude > 0.01f ? dir.normalized : forward;
        }

        // ════════════════════════════════════════════════════════
        //  GRAPPLE  (called by GrappleSystem)
        // ════════════════════════════════════════════════════════
        public void StartGrapple()
        {
            GrappleVelocity = Rb.linearVelocity;
            GrappleActive = true;
        }

        public void EndGrapple()
        {
            GrappleActive = false;
        }

        public void InjectVelocity(Vector3 v, int frames = 1)
        {
            if (!GrappleActive)
                Rb.AddForce(v, ForceMode.VelocityChange);
        }

        public void RedirectVelocity(Vector3 dir)
        {
            if (GrappleActive)
                GrappleVelocity = dir.normalized * GrappleVelocity.magnitude;
        }

#if UNITY_EDITOR
        private void OnGUI()
        {
            if (GameManager.Instance == null || !GameManager.Instance.DebugMode) return;
            GUILayout.Label($"State: {state}  Speed: {Speed:F1}  Grounded: {grounded}");
            GUILayout.Label($"Dash: {DashCharges}/{maxDashCharges}  IFrames: {IsInvincible}");
            GUILayout.Label($"Slide: {sliding}  Climb: {_isClimbing}  Grapple: {GrappleActive}");
        }
#endif
    }
}