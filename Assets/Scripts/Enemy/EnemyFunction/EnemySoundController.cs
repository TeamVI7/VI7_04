using UnityEngine;
using UnityEngine.AI;

[RequireComponent(typeof(EnemyHealth))]
public class EnemyAudio : MonoBehaviour
{
    [Header("Movement")]
    public AudioClip WalkClip;
    [Tooltip("Tốc độ tối thiểu để coi là đang di chuyển")]
    public float MinMoveSpeed = 0.1f;

    [Header("Weapons — Shoot Clips (randomized per shot)")]
    public AudioClip LaserClip;
    public AudioClip BombClip;
    [Tooltip("Multiple clank variations — one is picked at random per shot so bursts don't sound identical.")]
    public AudioClip[] SMGClips;
    [Tooltip("Multiple clank variations — one is picked at random per shot so bursts don't sound identical.")]
    public AudioClip[] PistolClips;

    [Header("Riot Shield")]
    [Tooltip("Played whenever RiotShieldBehaviour absorbs a ranged hit.")]
    public AudioClip ShieldBlockClip;
    [Tooltip("Played once when a melee hit destroys the shield.")]
    public AudioClip ShieldDestroyedClip;

    [Header("Shoot Sound Variation")]
    [Tooltip("Random pitch range applied to every gunfire one-shot, on top of clip variation, so even repeats of the same clip don't sound copy-pasted.")]
    public Vector2 ShootPitchRange = new Vector2(0.95f, 1.05f);

    [Header("Sniper Realistic Settings")]
    [Tooltip("Tiếng nổ tại nòng súng của quái (Có đuôi phiuuuu vang xa)")]
    public AudioClip SniperMuzzleClip; 
    [Tooltip("Tiếng nổ đanh/găm thép phát ngay tai Player khi đạn trúng đích")]
    public AudioClip SniperImpactClip; 
    [Tooltip("THÊM MỚI: Tiếng rít gió vút qua tai khi đạn bay sượt hụt Player")]
    public AudioClip SniperWhizClip; 

    [Header("Status")]
    public AudioClip DeathClip;
    [Tooltip("Played once the instant this enemy spots the player and enters Aggro.")]
    public AudioClip DetectClip;

    [Header("Melee")]
    [Tooltip("Played on the windup/swing of a melee attack (MeleeAttackBehaviour.OnAttackStarted).")]
    public AudioClip MeleeSwingClip;
    [Tooltip("Optional — played only if the melee swing actually connects (OnAttackLanded). Leave empty to skip.")]
    public AudioClip MeleeHitClip;

    [Header("Flight — Rotor Loop")]
    [Tooltip("Continuous propeller/thruster loop for airborne enemies. Pitch and volume scale with airspeed, so a diving drone audibly winds up.")]
    public AudioClip RotorLoopClip;
    [Tooltip("Rotor pitch at a standstill → at full chase speed. The kamikaze dive pushes past the top of this range.")]
    public Vector2 RotorPitchRange = new Vector2(0.8f, 1.25f);
    [Tooltip("Rotor volume at a standstill → at full chase speed. Idling flyers should still be audible, hence the non-zero floor.")]
    public Vector2 RotorVolumeRange = new Vector2(0.35f, 1f);

    [Header("Kamikaze Drone")]
    [Tooltip("One-shot the instant the drone locks on and starts its telegraph.")]
    public AudioClip DroneLockOnClip;
    [Tooltip("Looping siren from lock-on until detonation. Pitch/volume ramp with KamikazeDroneBehaviour.ThreatIntensity.")]
    public AudioClip DroneSirenLoopClip;
    public Vector2 DroneSirenPitchRange = new Vector2(1f, 1.7f);
    [Tooltip("One-shot as the dive launches — the 'it's coming' cue.")]
    public AudioClip DroneDiveClip;
    [Tooltip("Explosion. Played on a detached source because the drone destroys itself the same frame.")]
    public AudioClip DroneDetonateClip;
    [Tooltip("Played when a dodged dive gives up and the drone peels away to try again.")]
    public AudioClip DroneAbortClip;

