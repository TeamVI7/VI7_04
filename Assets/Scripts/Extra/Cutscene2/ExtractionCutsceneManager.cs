using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using DG.Tweening;
using System.Collections;

/// Outro sequence: the player extracts, a bomber climbs out, its payload is
/// shown and released, the missile runs to the target structure, and the site
/// is destroyed. Same shape as CutsceneManager — hard cuts between shots, a
/// hold-to-skip, and one audio listener alive at a time.
public class ExtractionCutsceneManager : MonoBehaviour
{
    [Header("Cinematic Shots (played in order)")]
    public ExtractionCutscene extraction;
    public BomberAscentCutscene bomberAscent;
    public MissilePayloadCutscene missilePayload;
    public MissileFlightCutscene missileFlight;
    public NukeDetonationCutscene nukeDetonation;

    [Header("Cut Timing")]
    [Tooltip("Seconds of black between shots. Long enough to read as a cut, " +
             "short enough not to feel like a loading hitch.")]
    public float cutToBlackDuration = 0.4f;
    public float fadeInDuration = 0.25f;

    [Header("Scene Transition Configuration")]
    public string nextSceneName;
    public SceneTransitionConfig nextTransition;

    [Header("Global Fade Overlay")]
    public Image fadeImage;

    [Header("Music")]
    public AudioSource musicAudioSource;
    public AudioClip musicClip;
    public float targetMusicVolume = 0.5f;
    public float musicFadeInDuration = 2f;
    public float musicFadeOutDuration = 3f;

    [Header("Skip (Hold Key)")]
    public KeyCode skipKey = KeyCode.E;
    public float skipHoldDuration = 1.5f;
    public GameObject skipPromptRoot;       // Optional "Hold E to skip" UI
    public Image skipFillImage;             // Optional radial/bar fill driven by hold progress

    [Header("Audio Listener")]
    // Every shot camera ships its own AudioListener, so several are live at once
    // unless exactly one is enabled per shot.
    public AudioListener fallbackListener;  // used before the first shot starts

    private float skipHoldTimer;
    private bool sequenceEnded;

    // False during the hard cuts between shots, when there is nothing on screen
    // to skip past.
    private bool skipArmed;

    private AudioListener[] allListeners;

