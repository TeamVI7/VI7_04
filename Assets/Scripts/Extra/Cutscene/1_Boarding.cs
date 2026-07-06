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
        for (int i = 1; i < waypoints.Length; i++)
        {
            float dist = Vector3.Distance(
                cutsceneCamera.position, waypoints[i].position);
            float duration = dist / moveSpeed;

            Tween moveTween = cutsceneCamera
                .DOMove(waypoints[i].position, duration)
                .SetEase(Ease.Linear);

            cutsceneCamera
                .DORotateQuaternion(waypoints[i].rotation, duration * 0.5f)
                .SetEase(Ease.InOutSine);

            // Subtle head bob while walking
            cutsceneCamera
                .DOLocalMoveY(cutsceneCamera.localPosition.y + 0.04f, 0.3f)
                .SetEase(Ease.InOutSine)
                .SetLoops(-1, LoopType.Yoyo)
                .SetId("bob");

            yield return moveTween.WaitForCompletion();
            DOTween.Kill("bob");
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
}