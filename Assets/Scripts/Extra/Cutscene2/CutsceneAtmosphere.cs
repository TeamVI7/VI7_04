// CutsceneAtmosphere.cs — drives the volumetric clouds and fog as part of the shot.
//
// The sky is the only thing in shots 2-4 with any sense of scale to it: a bomber
// against a static sky reads as a model on a poster. Pushing the cloud wind up
// while the aircraft is climbing costs nothing and does most of the work of
// selling the speed.
//
// Two packages are involved, both auto-referenced and both in the global
// namespace, so they can be driven directly:
//   VolumetricClouds             (Assets/Package/UnityVolumetricCloudsURP-main)
//   VolumetricFogVolumeComponent (com.cqf.urpvolumetricfog)
//
// Everything here goes through volume.profile, NOT volume.sharedProfile.
// sharedProfile is the asset on disk — writing to it in play mode edits the
// project and the change survives exiting play mode. profile returns a runtime
// clone, which is what a cutscene should be scribbling on.
using UnityEngine;
using UnityEngine.Rendering;
using DG.Tweening;

public class CutsceneAtmosphere : MonoBehaviour
{
    [Header("Volume")]
    [Tooltip("The global Volume in the cutscene scene. Its profile is cloned at " +
             "runtime, so nothing here touches the asset on disk.")]
    public Volume volume;

    [Header("Wind Per Shot")]
    // Indexed 1-5 by shot. Index 0 is the pre-roll before shot 1 starts.
    //
    // Scaled off InFlightCutscene in cutscene 1, which uses 20 at rest and 150
    // in flight and is known to look right in this project. The package labels
    // globalSpeed as km/h, but what matters is what reads on screen at this
    // project's cloud altitude and scale — so these follow the working shot
    // rather than a unit conversion.
    //
    // Shot 3 gets the highest of them on purpose — the camera is parented to the
    // bomber there, and shot 2 stops driving the aircraft when it ends, so the
    // airframe is genuinely motionless in frame. Moving sky is the only speed
    // cue that shot has.
    [Tooltip("globalSpeed for each shot: [0] pre-roll, [1] extraction ... [5] detonation. " +
             "Cutscene 1 uses 20 at rest and 150 in flight.")]
    public float[] windSpeedPerShot = { 20f, 25f, 140f, 200f, 160f, 60f };

    [Tooltip("Seconds to ramp between one shot's wind and the next.")]
    public float windRampDuration = 1.2f;

    [Header("Detail Layers While Moving")]
    // globalSpeed alone scrolls the big shapes. The erosion layer is what the eye
    // actually reads as detail rushing past, and it ships throttled to a quarter
    // speed — fine for idle weather, far too subdued when the sky is meant to be
    // tearing by.
    [Range(0f, 1f)] public float calmErosionSpeed = 0.25f;
    [Range(0f, 1f)] public float movingErosionSpeed = 0.85f;

    [Tooltip("Wind-driven vertical shear. Higher values lean the cloud layers " +
             "into the wind instead of sliding them along flat.")]
    [Range(-1f, 1f)] public float calmAltitudeDistortion = 0.25f;
    [Range(-1f, 1f)] public float movingAltitudeDistortion = 0.8f;

    [Header("Motion Clarity")]
    // Temporal accumulation is what makes the clouds cheap: each frame reuses
    // most of the last one. At 0.95 that is invisible on a slow sky and smears
    // badly the moment the wind is cranked, because there is nothing left of the
    // new frame to see. Lowering it while the sky is moving costs performance
    // and buys back the detail.
    [Range(0f, 1f)] public float calmAccumulation = 0.95f;
    [Range(0f, 1f)] public float movingAccumulation = 0.7f;
    [Tooltip("Wind speed at which the moving values are fully reached.")]
    public float fullMotionWind = 160f;

    [Header("Detonation")]
    [Tooltip("Wind speed spike as the shockwave passes through the cloud deck.")]
    public float blastWind = 400f;
    [Tooltip("Clouds are torn open — density falls away.")]
    [Range(0f, 1f)] public float blastDensity = 0.06f;
    [Tooltip("Bigger shapes read as the deck being pulled apart rather than scrolling.")]
    public float blastShapeScale = 14f;
    [Range(0f, 1f)] public float blastErosion = 1f;
    [Tooltip("Sun dimmer pushed above 1 lights the cloud deck from the fireball. " +
             "This is the part people actually read as 'the bomb lit up the sky'.")]
    public float blastSunDimmer = 2f;

    public float blastPunchDuration = 0.5f;   // seconds to reach the spike
    public float blastRecoverDuration = 7f;   // seconds easing back down

    [Header("Fog (optional)")]
    [Tooltip("Fog density punched up with the flash, then released. Leave at -1 to " +
             "leave the fog alone entirely.")]
    public float blastFogDensity = -1f;
    public float blastFogScattering = 0.6f;

    private VolumetricClouds _clouds;
    private VolumetricFogVolumeComponent _fog;

    // Captured at Awake so the recovery has something truthful to return to
    // rather than a guess baked into this component.
    private float _restDensity, _restShapeScale, _restErosion, _restSunDimmer;
    private float _restFogDensity, _restFogScattering;
    private bool _resolved;

