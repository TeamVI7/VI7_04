using UnityEngine;
using DG.Tweening;
using System.Collections;

public class TakeoffCutscene : MonoBehaviour
{
    [Header("Camera")]
    public Transform cutsceneCamera;

    [Header("Plane")]
    public Transform plane;     
    public float climbSpeed = 60f;          // meters per second climbing
    public float climbAngle = 20f;          // degrees nose pitches up

    [Header("Takeoff Path")]
    public AnimationCurve liftCurve;     // Y height over time
    public AnimationCurve forwardCurve;  // forward speed over time
    public float takeoffDuration = 6f;   // seconds for full takeoff
    
    [Header("Nacelles")]
    public Transform nacelleLeft;
    public Transform nacelleRight;
    public float nacelleHoverAngle = -90f;   // degrees in hover mode
    public float nacelleFlyAngle = 0f;       // degrees in flight mode
    public float nacelleConvertDuration = 1.8f; // seconds to convert

    [Header("Camera Follow")]
    public Vector3 followOffset = new Vector3(-10f, 3f, 0f); // behind and above
    public float followDamping = 3f;        // smoothness of follow

    [Header("Timing")]
    public float sceneDuration = 10f;       // seconds total

    [Header("Audio")]
    public AudioSource engineAudio;
    public AudioClip takeoffClip;
    public float engineRampUpTime = 2f;     // seconds to reach full volume

    private bool _playing = false;
    private bool _climbing = false;


    public IEnumerator Play()
    {
        _playing = true;
        cutsceneCamera.gameObject.SetActive(true);

        // Kill hover loop
        DOTween.Kill(plane);

        // Place camera behind plane at start
        cutsceneCamera.position = plane.position + plane.TransformDirection(followOffset);
        cutsceneCamera.LookAt(plane);

        if (engineAudio != null)
        {
            engineAudio.clip = takeoffClip;
            engineAudio.volume = 0f;
            engineAudio.loop = true;
            engineAudio.Play();
            engineAudio.DOFade(1f, engineRampUpTime);
        }

        StartCoroutine(MovePlane());
        StartCoroutine(FollowCamera());

        yield return new WaitForSeconds(sceneDuration);

        _playing = false;

        if (engineAudio != null)
            engineAudio.DOFade(0f, 1f);

        cutsceneCamera.gameObject.SetActive(false);
    }

    IEnumerator MovePlane()
    {
        Vector3 startPos = plane.position;
        float elapsed = 0f;

        // Nacelles convert halfway through
        bool converted = false;

        while (elapsed < takeoffDuration)
        {
            elapsed += Time.deltaTime;
            float t = elapsed / takeoffDuration;

            // Sample curves
            float height  = liftCurve.Evaluate(t);     // Y offset from start
            float forward = forwardCurve.Evaluate(t);  // forward speed

            plane.position = new Vector3(
                startPos.x + plane.right.x * forward,
                startPos.y + height,
                startPos.z + plane.right.z * forward);

            // Convert nacelles at t=0.3
            if (!converted && t >= 0.3f)
            {
                converted = true;
                nacelleLeft.DOLocalRotate(
                    new Vector3(nacelleFlyAngle, 0f, 0f), nacelleConvertDuration)
                    .SetEase(Ease.InOutSine);
                nacelleRight.DOLocalRotate(
                    new Vector3(nacelleFlyAngle, 0f, 0f), nacelleConvertDuration)
                    .SetEase(Ease.InOutSine);
                plane.DOLocalRotate(
                    new Vector3(-climbAngle, plane.localEulerAngles.y, 0f),
                    nacelleConvertDuration).SetEase(Ease.InOutSine);
            }

            yield return null;
        }

        // Full speed after takeoff done
        _climbing = true;
        while (_playing)
        {
            plane.position += plane.right * climbSpeed * Time.deltaTime;
            yield return null;
        }
    }

    IEnumerator FollowCamera()
    {
        while (_playing)
        {
            // Smooth follow behind plane
            Vector3 targetPos = plane.position + plane.TransformDirection(followOffset);

            cutsceneCamera.position = Vector3.Lerp(
                cutsceneCamera.position,
                targetPos,
                followDamping * Time.deltaTime);

            // Always look at plane
            Quaternion targetRot = Quaternion.LookRotation(
                plane.position - cutsceneCamera.position);

            cutsceneCamera.rotation = Quaternion.Slerp(
                cutsceneCamera.rotation,
                targetRot,
                followDamping * Time.deltaTime);

            yield return null;
        }
    }
}