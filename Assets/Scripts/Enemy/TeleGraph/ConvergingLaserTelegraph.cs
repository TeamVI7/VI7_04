using UnityEngine;

// Two beams start offset left/right of the aim line and converge into a
// single center line as the wind-up completes -- a "charging a big shot"
// telegraph. Widens right at the end as an extra "about to fire" cue. The
// owning attack script fires the actual shot when its own windup ends --
// this preset is purely the visual, it never touches damage.
public class ConvergingLaserTelegraph : TelegraphVisual, ITelegraphAimable
{
    #region Inspector

    [SerializeField] private LineRenderer leftLine;
    [SerializeField] private LineRenderer rightLine;
    [SerializeField] private float startOffset = 1.5f;
    [SerializeField] private float chargeWidthMultiplier = 2.5f;
    [Range(0f, 1f)] [SerializeField] private float chargeKickStart = 0.9f;

    #endregion

    #region State

    private Material _leftMat;
    private Material _rightMat;
    private Transform _origin;
    private Transform _target;

    #endregion

    #region Unity

    private void Awake()
    {
        if (leftLine != null)
        {
            leftLine.positionCount = 2;
            leftLine.enabled = false;
            if (leftLine.material != null) _leftMat = leftLine.material;
        }

        if (rightLine != null)
        {
            rightLine.positionCount = 2;
            rightLine.enabled = false;
            if (rightLine.material != null) _rightMat = rightLine.material;
        }
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
        if (leftLine != null) leftLine.enabled = true;
        if (rightLine != null) rightLine.enabled = true;

        if (profile.warningSound != null)
            AudioSource.PlayClipAtPoint(profile.warningSound, transform.position, profile.warningVolume);
    }

    protected override void OnTick(float normalizedTime, float curveValue)
    {
        if (_origin == null || _target == null || leftLine == null || rightLine == null) return;

        Vector3 dir = _target.position - _origin.position;
        dir.y = 0f;
        dir = dir.sqrMagnitude > 0.0001f ? dir.normalized : transform.forward;

        Vector3 side = Vector3.Cross(Vector3.up, dir).normalized;
        float offset = Mathf.Lerp(startOffset, 0f, curveValue);

        Vector3 leftOrigin  = _origin.position - side * offset;
        Vector3 rightOrigin = _origin.position + side * offset;

        leftLine.SetPosition(0, leftOrigin);
        leftLine.SetPosition(1, _target.position);
        rightLine.SetPosition(0, rightOrigin);
        rightLine.SetPosition(1, _target.position);

        float pulse = 0.5f + 0.5f * Mathf.Sin(Time.time * profile.pulseSpeed);
        Color c = profile.color;
        c.a = Mathf.Lerp(0.2f, 1f, curveValue) * pulse;

        float width = profile.lineWidth;
        if (normalizedTime > chargeKickStart)
        {
            float kick = (normalizedTime - chargeKickStart) / (1f - chargeKickStart);
            width = Mathf.Lerp(profile.lineWidth, profile.lineWidth * chargeWidthMultiplier, kick);
        }

        leftLine.startWidth = leftLine.endWidth = width;
        rightLine.startWidth = rightLine.endWidth = width;

        if (_leftMat != null) _leftMat.color = c;
        if (_rightMat != null) _rightMat.color = c;
    }

    protected override void OnStop()
    {
        if (leftLine != null) leftLine.enabled = false;
        if (rightLine != null) rightLine.enabled = false;
    }

    #endregion
}
