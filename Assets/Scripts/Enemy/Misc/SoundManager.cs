using UnityEngine;
using System.Collections.Generic;

public class SoundManager : MonoBehaviour
{
    public static SoundManager Instance { get; private set; }

    [Header("SFX Clips")]
    [SerializeField] private AudioClip walkingClip;
    [SerializeField] private AudioClip laserClip;
    [SerializeField] private AudioClip bombClip;
    [SerializeField] private AudioClip dieClip;
    [SerializeField] private AudioClip painClip;  // FIX: wired-up pain clip

    [Header("Pool Settings")]
    [SerializeField] private int poolSize = 8;

    private List<AudioSource> _pool = new();

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);
        BuildPool();
    }

    void BuildPool()
    {
        for (int i = 0; i < poolSize; i++)
        {
            var go  = new GameObject($"SFX_Pool_{i}");
            go.transform.SetParent(transform);
            var src = go.AddComponent<AudioSource>();
            src.playOnAwake = false;
            src.loop        = false;
            _pool.Add(src);
        }
    }

    private AudioSource GetFreeSource()
    {
        foreach (var src in _pool)
            if (!src.isPlaying) return src;

        Debug.LogWarning("[SoundManager] Pool exhausted!");
        return _pool[0];
    }

    public void PlaySFX(SFXType type, Vector3 position)
    {
        AudioClip clip = type switch
        {
            SFXType.Laser => laserClip,
            SFXType.Bomb  => bombClip,
            SFXType.Die   => dieClip,
            SFXType.Pain  => painClip,  // FIX: Pain now resolved
            _             => null
        };
        if (clip == null) return;

        var src = GetFreeSource();
        src.transform.position = position;
        src.clip         = clip;
        src.spatialBlend = 1f;
        src.Play();
    }

    public void SetupWalkingSource(AudioSource entitySource)
    {
        entitySource.clip        = walkingClip;
        entitySource.loop        = true;
        entitySource.spatialBlend = 1f;
        entitySource.playOnAwake  = false;
    }
}

public enum SFXType { Laser, Bomb, Die, Pain }  // FIX: Pain added