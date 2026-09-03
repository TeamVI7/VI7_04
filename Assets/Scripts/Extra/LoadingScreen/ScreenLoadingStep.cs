using System;
using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Loads a scene asynchronously and reports progress via AsyncOperation.progress.
///
/// NOTE: Unity's AsyncOperation.progress caps at 0.9 until allowSceneActivation
/// is true (the last 10% is the actual scene activation/Awake/Start pass, which
/// is intentionally held back so you can keep the loading screen up an extra
/// frame if you want — e.g. to let the shader warmup step run before the new
/// scene's first frame renders). This step remaps 0-0.9 -> 0-1 internally so
/// the bar still reads a clean 0-100%, then activates the scene at the end.
/// </summary>
public class SceneLoadStep : ILoadingStep
{
    public float Weight { get; }
    public string StatusLabel { get; }

    private readonly string _sceneName;
    private readonly LoadSceneMode _mode;

    /// <param name="sceneName">Scene to load (must be in Build Settings).</param>
    /// <param name="weight">Relative weight — see ILoadingStep.Weight.</param>
    /// <param name="statusLabel">Label shown on the BIOS terminal.</param>
    /// <param name="mode">Single (default) or Additive.</param>
    public SceneLoadStep(string sceneName, float weight = 0.8f,
                          string statusLabel = "MOUNTING SECTOR DATA",
                          LoadSceneMode mode = LoadSceneMode.Single)
    {
        _sceneName  = sceneName;
        _mode       = mode;
        Weight      = weight;
        StatusLabel = statusLabel;
    }

    public IEnumerator Run(Action<float> onProgress)
    {
        AsyncOperation op = SceneManager.LoadSceneAsync(_sceneName, _mode);

        if (op == null)
        {
            FPSDebug.LogError($"[SceneLoadStep] LoadSceneAsync returned null for '{_sceneName}'. " +
                               "Is it added to Build Settings?");
            onProgress?.Invoke(1f);
            yield break;
        }

        // Hold the scene from activating until we've reported full progress —
        // lets later steps (e.g. shader warmup) run before the new scene is live.
        op.allowSceneActivation = false;

        while (op.progress < 0.9f)
        {
            onProgress?.Invoke(Mathf.Clamp01(op.progress / 0.9f));
            yield return null;
        }

        onProgress?.Invoke(1f);

        // Caller (LoadingScreenController) decides when to flip this true,
        // typically after the very last step finishes.
        _pendingActivation = op;
    }

    private AsyncOperation _pendingActivation;

    /// <summary>Call once ALL loading steps are done to actually swap scenes.</summary>
    public void Activate()
    {
        if (_pendingActivation != null)
            _pendingActivation.allowSceneActivation = true;
    }

    /// <summary>
    /// False between Activate() and the moment the swap actually finishes.
    /// Flipping allowSceneActivation only *unblocks* activation — Unity still
    /// needs several frames to tear down the old scene and run Awake/OnEnable/
    /// Start across the new one. Poll this so the loading screen stays up
    /// through that stall instead of fading out over it.
    /// Also true when there was nothing to activate (null op / early bail).
    /// </summary>
    public bool IsActivationComplete => _pendingActivation == null || _pendingActivation.isDone;
}