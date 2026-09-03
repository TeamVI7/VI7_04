using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Subscribes to PlayerMovement/PlayerHealth/ability events and plays the
/// matching clips from a PlayerSoundData asset. Adding a new sound category
/// = add fields to PlayerSoundData + one Handle_XXX method here wired to the
/// relevant event. No other system needs to know this exists.
/// </summary>
[RequireComponent(typeof(AudioSource))]
public class PlayerSoundController : MonoBehaviour
{
    // ─────────────────────────────────────────────────────────────────────────
    #region Inspector
    // ─────────────────────────────────────────────────────────────────────────

    [Header("Data")]
    public PlayerSoundData data;

    [Header("References")]
    [Tooltip("Auto-found via GetComponent if left empty.")]
    public PlayerMovement playerMovement;
    public Rigidbody      rb;
    public Dashing        dashing;
    public Sliding        sliding;
    public WallRunning    wallRunning;
    public Climbing       climbing;
    public PlayerMelee    melee;

    [Header("Surface Detection")]
    public float groundCheckDistance = 1.2f;

    [Header("Debug")]
    [SerializeField] private bool debugLog = false;

    #endregion

    // ─────────────────────────────────────────────────────────────────────────
    #region Private State
    // ─────────────────────────────────────────────────────────────────────────

    private AudioSource _oneShotSource;
    private AudioSource _loopSource; // sprint breathing / slide / wallrun / climb loop — one active at a time
    private float _lastHP = -1f;
    private Dictionary<string, PlayerSoundData.SurfaceFootstepSet> _surfaceLookup;

    #endregion

    // ─────────────────────────────────────────────────────────────────────────
    #region Unity Lifecycle
    // ─────────────────────────────────────────────────────────────────────────

    private void Awake()
    {
        EnsureRefs();
        BuildSurfaceLookup();

        _oneShotSource = GetComponent<AudioSource>();
        _oneShotSource.playOnAwake  = false;
        _oneShotSource.spatialBlend = 1f;

        _loopSource = gameObject.AddComponent<AudioSource>();
        _loopSource.playOnAwake  = false;
        _loopSource.loop         = true;
        _loopSource.spatialBlend = 1f;
    }

    private void EnsureRefs()
    {
        if (playerMovement == null) playerMovement = GetComponent<PlayerMovement>();
        if (rb             == null) rb             = GetComponent<Rigidbody>();
        if (dashing         == null) dashing        = GetComponent<Dashing>();
        if (sliding         == null) sliding        = GetComponent<Sliding>();
        if (wallRunning     == null) wallRunning    = GetComponent<WallRunning>();
        if (climbing        == null) climbing       = GetComponent<Climbing>();
        if (melee           == null) melee          = GetComponent<PlayerMelee>();
    }

    private void OnEnable()
    {
        if (playerMovement != null)
        {
            playerMovement.OnFootstep += HandleFootstep;
            playerMovement.OnJumped   += HandleJump;
            playerMovement.OnLanded   += HandleLand;
        }

        PlayerHealth.OnHealthChanged += HandleHealthChanged;
        PlayerHealth.OnDied          += HandleDeath;

        if (dashing != null) dashing.OnDashStart += HandleDashStart;

        if (sliding != null)
        {
            sliding.OnSlideStart += HandleSlideStart;
            sliding.OnSlideEnd   += HandleSlideEnd;
        }

        if (wallRunning != null)
        {
            wallRunning.OnWallRunStart   += HandleWallRunStart;
            wallRunning.OnWallRunEnd     += HandleWallRunEnd;
            wallRunning.OnWallSlideStart += HandleWallSlideStart;
            wallRunning.OnWallSlideEnd   += HandleWallSlideEnd;
            wallRunning.OnWallJump       += HandleWallJump;
        }

        if (climbing != null)
        {
            climbing.OnClimbStart += HandleClimbStart;
            climbing.OnClimbEnd   += HandleClimbEnd;
            climbing.OnClimbJump  += HandleClimbJump;
        }

        if (melee != null)
        {
            melee.OnSwing   += HandleMeleeSwing;
            melee.OnHit     += HandleMeleeHit;
            melee.OnMiss    += HandleMeleeMiss;
            melee.OnExecute += HandleMeleeExecute;
        }
    }

