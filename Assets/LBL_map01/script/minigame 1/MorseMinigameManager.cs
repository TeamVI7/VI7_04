using System;
using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
public class MorseMinigameManager : MonoBehaviour
{
    [Header("Mật khẩu cài sẵn (chỉ gồm số)")]
    public string password = "1234";

    [Header("Cửa")]
    public SlidingDoorController door;

    [Header("UI - Màn hình hiển thị")]
    public TMP_Text displayText;      
    public Image    displayBackground; 
    public TMP_Text feedbackText;      

    [Header("UI - Numpad")]
    [Tooltip("Kéo 10 nút số 0-9 vào đây (thứ tự không bắt buộc, chỉ cần gắn đúng OnClick cho từng nút)")]
    public Button[] digitButtons;
    public Button delButton;
    public Button okButton;

    [Header("UI - Panel khóa khi nhập sai")]
    public GameObject lockedPanel;    
    public float      lockedDuration = 1.2f;

    [Header("Màu sắc")]
    public Color normalColor      = new Color(0.15f, 0.15f, 0.15f, 1f);
    public Color wrongColor       = new Color(0.6f,  0.05f, 0.05f, 1f);
    public Color correctColor     = new Color(0.1f,  0.5f,  0.15f, 1f);
    public Color correctTextColor = new Color(0.2f,  1f,    0.4f,  1f);
    public Color wrongTextColor   = new Color(1f,    0.3f,  0.3f,  1f);

    public float wrongFlashDuration = 0.6f;
    public float doorOpenDelay      = 0.5f;

    public event Action OnPasswordSolved;

    private string    _currentInput = "";
    private bool      _solved       = false;
    private bool      _locked       = false;
    private Coroutine _wrongFlash;

    private int MaxLength => password.Length;

    private void Start()
    {
        if (delButton != null) delButton.onClick.AddListener(OnDeletePressed);
        if (okButton  != null) okButton.onClick.AddListener(OnEnterPressed);

        SetDisplayColor(normalColor, instant: true);
        SetFeedback("", Color.clear);
        if (lockedPanel != null) lockedPanel.SetActive(false);

        UpdateDisplay();
    }

    public void StartNewRound()
    {
        if (_solved) return;
        _currentInput = "";
        _locked       = false;
        SetButtonsInteractable(true);
        SetFeedback("", Color.clear);
        SetDisplayColor(normalColor, instant: true);
        if (lockedPanel != null) lockedPanel.SetActive(false);
        UpdateDisplay();
    }

    public void OnDigitPressed(string digit)
    {
        if (_solved || _locked) return;
        if (_currentInput.Length >= MaxLength) return;

        _currentInput += digit;
        UpdateDisplay();
        if (_currentInput.Length == MaxLength)
            OnEnterPressed();
    }
    public void OnDeletePressed()
    {
        if (_solved || _locked) return;
        if (_currentInput.Length == 0) return;

        _currentInput = _currentInput.Substring(0, _currentInput.Length - 1);
        UpdateDisplay();
    }

    public void OnEnterPressed()
    {
        if (_solved || _locked) return;
        if (string.IsNullOrEmpty(_currentInput)) return;

        if (_currentInput == password) HandleCorrect();
        else                            HandleWrong();
    }
    private void HandleCorrect()
    {
        _solved = true;
        SetFeedback("CORRECT", correctTextColor);
        SetDisplayColor(correctColor, instant: true);
        SetButtonsInteractable(false);
        StartCoroutine(OpenDoorDelayed());
    }

    private IEnumerator OpenDoorDelayed()
    {
        yield return new WaitForSeconds(doorOpenDelay);
        door?.UnlockAndOpen();
        OnPasswordSolved?.Invoke();
    }
    private void HandleWrong()
    {
        if (_wrongFlash != null) StopCoroutine(_wrongFlash);
        _wrongFlash = StartCoroutine(WrongFlashRoutine());
    }

    private IEnumerator WrongFlashRoutine()
    {
        _locked = true;
        SetButtonsInteractable(false);

        SetDisplayColor(wrongColor, instant: true);
        SetFeedback("WRONG", wrongTextColor);
        if (lockedPanel != null) lockedPanel.SetActive(true);

        float half = wrongFlashDuration / 4f;
        for (int i = 0; i < 2; i++)
        {
            yield return new WaitForSeconds(half);
            SetDisplayColor(normalColor, instant: true);
            yield return new WaitForSeconds(half);
            SetDisplayColor(wrongColor, instant: true);
        }

        yield return new WaitForSeconds(Mathf.Max(0f, lockedDuration - wrongFlashDuration));

        _currentInput = "";
        _locked       = false;
        UpdateDisplay();
        SetFeedback("", Color.clear);
        SetDisplayColor(normalColor, instant: true);
        if (lockedPanel != null) lockedPanel.SetActive(false);
        SetButtonsInteractable(true);
    }
    private void UpdateDisplay()
    {
        if (displayText != null)
            displayText.text = _currentInput;
    }

    private void SetButtonsInteractable(bool value)
    {
        if (digitButtons != null)
            foreach (var b in digitButtons)
                if (b != null) b.interactable = value;

        if (delButton != null) delButton.interactable = value;
        if (okButton  != null) okButton.interactable  = value;
    }

    private void SetDisplayColor(Color target, bool instant = false)
    {
        if (displayBackground == null) return;
        displayBackground.color = target; 
    }

    private void SetFeedback(string msg, Color color)
    {
        if (!feedbackText) return;
        feedbackText.text  = msg;
        feedbackText.color = color;
    }
}