using UnityEngine;
using DG.Tweening;
using System.Collections;
using System.Collections.Generic;

/// Establishing shot that opens the cutscene, before the dropship is boarded.
///
/// Unlike the three cinematics that follow it, this one is built to run
/// *underneath* the briefing text rather than in front of it: it starts without
/// blocking and, once its move is done, holds on the final framing for as long
/// as the briefing needs. The manager decides when the shot is over, so the
/// authored duration is a pacing target rather than a hard cut point.
///
/// Leave the manager's intro slot empty and the briefing plays over black,
/// exactly as it did before.
public class IntroCutscene : MonoBehaviour
{
    [Header("Camera")]
    public Transform cutsceneCamera;

    [Header("Path — one empty for a static frame, more to drift between")]
    public Transform[] waypoints;
    public float duration = 14f;        // seconds for the full move

    [Header("Framing")]
    public Transform lookTarget;        // optional; overrides waypoint rotation
    public float fovDrift = -3f;        // degrees of push-in across the shot

    [Header("Feel")]
    public CameraHandheld handheld;     // optional drift layer

    [Header("Audio")]
    public AudioSource ambienceAudio;   // optional bed under the briefing
    public float ambienceVolume = 1f;
    public float ambienceFadeIn = 2f;
    public float ambienceFadeOut = 1f;

    private Camera _cam;
    private float _baseFov;

    private bool _playing;
    private float _elapsed;
    private Coroutine _shot;

    // Compacted at Begin() so half-authored waypoint arrays with empty slots in
    // them cannot throw once the shot is running.
    private Transform[] _path;
    private float[] _segments;
    private float _pathLength;
    private Vector3 _homePos;
    private Quaternion _homeRot;

    public bool IsPlaying => _playing;

    /// Starts the shot without blocking, so the briefing can type over it. The
    /// camera holds its final framing — still drifting — until End() or Stop().
    public void Begin()
    {
        if (_playing || cutsceneCamera == null) return;

        _playing = true;
        _shot = StartCoroutine(RunShot());
    }

    /// Yields until the authored move has played out. The shot keeps holding on
    /// its final framing afterwards — the caller decides when to End() it.
    public IEnumerator WaitForMove()
    {
        while (_playing && _elapsed < duration) yield return null;
    }

    /// Blocking form, for using the intro as a standalone establishing shot with
    /// nothing typed over it. Returns once the authored move has played out.
    public IEnumerator Play()
    {
        Begin();

        yield return WaitForMove();

        End();
    }

    /// Ends the shot and puts the camera away. Safe whether or not the move
    /// finished, and safe to call twice.
    public void End()
    {
        if (!_playing) return;

        _playing = false;

        if (_shot != null)
        {
            StopCoroutine(_shot);
            _shot = null;
        }

        if (ambienceAudio != null)
        {
            AudioSource bed = ambienceAudio;
            bed.DOFade(0f, ambienceFadeOut).OnComplete(() => bed.Stop());
        }

        RestoreFov();

        if (cutsceneCamera != null) cutsceneCamera.gameObject.SetActive(false);
    }

    /// Halts playback and releases every tween this cutscene owns. Used by the
    /// skip path, which must not fall back on DOTween.KillAll — that would also
    /// kill cleanup tweens belonging to persistent objects in other scenes.
    public void Stop()
    {
        _playing = false;
        _shot = null;
        StopAllCoroutines();
        RestoreFov();

        if (cutsceneCamera != null) DOTween.Kill(cutsceneCamera);
        if (ambienceAudio != null)
        {
            // Killed rather than faded: the skip path is already on its way to the
            // next scene, so a fade would be cut off mid-tween anyway.
            DOTween.Kill(ambienceAudio);
            ambienceAudio.Stop();
        }
    }

