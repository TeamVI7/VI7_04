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
    public float arcHeight = 300f;      // meters of CRUISE altitude above the straight line
    public float rollSpeed = 6f;        // degrees per second of body roll

    [Header("Flight Profile")]
    // Powered flight, not a lob. A symmetric parabola coasts over the top like
    // an artillery shell, which fights the motor burning and the trail emitting
    // for the whole run — and leaves the terminal camera swing acting out a dive
    // the missile never performs.
    [Range(0.05f, 0.9f)] public float climbFraction = 0.25f; // portion of the run spent climbing to cruise
    [Range(0.05f, 0.9f)] public float diveFraction = 0.2f;   // portion spent in the terminal dive

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

    [Header("Editor")]
    public bool drawGizmos = true;

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
        if (missile == null || target == null)
            Debug.LogWarning("MissileFlightCutscene: missile or target unassigned — the run " +
                             "will not happen and the detonation gets world origin as its " +
                             "impact point.", this);

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

            Vector3 pos = PathPoint(start, end, t);

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

    // ───────────────────────────────────────────────────────────────
    // TRAJECTORY
    // ───────────────────────────────────────────────────────────────

    /// Altitude profile as a 0-1 multiplier on <see cref="arcHeight"/>: climb off
    /// the release, hold cruise, then bunt over into a dive that steepens into
    /// the target. Asymmetric on purpose — the whole point is that the descent
    /// does not mirror the climb.
    public float ProfileHeight(float t)
    {
        float climb = climbFraction;
        float dive = diveFraction;

        // A climb and a dive that overlap would fight over the same stretch of
        // the run. Both are scaled down rather than one being clipped, so the
        // shape stays recognisable at any pair of values.
        float total = climb + dive;
        if (total > 0.98f)
        {
            float scale = 0.98f / total;
            climb *= scale;
            dive *= scale;
        }

        // Eased at both ends: the missile rotates into the climb instead of
        // snapping to it, and levels off instead of cornering at the top.
        if (t <= climb) return Mathf.SmoothStep(0f, 1f, t / climb);

        float diveStart = 1f - dive;
        if (t < diveStart) return 1f;

        // Flat where it leaves cruise, accelerating down from there. The nose
        // follows the path, so the dive angle steepens on its own as the shot
        // runs out — no separate pitch authoring needed.
        float u = (t - diveStart) / dive;
        return 1f - u * u;
    }

    /// World point on the run at normalised time t. The runtime flight and the
    /// scene-view gizmo both go through here, so the drawn path is the flown one.
    public Vector3 PathPoint(Vector3 start, Vector3 end, float t)
    {
        Vector3 p = Vector3.Lerp(start, end, t);
        p.y += arcHeight * ProfileHeight(t);
        return p;
    }

    // ───────────────────────────────────────────────────────────────
    // EDITOR
    // ───────────────────────────────────────────────────────────────

    private void OnDrawGizmosSelected()
    {
        if (!drawGizmos || missile == null || target == null) return;

        Color c = CutsceneGizmos.Shot4;

        Vector3 start = missile.position;
        Vector3 end = target.position;

        // The run really does start from wherever shot 3's drop left the missile,
        // so this path is only truthful if the missile is sitting at its release
        // position in the editor. Worth knowing while tuning arcHeight.
        // Sampled through PathPoint, so what is drawn is what gets flown.
        const int samples = 64;
        var path = new Vector3[samples + 1];
        for (int i = 0; i <= samples; i++)
            path[i] = PathPoint(start, end, i / (float)samples);
        CutsceneGizmos.Path(path, c, 0f);

        CutsceneGizmos.Marker(start, 3f, c, "release");
        CutsceneGizmos.Marker(end, 6f, CutsceneGizmos.Shot5, "target");
        CutsceneGizmos.Ring(end, 40f, CutsceneGizmos.Shot5 * 0.7f);

        // Straight-line reference against the lofted path — arcHeight only reads
        // as a loft relative to the distance it spans.
        Gizmos.color = c * 0.3f;
        Gizmos.DrawLine(start, end);

        float range = Vector3.Distance(start, end);
        CutsceneGizmos.Label(Vector3.Lerp(start, end, 0.5f) + Vector3.down * 10f,
                             "range " + CutsceneGizmos.Metres(range) +
                             "   cruise +" + CutsceneGizmos.Metres(arcHeight) +
                             "   " + flightDuration.ToString("0.#") + "s" +
                             "   (" + (range / Mathf.Max(0.01f, flightDuration)).ToString("0") + " m/s)",
                             c);

        // The two corners of the profile. arcHeight is added on top of the
        // interpolated line rather than being an absolute altitude, so cruise
        // altitude is not the same number as cruise Y.
        Vector3 topOfClimb = PathPoint(start, end, climbFraction);
        Vector3 diveEntry = PathPoint(start, end, 1f - diveFraction);

        CutsceneGizmos.Marker(topOfClimb, 4f, c * 0.8f,
                              "top of climb  " + (climbFraction * flightDuration).ToString("0.#") +
                              "s   y=" + topOfClimb.y.ToString("0"));
        CutsceneGizmos.Marker(diveEntry, 4f, c * 0.8f,
                              "dive entry  " + (diveFraction * flightDuration).ToString("0.#") + "s to impact");

        // The camera's terminal swing should start around the dive, not before
        // it — otherwise the framing commits to a dive the missile has not begun.
        float diveStart = 1f - diveFraction;
        if (Mathf.Abs(terminalStart - diveStart) > 0.1f)
            CutsceneGizmos.Label(PathPoint(start, end, terminalStart) + Vector3.up * 25f,
                                 "camera swings at " + terminalStart.ToString("0.00") +
                                 ", dive starts " + diveStart.ToString("0.00"),
                                 Color.red);

        // ── CHASE CAMERA ──────────────────────────────────────
        // Sampled poses along the run: the offset is applied in the missile's
        // frame, so on a steep arc the camera ends up somewhere quite unlike
        // "behind and above" read off the inspector numbers.
        float[] at = { 0.15f, 0.5f, 0.92f };
        foreach (float t in at)
        {
            Vector3 p = PathPoint(start, end, t);
            Vector3 ahead = PathPoint(start, end, Mathf.Min(1f, t + 0.02f));
            Vector3 heading = ahead - p;
            if (heading.sqrMagnitude < 0.0001f) continue;

            Quaternion rot = Quaternion.LookRotation(heading);

            // Same blend ChaseCamera uses, so the drawn poses are the framing
            // that actually plays.
            float terminalT = terminalStart >= 1f
                ? 0f
                : Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(terminalStart, 1f, t));
            Vector3 offset = Vector3.Lerp(chaseOffset, terminalOffset, terminalT);
            Vector3 camPos = p + rot * offset;

            CutsceneGizmos.CameraPose(camPos, Quaternion.LookRotation(p - camPos), c * 0.85f,
                                      "chase " + (t * 100f).ToString("0") + "%", 4f);
            Gizmos.color = c * 0.35f;
            Gizmos.DrawLine(camPos, p);
        }

        // Where the shot actually ends — short of the target on purpose, so the
        // last frame still has the missile in the air.
        float cutT = Mathf.Clamp01((flightDuration - cutBeforeImpact) / Mathf.Max(0.01f, flightDuration));
        Vector3 cut = PathPoint(start, end, cutT);
        CutsceneGizmos.Marker(cut, 4f, CutsceneGizmos.Handoff,
                              "cut  >  ImpactPoint handed to shot 5");
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
