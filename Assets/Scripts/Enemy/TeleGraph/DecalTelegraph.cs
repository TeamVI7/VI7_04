using UnityEngine;

// Generic ground-projected warning shape. Assign any mesh/sprite as a child
// (circle for AoE, fan/cone for a melee sweep, arrow for a dash telegraph,
// stomp ring for the mech) — this script only owns positioning + the
// grow/pulse animation, not the shape itself.
public class DecalTelegraph : TelegraphVisual, ITelegraphPlaceable
{
    #region Inspector

    [SerializeField] private Transform decal;
    [SerializeField] private Renderer decalRenderer;
    [SerializeField] private float growFrom = 0.6f;
    [SerializeField] private float growTo = 1f;

    #endregion

    #region State

    private Material _decalMat;

    #endregion

    #region Unity

    private void Awake()
    {
        if (decalRenderer != null && decalRenderer.material != null)
            _decalMat = decalRenderer.material;

        if (decal != null)
            decal.gameObject.SetActive(false);
    }

    #endregion

    #region ITelegraphPlaceable

    public void SetTransform(Vector3 worldPosition, Quaternion worldRotation)
    {
        if (decal == null) return;
        decal.SetPositionAndRotation(worldPosition, worldRotation);
    }

    #endregion

    #region TelegraphVisual Overrides

    protected override void OnPlay()
    {
        if (decal != null)
            decal.gameObject.SetActive(true);

        if (profile.warningSound != null)
            AudioSource.PlayClipAtPoint(profile.warningSound, transform.position, profile.warningVolume);
    }

    protected override void OnTick(float normalizedTime, float curveValue)
    {
        if (decal == null) return;

        float scale = Mathf.Lerp(growFrom, growTo, curveValue);
        decal.localScale = Vector3.one * scale;

        float pulse = 0.5f + 0.5f * Mathf.Sin(Time.time * profile.pulseSpeed);
        if (_decalMat != null)
        {
            Color c = profile.color;
            c.a = Mathf.Lerp(0.2f, 1f, curveValue) * pulse;
            _decalMat.color = c;
        }
    }

    protected override void OnStop()
    {
        if (decal != null)
            decal.gameObject.SetActive(false);
    }

    #endregion
}
