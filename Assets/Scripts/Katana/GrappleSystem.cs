// ============================================================
//  GrappleSystem.cs  —  Out of Bullet
//  Spring-joint grapple adapted for CharacterController.
//  Simulates SpringJoint math (spring + damper) manually and
//  feeds the resulting velocity into CC.Move() each frame.
//  Feels like the classic SpringJoint grapple without Rigidbody.
// ============================================================
using System.Collections;
using UnityEngine;
using OutOfBullet.Core;
using OutOfBullet.Enemy;
using OutOfBullet.Player;

namespace OutOfBullet.Player
{
    public class GrappleSystem : MonoBehaviour
    {
        [Header("References")]
        [Tooltip("Tip of the Katana — rope draws from here.")]
        public Transform GunTip;
        public LayerMask GrappleLayers;
        public LayerMask EnemyLayers;

        [Header("Spring Settings  (tune these for feel)")]
        public float Spring      = 8f;    // stiffness — higher = snappier pull
        public float Damper      = 4f;    // resistance — higher = less oscillation
        public float MassScale   = 2f;    // how much spring affects velocity
        public float MaxDistance = 25f;
        [Tooltip("Rope tries to pull player to this fraction of hit distance.")]
        [Range(0.1f, 1f)]
        public float TargetDistanceFraction = 0.4f;

        [Header("Aim Assist")]
        public float AimAssistAngle = 15f;
        public bool  AimAssistEnabled = true;

        [Header("Cooldown")]
        public float PenaltyCooldown = 3f;

        [Header("Input")]
        public KeyCode GrappleKey = KeyCode.Mouse1;

        [Header("Rope Visual")]
        public LineRenderer Lr;
        private Vector3 _currentRopeEnd;   // lerped for rope animation

        // ── Runtime ──────────────────────────────────────────────
        public bool  IsGrappling   { get; private set; }
        public bool  IsOnCooldown  { get; private set; }
        public float CooldownFraction => Mathf.Clamp01(_cooldownTimer / PenaltyCooldown);

        private PlayerController _pc;
        private Vector3             _grapplePoint;
        private EnemyBase           _anchoredEnemy;

        // Spring simulation state
        private Vector3 _velocity;        // our own velocity accumulator during grapple
        private float   _currentLength;   // current rope length
        private float   _targetLength;    // length the spring tries to reach

        private float   _cooldownTimer;
        private Coroutine _cooldownRoutine;

        // ── Unity ────────────────────────────────────────────────
        private void Awake()
        {
            _pc = GetComponent<PlayerController>();
        }

        private void OnEnable()
        {
            EventBus.Subscribe<EnemyExecutedEvent>(OnEnemyExecuted);
        }

        private void OnDisable()
        {
            EventBus.Unsubscribe<EnemyExecutedEvent>(OnEnemyExecuted);
            if (IsGrappling) StopGrapple(kill: false);
        }

        private void Update()
        {
            HandleInput();
            if (IsGrappling) SimulateSpring();
            DrawRope();
        }

        // ── Input ────────────────────────────────────────────────
        private void HandleInput()
        {
            if (Input.GetKeyDown(GrappleKey) && !IsOnCooldown && !IsGrappling)
                TryStartGrapple();

            if (Input.GetKeyUp(GrappleKey) && IsGrappling)
                StopGrapple(kill: false);
        }

        // ── Fire ─────────────────────────────────────────────────
        private void TryStartGrapple()
        {
            Camera cam = Camera.main;
            Ray    ray = cam.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));

            // Aim assist — find best enemy in cone
            if (AimAssistEnabled)
            {
                EnemyBase best = BestEnemyInCone(ray);
                if (best != null) { Latch(best.transform.position, best); return; }
            }