    private void OnDisable()
    {
        if (playerMovement != null)
        {
            playerMovement.OnFootstep -= HandleFootstep;
            playerMovement.OnJumped   -= HandleJump;
            playerMovement.OnLanded   -= HandleLand;
        }

        PlayerHealth.OnHealthChanged -= HandleHealthChanged;
        PlayerHealth.OnDied          -= HandleDeath;

        if (dashing != null) dashing.OnDashStart -= HandleDashStart;

        if (sliding != null)
        {
            sliding.OnSlideStart -= HandleSlideStart;
            sliding.OnSlideEnd   -= HandleSlideEnd;
        }

        if (wallRunning != null)
        {
            wallRunning.OnWallRunStart   -= HandleWallRunStart;
            wallRunning.OnWallRunEnd     -= HandleWallRunEnd;
            wallRunning.OnWallSlideStart -= HandleWallSlideStart;
            wallRunning.OnWallSlideEnd   -= HandleWallSlideEnd;
            wallRunning.OnWallJump       -= HandleWallJump;
        }

        if (climbing != null)
        {
            climbing.OnClimbStart -= HandleClimbStart;
            climbing.OnClimbEnd   -= HandleClimbEnd;
            climbing.OnClimbJump  -= HandleClimbJump;
        }

        if (melee != null)
        {
            melee.OnSwing   -= HandleMeleeSwing;
            melee.OnHit     -= HandleMeleeHit;
            melee.OnMiss    -= HandleMeleeMiss;
            melee.OnExecute -= HandleMeleeExecute;
        }
    }

    /// <summary>Only polls for sprint breathing — everything else is event-driven.</summary>
    private void Update()
    {
        TickSprintBreathing();
    }

    #endregion

    // ─────────────────────────────────────────────────────────────────────────
    #region Footstep / Jump / Land
    // ─────────────────────────────────────────────────────────────────────────

    private void HandleFootstep(bool isSprinting)
    {
        if (data == null) return;

        var set = GetSurfaceSet();
        AudioClip[] clips = set != null && set.footstepClips != null && set.footstepClips.Length > 0
            ? set.footstepClips
            : data.defaultFootstepClips;

        PlayRandomOneShot(clips, data.footstepVolume, data.footstepPitchRange);
    }

    private void HandleJump()
    {
        if (data == null) return;
        PlayRandomOneShot(data.jumpClips, data.jumpVolume, Vector2.one);
    }

    private void HandleLand()
    {
        if (data == null) return;

        float fallSpeed = rb != null ? Mathf.Abs(Mathf.Min(0f, rb.linearVelocity.y)) : 0f;
        bool  hard       = fallSpeed >= data.hardLandFallSpeedThreshold
                         && data.hardLandClips != null && data.hardLandClips.Length > 0;

        var set = GetSurfaceSet();
        AudioClip[] clips = hard
            ? data.hardLandClips
            : (set != null && set.landClips != null && set.landClips.Length > 0 ? set.landClips : data.defaultLandClips);

        PlayRandomOneShot(clips, data.landVolume, Vector2.one);
        Log($"Landed — fallSpeed={fallSpeed:F1} hard={hard}");
    }

    #endregion

    // ─────────────────────────────────────────────────────────────────────────
    #region Sprint Breathing (polled — no dedicated sprint-start/end event exists)
    // ─────────────────────────────────────────────────────────────────────────

    private void TickSprintBreathing()
    {
        if (data == null || data.sprintBreathingLoop == null || playerMovement == null) return;

        bool sprinting    = playerMovement.IsSprinting;
        bool loopIsSprint = _loopSource.clip == data.sprintBreathingLoop;

        if (sprinting && (!loopIsSprint || !_loopSource.isPlaying))
        {
            _loopSource.clip   = data.sprintBreathingLoop;
            _loopSource.volume = data.sprintBreathingVolume;
            _loopSource.Play();
        }
        else if (!sprinting && loopIsSprint && _loopSource.isPlaying)
        {
            _loopSource.Stop();
        }
    }

