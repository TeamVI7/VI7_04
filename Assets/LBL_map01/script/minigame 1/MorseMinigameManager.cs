using System;
using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class MorseMinigameManager : MonoBehaviour
{
    [Header("Mật khẩu cài sẵn")]
    public string password = "1234";

    [Header("Cửa")]
    public SlidingDoorController door;

    [Header("UI")]
    public TMP_InputField answerInput;
    public Button         enterButton;
    public TMP_Text       feedbackText;
    public Image          inputBackground;

    [Header("Màu sắc")]
    public Color normalColor      = new Color(0.15f, 0.15f, 0.15f, 1f);
    public Color hoverColor       = new Color(0.28f, 0.28f, 0.28f, 1f);
    public Color focusColor       = new Color(0.12f, 0.35f, 0.55f, 1f);
    public Color wrongColor       = new Color(0.6f,  0.05f, 0.05f, 1f);
    public Color correctTextColor = new Color(0.2f,  1f,   0.4f,  1f);
    public Color wrongTextColor   = new Color(1f,    0.3f, 0.3f,  1f);

    public float wrongFlashDuration = 0.6f;
    public float doorOpenDelay      = 0.5f;

    public event Action OnPasswordSolved;

    // ── State ────────────────────────────────────────────────────
    private bool      _solved    = false;
    private bool      _isFocused = false;
    private Coroutine _wrongFlash;
    private Coroutine _colorTween;

    private void Start()
    {
        enterButton?.onClick.AddListener(OnSubmit);

        if (answerInput != null)
        {
            answerInput.lineType = TMP_InputField.LineType.SingleLine;
            answerInput.onSelect.AddListener(_   => OnInputFocus(true));
            answerInput.onDeselect.AddListener(_ => OnInputFocus(false));
            answerInput.onSubmit.AddListener(_ => answerInput.ActivateInputField());
            SetupHoverEvents();
        }

        SetInputColor(normalColor, instant: true);
        SetFeedback("", Color.clear);
    }

    public void StartNewRound()
    {
        if (_solved) return;
        if (answerInput) { answerInput.text = ""; answerInput.interactable = true; }
        if (enterButton) enterButton.interactable = true;
        SetFeedback("", Color.clear);
        SetInputColor(normalColor, instant: true);
    }

    // ── Hover ─────────────────────────────────────────────────────
    private void SetupHoverEvents()
    {
        var trigger = answerInput.gameObject.GetComponent<UnityEngine.EventSystems.EventTrigger>()
                   ?? answerInput.gameObject.AddComponent<UnityEngine.EventSystems.EventTrigger>();

        var enter = new UnityEngine.EventSystems.EventTrigger.Entry
            { eventID = UnityEngine.EventSystems.EventTriggerType.PointerEnter };
        enter.callback.AddListener(_ => { if (!_isFocused) SetInputColor(hoverColor); });
        trigger.triggers.Add(enter);

        var exit = new UnityEngine.EventSystems.EventTrigger.Entry
            { eventID = UnityEngine.EventSystems.EventTriggerType.PointerExit };
        exit.callback.AddListener(_ => { if (!_isFocused) SetInputColor(normalColor); });
        trigger.triggers.Add(exit);
    }

    // ── Submit ────────────────────────────────────────────────────
    private void OnSubmit()
    {
        if (_solved || answerInput == null) return;
        string answer = answerInput.text.Trim().ToUpper();
        if (string.IsNullOrEmpty(answer)) return;

        if (answer == password.ToUpper()) HandleCorrect();
        else                              HandleWrong();
    }

    // ── Đúng ──────────────────────────────────────────────────────
    private void HandleCorrect()
    {
        _solved = true;
        SetFeedback("✓ ĐÚNG", correctTextColor);
        SetInputColor(new Color(0.1f, 0.5f, 0.15f, 1f), instant: true);
        if (answerInput) answerInput.interactable = false;
        if (enterButton) enterButton.interactable = false;
        StartCoroutine(OpenDoorDelayed());
    }

    private IEnumerator OpenDoorDelayed()
    {
        yield return new WaitForSeconds(doorOpenDelay);
        door?.UnlockAndOpen();
        OnPasswordSolved?.Invoke();
    }

    // ── Sai ───────────────────────────────────────────────────────
    private void HandleWrong()
    {
        if (_wrongFlash != null) StopCoroutine(_wrongFlash);
        _wrongFlash = StartCoroutine(WrongFlashRoutine());
    }

    private IEnumerator WrongFlashRoutine()
    {
        if (answerInput) answerInput.interactable = false;
        if (enterButton) enterButton.interactable = false;

        SetInputColor(wrongColor, instant: true);
        SetFeedback("✗ SAI", wrongTextColor);

        float half = wrongFlashDuration / 5f;
        for (int i = 0; i < 2; i++)
        {
            yield return new WaitForSeconds(half);
            SetInputColor(normalColor, instant: true);
            yield return new WaitForSeconds(half);
            SetInputColor(wrongColor, instant: true);
        }
        yield return new WaitForSeconds(half);

        if (answerInput) { answerInput.text = ""; answerInput.interactable = true; }
        if (enterButton) enterButton.interactable = true;
        SetFeedback("", Color.clear);
        SetInputColor(_isFocused ? focusColor : normalColor, instant: true);
        answerInput?.ActivateInputField();
    }

    // ── Focus ─────────────────────────────────────────────────────
    private void OnInputFocus(bool focused)
    {
        _isFocused = focused;
        if (_wrongFlash != null) return;
        SetInputColor(focused ? focusColor : normalColor);
    }

    // ── Helpers ───────────────────────────────────────────────────
    private void SetInputColor(Color target, bool instant = false)
    {
        if (inputBackground == null) return;
        if (instant || !Application.isPlaying) { inputBackground.color = target; return; }
        if (_colorTween != null) StopCoroutine(_colorTween);
        _colorTween = StartCoroutine(TweenColor(target));
    }

    private IEnumerator TweenColor(Color target)
    {
        Color start = inputBackground.color;
        float t = 0f;
        while (t < 1f)
        {
            t += Time.deltaTime * 8f;
            inputBackground.color = Color.Lerp(start, target, t);
            yield return null;
        }
        inputBackground.color = target;
    }

    private void SetFeedback(string msg, Color color)
    {
        if (!feedbackText) return;
        feedbackText.text  = msg;
        feedbackText.color = color;
    }
}