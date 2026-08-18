using UnityEngine;

// Pulses a Renderer's emission color in place. Cheapest, most universal
// preset — no geometry of its own. Good for an eye glow, a weakpoint glow,
// a muzzle warning, a spinning-up gatling barrel — anything already lit.
public class EmissionFlashTelegraph : TelegraphVisual
{
    #region Inspector

    [SerializeField] private Renderer targetRenderer;
    [SerializeField] private string emissionProperty = "_EmissionColor";

    #endregion

    #region State

    private Material _mat;
    private Color _baseEmission;

    #endregion

    #region Unity

    private void Awake()
    {
        if (targetRenderer == null) return;

        _mat = targetRenderer.material;
        if (_mat.HasProperty(emissionProperty))
            _baseEmission = _mat.GetColor(emissionProperty);
    }

    #endregion

    #region TelegraphVisual Overrides

    protected override void OnPlay()
    {
        if (_mat != null)
            _mat.EnableKeyword("_EMISSION");

        if (profile.warningSound != null)
            AudioSource.PlayClipAtPoint(profile.warningSound, transform.position, profile.warningVolume);
    }

    protected override void OnTick(float normalizedTime, float curveValue)
    {
        if (_mat == null || !_mat.HasProperty(emissionProperty)) return;

        float pulse = 0.5f + 0.5f * Mathf.Sin(Time.time * profile.pulseSpeed);
        Color addColor = profile.color * profile.emissionIntensity * curveValue * pulse;
        _mat.SetColor(emissionProperty, _baseEmission + addColor);
    }

    protected override void OnStop()
    {
        if (_mat != null && _mat.HasProperty(emissionProperty))
            _mat.SetColor(emissionProperty, _baseEmission);
    }

    #endregion
}
