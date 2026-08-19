using UnityEngine;
using DG.Tweening;
using System.Collections;

/// The missile's run to the target structure. Flies a lofted arc from the
/// release point to the construction and chases it from behind, so the target
/// grows in frame for the whole shot. Ends the instant before impact — the
/// detonation is its own cut.
public class MissileFlightCutscene : MonoBehaviour
{
    [Header("Camera")]
    public Transform cutsceneCamera;

    [Header("Flight")]
    public Transform missile;
    public Transform target;            // the construction / facility being hit
    public float flightDuration = 7f;   // seconds from release to impact
    public float arcHeight = 300f;      // meters the trajectory lofts above the straight line
    public float rollSpeed = 40f;       // degrees per second of body roll

    [Header("Chase Camera")]
    public Vector3 chaseOffset = new Vector3(0f, 6f, -22f); // meters behind and above
    public float chaseDamping = 4f;     // smoothness of the follow
    public float aimLead = 0.15f;       // 0-1, how far past the missile it aims

    [Header("Terminal Dive")]
    // Over the last stretch the camera falls back and swings wide so the target
    // fills frame rather than being hidden behind the airframe.
    [Range(0f, 1f)] public float terminalStart = 0.72f;
    public Vector3 terminalOffset = new Vector3(28f, 18f, -46f); // meters, at impact
    public float terminalShake = 0.6f;  // degrees of buffet at full terminal blend

    [Header("Cut Point")]
    public float cutBeforeImpact = 0.15f; // seconds trimmed off the end of the run

    [Header("Effects")]
    public ParticleSystem exhaustTrail; // optional motor plume / contrail

    [Header("Audio")]
    public AudioSource motorAudio;      // sustained rocket motor
    public float motorRampUpTime = 0.8f;// seconds to reach full volume

    private bool _playing = false;

    // 0-1 along the run. Driven by the flight timer rather than by distance to
    // the target: the lofted arc means distance does not fall monotonically.
    private float _progress = 0f;

    /// Where the missile actually ended up. The detonation shot reads this so
    /// the fireball lands on the impact point and not on a stale authored value.
    public Vector3 ImpactPoint { get; private set; }

    public IEnumerator Play()
    {
        _playing = true;
        cutsceneCamera.gameObject.SetActive(true);

        if (missile != null) missile.gameObject.SetActive(true);
        ImpactPoint = target != null ? target.position : Vector3.zero;

        if (exhaustTrail != null) exhaustTrail.Play();

        if (motorAudio != null)
        {
            motorAudio.loop = true;
            motorAudio.volume = 0f;
            motorAudio.Play();
            motorAudio.DOFade(1f, motorRampUpTime);
        }

        StartCoroutine(ChaseCamera());

        yield return StartCoroutine(FlyToTarget());

        _playing = false;

        if (motorAudio != null) motorAudio.Stop();
        if (exhaustTrail != null) exhaustTrail.Stop();
        if (missile != null) missile.gameObject.SetActive(false);

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
        if (missile != null) DOTween.Kill(missile);
        if (motorAudio != null)
        {
            DOTween.Kill(motorAudio);
            motorAudio.Stop();
        }
        if (exhaustTrail != null) exhaustTrail.Stop();
    }

    IEnumerator FlyToTarget()
    {
        if (missile == null || target == null) yield break;

        Vector3 start = missile.position;
        Vector3 end = target.position;
        float roll = 0f;
        _progress = 0f;

        // The run is cut fractionally short so the last frame of this shot still
        // has the missile in the air — the explosion sells the impact, not a
        // model intersecting geometry.
        float duration = Mathf.Max(0.01f, flightDuration - cutBeforeImpact);

        float elapsed = 0f;
        Vector3 previous = start;

        while (elapsed < duration && _playing)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / flightDuration);
            _progress = t;

            Vector3 pos = Vector3.Lerp(start, end, t);
            // Parabola peaking at mid-flight — 4t(1-t) is 0 at both ends, 1 at t=0.5.
            pos.y += arcHeight * (4f * t * (1f - t));

            missile.position = pos;

            // Nose follows the actual path, so the arc reads as a lofted throw
            // rather than a flat glide with a tilted model.
            Vector3 heading = pos - previous;
            if (heading.sqrMagnitude > 0.0001f)
            {
                roll += rollSpeed * Time.deltaTime;
                missile.rotation = Quaternion.LookRotation(heading) * Quaternion.Euler(0f, 0f, roll);
            }
            previous = pos;

            yield return null;
        }

        ImpactPoint = missile.position;
    }

    IEnumerator ChaseCamera()
    {
        // Snap to the opening framing so the cut lands already on the missile.
        if (missile != null)
        {
            cutsceneCamera.position = missile.position + missile.TransformDirection(chaseOffset);
            cutsceneCamera.LookAt(missile);
        }

        // Shake is composed into the same rotation write as the aim below rather
        // than run as a DOShakeRotation tween: the follow writes rotation every
        // frame, so a concurrent tween on the same transform just gets stomped.
        float shakePhase = 0f;

        while (_playing && missile != null)
        {
            float t = _progress;

            // Blend from the tight chase out to the wide terminal framing.
            float terminalT = terminalStart >= 1f
                ? 0f
                : Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(terminalStart, 1f, t));

            Vector3 offset = Vector3.Lerp(chaseOffset, terminalOffset, terminalT);
            Vector3 targetPos = missile.position + missile.TransformDirection(offset);

            cutsceneCamera.position = Vector3.Lerp(
                cutsceneCamera.position, targetPos, chaseDamping * Time.deltaTime);

            // Aim past the missile so the target creeps into frame ahead of it.
            Vector3 aim = missile.position + missile.forward * (aimLead * 100f);
            Quaternion targetRot = Quaternion.LookRotation(aim - cutsceneCamera.position);
            Quaternion aimed = Quaternion.Slerp(
                cutsceneCamera.rotation, targetRot, chaseDamping * Time.deltaTime);

            // Buffet builds only over the terminal stretch.
            shakePhase += Time.deltaTime * 28f;
            float amount = terminalShake * terminalT;
            Quaternion buffet = Quaternion.Euler(
                Mathf.Sin(shakePhase * 1.7f) * amount,
                Mathf.Sin(shakePhase * 1.1f) * amount,
                Mathf.Sin(shakePhase * 2.3f) * amount);

            cutsceneCamera.rotation = aimed * buffet;

            yield return null;
        }
    }
}
