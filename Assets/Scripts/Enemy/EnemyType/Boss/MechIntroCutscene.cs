// MechIntroCutscene.cs
// Directs the mech boss's drop-in as a scripted cutscene, matching the project's
// existing cutscene convention (see Extra/Cutscene/*): a pre-placed cutsceneCamera
// swapped in over the player camera, a black fadePanel for hard cuts, and a
// DOTween coroutine sequence.
//
// Beats: cut to black -> cut to a low dramatic angle watching the drop zone ->
// slow motion during the fall -> snap to real time + camera shake on impact ->
// push in on the reveal (with an optional name card) -> hold -> cut back to the
// player.
//
// Drop this on the same GameObject as MechBossBrain and assign cutsceneCamera
// (a Camera placed in the scene at the low-angle shot). Everything else is
// optional polish. With no cutsceneCamera assigned, the boss just drops in
// immediately (see MechBossBrain.Start), so this component is purely additive.

using System;
using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using DG.Tweening;

[RequireComponent(typeof(MechBossBrain))]
public class MechIntroCutscene : MonoBehaviour
{
    #region Events

    /// <summary>Fired once the full cutscene (including the cut back to the player
    /// camera) has finished. External flow controllers (e.g. a minigame sequencer
    /// that owns its own input lock) can wait on this instead of a fixed timer.</summary>
    public event Action OnCutsceneComplete;

    #endregion

    #region Inspector

    [Header("Cameras")]
    [Tooltip("Leave empty to auto-find Camera.main.")]
    public Camera playerCamera;
    [Tooltip("The FPS look script on the player camera — disabled for the duration so it can't fight the cutscene camera. Auto-found on playerCamera if left empty.")]
    public PlayerCam playerCamScript;
    [Tooltip("REQUIRED. Pre-placed low-angle shot watching the drop zone. Leave its GameObject inactive in the scene — this script activates/deactivates it.")]
    public Camera cutsceneCamera;
    [Tooltip("Optional. Where cutsceneCamera pushes in to for the post-impact reveal. If empty, it just dollies forward along its own facing.")]
    public Transform pushInPoint;
    [Tooltip("Used when pushInPoint is empty — how far forward (local Z) to dolly in.")]
    public float pushInDistance = 4f;
    [Tooltip("Optional. What the cutscene camera looks at while the mech falls. Falls back to the brain's visualRoot.")]
    public Transform lookTarget;

    [Header("Timing")]
    public float pushInDuration = 1.2f;
    public float holdDuration = 1.6f;
    public float cutFadeDuration = 0.25f;

    [Header("Slow Motion")]
    [Range(0.05f, 1f)] public float slowMoScale = 0.3f;
    public float slowMoTweenTime = 0.2f;

    [Header("Impact Shake")]
    public float impactShakeStrength = 0.6f;
    public float impactShakeDuration = 0.4f;
    public int impactShakeVibrato = 12;

    [Header("Fade Panel (optional — black CanvasGroup for hard cuts)")]
    public CanvasGroup fadePanel;

    [Header("Letterbox (auto-built if left unassigned)")]
    public bool useLetterbox = true;
    public float letterboxHeight = 120f;
    public float letterboxTweenDuration = 0.4f;

    [Header("Name Card (optional)")]
    public TextMeshProUGUI nameCardText;
    public CanvasGroup nameCardGroup;
    public string bossName = "MECH";

    [Header("Input Lock")]
    [Tooltip("If true, this script locks/unlocks the player itself via PlayerActionLock. Turn off when an external controller (e.g. a minigame flow controller) already owns input blocking for this sequence, so the two don't fight or double-unlock.")]
    public bool manageInputLock = true;

    [Header("Safety")]
    [Tooltip("Hard cap on the whole sequence. If the boss's intro never reports completion — a script fault mid-drop, a missing animation event — the cutscene cuts back to the player anyway after this many seconds rather than leaving them stuck behind a camera they don't control. Measured in unscaled time, so slow-mo doesn't stretch it.")]
    public float maxCutsceneDuration = 12f;

    [Header("Debug")]
    [SerializeField] private bool showDebugLogs = true;

    #endregion

    private const string TimeScaleTweenId = "MechIntroTimeScale";

    private MechBossBrain _brain;
    private GameObject _letterboxCanvas;
    private RectTransform _barTop, _barBottom;
    private bool _tracking;
    private bool _cutBackStarted;

