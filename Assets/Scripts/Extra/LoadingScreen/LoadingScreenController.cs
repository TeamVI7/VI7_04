using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Orchestrates a sequence of ILoadingStep instances and exposes a single
/// blended 0-1 progress value + status label via events. Holds no visual
/// logic — LoadingBIOSDisplay (or any other UI) subscribes to these events.
///
/// USAGE:
///   var controller = gameObject.AddComponent&lt;LoadingScreenController&gt;();
///   controller.BeginLoad(new List&lt;ILoadingStep&gt; {
///       new SceneLoadStep("Level_02", weight: 0.8f),
///       new ShaderWarmupStep(myCollections, weight: 0.2f)
///   });
///
/// EXTEND:
///   Add new ILoadingStep implementations and pass them into BeginLoad — the
///   controller doesn't know or care what kind of work each step does.
///
/// DEBUG:
///   Enable debugLog to see per-step start/finish and blended progress ticks
///   in the console. Each tick is throttled (progressLogInterval) so it
///   doesn't spam every frame.
///
///   In the Editor, press debugTestKey (F9 default) at any time to fire a
///   fake load (SceneLoadStep skipped, just a timed FakeDelayStep) so you
///   can iterate on the BIOS visuals without playing through cutscenes/menus
///   to trigger a real transition. Editor-only — compiled out of builds.
/// </summary>
public class LoadingScreenController : MonoBehaviour
{
    // ─────────────────────────────────────────────────────────────────────────
    #region Singleton / Persistence
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Tick if this lives in a persistent bootstrap scene and should survive
    /// every subsequent SceneManager.LoadSceneAsync call (the usual setup).
    /// Untick only if you're deliberately rebuilding the loading screen fresh
    /// in every scene instead — uncommon, but supported.
    /// </summary>
    [Header("Persistence")]
    [Tooltip("Survives scene loads via DontDestroyOnLoad. Self-destructs if " +
             "a duplicate instance is ever loaded — make sure your bootstrap " +
             "scene only contains ONE of these.")]
    public bool persistAcrossScenes = true;

    public static LoadingScreenController Instance { get; private set; }