    [Header("Flying Sniper")]
    [Tooltip("Looping charge whine while the laser tracks the player.")]
    public AudioClip FlyingSniperChargeLoopClip;
    [Tooltip("One-shot when the laser hardens into a lock — the last warning before the shot.")]
    public AudioClip FlyingSniperLockClip;
    [Tooltip("The shot itself. Falls back to SniperMuzzleClip if left empty.")]
    public AudioClip FlyingSniperShotClip;

    [Header("Squad")]
    [Tooltip("Played when this enemy is alerted/enraged by a nearby squadmate dying (EnemyAvengeReaction.OnAvengeTriggered).")]
    public AudioClip AvengeClip;

    [Header("3D Audio — Settings")]
    [Range(0f, 1f)] public float SpatialBlend = 1.0f;
    public float MinDistance = 5f;
    public float MaxDistance = 30f;

    private AudioSource _loopSource;
    private AudioSource _oneShotSource;
    private AudioSource _rotorSource;  // airborne enemies only — always-on propeller bed
    private AudioSource _alarmSource;  // kamikaze siren / sniper charge whine

    private EnemyHealth           _health;
    private EnemyBrain            _brain;
    private LaserBehaviour        _laser;
    private GrenadeBurstBehaviour _burst;
    private SMGAttackBehaviour    _smg;
    private PistolAttackBehaviour _pistol;
    private NavMeshAgent           _nav;
    private SniperAttackBehaviour _sniper;
    private MeleeAttackBehaviour  _melee;
    private RiotShieldBehaviour    _shield;
    private EnemyAvengeReaction    _avenge;
    private FlyingMovement         _flight;
    private KamikazeDroneBehaviour _drone;
    private FlyingSniperBehaviour  _flyingSniper;

    private bool _isAggro;
    private EnemyState _lastState = EnemyState.Idle;

    private void Awake()
    {
        _health = GetComponent<EnemyHealth>();
        _brain  = GetComponent<EnemyBrain>();
        _laser  = GetComponent<LaserBehaviour>();
        _burst  = GetComponent<GrenadeBurstBehaviour>();
        _smg    = GetComponent<SMGAttackBehaviour>();
        _pistol = GetComponent<PistolAttackBehaviour>();
        _sniper = GetComponent<SniperAttackBehaviour>();
        _nav    = GetComponent<NavMeshAgent>();
        _melee  = GetComponent<MeleeAttackBehaviour>();
        _shield  = GetComponentInChildren<RiotShieldBehaviour>(); // lives on the shield prop, not this root
        _avenge  = GetComponent<EnemyAvengeReaction>();

        _flight       = GetComponent<FlyingMovement>();
        _drone        = GetComponent<KamikazeDroneBehaviour>();
        _flyingSniper = GetComponent<FlyingSniperBehaviour>();

        _loopSource = gameObject.AddComponent<AudioSource>();
        Configure3D(_loopSource, loop: true);

        _oneShotSource = gameObject.AddComponent<AudioSource>();
        Configure3D(_oneShotSource, loop: false);

        // Only airborne enemies pay for these two extra sources.
        if (_flight != null)
        {
            _rotorSource = gameObject.AddComponent<AudioSource>();
            Configure3D(_rotorSource, loop: true);

            _alarmSource = gameObject.AddComponent<AudioSource>();
            Configure3D(_alarmSource, loop: true);
        }
    }

