using System.Collections;
using UnityEngine;

/// <summary>
/// Drives <see cref="DatamoshRenderFeature"/> off the player's death.
///
/// The moment the player dies the image stops refreshing and starts smearing along the
/// tumbling death camera, so the picture falls apart in the same beat the body does. It
/// holds through the fade to black and is cleared on respawn — the buffer has to be
/// dropped there or the mosh would drag the corpse's view over the checkpoint the player
/// wakes up at.
///
/// Put this on the same object as <see cref="DeathCamera"/> (or anything that lives for
/// the whole level). It only touches statics, so it needs no references wiring up.
/// </summary>
public class DatamoshOnDeath : MonoBehaviour
{
    [Header("Ramp")]
    [Tooltip("Beat of clean image after death before the corruption starts. A little delay " +
             "sells the hit — the player sees themselves die, then the feed gives out.")]
    [SerializeField] private float onset = 0.08f;

    [Tooltip("How long the corruption takes to spread from nothing to full.")]
    [SerializeField] private float attack = 0.45f;

    [Tooltip("Fraction of the screen that ends up frozen. Below 1 some blocks keep " +
             "refreshing, which reads as a feed still fighting to come back.")]
    [Range(0f, 1f)]
    [SerializeField] private float peak = 0.96f;

    [Tooltip("Multiplier on the renderer feature's smear while dead. Above 1 the blocks " +
             "overshoot the camera's actual motion and the image tears itself apart.")]
    [SerializeField] private float smearScale = 1.35f;

    [Tooltip("Shape of the ramp. The default eases in, so the first frames barely glitch " +
             "before it runs away.")]
    [SerializeField] private AnimationCurve ramp = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

    private Coroutine _routine;

    private void OnEnable()
    {
        PlayerHealth.OnDied            += HandleDeath;
        CheckpointManager.OnRespawned  += HandleRespawn;
    }

    private void OnDisable()
    {
        PlayerHealth.OnDied            -= HandleDeath;
        CheckpointManager.OnRespawned  -= HandleRespawn;

        // Whatever tore this down — scene change, disable, level restart — must not leave
        // the next camera rendering through a dead player's mosh buffer.
        Clear();
    }

    private void HandleDeath()
    {
        if (_routine != null) return;
        _routine = StartCoroutine(Co_Mosh());
    }

    private void HandleRespawn() => Clear();

    private IEnumerator Co_Mosh()
    {
        DatamoshRenderFeature.RequestKeyframe();
        DatamoshRenderFeature.SmearScale = smearScale;

        // Unscaled throughout: the death sequence plays out even if something left
        // timeScale at zero, and CheckpointManager waits in realtime for the same reason.
        if (onset > 0f) yield return new WaitForSecondsRealtime(onset);

        float t = 0f;
        while (t < attack)
        {
            t += Time.unscaledDeltaTime;
            DatamoshRenderFeature.Intensity = peak * ramp.Evaluate(Mathf.Clamp01(t / attack));
            yield return null;
        }

        DatamoshRenderFeature.Intensity = peak;
        _routine = null;
    }

    private void Clear()
    {
        if (_routine != null)
        {
            StopCoroutine(_routine);
            _routine = null;
        }

        DatamoshRenderFeature.Intensity  = 0f;
        DatamoshRenderFeature.SmearScale = 1f;
        DatamoshRenderFeature.RequestKeyframe();
    }
}
