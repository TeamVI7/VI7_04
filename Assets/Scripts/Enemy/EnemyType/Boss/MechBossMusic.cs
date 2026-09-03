// MechBossMusic.cs — swaps the track over when the mech finishes its landing.
//
// The cue is OnIntroComplete, not OnIntroStart or OnIntroImpact: the drop and the
// slam are still cinematic, and cutting the music underneath them fights the impact
// sound. OnIntroComplete is the frame the boss becomes a fight, which is exactly
// where the music should turn over.
//
// The track plays on a DETACHED GameObject rather than on the boss. The boss is
// destroyed when it dies, and anything parented to it — audio source, coroutine,
// tween — dies mid-note with it. Everything that has to outlive the fight lives on
// BossMusicPlayer at the bottom of this file instead.
using System.Collections;
using UnityEngine;
using UnityEngine.Audio;

public class MechBossMusic : MonoBehaviour
{
    #region Inspector

    [Header("Boss Track")]
    [Tooltip("What starts playing once the mech has landed. Leave empty to only fade the current music out.")]
    public AudioClip bossTrack;
    [Range(0f, 1f)] public float bossVolume = 0.6f;
    public bool loop = true;
    [Tooltip("Optional mixer group for the boss track. Leave empty to play unrouted.")]
    public AudioMixerGroup output;

    [Header("Transition")]
    [Tooltip("Seconds the old track fades out and the new one fades in, overlapping. 0 is a hard cut.")]
    public float crossfadeDuration = 2f;
    [Tooltip("Extra beat between the landing finishing and the track coming in. A short hold sells the mech standing up before the music hits.")]
    public float bossTrackDelay = 0f;

    [Header("Current Music")]
    [Tooltip("The track playing before the fight. Usually leave all of this alone — the boss is spawned at runtime so a prefab can't hold a scene reference, and the scene's PlaylistManager is found by type without needing any of it.")]
    public AudioSource previousMusicSource;
    [Tooltip("Optional tag, tried after the PlaylistManager lookup. Ignored if the tag isn't defined in the project.")]
    public string previousMusicTag = "";
    [Tooltip("Optional GameObject name, tried after the tag.")]
    public string previousMusicObjectName = "";
    [Tooltip("Last resort: the loudest playing 2D AudioSource in the scene. Can grab an ambience bed by mistake — set a name above if it picks the wrong one.")]
    public bool findLoudest2DSourceAsFallback = true;

    [Header("On Boss Death")]
    [Tooltip("Fade the boss track out and bring the previous track back when the mech dies. Off leaves the boss track playing.")]
    public bool restorePreviousOnDeath = true;
    public float deathFadeDuration = 3f;

    [Header("Debug")]
    public bool debugLog;

    #endregion

    private MechBossBrain _brain;
    private EnemyHealth _health;
    private BossMusicPlayer _player;
    private bool _swapped;

    // Awake, matching MechBossAudio: the brain kicks its intro off from its own
    // Start, so a Start here can lose the race on the early intro events. The
    // completion event is late enough not to care, but there's no reason to be
    // inconsistent about it.
    private void Awake()
    {
        _brain = GetComponent<MechBossBrain>();
        _health = GetComponent<EnemyHealth>();

        if (_brain == null)
        {
            Debug.LogWarning($"[{nameof(MechBossMusic)}] No MechBossBrain on {name} — nothing to take a cue from.", this);
            enabled = false;
            return;
        }

        _brain.OnIntroComplete += HandleIntroComplete;
        if (_health != null && restorePreviousOnDeath) _health.OnDied += HandleDied;
    }

    private void OnDestroy()
    {
        if (_brain != null) _brain.OnIntroComplete -= HandleIntroComplete;
        if (_health != null) _health.OnDied -= HandleDied;
    }

    private void HandleIntroComplete()
    {
        // OnIntroComplete fires once, but a re-entered fight or a second boss would
        // otherwise stack two players over each other.
        if (_swapped) return;
        _swapped = true;

        PlaylistManager playlist = ResolvePreviousPlaylist();
        AudioSource previous = ResolvePreviousMusic(playlist);

        var go = new GameObject($"{name}_BossMusic");
        _player = go.AddComponent<BossMusicPlayer>();
        _player.Begin(bossTrack, bossVolume, loop, output, previous, playlist,
                      crossfadeDuration, bossTrackDelay);

        if (debugLog)
            Debug.Log($"[{nameof(MechBossMusic)}] {name}: landing done — " +
                      $"{(bossTrack != null ? bossTrack.name : "(no track)")} in over {crossfadeDuration:0.##}s, " +
                      $"previous = {(previous != null ? previous.gameObject.name : "none found")}" +
                      $"{(playlist != null ? " (PlaylistManager — will be paused, not stopped)" : "")}.", this);
    }

    private void HandleDied(Vector3 impulse)
    {
        if (_player == null) return;
        _player.EndAndRestore(deathFadeDuration);
    }

    /// <summary>The scene's background-music driver, if it has one. Found by type, so
    /// it needs no tag, no name and no inspector wiring — which matters because the
    /// boss is a runtime instance and can't hold a scene reference.</summary>
    private PlaylistManager ResolvePreviousPlaylist() =>
        FindFirstObjectByType<PlaylistManager>(FindObjectsInactive.Exclude);