    private void Start()
    {
        _health.OnDied += HandleDeath;

        if (_brain != null)  _brain.OnStateChanged += HandleStateChanged;
        if (_laser != null)  _laser.OnLaserToggled += HandleLaserToggled;
        if (_burst != null)  _burst.OnBurstStarted += PlayBomb;
        if (_smg    != null) _smg.OnSMGFired       += PlaySMG;
        if (_pistol != null) _pistol.OnPistolFired += PlayPistol;
        if (_sniper != null) _sniper.OnSniperShot  += PlaySniper;

        if (_melee != null)
        {
            _melee.OnAttackStarted += PlayMeleeSwing;
            _melee.OnAttackLanded  += PlayMeleeHit;
        }

        if (_shield != null)
        {
            _shield.OnBlocked         += PlayShieldBlock;
            _shield.OnShieldDestroyed += PlayShieldDestroyed;
        }
        if (_avenge != null) _avenge.OnAvengeTriggered += PlayAvenge;

        if (_drone != null)
        {
            _drone.OnLockStarted += PlayDroneLockOn;
            _drone.OnDiveStarted += PlayDroneDive;
            _drone.OnDiveAborted += PlayDroneAbort;
            _drone.OnDetonated   += PlayDroneDetonation;
        }

        if (_flyingSniper != null)
        {
            _flyingSniper.OnChargeStarted += StartSniperChargeLoop;
            _flyingSniper.OnLockAcquired  += PlayFlyingSniperLock;
            _flyingSniper.OnShotFired     += PlayFlyingSniperShot;
        }

        if (RotorLoopClip != null && _rotorSource != null)
        {
            _rotorSource.clip = RotorLoopClip;
            _rotorSource.time = UnityEngine.Random.Range(0f, RotorLoopClip.length); // desync a swarm
            _rotorSource.Play();
        }
    }

    private void Update()
    {
        HandleWalking();
        HandleRotor();
        HandleAlarmLoop();
    }

    private void OnDestroy()
    {
        if (_health != null) _health.OnDied         -= HandleDeath;
        if (_brain  != null) _brain.OnStateChanged -= HandleStateChanged;
        if (_laser  != null) _laser.OnLaserToggled -= HandleLaserToggled;
        if (_burst  != null) _burst.OnBurstStarted -= PlayBomb;
        if (_smg    != null) _smg.OnSMGFired       -= PlaySMG;
        if (_pistol != null) _pistol.OnPistolFired -= PlayPistol;
        if (_sniper != null) _sniper.OnSniperShot  -= PlaySniper;

        if (_melee != null)
        {
            _melee.OnAttackStarted -= PlayMeleeSwing;
            _melee.OnAttackLanded  -= PlayMeleeHit;
        }

        if (_shield != null)
        {
            _shield.OnBlocked         -= PlayShieldBlock;
            _shield.OnShieldDestroyed -= PlayShieldDestroyed;
        }
        if (_avenge != null) _avenge.OnAvengeTriggered -= PlayAvenge;

        if (_drone != null)
        {
            _drone.OnLockStarted -= PlayDroneLockOn;
            _drone.OnDiveStarted -= PlayDroneDive;
            _drone.OnDiveAborted -= PlayDroneAbort;
            _drone.OnDetonated   -= PlayDroneDetonation;
        }

        if (_flyingSniper != null)
        {
            _flyingSniper.OnChargeStarted -= StartSniperChargeLoop;
            _flyingSniper.OnLockAcquired  -= PlayFlyingSniperLock;
            _flyingSniper.OnShotFired     -= PlayFlyingSniperShot;
        }
    }

    private void HandleWalking()
    {
        if (WalkClip == null || !_isAggro) return;
        bool isMoving = _nav != null && _nav.enabled && _nav.velocity.magnitude > MinMoveSpeed;

        if (isMoving && !_loopSource.isPlaying)
        {
            _loopSource.clip = WalkClip;
            _loopSource.Play();
        }
        else if (!isMoving && _loopSource.isPlaying)
        {
            _loopSource.Stop();
        }
    }

    // Rotor bed for airborne enemies — the flying counterpart of HandleWalking.
    // Unlike footsteps this plays even while idle (a hovering drone is never
    // silent) and rides airspeed, so a dive winds the pitch up on its own.
    private void HandleRotor()
    {
        if (_rotorSource == null || RotorLoopClip == null) return;

        if (!_health.IsAlive)
        {
            if (_rotorSource.isPlaying) _rotorSource.Stop();
            return;
        }

        // During a kamikaze dive FlyingMovement is overridden and reports no
        // velocity, so ThreatIntensity stands in for the speed the drone is
        // actually doing — otherwise the rotor would drop to idle mid-dive.
        float speed01 = _flight != null
            ? Mathf.Clamp01(_flight.Velocity.magnitude / Mathf.Max(_flight.ChaseSpeed, 0.01f))
            : 0f;

        if (_drone != null) speed01 = Mathf.Max(speed01, _drone.ThreatIntensity);

        _rotorSource.pitch  = Mathf.Lerp(RotorPitchRange.x,  RotorPitchRange.y,  speed01);
        _rotorSource.volume = Mathf.Lerp(RotorVolumeRange.x, RotorVolumeRange.y, speed01);
    }

