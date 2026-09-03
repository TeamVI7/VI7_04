// MechBossAudio.cs
// The mech's voice. Same shape as EnemyAudio (see EnemySoundController.cs): one
// component holding every clip, subscribing to the gameplay events the boss
// already raises, so no attack script has to know anything about sound.
//
// Four channels rather than one source, because the mech does several things at
// once and a single AudioSource means each new cue cuts the last one off:
//
//   body    — servo/locomotion bed, rides the agent's real speed
//   weapon  — the gatling's sustained fire, so it can't be cut by a footstep
//   charge  — sustained telegraphs (laser wind-up, barrage pod doors)
//   oneShot — everything punctual: impacts, launches, phase-ups
//
// Every clip is optional. An unassigned clip is simply silent — the boss works
// exactly as before with none of them filled in.
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

[RequireComponent(typeof(MechBossBrain))]
public class MechBossAudio : MonoBehaviour
{
    #region Inspector

    [Header("Intro")]
    [Tooltip("Rising cue as the drop begins — plays under the cutscene while the mech is still falling.")]
    public AudioClip IntroDropClip;
    [Tooltip("The landing. Played detached so a long tail isn't cut short by anything the boss does next.")]
    public AudioClip IntroImpactClip;

    [Header("Locomotion")]
    [Tooltip("Continuous servo/hydraulic bed while the mech is alive. Pitch and volume ride its actual ground speed, so a phase-3 charge audibly winds up.")]
    public AudioClip ServoLoopClip;
    public Vector2 ServoPitchRange = new Vector2(0.85f, 1.2f);
    [Tooltip("Volume standing still → at full chase speed. The floor is non-zero on purpose: a mech this size is never silent.")]
    public Vector2 ServoVolumeRange = new Vector2(0.25f, 1f);
    [Tooltip("Footfalls. Hook AnimEvent_Footstep() on the walk clip's contact frames — one is picked at random per step. Without the animation events these never play.")]
    public AudioClip[] FootstepClips;

    [Header("Stomp")]
    [Tooltip("Hydraulic wind-up as the leg comes up.")]
    public AudioClip StompWindupClip;
    [Tooltip("The slam. Played on the one-shot channel at the moment damage lands.")]
    public AudioClip StompImpactClip;

    [Header("Earth Spike")]
    public AudioClip SpikeWindupClip;
    [Tooltip("The ground slam that starts the pattern.")]
    public AudioClip SpikeSlamClip;

    [Header("Dash")]
    [Tooltip("Thruster burst as the mech launches, after the wind-up.")]
    public AudioClip DashLaunchClip;
    [Tooltip("Only plays if the dash actually connects.")]
    public AudioClip DashImpactClip;

    [Header("Gatling")]
    public AudioClip GatlingSpinUpClip;
    [Tooltip("Sustained fire loop. Preferred when set — it's steadier than per-shot clips at a fast fire rate.")]
    public AudioClip GatlingFireLoopClip;
    [Tooltip("Used only when there's no fire loop: one is picked at random per shot, with a pitch jitter so a burst doesn't sound copy-pasted.")]
    public AudioClip[] GatlingShotClips;
    [Tooltip("Spin-down as the barrels stop.")]
    public AudioClip GatlingSpinDownClip;

    [Header("Missiles")]
    [Tooltip("Pod doors opening — the barrage's telegraph.")]
    public AudioClip MissilePodOpenClip;
    [Tooltip("Per missile leaving the rack. Pitch-jittered so a 14-missile volley doesn't sound like one clip stuttering.")]
    public AudioClip MissileLaunchClip;

    [Header("Laser Ultimate")]
    [Tooltip("Looping charge whine, held for the whole wind-up.")]
    public AudioClip LaserChargeLoopClip;
    [Tooltip("One-shot the instant the beams come on.")]
    public AudioClip LaserFireClip;
    [Tooltip("Looping hum while the beams sweep.")]
    public AudioClip LaserSweepLoopClip;
    public AudioClip LaserEndClip;

    [Header("Reactions")]
    [Tooltip("Played on each phase transition — the boss escalating.")]
    public AudioClip PhaseUpClip;
    [Tooltip("Played when the boss is staggered.")]
    public AudioClip StaggerClip;
    [Tooltip("Played when the weakpoint opens — the cue that tells the player to close in.")]
    public AudioClip WeakpointExposedClip;
    [Tooltip("Played when the weakpoint seals again.")]
    public AudioClip WeakpointClosedClip;
    [Tooltip("Death. Detached, so it survives the boss's despawn.")]
    public AudioClip DeathClip;