    // The impact shake is kept as an offset applied in LateUpdate rather than as a
    // DOShakePosition on the camera transform. Both the shake and the reveal's
    // push-in drive the same transform, and whichever tween DOTween evaluates last
    // wins the frame — the push-in is created second, so it silently swallowed the
    // shake entirely. Layering an offset on top composes instead of competing.
    private Vector3 _shakeOffset;
    private Vector3 _appliedShake;

    // OnIntroComplete is a *gameplay* signal — the boss brain fires it the
    // instant landing settles, which is a fixed, short window. The reveal
    // (push-in + hold) is a *camera* beat on its own independent timer
    // (holdDuration) that in practice runs longer. Gating the cut on both
    // means whichever finishes last decides when the camera actually cuts —
    // without this, the cutscene camera turns off mid push-in almost every run.
    private bool _introReported;
    private bool _revealStarted;
    private bool _revealFinished;
    private Tween _pushInTween;

    #region Unity Lifecycle

    private void Awake()
    {
        _brain = GetComponent<MechBossBrain>();

        if (playerCamera == null) playerCamera = Camera.main;
        if (playerCamScript == null && playerCamera != null) playerCamScript = playerCamera.GetComponent<PlayerCam>();

        SetCam(cutsceneCamera, false);
        if (useLetterbox) BuildLetterbox();
    }

    private void Start()
    {
        StartCoroutine(Co_Direct());
    }

    // LateUpdate, so this runs after DOTween has written the push-in's position for
    // the frame: aim from where the camera actually ended up, then add the shake.
    private void LateUpdate()
    {
        if (cutsceneCamera == null) return;

        Transform cam = cutsceneCamera.transform;

        // Undo last frame's offset first. While the push-in tween is running it
        // rewrites the position anyway, but once it finishes the offset would
        // otherwise accumulate and walk the camera away.
        if (_appliedShake != Vector3.zero)
        {
            cam.position -= _appliedShake;
            _appliedShake = Vector3.zero;
        }

        if (_tracking)
        {
            // IntroFocus, not visualRoot — with a drop stand-in the real mech is
            // invisible and stationary on the ground, so tracking it would leave the
            // camera staring at nothing while the drop happens above.
            Transform target = lookTarget != null ? lookTarget : _brain.IntroFocus;
            if (target != null) cam.LookAt(target);
        }

        if (_shakeOffset != Vector3.zero)
        {
            cam.position += _shakeOffset;
            _appliedShake = _shakeOffset;
        }
    }

    /// <summary>Last line of defence for the global state this component touches.
    /// The slow-mo tween, the watchdog and both restore paths all live on this
    /// GameObject — if the boss is destroyed mid-intro they die with it, and the
    /// player is left running at 30% speed for the rest of the session.</summary>
    private void OnDestroy()
    {
        DOTween.Kill(TimeScaleTweenId);
        if (!_cutBackStarted) Time.timeScale = 1f;

        _pushInTween?.Kill();

        if (_brain != null)
        {
            _brain.OnIntroImpact -= HandleImpact;
            _brain.OnIntroComplete -= HandleIntroComplete;
        }

        // Built in Awake and parented to nothing, so it outlives the boss unless
        // it's cleaned up here — one orphaned canvas per boss spawn otherwise.
        if (_letterboxCanvas != null) Destroy(_letterboxCanvas);
    }

    #endregion

    #region Sequence

    private IEnumerator Co_Direct()
    {
        if (cutsceneCamera == null)
        {
            Log("No cutsceneCamera assigned — skipping cutscene, letting boss drop in immediately.");
            _brain.BeginIntro();
            yield break;
        }

        // Re-resolve here (not just in Awake) — an external spawner (e.g. a minigame
        // flow controller) may reassign playerCamera right after Instantiate, before
        // Start runs, and that shouldn't leave us holding Camera.main's PlayerCam instead.
        if (playerCamera != null && (playerCamScript == null || playerCamScript.gameObject != playerCamera.gameObject))
            playerCamScript = playerCamera.GetComponent<PlayerCam>();

        if (manageInputLock) PlayerActionLock.Instance?.SetLock(PlayerActionLock.LockReason.Custom1, true);
        if (playerCamScript != null) playerCamScript.enabled = false;

        if (fadePanel != null)
        {
            fadePanel.alpha = 0f;
            yield return fadePanel.DOFade(1f, cutFadeDuration).WaitForCompletion();
        }

        // Cut: swap to the low-angle cutscene shot.
        SetCam(playerCamera, false);
        SetCam(cutsceneCamera, true);
        _tracking = true;

        if (useLetterbox) ShowLetterbox();

        if (fadePanel != null)
            yield return fadePanel.DOFade(0f, cutFadeDuration).WaitForCompletion();

        _brain.OnIntroImpact += HandleImpact;
        _brain.OnIntroComplete += HandleIntroComplete;

        DOTween.To(() => Time.timeScale, v => Time.timeScale = v, slowMoScale, slowMoTweenTime)
            .SetUpdate(true)
            .SetId(TimeScaleTweenId);

        _brain.BeginIntro();

        if (maxCutsceneDuration > 0f) StartCoroutine(Co_Watchdog());
    }