    // One looping "something bad is about to happen" channel, shared by the
    // kamikaze siren and the flying sniper's charge whine — no enemy carries both.
    private void HandleAlarmLoop()
    {
        if (_alarmSource == null) return;

        if (_drone != null)
        {
            bool armed = _health.IsAlive && _drone.ThreatIntensity > 0f;

            if (armed && DroneSirenLoopClip != null)
            {
                if (!_alarmSource.isPlaying)
                {
                    _alarmSource.clip = DroneSirenLoopClip;
                    _alarmSource.Play();
                }
                _alarmSource.pitch = Mathf.Lerp(DroneSirenPitchRange.x, DroneSirenPitchRange.y, _drone.ThreatIntensity);
            }
            else if (!armed && _alarmSource.isPlaying)
            {
                _alarmSource.Stop();
            }
            return;
        }

        // Polls IsAiming rather than trusting a stop event, so a shot skipped for
        // being off-angle can't leave the charge whine looping forever.
        if (_flyingSniper != null && _alarmSource.isPlaying && !_flyingSniper.IsAiming)
            _alarmSource.Stop();
    }

    private void HandleStateChanged(EnemyState state)
    {
        _isAggro = state == EnemyState.Aggro;

        // Fires once, the instant this enemy spots the player and enters Aggro
        // from Idle. Deliberately checks _lastState (not just "wasn't aggro")
        // so the Staggered -> Aggro recovery bounce doesn't replay the detect
        // bark — the enemy was already engaged before it got staggered.
        if (state == EnemyState.Aggro && _lastState == EnemyState.Idle)
            PlayDetect();

        if (!_isAggro) 
        {
            _loopSource.Stop();
            if (state == EnemyState.Staggered || state == EnemyState.Dead)
            {
                _oneShotSource.Stop(); 
            }
        }

        _lastState = state;
    }

    private void HandleLaserToggled(bool isOn)
    {
        if (isOn && LaserClip != null && !_oneShotSource.isPlaying)
        {
            _oneShotSource.clip = LaserClip;
            _oneShotSource.Play();
        }
        else if (!isOn)
        {
            _oneShotSource.Stop();
        }
    }

    private void PlayBomb() { if (BombClip != null) _oneShotSource.PlayOneShot(BombClip); }

    private void PlaySMG()    => PlayRandomShootClip(SMGClips);
    private void PlayPistol() => PlayRandomShootClip(PistolClips);

    /// <summary>Picks a random clip from the array and plays it with a random pitch,
    /// so repeated fire (SMG bursts, shotgun blasts) doesn't sound copy-pasted.</summary>
    private void PlayRandomShootClip(AudioClip[] clips)
    {
        if (clips == null || clips.Length == 0) return;

        var clip = clips[UnityEngine.Random.Range(0, clips.Length)];
        if (clip == null) return;

        _oneShotSource.pitch = UnityEngine.Random.Range(ShootPitchRange.x, ShootPitchRange.y);
        _oneShotSource.PlayOneShot(clip);
    }

    private void PlayDetect()
    {
        if (DetectClip == null) return;
        if (!EnemySquadCoordinator.TryPlayDetectBark()) return; // squad gate, cooldown 0.35s
        _oneShotSource.PlayOneShot(DetectClip);
    }
    private void PlayMeleeSwing() { if (MeleeSwingClip != null) _oneShotSource.PlayOneShot(MeleeSwingClip); }
    private void PlayMeleeHit() { if (MeleeHitClip != null) _oneShotSource.PlayOneShot(MeleeHitClip); }

