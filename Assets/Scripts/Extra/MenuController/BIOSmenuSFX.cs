using UnityEngine;

/// <summary>
/// MenuAudio — Standalone singleton for menu sound effects.
///
/// SETUP:
/// 1. Create empty GameObject in your Menu scene, name it "MenuAudio"
/// 2. Attach this script to it
/// 3. Drag 3 AudioClips into the slots in the Inspector
///
/// Tip: reuse your existing lineBeepSound, injectDoneSound, glitchSound
/// from IntroCameraBootUp — keeps the audio consistent across scenes.
/// </summary>
public class MenuAudio : MonoBehaviour
{
    public static MenuAudio Instance { get; private set; }

    [Header("Clips")]
    public AudioClip clipNavigate; // short tick — plays on ↑↓ row select
    public AudioClip clipConfirm;  // heavier click — plays on Enter / confirm
    public AudioClip clipOpen;     // whoosh/blip — plays on tab switch

    [Header("Volume")]
    [Range(0f, 1f)] public float navigateVolume = 0.4f;
    [Range(0f, 1f)] public float confirmVolume  = 0.7f;
    [Range(0f, 1f)] public float openVolume     = 0.5f;

    private AudioSource _source;

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        DontDestroyOnLoad(gameObject);

        _source = gameObject.AddComponent<AudioSource>();
        _source.playOnAwake = false;
    }

    public void PlayNavigate() => Play(clipNavigate, navigateVolume);
    public void PlayConfirm()  => Play(clipConfirm,  confirmVolume);
    public void PlayOpen()     => Play(clipOpen,      openVolume);

    void Play(AudioClip clip, float volume)
    {
        if (clip == null || _source == null) return;
        _source.PlayOneShot(clip, volume);
    }
}