using System;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class CodeInputMinigame : MonoBehaviour
{
    [Header("UI References")]
    public TMP_Text[] digitTexts;
    public TMP_Text statusText;
    public Image lockImage;

    [Header("Lock Sprites")]
    public Sprite lockedSprite;
    public Sprite unlockedSprite;

    [Header("Colors")]
    public Color normalColor = Color.white;
    public Color selectedColor = Color.cyan;

    [Header("Keys")]
    public KeyCode moveLeftKey = KeyCode.A;
    public KeyCode moveRightKey = KeyCode.D;
    public KeyCode increaseKey = KeyCode.W;
    public KeyCode decreaseKey = KeyCode.S;
    public KeyCode confirmKey = KeyCode.Return;

    [Header("Developer / Testing")]
    [Tooltip("Bật để cho phép dùng thêm 1 mã code phụ (devCode) luôn đúng, không phụ thuộc mã thật do CodeClueDistributor sinh ra. Chỉ dùng để test cho nhanh — nhớ TẮT trước khi build bản chính thức.")]
    public bool enableDevCode = false;
    [Tooltip("Mã code phụ dùng để test nhanh khi enableDevCode = true. Độ dài không cần khớp digitTexts.Length, chỉ cần nhập đúng chuỗi này là qua.")]
    public string devCode = "0000";

    private int[] _digits;
    private int _selectedIndex;
    private string _targetCode;
    private Action _onComplete;
    private bool _active;
    private bool _paused;

    public void StartMinigame(string targetCode, Action onComplete)
    {
        _targetCode = targetCode;
        _onComplete = onComplete;
        _digits = new int[digitTexts.Length];
        _selectedIndex = 0;
        _active = true;
        _paused = false;

        if (statusText) statusText.text = "";
        if (lockImage != null && lockedSprite != null) lockImage.sprite = lockedSprite;

        RefreshDisplay();
    }

    public void Pause()
    {
        _paused = true;
    }

    public void Resume()
    {
        _paused = false;
    }

    private void Update()
    {
        if (!_active || _paused) return;

        if (Input.GetKeyDown(moveLeftKey)) MoveSelection(-1);
        if (Input.GetKeyDown(moveRightKey)) MoveSelection(1);
        if (Input.GetKeyDown(increaseKey)) ChangeDigit(1);
        if (Input.GetKeyDown(decreaseKey)) ChangeDigit(-1);
        if (Input.GetKeyDown(confirmKey)) TrySubmit();
    }

    private void MoveSelection(int dir)
    {
        int count = digitTexts.Length;
        _selectedIndex = (_selectedIndex + dir + count) % count;
        RefreshDisplay();
    }

    private void ChangeDigit(int dir)
    {
        _digits[_selectedIndex] = (_digits[_selectedIndex] + dir + 10) % 10;
        RefreshDisplay();
    }

    private void RefreshDisplay()
    {
        for (int i = 0; i < digitTexts.Length; i++)
        {
            digitTexts[i].text = _digits[i].ToString();
            digitTexts[i].color = i == _selectedIndex ? selectedColor : normalColor;
        }
    }

    private void TrySubmit()
    {
        string entered = "";
        for (int i = 0; i < _digits.Length; i++)
            entered += _digits[i];

        bool isDevMatch = enableDevCode && !string.IsNullOrEmpty(devCode) && entered == devCode;

        if (entered == _targetCode || isDevMatch)
        {
            _active = false;
            if (statusText) statusText.text = "ACCESS GRANTED";
            if (lockImage != null && unlockedSprite != null) lockImage.sprite = unlockedSprite;
            if (isDevMatch) Debug.Log("[CodeInputMinigame] Mở khoá bằng DEV CODE (chỉ dùng để test).");
            _onComplete?.Invoke();
        }
        else
        {
            if (statusText) statusText.text = "ACCESS DENIED";
        }
    }
}