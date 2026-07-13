using UnityEngine;
using UnityEngine.UI;
using TMPro;

[RequireComponent(typeof(Button))]
public class TerminalWordSlot : MonoBehaviour
{
    public TMP_Text label;

    public bool isDudRemovalBracket = false;

    [HideInInspector] public string assignedWord;
    [HideInInspector] public bool   isUsed = false;

    private Button _button;

    public Button Button
    {
        get
        {
            if (_button == null) _button = GetComponent<Button>();
            return _button;
        }
    }

    private void Awake()
    {
        if (label == null) label = GetComponentInChildren<TMP_Text>();
    }

    public void ResetSlot()
    {
        isUsed = false;
        assignedWord = null;
        isDudRemovalBracket = false;
        SetInteractable(true);
        SetHighlight(TerminalVisualState.Normal);
    }

    public void SetText(string text)
    {
        if (label != null) label.text = text;
    }

    public void SetInteractable(bool value)
    {
        Button.interactable = value;
    }

    public void SetHighlight(TerminalVisualState state)
    {
        if (label == null) return;

        switch (state)
        {
            case TerminalVisualState.Normal:
                label.color = TerminalTheme.NormalColor;
                break;
            case TerminalVisualState.Correct:
                label.color = TerminalTheme.CorrectColor;
                break;
            case TerminalVisualState.Wrong:
                label.color = TerminalTheme.WrongColor;
                break;
            case TerminalVisualState.Disabled:
                label.color = TerminalTheme.DisabledColor;
                break;
        }
    }
}

public enum TerminalVisualState
{
    Normal,
    Correct,
    Wrong,
    Disabled
}

public static class TerminalTheme
{
    public static Color NormalColor   = new Color(0.20f, 1f, 0.30f);
    public static Color CorrectColor  = new Color(0.6f, 1f, 0.6f);
    public static Color WrongColor    = new Color(1f, 0.25f, 0.2f);
    public static Color DisabledColor = new Color(0.15f, 0.4f, 0.18f);
}