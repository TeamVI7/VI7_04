using UnityEngine;

[CreateAssetMenu(fileName = "TelegraphProfile", menuName = "Combat/Telegraph Profile")]
public class TelegraphProfile : ScriptableObject
{
    #region Timing

    // No duration field here on purpose — EnemyRangedAttackBehaviour.TelegraphTime
    // is the single source of truth for how long the wind-up lasts. Duplicating it
    // here would let profile and behaviour drift out of sync.
    public AnimationCurve intensityCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

    #endregion

    #region Visual

    public Color color = Color.red;
    public float pulseSpeed = 4f;
    public float lineWidth = 0.02f;
    [Tooltip("Multiplier for HDR emission color — only used by EmissionFlashTelegraph.")]
    public float emissionIntensity = 2f;

    #endregion

    #region Audio

    public AudioClip warningSound;
    [Range(0f, 1f)] public float warningVolume = 0.6f;

    #endregion
}