    void Start()
    {
        if (fadeImage != null)
        {
            fadeImage.gameObject.SetActive(true);
            SetFadeInstant(true);
        }

        if (musicAudioSource != null && musicClip != null)
        {
            musicAudioSource.clip = musicClip;
            musicAudioSource.volume = 0f;
            musicAudioSource.loop = true;
            musicAudioSource.Play();
            musicAudioSource.DOFade(targetMusicVolume, musicFadeInDuration);

            // Detached and made persistent so the track survives the handoff to
            // the next scene and can be faded out over it.
            musicAudioSource.transform.SetParent(null);
            DontDestroyOnLoad(musicAudioSource.gameObject);
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
        // Include inactive: every shot camera except the first starts disabled.
        allListeners = FindObjectsByType<AudioListener>(
            FindObjectsInactive.Include, FindObjectsSortMode.None);

        if (fallbackListener != null) return;

        foreach (var listener in allListeners)
        {
            if (listener == null) continue;
            if (IsUnder(listener, extraction      != null ? extraction.cutsceneCamera      : null)) continue;
            if (IsUnder(listener, bomberAscent    != null ? bomberAscent.cutsceneCamera    : null)) continue;
            if (IsUnder(listener, missilePayload  != null ? missilePayload.cutsceneCamera  : null)) continue;
            if (IsUnder(listener, missileFlight   != null ? missileFlight.cutsceneCamera   : null)) continue;
            if (IsUnder(listener, nukeDetonation  != null ? nukeDetonation.cutsceneCamera  : null)) continue;

            fallbackListener = listener;
            break;
        }
    }

    private static bool IsUnder(AudioListener listener, Transform root)
    {
        return root != null && listener.transform.IsChildOf(root);
    }

    /// Enables the listener belonging to <paramref name="cameraRoot"/> and disables
    /// every other one. Pass null before the first shot.
    private void UseListenerFor(Transform cameraRoot)
    {
        if (allListeners == null) return;

        AudioListener chosen = cameraRoot != null
            ? cameraRoot.GetComponentInChildren<AudioListener>(true)
            : fallbackListener;

        // Never end up with zero listeners — that would silence the whole cutscene.
        if (chosen == null) chosen = fallbackListener;
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
        // Force every shot camera off; each one is switched on by its own Play().
        DisableCamera(extraction     != null ? extraction.cutsceneCamera     : null);
        DisableCamera(bomberAscent   != null ? bomberAscent.cutsceneCamera   : null);
        DisableCamera(missilePayload != null ? missilePayload.cutsceneCamera : null);
        DisableCamera(missileFlight  != null ? missileFlight.cutsceneCamera  : null);
        DisableCamera(nukeDetonation != null ? nukeDetonation.cutsceneCamera : null);

        UseListenerFor(null);
        yield return null;

        // ── SHOT 1: EXTRACTION ────────────────────────────────
        if (extraction != null)
        {
            UseListenerFor(extraction.cutsceneCamera);
            yield return StartCoroutine(FadeIn(0.75f));
            yield return StartCoroutine(extraction.Play());
            yield return StartCoroutine(HardCut());
        }

        // ── SHOT 2: BOMBER CLIMBING OUT ───────────────────────
        if (bomberAscent != null)
        {
            UseListenerFor(bomberAscent.cutsceneCamera);
            yield return StartCoroutine(FadeIn(fadeInDuration));
            yield return StartCoroutine(bomberAscent.Play());
            yield return StartCoroutine(HardCut());
        }

        // ── SHOT 3: THE PAYLOAD ───────────────────────────────
        if (missilePayload != null)
        {
            UseListenerFor(missilePayload.cutsceneCamera);
            yield return StartCoroutine(FadeIn(fadeInDuration));
            yield return StartCoroutine(missilePayload.Play());
            yield return StartCoroutine(HardCut());
        }

        // ── SHOT 4: RUN TO THE TARGET ─────────────────────────
        if (missileFlight != null)
        {
            UseListenerFor(missileFlight.cutsceneCamera);
            yield return StartCoroutine(FadeIn(fadeInDuration));
            yield return StartCoroutine(missileFlight.Play());

            // Hand the real impact position to the detonation so the fireball
            // lands where the missile actually ended up.
            if (nukeDetonation != null)
                nukeDetonation.OverridePoint = missileFlight.ImpactPoint;

            yield return StartCoroutine(HardCut());
        }

        // ── SHOT 5: DETONATION ────────────────────────────────
        if (nukeDetonation != null)
        {
            UseListenerFor(nukeDetonation.cutsceneCamera);
            yield return StartCoroutine(FadeIn(fadeInDuration));
            yield return StartCoroutine(nukeDetonation.Play());
        }

        // Past this point the sequence is on its way out; skipping it is moot.
        SetSkipArmed(false);

        yield return StartCoroutine(FadeOut(1.5f));

        GoToNextScene();
    }

    private static void DisableCamera(Transform cam)
    {
        if (cam != null) cam.gameObject.SetActive(false);
    }

    /// Black frame between shots, with skip disarmed so a hold cannot build
    /// progress against a screen that has nothing on it.
    IEnumerator HardCut()
    {
        SetFadeInstant(true);
        SetSkipArmed(false);
        yield return new WaitForSeconds(cutToBlackDuration);
        SetSkipArmed(true);
    }

    void Update()
    {
        if (sequenceEnded) return;

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

        // Each shot runs its own nested coroutines and tweens; stopping only this
        // object's would leave cameras still gliding under the fade. Each shot
        // kills the tweens it owns, by target.
        //
        // Deliberately NOT DOTween.KillAll(): that also kills tweens on
        // DontDestroyOnLoad objects elsewhere in the project, including this
        // manager's own music fade-out.
        if (extraction     != null) extraction.Stop();
        if (bomberAscent   != null) bomberAscent.Stop();
        if (missilePayload != null) missilePayload.Stop();
        if (missileFlight  != null) missileFlight.Stop();
        if (nukeDetonation != null) nukeDetonation.Stop();

        // Drop any in-flight fade so it can't fight SkipRoutine's fade to black.
        if (fadeImage != null) DOTween.Kill(fadeImage);

        StartCoroutine(SkipRoutine());
    }

    IEnumerator SkipRoutine()
    {
        SetSkipArmed(false);

        yield return StartCoroutine(FadeOut(0.35f));

        GoToNextScene();
    }

    private void GoToNextScene()
    {
        sequenceEnded = true;
        SetSkipArmed(false);

        if (musicAudioSource != null)
        {
            musicAudioSource.DOFade(0f, musicFadeOutDuration).OnComplete(() => {
                Destroy(musicAudioSource.gameObject);
            });
        }

        if (LoadingScreenController.Instance != null && nextTransition != null)
            LoadingScreenController.Instance.BeginLoad(nextTransition.BuildSteps());
        else if (!string.IsNullOrEmpty(nextSceneName))
            SceneManager.LoadScene(nextSceneName);
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