    private void Awake()
    {
        if (!persistAcrossScenes) return;

        if (Instance != null && Instance != this)
        {
            LogWarning("Duplicate LoadingScreenController found — destroying this one. " +
                       "Check your bootstrap scene only has one instance.");
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    #endregion

    // ─────────────────────────────────────────────────────────────────────────
    #region Inspector
    // ─────────────────────────────────────────────────────────────────────────

    [Header("Behaviour")]
    [Tooltip("Minimum seconds the loading screen stays up, even if loading " +
             "finishes instantly. Prevents a one-frame flash on fast loads.")]
    public float minimumDisplayTime = 2f;

    [Tooltip("Seconds to hold at 100% before firing OnLoadComplete — gives the " +
             "terminal text a moment to read 'READY' before cutting away.")]
    public float completeHoldTime = 0.3f;

    [Header("Debug")]
    [SerializeField] private bool debugLog = true;
    [SerializeField] private float progressLogInterval = 0.25f;

    [Header("Debug — Editor Test Load")]
    [Tooltip("Editor-only. Press this key to fire a fake timed load, so you " +
             "can iterate on the BIOS/spinner visuals without walking through " +
             "cutscenes or menus to trigger a real scene transition.")]
    [SerializeField] private KeyCode debugTestKey = KeyCode.F9;
    [SerializeField] private float debugTestDuration = 4f;

    #endregion

    // ─────────────────────────────────────────────────────────────────────────
    #region Events
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>Fired once when loading begins.</summary>
    public event Action OnLoadStart;

    /// <summary>Fired every time blended progress changes. Args: (progress 0-1, current step label)</summary>
    public event Action<float, string> OnProgressChanged;

    /// <summary>Fired when a new step becomes active. Args: (step index, total steps, label)</summary>
    public event Action<int, int, string> OnStepChanged;

    /// <summary>Fired once all steps finish and the hold time elapses.</summary>
    public event Action OnLoadComplete;

    /// <summary>
    /// Fired once progress hits 100%% ONLY when this load was started with
    /// requireHoldToConfirm = true. UI (e.g. LoadingBIOSDisplay) should show
    /// its hold-to-confirm prompt on this and call ConfirmReady() once the
    /// hold completes.
    /// </summary>
    public event Action OnReadyForConfirm;

    #endregion

    // ─────────────────────────────────────────────────────────────────────────
    #region Public Read-only State
    // ─────────────────────────────────────────────────────────────────────────

    public bool  IsLoading      { get; private set; }
    public float CurrentProgress { get; private set; }

    /// <summary>True while a load is waiting on the player to hold the
    /// confirm key — only ever true when this load started with
    /// requireHoldToConfirm = true.</summary>
    public bool  AwaitingConfirm { get; private set; }

    #endregion

    // ─────────────────────────────────────────────────────────────────────────
    #region Private State
    // ─────────────────────────────────────────────────────────────────────────

    private Coroutine _loadCoroutine;
    private float     _lastLoggedProgress = -1f;
    private bool      _requireHoldToConfirm;
    private bool      _confirmReceived;

    #endregion

    // ─────────────────────────────────────────────────────────────────────────
    #region Public API
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Begin running the given steps in order. Safe to call only when not
    /// already loading — a second call while IsLoading is true is ignored
    /// (with a warning) rather than stacking coroutines.
    /// </summary>
    /// <param name="requireHoldToConfirm">
    /// If true, once all steps hit 100%% the coroutine pauses and fires
    /// OnReadyForConfirm instead of auto-continuing — call ConfirmReady()
    /// (typically from a hold-E UI) to let it proceed. Per-transition, not
    /// global — pass config.requireHoldToConfirm from SceneTransitionConfig.
    /// </param>
    public void BeginLoad(List<ILoadingStep> steps, bool requireHoldToConfirm = false)
    {
        if (IsLoading)
        {
            LogWarning("BeginLoad called while already loading — ignored.");
            return;
        }
        if (steps == null || steps.Count == 0)
        {
            LogWarning("BeginLoad called with no steps — nothing to do.");
            return;
        }

        _requireHoldToConfirm = requireHoldToConfirm;
        _confirmReceived      = false;
        _loadCoroutine = StartCoroutine(Co_RunSteps(steps));
    }

    /// <summary>
    /// Call once the player has finished holding the confirm input. No-op
    /// if no load is currently waiting on a confirm — safe to call blindly
    /// from UI without checking AwaitingConfirm first.
    /// </summary>
    public void ConfirmReady()
    {
        if (!AwaitingConfirm)
        {
            LogWarning("ConfirmReady called but nothing is awaiting confirm — ignored.");
            return;
        }
        _confirmReceived = true;
    }

    #endregion

    // ─────────────────────────────────────────────────────────────────────────
    #region Editor Debug Trigger
    // ─────────────────────────────────────────────────────────────────────────

#if UNITY_EDITOR
    private void Update()
    {
        if (Input.GetKeyDown(debugTestKey))
            TriggerDebugTestLoad();
    }

    /// <summary>
    /// Fires a fake, no-scene-load sequence purely to preview/tune the BIOS
    /// display and spinner. Skips SceneLoadStep entirely — nothing actually
    /// loads, so it's safe to press mid-gameplay too, not just from menus.
    /// </summary>
    [ContextMenu("Trigger Debug Test Load")]
    private void TriggerDebugTestLoad()
    {
        if (IsLoading)
        {
            LogWarning("Debug test load requested while already loading — ignored.");
            return;
        }

        Log($"Debug test load triggered ({debugTestKey}) — {debugTestDuration}s fake load, no scene change.");
        BeginLoad(new List<ILoadingStep>
        {
            new FakeDelayStep(debugTestDuration)
        });
    }
#endif

    #endregion

    // ─────────────────────────────────────────────────────────────────────────
    #region Core Coroutine
    // ─────────────────────────────────────────────────────────────────────────

    private IEnumerator Co_RunSteps(List<ILoadingStep> steps)
    {
        IsLoading       = true;
        CurrentProgress = 0f;
        _lastLoggedProgress = -1f;

        float startTime  = Time.unscaledTime;
        float totalWeight = 0f;
        foreach (var s in steps) totalWeight += Mathf.Max(0.0001f, s.Weight);

        Log($"Load started — {steps.Count} step(s), totalWeight={totalWeight:F2}");
        OnLoadStart?.Invoke();

        float weightCompletedSoFar = 0f;

        for (int i = 0; i < steps.Count; i++)
        {
            var step = steps[i];
            float stepWeight = Mathf.Max(0.0001f, step.Weight);

            Log($"Step {i + 1}/{steps.Count} → '{step.StatusLabel}' (weight={stepWeight:F2})");
            OnStepChanged?.Invoke(i, steps.Count, step.StatusLabel);

            // Capture the running weight total in a local so the closure below
            // doesn't accidentally read a mutated outer variable.
            float baseWeight = weightCompletedSoFar;

            yield return StartCoroutine(step.Run(stepProgress =>
            {
                float blended = (baseWeight + stepProgress * stepWeight) / totalWeight;
                ReportProgress(Mathf.Clamp01(blended), step.StatusLabel);
            }));

            weightCompletedSoFar += stepWeight;
        }

        ReportProgress(1f, "READY");

        if (_requireHoldToConfirm)
        {
            Log("Waiting for hold-to-confirm input...");
            AwaitingConfirm = true;
            OnReadyForConfirm?.Invoke();
            yield return new WaitUntil(() => _confirmReceived);
            AwaitingConfirm = false;
            Log("Confirm received — continuing.");
        }

        // Activate any pending scene load now that every step (incl. shader
        // warmup) has finished, so the new scene's first frame is already warm.
        foreach (var step in steps)
        {
            if (step is SceneLoadStep sceneStep)
                sceneStep.Activate();
        }

        // Respect minimum display time so fast loads don't flash.
        float elapsed = Time.unscaledTime - startTime;
        if (elapsed < minimumDisplayTime)
            yield return new WaitForSecondsRealtime(minimumDisplayTime - elapsed);

        if (completeHoldTime > 0f)
            yield return new WaitForSecondsRealtime(completeHoldTime);

        Log("Load complete.");
        IsLoading = false;
        _loadCoroutine = null;
        OnLoadComplete?.Invoke();
    }

    private void ReportProgress(float progress, string label)
    {
        CurrentProgress = progress;
        OnProgressChanged?.Invoke(progress, label);

        if (debugLog && (progress - _lastLoggedProgress >= progressLogInterval || progress >= 1f))
        {
            _lastLoggedProgress = progress;
            Log($"Progress: {progress * 100f:F0}% — {label}");
        }
    }

    #endregion

    // ─────────────────────────────────────────────────────────────────────────
    #region Debug
    // ─────────────────────────────────────────────────────────────────────────

    [System.Diagnostics.Conditional("UNITY_EDITOR")]
    private void Log(string msg)
    {
        if (debugLog) Debug.Log($"[LoadingScreenController] {msg}", this);
    }

    [System.Diagnostics.Conditional("UNITY_EDITOR")]
    private void LogWarning(string msg) => Debug.LogWarning($"[LoadingScreenController] ⚠ {msg}", this);

    #endregion
}

/// <summary>
/// Timed fake step with no real work — reports 0→1 progress over N seconds.
/// Use for previewing/tuning the loading UI (BIOS text, spinner, progress
/// bar, fades) without needing a real scene load or shader warmup to sit
/// through. See LoadingScreenController's debugTestKey (F9) for the
/// one-button trigger — this class is what it runs.
/// </summary>
public class FakeDelayStep : ILoadingStep
{
    public float Weight { get; }
    public string StatusLabel { get; }

    private readonly float _seconds;

    public FakeDelayStep(float seconds, float weight = 1f, string statusLabel = "SIMULATING LOAD")
    {
        _seconds    = Mathf.Max(0f, seconds);
        Weight      = weight;
        StatusLabel = statusLabel;
    }

    public IEnumerator Run(Action<float> onProgress)
    {
        float t = 0f;
        while (t < _seconds)
        {
            t += Time.unscaledDeltaTime;
            onProgress?.Invoke(Mathf.Clamp01(t / _seconds));
            yield return null;
        }
        onProgress?.Invoke(1f);
    }
}

/// <summary>
/// Put on whatever fires a real scene transition — a trigger volume, a
/// portal, a level-end zone, a menu button. Holds a SceneTransitionConfig
/// asset and kicks off LoadingScreenController.Instance when triggered.
///
/// This is the piece that was missing per-map: SceneTransitionConfig only
/// holds data, it doesn't fire anything on its own. Something has to call
/// config.BuildSteps() + controller.BeginLoad() — this is that something.
///
/// SETUP:
///   1. Attach to the trigger/button GameObject for a given map transition.
///   2. Assign a SceneTransitionConfig asset (Right-click > FPS > Loading >
///      Scene Transition Config), targetSceneName set to the destination.
///   3. If it's a trigger volume: make sure the Collider has "Is Trigger"
///      checked, and the player has the "Player" tag — OnTriggerEnter below
///      handles it automatically.
///   4. If it's a button/interact instead: call Trigger() from that (e.g.
///      wire a UI Button's OnClick to Trigger()).
/// </summary>
public class SceneTransitionTrigger : MonoBehaviour
{
    public SceneTransitionConfig config;

    public void Trigger()
    {
        if (config == null)
        {
            Debug.LogError("[SceneTransitionTrigger] No config assigned.", this);
            return;
        }

        if (LoadingScreenController.Instance == null)
        {
            Debug.LogError("[SceneTransitionTrigger] No LoadingScreenController.Instance found in scene.", this);
            return;
        }

        LoadingScreenController.Instance.BeginLoad(config.BuildSteps(), config.requireHoldToConfirm);
    }

    private void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag("Player"))
            Trigger();
    }
}