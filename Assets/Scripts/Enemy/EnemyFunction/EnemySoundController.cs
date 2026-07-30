using UnityEngine;
using UnityEngine.AI;
using System;

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
    public AudioClip[] ShotgunClips;
    [Tooltip("Multiple clank variations — one is picked at random per shot so bursts don't sound identical.")]
    public AudioClip[] PistolClips;

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

    [Header("3D Audio — Settings")]
    [Range(0f, 1f)] public float SpatialBlend = 1.0f;
    public float MinDistance = 5f;
    public float MaxDistance = 30f;

    private AudioSource _loopSource;
    private AudioSource _oneShotSource;

    private EnemyHealth           _health;
    private EnemyBrain            _brain;
    private LaserBehaviour        _laser;
    private GrenadeBurstBehaviour _burst;
    private SMGAttackBehaviour    _smg;
    private PistolAttackBehaviour _pistol;
    private NavMeshAgent           _nav; 
    private SniperAttackBehaviour _sniper; 
    private MeleeAttackBehaviour  _melee;
    
    private Component             _shotgunComponent; 
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

        _loopSource = gameObject.AddComponent<AudioSource>();
        Configure3D(_loopSource, loop: true);

        _oneShotSource = gameObject.AddComponent<AudioSource>();
        Configure3D(_oneShotSource, loop: false);
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

        _shotgunComponent = GetComponent("ShotgunAttackBehaviour");
        if (_shotgunComponent != null)
        {
            var ev = _shotgunComponent.GetType().GetEvent("OnShotgunFired");
            if (ev != null)
            {
                Action handler = PlayShotgun;
                ev.AddMethod.Invoke(_shotgunComponent, new object[] { handler });
            }
        }
    }

    private void Update()
    {
        HandleWalking();
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

        if (_shotgunComponent != null)
        {
            var ev = _shotgunComponent.GetType().GetEvent("OnShotgunFired");
            if (ev != null)
            {
                Action handler = PlayShotgun;
                ev.RemoveMethod.Invoke(_shotgunComponent, new object[] { handler });
            }
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

    private void PlaySMG()     => PlayRandomShootClip(SMGClips);
    private void PlayPistol()  => PlayRandomShootClip(PistolClips);
    private void PlayShotgun() => PlayRandomShootClip(ShotgunClips);

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

    private void HandleDeath(Vector3 impulse)
    {
        _loopSource.Stop();
        _oneShotSource.Stop();
        if (DeathClip == null) return;

        var go = new GameObject($"{gameObject.name}_Death");
        go.transform.position = transform.position;
        var src = go.AddComponent<AudioSource>();
        src.spatialBlend = 0f;
        src.clip = DeathClip;
        src.Play();

        Destroy(go, DeathClip.length + 0.1f);
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