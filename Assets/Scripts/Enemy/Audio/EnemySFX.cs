using UnityEngine;
using System.Collections.Generic;

public enum SFXType { Laser, Bomb, Die, Pain }

/// <summary>
/// Restored global manager for the rest of the game (Player, UI, etc.).
/// Enemies no longer use this, as they have their own EnemyAudio component.
/// </summary>
public class SoundManager : MonoBehaviour
{
    public static SoundManager Instance { get; private set; }

    [Header("Global SFX Clips")]
    [SerializeField] private AudioClip laserClip;
    [SerializeField] private AudioClip bombClip;
    [SerializeField] private AudioClip dieClip;
    [SerializeField] private AudioClip painClip;

    private List<AudioSource> _pool = new();

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);
        
        // Build a basic pool for global sounds
        for (int i = 0; i < 10; i++)
        {
            var go = new GameObject($"Global_SFX_{i}");
            go.transform.SetParent(transform);
            var src = go.AddComponent<AudioSource>();
            src.playOnAwake = false;
            src.spatialBlend = 1f; // Standard 3D audio
            _pool.Add(src);
        }
    }

    public void PlaySFX(SFXType type, Vector3 position)
    {
        AudioClip clip = type switch
        {
            SFXType.Laser => laserClip,
            SFXType.Bomb  => bombClip,
            SFXType.Die   => dieClip,
            SFXType.Pain  => painClip,
            _             => null
        };
        
        if (clip == null) return;

        var src = GetFree();
        src.transform.position = position;
        src.clip = clip;
        src.Play();
    }

    private AudioSource GetFree()
    {
        foreach (var s in _pool) 
        {
            if (!s.isPlaying) return s;
        }
        return _pool[0]; 
    }
}