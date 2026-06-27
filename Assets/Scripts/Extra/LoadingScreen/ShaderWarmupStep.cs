using System;
using System.Collections;
using UnityEngine;

/// <summary>
/// Warms up a ShaderVariantCollection so the first real frame in the new scene
/// doesn't stall on just-in-time shader compilation.
///
/// IMPORTANT — read before wiring this up:
///   ShaderVariantCollection.WarmUp() has no progress callback and blocks the
///   main thread for its full duration. There is no supported way to warm up
///   a *partial* collection per-frame in older Unity versions — WarmUp() always
///   compiles every variant in the collection in one call.
///
///   This step works around that by giving you two modes:
///     1) Single-shot (default): call WarmUp() once, report 0% -> 100% as a
///        single jump. Simple, but the bar will appear to "stick" while it runs.
///     2) Chunked (recommended): split your variants ACROSS MULTIPLE SVC
///        ASSETS at author time (e.g. SVC_Weapons, SVC_Environment,
///        SVC_Characters) and pass all of them in — this step warms each
///        collection in turn and yields a frame between them, so the bar
///        advances in visible steps instead of one big stall.
///
///   For finer granularity than "one asset = one chunk", split further at
///   authoring time (e.g. per weapon, per level). The runtime code here
///   doesn't care how many collections you give it.
///
/// SETUP:
///   1. Play through representative gameplay with Edit > Project Settings >
///      Graphics > "Save to asset..." tracking enabled (or use
///      ShaderVariantCollection.Add at runtime in a dev build) to capture
///      real variants — don't hand-author this list, you'll miss combinations
///      or include unused ones.
///   2. Drag the resulting .shadervariants assets into the warmupCollections
///      array on whatever ScriptableObject/config holds your loading step list.
/// </summary>
public class ShaderWarmupStep : ILoadingStep
{
    public float Weight { get; }
    public string StatusLabel { get; }

    private readonly ShaderVariantCollection[] _collections;

    /// <param name="collections">
    /// One or more variant collections to warm, in order. Multiple collections
    /// = multiple progress steps (see class remarks above).
    /// </param>
    /// <param name="weight">Relative weight — see ILoadingStep.Weight.</param>
    /// <param name="statusLabel">Label shown on the BIOS terminal.</param>
    public ShaderWarmupStep(ShaderVariantCollection[] collections, float weight = 0.2f,
                             string statusLabel = "WARMING RENDER CACHE")
    {
        _collections = collections ?? Array.Empty<ShaderVariantCollection>();
        Weight       = weight;
        StatusLabel  = statusLabel;
    }

    public IEnumerator Run(Action<float> onProgress)
    {
        if (_collections.Length == 0)
        {
            FPSDebug.LogWarning("[ShaderWarmupStep] No collections assigned — skipping.");
            onProgress?.Invoke(1f);
            yield break;
        }

        for (int i = 0; i < _collections.Length; i++)
        {
            var svc = _collections[i];
            if (svc == null)
            {
                FPSDebug.LogWarning($"[ShaderWarmupStep] Collection at index {i} is null — skipping.");
                continue;
            }

            // WarmUp() is blocking and void — there's no async/incremental
            // variant of it and no return value to check. We eat that cost
            // per-collection rather than per-variant, then yield a frame so
            // the loading UI can repaint before the next chunk.
            // isWarmedUp lets us at least confirm it actually compiled something.
            svc.WarmUp();
            if (!svc.isWarmedUp)
                FPSDebug.LogWarning($"[ShaderWarmupStep] '{svc.name}' did not report as warmed up after WarmUp().");

            float fraction = (i + 1) / (float)_collections.Length;
            onProgress?.Invoke(fraction);

            yield return null; // let the bar/terminal repaint between chunks
        }

        onProgress?.Invoke(1f);
    }
}