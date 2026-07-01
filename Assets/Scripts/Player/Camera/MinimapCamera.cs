using UnityEngine;

public class MinimapCamera : MonoBehaviour
{
    private bool _fogWas;

    private void OnPreRender()
    {
        _fogWas = RenderSettings.fog;
        RenderSettings.fog = false;
    }

    private void OnPostRender()
    {
        RenderSettings.fog = _fogWas;
    }
}