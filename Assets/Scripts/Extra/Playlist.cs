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
        if (currentIndex >= 0 && !audioSource.isPlaying && audioSource.time == 0f)
            PlayNext();
    }

    public void Play(int trackIndex)
    {
        if (trackIndex < 0 || trackIndex >= tracks.Count) return;
        currentIndex = trackIndex;
        audioSource.clip = tracks[trackIndex];
        audioSource.Play();
    }

    public void PlayNext()
    {
        if (tracks.Count == 0) return;

        int orderPos = playOrder.IndexOf(currentIndex) + 1;

        if (orderPos >= playOrder.Count)
        {
            if (!loopPlaylist)
            {
                currentIndex = -1;
                return;
            }
            BuildPlayOrder();
            orderPos = 0;
        }

        Play(playOrder[orderPos]);
    }

    public void PlayPrevious()
    {
        if (tracks.Count == 0) return;

        int orderPos = playOrder.IndexOf(currentIndex) - 1;
        if (orderPos < 0) orderPos = playOrder.Count - 1;

        Play(playOrder[orderPos]);
    }

    public void Stop() => audioSource.Stop();
    public void Pause() => audioSource.Pause();
    public void Resume() => audioSource.UnPause();

    private void BuildPlayOrder()
    {
        playOrder.Clear();
        for (int i = 0; i < tracks.Count; i++)
            playOrder.Add(i);

        if (shuffle)
        {
            for (int i = playOrder.Count - 1; i > 0; i--)
            {
                int rand = Random.Range(0, i + 1);
                (playOrder[i], playOrder[rand]) = (playOrder[rand], playOrder[i]);
            }
        }
    }
}