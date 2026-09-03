using UnityEngine;
using DG.Tweening;
using System.Collections;

public class BoardingCutscene : MonoBehaviour
{
    [Header("Camera")]
    public Transform cutsceneCamera;

    [Header("Path — place empties along ramp")]
    public Transform[] waypoints;       // empty GOs along ramp path
    public float moveSpeed = 1.5f;      // meters per second walking speed

    [Header("Gait")]
    [Range(0f, 0.5f)]
    public float easeFraction = 0.15f;  // portion of the walk spent starting up / stopping
    public float bobHeight = 0.04f;     // meters of vertical sway
    public float bobSpeed = 10f;        // radians per second
    public float swayScale = 0.6f;      // lateral sway relative to bobHeight
    public float stepRoll = 0.35f;      // degrees of roll per step

    [Header("Feel")]
    public CameraHandheld handheld;     // optional drift layer
    public float fovDrift = -2f;        // degrees of push-in across the walk

    [Header("Hatch")]
    public Transform hatch;
    public float hatchCloseDuration = 1.2f; // seconds hatch takes to close
    public float hatchCloseAngle = -50f;    // degrees hatch closes to

    [Header("Audio")]
    public AudioSource footstepAudio;   // optional footstep loop
    public AudioClip hatchAudio;      // optional hatch close sound

    private Camera _cam;
    private float _baseFov;

    public IEnumerator Play()
    {
        cutsceneCamera.gameObject.SetActive(true);

        CacheCamera();

        // Start at first waypoint
        cutsceneCamera.SetPositionAndRotation(waypoints[0].position, waypoints[0].rotation);

        if (footstepAudio != null)
        {
            footstepAudio.loop = true;
            footstepAudio.Play();
        }

        // ── WALK UP RAMP ──────────────────────────────────────
        // Distance is integrated from a gait envelope rather than driven off a
        // fixed timer, so the walk can start up and settle without the position
        // and the bob disagreeing about how fast the character is moving.
        float[] segmentLengths = new float[Mathf.Max(0, waypoints.Length - 1)];
        float totalLength = 0f;
        for (int i = 0; i < segmentLengths.Length; i++)
        {
            segmentLengths[i] = Vector3.Distance(waypoints[i].position, waypoints[i + 1].position);
            totalLength += segmentLengths[i];
        }

        float travelled = 0f;
        float bobPhase = 0f;

        while (totalLength > 0f && travelled < totalLength)
        {
            float u = travelled / totalLength;

            float gait = GaitAt(u);

            travelled += moveSpeed * gait * Time.deltaTime;
            bobPhase += bobSpeed * gait * Time.deltaTime;

            // Locate the current segment
            float along = Mathf.Min(travelled, totalLength);
            int seg = 0;
            while (seg < segmentLengths.Length - 1 && along > segmentLengths[seg])
            {
                along -= segmentLengths[seg];
                seg++;
            }
            float localT = segmentLengths[seg] > 0f ? Mathf.Clamp01(along / segmentLengths[seg]) : 1f;

            Vector3 pos = Vector3.Lerp(waypoints[seg].position, waypoints[seg + 1].position, localT);
            Quaternion rot = Quaternion.Slerp(
                waypoints[seg].rotation, waypoints[seg + 1].rotation,
                Mathf.SmoothStep(0f, 1f, localT));

            // Real head bob traces a figure eight: the lateral sway runs at half
            // the vertical frequency, with a little roll riding along with it.
            // All of it scales with gait so the sway settles as the walk stops.
            float bobY = Mathf.Sin(bobPhase) * bobHeight * gait;
            float swayX = Mathf.Sin(bobPhase * 0.5f) * bobHeight * swayScale * gait;
            float roll = Mathf.Sin(bobPhase * 0.5f) * stepRoll * gait;

            pos += rot * new Vector3(swayX, bobY, 0f);
            rot *= Quaternion.Euler(0f, 0f, roll);

            // Handheld drift composed into the same single write
            if (handheld != null)
            {
                handheld.Sample(Time.time);
                pos += rot * handheld.PositionOffset;
                rot *= handheld.RotationOffset;
            }

            cutsceneCamera.SetPositionAndRotation(pos, rot);

            // Slow push-in across the approach
            if (_cam != null)
                _cam.fieldOfView = _baseFov + fovDrift * Mathf.SmoothStep(0f, 1f, u);

            yield return null;
        }

        if (footstepAudio != null) footstepAudio.Stop();

        // ── HATCH CLOSES ──────────────────────────────────────
        yield return new WaitForSeconds(0.5f);

        yield return hatch
            .DOLocalRotate(new Vector3(0f, 0f, hatchCloseAngle), hatchCloseDuration)
            .SetEase(Ease.InOutSine)
            .WaitForCompletion();

        yield return hatch
            .DOLocalRotate(new Vector3(hatchCloseAngle, 0f, 0f), hatchCloseDuration)
            .SetEase(Ease.InOutSine)
            .WaitForCompletion();

        yield return new WaitForSeconds(0.3f);

        RestoreFov();
        cutsceneCamera.gameObject.SetActive(false);
    }

