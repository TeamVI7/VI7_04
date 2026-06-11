using UnityEngine;
using DG.Tweening;

public class MV38Condor : MonoBehaviour
{
    [Header("Engines")]
    public Transform nacelleLeft;
    public Transform nacelleRight;

    [Header("Hatch")]
    public Transform hatch;
    public float hatchCloseDuration = 1.2f; // seconds to close

    [Header("Hover Wobble")]
    public float hoverBobMeters = 0.05f;  // meters up and down
    public float hoverBobSeconds = 3f;    // seconds per cycle

    [Header("Fly Away")]
    public float takeoffAfterSeconds = 8f;  // seconds until heli leaves
    public float flyDelay = 0.8f;           // seconds before nacelles tilt
    public float conversionDuration = 1.8f; // seconds nacelles take to tilt
    public float flyDuration = 5f;          // seconds to fly off into distance
    public float flyTilt = 20f;             // degrees body pitches forward

    [Header("Audio")]
    public AudioSource engineAudio;   // idle audiosource
    public AudioSource takeoffAudio;  // takeoff audiosource
    public AudioClip idleClip;        // mv38_idle.wav
    public AudioClip takeoffClip;     // mv38_takeoff.wav

    private bool _flyingAway = false;

    void Awake()
    {
        // Nothing here — positions captured in Start with localPosition
    }

    void Start()
    {
        // Nacelles start pointing down in hover
        if (nacelleLeft)  nacelleLeft.localRotation = Quaternion.Euler(90f, 0f, 0f);
        if (nacelleRight) nacelleRight.localRotation = Quaternion.Euler(90f, 0f, 0f);

        StartHoverLoop();

        // Play idle loop
        engineAudio.clip = idleClip;
        engineAudio.loop = true;
        engineAudio.Play();

        // Auto takeoff after X seconds
        DOVirtual.DelayedCall(takeoffAfterSeconds, FlyAway);

        // Log total flyaway time
        float totalTime = flyDelay + conversionDuration + flyDuration;
        Debug.Log($"Heli flies away in {totalTime} seconds total");
    }

    void StartHoverLoop()
    {
        float currentY = transform.localPosition.y;

        transform.DOLocalMoveY(currentY + hoverBobMeters, hoverBobSeconds)
            .SetEase(Ease.InOutSine)
            .SetLoops(-1, LoopType.Yoyo);
    }

    public void FlyAway()
    {
        if (_flyingAway) return;
        _flyingAway = true;

        Debug.Log("FlyAway called");

        // Kill hover and nacelle tweens
        DOTween.Kill(transform);
        DOTween.Kill(nacelleLeft);
        DOTween.Kill(nacelleRight);

        // Crossfade idle out, takeoff in
        engineAudio.DOFade(0f, 2f);

        takeoffAudio.clip = takeoffClip;
        takeoffAudio.loop = true;
        takeoffAudio.volume = 0f;
        takeoffAudio.Play();
        takeoffAudio.DOFade(1f, 2f);

        // Fade takeoff out after heli gone
        float heliGoneTime = flyDelay + conversionDuration + flyDuration;
        DOVirtual.DelayedCall(heliGoneTime, () =>
        {
            takeoffAudio.DOFade(0f, 7f).OnComplete(() =>
            {
                Destroy(takeoffAudio.gameObject);
            });
        });

        Sequence s = DOTween.Sequence();

        // Pause before converting
        s.AppendInterval(flyDelay);
        // Close hatch
        s.Append(hatch.DOLocalRotate(
        new Vector3(0f, 0f, 0f), hatchCloseDuration)
        .SetEase(Ease.InOutSine));
        // Nacelles tilt forward
        s.Append(nacelleLeft.DOLocalRotate(
            new Vector3(0f, 0f, 0f), conversionDuration)
            .SetEase(Ease.InOutSine));
        s.Join(nacelleRight.DOLocalRotate(
            new Vector3(0f, 0f, 0f), conversionDuration)
            .SetEase(Ease.InOutSine));
        // Body pitches forward
        s.Join(transform.DOLocalRotate(
            new Vector3(-flyTilt, transform.eulerAngles.y, 0f),
            conversionDuration).SetEase(Ease.InOutQuad));

        // Climb slightly
        s.Join(transform.DOLocalMoveY(
            transform.localPosition.y + 3f, conversionDuration)
            .SetEase(Ease.OutQuad));

        // Blast off using heli's right axis
        s.Join(transform.DOMove(
            transform.position + transform.right * 200f + Vector3.up * 20f,
            flyDuration).SetEase(Ease.InCubic));

        s.OnComplete(() =>
        {
            // Detach audio so it survives destroy
            takeoffAudio.transform.SetParent(null);
            DontDestroyOnLoad(takeoffAudio.gameObject);

            Destroy(gameObject);
        });
    }
}