using System;
using UnityEngine;

/// <summary>
/// Suicide FPV drone. Closes on the player with FlyingMovement, holds a short
/// telegraph (siren + blinking light) so the dive is readable, then takes
/// position control off FlyingMovement and rockets in on a turn-rate-limited
/// intercept — limited on purpose, so a sidestep at the right moment makes it
/// overshoot. Detonates on proximity, on contact, on dive timeout, and (if the
/// player shoots it down mid-dive) on death.
/// </summary>
[RequireComponent(typeof(EnemyBrain))]
[RequireComponent(typeof(FlyingMovement))]
public class KamikazeDroneBehaviour : MonoBehaviour, IEnemyAimController
{
    private enum Phase { Idle, Locking, Diving, Recovering }

    [Header("Engage")]
    [Tooltip("Distance at which the drone commits to a dive.")]
    public float LockRange = 25f;
    [Tooltip("Seconds of telegraph before the dive launches — the player's window to react.")]
    public float LockTime = 1.1f;
    [Tooltip("Layers that block line-of-sight to the player. Should NOT include the player's own layer.")]
    public LayerMask ObstacleLayers;

    [Header("Dive")]
    public float DiveSpeed = 26f;
    public float DiveAcceleration = 30f;
    [Tooltip("Degrees per second the drone can correct its heading mid-dive. Low values are dodgeable, high values are near-unavoidable.")]
    public float DiveTurnRate = 110f;
    [Tooltip("Seconds before an unsuccessful dive gives up and detonates in the air.")]
    public float MaxDiveTime = 3.5f;
    [Tooltip("Aim ahead of the player by this many seconds of their current velocity. 0 = pure pursuit.")]
    public float LeadTime = 0.15f;
    [Tooltip("Seconds spent flying normally after a missed dive before trying again.")]
    public float RecoverTime = 2f;

    [Header("Detonation")]
    [Tooltip("Proximity fuse — detonates once this close to the player.")]
    public float ProximityFuseRadius = 1.6f;
    public float ExplosionRadius = 5f;
    public float MaxDamage = 45f;
    [Tooltip("Damage dealt at the very edge of ExplosionRadius. Falls off linearly from MaxDamage at the centre.")]
    public float MinDamage = 12f;
    public float KnockbackForce = 12f;
    [Tooltip("Layers that trigger a contact detonation (walls, floors, the player). Leave the enemy layer out.")]
    public LayerMask DetonateOnLayers = ~0;
    [Tooltip("Also blow up when killed. Off = shooting it down is completely safe.")]
    public bool DetonateOnDeath = true;
    [Tooltip("Only detonate on death if it had already committed to a dive.")]
    public bool DeathDetonationRequiresDive = true;

    [Header("VFX / SFX")]
    public GameObject ExplosionVFXPrefab;
    public float ExplosionVFXLifetime = 3f;
    [Tooltip("Light or emissive renderer object toggled on/off during the telegraph.")]
    public GameObject WarningLight;
    public float WarningBlinkStartInterval = 0.25f;
    public float WarningBlinkEndInterval = 0.05f;
    [Tooltip("Looping siren played from lock-on until detonation. Pitch ramps up through the dive.")]
    public AudioSource SirenSource;
    public float SirenMaxPitch = 1.6f;

    public event Action OnLockStarted;
    public event Action OnDiveStarted;
    public event Action OnDetonated;

    // ── IEnemyAimController — outranks everything; a committed dive owns facing.
    public int AimPriority => 30;
    public bool WantsAim => _phase == Phase.Locking || _phase == Phase.Diving;

    public void TickAim(float deltaTime)
    {
        Vector3 dir = _phase == Phase.Diving
            ? _diveDirection
            : (GetAimPoint() - transform.position);

        if (dir.sqrMagnitude < 0.001f) return;

        // Full 3D facing — unlike ground enemies, a diving drone should pitch down.
        transform.rotation = Quaternion.Slerp(transform.rotation,
            Quaternion.LookRotation(dir.normalized), 12f * deltaTime);
    }

    private EnemyBrain _brain;
    private EnemyHealth _health;
    private FlyingMovement _flight;

    private Phase _phase = Phase.Idle;
    private float _phaseTimer;
    private float _blinkTimer;
    private bool _blinkOn;
    private Vector3 _diveDirection;
    private float _diveSpeed;
    private bool _detonated;

