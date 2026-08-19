using UnityEngine;
using DG.Tweening;
using System.Collections;

/// Hard cut away from the extraction: a bomber climbing out of frame, seen from
/// below. Deliberately a ground-anchored camera so the scale reads — the shot
/// after this one is a close-up on what the bomber is carrying.
public class BomberAscentCutscene : MonoBehaviour
{
    [Header("Camera")]
    public Transform cutsceneCamera;

    [Header("Bomber")]
    public Transform bomber;
    public float climbSpeed = 120f;     // meters per second along its own nose
    public float climbAngle = 28f;      // degrees nose pitches up
    public float pitchUpDuration = 2f;  // seconds to rotate into the climb

    [Header("Entry")]
    // The bomber starts low and behind the camera so it enters frame rather than
    // being sitting there when the cut lands.
    public Vector3 startOffset = new Vector3(0f, -40f, -260f); // meters, relative to camera
    public float bankAngle = 8f;        // degrees of roll during the climb
    public float bankDuration = 3f;     // seconds to reach full bank

    [Header("Camera Track")]
    public float trackDamping = 2.5f;   // how hard the camera pans to keep up
    public float trackLead = 0.35f;     // 0-1, how far ahead of the bomber it aims
    public bool  keepCameraStatic = true; // pan only, never translate

    [Header("Timing")]
    public float sceneDuration = 6f;    // seconds total

    [Header("Audio")]
    public AudioSource engineAudio;     // bomber pass — expects a doppler-ish clip
    public float engineRampUpTime = 1.5f; // seconds to reach full volume

    private bool _playing = false;

    public IEnumerator Play()
    {
        _playing = true;
        cutsceneCamera.gameObject.SetActive(true);

        if (bomber != null)
        {
            bomber.gameObject.SetActive(true);
            bomber.position = cutsceneCamera.position + cutsceneCamera.TransformVector(startOffset);
            bomber.rotation = Quaternion.LookRotation(cutsceneCamera.forward);
        }

        if (engineAudio != null)
        {
            engineAudio.loop = true;
            engineAudio.volume = 0f;
            engineAudio.Play();
            engineAudio.DOFade(1f, engineRampUpTime);
        }

        StartCoroutine(FlyOut());
        StartCoroutine(TrackBomber());

        yield return new WaitForSeconds(sceneDuration);

        _playing = false;

        if (engineAudio != null) engineAudio.DOFade(0f, 0.6f);

        cutsceneCamera.gameObject.SetActive(false);
    }

    /// Halts playback and releases every tween this cutscene owns. Used by the
    /// skip path, which must not fall back on DOTween.KillAll — that would also
    /// kill cleanup tweens belonging to persistent objects in other scenes.
    public void Stop()
    {
        _playing = false;
        StopAllCoroutines();

        if (cutsceneCamera != null) DOTween.Kill(cutsceneCamera);
        if (bomber != null) DOTween.Kill(bomber);
        if (engineAudio != null)
        {
            DOTween.Kill(engineAudio);
            engineAudio.Stop();
        }
    }

    IEnumerator FlyOut()
    {
        if (bomber == null) yield break;

        // Pitch and bank are driven here rather than by two DORotate tweens.
        // They overlap in time — the roll starts before the climb has settled —
        // and two tweens writing the same rotation would fight over it.
        float heading = bomber.eulerAngles.y;
        float bankStart = pitchUpDuration * 0.5f;
        float elapsed = 0f;

        while (_playing)
        {
            elapsed += Time.deltaTime;

            float pitchT = Mathf.SmoothStep(0f, 1f,
                Mathf.Clamp01(elapsed / Mathf.Max(0.01f, pitchUpDuration)));
            float bankT = Mathf.SmoothStep(0f, 1f,
                Mathf.Clamp01((elapsed - bankStart) / Mathf.Max(0.01f, bankDuration)));

            bomber.rotation = Quaternion.Euler(
                -climbAngle * pitchT, heading, bankAngle * bankT);

            // Written after the rotation so the nose direction used for travel is
            // this frame's, not the previous one's.
            bomber.position += bomber.forward * climbSpeed * Time.deltaTime;

            yield return null;
        }
    }

    IEnumerator TrackBomber()
    {
        while (_playing && bomber != null)
        {
            // Aim slightly ahead of the airframe: a camera that centres the
            // target perfectly looks locked-on and robotic on a fast climb.
            Vector3 aim = bomber.position + bomber.forward * (climbSpeed * trackLead);

            Quaternion targetRot = Quaternion.LookRotation(aim - cutsceneCamera.position);
            cutsceneCamera.rotation = Quaternion.Slerp(
                cutsceneCamera.rotation, targetRot, trackDamping * Time.deltaTime);

            if (!keepCameraStatic)
                cutsceneCamera.position = Vector3.Lerp(
                    cutsceneCamera.position,
                    bomber.position - bomber.forward * 60f,
                    trackDamping * Time.deltaTime);

            yield return null;
        }
    }
}
