using UnityEngine;
using DG.Tweening;
using System.Collections;
using UnityEngine.Rendering;

public class InFlightCutscene : MonoBehaviour
{
    [Header("Camera")]
    public Transform cutsceneCamera;    // place inside plane looking at window

    [Header("Clouds")]
    public Volume cloudVolume;
    public float flightWindSpeed = 150f;   // wind speed during scene
    public float defaultWindSpeed = 20f;   // wind speed before and after

    private VolumetricClouds _clouds;

    [Header("Turbulence")]
    // Seated inside an airframe you barely translate — the cabin pitches and rolls
    // around you. Angular turbulence reads as flight in a way position shake does not.
    public Vector3 turbulenceAngles = new Vector3(0.9f, 0.4f, 1.6f); // pitch, yaw, roll degrees
    public float turbulencePositionShake = 0.01f; // meters, kept token on purpose
    public float turbulenceFrequency = 9f;   // oscillations per second during a hit
    public float turbulenceDecay = 2.5f;     // how fast a hit dies away
    public float turbulenceInterval = 2f;    // seconds between hits

    [Header("Feel")]
    public CameraHandheld handheld;     // optional drift layer
    public float fovDrift = 1.5f;       // degrees the lens eases open across the hold

    [Header("Timing")]
    public float sceneDuration = 8f;    // seconds total in flight scene

    [Header("Audio")]
    public AudioSource engineHumAudio;  // interior engine hum
    public AudioSource turbulenceAudio; // optional turbulence hit sound

    private bool _playing = false;
    private Camera _cam;
    private float _baseFov;

    // Decaying impulse, kicked to 1 by each turbulence hit
    private float _jolt;
    private float _joltPhase;

    public IEnumerator Play()
    {
        _playing = true;
        cutsceneCamera.gameObject.SetActive(true);

        if (_cam == null) _cam = cutsceneCamera.GetComponentInChildren<Camera>(true);
        if (_cam != null && _baseFov <= 0f) _baseFov = _cam.fieldOfView;

        Vector3 homePos = cutsceneCamera.position;
        Quaternion homeRot = cutsceneCamera.rotation;

        // Ramp up wind when scene starts
        cloudVolume.profile.TryGet(out _clouds);
        if (_clouds != null)
            DOTween.To(
                () => _clouds.globalSpeed .value,
                x => _clouds.globalSpeed .value = x,
                flightWindSpeed, 2f)   // 2 seconds to ramp up
                .SetTarget(cloudVolume); // tagged so Stop() can kill it by target

        if (engineHumAudio != null)
        {
            engineHumAudio.loop = true;
            engineHumAudio.Play();
        }

        StartCoroutine(Turbulence());

        // ── HOLD ──────────────────────────────────────────────
        // A single write per frame composing jolt and drift, rather than letting
        // DOShakePosition own the transform. Keeps the two from fighting and lets
        // the shake be angular.
        float elapsed = 0f;
        while (elapsed < sceneDuration)
        {
            elapsed += Time.deltaTime;

            _jolt = Mathf.Max(0f, _jolt - turbulenceDecay * Time.deltaTime);
            _joltPhase += turbulenceFrequency * Time.deltaTime;

            // Three slightly detuned oscillators so the axes never line up into
            // an obvious repeating wobble.
            Vector3 shakeEuler = new Vector3(
                Mathf.Sin(_joltPhase * 1.00f) * turbulenceAngles.x,
                Mathf.Sin(_joltPhase * 0.71f) * turbulenceAngles.y,
                Mathf.Sin(_joltPhase * 1.31f) * turbulenceAngles.z) * _jolt;

            Vector3 pos = homePos;
            Quaternion rot = homeRot * Quaternion.Euler(shakeEuler);

            pos += rot * new Vector3(
                Mathf.Sin(_joltPhase * 1.7f), Mathf.Sin(_joltPhase * 2.3f), 0f)
                * (turbulencePositionShake * _jolt);

            if (handheld != null)
            {
                handheld.Sample(Time.time);
                pos += rot * handheld.PositionOffset;
                rot *= handheld.RotationOffset;
            }

            cutsceneCamera.SetPositionAndRotation(pos, rot);

            if (_cam != null && _baseFov > 0f)
                _cam.fieldOfView = _baseFov + fovDrift * Mathf.SmoothStep(0f, 1f, elapsed / sceneDuration);

            yield return null;
        }

        _playing = false;

        // Ramp wind back down
        if (_clouds != null)
            DOTween.To(
                () => _clouds.globalSpeed .value,
                x => _clouds.globalSpeed .value = x,
                defaultWindSpeed, 2f)
                .SetTarget(cloudVolume);

        if (engineHumAudio != null)
            engineHumAudio.DOFade(0f, 1f);

        RestoreFov();
        cutsceneCamera.gameObject.SetActive(false);
    }

    /// Halts playback and releases every tween this cutscene owns. Used by the
    /// skip path, which must not fall back on DOTween.KillAll — that would also
    /// kill cleanup tweens belonging to persistent objects in other scenes.
    public void Stop()
    {
        _playing = false;
        StopAllCoroutines();
        RestoreFov();

        if (cutsceneCamera != null) DOTween.Kill(cutsceneCamera);
        if (cloudVolume != null) DOTween.Kill(cloudVolume);
        if (engineHumAudio != null)
        {
            DOTween.Kill(engineHumAudio);
            engineHumAudio.Stop();
        }
        if (turbulenceAudio != null) turbulenceAudio.Stop();

        // The wind ramp is killed mid-tween above, so put the clouds back by hand
        // rather than leaving the next scene running at flight speed.
        if (_clouds != null) _clouds.globalSpeed.value = defaultWindSpeed;
    }

    private void RestoreFov()
    {
        if (_cam != null && _baseFov > 0f) _cam.fieldOfView = _baseFov;
    }

#if UNITY_EDITOR
    [Header("Gizmos")]
    public bool drawGizmos = true;

    private void OnDrawGizmos()
    {
        if (!drawGizmos || cutsceneCamera == null) return;

        Vector3 p = cutsceneCamera.position;

        Gizmos.color = new Color(1f, 0.9f, 0.2f);
        Gizmos.DrawWireSphere(p, 0.15f);
        Gizmos.DrawRay(p, cutsceneCamera.forward * 2f);

        // Turbulence envelope: the extremes of the roll and pitch swing, drawn as
        // the frame corners the shot will actually reach at full jolt.
        Gizmos.color = new Color(1f, 0.4f, 0.2f, 0.7f);
        foreach (int sign in new[] { 1, -1 })
        {
            Quaternion extreme = cutsceneCamera.rotation * Quaternion.Euler(
                turbulenceAngles.x * sign,
                turbulenceAngles.y * sign,
                turbulenceAngles.z * sign);
            Gizmos.DrawRay(p, extreme * Vector3.forward * 2f);
        }

        UnityEditor.Handles.Label(
            p,
            $"  In-flight\n  {sceneDuration:0.0}s\n  turb ±{turbulenceAngles.z:0.0}° roll");
    }
#endif

    IEnumerator Turbulence()
    {
        while (_playing)
        {
            // Randomised locally so the serialized interval stays the authored
            // value instead of being overwritten on the first hit.
            yield return new WaitForSeconds(Random.Range(turbulenceInterval * 0.75f,
                                                         turbulenceInterval * 1.75f));

            if (!_playing) break;

            if (turbulenceAudio != null) turbulenceAudio.Play();

            // Vary the strength so no two hits land identically
            _jolt = Mathf.Max(_jolt, Random.Range(0.6f, 1f));
        }
    }
}