    /// <summary>Backstop for the whole sequence. Everything past the camera cut
    /// depends on the brain's events firing; if the intro coroutine dies partway
    /// (a solver exception, a never-raised animation event) none of them arrive
    /// and the player is left with no camera control and a slowed clock forever.</summary>
    private IEnumerator Co_Watchdog()
    {
        float elapsed = 0f;
        while (elapsed < maxCutsceneDuration && !_cutBackStarted)
        {
            elapsed += Time.unscaledDeltaTime;
            yield return null;
        }

        if (_cutBackStarted) yield break;

        Debug.LogWarning($"[MechIntroCutscene] Boss intro never completed within {maxCutsceneDuration}s — " +
                         "forcing the cut back to the player. Check the Console for an earlier exception " +
                         "thrown inside MechBossBrain.Co_Intro.", this);

        // Force path — don't wait on the reveal, something's already broken.
        // Kill the push-in tween rather than let it keep moving a camera that's
        // about to be disabled anyway.
        _pushInTween?.Kill();
        _revealFinished = true;
        HandleIntroComplete();
    }

    private void HandleImpact()
    {
        _brain.OnIntroImpact -= HandleImpact;

        DOTween.To(() => Time.timeScale, v => Time.timeScale = v, 1f, slowMoTweenTime)
            .SetUpdate(true)
            .SetId(TimeScaleTweenId);

        // Shakes a plain offset, not the transform — see _shakeOffset. Unscaled so
        // the landing still hits hard while the clock is ramping back up.
        DOTween.Shake(() => _shakeOffset, v => _shakeOffset = v,
                      impactShakeDuration, impactShakeStrength, impactShakeVibrato,
                      90f, false, true)
               .SetUpdate(true)
               .OnKill(() => _shakeOffset = Vector3.zero);

        StartCoroutine(Co_Reveal());
    }

    private IEnumerator Co_Reveal()
    {
        _revealStarted = true;

        if (nameCardGroup != null) ShowNameCard();

        Vector3 pushTo = pushInPoint != null
            ? pushInPoint.position
            : cutsceneCamera.transform.position + cutsceneCamera.transform.forward * pushInDistance;

        _pushInTween = cutsceneCamera.transform.DOMove(pushTo, pushInDuration).SetEase(Ease.OutQuad);

        yield return new WaitForSeconds(holdDuration);

        if (nameCardGroup != null) HideNameCard();

        _revealFinished = true;
        TryStartCutBack();
    }

    private void HandleIntroComplete()
    {
        _brain.OnIntroComplete -= HandleIntroComplete;
        // Safe whether or not it's still subscribed — stops a late impact event
        // from kicking off a push-in after we've already cut back.
        _brain.OnIntroImpact -= HandleImpact;

        _introReported = true;
        TryStartCutBack();
    }

    /// <summary>Only actually cuts once both halves are done: the brain reports
    /// the boss landed, AND the camera's own reveal beat has played out (or never
    /// started at all — e.g. impact fired but Co_Reveal hasn't been reached yet
    /// is impossible since HandleImpact starts it synchronously, but guarding
    /// keeps this safe regardless of call order).</summary>
    private void TryStartCutBack()
    {
        if (_cutBackStarted) return;
        if (!_introReported) return;
        if (_revealStarted && !_revealFinished) return; // reveal still playing — let it finish

        _cutBackStarted = true;
        StartCoroutine(Co_CutBack());
    }