    /// <summary>Finds whatever is playing right now, so it can be faded down and
    /// brought back afterwards. Most explicit route first.</summary>
    private AudioSource ResolvePreviousMusic(PlaylistManager playlist)
    {
        if (previousMusicSource != null) return previousMusicSource;
        if (playlist != null && playlist.Source != null) return playlist.Source;

        if (!string.IsNullOrEmpty(previousMusicTag))
        {
            // Throws outright if the tag was never defined in the project, which is
            // a perfectly normal state here — it isn't an error, just a miss.
            try
            {
                GameObject tagged = GameObject.FindWithTag(previousMusicTag);
                if (tagged != null && tagged.TryGetComponent(out AudioSource tagSource)) return tagSource;
            }
            catch (UnityException)
            {
                if (debugLog)
                    Debug.Log($"[{nameof(MechBossMusic)}] Tag '{previousMusicTag}' isn't defined — skipping that lookup.", this);
            }
        }

        if (!string.IsNullOrEmpty(previousMusicObjectName))
        {
            GameObject named = GameObject.Find(previousMusicObjectName);
            if (named != null && named.TryGetComponent(out AudioSource namedSource)) return namedSource;
        }

        if (!findLoudest2DSourceAsFallback) return null;

        // Background music, described structurally: playing and fully 2D so it doesn't
        // fall off with distance. Deliberately NOT filtered on loop — a playlist-driven
        // source plays one track at a time with loop off, so requiring it would skip
        // exactly the case this project actually uses. The loudest match wins.
        AudioSource best = null;
        foreach (AudioSource source in FindObjectsByType<AudioSource>(FindObjectsSortMode.None))
        {
            if (source == null || !source.isPlaying) continue;
            if (source.spatialBlend > 0.1f) continue;
            if (source.transform.IsChildOf(transform)) continue; // our own boss SFX
            if (best == null || source.volume > best.volume) best = source;
        }

        return best;
    }
}

/// <summary>
/// Owns the boss track for as long as it plays. Detached from the boss on purpose —
/// it has to keep going through the mech's death, the ragdoll, and the corpse being
/// destroyed, and it's the thing that restores the previous track afterwards.
/// </summary>
public class BossMusicPlayer : MonoBehaviour
{
    private AudioSource _source;
    private AudioSource _previous;
    private PlaylistManager _previousPlaylist;
    private float _previousVolume;
    private Coroutine _routine;

    public void Begin(AudioClip clip, float volume, bool loop, AudioMixerGroup output,
                      AudioSource previous, PlaylistManager previousPlaylist,
                      float crossfade, float delay)
    {
        _previous = previous;
        _previousPlaylist = previousPlaylist;
        _previousVolume = previous != null ? previous.volume : 0f;

        if (clip != null)
        {
            _source = gameObject.AddComponent<AudioSource>();
            _source.clip = clip;
            _source.loop = loop;
            _source.playOnAwake = false;
            _source.spatialBlend = 0f; // music is never positional
            _source.volume = 0f;
            if (output != null) _source.outputAudioMixerGroup = output;
        }

        _routine = StartCoroutine(Co_Swap(volume, crossfade, delay));
    }

    private IEnumerator Co_Swap(float volume, float crossfade, float delay)
    {
        // The old track starts leaving immediately; the new one can be held back, so
        // a delay reads as a gap rather than as two tracks fighting.
        // Faded to silence, then PAUSED rather than stopped when a playlist owns it.
        // Pausing leaves the track mid-bar so it can be resumed where it left off,
        // and it keeps the playlist's own auto-advance from treating a stopped source
        // as a finished track and starting the next one underneath the boss music.
        Coroutine fadeOut = _previous != null
            ? StartCoroutine(Co_Fade(_previous, _previousVolume, 0f, crossfade,
                                     stopAtEnd: _previousPlaylist == null))
            : null;

        if (delay > 0f) yield return new WaitForSeconds(delay);

        if (_source != null)
        {
            _source.Play();
            yield return Co_Fade(_source, 0f, volume, crossfade, stopAtEnd: false);
        }

        if (fadeOut != null) yield return fadeOut;
        if (_previousPlaylist != null) _previousPlaylist.Pause();
        _routine = null;
    }

    /// <summary>Fades the boss track out and brings the previous track back up.</summary>
    public void EndAndRestore(float duration)
    {
        if (_routine != null) StopCoroutine(_routine);
        _routine = StartCoroutine(Co_End(duration));
    }

    private IEnumerator Co_End(float duration)
    {
        Coroutine back = null;
        if (_previous != null)
        {
            _previous.volume = 0f;

            // Resume, not Play: the playlist knows whether to un-pause the held track
            // or start a fresh one, and calling Play directly would restart the clip
            // from zero and desync its running order.
            if (_previousPlaylist != null) _previousPlaylist.Resume();
            else if (!_previous.isPlaying) _previous.Play();

            back = StartCoroutine(Co_Fade(_previous, 0f, _previousVolume, duration, stopAtEnd: false));
        }

        if (_source != null) yield return Co_Fade(_source, _source.volume, 0f, duration, stopAtEnd: true);
        if (back != null) yield return back;

        Destroy(gameObject);
    }

    // Unscaled: the intro cutscene runs on a slowed timeScale and the death sequence
    // may too, and a music fade that stretches with slow-mo sounds broken.
    private static IEnumerator Co_Fade(AudioSource source, float from, float to, float duration, bool stopAtEnd)
    {
        if (source == null) yield break;

        if (duration <= 0f)
        {
            source.volume = to;
        }
        else
        {
            float t = 0f;
            while (t < duration)
            {
                if (source == null) yield break;
                t += Time.unscaledDeltaTime;
                source.volume = Mathf.Lerp(from, to, Mathf.Clamp01(t / duration));
                yield return null;
            }
            source.volume = to;
        }

        if (stopAtEnd && source != null) source.Stop();
    }
}