            // Straight raycast fallback
            if (Physics.Raycast(ray, out RaycastHit hit, MaxDistance, GrappleLayers))
            {
                EnemyBase enemy = hit.collider.GetComponentInParent<EnemyBase>();
                Latch(hit.point, enemy);
            }
            else
            {
                Miss();
            }
        }

        private void Latch(Vector3 point, EnemyBase enemy)
        {
            _grapplePoint   = point;
            _anchoredEnemy  = enemy;

            float dist      = Vector3.Distance(transform.position, _grapplePoint);
            _targetLength   = dist * TargetDistanceFraction;
            _currentLength  = dist;

            // Seed velocity from current player velocity for momentum continuity
            _velocity = _pc.Rb.linearVelocity;

            IsGrappling = true;
            _pc.StartGrapple();

            // Rope animation
            _currentRopeEnd = GunTip != null ? GunTip.position : transform.position;
            if (Lr != null) Lr.positionCount = 2;

            EventBus.Publish(new GrappleLandedEvent
            {
                AnchorTransform = enemy != null ? enemy.transform : null,
                IsEnemy         = enemy != null
            });

            GameManager.Instance?.DebugLog(
                $"[Grapple] Latched — dist:{dist:F1}  target:{_targetLength:F1}");
        }

        private void Miss()
        {
            EventBus.Publish(new GrappleMissedEvent());
            StartPenaltyCooldown();
        }

        // ── Spring simulation ────────────────────────────────────
        // Mirrors SpringJoint behaviour:
        //   force = spring * (currentLength - targetLength) - damper * radialVelocity
        // Applied to our own velocity accumulator, then fed into CC.Move().
        private void SimulateSpring()
        {
            // Update grapple point if attached to a moving enemy
            if (_anchoredEnemy != null && _anchoredEnemy.IsAlive)
                _grapplePoint = _anchoredEnemy.transform.position;

            Vector3 toPoint  = _grapplePoint - transform.position;
            _currentLength   = toPoint.magnitude;
            Vector3 dir      = toPoint.normalized;

            // Spring force: pulls when too far, pushes when too close
            float   stretch      = _currentLength - _targetLength;
            float   radialVel    = Vector3.Dot(_velocity, dir);  // velocity toward anchor
            float   springForce  = (stretch * Spring - radialVel * Damper) * MassScale;

            _velocity += dir * springForce * Time.deltaTime;

            // Gravity
            _velocity += Physics.gravity * Time.deltaTime;

            // Move the CC
            _pc.GrappleVelocity = _velocity;
            // PlayerController.FixedUpdate drives Rb.linearVelocity = GrappleVelocity

            if (_pc.IsGrounded && _velocity.y < 0f)
                _velocity.y = -2f;

            // Arrival check
            if (_currentLength <= 1.8f && _anchoredEnemy != null && _anchoredEnemy.IsAlive)
                StopGrapple(kill: true);
        }

        // ── Stop ─────────────────────────────────────────────────
        private void StopGrapple(bool kill)
        {
            if (!IsGrappling) return;
            IsGrappling = false;

            if (Lr != null) Lr.positionCount = 0;

            if (kill && _anchoredEnemy != null && _anchoredEnemy.IsAlive)
            {
                _anchoredEnemy.TriggerExecute(_pc);
                // Cooldown reset fires via OnEnemyExecuted
            }
            else
            {
                StartPenaltyCooldown();
            }

            _pc.EndGrapple();
            _anchoredEnemy = null;

            GameManager.Instance?.DebugLog(
                $"[Grapple] Stopped — kill:{kill}  speed:{_pc.Rb.linearVelocity.magnitude:F1}");
        }

        // ── Rope draw (lerped tip like the original) ─────────────
        private void DrawRope()
        {
            if (Lr == null || !IsGrappling) return;

            _currentRopeEnd = Vector3.Lerp(_currentRopeEnd, _grapplePoint, Time.deltaTime * 12f);

            Vector3 origin = GunTip != null ? GunTip.position : transform.position + Vector3.up * 1.5f;
            Lr.SetPosition(0, origin);
            Lr.SetPosition(1, _currentRopeEnd);
        }

        // ── Aim assist ───────────────────────────────────────────
        private EnemyBase BestEnemyInCone(Ray ray)
        {
            Collider[] hits     = Physics.OverlapSphere(transform.position, MaxDistance, EnemyLayers);
            EnemyBase  best     = null;
            float      bestAngle = AimAssistAngle;

            foreach (var col in hits)
            {
                EnemyBase e = col.GetComponentInParent<EnemyBase>();
                if (e == null || !e.IsAlive) continue;

                Vector3 toEnemy = (col.transform.position - transform.position).normalized;
                float   angle   = Vector3.Angle(ray.direction, toEnemy);

                if (angle < bestAngle) { bestAngle = angle; best = e; }
            }
            return best;
        }

        // ── Cooldown ─────────────────────────────────────────────
        public void ResetCooldownInstant()
        {
            if (_cooldownRoutine != null) StopCoroutine(_cooldownRoutine);
            _cooldownTimer = 0f;
            IsOnCooldown   = false;
            EventBus.Publish(new GrappleCooldownEndedEvent());
        }

        private void StartPenaltyCooldown()
        {
            if (_cooldownRoutine != null) StopCoroutine(_cooldownRoutine);
            _cooldownRoutine = StartCoroutine(CooldownRoutine());
        }

        private IEnumerator CooldownRoutine()
        {
            IsOnCooldown   = true;
            _cooldownTimer = 0f;
            EventBus.Publish(new GrappleCooldownStartedEvent { Duration = PenaltyCooldown });
            while (_cooldownTimer < PenaltyCooldown)
            {
                _cooldownTimer += Time.deltaTime;
                yield return null;
            }
            IsOnCooldown = false;
            EventBus.Publish(new GrappleCooldownEndedEvent());
        }

        private void OnEnemyExecuted(EnemyExecutedEvent evt) => ResetCooldownInstant();

#if UNITY_EDITOR
        private void OnDrawGizmosSelected()
        {
            Gizmos.color = IsGrappling ? Color.yellow : (IsOnCooldown ? Color.red : Color.green);
            if (Camera.main)
                Gizmos.DrawRay(Camera.main.transform.position,
                    Camera.main.transform.forward * MaxDistance);

            if (IsGrappling)
            {
                Gizmos.color = Color.yellow;
                Gizmos.DrawLine(transform.position, _grapplePoint);
                Gizmos.DrawWireSphere(_grapplePoint, 0.3f);
            }
        }

        private void OnGUI()
        {
            if (GameManager.Instance == null || !GameManager.Instance.DebugMode) return;
            GUILayout.BeginArea(new Rect(10, 470, 260, 80));
            GUILayout.Label($"Grapple: {(IsGrappling ? $"ACTIVE  len:{_currentLength:F1}/{_targetLength:F1}" : "idle")}");
            GUILayout.Label($"CD: {(IsOnCooldown ? $"{_cooldownTimer:F1}/{PenaltyCooldown}s" : "READY")}");
            GUILayout.Label($"Vel: {_velocity.magnitude:F1} m/s");
            GUILayout.EndArea();
        }
#endif
    }
}