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

    [Header("Instant Cuts")]
    // A dip to black puts a beat between two shots — useful when the shots are
    // unrelated in space and the audience needs a moment to re-orient. It is
    // exactly wrong for a cause-and-effect pair: the missile is in the air, then
    // the sky is on fire. Cutting straight between two live frames is what makes
    // that land, and any black at all softens it into two separate events.
    //
    // A shot flagged here skips both the black hold and the fade in.
    public bool instantCutToExtraction;
    public bool instantCutToBomberAscent;
    public bool instantCutToMissilePayload;
    public bool instantCutToMissileFlight;
    public bool instantCutToNukeDetonation = true;

    [Header("Scene Transition Configuration")]
    public string nextSceneName;
    public SceneTransitionConfig nextTransition;

    [Header("Atmosphere")]
    [Tooltip("Optional. Drives the volumetric cloud wind per shot so the sky is " +
             "not a static backdrop behind a climbing aircraft.")]
    public CutsceneAtmosphere atmosphere;

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

    [Header("Persistent Gameplay Objects")]
    // The player is not authored in the gameplay scene — it rides a
    // DontDestroyOnLoad root, so loading this scene does not take it with the
    // level. It arrives here with its camera, its HUD canvas and its input all
    // still live, drawing and listening over the top of the cutscene.
    public bool disablePersistentPlayer = true;
    public string playerTag = "Player";

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

            // Forced 2D. This source is left standing in world space while the
            // AudioListener is teleported from shot camera to shot camera, and
            // those are kilometres apart — any spatial blend at all and the
            // music drops out of rolloff range on the first cut, which reads as
            // the track cutting off exactly when the camera changes.
            musicAudioSource.spatialBlend = 0f;
            musicAudioSource.Play();
            musicAudioSource.DOFade(targetMusicVolume, musicFadeInDuration);

            // Detached and made persistent so the track survives the handoff to
            // the next scene and can be faded out over it.
            musicAudioSource.transform.SetParent(null);
            DontDestroyOnLoad(musicAudioSource.gameObject);
        }

        if (skipFillImage != null) skipFillImage.fillAmount = 0f;
        SetSkipArmed(true);

        // Before CacheListeners: the player carries an AudioListener, and a
        // listener belonging to an object we are about to switch off must not be
        // the one the fallback lands on.
        DisablePersistentPlayer();

        CacheListeners();

        StartCoroutine(PlayMasterSequence());
    }

    // ───────────────────────────────────────────────────────────────
    // GAMEPLAY TEARDOWN
    // ───────────────────────────────────────────────────────────────

    /// Switches off the persistent player root that followed us in from the
    /// gameplay scene.
    ///
    /// Deactivated rather than destroyed: PauseMenuController and the restart
    /// bookkeeping share that root, and a cutscene is not the thing that gets to
    /// decide they are gone for good. Deactivating is also reversible, which
    /// matters if this sequence is ever reused mid-campaign rather than as an
    /// outro.
    private void DisablePersistentPlayer()
    {
        if (!disablePersistentPlayer) return;

        GameObject player = GameObject.FindGameObjectWithTag(playerTag);
        if (player == null) return;

        // The HUD sits NEXT TO the player on that root rather than under it, so
        // switching off the player alone leaves the health bar and the crosshair
        // drawn over the cutscene. Taking the root takes both, plus the pause
        // menu that would otherwise still answer the Escape key mid-shot.
        player.transform.root.gameObject.SetActive(false);
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

            // Shot cameras start disabled and are legitimately skipped below.
            // Anything else that is inactive is inactive on purpose — the player
            // root we just switched off — and picking it would leave the opening
            // of the sequence with a listener that cannot hear anything.
            if (!listener.gameObject.activeInHierarchy &&
                listener.transform.root.gameObject.activeSelf == false) continue;

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
                if (listener == null) continue;

                // Skip anything sitting on a root we switched off — the player.
                // Enabling a listener on an inactive object satisfies the "one
                // listener" rule on paper and produces total silence in practice,
                // which is the worst of both outcomes.
                if (!listener.transform.root.gameObject.activeSelf) continue;

                chosen = listener;
                break;
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
            // Opens from the black Start() already left on screen, so there is
            // no beat to hold first — just the reveal.
            if (!BeginShotInstant(extraction.cutsceneCamera, instantCutToExtraction))
                yield return StartCoroutine(DipCut(0f, 0.75f));

            if (atmosphere != null) atmosphere.EnterShot(1);
            yield return StartCoroutine(extraction.Play());
        }

        // ── SHOT 2: BOMBER CLIMBING OUT ───────────────────────
        if (bomberAscent != null)
        {
            if (!BeginShotInstant(bomberAscent.cutsceneCamera, instantCutToBomberAscent))
                yield return StartCoroutine(DipCut(cutToBlackDuration, fadeInDuration));

            if (atmosphere != null) atmosphere.EnterShot(2);
            yield return StartCoroutine(bomberAscent.Play());
        }

        // ── SHOT 3: THE PAYLOAD ───────────────────────────────
        if (missilePayload != null)
        {
            if (!BeginShotInstant(missilePayload.cutsceneCamera, instantCutToMissilePayload))
                yield return StartCoroutine(DipCut(cutToBlackDuration, fadeInDuration));

            if (atmosphere != null) atmosphere.EnterShot(3);
            yield return StartCoroutine(missilePayload.Play());
        }

        // ── SHOT 4: RUN TO THE TARGET ─────────────────────────
        if (missileFlight != null)
        {
            if (!BeginShotInstant(missileFlight.cutsceneCamera, instantCutToMissileFlight))
                yield return StartCoroutine(DipCut(cutToBlackDuration, fadeInDuration));

            if (atmosphere != null) atmosphere.EnterShot(4);
            yield return StartCoroutine(missileFlight.Play());

            // Hand the real impact position to the detonation so the fireball
            // lands where the missile actually ended up. On an instant cut the
            // detonation begins on this same frame, so this has to be set before
            // anything yields.
            if (nukeDetonation != null)
                nukeDetonation.OverridePoint = missileFlight.ImpactPoint;
        }

        // ── SHOT 5: DETONATION ────────────────────────────────
        if (nukeDetonation != null)
        {
            if (!BeginShotInstant(nukeDetonation.cutsceneCamera, instantCutToNukeDetonation))
                yield return StartCoroutine(DipCut(cutToBlackDuration, fadeInDuration));

            if (atmosphere != null) atmosphere.EnterShot(5);
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

    /// Straight cut into the next shot. Deliberately NOT a coroutine: it must
    /// not cost a frame.
    ///
    /// The outgoing shot's Play() has just switched its own camera off, and the
    /// incoming shot's Play() switches its own on. Between those two points
    /// nothing may yield, or Unity renders a frame with no camera live and the
    /// "instant" cut shows a black flash — the exact thing it exists to avoid.
    /// Hence the camera is also switched on here rather than waiting for Play(),
    /// which covers the frame boundary that yielding on a nested coroutine can
    /// introduce.
    ///
    /// Returns false when this join is a dip, leaving the caller to run DipCut.
    private bool BeginShotInstant(Transform cameraRoot, bool instant)
    {
        UseListenerFor(cameraRoot);

        if (!instant) return false;

        // A fade still in flight would keep writing alpha over a cut that is
        // supposed to already be clear.
        if (fadeImage != null) DOTween.Kill(fadeImage);

        SetFadeInstant(false);
        if (cameraRoot != null) cameraRoot.gameObject.SetActive(true);
        SetSkipArmed(true);

        return true;
    }

    /// Black between shots, then back up. Skip is disarmed across the black so a
    /// hold cannot build progress against a screen with nothing on it.
    IEnumerator DipCut(float blackHold, float fadeDuration)
    {
        SetFadeInstant(true);
        SetSkipArmed(false);

        if (blackHold > 0f) yield return new WaitForSeconds(blackHold);

        SetSkipArmed(true);
        yield return StartCoroutine(FadeIn(fadeDuration));
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