    // Deliberately NOT resolved in Awake and latched. InFlightCutscene in
    // cutscene 1 calls TryGet inside Play() for a reason: at Awake the Volume may
    // not have its profile instantiated yet, and a lookup that fails once must
    // not be cached as "there are no clouds" for the rest of the sequence. Every
    // entry point calls this, and it only latches once it actually found them.
    private void Resolve()
    {
        if (_resolved || volume == null) return;

        // profile, not sharedProfile — see the file header.
        VolumeProfile profile = volume.profile;
        if (profile == null) return;

        profile.TryGet(out _clouds);
        profile.TryGet(out _fog);

        if (_clouds == null)
        {
            Debug.LogWarning("CutsceneAtmosphere: the assigned Volume's profile has no " +
                             "'Sky/Volumetric Clouds (URP)' override, so there is nothing " +
                             "to drive. Add the override to the profile and tick its State.",
                             this);
            return;   // no latch — try again on the next shot
        }

        {
            _restDensity    = _clouds.densityMultiplier.value;
            _restShapeScale = _clouds.shapeScale.value;
            _restErosion    = _clouds.erosionFactor.value;
            _restSunDimmer  = _clouds.sunLightDimmer.value;
        }

        if (_fog != null)
        {
            _restFogDensity    = _fog.density.value;
            _restFogScattering = _fog.scattering.value;
        }

        _resolved = true;
    }

    /// A volume parameter with its override switched off is ignored no matter
    /// what value is written to it, so every parameter this component drives has
    /// to be claimed first.
    private static void Claim(VolumeParameter parameter) => parameter.overrideState = true;

    // ───────────────────────────────────────────────────────────────
    // WIND
    // ───────────────────────────────────────────────────────────────

    /// Called by the manager as each shot begins. Shot index is 1-5; 0 is the
    /// pre-roll.
    public void EnterShot(int shotIndex)
    {
        Resolve();
        if (_clouds == null) return;
        if (windSpeedPerShot == null || shotIndex < 0 || shotIndex >= windSpeedPerShot.Length) return;

        SetWind(windSpeedPerShot[shotIndex], windRampDuration);
    }

    public void SetWind(float speed, float duration)
    {
        Resolve();
        if (_clouds == null) return;

        Claim(_clouds.globalSpeed);
        Claim(_clouds.temporalAccumulationFactor);
        Claim(_clouds.erosionSpeedMultiplier);
        Claim(_clouds.altitudeDistortion);

        DOTween.Kill(this);

        float from = _clouds.globalSpeed.value;
        DOVirtual.Float(from, speed, Mathf.Max(0.01f, duration), value =>
        {
            _clouds.globalSpeed.value = value;

            // Everything else follows the wind rather than being switched per
            // shot: the ramp itself is the fast part, and a hard switch at the
            // shot boundary would pop.
            float motion = Mathf.Clamp01(Mathf.Abs(value) / Mathf.Max(1f, fullMotionWind));

            _clouds.temporalAccumulationFactor.value =
                Mathf.Lerp(calmAccumulation, movingAccumulation, motion);
            _clouds.erosionSpeedMultiplier.value =
                Mathf.Lerp(calmErosionSpeed, movingErosionSpeed, motion);
            _clouds.altitudeDistortion.value =
                Mathf.Lerp(calmAltitudeDistortion, movingAltitudeDistortion, motion);
        }).SetId(this).SetEase(Ease.InOutSine);
    }

    // ───────────────────────────────────────────────────────────────
    // DETONATION
    // ───────────────────────────────────────────────────────────────

    /// The blast reaching the cloud deck: torn open, lit from below, shoved.
    /// Called from the detonation shot at the flash.
    public void Detonate()
    {
        Resolve();

        DOTween.Kill(this);

        if (_clouds != null)
        {
            Claim(_clouds.globalSpeed);
            Claim(_clouds.densityMultiplier);
            Claim(_clouds.shapeScale);
            Claim(_clouds.erosionFactor);
            Claim(_clouds.sunLightDimmer);
            Claim(_clouds.temporalAccumulationFactor);

            // Accumulation is dropped for the whole punch. Everything below is
            // changing far too fast to reuse last frame's result, and leaving it
            // high turns the spread into a smear.
            _clouds.temporalAccumulationFactor.value = movingAccumulation;

            Punch(_clouds.globalSpeed.value,       blastWind,       v => _clouds.globalSpeed.value = v,       _clouds.globalSpeed.value);
            Punch(_clouds.densityMultiplier.value, blastDensity,    v => _clouds.densityMultiplier.value = v, _restDensity);
            Punch(_clouds.shapeScale.value,        blastShapeScale, v => _clouds.shapeScale.value = v,        _restShapeScale);
            Punch(_clouds.erosionFactor.value,     blastErosion,    v => _clouds.erosionFactor.value = v,     _restErosion);
            Punch(_clouds.sunLightDimmer.value,    blastSunDimmer,  v => _clouds.sunLightDimmer.value = v,    _restSunDimmer);

            DOVirtual.Float(movingAccumulation, calmAccumulation, blastRecoverDuration,
                            v => _clouds.temporalAccumulationFactor.value = v)
                     .SetDelay(blastPunchDuration).SetId(this);
        }

        if (_fog != null && blastFogDensity >= 0f)
        {
            Claim(_fog.density);
            Claim(_fog.scattering);

            Punch(_fog.density.value,    blastFogDensity,    v => _fog.density.value = v,    _restFogDensity);
            Punch(_fog.scattering.value, blastFogScattering, v => _fog.scattering.value = v, _restFogScattering);
        }
    }

    /// Spike to a value, then ease back to a resting value. Two tweens rather
    /// than a DOPunch so the recovery target can differ from the start value —
    /// the sky after a nuke should not settle back exactly where it began.
    private void Punch(float from, float to, TweenCallback<float> setter, float restValue)
    {
        DOVirtual.Float(from, to, Mathf.Max(0.01f, blastPunchDuration), setter)
                 .SetEase(Ease.OutQuad).SetId(this);

        DOVirtual.Float(to, restValue, Mathf.Max(0.01f, blastRecoverDuration), setter)
                 .SetDelay(blastPunchDuration).SetEase(Ease.InOutSine).SetId(this);
    }

    void OnDestroy() => DOTween.Kill(this);
}
