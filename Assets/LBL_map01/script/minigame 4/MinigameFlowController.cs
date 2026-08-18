using System.Collections;
using UnityEngine;
using TMPro;
using DG.Tweening;

public class MinigameFlowController : MonoBehaviour
{
    [Header("Shared World Space Screen")]
    public Canvas minigameCanvas;
    public Camera minigameCamera;

    [Header("Support Scripts")]
    public UIInputBlocker inputBlocker;
    public ForceRaycastTarget raycastFixer;
    public FixCanvasCameraSync canvasCameraSync;
    public ServerMinigameManager serverMinigameManager;

    [Header("Minigame 1 - Arrow Sequence")]
    public GameObject arrowPanel;
    public ArrowSequenceMinigame arrowMinigame;

    [Header("Minigame 2 - Code Input")]
    public GameObject codePanel;
    public CodeInputMinigame codeMinigame;
    public CodeClueDistributor codeClueDistributor;
    [Tooltip("Fallback code used only if codeClueDistributor is not assigned or hasn't generated a code yet.")]
    public string presetCode = "1234";

    [Header("Minigame 3 - Voltage Calibration")]
    public GameObject voltagePanel;
    public VoltageCalibrationMinigame voltageMinigame;

    [Header("AI Shutdown Notice (dùng chung cho cả bước Tắt Server)")]
    [Tooltip("Panel này được TÁI SỬ DỤNG cho 2 việc: (1) hiển thị 'TẮT SERVER X' / 'SAI THỨ TỰ' trong lúc chơi minigame tắt server, (2) hiển thị thông báo 'AI CORE OFFLINE' ở cuối chuỗi minigame.")]
    public GameObject aiShutdownPanel;
    public TMP_Text aiShutdownText;
    public string aiShutdownMessage = "AI CORE OFFLINE";
    public float aiShutdownDisplayDuration = 3f;

    [Header("Cutscene Cameras")]
    public Camera playerCamera;
    public Camera serverRiseCamera;
    public Camera ceilingMechCamera;

    [Header("Boss Spawn")]
    public GameObject bossPrefab;
    public Transform bossSpawnPoint;
    [Tooltip("How long to hold on ceilingMechCamera watching the ceiling explosion before it repositions (if set) and the boss spawns. Also used as a fallback hold if the spawned boss has no MechIntroCutscene.")]
    public float ceilingCameraHoldDuration = 2.5f;
    [Tooltip("Optional. Once the explosion beat finishes, ceilingMechCamera moves here before the boss spawns and its drop-in cutscene takes over — e.g. swinging from 'watching the ceiling' to 'watching the drop zone'. Leave empty to spawn the boss directly at the explosion framing.")]
    public Transform mechRevealCamPose;
    public float camRepositionDuration = 0.8f;
    public Ease camRepositionEase = Ease.InOutSine;

    [Header("Lights To Turn On")]
    public Light[] lightsToTurnOn;

    private bool _isRunning = false;
    private bool _isPaused = false;
    private bool _inCutscene = false;

    private void Start()
    {
        SetCam(playerCamera, true);
        SetCam(minigameCamera, false);
        SetCam(serverRiseCamera, false);
        SetCam(ceilingMechCamera, false);
    }

    public void StartTerminal()
    {
        if (_inCutscene) return;

        if (!_isRunning)
        {
            BeginFreshRun();
            return;
        }

        if (!_isPaused)
        {
            PauseTerminal();
        }
        else
        {
            ResumeTerminal();
        }
    }

    public void ForceExitZone()
    {
        if (_inCutscene) return;
        if (_isRunning && !_isPaused)
            PauseTerminal();
    }

    private void BeginFreshRun()
    {
        _isRunning = true;
        _isPaused = false;

        if (minigameCanvas) minigameCanvas.gameObject.SetActive(true);

        SetCam(playerCamera, false);
        SetCam(minigameCamera, true);

        if (raycastFixer) raycastFixer.FixAll();
        if (inputBlocker) inputBlocker.BlockInput();

        ShowOnly(arrowPanel);
        arrowMinigame.StartMinigame(OnArrowComplete);
    }

