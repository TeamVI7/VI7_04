using UnityEngine;
using UnityEngine.UI;
using DG.Tweening;
using System.Collections;

/// The payoff shot: detonation at the impact point, watched from a long way off.
/// Expects the Hovl Studio "Nuclear explosion" prefab
/// (Assets/Package/Hovl Studio/Nuclear explosion/Prefabs) in <see cref="nukePrefab"/>;
/// that prefab is particles only, so the flash, light and audio are driven here.
public class NukeDetonationCutscene : MonoBehaviour
{
    [Header("Camera")]
    public Transform cutsceneCamera;

    [Header("Detonation")]
    public GameObject nukePrefab;       // Hovl Studio → Nuclear explosion.prefab
    public Transform detonationPoint;   // fallback if no impact point is handed in
    public float nukeScale = 1f;        // prefab is authored around a ~1 unit = 1 m scale

    [Header("Framing")]
    // A static, locked-off camera. The blast does the moving.
    public Transform cameraAnchor;      // where the shot is watched from
    public float slowPushDistance = 12f;// meters of very slow push in over the shot
    public float pushDuration = 9f;     // seconds of that push

    [Header("Flash")]
    public Image flashImage;            // full-screen white overlay
    public float flashUpDuration = 0.06f;   // seconds to full white
    public float flashHoldDuration = 0.25f; // seconds held blown out
    public float flashDownDuration = 1.6f;  // seconds to clear
    public Light flashLight;            // optional scene light punched on detonation
    public float flashLightIntensity = 8f;
    public float flashLightFalloff = 2.5f;  // seconds for the light to decay

    [Header("Shockwave")]
    // The blast is seen before it is heard and felt — that delay is what gives
    // the shot its sense of distance.
    public float shockwaveDelay = 3.2f; // seconds between flash and shockwave
    public float shakeDuration = 2.5f;  // seconds of camera shake
    public float shakeStrength = 1.2f;  // meters of shake at the moment it lands
    public int shakeVibrato = 14;       // shake oscillations per second

    [Header("Timing")]
    public float holdOnCloud = 7f;      // seconds held on the rising cloud after the wave

    [Header("Audio")]
    public AudioSource detonationAudio; // plays the two one-shots below
    public AudioClip explosionClip;     // Hovl → Sounds/Nuclear Explosion.wav
    public AudioClip shockwaveClip;     // Hovl → Sounds/Nuclear Shockwave.wav

    [Header("Editor")]
    public bool drawGizmos = true;

    private bool _playing = false;
    private GameObject _spawnedNuke;
    private bool _shaking = false;
    private float _shakeElapsed = 0f;

    /// Impact point handed over by the missile flight shot. Falls back to
    /// <see cref="detonationPoint"/> when the flight shot was skipped.
    public Vector3? OverridePoint { get; set; }

    public IEnumerator Play()
    {
        _playing = true;
        cutsceneCamera.gameObject.SetActive(true);

        Vector3 groundZero = OverridePoint
            ?? (detonationPoint != null ? detonationPoint.position : Vector3.zero);

        if (cameraAnchor != null)
        {
            cutsceneCamera.position = cameraAnchor.position;
            cutsceneCamera.rotation = cameraAnchor.rotation;
        }
        cutsceneCamera.LookAt(groundZero);

        // The push and the shockwave shake are composed into one position write
        // per frame by CameraMove. Run as two concurrent tweens they would fight
        // over the transform and the push would snap back on every shake frame.
        StartCoroutine(CameraMove());

        if (flashImage != null)
        {
            flashImage.gameObject.SetActive(true);
            flashImage.color = new Color(1f, 1f, 1f, 0f);
        }

        yield return new WaitForSeconds(0.4f);

        // ── DETONATION ────────────────────────────────────────
        if (nukePrefab != null)
        {
            _spawnedNuke = Instantiate(nukePrefab, groundZero, Quaternion.identity);
            _spawnedNuke.transform.localScale = Vector3.one * nukeScale;
        }

        if (detonationAudio != null && explosionClip != null)
            detonationAudio.PlayOneShot(explosionClip);

        StartCoroutine(Flash());

        // ── SHOCKWAVE ARRIVES ─────────────────────────────────
        yield return new WaitForSeconds(shockwaveDelay);

        if (!_playing) yield break;

        if (detonationAudio != null && shockwaveClip != null)
            detonationAudio.PlayOneShot(shockwaveClip);

        _shakeElapsed = 0f;
        _shaking = true;

        // ── HOLD ON THE CLOUD ─────────────────────────────────
        yield return new WaitForSeconds(holdOnCloud);

        _playing = false;
        cutsceneCamera.gameObject.SetActive(false);
    }

