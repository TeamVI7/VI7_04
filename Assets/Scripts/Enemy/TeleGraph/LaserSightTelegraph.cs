using UnityEngine;

[RequireComponent(typeof(LineRenderer))]
public class LaserSightTelegraph : TelegraphVisual, ITelegraphAimable
{
    #region State

    private LineRenderer _line;
    private Material _lineMat;
    private Transform _origin;
    private Transform _target;

    #endregion

    #region Unity

    private void Awake()
    {
        _line = GetComponent<LineRenderer>();
        _line.positionCount = 2;
        _line.enabled = false;

        if (_line.material != null)
            _lineMat = _line.material; // instance, safe to tint per-enemy
    }

    #endregion

    #region ITelegraphAimable

    public void SetEndpoints(Transform origin, Transform target)
    {
        _origin = origin;
        _target = target;
    }

    #endregion

    #region TelegraphVisual Overrides

    protected override void OnPlay()
    {
        _line.enabled = true;
        _line.startWidth = profile.lineWidth;
        _line.endWidth = profile.lineWidth;

        if (profile.warningSound != null)
            AudioSource.PlayClipAtPoint(profile.warningSound, transform.position, profile.warningVolume);
    }

    protected override void OnTick(float normalizedTime, float curveValue)
    {
        if (_origin == null || _target == null) return;

        _line.SetPosition(0, _origin.position);
        _line.SetPosition(1, _target.position);

        float pulse = 0.5f + 0.5f * Mathf.Sin(Time.time * profile.pulseSpeed);
        Color c = profile.color;
        c.a = Mathf.Lerp(0.15f, 1f, curveValue) * pulse;

        if (_lineMat != null)
            _lineMat.color = c;
    }

    protected override void OnStop()
    {
        _line.enabled = false;
    }

    #endregion
}