    /// Halts playback and releases every tween this cutscene owns. Used by the
    /// skip path, which must not fall back on DOTween.KillAll — that would also
    /// kill cleanup tweens belonging to persistent objects in other scenes.
    public void Stop()
    {
        StopAllCoroutines();
        RestoreFov();

        if (cutsceneCamera != null) DOTween.Kill(cutsceneCamera);
        if (hatch != null) DOTween.Kill(hatch);
        if (footstepAudio != null)
        {
            DOTween.Kill(footstepAudio);
            footstepAudio.Stop();
        }
    }

    /// Stride envelope at normalised path position <paramref name="u"/>: eases in
    /// over the first easeFraction, out over the last, full stride between. Shared
    /// with the gizmo so the preview cannot drift from what actually runs.
    private float GaitAt(float u)
    {
        float gait = 1f;
        if (easeFraction > 0f)
        {
            gait = Mathf.Min(
                Mathf.SmoothStep(0f, 1f, u / easeFraction),
                Mathf.SmoothStep(0f, 1f, (1f - u) / easeFraction));
        }
        // Never let the stride reach exactly zero, or the walk loop cannot finish.
        return Mathf.Clamp(gait, 0.05f, 1f);
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
        if (!drawGizmos || waypoints == null || waypoints.Length < 2) return;

        // Segment lengths, skipping unassigned slots so the gizmo stays usable
        // while the path is still being authored.
        float total = 0f;
        for (int i = 0; i < waypoints.Length - 1; i++)
        {
            if (waypoints[i] == null || waypoints[i + 1] == null) continue;
            total += Vector3.Distance(waypoints[i].position, waypoints[i + 1].position);
        }
        if (total <= 0f) return;

        // ── PATH, COLOURED BY STRIDE ──────────────────────────
        // Subdivided so the ease-in and ease-out zones are visible directly on the
        // path rather than having to be imagined from the easeFraction number.
        const int subdivisions = 12;
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

                float gait = GaitAt(u);
                // Amber where the character is starting up or slowing down,
                // green at full stride.
                Gizmos.color = Color.Lerp(new Color(1f, 0.65f, 0f), Color.green, gait);
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
            Gizmos.DrawWireSphere(p, gizmoScale * 0.35f);

            // Facing at this waypoint — the camera slerps toward it across the leg
            Gizmos.color = new Color(0.3f, 0.6f, 1f);
            Gizmos.DrawRay(p, waypoints[i].forward * gizmoScale * 3f);

            // Bob envelope, so the sway amplitude is judgeable in context
            Gizmos.color = new Color(1f, 1f, 1f, 0.25f);
            Gizmos.DrawLine(p + Vector3.up * bobHeight, p - Vector3.up * bobHeight);

            UnityEditor.Handles.color = Color.white;
            UnityEditor.Handles.Label(p + Vector3.up * gizmoScale, $"  {i}");
        }

        // ── SUMMARY ───────────────────────────────────────────
        // Duration integrates the gait envelope rather than assuming full stride,
        // so it matches the shot length you actually get.
        const int steps = 200;
        float duration = 0f;
        for (int i = 0; i < steps; i++)
        {
            float u = (i + 0.5f) / steps;
            duration += (total / steps) / (moveSpeed * GaitAt(u));
        }

        UnityEditor.Handles.color = Color.white;
        UnityEditor.Handles.Label(
            waypoints[0].position + Vector3.up * gizmoScale * 3f,
            $"Boarding walk\n{total:0.0} m @ {moveSpeed:0.0} m/s\n~{duration:0.0}s");
    }
#endif
}
