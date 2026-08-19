using UnityEngine;
using DG.Tweening;
using System.Collections;

/// Close-up on the payload still mounted under the bomber, ending on release.
/// The camera is parented to the aircraft for the whole shot so the airframe
/// reads as still while the sky moves behind it.
public class MissilePayloadCutscene : MonoBehaviour
{
    [Header("Camera")]
    public Transform cutsceneCamera;

    [Header("Rig")]
    public Transform bomber;            // camera is parented here for the shot
    public Transform missile;           // the payload itself
    public Transform pylon;             // optional clamp/hardpoint that opens

    [Header("Dolly — parent these two under the bomber")]
    // Driven in local space, so the push holds its framing on the airframe even
    // while the bomber itself is flying.
    public Transform dollyStart;        // framing on the missile's tail
    public Transform dollyEnd;          // framing on the nose / seeker head
    public float dollyDuration = 4.5f;  // seconds of the slow push
    public Ease dollyEase = Ease.InOutSine;

    [Header("Release")]
    public float releaseDelay = 0.6f;   // seconds held after the dolly settles
    public float pylonOpenAngle = 40f;  // degrees the clamp swings open
    public float pylonOpenDuration = 0.5f; // seconds for the clamp
    public float dropDistance = 45f;    // meters the missile falls away from the wing
    public float dropDuration = 1.4f;   // seconds of the drop before the cut
    public float dropPitch = 12f;       // degrees the nose tips over as it falls

    [Header("Ignition")]
    public ParticleSystem boosterFlame; // optional — lit at the end of the drop
    public float ignitionAt = 0.7f;     // 0-1 through the drop when the motor lights

    [Header("Timing")]
    public float holdAfterDrop = 0.5f;  // seconds held before cutting away

    [Header("Audio")]
    public AudioSource ambientAudio;    // muffled airframe/wind bed
    public AudioClip clampClip;         // hardpoint release clunk
    public AudioClip ignitionClip;      // motor light-off

    private bool _playing = false;
    private Transform _cameraOriginalParent;

    public IEnumerator Play()
    {
        _playing = true;
        cutsceneCamera.gameObject.SetActive(true);

        _cameraOriginalParent = cutsceneCamera.parent;
        if (bomber != null) cutsceneCamera.SetParent(bomber, true);

        if (dollyStart != null)
        {
            cutsceneCamera.localPosition = dollyStart.localPosition;
            cutsceneCamera.localRotation = dollyStart.localRotation;
        }

        if (ambientAudio != null)
        {
            ambientAudio.loop = true;
            ambientAudio.volume = 0f;
            ambientAudio.Play();
            ambientAudio.DOFade(1f, 1f);
        }

        // ── SLOW PUSH DOWN THE BODY ───────────────────────────
        if (dollyEnd != null)
        {
            cutsceneCamera.DOLocalMove(dollyEnd.localPosition, dollyDuration).SetEase(dollyEase)
                .SetTarget(cutsceneCamera);   // tagged so Stop() can kill it by target
            cutsceneCamera.DOLocalRotateQuaternion(dollyEnd.localRotation, dollyDuration)
                .SetEase(dollyEase)
                .SetTarget(cutsceneCamera);

            yield return new WaitForSeconds(dollyDuration);
        }

        yield return new WaitForSeconds(releaseDelay);

        // ── HARDPOINT RELEASES ────────────────────────────────
        if (clampClip != null && ambientAudio != null)
            ambientAudio.PlayOneShot(clampClip);

        if (pylon != null)
            yield return pylon
                .DOLocalRotate(new Vector3(pylonOpenAngle, 0f, 0f), pylonOpenDuration)
                .SetEase(Ease.OutBack)
                .WaitForCompletion();

        // ── DROP AWAY ─────────────────────────────────────────
        yield return StartCoroutine(Drop());

        yield return new WaitForSeconds(holdAfterDrop);

        _playing = false;

        if (ambientAudio != null) ambientAudio.DOFade(0f, 0.4f);

        // Released from the airframe before the cut — the flight shot drives the
        // missile in world space and must not inherit the bomber's transform.
        if (missile != null) missile.SetParent(null, true);
        cutsceneCamera.SetParent(_cameraOriginalParent, true);
        cutsceneCamera.gameObject.SetActive(false);
    }

    /// Halts playback and releases every tween this cutscene owns. Used by the
    /// skip path, which must not fall back on DOTween.KillAll — that would also
    /// kill cleanup tweens belonging to persistent objects in other scenes.
    public void Stop()
    {
        _playing = false;
        StopAllCoroutines();

        if (cutsceneCamera != null)
        {
            DOTween.Kill(cutsceneCamera);
            cutsceneCamera.SetParent(_cameraOriginalParent, true);
        }
        if (missile != null)
        {
            DOTween.Kill(missile);
            missile.SetParent(null, true);
        }
        if (pylon != null) DOTween.Kill(pylon);
        if (ambientAudio != null)
        {
            DOTween.Kill(ambientAudio);
            ambientAudio.Stop();
        }
        if (boosterFlame != null) boosterFlame.Stop();
    }

    IEnumerator Drop()
    {
        if (missile == null) yield break;

        // Captured in the bomber's frame so the fall stays relative to a wing
        // that is itself still moving.
        Vector3 startLocal = missile.localPosition;
        Vector3 startEuler = missile.localEulerAngles;
        bool ignited = false;

        float elapsed = 0f;
        while (elapsed < dropDuration && _playing)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / dropDuration);

            // Gravity-ish: slow off the rail, then away fast.
            missile.localPosition = startLocal + Vector3.down * (dropDistance * t * t);
            missile.localEulerAngles = startEuler + new Vector3(dropPitch * t, 0f, 0f);

            if (!ignited && t >= ignitionAt)
            {
                ignited = true;
                if (boosterFlame != null) boosterFlame.Play();
                if (ignitionClip != null && ambientAudio != null)
                    ambientAudio.PlayOneShot(ignitionClip);
            }

            yield return null;
        }
    }
}
