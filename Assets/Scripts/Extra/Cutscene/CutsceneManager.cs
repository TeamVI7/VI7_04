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

    [Header("Skip (Hold Key)")]
    public KeyCode skipKey = KeyCode.E;
    public float skipHoldDuration = 1.5f;
    public GameObject skipPromptRoot;       // Optional "Hold E to skip" UI
    public Image skipFillImage;             // Optional radial/bar fill driven by hold progress

    [Header("Audio Listener")]
    // The scene ships an AudioListener on every cutscene camera plus one on the
    // briefing UI, so two are live at once for the whole cutscene and Unity picks
    // a winner arbitrarily. The manager keeps exactly one enabled at a time.
    public AudioListener briefingListener;  // Optional; auto-detected if left empty

    private float skipHoldTimer;
    private bool sequenceEnded;

    // False during the hard cuts between cinematics, when there is nothing on
    // screen to skip past.
    private bool skipArmed;

    private AudioListener[] allListeners;

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
        
        if (skipFillImage != null) skipFillImage.fillAmount = 0f;
        SetSkipArmed(true);

        CacheListeners();

        StartCoroutine(PlayMasterSequence());
    }

    // ───────────────────────────────────────────────────────────────
    // AUDIO LISTENER ARBITRATION
    // ───────────────────────────────────────────────────────────────

    private void CacheListeners()
    {
        // Include inactive: the takeoff and in-flight cameras start disabled.
        allListeners = FindObjectsByType<AudioListener>(
            FindObjectsInactive.Include, FindObjectsSortMode.None);

        if (briefingListener != null) return;

        // Fall back to whichever listener is not parented under a cutscene camera.
        foreach (var listener in allListeners)
        {
            if (listener == null) continue;
            if (IsUnder(listener, boarding != null ? boarding.cutsceneCamera : null)) continue;
            if (IsUnder(listener, takeoff != null ? takeoff.cutsceneCamera : null)) continue;
            if (IsUnder(listener, inFlight != null ? inFlight.cutsceneCamera : null)) continue;

            briefingListener = listener;
            break;
        }
    }

    private static bool IsUnder(AudioListener listener, Transform root)
    {
        return root != null && listener.transform.IsChildOf(root);
    }

    /// Enables the listener belonging to <paramref name="cameraRoot"/> and disables
    /// every other one. Pass null for the briefing phase.
    private void UseListenerFor(Transform cameraRoot)
    {
        if (allListeners == null) return;

        AudioListener chosen = cameraRoot != null
            ? cameraRoot.GetComponentInChildren<AudioListener>(true)
            : briefingListener;

        // Never end up with zero listeners — that would silence the whole cutscene.
        if (chosen == null) chosen = briefingListener;
        if (chosen == null)
        {
            foreach (var listener in allListeners)
            {
                if (listener != null) { chosen = listener; break; }
            }
        }
        if (chosen == null) return;

        foreach (var listener in allListeners)
        {
            if (listener != null) listener.enabled = (listener == chosen);
        }
    }

    private void SetSkipArmed(bool armed)
    {
        skipArmed = armed;
        if (skipPromptRoot != null) skipPromptRoot.SetActive(armed);
    }

    IEnumerator PlayMasterSequence()
    {
        // Force all 3D cutscene cameras OFF while the text is running
        if (boarding != null && boarding.cutsceneCamera != null) boarding.cutsceneCamera.gameObject.SetActive(false);
        if (takeoff != null && takeoff.cutsceneCamera != null) takeoff.cutsceneCamera.gameObject.SetActive(false);
        if (inFlight != null && inFlight.cutsceneCamera != null) inFlight.cutsceneCamera.gameObject.SetActive(false);

        // BoardingCamera ships active in the scene, so its listener is live from
        // frame zero alongside the briefing one. Hand the briefing phase its own.
        UseListenerFor(null);

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
            yield return StartCoroutine(missionTitle.PlaySequence());
            yield return new WaitForSeconds(0.5f);
        }

        // ── STEP 3: HARD CUT TO BLACK & START CINEMATICS ───────────
        SetFadeInstant(true);
        yield return null; 

        // ── BOARDING START (Music keeps playing) ───────────────────
        if (boarding != null)
        {
            UseListenerFor(boarding.cutsceneCamera);
            yield return StartCoroutine(FadeIn(0.75f));
            yield return StartCoroutine(boarding.Play());

            SetFadeInstant(true);
            SetSkipArmed(false);
            yield return new WaitForSeconds(0.4f);
            SetSkipArmed(true);
        }

        // ── TAKEOFF START (Music keeps playing) ────────────────────
        if (takeoff != null)
        {
            UseListenerFor(takeoff.cutsceneCamera);
            yield return StartCoroutine(FadeIn(0.2f));
            yield return StartCoroutine(takeoff.Play());

            SetFadeInstant(true);
            SetSkipArmed(false);
            yield return new WaitForSeconds(0.4f);
            SetSkipArmed(true);
        }

        // ── IN FLIGHT START (Music keeps playing) ──────────────────
        if (inFlight != null)
        {
            UseListenerFor(inFlight.cutsceneCamera);
            yield return StartCoroutine(FadeIn(0.2f));
            yield return StartCoroutine(inFlight.Play());
        }

        // Past this point the sequence is on its way out; skipping it is moot.
        SetSkipArmed(false);

        // Final smooth fade out of the screen canvas
        yield return StartCoroutine(FadeOut(0.5f));

        GoToRappelScene();
    }

    void Update()
    {
        if (sequenceEnded) return;

        // During the hard cuts the screen is already black, so a hold would build
        // progress against nothing. Bleed it off instead of accumulating.
        if (!skipArmed)
        {
            skipHoldTimer = Mathf.Max(0f, skipHoldTimer - Time.unscaledDeltaTime * 2f);
            if (skipFillImage != null)
                skipFillImage.fillAmount = Mathf.Clamp01(skipHoldTimer / skipHoldDuration);
            return;
        }

        if (Input.GetKey(skipKey))
            skipHoldTimer += Time.unscaledDeltaTime;
        else
            skipHoldTimer = Mathf.Max(0f, skipHoldTimer - Time.unscaledDeltaTime * 2f);

        if (skipFillImage != null)
            skipFillImage.fillAmount = Mathf.Clamp01(skipHoldTimer / skipHoldDuration);

        if (skipHoldTimer >= skipHoldDuration) Skip();
    }

    private void Skip()
    {
        sequenceEnded = true;

        StopAllCoroutines();

        // Sub-cutscenes run their own nested coroutines and tweens; stopping only this
        // object's would leave cameras still gliding under the fade. Each sequence
        // kills the tweens it owns, by target.
        //
        // Deliberately NOT DOTween.KillAll(): that also kills tweens on
        // DontDestroyOnLoad objects elsewhere in the project. MV38Condor detaches its
        // takeoff AudioSource and relies on a DOFade(...).OnComplete(Destroy) to clean
        // it up — killing that tween strands the AudioSource across the scene load,
        // looping at full volume with nothing alive to destroy it.
        if (missionDescription != null) missionDescription.Stop();
        if (missionTitle != null) missionTitle.Stop();
        if (boarding != null) boarding.Stop();
        if (takeoff != null) takeoff.Stop();
        if (inFlight != null) inFlight.Stop();

        // Drop any in-flight fade so it can't fight SkipRoutine's fade to black.
        if (fadeImage != null) DOTween.Kill(fadeImage);

        StartCoroutine(SkipRoutine());
    }

    IEnumerator SkipRoutine()
    {
        SetSkipArmed(false);

        yield return StartCoroutine(FadeOut(0.35f));

        GoToRappelScene();
    }

    private void GoToRappelScene()
    {
        sequenceEnded = true;
        SetSkipArmed(false);

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
        if (fadeImage == null) yield break;
        yield return fadeImage.DOFade(0f, duration).WaitForCompletion();
    }

    public IEnumerator FadeOut(float duration)
    {
        if (fadeImage == null) yield break;
        yield return fadeImage.DOFade(1f, duration).WaitForCompletion();
    }
}