    #endregion

    // ─────────────────────────────────────────────────────────────────────────
    #region Dash
    // ─────────────────────────────────────────────────────────────────────────

    private void HandleDashStart(Vector3 direction)
    {
        if (data == null) return;
        PlayRandomOneShot(data.dashClips, data.dashVolume, Vector2.one);
    }

    #endregion

    // ─────────────────────────────────────────────────────────────────────────
    #region Slide
    // ─────────────────────────────────────────────────────────────────────────

    private void HandleSlideStart()
    {
        if (data == null) return;
        PlayOneShot(data.slideStartClip, data.slideVolume);

        if (data.slideLoopClip != null)
        {
            _loopSource.clip   = data.slideLoopClip;
            _loopSource.volume = data.slideVolume;
            _loopSource.Play();
        }
    }

    private void HandleSlideEnd()
    {
        if (data == null) return;
        if (_loopSource.clip == data.slideLoopClip) _loopSource.Stop();
        PlayOneShot(data.slideEndClip, data.slideVolume);
    }

    #endregion

    // ─────────────────────────────────────────────────────────────────────────
    #region Wall Run / Wall Slide
    // ─────────────────────────────────────────────────────────────────────────

    private void HandleWallRunStart()
    {
        if (data == null || data.wallRunLoopClip == null) return;
        _loopSource.clip   = data.wallRunLoopClip;
        _loopSource.volume = data.wallRunVolume;
        _loopSource.Play();
    }

    private void HandleWallRunEnd()
    {
        if (data != null && _loopSource.clip == data.wallRunLoopClip) _loopSource.Stop();
    }

    private void HandleWallSlideStart()
    {
        if (data == null || data.wallSlideLoopClip == null) return;
        _loopSource.clip   = data.wallSlideLoopClip;
        _loopSource.volume = data.wallRunVolume;
        _loopSource.Play();
    }

    private void HandleWallSlideEnd()
    {
        if (data != null && _loopSource.clip == data.wallSlideLoopClip) _loopSource.Stop();
    }

    private void HandleWallJump(Vector3 force)
    {
        if (data == null) return;
        PlayRandomOneShot(data.wallJumpClips, data.wallRunVolume, Vector2.one);
    }

    #endregion

    // ─────────────────────────────────────────────────────────────────────────
    #region Climbing
    // ─────────────────────────────────────────────────────────────────────────

    private void HandleClimbStart()
    {
        if (data == null || data.climbLoopClip == null) return;
        _loopSource.clip   = data.climbLoopClip;
        _loopSource.volume = data.climbVolume;
        _loopSource.Play();
    }

    private void HandleClimbEnd()
    {
        if (data != null && _loopSource.clip == data.climbLoopClip) _loopSource.Stop();
    }

    private void HandleClimbJump(Vector3 force)
    {
        if (data == null) return;
        PlayRandomOneShot(data.climbJumpClips, data.climbVolume, Vector2.one);
    }

    #endregion

    // ─────────────────────────────────────────────────────────────────────────
    #region Health / Death
    // ─────────────────────────────────────────────────────────────────────────

    private void HandleHealthChanged(float current, float max)
    {
        // First callback after enable — just baseline, don't play a hurt sound for it.
        if (_lastHP < 0f) { _lastHP = current; return; }

        float damageTaken = _lastHP - current;
        _lastHP = current;

        if (data == null || damageTaken <= 0f) return;
        if (max > 0f && damageTaken / max < data.minHurtDamageFraction) return;

        PlayRandomOneShot(data.hurtClips, data.hurtVolume, Vector2.one);
    }

    private void HandleDeath()
    {
        if (data == null) return;
        _loopSource.Stop();

        // NOT PlayRandomOneShot: DeathCamera.Play() deactivates the player GameObject
        // in this same frame, which stops every AudioSource on it — the death clip would
        // be cut off before a single sample is heard. Play it on a detached object instead.
        PlayDetachedRandomOneShot(data.deathClips, data.deathVolume);
    }

    #endregion

