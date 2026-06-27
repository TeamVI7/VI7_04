using System;
using System.Collections;

/// <summary>
/// A single unit of loading work (scene load, shader warmup, addressables, etc.).
/// LoadingScreenController runs a List&lt;ILoadingStep&gt; in sequence and uses
/// each step's Weight to map its 0-1 internal progress onto the overall bar.
///
/// EXTEND:
///   Add a new class implementing this interface for any future loading phase
///   (e.g. SaveDataLoadStep, AddressablesPreloadStep) and drop it into the
///   LoadingScreenController.steps list — no changes needed elsewhere.
/// </summary>
public interface ILoadingStep
{
    /// <summary>
    /// Relative weight of this step in the overall progress bar.
    /// Weights across all steps are normalised automatically — they do NOT
    /// need to sum to 1. e.g. SceneLoad=0.8, ShaderWarmup=0.2 behaves the
    /// same as SceneLoad=4, ShaderWarmup=1.
    /// </summary>
    float Weight { get; }

    /// <summary>
    /// Human-readable label shown on the BIOS terminal while this step runs.
    /// e.g. "MOUNTING SECTOR DATA", "WARMING RENDER CACHE"
    /// </summary>
    string StatusLabel { get; }

    /// <summary>
    /// Run the step. Call onProgress(0f..1f) as work completes so the
    /// controller can blend it into the overall bar. Always call onProgress(1f)
    /// just before returning, even if the underlying API doesn't report progress.
    /// </summary>
    IEnumerator Run(Action<float> onProgress);
}