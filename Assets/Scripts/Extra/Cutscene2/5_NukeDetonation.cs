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

    [Header("Ground Zero")]
    [Tooltip("Use the real impact point handed over by the flight shot. A missile " +
             "that flies to one place and detonates somewhere else reads as a bug, " +
             "so this normally wins over detonationPoint. Turn off to always use " +
             "the authored marker.")]
    public bool useImpactPoint = true;

    [Tooltip("Take the horizontal position from the impact but the height from " +
             "detonationPoint. Without this the fireball spawns wherever the " +
             "missile was when the shot cut — which is still in the air.")]
    public bool useDetonationPointHeight = true;

    [Header("Structure")]
    // The target coming apart. Optional — without it the blast is particles only.
    public StructureDemolition structure;

    [Header("Atmosphere")]
    // Optional. Tears the cloud deck open and lights it from the fireball.
    public CutsceneAtmosphere atmosphere;

    [Header("Framing")]
    // A near-locked-off camera. The blast does the moving — but "near-locked-off"
    // only reads as a choice if something on screen is moving, and at the
    // standoff this shot is watched from, metres of dolly are sub-pixel.
    public Transform cameraAnchor;      // where the shot is watched from
    public float slowPushDistance = 12f;// meters of very slow push in over the shot
    public float pushDuration = 9f;     // seconds of that push

    [Header("Cloud Track")]
    // The column climbs for the whole shot. Aimed once at ground zero and left
    // there, the camera watches an empty crater while the cloud leaves frame.
    public float cloudTrackHeight = 260f;   // meters the aim point rises with the column
    public float cloudTrackDuration = 9f;   // seconds the tilt takes

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
    public float shakeAngle = 0.9f;     // degrees of buffet — the half that reads at distance
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

    // Where the blast actually went off — handed over by the flight shot, so it
    // is only known once Play() has resolved it. CameraMove re-aims at a point
    // climbing above this every frame.
    private Vector3 _groundZero;

    /// Impact point handed over by the missile flight shot. Falls back to
    /// <see cref="detonationPoint"/> when the flight shot was skipped.
    public Vector3? OverridePoint { get; set; }

    public IEnumerator Play()
    {
        _playing = true;
        cutsceneCamera.gameObject.SetActive(true);

        // Vector3.zero here is not a choice, it is the absence of one. Left
        // silent it looks like the shot decided to detonate at world origin.
        if (detonationPoint == null && !OverridePoint.HasValue)
            Debug.LogWarning("NukeDetonationCutscene: no detonationPoint and no impact " +
                             "point from the flight shot — detonating at world origin.", this);

        Vector3 authored = detonationPoint != null ? detonationPoint.position : Vector3.zero;

        Vector3 groundZero = (useImpactPoint && OverridePoint.HasValue)
            ? OverridePoint.Value
            : authored;

        // The flight shot trims cutBeforeImpact off the end of the run, so the
        // missile's last position is still airborne — and on a diving profile
        // that is tens of metres up, not a token few. Taking the horizontal
        // position from where the missile actually went and the height from the
        // authored marker keeps both halves honest.
        if (useDetonationPointHeight && detonationPoint != null)
            groundZero.y = authored.y;

        _groundZero = groundZero;

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

        // The structure is standing at ground zero, so it goes with the flash —
        // not with the shockwave that reaches the camera three seconds later.
        // Fired here rather than on a timer so it uses the real impact point.
        if (structure != null) structure.Demolish(groundZero);

        // Sky goes with the flash too. The shockwave reaching the camera later is
        // a separate beat — this is the light and the column arriving overhead.
        if (atmosphere != null) atmosphere.Detonate();

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
            float buffet = 0f;

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
                    float falloff = decay * decay;
                    shakePhase += Time.deltaTime * shakeVibrato;

                    offset = new Vector3(
                        Mathf.Sin(shakePhase * 1.9f),
                        Mathf.Sin(shakePhase * 2.7f),
                        Mathf.Sin(shakePhase * 1.3f)) * (shakeStrength * falloff);

                    // Translating the camera a metre or two is invisible from a
                    // kilometre out — every point on screen moves by the same
                    // sub-pixel amount. Rotating it moves the whole frame, which
                    // is what actually sells the wave landing.
                    buffet = shakeAngle * falloff;
                }
            }

            cutsceneCamera.position = start + push * t + offset;

            // ── TILT UP WITH THE COLUMN ───────────────────────
            // Re-aimed every frame rather than aimed once in Play(): the cloud
            // climbs for the length of the shot, and a camera nailed to ground
            // zero spends the back half of it watching an empty crater.
            float rise = Mathf.SmoothStep(0f, 1f,
                Mathf.Clamp01(elapsed / Mathf.Max(0.01f, cloudTrackDuration)));
            Vector3 aim = _groundZero + Vector3.up * (cloudTrackHeight * rise);

            Quaternion look = Quaternion.LookRotation(aim - cutsceneCamera.position);
            if (buffet > 0f)
                look *= Quaternion.Euler(
                    Mathf.Sin(shakePhase * 1.7f) * buffet,
                    Mathf.Sin(shakePhase * 1.1f) * buffet,
                    Mathf.Sin(shakePhase * 2.3f) * buffet);

            cutsceneCamera.rotation = look;

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

        // Where the aim ends up once the tilt has run, and the cone it swept.
        Vector3 aimTop = detonationPoint.position + Vector3.up * cloudTrackHeight;
        CutsceneGizmos.Arrow(detonationPoint.position, aimTop, c * 0.8f,
                             "cloud track  " + CutsceneGizmos.Metres(cloudTrackHeight) +
                             " over " + cloudTrackDuration.ToString("0.#") + "s");
        Gizmos.color = c * 0.4f;
        Gizmos.DrawLine(cameraAnchor.position, aimTop);

        // Both camera motions are authored in absolute metres while the shot is
        // framed from kilometres away, so they can silently become sub-pixel.
        // The inspector cannot show that; this can.
        float pushPercent = slowPushDistance / Mathf.Max(0.01f, dist) * 100f;
        if (pushPercent < 1.5f)
            CutsceneGizmos.Label(cameraAnchor.position + Vector3.up * 12f,
                                 "push is " + pushPercent.ToString("0.0") + "% of standoff - invisible" +
                                 "   (try " + CutsceneGizmos.Metres(dist * 0.03f) + ")",
                                 Color.red);

        if (flashLight != null)
            CutsceneGizmos.Marker(flashLight.transform.position, 3f, c * 0.7f, "flash light");
    }
}
