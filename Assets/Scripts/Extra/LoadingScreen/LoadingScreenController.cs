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
    public float minimumDisplayTime = 0.5f;

    [Tooltip("Seconds to hold at 100% before firing OnLoadComplete — gives the " +
             "terminal text a moment to read 'READY' before cutting away.")]
    public float completeHoldTime = 0.3f;

    [Header("Debug")]
    [SerializeField] private bool debugLog = true;
    [SerializeField] private float progressLogInterval = 0.25f;

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

    #endregion

    // ─────────────────────────────────────────────────────────────────────────
    #region Public Read-only State
    // ─────────────────────────────────────────────────────────────────────────

    public bool  IsLoading      { get; private set; }
    public float CurrentProgress { get; private set; }

    #endregion

    // ─────────────────────────────────────────────────────────────────────────
    #region Private State
    // ─────────────────────────────────────────────────────────────────────────

    private Coroutine _loadCoroutine;
    private float     _lastLoggedProgress = -1f;

    #endregion

    // ─────────────────────────────────────────────────────────────────────────
    #region Public API
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Begin running the given steps in order. Safe to call only when not
    /// already loading — a second call while IsLoading is true is ignored
    /// (with a warning) rather than stacking coroutines.
    /// </summary>
    public void BeginLoad(List<ILoadingStep> steps)
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

        _loadCoroutine = StartCoroutine(Co_RunSteps(steps));
    }

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