    [Header("Variation")]
    [Tooltip("Random pitch range applied to repeated one-shots (footsteps, gatling shots, missile launches) so runs of them don't sound identical.")]
    public Vector2 PitchJitter = new Vector2(0.94f, 1.06f);

    [Header("3D Audio — Settings")]
    [Range(0f, 1f)] public float SpatialBlend = 1f;
    [Tooltip("A boss is heard across the arena, so these are much wider than a regular enemy's.")]
    public float MinDistance = 12f;
    public float MaxDistance = 90f;

    #endregion

    private AudioSource _body;
    private AudioSource _weapon;
    private AudioSource _charge;
    private AudioSource _oneShot;

    private MechBossBrain _brain;
    private EnemyHealth _health;
    private NavMeshAgent _nav;
    private MechChaseBehaviour _chase;
    private MechWeakpoint _weakpoint;

    private MechStompAttack _stomp;
    private MechEarthSpikeAttack _spike;
    private MechDashAttack _dash;
    private MechGatlingAttack _gatling;
    private MechMissileAttack _missile;
    private MechMissileBarrageAttack _barrage;
    private MechLaserSweepUltimate _laser;

    private float _topSpeed = 1f;

    #region Unity Lifecycle

    private void Awake()
    {
        _brain = GetComponent<MechBossBrain>();
        _health = GetComponent<EnemyHealth>();
        _nav = GetComponent<NavMeshAgent>();
        _chase = GetComponent<MechChaseBehaviour>();
        _weakpoint = GetComponentInChildren<MechWeakpoint>(true);

        _stomp = GetComponent<MechStompAttack>();
        _spike = GetComponent<MechEarthSpikeAttack>();
        _dash = GetComponent<MechDashAttack>();
        _gatling = GetComponent<MechGatlingAttack>();
        _missile = GetComponent<MechMissileAttack>();
        _barrage = GetComponent<MechMissileBarrageAttack>();
        _laser = GetComponent<MechLaserSweepUltimate>();

        _body = AddSource(loop: true);
        _weapon = AddSource(loop: true);
        _charge = AddSource(loop: true);
        _oneShot = AddSource(loop: false);

        // The phase multipliers push the agent well past its authored chase speed,
        // so the servo bed is normalised against the fastest it will ever go —
        // otherwise it pins at full volume from phase 2 onward.
        if (_chase != null)
        {
            _topSpeed = _chase.chaseSpeed * Mathf.Max(1f, Mathf.Max(_chase.phase2SpeedMultiplier, _chase.phase3SpeedMultiplier));
        }
        else if (_nav != null)
        {
            _topSpeed = Mathf.Max(0.01f, _nav.speed);
        }

        Subscribe();

        if (ServoLoopClip != null)
        {
            _body.clip = ServoLoopClip;
            _body.volume = ServoVolumeRange.x;
            _body.Play();
        }
    }

    // Awake, not Start. MechBossBrain kicks the intro off from its own Start and
    // Co_Intro raises OnIntroStart on its very first line — synchronously, inside
    // that StartCoroutine call. Subscribing from Start here would miss the drop cue
    // entirely whenever the brain's Start happened to run first.
    private void Subscribe()
    {
        _brain.OnIntroStart += PlayIntroDrop;
        _brain.OnIntroImpact += PlayIntroImpact;
        _brain.OnPhaseChanged += HandlePhaseChanged;
        _brain.OnStateChanged += HandleStateChanged;

        if (_health != null) _health.OnDied += HandleDeath;

        if (_weakpoint != null)
        {
            _weakpoint.OnWeakpointExposed += PlayWeakpointExposed;
            _weakpoint.OnWeakpointClosed += PlayWeakpointClosed;
        }

        if (_stomp != null)
        {
            _stomp.OnTelegraphStart += PlayStompWindup;
            _stomp.OnTelegraphResolved += PlayStompImpact;
        }

        if (_spike != null)
        {
            _spike.OnTelegraphStart += PlaySpikeWindup;
            _spike.OnSlam += PlaySpikeSlam;
        }

        if (_dash != null)
        {
            _dash.OnDashStarted += PlayDashLaunch;
            _dash.OnDashImpact += PlayDashImpact;
        }

        if (_gatling != null)
        {
            _gatling.OnSpinUpStarted += PlayGatlingSpinUp;
            _gatling.OnFireStarted += StartGatlingFire;
            _gatling.OnShotFired += PlayGatlingShot;
            _gatling.OnFireStopped += StopGatlingFire;
        }

        if (_missile != null) _missile.OnMissileLaunched += PlayMissileLaunch;

        if (_barrage != null)
        {
            _barrage.OnVolleyStarted += PlayPodsOpen;
            _barrage.OnMissileLaunched += PlayMissileLaunch;
            _barrage.OnVolleyEnded += StopCharge;
        }

        if (_laser != null)
        {
            // UnityEvents rather than C# events — the laser already exposes its
            // beats that way for the camera/VFX hooks, so audio rides the same ones.
            // Null-checked because a UnityEvent field is only guaranteed non-null on
            // a deserialized component, not one added at runtime.
            _laser.onChargeStart?.AddListener(StartLaserCharge);
            _laser.onSweepStart?.AddListener(StartLaserSweep);
            _laser.onSweepEnd?.AddListener(EndLaserSweep);
        }
    }

