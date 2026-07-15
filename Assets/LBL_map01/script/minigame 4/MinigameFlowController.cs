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

    [Header("AI Shutdown Notice")]
    public GameObject aiShutdownPanel;
    public TMP_Text aiShutdownText;
    public string aiShutdownMessage = "AI CORE OFFLINE";
    public float aiShutdownDisplayDuration = 3f;

    [Header("Lights To Turn On")]
    public Light[] lightsToTurnOn;

    private bool _isRunning = false;

    public void StartTerminal()
    {
        if (_isRunning) return;
        _isRunning = true;

        if (minigameCanvas) minigameCanvas.gameObject.SetActive(true);
        if (minigameCamera) minigameCamera.gameObject.SetActive(true);
        if (raycastFixer) raycastFixer.FixAll();
        if (inputBlocker) inputBlocker.BlockInput();

        ShowOnly(arrowPanel);
        arrowMinigame.StartMinigame(OnArrowComplete);
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
        ShowOnly(null);
        StartCoroutine(RiseAndShowVoltage());
    }

    private IEnumerator RiseAndShowVoltage()
    {
        serverMinigameManager.OnPlayerEnterTrigger();
        float wait = serverMinigameManager.GetRiseSequenceDuration();
        yield return new WaitForSeconds(wait);

        ShowOnly(voltagePanel);
        voltageMinigame.StartMinigame(OnVoltageComplete);
    }

    private void OnVoltageComplete()
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
    }

    private void ShowOnly(GameObject panel)
    {
        if (arrowPanel) arrowPanel.SetActive(panel == arrowPanel);
        if (codePanel) codePanel.SetActive(panel == codePanel);
        if (voltagePanel) voltagePanel.SetActive(panel == voltagePanel);
        if (aiShutdownPanel) aiShutdownPanel.SetActive(panel == aiShutdownPanel);
    }
}