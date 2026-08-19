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

    [Header("Head Bob")]
    public float bobHeight = 0.04f;     // meters of vertical sway
    public float bobSpeed = 10f;        // radians per second

    [Header("Hatch")]
    public Transform hatch;
    public float hatchCloseDuration = 1.2f; // seconds hatch takes to close
    public float hatchCloseAngle = -50f;    // degrees hatch closes to

    [Header("Audio")]
    public AudioSource footstepAudio;   // optional footstep loop
    public AudioClip hatchAudio;      // optional hatch close sound

    public IEnumerator Play()
    {
        cutsceneCamera.gameObject.SetActive(true);

        // Start at first waypoint
        cutsceneCamera.position = waypoints[0].position;
        cutsceneCamera.rotation = waypoints[0].rotation;

        if (footstepAudio != null)
        {
            footstepAudio.loop = true;
            footstepAudio.Play();
        }

        // ── WALK UP RAMP ──────────────────────────────────────
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

                // Travel and bob are composed into a single write. Running them
                // as two concurrent tweens made them fight over the transform,
                // and re-seeding the bob from the current Y each leg let it
                // creep upward across the ramp.
                Vector3 pos = Vector3.Lerp(startPos, waypoints[i].position, t);
                pos.y += Mathf.Sin(bobPhase) * bobHeight;
                cutsceneCamera.position = pos;

                // Rotation settles over the first half of the leg, as before
                float rotT = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t * 2f));
                cutsceneCamera.rotation = Quaternion.Slerp(startRot, waypoints[i].rotation, rotT);

                yield return null;
            }

            cutsceneCamera.rotation = waypoints[i].rotation;
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

        cutsceneCamera.gameObject.SetActive(false);
    }

    /// Halts playback and releases every tween this cutscene owns. Used by the
    /// skip path, which must not fall back on DOTween.KillAll — that would also
    /// kill cleanup tweens belonging to persistent objects in other scenes.
    public void Stop()
    {
        StopAllCoroutines();

        if (cutsceneCamera != null) DOTween.Kill(cutsceneCamera);
        if (hatch != null) DOTween.Kill(hatch);
        if (footstepAudio != null)
        {
            DOTween.Kill(footstepAudio);
            footstepAudio.Stop();
        }
    }
}
