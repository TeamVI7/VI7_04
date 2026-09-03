using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(AudioSource))]
public class PlaylistManager : MonoBehaviour
{
    [SerializeField] private List<AudioClip> tracks = new List<AudioClip>();
    [SerializeField] private bool shuffle = false;
    [SerializeField] private bool loopPlaylist = true;
    [SerializeField] private bool playOnStart = true;

    private AudioSource audioSource;
    private List<int> playOrder = new List<int>();
    private int currentIndex = -1;
    private bool stopped;

    /// <summary>True while a track is playing or paused mid-track.</summary>
    public bool IsActive => !stopped && currentIndex >= 0;

    /// <summary>The source the playlist drives. Handed out so a fade can be run over
    /// it without every caller having to know it lives on this GameObject.</summary>
    public AudioSource Source => audioSource;

    private void Awake()
    {
        audioSource = GetComponent<AudioSource>();
        audioSource.loop = false;
        BuildPlayOrder();
    }

    private void Start()
    {
        if (playOnStart && tracks.Count > 0)
            PlayNext();
    }

    private void Update()
    {
        // The auto-advance can't just test "not playing": Stop() leaves the source
        // not-playing at time 0, which is indistinguishable from a track that ended,
        // so without the stopped flag Stop() was undone on the very next frame and
        // the playlist could never actually be stopped. Pause() is safe either way —
        // it leaves time mid-track — but this makes the intent explicit rather than
        // relying on that.
        if (stopped) return;
        if (currentIndex >= 0 && !audioSource.isPlaying && audioSource.time == 0f)
            PlayNext();
    }

    public void Play(int trackIndex)
    {
        if (trackIndex < 0 || trackIndex >= tracks.Count) return;

        // An empty slot would leave the source with a null clip, and the auto-advance
        // above would then spin through PlayNext every frame. PlayNext skips past
        // these, so the only way to land here is a direct Play() call.
        if (tracks[trackIndex] == null)
        {
            Debug.LogWarning($"[{nameof(PlaylistManager)}] Track {trackIndex} on {name} is empty.", this);
            return;
        }

        stopped = false;
        currentIndex = trackIndex;
        audioSource.clip = tracks[trackIndex];
        audioSource.Play();
    }

    public void PlayNext()
    {
        if (tracks.Count == 0) return;

        // Bounded: an all-empty list would otherwise have Update calling this every
        // frame forever, since nothing ever starts playing.
        for (int attempt = 0; attempt < playOrder.Count; attempt++)
        {
            int orderPos = playOrder.IndexOf(currentIndex) + 1;

            if (orderPos >= playOrder.Count)
            {
                if (!loopPlaylist)
                {
                    currentIndex = -1;
                    stopped = true;
                    return;
                }
                BuildPlayOrder();
                orderPos = 0;
            }

            int track = playOrder[orderPos];
            if (tracks[track] != null)
            {
                Play(track);
                return;
            }

            currentIndex = track; // empty slot — step over it and try the next one
        }

        Debug.LogWarning($"[{nameof(PlaylistManager)}] {name} has no playable tracks — stopping.", this);
        currentIndex = -1;
        stopped = true;
    }

    public void PlayPrevious()
    {
        if (tracks.Count == 0) return;

        int orderPos = playOrder.IndexOf(currentIndex) - 1;
        if (orderPos < 0) orderPos = playOrder.Count - 1;

        Play(playOrder[orderPos]);
    }

    public void Stop()
    {
        stopped = true;
        audioSource.Stop();
    }

    /// <summary>Holds the current track where it is. Prefer this over Stop when the
    /// playlist is coming back — Resume picks the same track up mid-bar, where Stop
    /// restarts the whole running order.</summary>
    public void Pause() => audioSource.Pause();

    public void Resume()
    {
        stopped = false;
        // Nothing to un-pause if the playlist was stopped outright, or never started
        // because playOnStart was off — start it properly instead of silently no-oping.
        if (currentIndex < 0 || audioSource.clip == null) PlayNext();
        else audioSource.UnPause();
    }

    private void BuildPlayOrder()
    {
        playOrder.Clear();
        for (int i = 0; i < tracks.Count; i++)
            playOrder.Add(i);

        if (!shuffle) return;

        for (int i = playOrder.Count - 1; i > 0; i--)
        {
            int rand = Random.Range(0, i + 1);
            (playOrder[i], playOrder[rand]) = (playOrder[rand], playOrder[i]);
        }

        // A reshuffle at the end of a lap can deal the track that just finished into
        // first place, which plays it twice in a row across the loop point — the one
        // repeat a shuffle is supposed to prevent. Push it back one slot.
        if (playOrder.Count > 1 && playOrder[0] == currentIndex)
            (playOrder[0], playOrder[1]) = (playOrder[1], playOrder[0]);
    }
}