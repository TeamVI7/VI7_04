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

    [Header("Hold To Confirm")]
    [Tooltip("If true, once loading hits 100% the player must hold the " +
             "confirm key/button before the scene actually activates — " +
             "instead of auto-continuing. Leave off for normal transitions; " +
             "turn on only for the specific scenes that need it.")]
    public bool requireHoldToConfirm = false;

    /// <summary>Builds the ordered ILoadingStep list for this transition.</summary>
    public System.Collections.Generic.List<ILoadingStep> BuildSteps()
    {
        var steps = new System.Collections.Generic.List<ILoadingStep>
        {
            new SceneLoadStep(targetSceneName, sceneLoadWeight, sceneLoadLabel)
        };

        if (shaderCollections != null && shaderCollections.Length > 0 && shaderWarmupWeight > 0f)
            steps.Add(new ShaderWarmupStep(shaderCollections, shaderWarmupWeight, shaderWarmupLabel));

        return steps;
    }
}