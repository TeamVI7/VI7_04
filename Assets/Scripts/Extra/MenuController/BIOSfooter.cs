using UnityEngine;
using TMPro;
using UnityEngine.UI;

public class BIOSFooterBar : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private TextMeshProUGUI footerLeft;
    [SerializeField] private TextMeshProUGUI footerRight;
    [SerializeField] private Image           barImage;

    [Header("Colors")]
    [SerializeField] private Color barColor   = new Color(0.1f, 0.1f, 0.1f);
    [SerializeField] private Color textColor  = new Color(0.67f, 0.67f, 0.67f);
    [SerializeField] private Color accentColor= Color.cyan;

    private static readonly string[] _hints =
    {
        "TAB Switch Tab         \u2191\u2193 Select         ENTER Deploy Operative          ESC Back",
        "TAB Switch Tab         \u2191\u2193 Select         \u2190\u2192 Change Value         ENTER Save         ESC Abort",
        "TAB Switch Tab         \u2191\u2193 Scroll         ESC Close File",
        "\u2190\u2192 Select         ENTER Confirm         ESC Abort Disconnect",
    };

    private int _lastTab = -1;

    void Start()
    {
        if (barImage   != null) barImage.color    = barColor;
        if (footerLeft != null) footerLeft.color  = textColor;
        if (footerRight != null)
        {
            footerRight.text  = "CORTEX LINK ACTIVE";
            footerRight.color = accentColor;
        }
    }

    void Update()
    {
        if (BIOSMainMenu.Instance == null) return;

        int tab = BIOSMainMenu.Instance.CurrentTab; // reads the real value now
        if (tab == _lastTab) return;

        _lastTab = tab;
        if (footerLeft != null && tab >= 0 && tab < _hints.Length)
            footerLeft.text = _hints[tab];
    }
}