using UnityEngine;
using DG.Tweening;
using UnityEngine.UI;

public class RappelCutscene : MonoBehaviour
{
    [Header("References")]
    public Transform cutsceneCamera;
    public Transform fpsArmsRoot;
    public Animator armsAnimator;
    public Transform ropeAnchor;
    public LineRenderer ropeRenderer;
    public GameObject playerObject;
    public Camera playerCamera;      // drag main camera here
    public CanvasGroup fadePanel;    // black image with CanvasGroup (alpha 1 = opaque)

    [Header("Rappel Settings")]
    public float descentSpeed = 2.5f;       // meters per second
    public float descentDistance = 15f;     // meters to descend
    public float swayAmount = 0.15f;        // meters of sway
    public float swaySpeed = 1.2f;          // sway cycles per second

    [Header("Landing")]
    public Transform landingTarget;
    public float landingBlendDuration = 0.8f; // seconds to blend to player

    [Header("Camera Shake on Land")]
    public float shakeStrength = 0.3f;
    public float shakeDuration = 0.4f;      // seconds
    public int shakeVibrato = 20;

    [Header("Fade")]
    public float fadeDuration = 1f;         // seconds for fade-in from black

    private static readonly int GrabHash = Animator.StringToHash("Grab");

    void Start()
    {
        Invoke(nameof(StartRappel), 0.1f);
    }

    public void StartRappel()
    {
        StartCoroutine(RappelSequence());
    }

    System.Collections.IEnumerator RappelSequence()
    {
        // Null checks
        if (cutsceneCamera == null) { Debug.LogError("NO CUTSCENE CAMERA"); yield break; }
        if (ropeAnchor == null)     { Debug.LogError("NO ROPE ANCHOR"); yield break; }
        if (landingTarget == null)  { Debug.LogError("NO LANDING TARGET"); yield break; }
        if (playerObject == null)   { Debug.LogError("NO PLAYER OBJECT"); yield break; }
        if (playerCamera == null)   { Debug.LogError("NO PLAYER CAMERA"); yield break; }

        // ── 1. SETUP ──────────────────────────────────────────
        Debug.Log("SETUP");
        playerObject.SetActive(false);
        cutsceneCamera.gameObject.SetActive(true);
        playerCamera.gameObject.SetActive(false);

        cutsceneCamera.position = ropeAnchor.position;
        cutsceneCamera.rotation = Quaternion.Euler(10f, ropeAnchor.eulerAngles.y, 0f);

        // ── FADE IN FROM BLACK ────────────────────────────────
        Debug.Log("FADE IN");
        if (fadePanel != null)
        {
            fadePanel.alpha = 1f;  // start opaque
            yield return fadePanel
                .DOFade(0f, fadeDuration)  // fade to transparent
                .SetEase(Ease.InOutQuad)
                .WaitForCompletion();
        }

        // ── 2. GRAB ───────────────────────────────────────────
        Debug.Log("GRAB");
        if (armsAnimator != null)
            armsAnimator.SetTrigger(GrabHash);

        cutsceneCamera
            .DOLocalMoveY(cutsceneCamera.localPosition.y + 0.15f, 0.25f)
            .SetEase(Ease.OutQuad)
            .OnComplete(() =>
                cutsceneCamera.DOLocalMoveY(
                    cutsceneCamera.localPosition.y - 0.15f, 0.2f)
                    .SetEase(Ease.InQuad));

        yield return new WaitForSeconds(0.6f);

        // ── 3. DESCENT ────────────────────────────────────────
        Debug.Log("DESCENT");
        float duration = descentDistance / descentSpeed;
        Vector3 endPos = ropeAnchor.position + Vector3.down * descentDistance;

        Tween descentTween = cutsceneCamera
            .DOMove(endPos, duration)
            .SetEase(Ease.InOutSine);

        Tween swayTween = cutsceneCamera
            .DOLocalMoveX(swayAmount, 1f / swaySpeed)
            .SetEase(Ease.InOutSine)
            .SetLoops(-1, LoopType.Yoyo);

        Tween tiltTween = cutsceneCamera
            .DOLocalRotate(
                new Vector3(8f, cutsceneCamera.eulerAngles.y, 3f),
                1f / swaySpeed)
            .SetEase(Ease.InOutSine)
            .SetLoops(-1, LoopType.Yoyo);

        yield return descentTween.WaitForCompletion();

        swayTween.Kill();
        tiltTween.Kill();

        // ── 4. LAND ───────────────────────────────────────────
        Debug.Log("LAND");
        cutsceneCamera
            .DOShakePosition(shakeDuration, shakeStrength, shakeVibrato)
            .SetEase(Ease.OutQuad);

        cutsceneCamera
            .DOLocalRotate(
                new Vector3(0f, landingTarget.eulerAngles.y, 0f), 0.3f)
            .SetEase(Ease.OutBack);

        yield return new WaitForSeconds(shakeDuration);

        // ── 5. BLEND TO PLAYER ────────────────────────────────
        Debug.Log("BLEND");
        Vector3 eyePos = landingTarget.position + Vector3.up * 1.7f;

        yield return cutsceneCamera
            .DOMove(eyePos, landingBlendDuration)
            .SetEase(Ease.InOutCubic)
            .WaitForCompletion();

        cutsceneCamera
            .DORotateQuaternion(landingTarget.rotation, landingBlendDuration)
            .SetEase(Ease.InOutCubic);

        // ── 6. HAND OFF ───────────────────────────────────────
        Debug.Log("HANDOFF");
        playerObject.transform.position = landingTarget.position;
        playerObject.transform.rotation = landingTarget.rotation;
        playerObject.SetActive(true);

        playerCamera.gameObject.SetActive(true);
        cutsceneCamera.gameObject.SetActive(false);
    }
}