    private void PauseTerminal()
    {
        _isPaused = true;

        if (inputBlocker) inputBlocker.UnblockInput();
        SetCam(minigameCamera, false);
        SetCam(playerCamera, true); // let the player see/move while paused

        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;

        PauseAllMinigames();
    }

    private void ResumeTerminal()
    {
        _isPaused = false;

        SetCam(playerCamera, false);
        SetCam(minigameCamera, true);
        if (raycastFixer) raycastFixer.FixAll();
        if (inputBlocker) inputBlocker.BlockInput();

        ResumeAllMinigames();
    }

    private void PauseAllMinigames()
    {
        if (arrowMinigame) arrowMinigame.Pause();
        if (codeMinigame) codeMinigame.Pause();
        if (voltageMinigame) voltageMinigame.Pause();
    }

    private void ResumeAllMinigames()
    {
        if (arrowMinigame) arrowMinigame.Resume();
        if (codeMinigame) codeMinigame.Resume();
        if (voltageMinigame) voltageMinigame.Resume();
    }

    private void OnArrowComplete()
    {
        ShowOnly(codePanel);

        string codeToUse = presetCode;
        if (codeClueDistributor != null && !string.IsNullOrEmpty(codeClueDistributor.GeneratedCode))
            codeToUse = codeClueDistributor.GeneratedCode;

        codeMinigame.StartMinigame(codeToUse, OnCodeComplete);
    }

    private void OnCodeComplete()
    {
        ShowOnly(voltagePanel);
        voltageMinigame.StartMinigame(OnVoltageComplete);
    }

    private void OnVoltageComplete()
    {
        Debug.Log("[MinigameFlowController] OnVoltageComplete");
        ShowOnly(null);

        _inCutscene = true;

        if (canvasCameraSync != null) canvasCameraSync.enabled = false;

        SetCam(minigameCamera, false);
        SetCam(playerCamera, false);
        SetCam(serverRiseCamera, true);

        StartServerShutdownPuzzle();
    }

    private void SetCam(Camera cam, bool state)
    {
        if (cam == null)
        {
            Debug.LogWarning("[MinigameFlowController] SetCam called with null camera");
            return;
        }
        cam.enabled = state;
        Debug.Log($"[MinigameFlowController] {cam.name}.enabled = {state}");
        var listener = cam.GetComponent<AudioListener>();
        if (listener != null) listener.enabled = state;
    }

    private void StartServerShutdownPuzzle()
    {
        Debug.Log("[MinigameFlowController] StartServerShutdownPuzzle - waiting on ServerMinigameManager callbacks");
        ShowOnly(aiShutdownPanel);

        serverMinigameManager.SetNoticeUI(aiShutdownPanel, aiShutdownText);
        serverMinigameManager.OnPuzzleInteractionReady = OnPuzzleInteractionReady;
        serverMinigameManager.OnCeilingExplosionTriggered = OnCeilingExplode;
        serverMinigameManager.OnAllServersShutdown = OnServersShutdownComplete;
        serverMinigameManager.OnPlayerEnterTrigger();
    }

    private void OnPuzzleInteractionReady()
    {
        Debug.Log("[MinigameFlowController] OnPuzzleInteractionReady - handing control back to player");
        SetCam(serverRiseCamera, false);
        SetCam(playerCamera, true);

        if (inputBlocker) inputBlocker.UnblockInput();
    }

    private void OnCeilingExplode()
    {
        Debug.Log("[MinigameFlowController] OnCeilingExplode");
        if (inputBlocker) inputBlocker.BlockInput();

        SetCam(playerCamera, false);
        SetCam(serverRiseCamera, false);
        SetCam(ceilingMechCamera, true);

        StartCoroutine(Co_WatchExplosionThenSpawnBoss());
    }

    // Lets the ceiling explosion play out on ceilingMechCamera for a beat first —
    // the boss doesn't spawn until this finishes, so the reveal reads as
    // "ceiling blows -> camera swings to the drop zone -> mech drops in" rather
    // than everything happening at the same static angle.
    private IEnumerator Co_WatchExplosionThenSpawnBoss()
    {
        yield return new WaitForSeconds(ceilingCameraHoldDuration);

        if (mechRevealCamPose != null && ceilingMechCamera != null)
        {
            Transform camT = ceilingMechCamera.transform;
            Sequence reposition = DOTween.Sequence()
                .Join(camT.DOMove(mechRevealCamPose.position, camRepositionDuration).SetEase(camRepositionEase))
                .Join(camT.DORotateQuaternion(mechRevealCamPose.rotation, camRepositionDuration).SetEase(camRepositionEase));

            yield return reposition.WaitForCompletion();
        }

        SpawnBoss();
    }