    private IEnumerator RunShot()
    {
        cutsceneCamera.gameObject.SetActive(true);

        CacheCamera();
        BuildPath();

        if (ambienceAudio != null)
        {
            ambienceAudio.loop = true;
            ambienceAudio.volume = 0f;
            ambienceAudio.Play();
            ambienceAudio.DOFade(ambienceVolume, ambienceFadeIn);
        }

        _elapsed = 0f;

        while (_playing)
        {
            _elapsed += Time.deltaTime;

            // Clamped rather than ended: past the move the camera holds its final
            // framing and keeps drifting, so a briefing that outlasts the authored
            // duration does not run out of shot.
            float t = duration > 0f ? Mathf.Clamp01(_elapsed / duration) : 1f;
            float eased = Mathf.SmoothStep(0f, 1f, t);

            Sample(eased, out Vector3 pos, out Quaternion rot);

            // A look target wins over the authored waypoint rotation, so a slow
            // arc around a subject only needs its positions placing.
            if (lookTarget != null)
            {
                Vector3 toTarget = lookTarget.position - pos;
                if (toTarget.sqrMagnitude > 0.0001f)
                    rot = Quaternion.LookRotation(toTarget, Vector3.up);
            }

            // Handheld drift composed into the same single write, matching the
            // other cutscenes rather than letting the component move the camera.
            if (handheld != null)
            {
                handheld.Sample(Time.time);
                pos += rot * handheld.PositionOffset;
                rot *= handheld.RotationOffset;
            }

            cutsceneCamera.SetPositionAndRotation(pos, rot);

            if (_cam != null && _baseFov > 0f)
                _cam.fieldOfView = _baseFov + fovDrift * eased;

            yield return null;
        }
    }

    /// Position and rotation at normalised path position <paramref name="u"/>,
    /// parameterised by distance so uneven waypoint spacing does not change speed.
    private void Sample(float u, out Vector3 pos, out Quaternion rot)
    {
        if (_path == null || _path.Length < 2 || _pathLength <= 0f)
        {
            pos = _homePos;
            rot = _homeRot;
            return;
        }

        float along = Mathf.Clamp01(u) * _pathLength;

        int seg = 0;
        while (seg < _segments.Length - 1 && along > _segments[seg])
        {
            along -= _segments[seg];
            seg++;
        }

        float localT = _segments[seg] > 0f ? Mathf.Clamp01(along / _segments[seg]) : 1f;

        pos = Vector3.Lerp(_path[seg].position, _path[seg + 1].position, localT);
        rot = Quaternion.Slerp(_path[seg].rotation, _path[seg + 1].rotation, localT);
    }

    private void BuildPath()
    {
        List<Transform> valid = new List<Transform>();
        if (waypoints != null)
        {
            foreach (var w in waypoints)
            {
                if (w != null) valid.Add(w);
            }
        }

        _path = valid.ToArray();
        _segments = new float[Mathf.Max(0, _path.Length - 1)];
        _pathLength = 0f;

        for (int i = 0; i < _segments.Length; i++)
        {
            _segments[i] = Vector3.Distance(_path[i].position, _path[i + 1].position);
            _pathLength += _segments[i];
        }

        // With no waypoints authored the shot is simply wherever the camera was
        // left in the scene, plus drift.
        _homePos = _path.Length > 0 ? _path[0].position : cutsceneCamera.position;
        _homeRot = _path.Length > 0 ? _path[0].rotation : cutsceneCamera.rotation;
    }

    private void CacheCamera()
    {
        if (_cam == null && cutsceneCamera != null)
            _cam = cutsceneCamera.GetComponentInChildren<Camera>(true);

        if (_cam != null && _baseFov <= 0f) _baseFov = _cam.fieldOfView;
    }

    private void RestoreFov()
    {
        if (_cam != null && _baseFov > 0f) _cam.fieldOfView = _baseFov;
    }

#if UNITY_EDITOR
    [Header("Gizmos")]
    public bool drawGizmos = true;
    public float gizmoScale = 0.3f;

