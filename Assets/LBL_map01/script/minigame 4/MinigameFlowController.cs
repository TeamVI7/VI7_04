using System.Collections;
using UnityEngine;
using TMPro;

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

    [Header("Lights To Turn On")]
    public Light[] lightsToTurnOn;

    private bool _isRunning = false;
    private bool _isPaused = false;

    public void StartTerminal()
    {
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

    private void BeginFreshRun()
    {
        _isRunning = true;
        _isPaused = false;

        if (minigameCanvas) minigameCanvas.gameObject.SetActive(true);
        if (minigameCamera) minigameCamera.gameObject.SetActive(true);
        if (raycastFixer) raycastFixer.FixAll();
        if (inputBlocker) inputBlocker.BlockInput();

        ShowOnly(arrowPanel);
        arrowMinigame.StartMinigame(OnArrowComplete);
    }

    private void PauseTerminal()
    {
        _isPaused = true;

        if (inputBlocker) inputBlocker.UnblockInput();
        if (minigameCamera) minigameCamera.gameObject.SetActive(false);

        PauseAllMinigames();
    }

    private void ResumeTerminal()
    {
        _isPaused = false;

        if (minigameCamera) minigameCamera.gameObject.SetActive(true);
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

    // Cả 3 minigame (Arrow, Code, Voltage) đã xong ở đây -> mới được phép bắt đầu puzzle tắt server.
    private void OnVoltageComplete()
    {
        ShowOnly(null);
        StartServerShutdownPuzzle();
    }

    /// <summary>
    /// Bước 4: Server trồi lên rồi bắt đầu puzzle "tắt đúng server theo đúng thứ tự".
    /// Chỉ được gọi SAU KHI cả 3 minigame (Arrow, Code, Voltage) đã hoàn thành.
    /// Thứ tự là RANDOM mỗi lần (kể cả sau khi bấm sai bị reset) — số hiệu server thì CỐ ĐỊNH (ServerBlock.serverNumber).
    /// Màn hình thông báo dùng CHUNG với AI Shutdown Panel/Text ở dưới, không cần UI riêng.
    /// </summary>
    private void StartServerShutdownPuzzle()
    {
        ShowOnly(aiShutdownPanel);

        serverMinigameManager.SetNoticeUI(aiShutdownPanel, aiShutdownText);
        serverMinigameManager.OnAllServersShutdown = OnServersShutdownComplete;
        serverMinigameManager.OnPlayerEnterTrigger();
    }

    private void OnServersShutdownComplete()
    {
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
        if (lightsToTurnOn == null) return;
        foreach (var l in lightsToTurnOn)
            if (l != null) l.enabled = true;
    }

    private void EndTerminal()
    {
        ShowOnly(null);
        if (inputBlocker) inputBlocker.UnblockInput();
        if (minigameCamera) minigameCamera.gameObject.SetActive(false);
        if (minigameCanvas) minigameCanvas.gameObject.SetActive(false);
        _isRunning = false;
        _isPaused = false;
    }

    private void ShowOnly(GameObject panel)
    {
        if (arrowPanel) arrowPanel.SetActive(panel == arrowPanel);
        if (codePanel) codePanel.SetActive(panel == codePanel);
        if (voltagePanel) voltagePanel.SetActive(panel == voltagePanel);
        if (aiShutdownPanel) aiShutdownPanel.SetActive(panel == aiShutdownPanel);
    }
}