using UnityEngine;
using DG.Tweening;
using System.Collections;

public class ExtractionCutscene : MonoBehaviour
{
    [Header("Camera")]
    public Transform cutsceneCamera;

    [Header("Board Path — place empties from the LZ up the ramp")]
    public Transform[] waypoints;       // empty GOs from LZ ground into the hold
    public float moveSpeed = 3.2f;      // meters per second — a run, not the boarding walk

    [Header("Head Bob")]
    public float bobHeight = 0.07f;     // meters of vertical sway
    public float bobSpeed = 14f;        // radians per second

    [Header("Dropship")]
    public Transform dropship;          // whole ship — camera rides it after boarding
    public Transform hatch;
    public float hatchCloseDuration = 1.0f; // seconds hatch takes to close
    public float hatchCloseAngle = -50f;    // degrees hatch closes to

    [Header("Lift Off")]
    public float liftHeight = 220f;     // meters climbed while the shot holds
    public float liftDuration = 5f;     // seconds of climb
    public float liftForward = 160f;    // meters travelled forward during climb
    public float bankAngle = 12f;       // degrees the ship rolls as it pulls away

    [Header("Look Back")]
    // After the hatch shuts the camera swings to the open side and holds on the
    // ground falling away, which is the beat that sells the extraction.
    public Transform lookBackAnchor;    // optional framing target on the LZ
    public float lookBackDelay = 0.4f;  // seconds after hatch before the swing
    public float lookBackDuration = 1.2f; // seconds of the swing itself

    [Header("Timing")]
    public float holdAfterLift = 1.0f;  // seconds held on the receding ground

    [Header("Audio")]
    public AudioSource footstepAudio;   // optional footstep loop
    public AudioSource engineAudio;     // dropship engines
    public AudioClip hatchClip;         // hatch slam
    public float engineRampUpTime = 2f; // seconds to reach full volume

    private bool _playing = false;

    public IEnumerator Play()
    {
        _playing = true;
        cutsceneCamera.gameObject.SetActive(true);

        // The camera is parented to the ship only once the player is aboard, so
        // the run-up is authored in world space against the waypoints.
        cutsceneCamera.SetParent(null, true);
        cutsceneCamera.position = waypoints[0].position;
        cutsceneCamera.rotation = waypoints[0].rotation;

        if (engineAudio != null)
        {
            engineAudio.loop = true;
            engineAudio.volume = 0f;
            engineAudio.Play();
            engineAudio.DOFade(1f, engineRampUpTime);
        }

        if (footstepAudio != null)
        {
            footstepAudio.loop = true;
            footstepAudio.Play();
        }

        // ── RUN FOR THE RAMP ──────────────────────────────────
        // Bob phase runs continuously across every leg so the sway never jumps
        // at a waypoint boundary.
        float bobPhase = 0f;

        for (int i = 1; i < waypoints.Length; i++)
        {
            Vector3 startPos = cutsceneCamera.position;
            Quaternion startRot = cutsceneCamera.rotation;

            float dist = Vector3.Distance(startPos, waypoints[i].position);
            float duration = Mathf.Max(0.01f, dist / moveSpeed);

            float elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                bobPhase += Time.deltaTime * bobSpeed;
                float t = Mathf.Clamp01(elapsed / duration);

                Vector3 pos = Vector3.Lerp(startPos, waypoints[i].position, t);
                pos.y += Mathf.Sin(bobPhase) * bobHeight;
                cutsceneCamera.position = pos;

                float rotT = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t * 2f));
                cutsceneCamera.rotation = Quaternion.Slerp(startRot, waypoints[i].rotation, rotT);

                yield return null;
            }

            cutsceneCamera.rotation = waypoints[i].rotation;
        }

        if (footstepAudio != null) footstepAudio.Stop();

        // Aboard — hand the camera to the ship so the climb carries it along.
        if (dropship != null) cutsceneCamera.SetParent(dropship, true);

        // ── HATCH CLOSES ──────────────────────────────────────
        yield return new WaitForSeconds(0.3f);

        if (hatchClip != null && engineAudio != null)
            engineAudio.PlayOneShot(hatchClip);

        if (hatch != null)
            yield return hatch
                .DOLocalRotate(new Vector3(hatchCloseAngle, 0f, 0f), hatchCloseDuration)
                .SetEase(Ease.InOutSine)
                .WaitForCompletion();

        // ── LIFT OFF ──────────────────────────────────────────
        StartCoroutine(LiftOff());
        StartCoroutine(LookBack());

        yield return new WaitForSeconds(liftDuration + holdAfterLift);

        _playing = false;

        if (engineAudio != null) engineAudio.DOFade(0f, 0.8f);

        // Unparent before the shot ends: the next cutscene owns this camera only
        // if it is not still riding a transform this one was driving.
        cutsceneCamera.SetParent(null, true);
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
            cutsceneCamera.SetParent(null, true);
        }
        if (dropship != null) DOTween.Kill(dropship);
        if (hatch != null) DOTween.Kill(hatch);
        if (footstepAudio != null)
        {
            DOTween.Kill(footstepAudio);
            footstepAudio.Stop();
        }
        if (engineAudio != null)
        {
            DOTween.Kill(engineAudio);
            engineAudio.Stop();
        }
    }

    IEnumerator LiftOff()
    {
        if (dropship == null) yield break;

        Vector3 startPos = dropship.position;
        Vector3 forward = dropship.forward;
        Quaternion startRot = dropship.rotation;

        dropship.DORotateQuaternion(startRot * Quaternion.Euler(0f, 0f, bankAngle), liftDuration * 0.6f)
            .SetEase(Ease.InOutSine)
            .SetTarget(dropship);   // tagged so Stop() can kill it by target

        float elapsed = 0f;
        while (elapsed < liftDuration && _playing)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / liftDuration);

            // Climb eases out, forward run eases in — the ship goes up hard off
            // the deck, then converts that into distance.
            float climb = Mathf.SmoothStep(0f, 1f, t);
            float run = t * t;

            dropship.position = startPos
                + Vector3.up * (liftHeight * climb)
                + forward * (liftForward * run);

            yield return null;
        }
    }

    IEnumerator LookBack()
    {
        yield return new WaitForSeconds(lookBackDelay);

        if (!_playing || lookBackAnchor == null) yield break;

        Quaternion startRot = cutsceneCamera.rotation;
        float elapsed = 0f;

        while (elapsed < lookBackDuration && _playing)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / lookBackDuration));

            // Re-aimed every frame rather than tweened to a fixed quaternion: the
            // anchor keeps sliding away underneath as the ship climbs.
            Quaternion targetRot = Quaternion.LookRotation(
                lookBackAnchor.position - cutsceneCamera.position);

            cutsceneCamera.rotation = Quaternion.Slerp(startRot, targetRot, t);
            yield return null;
        }

        // Hold the anchor for the rest of the shot.
        while (_playing)
        {
            cutsceneCamera.rotation = Quaternion.Slerp(
                cutsceneCamera.rotation,
                Quaternion.LookRotation(lookBackAnchor.position - cutsceneCamera.position),
                3f * Time.deltaTime);
            yield return null;
        }
    }
}
