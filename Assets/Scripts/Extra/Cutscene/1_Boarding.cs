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

            // Ease in at the start, ease out at the end, full stride in between.
            float gait = 1f;
            if (easeFraction > 0f)
            {
                gait = Mathf.Min(
                    Mathf.SmoothStep(0f, 1f, u / easeFraction),
                    Mathf.SmoothStep(0f, 1f, (1f - u) / easeFraction));
            }
            // Never let the stride reach exactly zero, or the loop cannot finish.
            gait = Mathf.Clamp(gait, 0.05f, 1f);

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
}