    private void PlayShieldBlock() { if (ShieldBlockClip != null) _oneShotSource.PlayOneShot(ShieldBlockClip); }
    private void PlayShieldDestroyed() { if (ShieldDestroyedClip != null) _oneShotSource.PlayOneShot(ShieldDestroyedClip); }
    private void PlayAvenge() { if (AvengeClip != null) _oneShotSource.PlayOneShot(AvengeClip); }

    private void PlaySniper() 
    { 
        if (SniperMuzzleClip != null) 
        {
            _oneShotSource.PlayOneShot(SniperMuzzleClip); 
        }

        Vector3 fireOrigin = _sniper != null && _sniper.FirePoint != null 
            ? _sniper.FirePoint.position 
            : transform.position + Vector3.up * 1.5f;

        Collider[] hitProjectiles = Physics.OverlapSphere(fireOrigin, 1.5f);
        foreach (var col in hitProjectiles)
        {
            var projectile = col.GetComponent<Enemy.SniperProjectile>();
            if (projectile != null)
            {
                // THÊM MỚI: Tiêm đồng thời cả Sound nổ Impact và Sound rít gió Whiz vào đạn
                projectile.AssignSniperClips(SniperImpactClip, SniperWhizClip);
                break; 
            }
        }
    }

    // ── Kamikaze drone ───────────────────────────────────────────────────────
    private void PlayDroneLockOn() { if (DroneLockOnClip != null) _oneShotSource.PlayOneShot(DroneLockOnClip); }
    private void PlayDroneDive()   { if (DroneDiveClip   != null) _oneShotSource.PlayOneShot(DroneDiveClip); }
    private void PlayDroneAbort()  { if (DroneAbortClip  != null) _oneShotSource.PlayOneShot(DroneAbortClip); }

    // The drone destroys itself on the same frame it detonates, so the explosion
    // has to outlive it on a detached source — same trick HandleDeath uses.
    private void PlayDroneDetonation()
    {
        _rotorSource?.Stop();
        _alarmSource?.Stop();

        if (DroneDetonateClip == null) return;
        PlayDetached(DroneDetonateClip, $"{gameObject.name}_Detonation");
    }

    // ── Flying sniper ────────────────────────────────────────────────────────
    private void StartSniperChargeLoop()
    {
        if (_alarmSource == null || FlyingSniperChargeLoopClip == null) return;

        _alarmSource.clip  = FlyingSniperChargeLoopClip;
        _alarmSource.pitch = 1f;
        _alarmSource.Play();
    }

    private void PlayFlyingSniperLock()
    {
        if (FlyingSniperLockClip != null) _oneShotSource.PlayOneShot(FlyingSniperLockClip);
    }

    private void PlayFlyingSniperShot()
    {
        _alarmSource?.Stop();

        var clip = FlyingSniperShotClip != null ? FlyingSniperShotClip : SniperMuzzleClip;
        if (clip != null) _oneShotSource.PlayOneShot(clip);
    }

    private void PlayDetached(AudioClip clip, string objectName)
    {
        var go = new GameObject(objectName);
        go.transform.position = transform.position;

        var src = go.AddComponent<AudioSource>();
        Configure3D(src, loop: false);
        src.clip = clip;
        src.Play();

        Destroy(go, clip.length + 0.1f);
    }

    private void HandleDeath(Vector3 impulse)
    {
        _loopSource.Stop();
        _oneShotSource.Stop();
        _rotorSource?.Stop();
        _alarmSource?.Stop();

        if (DeathClip == null) return;

        // Detached so it survives the enemy's despawn. 3D, not 2D — a 2D source
        // blasts at full volume regardless of distance and drowns out everything else.
        PlayDetached(DeathClip, $"{gameObject.name}_Death");
    }

    private void Configure3D(AudioSource src, bool loop)
    {
        src.playOnAwake = false;
        src.loop         = loop;
        src.spatialBlend = SpatialBlend;
        src.rolloffMode = AudioRolloffMode.Linear;
        src.minDistance = MinDistance;
        src.maxDistance = MaxDistance;
        src.dopplerLevel = 0f;
    }
}