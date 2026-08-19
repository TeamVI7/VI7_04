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
    [Range(0.2f, 1f)]
    public float rotationLag = 0.6f;        // <1 lets the aim trail the move, like an operator
    public float aimLead = 12f;             // meters ahead of the nose to aim at

    [Header("Feel")]
    public CameraHandheld handheld;         // optional drift layer
    public float fovClimbBoost = 6f;        // degrees the lens opens up under climb
    public float cruiseBlendDuration = 1.2f;// seconds to reconcile curve speed with climbSpeed

    [Header("Timing")]
    public float sceneDuration = 10f;       // seconds total

    [Header("Audio")]
    public AudioSource engineAudio;
    public AudioClip takeoffClip;
    public float engineRampUpTime = 2f;     // seconds to reach full volume

    private bool _playing = false;
    private Camera _cam;
    private float _baseFov;

    void OnValidate()
    {
        // The camera cuts out at sceneDuration regardless, so a longer takeoff
        // would simply be truncated mid-climb.
        if (takeoffDuration > sceneDuration) takeoffDuration = sceneDuration;
    }

    public IEnumerator Play()
    {
        _playing = true;
        cutsceneCamera.gameObject.SetActive(true);

        if (_cam == null) _cam = cutsceneCamera.GetComponentInChildren<Camera>(true);
        if (_cam != null && _baseFov <= 0f) _baseFov = _cam.fieldOfView;

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

        if (_cam != null && _baseFov > 0f) _cam.fieldOfView = _baseFov;
        cutsceneCamera.gameObject.SetActive(false);
    }

    /// Halts playback and releases every tween this cutscene owns. Used by the
    /// skip path, which must not fall back on DOTween.KillAll — that would also
    /// kill cleanup tweens belonging to persistent objects in other scenes.
    public void Stop()
    {
        _playing = false;
        StopAllCoroutines();

        if (_cam != null && _baseFov > 0f) _cam.fieldOfView = _baseFov;

        if (cutsceneCamera != null) DOTween.Kill(cutsceneCamera);
        if (plane != null) DOTween.Kill(plane);
        if (nacelleLeft != null) DOTween.Kill(nacelleLeft);
        if (nacelleRight != null) DOTween.Kill(nacelleRight);
        if (engineAudio != null)
        {
            DOTween.Kill(engineAudio);
            engineAudio.Stop();
        }
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

        // ── CRUISE ────────────────────────────────────────────
        // Position is continuous across this handoff but velocity was not: the
        // curve's exit speed rarely equals climbSpeed, which showed up as a lurch
        // the moment takeoffDuration elapsed. Sample the curve's exit slope and
        // ease into climbSpeed from there.
        const float sampleWindow = 0.02f;
        float exitSpeed = (forwardCurve.Evaluate(1f) - forwardCurve.Evaluate(1f - sampleWindow))
                          / (sampleWindow * Mathf.Max(0.01f, takeoffDuration));

        float blend = 0f;
        while (_playing)
        {
            blend = Mathf.Min(1f, blend + Time.deltaTime / Mathf.Max(0.01f, cruiseBlendDuration));
            float speed = Mathf.Lerp(exitSpeed, climbSpeed, Mathf.SmoothStep(0f, 1f, blend));

            plane.position += plane.right * speed * Time.deltaTime;

            // Lens opens up as the aircraft accelerates, then settles once cruising
            if (_cam != null && _baseFov > 0f)
                _cam.fieldOfView = _baseFov + fovClimbBoost * Mathf.Sin(blend * Mathf.PI);

            yield return null;
        }
    }

    IEnumerator FollowCamera()
    {
        // Smoothing runs against private state rather than the transform, so the
        // handheld offset written below can never feed back into the follow and
        // compound frame over frame.
        Vector3 camPos = cutsceneCamera.position;
        Quaternion camRot = cutsceneCamera.rotation;

        while (_playing)
        {
            Vector3 targetPos = plane.position + plane.TransformDirection(followOffset);

            // Framerate-independent smoothing. Lerp(a, b, k * dt) compounds
            // differently at different framerates, so the shot was genuinely
            // framed tighter on a fast machine than a slow one — and broke
            // outright if a hitch pushed k * dt past 1.
            float posT = 1f - Mathf.Exp(-followDamping * Time.deltaTime);
            float rotT = 1f - Mathf.Exp(-followDamping * rotationLag * Time.deltaTime);

            camPos = Vector3.Lerp(camPos, targetPos, posT);

            // Aim ahead of the nose so the aircraft sits off-centre and leads the
            // frame, instead of being pinned dead centre like a lock-on.
            Vector3 aim = plane.position + plane.right * aimLead;
            camRot = Quaternion.Slerp(camRot, Quaternion.LookRotation(aim - camPos), rotT);

            Vector3 pos = camPos;
            Quaternion rot = camRot;
            if (handheld != null)
            {
                handheld.Sample(Time.time);
                pos += rot * handheld.PositionOffset;
                rot *= handheld.RotationOffset;
            }

            cutsceneCamera.SetPositionAndRotation(pos, rot);

            yield return null;
        }
    }
}