    /// Halts playback and releases every tween this cutscene owns. Used by the
    /// skip path, which must not fall back on DOTween.KillAll — that would also
    /// kill cleanup tweens belonging to persistent objects in other scenes.
    public void Stop()
    {
        _playing = false;
        _shaking = false;
        StopAllCoroutines();

        if (cutsceneCamera != null) DOTween.Kill(cutsceneCamera);
        if (flashImage != null)
        {
            DOTween.Kill(flashImage);
            flashImage.color = new Color(1f, 1f, 1f, 0f);
        }
        if (detonationAudio != null)
        {
            DOTween.Kill(detonationAudio);
            detonationAudio.Stop();
        }
        if (flashLight != null) flashLight.intensity = 0f;

        // The blast prefab is spawned at runtime and is not parented to anything
        // in the scene, so a skip would otherwise strand it for the scene's life.
        if (_spawnedNuke != null) Destroy(_spawnedNuke);
    }

    /// Slow push plus shockwave shake, composed into a single position write.
    IEnumerator CameraMove()
    {
        Vector3 start = cutsceneCamera.position;
        Vector3 push = cutsceneCamera.forward * slowPushDistance;
        float elapsed = 0f;
        float shakePhase = 0f;

        while (_playing)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / pushDuration));

            Vector3 offset = Vector3.zero;

            if (_shaking)
            {
                _shakeElapsed += Time.deltaTime;
                if (_shakeElapsed >= shakeDuration)
                {
                    _shaking = false;
                }
                else
                {
                    // Hits hardest as the wave arrives, then bleeds off.
                    float decay = 1f - (_shakeElapsed / shakeDuration);
                    float amount = shakeStrength * decay * decay;
                    shakePhase += Time.deltaTime * shakeVibrato;

                    offset = new Vector3(
                        Mathf.Sin(shakePhase * 1.9f),
                        Mathf.Sin(shakePhase * 2.7f),
                        Mathf.Sin(shakePhase * 1.3f)) * amount;
                }
            }

            cutsceneCamera.position = start + push * t + offset;
            yield return null;
        }
    }

    IEnumerator Flash()
    {
        if (flashLight != null)
        {
            flashLight.enabled = true;
            flashLight.intensity = flashLightIntensity;
        }

        if (flashImage != null)
        {
            yield return flashImage.DOFade(1f, flashUpDuration)
                .SetEase(Ease.OutQuad).WaitForCompletion();
            yield return new WaitForSeconds(flashHoldDuration);
            flashImage.DOFade(0f, flashDownDuration).SetEase(Ease.InQuad);
        }

        // Light decays on its own clock, slower than the overlay clears, so the
        // scene stays lit by the fireball after the blow-out has faded.
        if (flashLight != null)
        {
            float elapsed = 0f;
            while (elapsed < flashLightFalloff && _playing)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / flashLightFalloff);
                flashLight.intensity = Mathf.Lerp(flashLightIntensity, 0f, t * t);
                yield return null;
            }
            flashLight.intensity = 0f;
        }
    }

    // ───────────────────────────────────────────────────────────────
    // EDITOR
    // ───────────────────────────────────────────────────────────────

    private void OnDrawGizmosSelected()
    {
        if (!drawGizmos) return;

        Color c = CutsceneGizmos.Shot5;

        // Only the fallback is drawable in the editor — the real ground zero is
        // whatever shot 4 hands over at runtime. Worth keeping detonationPoint
        // roughly where shot 4's target sits so this preview stays honest.
        if (detonationPoint != null)
        {
            CutsceneGizmos.Marker(detonationPoint.position, 8f, c, "ground zero (fallback)");
            CutsceneGizmos.Ring(detonationPoint.position, 60f * nukeScale, c * 0.7f);
            CutsceneGizmos.Ring(detonationPoint.position, 150f * nukeScale, c * 0.4f,
                                "blast scale x" + nukeScale.ToString("0.##"));
        }

        if (cameraAnchor == null) return;

        CutsceneGizmos.CameraPose(cameraAnchor, c, "watch from here", 6f);

        // ── SLOW PUSH ─────────────────────────────────────────
        Vector3 pushed = cameraAnchor.position + cameraAnchor.forward * slowPushDistance;
        CutsceneGizmos.Arrow(cameraAnchor.position, pushed, c * 0.8f,
                             "push " + CutsceneGizmos.Metres(slowPushDistance) +
                             " over " + pushDuration.ToString("0.#") + "s");

        if (detonationPoint == null) return;

        // The distance that decides whether the shot reads as a nuke or as a
        // grenade. Also the one the shockwaveDelay is standing in for — a delay
        // authored against a viewing distance it no longer matches is the thing
        // that quietly breaks this shot.
        float dist = Vector3.Distance(cameraAnchor.position, detonationPoint.position);
        CutsceneGizmos.SightLine(cameraAnchor.position, detonationPoint.position, c, "standoff");

        CutsceneGizmos.Label(
            Vector3.Lerp(cameraAnchor.position, detonationPoint.position, 0.35f) + Vector3.up * 25f,
            "shockwave arrives " + shockwaveDelay.ToString("0.#") + "s after flash" +
            "   (" + (dist / Mathf.Max(0.01f, shockwaveDelay)).ToString("0") + " m/s implied)",
            c * 0.85f);

        if (flashLight != null)
            CutsceneGizmos.Marker(flashLight.transform.position, 3f, c * 0.7f, "flash light");
    }
}
