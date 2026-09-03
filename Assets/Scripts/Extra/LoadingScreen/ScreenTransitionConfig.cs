using UnityEngine;

/// <summary>
/// Configures a single scene transition's loading steps. Create one per
/// transition that needs custom shader collections (e.g. SceneTransition_ToLevel01,
/// SceneTransition_ToMenu) and assign it wherever that transition is triggered.
///
/// Create via: Right-click > FPS > Loading > Scene Transition Config
/// </summary>
[CreateAssetMenu(fileName = "NewSceneTransition", menuName = "FPS/Loading/Scene Transition Config")]
public class SceneTransitionConfig : ScriptableObject
{
    [Header("Target Scene")]
    [Tooltip("Exact scene name as it appears in Build Settings.")]
    public string targetSceneName;

    [Header("Step Weights")]
    [Tooltip("Relative weight of the scene-load portion of the bar.")]
    public float sceneLoadWeight = 0.8f;

    [Tooltip("Relative weight of the shader-warmup portion of the bar. " +
             "Set to 0 (and leave shaderCollections empty) to skip warmup entirely.")]
    public float shaderWarmupWeight = 0.2f;

    [Header("Shader Warmup")]
    [Tooltip("One or more .shadervariants assets to warm before the scene activates. " +
             "Multiple entries = smoother progress (see ShaderWarmupStep remarks).")]
    public ShaderVariantCollection[] shaderCollections;

    [Header("Status Labels")]
    public string sceneLoadLabel    = "MOUNTING SECTOR DATA";
    public string shaderWarmupLabel = "WARMING RENDER CACHE";

    /// <summary>Builds the ordered ILoadingStep list for this transition.</summary>
    /// <param name="sceneLoadLabelOverride">
    /// Replaces <see cref="sceneLoadLabel"/> for this one load. Lets a caller
    /// reuse a scene's config — crucially its shaderCollections — while still
    /// showing its own status text (e.g. a save restore reading
    /// "RESTORING OPERATIVE STATE" instead of "MOUNTING SECTOR DATA").
    /// Pass null to keep the configured label.
    /// </param>
    public System.Collections.Generic.List<ILoadingStep> BuildSteps(string sceneLoadLabelOverride = null)
    {
        string label = string.IsNullOrEmpty(sceneLoadLabelOverride) ? sceneLoadLabel : sceneLoadLabelOverride;

        var steps = new System.Collections.Generic.List<ILoadingStep>
        {
            new SceneLoadStep(targetSceneName, sceneLoadWeight, label)
        };

        if (shaderCollections != null && shaderCollections.Length > 0 && shaderWarmupWeight > 0f)
            steps.Add(new ShaderWarmupStep(shaderCollections, shaderWarmupWeight, shaderWarmupLabel));

        return steps;
    }
}