    private void OnDestroy()
    {
        _brain.OnIntroStart -= PlayIntroDrop;
        _brain.OnIntroImpact -= PlayIntroImpact;
        _brain.OnPhaseChanged -= HandlePhaseChanged;
        _brain.OnStateChanged -= HandleStateChanged;

        if (_health != null) _health.OnDied -= HandleDeath;

        if (_weakpoint != null)
        {
            _weakpoint.OnWeakpointExposed -= PlayWeakpointExposed;
            _weakpoint.OnWeakpointClosed -= PlayWeakpointClosed;
        }

        if (_stomp != null)
        {
            _stomp.OnTelegraphStart -= PlayStompWindup;
            _stomp.OnTelegraphResolved -= PlayStompImpact;
        }

        if (_spike != null)
        {
            _spike.OnTelegraphStart -= PlaySpikeWindup;
            _spike.OnSlam -= PlaySpikeSlam;
        }

        if (_dash != null)
        {
            _dash.OnDashStarted -= PlayDashLaunch;
            _dash.OnDashImpact -= PlayDashImpact;
        }

        if (_gatling != null)
        {
            _gatling.OnSpinUpStarted -= PlayGatlingSpinUp;
            _gatling.OnFireStarted -= StartGatlingFire;
            _gatling.OnShotFired -= PlayGatlingShot;
            _gatling.OnFireStopped -= StopGatlingFire;
        }

        if (_missile != null) _missile.OnMissileLaunched -= PlayMissileLaunch;

        if (_barrage != null)
        {
            _barrage.OnVolleyStarted -= PlayPodsOpen;
            _barrage.OnMissileLaunched -= PlayMissileLaunch;
            _barrage.OnVolleyEnded -= StopCharge;
        }

        if (_laser != null)
        {
            _laser.onChargeStart?.RemoveListener(StartLaserCharge);
            _laser.onSweepStart?.RemoveListener(StartLaserSweep);
            _laser.onSweepEnd?.RemoveListener(EndLaserSweep);
        }
    }

    private void Update() => UpdateServoBed();

    #endregion

    #region Locomotion

    /// <summary>Rides the agent's real ground speed, the same way EnemyAudio's rotor
    /// bed rides a flyer's airspeed — so the mech sounds like it's working harder
    /// when it actually is, without any per-phase audio tuning.</summary>
    private void UpdateServoBed()
    {
        if (_body == null || ServoLoopClip == null) return;

        if (_health != null && !_health.IsAlive)
        {
            if (_body.isPlaying) _body.Stop();
            return;
        }

        float speed = (_nav != null && _nav.enabled && _nav.isOnNavMesh)
            ? Vector3.ProjectOnPlane(_nav.velocity, Vector3.up).magnitude
            : 0f;

        float speed01 = Mathf.Clamp01(speed / Mathf.Max(0.01f, _topSpeed));
        _body.pitch = Mathf.Lerp(ServoPitchRange.x, ServoPitchRange.y, speed01);
        _body.volume = Mathf.Lerp(ServoVolumeRange.x, ServoVolumeRange.y, speed01);
    }

    /// <summary>Hook this on the walk clip's foot-contact frames.</summary>
    public void AnimEvent_Footstep() => PlayRandom(FootstepClips);

    #endregion

    #region Intro / State

    private void PlayIntroDrop() => PlayOneShot(IntroDropClip);

    // Detached: the impact tail is long and the boss immediately starts doing
    // other things on the one-shot channel.
    private void PlayIntroImpact() => PlayDetached(IntroImpactClip, "MechImpact");

    private void HandlePhaseChanged(int prev, int next) => PlayOneShot(PhaseUpClip);

    private void HandleStateChanged(MechBossState prev, MechBossState next)
    {
        if (next != MechBossState.Staggered) return;

        // The brain aborts every running attack on entering Staggered, so the
        // sustained channels have to come down with them or the gatling keeps
        // roaring over a boss that's stopped firing.
        StopSustained();
        PlayOneShot(StaggerClip);
    }

    private void HandleDeath(Vector3 impulse)
    {
        StopSustained();
        _body.Stop();
        _oneShot.Stop();

        PlayDetached(DeathClip, "MechDeath");
    }

