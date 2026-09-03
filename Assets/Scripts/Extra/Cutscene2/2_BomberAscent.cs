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
    //
    // Placing it by offset means the entry is correct by construction whatever
    // the camera is doing — but it also means the transform you authored in the
    // scene is thrown away, which is surprising if you positioned it by eye
    // against terrain or a skyline. Switch to the authored pose when the entry
    // needs to relate to something in the world rather than to the lens.
    [Tooltip("Fly from the bomber's own transform, ignoring startOffset. Position " +
             "and full rotation are both kept exactly as authored — the climb and " +
             "bank are layered on top of them, not built from scratch.")]
    public bool useAuthoredStart = false;

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

    [Header("Editor")]
    public bool drawGizmos = true;

    private bool _playing = false;

    public IEnumerator Play()
    {
        _playing = true;
        cutsceneCamera.gameObject.SetActive(true);

        if (bomber != null)
        {
            bomber.gameObject.SetActive(true);

            // Left alone on the authored path — FlyOut reads the heading off the
            // bomber's own yaw, so whatever it is aimed at in the scene is the
            // way it climbs.
            if (!useAuthoredStart)
            {
                bomber.position = cutsceneCamera.position + cutsceneCamera.TransformVector(startOffset);
                bomber.rotation = Quaternion.LookRotation(cutsceneCamera.forward);
            }
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
        //
        // The climb is applied ON TOP of the bomber's rotation rather than
        // rebuilt from its yaw. Rebuilding kept the heading and silently
        // flattened any pitch or roll the aircraft was authored with, on the
        // first frame, every time.
        Quaternion baseRot = bomber.rotation;
        float bankStart = pitchUpDuration * 0.5f;
        float elapsed = 0f;

        while (_playing)
        {
            elapsed += Time.deltaTime;

            float pitchT = Mathf.SmoothStep(0f, 1f,
                Mathf.Clamp01(elapsed / Mathf.Max(0.01f, pitchUpDuration)));
            float bankT = Mathf.SmoothStep(0f, 1f,
                Mathf.Clamp01((elapsed - bankStart) / Mathf.Max(0.01f, bankDuration)));

            bomber.rotation = baseRot * Quaternion.Euler(
                -climbAngle * pitchT, 0f, bankAngle * bankT);

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

    // ───────────────────────────────────────────────────────────────
    // EDITOR
    // ───────────────────────────────────────────────────────────────

    private void OnDrawGizmosSelected()
    {
        if (!drawGizmos || cutsceneCamera == null) return;

        Color c = CutsceneGizmos.Shot2;

        CutsceneGizmos.CameraPose(cutsceneCamera, c, "shot 2 camera", 2f);

        // Where the run actually begins, which is only where the bomber sits in
        // the scene when useAuthoredStart is on. Otherwise Play() teleports it to
        // startOffset in camera space, and drawing that is the only way to tell
        // whether the offset puts it behind and below the camera — which is what
        // makes it enter frame from underneath rather than pop in overhead.
        Vector3 spawn;
        Quaternion baseRot;

        if (useAuthoredStart && bomber != null)
        {
            spawn = bomber.position;
            baseRot = bomber.rotation;
            CutsceneGizmos.Marker(spawn, 4f, c, "bomber start (authored)");
            CutsceneGizmos.CameraPose(spawn, baseRot, c * 0.9f, null, 5f);
        }
        else
        {
            spawn = cutsceneCamera.position + cutsceneCamera.TransformVector(startOffset);
            baseRot = Quaternion.LookRotation(cutsceneCamera.forward);
            CutsceneGizmos.Marker(spawn, 4f, c, "bomber spawn (from startOffset)");

            // Only meaningful in offset mode — it is the relationship the offset
            // is expressed in.
            CutsceneGizmos.SightLine(cutsceneCamera.position, spawn, c * 0.7f, "entry");
        }

        // ── CLIMB ─────────────────────────────────────────────
        // Integrated with the same rule FlyOut uses: pitch smoothsteps in over
        // pitchUpDuration while the nose drives the travel, so the path curves
        // instead of running straight out at the final angle.
        float bankStart = pitchUpDuration * 0.5f;

        const int samples = 40;
        float dt = Mathf.Max(0.01f, sceneDuration) / samples;
        Vector3 pos = spawn;
        Vector3 prev = pos;

        for (int i = 1; i <= samples; i++)
        {
            float elapsed = i * dt;
            float pitchT = Mathf.SmoothStep(0f, 1f,
                Mathf.Clamp01(elapsed / Mathf.Max(0.01f, pitchUpDuration)));
            float bankT = Mathf.SmoothStep(0f, 1f,
                Mathf.Clamp01((elapsed - bankStart) / Mathf.Max(0.01f, bankDuration)));

            Quaternion rot = baseRot * Quaternion.Euler(-climbAngle * pitchT, 0f, bankAngle * bankT);
            pos += rot * Vector3.forward * climbSpeed * dt;

            Gizmos.color = c;
            Gizmos.DrawLine(prev, pos);
            prev = pos;

            // A pose partway up, where the bomber is roughly at its closest read.
            if (i == samples / 3)
                CutsceneGizmos.CameraPose(pos, rot, c * 0.8f, null, 3f);
        }

        CutsceneGizmos.Marker(pos, 6f, CutsceneGizmos.Handoff,
                              "exit after " + sceneDuration.ToString("0.#") + "s  (" +
                              CutsceneGizmos.Metres(Vector3.Distance(cutsceneCamera.position, pos)) +
                              " out)");
    }
}