    // Fallback only — used if the spawned boss prefab has no MechIntroCutscene
    // (e.g. an older/placeholder prefab), so the encounter never silently stalls
    // in the blocked-input state.
    private IEnumerator ReturnToPlayerCameraAfterDelay()
    {
        yield return new WaitForSeconds(ceilingCameraHoldDuration);
        Debug.Log("[MinigameFlowController] ReturnToPlayerCameraAfterDelay complete - playerCamera back on");
        SetCam(ceilingMechCamera, false);
        SetCam(playerCamera, true);

        if (inputBlocker) inputBlocker.UnblockInput();
    }

    private void SpawnBoss()
    {
        if (bossPrefab == null || bossSpawnPoint == null) return;

        GameObject bossGO = Instantiate(bossPrefab, bossSpawnPoint.position, bossSpawnPoint.rotation);

        // Hand the reveal off to the boss's own drop-in cutscene, reusing this
        // controller's cameras rather than running the two in parallel — that's
        // what was killing the cutscene: this controller's fixed timer used to
        // cut back to playerCamera and unblock input regardless of whether the
        // boss's own drop-in was still mid-sequence.
        var mechIntro = bossGO.GetComponent<MechIntroCutscene>();
        if (mechIntro != null)
        {
            mechIntro.cutsceneCamera = ceilingMechCamera;
            mechIntro.playerCamera = playerCamera;
            mechIntro.manageInputLock = false; // this controller already owns input blocking
            mechIntro.OnCutsceneComplete += HandleMechIntroComplete;
        }
        else
        {
            StartCoroutine(ReturnToPlayerCameraAfterDelay());
        }
    }

    private void HandleMechIntroComplete()
    {
        Debug.Log("[MinigameFlowController] HandleMechIntroComplete - mech cutscene finished, unblocking input");
        if (inputBlocker) inputBlocker.UnblockInput();

        // PlayerCam only sets the cursor lock state once, in its own Start() —
        // it never reasserts it later. Every other handoff point in this
        // controller (ResumeTerminal, EndTerminal, OnPuzzleInteractionReady's
        // callers) re-locks explicitly for that reason; this one needs to too,
        // or the cursor stays wherever the minigame UI last left it.
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    private void OnServersShutdownComplete()
    {
        Debug.Log("[MinigameFlowController] OnServersShutdownComplete");
        ShowOnly(aiShutdownPanel);
        if (aiShutdownText) aiShutdownText.text = aiShutdownMessage;

        StartCoroutine(FinishSequence());
    }

    private IEnumerator FinishSequence()
    {
        yield return new WaitForSeconds(aiShutdownDisplayDuration);
        TurnOnLights();
        EndTerminal();
    }

    private void TurnOnLights()
    {
        if (serverMinigameManager) serverMinigameManager.StopWarningLights();

        if (lightsToTurnOn == null) return;
        foreach (var l in lightsToTurnOn)
            if (l != null) l.enabled = true;
    }

    private void EndTerminal()
    {
        Debug.Log("[MinigameFlowController] EndTerminal - _inCutscene cleared, F unlocked");
        ShowOnly(null);
        if (inputBlocker) inputBlocker.UnblockInput();
        SetCam(minigameCamera, false);
        SetCam(playerCamera, true);
        if (minigameCanvas) minigameCanvas.gameObject.SetActive(false);

        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;

        _isRunning = false;
        _isPaused = false;
        _inCutscene = false;
    }

    private void ShowOnly(GameObject panel)
    {
        if (arrowPanel) arrowPanel.SetActive(panel == arrowPanel);
        if (codePanel) codePanel.SetActive(panel == codePanel);
        if (voltagePanel) voltagePanel.SetActive(panel == voltagePanel);
        if (aiShutdownPanel) aiShutdownPanel.SetActive(panel == aiShutdownPanel);
    }
}