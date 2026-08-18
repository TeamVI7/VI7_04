using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Disables scene fog while the minimap camera renders, so the top-down map isn't
/// washed out by the gameplay fog settings.
///
/// FIX: this used OnPreRender/OnPostRender, which are Built-in Render Pipeline only —
/// URP never calls them, so the fog toggle silently did nothing. URP raises
/// RenderPipelineManager.beginCameraRendering/endCameraRendering instead.
/// </summary>
[RequireComponent(typeof(Camera))]
public class MinimapCamera : MonoBehaviour
{
    private Camera _cam;
    private bool   _fogWas;

    private void Awake() => _cam = GetComponent<Camera>();

    private void OnEnable()
    {
        RenderPipelineManager.beginCameraRendering += HandleBeginCameraRendering;
        RenderPipelineManager.endCameraRendering   += HandleEndCameraRendering;
    }

    private void OnDisable()
    {
        RenderPipelineManager.beginCameraRendering -= HandleBeginCameraRendering;
        RenderPipelineManager.endCameraRendering   -= HandleEndCameraRendering;

        // If we're disabled mid-frame between the two callbacks, fog would stay off
        // for the whole game. Restoring here can only ever turn it back on.
        if (!RenderSettings.fog && _fogWas) RenderSettings.fog = true;
    }

    private void HandleBeginCameraRendering(ScriptableRenderContext ctx, Camera cam)
    {
        if (cam != _cam) return;
        _fogWas = RenderSettings.fog;
        RenderSettings.fog = false;
    }

    private void HandleEndCameraRendering(ScriptableRenderContext ctx, Camera cam)
    {
        if (cam != _cam) return;
        RenderSettings.fog = _fogWas;
    }
}
