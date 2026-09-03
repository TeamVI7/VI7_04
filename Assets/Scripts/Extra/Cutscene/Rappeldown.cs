using System.Collections.Generic;
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

    [Header("Player HUD")]
    [Tooltip("Root of the player HUD (health/stamina, crosshair, weapon, ability). It sits " +
             "on the DontDestroyOnLoad root NEXT TO the player, not under it, so deactivating " +
             "playerObject leaves it drawing over the cutscene. Left empty, the HUD canvases " +
             "are found from the HUD scripts instead.")]
    public GameObject playerHUDRoot;
    [Header("Dialogue")]
    public DialogueData dialogueOnLand;
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
        SetPlayerHUDVisible(false);

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
        SetPlayerHUDVisible(true);

        if (dialogueOnLand != null)
            DialogueManager.Instance.Play(dialogueOnLand);
    }

    /// <summary>
    /// The HUD outlives this cutscene — if the scene is torn down mid-sequence (skip, quit
    /// to menu, restart) the hidden canvases would stay hidden for the rest of the session.
    /// </summary>
    void OnDestroy() => SetPlayerHUDVisible(true);

    private List<Canvas> _hiddenHUD;

    private void SetPlayerHUDVisible(bool visible)
    {
        if (playerHUDRoot != null)
        {
            playerHUDRoot.SetActive(visible);
            return;
        }

        if (!visible)
        {
            // No single root wired: hide the canvases owning the player HUD scripts.
            // Canvas.enabled rather than SetActive — it stops the drawing without
            // interrupting the scripts, which keep tracking health/ammo through the shot.
            _hiddenHUD = new List<Canvas>();
            CollectHUDCanvas<HealthStaminaHUD>(_hiddenHUD);
            CollectHUDCanvas<CrosshairController>(_hiddenHUD);
            CollectHUDCanvas<WeaponHUD>(_hiddenHUD);
            CollectHUDCanvas<AbilityHUDController>(_hiddenHUD);
        }

        if (_hiddenHUD == null) return;

        foreach (Canvas c in _hiddenHUD)
            if (c != null) c.enabled = visible;
    }

    private static void CollectHUDCanvas<T>(List<Canvas> into) where T : MonoBehaviour
    {
        T component = FindObjectOfType<T>(true);
        if (component == null) return;

        Canvas canvas = component.GetComponentInParent<Canvas>(true);
        if (canvas == null) return;

        // Several HUD scripts can share one canvas, and nested canvases would leave the
        // parent still drawing — always toggle the top one, once.
        canvas = canvas.rootCanvas;
        if (!into.Contains(canvas)) into.Add(canvas);
    }

#if UNITY_EDITOR
    [Header("Gizmos")]
    public bool drawGizmos = true;

    private void OnDrawGizmos()
    {
        if (!drawGizmos) return;

        // Each section guards its own refs rather than bailing on the first null,
        // so the gizmo stays useful while the shot is still being wired up.
        Vector3 descentEnd = Vector3.zero;
        bool haveDescent = false;

        // ── ROPE ANCHOR AND DESCENT PATH ──────────────────────
        if (ropeAnchor != null)
        {
            descentEnd = ropeAnchor.position + Vector3.down * descentDistance;
            haveDescent = true;

            Gizmos.color = new Color(1f, 0.9f, 0.2f);
            Gizmos.DrawWireSphere(ropeAnchor.position, 0.2f);

            // The descent is a world-space DOMove straight down, so the authored
            // path is a plumb line regardless of how the anchor is oriented.
            Gizmos.DrawLine(ropeAnchor.position, descentEnd);
            Gizmos.DrawWireSphere(descentEnd, 0.2f);

            float duration = descentSpeed > 0f ? descentDistance / descentSpeed : 0f;
            UnityEditor.Handles.Label(
                ropeAnchor.position,
                $"  Rappel anchor\n  {descentDistance:0.#}m @ {descentSpeed:0.#}m/s = {duration:0.0}s");
        }

        // ── LANDING ───────────────────────────────────────────
        if (landingTarget != null)
        {
            // Must track the hand-off in RappelSequence: eye height is hardcoded
            // there, so if that changes this gizmo lies.
            Vector3 eyePos = landingTarget.position + Vector3.up * 1.7f;

            Gizmos.color = new Color(0.2f, 1f, 0.4f);
            Gizmos.DrawWireSphere(eyePos, 0.2f);
            Gizmos.DrawLine(landingTarget.position, eyePos);
            Gizmos.DrawRay(eyePos, landingTarget.forward * 2f);

            // ── BLEND GAP ─────────────────────────────────────
            // How far the camera still has to travel after the rope run ends.
            // A long red line means descentDistance doesn't reach the landing
            // spot and the blend is hiding the shortfall.
            if (haveDescent)
            {
                float gap = Vector3.Distance(descentEnd, eyePos);
                Gizmos.color = gap > 1f ? Color.red : new Color(0.2f, 1f, 0.4f, 0.5f);
                Gizmos.DrawLine(descentEnd, eyePos);
                UnityEditor.Handles.Label(
                    Vector3.Lerp(descentEnd, eyePos, 0.5f),
                    $"  blend gap {gap:0.00}m over {landingBlendDuration:0.0}s");
            }

            // ── LANDING FACING, AS AUTHORED VS AS EXECUTED ────
            // RappelSequence lands with DOLocalRotate fed landingTarget's WORLD
            // yaw. Under a rotated parent those disagree; red is where the camera
            // actually ends up pointing, green is the landing target's facing.
            // They should sit on top of each other.
            if (cutsceneCamera != null)
            {
                Quaternion parentRot = cutsceneCamera.parent != null
                    ? cutsceneCamera.parent.rotation
                    : Quaternion.identity;
                Quaternion executed =
                    parentRot * Quaternion.Euler(0f, landingTarget.eulerAngles.y, 0f);

                float error = Quaternion.Angle(executed, landingTarget.rotation);
                if (error > 1f)
                {
                    Gizmos.color = Color.red;
                    Gizmos.DrawRay(eyePos, executed * Vector3.forward * 2f);
                    UnityEditor.Handles.Label(
                        eyePos + executed * Vector3.forward * 2f,
                        $"  landing facing off by {error:0.#}°");
                }
            }
        }

        // ── SWAY AXIS ─────────────────────────────────────────
        // DOLocalMoveX sways along the PARENT's right axis while the descent
        // drives world position, so under a rotated parent the sway runs off at
        // an angle to the plumb line instead of across it.
        if (cutsceneCamera != null && haveDescent && swayAmount > 0f)
        {
            Vector3 swayDir = cutsceneCamera.parent != null
                ? cutsceneCamera.parent.right
                : Vector3.right;
            Vector3 mid = Vector3.Lerp(ropeAnchor.position, descentEnd, 0.5f);

            Gizmos.color = new Color(0.4f, 0.7f, 1f);
            Gizmos.DrawLine(mid - swayDir * swayAmount, mid + swayDir * swayAmount);

            float swayHalfCycle = swaySpeed > 0f ? 1f / swaySpeed : 0f;
            UnityEditor.Handles.Label(
                mid + swayDir * swayAmount,
                $"  sway ±{swayAmount:0.##}m, {swayHalfCycle:0.0}s per half-cycle");
        }
    }
#endif
}