    private void Awake()
    {
        _brain = GetComponent<EnemyBrain>();
        _health = GetComponent<EnemyHealth>();
        _flight = GetComponent<FlyingMovement>();

        _brain.OnStateChanged += OnStateChanged;
        _health.OnDied += OnDied;

        SetWarningLight(false);
    }

    private void OnDestroy()
    {
        _brain.OnStateChanged -= OnStateChanged;
        _health.OnDied -= OnDied;
    }

    private void Update()
    {
        if (_brain.State == EnemyState.Dead) return;

        if (_brain.State == EnemyState.Staggered)
        {
            // A stagger shakes the drone off its attack run, but it keeps its fuse.
            if (_phase != Phase.Idle) AbortToRecover();
            return;
        }

        float dt = Time.deltaTime;

        switch (_phase)
        {
            case Phase.Idle: TickIdle(); break;
            case Phase.Locking: TickLocking(dt); break;
            case Phase.Diving: TickDiving(dt); break;
            case Phase.Recovering:
                _phaseTimer -= dt;
                if (_phaseTimer <= 0f) _phase = Phase.Idle;
                break;
        }
    }

    // ── Phases ──────────────────────────────────────────────────────────────
    private void TickIdle()
    {
        if (_brain.State != EnemyState.Aggro || PlayerHealth.Transform == null) return;

        float dist = Vector3.Distance(transform.position, PlayerHealth.Transform.position);
        if (dist > LockRange) return;
        if (!EnemyVision.HasLineOfSight(transform.position, PlayerHealth.Transform, LockRange, ObstacleLayers)) return;

        _phase = Phase.Locking;
        _phaseTimer = LockTime;
        _blinkTimer = 0f;

        if (SirenSource != null)
        {
            SirenSource.loop = true;
            SirenSource.pitch = 1f;
            SirenSource.Play();
        }

        OnLockStarted?.Invoke();
    }

    private void TickLocking(float dt)
    {
        if (PlayerHealth.Transform == null) { AbortToRecover(); return; }

        _phaseTimer -= dt;

        // Blink accelerates as the dive approaches — the readable "it's about to go" cue.
        float t = 1f - Mathf.Clamp01(_phaseTimer / Mathf.Max(LockTime, 0.01f));
        float interval = Mathf.Lerp(WarningBlinkStartInterval, WarningBlinkEndInterval, t);

        _blinkTimer -= dt;
        if (_blinkTimer <= 0f)
        {
            _blinkTimer = interval;
            _blinkOn = !_blinkOn;
            SetWarningLight(_blinkOn);
        }

        if (_phaseTimer <= 0f) StartDive();
    }

    private void StartDive()
    {
        _phase = Phase.Diving;
        _phaseTimer = MaxDiveTime;
        _diveSpeed = Mathf.Max(_flight.Velocity.magnitude, 4f);

        Vector3 toTarget = GetAimPoint() - transform.position;
        _diveDirection = toTarget.sqrMagnitude > 0.001f ? toTarget.normalized : transform.forward;

        _flight.MovementOverrideActive = true;
        SetWarningLight(true);

        OnDiveStarted?.Invoke();
    }

    private void TickDiving(float dt)
    {
        _phaseTimer -= dt;

        if (PlayerHealth.Transform != null)
        {
            Vector3 desired = (GetAimPoint() - transform.position);
            if (desired.sqrMagnitude > 0.001f)
            {
                // Turn-rate limited steering: this is what makes the dive dodgeable.
                _diveDirection = Vector3.RotateTowards(_diveDirection, desired.normalized,
                    DiveTurnRate * Mathf.Deg2Rad * dt, 0f).normalized;
            }
        }

        _diveSpeed = Mathf.MoveTowards(_diveSpeed, DiveSpeed, DiveAcceleration * dt);

        Vector3 step = _diveDirection * (_diveSpeed * dt);

        if (SirenSource != null)
            SirenSource.pitch = Mathf.Lerp(1f, SirenMaxPitch, Mathf.Clamp01(_diveSpeed / Mathf.Max(DiveSpeed, 0.01f)));

        // Sweep the frame's movement instead of trusting a trigger — at dive speed
        // a discrete overlap test tunnels straight through walls and the player.
        if (Physics.SphereCast(transform.position, ProximityFuseRadius, _diveDirection, out RaycastHit hit,
                               step.magnitude, DetonateOnLayers, QueryTriggerInteraction.Ignore)
            && !hit.collider.transform.IsChildOf(transform))
        {
            transform.position = hit.point - _diveDirection * ProximityFuseRadius;
            Detonate();
            return;
        }

        transform.position += step;

        if (PlayerHealth.Transform != null &&
            Vector3.Distance(transform.position, PlayerHealth.Transform.position) <= ProximityFuseRadius)
        {
            Detonate();
            return;
        }

        // Ran out of dive time, or the player dodged and the drone is now flying
        // away from them — either way, burn out rather than loop forever.
        if (_phaseTimer <= 0f) { Detonate(); return; }

        if (PlayerHealth.Transform != null)
        {
            Vector3 toPlayer = PlayerHealth.Transform.position - transform.position;
            if (toPlayer.magnitude > LockRange * 1.5f && Vector3.Dot(toPlayer.normalized, _diveDirection) < 0f)
                AbortToRecover();
        }
    }

