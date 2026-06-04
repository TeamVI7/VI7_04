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
    public float turbulenceStrength = 0.05f;  // meters of shake
    public float turbulenceDuration = 0.3f;   // seconds per shake hit
    public float turbulenceInterval = 2f;     // seconds between hits

    [Header("Timing")]
    public float sceneDuration = 8f;    // seconds total in flight scene

    [Header("Audio")]
    public AudioSource engineHumAudio;  // interior engine hum
    public AudioSource turbulenceAudio; // optional turbulence hit sound

    private bool _playing = false;

    public IEnumerator Play()
    {
        _playing = true;
        cutsceneCamera.gameObject.SetActive(true);

        // Ramp up wind when scene starts
        cloudVolume.profile.TryGet(out _clouds);
        if (_clouds != null)
            DOTween.To(
                () => _clouds.globalSpeed .value,
                x => _clouds.globalSpeed .value = x,
                flightWindSpeed, 2f);  // 2 seconds to ramp up

        if (engineHumAudio != null)
        {
            engineHumAudio.loop = true;
            engineHumAudio.Play();
        }

        // Start cloud loop and turbulence
        StartCoroutine(Turbulence());

        yield return new WaitForSeconds(sceneDuration);

        _playing = false;

        // Ramp wind back down
        if (_clouds != null)
            DOTween.To(
                () => _clouds.globalSpeed .value,
                x => _clouds.globalSpeed .value = x,
                defaultWindSpeed, 2f);

        if (engineHumAudio != null)
            engineHumAudio.DOFade(0f, 1f);

        cutsceneCamera.gameObject.SetActive(false);
    }

    IEnumerator Turbulence()
    {
        while (_playing)
        {
            yield return new WaitForSeconds(turbulenceInterval);

            if (!_playing) break;

            if (turbulenceAudio != null) turbulenceAudio.Play();

            cutsceneCamera.DOShakePosition(
                turbulenceDuration, turbulenceStrength, 20)
                .SetEase(Ease.OutQuad);

            // Random next interval so it feels natural
            turbulenceInterval = Random.Range(1.5f, 3.5f);
        }
    }
}