using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// BIOSDisconnectPanel — Confirm disconnect from The Board terminal.
///
///   TERMINATE CORTEX LINK?
///   ─────────────────────────────────────────────────
///   WARNING: Disconnecting will suspend your operative
///   status. The Board does not forget deserters.
///
///   [ CONFIRM ]        [ ABORT ]
/// </summary>
public class BIOSQuitPanel : MonoBehaviour
{
    [Header("Buttons")]
    [SerializeField] private Button btnYes;
    [SerializeField] private Button btnNo;

    [Header("Button Labels")]
    [SerializeField] private TextMeshProUGUI lblYes;
    [SerializeField] private TextMeshProUGUI lblNo;

    [Header("Colors")]
    [SerializeField] private Color normalColor  = new Color(0.67f, 0.67f, 0.67f);
    [SerializeField] private Color selectedColor= Color.black;
    [SerializeField] private Color selectedBg   = new Color(0.67f, 0.67f, 0.67f);

    private int  _selected   = 1; // default: ABORT
    private bool _blockInput = false;

    void OnEnable()
    {
        _selected   = 1;
        _blockInput = true;
        Refresh();
    }

    void Update()
    {
        if (!gameObject.activeInHierarchy) return;
        if (_blockInput) { _blockInput = false; return; }

        if (Input.GetKeyDown(KeyCode.LeftArrow)  || Input.GetKeyDown(KeyCode.A)) SetSelected(0);
        if (Input.GetKeyDown(KeyCode.RightArrow) || Input.GetKeyDown(KeyCode.D)) SetSelected(1);

        if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter)) Confirm();
        if (Input.GetKeyDown(KeyCode.Escape)) { SetSelected(1); Confirm(); }
    }

    void SetSelected(int i)
    {
        _selected = i;
        Refresh();
        MenuAudio.Instance?.PlayNavigate();
    }

    void Confirm()
    {
        MenuAudio.Instance?.PlayConfirm();
        if (_selected == 0) BIOSMainMenu.Instance?.OnDisconnect();
        else                BIOSMainMenu.Instance?.SwitchTab(0);
    }

    void Refresh()
    {
        SetStyle(lblYes, btnYes, _selected == 0, "CONFIRM");
        SetStyle(lblNo,  btnNo,  _selected == 1, "ABORT");
    }

    void SetStyle(TextMeshProUGUI lbl, Button btn, bool active, string label)
    {
        if (lbl != null)
        {
            lbl.text  = active ? $"[ {label} ]" : $"  {label}  ";
            lbl.color = active ? selectedColor : normalColor;
        }
        if (btn != null)
        {
            var img = btn.GetComponent<Image>();
            if (img != null) img.color = active ? selectedBg : Color.clear;
        }
    }

    public void ClickYes() { SetSelected(0); Confirm(); }
    public void ClickNo()  { SetSelected(1); Confirm(); }
}