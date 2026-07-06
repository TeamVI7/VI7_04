using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using DG.Tweening;
using System.Collections;

public class CutsceneManager : MonoBehaviour
{
    [Header("Phase 1: Briefing Sequences (Runs First)")]
    public MissionDescriptionUI missionDescription; 
    public MissionTitleUI missionTitle;             

    [Header("Phase 2: Cinematic Cutscenes")]
    public BoardingCutscene boarding;
    public TakeoffCutscene takeoff; 
    public InFlightCutscene inFlight;
    
    [Header("Scene Transition Configuration")]
    public string rappelSceneName;                  
    public SceneTransitionConfig rappelTransition;  
    
    [Header("Global Fade Overlay")]
    public Image fadeImage;                         

    [Header("Persistent Briefing Music")]
    public AudioSource briefingAudioSource; // Assign your AudioSource here
    public AudioClip briefingMusicClip;     // Assign your briefing background track here
    public float targetMusicVolume = 0.5f;   // Max volume during cinmatics
    public float musicFadeOutDuration = 2.0f;// How fast music fades when transferring scenes

    void Start()
    {
        if (fadeImage != null)
        {
            fadeImage.gameObject.SetActive(true);
            SetFadeInstant(true); 
        }

        // ───────────────────────────────────────────────────────────
        // MASTER MUSIC INITIALIZATION
        // ───────────────────────────────────────────────────────────
        if (briefingAudioSource != null && briefingMusicClip != null)
        {
            briefingAudioSource.clip = briefingMusicClip;
            briefingAudioSource.volume = 0f;
            briefingAudioSource.loop = true;
            briefingAudioSource.Play();
            
            // Fade the track in smoothly over 1.5 seconds
            briefingAudioSource.DOFade(targetMusicVolume, 1.5f);

            // DETACH the audio object hierarchy and mark it persistent 
            // so it continues playing even when scenes change.
            briefingAudioSource.transform.SetParent(null);
            DontDestroyOnLoad(briefingAudioSource.gameObject);
        }
        
        StartCoroutine(PlayMasterSequence());
    }

    IEnumerator PlayMasterSequence()
    {
        // Force all 3D cutscene cameras OFF while the text is running
        if (boarding != null && boarding.cutsceneCamera != null) boarding.cutsceneCamera.gameObject.SetActive(false);
        if (takeoff != null && takeoff.cutsceneCamera != null) takeoff.cutsceneCamera.gameObject.SetActive(false);
        if (inFlight != null && inFlight.cutsceneCamera != null) inFlight.cutsceneCamera.gameObject.SetActive(false);

        // ── STEP 1: RUN THE MISSION DESCRIPTION ────────────────────
        if (missionDescription != null)
        {
            yield return StartCoroutine(missionDescription.PlayBriefingSequence());
            yield return new WaitForSeconds(0.5f); 
        }

        // ── STEP 2: RUN THE MISSION TITLE SYSTEM ───────────────────
        if (missionTitle != null)
        {
            missionTitle.gameObject.SetActive(true);
            float totalTitleTime = 0.5f + (missionTitle.entries.Length * missionTitle.delayBetweenLines) + missionTitle.holdDuration + missionTitle.fadeOutDuration;
            
            yield return new WaitForSeconds(totalTitleTime);
            yield return new WaitForSeconds(0.5f); 
        }

        // ── STEP 3: HARD CUT TO BLACK & START CINEMATICS ───────────
        SetFadeInstant(true);
        yield return null; 

        // ── BOARDING START (Music keeps playing) ───────────────────
        yield return StartCoroutine(FadeIn(0.75f)); 
        yield return StartCoroutine(boarding.Play());
        
        SetFadeInstant(true); 
        yield return new WaitForSeconds(0.4f); 

        // ── TAKEOFF START (Music keeps playing) ────────────────────
        yield return StartCoroutine(FadeIn(0.2f));
        yield return StartCoroutine(takeoff.Play());
        
        SetFadeInstant(true);
        yield return new WaitForSeconds(0.4f);

        // ── IN FLIGHT START (Music keeps playing) ──────────────────
        yield return StartCoroutine(FadeIn(0.2f));
        yield return StartCoroutine(inFlight.Play());
        
        // Final smooth fade out of the screen canvas
        yield return StartCoroutine(FadeOut(0.5f));

        // ───────────────────────────────────────────────────────────
        // SCENE TRANSFER: CROSSFADE BRIEFING MUSIC OUT 
        // ───────────────────────────────────────────────────────────
        if (briefingAudioSource != null)
        {
            briefingAudioSource.DOFade(0f, musicFadeOutDuration).OnComplete(() => {
                Destroy(briefingAudioSource.gameObject);
            });
        }

        // Handoff to loading scene
        if (LoadingScreenController.Instance != null)
            LoadingScreenController.Instance.BeginLoad(rappelTransition.BuildSteps());
        else
            SceneManager.LoadScene(rappelSceneName); 
    }

    public void SetFadeInstant(bool blankScreen)
    {
        if (fadeImage != null)
        {
            fadeImage.color = new Color(0f, 0f, 0f, blankScreen ? 1f : 0f);
        }
    }

    public IEnumerator FadeIn(float duration)
    {
        yield return fadeImage.DOFade(0f, duration).WaitForCompletion();
    }

    public IEnumerator FadeOut(float duration)
    {
        yield return fadeImage.DOFade(1f, duration).WaitForCompletion();
    }
}