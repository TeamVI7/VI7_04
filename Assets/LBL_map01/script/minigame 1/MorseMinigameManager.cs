using System;
using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Numpad passcode minigame — nhập HOÀN TOÀN bằng bấm chuột trên Canvas
/// (không cần gõ bàn phím). Gắn các nút số 0-9, DEL, OK vào Inspector.
/// </summary>
public class MorseMinigameManager : MonoBehaviour
{
    [Header("Mật khẩu cài sẵn (chỉ gồm số)")]
    public string password = "1234";

    [Header("Cửa")]
    public SlidingDoorController door;

    [Header("UI - Màn hình hiển thị")]
    public TMP_Text displayText;      // hiển thị số đã nhập, vd "045"
    public Image    displayBackground; // nền màn hình, đổi màu khi đúng/sai
    public TMP_Text feedbackText;      // optional: "✓ ĐÚNG" / "✗ SAI"

    [Header("UI - Numpad")]
    [Tooltip("Kéo 10 nút số 0-9 vào đây (thứ tự không bắt buộc, chỉ cần gắn đúng OnClick cho từng nút)")]
    public Button[] digitButtons;
    public Button delButton;
    public Button okButton;

    [Header("UI - Panel khóa khi nhập sai")]
    public GameObject lockedPanel;     // panel "KEYPAD LOCKED", để inactive sẵn trong scene
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

    // ── State ────────────────────────────────────────────────────
    private string    _currentInput = "";
    private bool      _solved       = false;
    private bool      _locked       = false;
    private Coroutine _wrongFlash;

    private int MaxLength => password.Length;

    private void Start()
    {
        // Gắn OnClick cho DEL / OK bằng code (khỏi cần kéo trong Inspector nếu muốn)
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

    // ── Gọi từ nút số 0-9 (OnClick -> OnDigitPressed, truyền string "0".."9") ──
    public void OnDigitPressed(string digit)
    {
        if (_solved || _locked) return;
        if (_currentInput.Length >= MaxLength) return;

        _currentInput += digit;
        UpdateDisplay();

        // Tự động check khi nhập đủ số ký tự (khỏi cần bấm OK)
        if (_currentInput.Length == MaxLength)
            OnEnterPressed();
    }

    // ── Gọi từ nút DEL ───────────────────────────────────────────
    public void OnDeletePressed()
    {
        if (_solved || _locked) return;
        if (_currentInput.Length == 0) return;

        _currentInput = _currentInput.Substring(0, _currentInput.Length - 1);
        UpdateDisplay();
    }

    // ── Gọi từ nút OK (hoặc tự động khi nhập đủ số) ─────────────
    public void OnEnterPressed()
    {
        if (_solved || _locked) return;
        if (string.IsNullOrEmpty(_currentInput)) return;

        if (_currentInput == password) HandleCorrect();
        else                            HandleWrong();
    }

    // ── Đúng ──────────────────────────────────────────────────────
    private void HandleCorrect()
    {
        _solved = true;
        SetFeedback("✓ ĐÚNG", correctTextColor);
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

    // ── Sai ───────────────────────────────────────────────────────
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
        SetFeedback("✗ SAI", wrongTextColor);
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

    // ── Helpers ───────────────────────────────────────────────────
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
        displayBackground.color = target; // instant, đủ dùng cho numpad; có thể thêm tween nếu muốn mượt hơn
    }

    private void SetFeedback(string msg, Color color)
    {
        if (!feedbackText) return;
        feedbackText.text  = msg;
        feedbackText.color = color;
    }
}