    private IEnumerator Co_CutBack()
    {
        _tracking = false;
        _pushInTween?.Kill();

        // Hard-restore the clock. If the intro died before HandleImpact ran, the
        // slow-mo tween never got its counterpart and timeScale would sit at
        // slowMoScale for the rest of the session.
        DOTween.Kill(TimeScaleTweenId);
        Time.timeScale = 1f;

        if (fadePanel != null)
            yield return fadePanel.DOFade(1f, cutFadeDuration).WaitForCompletion();

        if (useLetterbox) HideLetterbox();

        SetCam(cutsceneCamera, false);
        SetCam(playerCamera, true);

        if (fadePanel != null)
            yield return fadePanel.DOFade(0f, cutFadeDuration).WaitForCompletion();

        if (playerCamScript != null) playerCamScript.enabled = true;

        // PlayerCam only locks the cursor once, in its own Start() — it never
        // reasserts this later, so if anything during the cutscene (UI, other
        // systems) left the cursor unlocked/visible it would stay that way
        // forever otherwise.
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;

        if (manageInputLock) PlayerActionLock.Instance?.SetLock(PlayerActionLock.LockReason.Custom1, false);

        Log("Cutscene complete — control returned to player.");
        OnCutsceneComplete?.Invoke();
    }

    /// <summary>Enables/disables a camera the same way the rest of the project's
    /// cutscene cameras are toggled (Camera.enabled + its AudioListener), not
    /// GameObject.SetActive — so this plays nicely with systems (e.g.
    /// MinigameFlowController) that keep the camera's GameObject alive and only
    /// flip the render/listener state.</summary>
    private static void SetCam(Camera cam, bool state)
    {
        if (cam == null) return;
        cam.enabled = state;
        if (cam.TryGetComponent(out AudioListener listener)) listener.enabled = state;
    }

    #endregion

    #region Letterbox

    private void BuildLetterbox()
    {
        GameObject canvasGO = new GameObject("MechCutsceneLetterbox", typeof(Canvas), typeof(CanvasScaler));
        _letterboxCanvas = canvasGO;
        Canvas canvas = canvasGO.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 100;
        CanvasScaler scaler = canvasGO.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);

        _barTop = CreateBar("LetterboxTop", canvas.transform, new Vector2(0.5f, 1f));
        _barBottom = CreateBar("LetterboxBottom", canvas.transform, new Vector2(0.5f, 0f));
    }

    private RectTransform CreateBar(string name, Transform parent, Vector2 anchor)
    {
        GameObject go = new GameObject(name, typeof(RectTransform), typeof(Image));
        go.transform.SetParent(parent, false);
        go.GetComponent<Image>().color = Color.black;

        RectTransform rt = go.GetComponent<RectTransform>();
        rt.anchorMin = anchor;
        rt.anchorMax = anchor;
        rt.pivot = anchor;
        rt.sizeDelta = new Vector2(0f, letterboxHeight);
        // Off-screen until ShowLetterbox() slides these in.
        rt.anchoredPosition = new Vector2(0f, anchor.y > 0.5f ? letterboxHeight : -letterboxHeight);
        return rt;
    }

    // Null-guarded: useLetterbox can be switched on at runtime after Awake already
    // decided not to build the bars.
    private void ShowLetterbox()
    {
        if (_barTop == null || _barBottom == null) return;
        _barTop.DOAnchorPosY(0f, letterboxTweenDuration).SetEase(Ease.OutQuad);
        _barBottom.DOAnchorPosY(0f, letterboxTweenDuration).SetEase(Ease.OutQuad);
    }

    private void HideLetterbox()
    {
        if (_barTop == null || _barBottom == null) return;
        _barTop.DOAnchorPosY(letterboxHeight, letterboxTweenDuration).SetEase(Ease.InQuad);
        _barBottom.DOAnchorPosY(-letterboxHeight, letterboxTweenDuration).SetEase(Ease.InQuad);
    }

    #endregion

    #region Name Card

    private void ShowNameCard()
    {
        if (nameCardText != null) nameCardText.text = bossName;
        nameCardGroup.DOFade(1f, 0.5f).SetEase(Ease.OutQuad);
    }

    private void HideNameCard() => nameCardGroup.DOFade(0f, 0.5f).SetEase(Ease.InQuad);

    #endregion

    #region Debug

    [System.Diagnostics.Conditional("UNITY_EDITOR")]
    private void Log(string msg)
    {
        if (showDebugLogs) Debug.Log($"[MechIntroCutscene] {msg}", this);
    }

    #endregion
}