    private void AbortToRecover()
    {
        _phase = Phase.Recovering;
        _phaseTimer = RecoverTime;

        _flight.MovementOverrideActive = false;
        _flight.SetVelocity(_diveDirection * Mathf.Min(_diveSpeed, _flight.ChaseSpeed));
        _flight.Reposition();

        _diveSpeed = 0f;
        SetWarningLight(false);
        if (SirenSource != null) SirenSource.Stop();
    }

    // ── Detonation ──────────────────────────────────────────────────────────
    private void Detonate()
    {
        if (_detonated) return;
        _detonated = true;

        _flight.MovementOverrideActive = false;
        SetWarningLight(false);
        if (SirenSource != null) SirenSource.Stop();

        Vector3 center = transform.position;

        if (ExplosionVFXPrefab != null)
            Destroy(Instantiate(ExplosionVFXPrefab, center, Quaternion.identity), ExplosionVFXLifetime);

        SoundManager.Instance?.PlaySFX(SFXType.Bomb, center);

        ApplyExplosionDamage(center);

        OnDetonated?.Invoke();

        // Route the drone's own destruction through EnemyHealth so death VFX,
        // drops and squad-avenge events all fire exactly as they do for a kill.
        if (_health.IsAlive)
            _health.TakeDamage(_health.CurrentHP, Vector3.up, center);
        else
            Destroy(gameObject);
    }

    private void ApplyExplosionDamage(Vector3 center)
    {
        if (PlayerHealth.Transform == null) return;

        float dist = Vector3.Distance(center, PlayerHealth.Transform.position + Vector3.up);
        if (dist > ExplosionRadius) return;

        float t = Mathf.Clamp01(dist / Mathf.Max(ExplosionRadius, 0.01f));
        PlayerHealth.Instance?.TakeDamage(Mathf.Lerp(MaxDamage, MinDamage, t));

        if (KnockbackForce > 0f && PlayerHealth.Transform.TryGetComponent(out Rigidbody rb))
        {
            Vector3 dir = (PlayerHealth.Transform.position - center).normalized + Vector3.up * 0.4f;
            rb.AddForce(dir.normalized * (KnockbackForce * (1f - t)), ForceMode.Impulse);
        }
    }

    // Contact fuse for anything the dive sweep didn't catch (something walking
    // into the drone while it hovers, physics pushing it into a wall).
    private void OnCollisionEnter(Collision collision)
    {
        if (_phase != Phase.Diving) return;
        if ((DetonateOnLayers.value & (1 << collision.gameObject.layer)) == 0) return;
        Detonate();
    }

    private void OnDied(Vector3 _)
    {
        if (!DetonateOnDeath) return;
        if (DeathDetonationRequiresDive && _phase != Phase.Diving && _phase != Phase.Locking) return;
        Detonate();
    }

    private void OnStateChanged(EnemyState state)
    {
        if (state == EnemyState.Idle && _phase == Phase.Locking) AbortToRecover();
    }

    // Predicts where the player will be at impact, so a strafing player isn't a
    // guaranteed miss. LeadTime 0 turns this back into pure pursuit.
    private Vector3 GetAimPoint()
    {
        if (PlayerHealth.Transform == null) return transform.position + transform.forward;

        Vector3 point = PlayerHealth.Transform.position + Vector3.up * 0.9f;

        if (LeadTime > 0f && PlayerHealth.Transform.TryGetComponent(out Rigidbody rb))
            point += rb.linearVelocity * LeadTime;

        return point;
    }

    private void SetWarningLight(bool on)
    {
        if (WarningLight != null) WarningLight.SetActive(on);
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(1f, 0.4f, 0f);
        Gizmos.DrawWireSphere(transform.position, LockRange);
        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(transform.position, ExplosionRadius);
    }
}