    // ─────────────────────────────────────────────────────────────────────────
    #region Melee
    // ─────────────────────────────────────────────────────────────────────────

    private void HandleMeleeSwing() => PlayRandomOneShot(data?.meleeSwingClips, data?.meleeVolume ?? 1f, Vector2.one);
    private void HandleMeleeHit()   => PlayRandomOneShot(data?.meleeHitClips, data?.meleeVolume ?? 1f, Vector2.one);
    private void HandleMeleeMiss()  => PlayRandomOneShot(data?.meleeMissClips, data?.meleeVolume ?? 1f, Vector2.one);
    private void HandleMeleeExecute(EnemyHealth target) => PlayRandomOneShot(data?.meleeExecuteClips, data?.meleeVolume ?? 1f, Vector2.one);

    #endregion

    // ─────────────────────────────────────────────────────────────────────────
    #region Surface Detection
    // ─────────────────────────────────────────────────────────────────────────

    private void BuildSurfaceLookup()
    {
        _surfaceLookup = new Dictionary<string, PlayerSoundData.SurfaceFootstepSet>();
        if (data == null || data.surfaceOverrides == null) return;

        foreach (var set in data.surfaceOverrides)
        {
            if (set == null || string.IsNullOrEmpty(set.surfaceTag)) continue;
            _surfaceLookup[set.surfaceTag] = set;
        }
    }

    private PlayerSoundData.SurfaceFootstepSet GetSurfaceSet()
    {
        if (_surfaceLookup == null || _surfaceLookup.Count == 0) return null;
        if (!Physics.Raycast(transform.position, Vector3.down, out RaycastHit hit, groundCheckDistance)) return null;

        return _surfaceLookup.TryGetValue(hit.collider.tag, out var set) ? set : null;
    }

    #endregion

    // ─────────────────────────────────────────────────────────────────────────
    #region Playback Helpers
    // ─────────────────────────────────────────────────────────────────────────

    private void PlayOneShot(AudioClip clip, float volume)
    {
        if (clip == null || _oneShotSource == null) return;
        _oneShotSource.pitch = 1f;
        _oneShotSource.PlayOneShot(clip, volume);
    }

    private void PlayRandomOneShot(AudioClip[] clips, float volume, Vector2 pitchRange)
    {
        if (clips == null || clips.Length == 0 || _oneShotSource == null) return;

        AudioClip clip = clips[Random.Range(0, clips.Length)];
        _oneShotSource.pitch = Random.Range(pitchRange.x, pitchRange.y);
        _oneShotSource.PlayOneShot(clip, volume);
        Log($"Played '{clip.name}' vol={volume:F2} pitch={_oneShotSource.pitch:F2}");
    }

    /// <summary>
    /// Plays a clip on a throwaway GameObject at the root of the scene, so it keeps
    /// playing after the player GameObject is deactivated (death). 2D on purpose: the
    /// death camera detaches and tumbles away, and this sound should not pan or fade
    /// with it. The temp object destroys itself once the clip is done.
    /// </summary>
    private void PlayDetachedRandomOneShot(AudioClip[] clips, float volume)
    {
        if (clips == null || clips.Length == 0) return;

        AudioClip clip = clips[Random.Range(0, clips.Length)];
        if (clip == null) return;

        var go  = new GameObject($"PlayerSound_OneShot_{clip.name}");
        go.transform.position = transform.position;

        var src = go.AddComponent<AudioSource>();
        src.clip         = clip;
        src.volume       = volume;
        src.spatialBlend = 0f;
        src.Play();

        // Unscaled margin: the death sequence can run while timeScale is messed with,
        // and Destroy's delay is scaled time — the extra second keeps it from being culled early.
        Destroy(go, clip.length + 1f);
        Log($"Played detached '{clip.name}' vol={volume:F2}");
    }

    #endregion

    // ─────────────────────────────────────────────────────────────────────────
    #region Debug
    // ─────────────────────────────────────────────────────────────────────────

    [System.Diagnostics.Conditional("UNITY_EDITOR")]
    private void Log(string msg)
    {
        if (debugLog) Debug.Log($"[PlayerSoundController] {msg}", this);
    }

    #endregion
}