    private void OnDrawGizmos()
    {
        if (!drawGizmos) return;

        // ── STATIC FRAME ──────────────────────────────────────
        // Drawn off the camera itself so an intro with no waypoints authored yet
        // still previews something.
        if (waypoints == null || waypoints.Length < 2)
        {
            Transform anchor = (waypoints != null && waypoints.Length == 1 && waypoints[0] != null)
                ? waypoints[0]
                : cutsceneCamera;
            if (anchor == null) return;

            Gizmos.color = new Color(0.5f, 0.8f, 1f);
            Gizmos.DrawWireSphere(anchor.position, gizmoScale * 0.35f);
            Gizmos.DrawRay(anchor.position, anchor.forward * gizmoScale * 4f);

            DrawLookTarget(anchor.position);

            UnityEditor.Handles.color = Color.white;
            UnityEditor.Handles.Label(
                anchor.position + Vector3.up * gizmoScale * 2f,
                $"Intro\nstatic frame\n~{duration:0.0}s");
            return;
        }

        // ── PATH ──────────────────────────────────────────────
        // Subdivided and shaded by eased speed, so the slow-in and slow-out of the
        // move read on the path rather than having to be imagined.
        const int subdivisions = 12;
        float total = 0f;
        for (int i = 0; i < waypoints.Length - 1; i++)
        {
            if (waypoints[i] == null || waypoints[i + 1] == null) continue;
            total += Vector3.Distance(waypoints[i].position, waypoints[i + 1].position);
        }
        if (total <= 0f) return;

        float walked = 0f;
        for (int i = 0; i < waypoints.Length - 1; i++)
        {
            if (waypoints[i] == null || waypoints[i + 1] == null) continue;

            Vector3 a = waypoints[i].position;
            Vector3 b = waypoints[i + 1].position;
            float segLen = Vector3.Distance(a, b);

            for (int s = 0; s < subdivisions; s++)
            {
                float t0 = s / (float)subdivisions;
                float t1 = (s + 1) / (float)subdivisions;
                float u = (walked + segLen * (t0 + t1) * 0.5f) / total;

                // Derivative of SmoothStep, normalised: 0 at the ends, 1 mid-move.
                float speed = 6f * u * (1f - u);
                Gizmos.color = Color.Lerp(new Color(0f, 0.35f, 0.6f), new Color(0.4f, 0.85f, 1f), speed);
                Gizmos.DrawLine(Vector3.Lerp(a, b, t0), Vector3.Lerp(a, b, t1));
            }

            walked += segLen;
        }

        // ── WAYPOINT MARKERS ──────────────────────────────────
        for (int i = 0; i < waypoints.Length; i++)
        {
            if (waypoints[i] == null) continue;

            Vector3 p = waypoints[i].position;

            Gizmos.color = Color.white;
            Gizmos.DrawWireSphere(p, gizmoScale * 0.3f);

            // Framing at this waypoint — ignored while a look target is assigned.
            Gizmos.color = lookTarget != null
                ? new Color(0.3f, 0.6f, 1f, 0.25f)
                : new Color(0.3f, 0.6f, 1f);
            Gizmos.DrawRay(p, waypoints[i].forward * gizmoScale * 3f);

            UnityEditor.Handles.color = Color.white;
            UnityEditor.Handles.Label(p + Vector3.up * gizmoScale, $"  {i}");

            DrawLookTarget(p);
        }

        UnityEditor.Handles.color = Color.white;
        UnityEditor.Handles.Label(
            waypoints[0].position + Vector3.up * gizmoScale * 3f,
            $"Intro shot\n{total:0.0} m over {duration:0.0}s\nholds until briefing ends");
    }

    private void DrawLookTarget(Vector3 from)
    {
        if (lookTarget == null) return;

        Gizmos.color = new Color(1f, 0.8f, 0.2f, 0.5f);
        Gizmos.DrawLine(from, lookTarget.position);
    }
#endif
}
