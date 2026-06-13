using UnityEngine;
using System;

[RequireComponent(typeof(EnemyHealth))]
public class EnemyAudio : MonoBehaviour
{
    [Header("Movement")]
    public AudioClip WalkClip;

    [Header("Weapons")]
    public AudioClip LaserClip;
    public AudioClip BombClip;

    [Header("Status")]
    public AudioClip HurtClip;
    public AudioClip DeathClip;

    [Header("3D Audio Settings")]
    [Range(0f, 1f)] public float SpatialBlend = 1.0f;
    public float MinDistance = 5f;
    public float MaxDistance = 30f;

    private AudioSource _loopSource;
    private AudioSource _oneShotSource;
    
    private EnemyHealth _health;
    private EnemyBrain _brain;
    private LaserBehaviour _laser;
    private GrenadeBurstBehaviour _burst;

    private void Awake()
    {
        _health = GetComponent<EnemyHealth>();
        _brain = GetComponent<EnemyBrain>();
        _laser = GetComponent<LaserBehaviour>();
        _burst = GetComponent<GrenadeBurstBehaviour>();

        // Source 1: Dedicated to continuous, looping sounds
        _loopSource = gameObject.AddComponent<AudioSource>();
        ConfigureSource(_loopSource, true);

        // Source 2: Dedicated to overlapping, instantaneous sounds
        _oneShotSource = gameObject.AddComponent<AudioSource>();
        ConfigureSource(_oneShotSource, false);
    }

    private void Start()
    {
        _health.OnDied += HandleDeath;
        
        if (_brain != null) 
            _brain.OnStateChanged += HandleStateChanged;
            
        // Uncomment these if your weapon scripts contain corresponding events
        // if (_laser != null) _laser.OnFired += PlayLaser;
        // if (_burst != null) _burst.OnFired += PlayBomb;
    }

    private void OnDestroy()
    {
        if (_health != null) _health.OnDied -= HandleDeath;
        if (_brain != null)  _brain.OnStateChanged -= HandleStateChanged;
        
        // if (_laser != null) _laser.OnFired -= PlayLaser;
        // if (_burst != null) _burst.OnFired -= PlayBomb;
    }

    private void ConfigureSource(AudioSource source, bool isLooping)
    {
        source.playOnAwake = false;
        source.loop = isLooping;
        source.spatialBlend = SpatialBlend;
        source.rolloffMode = AudioRolloffMode.Linear;
        source.minDistance = MinDistance;
        source.maxDistance = MaxDistance;
        source.dopplerLevel = 0f; // Prevents unwanted pitch bending during fast movement
    }

    private void HandleStateChanged(EnemyState state)
    {
        if (state == EnemyState.Aggro)
        {
            if (WalkClip != null && !_loopSource.isPlaying)
            {
                _loopSource.clip = WalkClip;
                _loopSource.Play();
            }
        }
        else
        {
            _loopSource.Stop();
        }
    }

    public void PlayLaser() => PlayOneShot(LaserClip);
    
    public void PlayBomb() => PlayOneShot(BombClip);
    
    public void PlayHurt() => PlayOneShot(HurtClip);

    private void PlayOneShot(AudioClip clip)
    {
        if (clip != null)
        {
            _oneShotSource.PlayOneShot(clip);
        }
    }

    private void HandleDeath(Vector3 deathPosition)
    {
        _loopSource.Stop();

        if (DeathClip != null)
        {
            GameObject deathAudioObject = new GameObject($"{gameObject.name}_DeathSound");
            
            // You can now use the exact death position passed by the event
            deathAudioObject.transform.position = deathPosition; 
            
            AudioSource deathSource = deathAudioObject.AddComponent<AudioSource>();
            ConfigureSource(deathSource, false);
            deathSource.clip = DeathClip;
            deathSource.Play();
            
            Destroy(deathAudioObject, DeathClip.length + 0.1f);
        }
    }
}