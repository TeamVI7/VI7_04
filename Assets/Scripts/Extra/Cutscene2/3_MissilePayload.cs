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

    [Header("Editor")]
    public bool drawGizmos = true;

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

    // ───────────────────────────────────────────────────────────────
    // EDITOR
    // ───────────────────────────────────────────────────────────────

    private void OnDrawGizmosSelected()
    {
        if (!drawGizmos) return;

        Color c = CutsceneGizmos.Shot3;

        // ── DOLLY ─────────────────────────────────────────────
        // Only localPosition/localRotation are read, and they are applied to a
        // camera parented to the bomber. Anything not parented under the bomber
        // therefore lands somewhere else entirely at runtime — flagged rather
        // than drawn wrong, because a correct-looking gizmo on a misparented
        // empty is worse than none.
        bool startOk = dollyStart != null && bomber != null && dollyStart.IsChildOf(bomber);
        bool endOk   = dollyEnd   != null && bomber != null && dollyEnd.IsChildOf(bomber);

        if (dollyStart != null)
            CutsceneGizmos.CameraPose(dollyStart, startOk ? c : Color.red,
                                      startOk ? "dolly start (tail)"
                                              : "dolly start NOT under bomber", 0.8f);
        if (dollyEnd != null)
            CutsceneGizmos.CameraPose(dollyEnd, endOk ? c : Color.red,
                                      endOk ? "dolly end (nose)"
                                            : "dolly end NOT under bomber", 0.8f);

        if (dollyStart != null && dollyEnd != null)
            CutsceneGizmos.SightLine(dollyStart.position, dollyEnd.position, c,
                                     "push " + dollyDuration.ToString("0.#") + "s");

        if (bomber != null) CutsceneGizmos.Marker(bomber.position, 2f, c * 0.7f, "bomber");
        if (pylon  != null) CutsceneGizmos.Marker(pylon.position, 0.5f, c * 0.8f, "pylon pivot");

        // ── DROP ──────────────────────────────────────────────
        if (missile != null)
        {
            // The fall is authored in the parent's frame, so "down" is the
            // parent's down, not the world's — on a banked wing those differ.
            Vector3 down = missile.parent != null
                ? missile.parent.TransformDirection(Vector3.down)
                : Vector3.down;

            Vector3 from = missile.position;
            Vector3 to = from + down * dropDistance;

            // t squared, matching Drop(): slow off the rail, then away fast.
            const int samples = 20;
            Vector3 prev = from;
            for (int i = 1; i <= samples; i++)
            {
                float t = i / (float)samples;
                Vector3 next = from + down * (dropDistance * t * t);
                Gizmos.color = c;
                Gizmos.DrawLine(prev, next);
                prev = next;

                // Where the booster lights, partway through the fall.
                if (i == Mathf.RoundToInt(ignitionAt * samples))
                    CutsceneGizmos.Marker(next, 1f, CutsceneGizmos.Shot4, "ignition");
            }

            bool parented = missile.parent != null;
            CutsceneGizmos.Marker(from, 1f, parented ? c : Color.red,
                                  parented ? "missile" : "missile has NO parent");
            CutsceneGizmos.Marker(to, 2f, CutsceneGizmos.Handoff,
                                  "released  (" + CutsceneGizmos.Metres(dropDistance) +
                                  ")  >  shot 4 starts here");
        }
    }
}