    #endregion

    #region Attacks

    private void PlayStompWindup() => PlayOneShot(StompWindupClip);
    private void PlayStompImpact() => PlayOneShot(StompImpactClip);

    private void PlaySpikeWindup() => PlayOneShot(SpikeWindupClip);
    private void PlaySpikeSlam(MechEarthSpikeAttack.SpikePattern pattern) => PlayOneShot(SpikeSlamClip);

    private void PlayDashLaunch() => PlayOneShot(DashLaunchClip);
    private void PlayDashImpact() => PlayOneShot(DashImpactClip);

    private void PlayGatlingSpinUp() => PlayOneShot(GatlingSpinUpClip);

    private void StartGatlingFire()
    {
        if (GatlingFireLoopClip == null) return; // per-shot clips instead
        _weapon.clip = GatlingFireLoopClip;
        _weapon.pitch = 1f;
        _weapon.Play();
    }

    // Only used when there's no fire loop — otherwise the loop already covers it
    // and layering both turns a burst into mud.
    private void PlayGatlingShot()
    {
        if (GatlingFireLoopClip != null) return;
        PlayRandom(GatlingShotClips);
    }

    private void StopGatlingFire()
    {
        if (_weapon.isPlaying) _weapon.Stop();
        PlayOneShot(GatlingSpinDownClip);
    }

    private void PlayPodsOpen()
    {
        if (MissilePodOpenClip == null) return;
        _charge.clip = MissilePodOpenClip;
        _charge.pitch = 1f;
        _charge.Play();
    }

    private void PlayMissileLaunch() => PlayJittered(MissileLaunchClip);

    private void StartLaserCharge()
    {
        if (LaserChargeLoopClip == null) return;
        _charge.clip = LaserChargeLoopClip;
        _charge.pitch = 1f;
        _charge.Play();
    }

    private void StartLaserSweep()
    {
        PlayOneShot(LaserFireClip);

        if (LaserSweepLoopClip == null) { StopCharge(); return; }

        // Straight from the charge whine into the sweep hum on the same channel:
        // they're one continuous idea and shouldn't overlap.
        _charge.clip = LaserSweepLoopClip;
        _charge.pitch = 1f;
        _charge.Play();
    }

    private void EndLaserSweep()
    {
        StopCharge();
        PlayOneShot(LaserEndClip);
    }

    private void StopCharge()
    {
        if (_charge.isPlaying) _charge.Stop();
    }

    private void StopSustained()
    {
        StopCharge();
        if (_weapon.isPlaying) _weapon.Stop();
    }

    #endregion

    #region Weakpoint

    private void PlayWeakpointExposed() => PlayOneShot(WeakpointExposedClip);
    private void PlayWeakpointClosed() => PlayOneShot(WeakpointClosedClip);

    #endregion

    #region Playback

    private AudioSource AddSource(bool loop)
    {
        var src = gameObject.AddComponent<AudioSource>();
        src.playOnAwake = false;
        src.loop = loop;
        src.spatialBlend = SpatialBlend;
        src.rolloffMode = AudioRolloffMode.Linear;
        src.minDistance = MinDistance;
        src.maxDistance = MaxDistance;
        src.dopplerLevel = 0f;
        return src;
    }

    private void PlayOneShot(AudioClip clip)
    {
        if (clip == null) return;
        _oneShot.pitch = 1f;
        _oneShot.PlayOneShot(clip);
    }

    private void PlayJittered(AudioClip clip)
    {
        if (clip == null) return;
        _oneShot.pitch = Random.Range(PitchJitter.x, PitchJitter.y);
        _oneShot.PlayOneShot(clip);
    }

    private void PlayRandom(IReadOnlyList<AudioClip> clips)
    {
        if (clips == null || clips.Count == 0) return;
        PlayJittered(clips[Random.Range(0, clips.Count)]);
    }

    /// <summary>For cues that have to outlive the boss (its death) or outlast
    /// anything it does next (the landing). Same trick EnemyAudio uses.</summary>
    private void PlayDetached(AudioClip clip, string objectName)
    {
        if (clip == null) return;

        var go = new GameObject($"{gameObject.name}_{objectName}");
        go.transform.position = transform.position;

        var src = go.AddComponent<AudioSource>();
        src.playOnAwake = false;
        src.spatialBlend = SpatialBlend;
        src.rolloffMode = AudioRolloffMode.Linear;
        src.minDistance = MinDistance;
        src.maxDistance = MaxDistance;
        src.dopplerLevel = 0f;
        src.clip = clip;
        src.Play();

        Destroy(go, clip.length + 0.1f);
    }

    #endregion
}
