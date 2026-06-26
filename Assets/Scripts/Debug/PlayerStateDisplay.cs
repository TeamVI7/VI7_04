using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// On-screen debug overlay (OnGUI) for the FPS player.
/// Shows movement state, health, stamina, and ability cooldowns/charges
/// (dash, slide, wall-run, climb) in one place, with a runtime font/size
/// switcher so it's readable on any build.
///
/// SETUP:
///   Drop on the Player root alongside PlayerMovement. All other references
///   (health, dash, sliding, wallRunning, climbing) are optional — assign
///   what exists in your scene. Missing refs are skipped silently, never
///   throw, and just don't render their row.
///
/// EXTENDING — adding a new tracked system:
///   1. Add a public reference field in the "References" region.
///   2. Add a one-line row inside the matching Draw_XXX() method
///      (or add a new Draw_XXX() method + call it from OnGUI/BuildSections).
///   3. That's it — no need to touch toggle/layout plumbing.
///   The row-drawing helper (Row()) handles spacing/auto-advance for you.
///
/// CONTROLS (defaults):
///   F1            toggle whole overlay
///   F2            cycle section visibility (All → Compact → Off)
///   F3            cycle font family
///   [ / ]         decrease / increase font size
///
/// DEBUG:
///   This whole component IS the debug tool — toggle showOverlay or remove
///   the GameObject for shipping builds. Wrap instantiation in
///   UNITY_EDITOR/DEVELOPMENT_BUILD at a higher level if you want it
///   compiled out of release builds entirely.
/// </summary>
[DisallowMultipleComponent]
public class PlayerStateDisplay : MonoBehaviour
{
    // ─────────────────────────────────────────────────────────────────────────
    #region Inspector — References
    // ─────────────────────────────────────────────────────────────────────────

    [Header("Core (required for movement/velocity rows)")]
    public PlayerMovement playerMovement;
    public Rigidbody      rb;

    [Header("Optional — Health")]
    [Tooltip("Leave empty to auto-find PlayerHealth.Instance at runtime.")]
    public PlayerHealth playerHealth;

    [Header("Optional — Abilities (assign what exists in your scene)")]
    public Dashing     dashing;
    public Sliding      sliding;
    public WallRunning   wallRunning;
    public Climbing      climbing;
    public GroundStick   groundStick;

    #endregion

    // ─────────────────────────────────────────────────────────────────────────
    #region Inspector — Display
    // ─────────────────────────────────────────────────────────────────────────

    [Header("Toggle Keys")]
    public KeyCode toggleOverlayKey  = KeyCode.F1;
    public KeyCode cycleDetailKey    = KeyCode.F2;
    public KeyCode cycleFontKey      = KeyCode.F3;
    public KeyCode fontSizeDownKey   = KeyCode.LeftBracket;
    public KeyCode fontSizeUpKey     = KeyCode.RightBracket;

    [Header("Display")]
    public bool  showOverlay = true;
    public Color textColor   = Color.white;
    public Color headerColor = new Color(0.4f, 0.9f, 1f);
    public Color warnColor   = new Color(1f, 0.35f, 0.3f);
    public Color goodColor   = new Color(0.4f, 1f, 0.5f);

    [Header("Font")]
    [Tooltip("Cycled with cycleFontKey. Add/remove names freely — invalid OS fonts are skipped at runtime.")]
    public string[] osFontCandidates =
    {
        "Consolas", "Courier New", "Lucida Console", // Windows monospace
        "Menlo", "Monaco",                            // macOS monospace
        "DejaVu Sans Mono", "Liberation Mono",        // Linux monospace
        "Arial", "Verdana"                            // fallback non-mono
    };
    [Range(8, 36)] public int fontSize = 16;
    private const int FontSizeStep = 2;
    private const int FontSizeMin  = 8;
    private const int FontSizeMax  = 36;

    /// <summary>Detail level cycled by cycleDetailKey.</summary>
    private enum DetailLevel { Full, Compact, Off }
    [SerializeField] private DetailLevel _detail = DetailLevel.Full;

    #endregion

    // ─────────────────────────────────────────────────────────────────────────
    #region Public Read-only State (kept from original — still useful externally)
    // ─────────────────────────────────────────────────────────────────────────

    public Vector3 CurrentVelocity   => rb ? rb.linearVelocity : Vector3.zero;
    public float   CurrentSpeed      => CurrentVelocity.magnitude;
    public float   CurrentFlatSpeed  => new Vector3(CurrentVelocity.x, 0f, CurrentVelocity.z).magnitude;
    public float   VerticalSpeed     => CurrentVelocity.y;
    public string  CurrentState      => playerMovement ? playerMovement.state.ToString() : "None";
    public bool    IsGrounded        => playerMovement && playerMovement.grounded;

    #endregion

    // ─────────────────────────────────────────────────────────────────────────
    #region Private — Layout / Font State
    // ─────────────────────────────────────────────────────────────────────────

    private GUIStyle _style;
    private GUIStyle _headerStyle;
    private Font     _activeFont;
    private int      _fontIndex = -1; // -1 = default GUI font (no OS font applied yet)

    // Running layout cursor — reset each OnGUI pass, advanced by Row()/Header()
    private float _cursorX;
    private float _cursorY;
    private const float ColumnWidth = 230f;
    private const float PanelPad    = 12f;

    #endregion

    // ─────────────────────────────────────────────────────────────────────────
    #region Unity Lifecycle
    // ─────────────────────────────────────────────────────────────────────────

    private void Awake()
    {
        if (!playerMovement) playerMovement = GetComponent<PlayerMovement>();
        if (!rb)             rb             = GetComponent<Rigidbody>();
        if (!playerHealth)   playerHealth   = PlayerHealth.Instance;

        // Auto-pickup ability components if present on this GameObject and unassigned —
        // mirrors how Sliding/Dashing/etc. are typically co-located on the player root.
        if (!dashing)     dashing     = GetComponent<Dashing>();
        if (!sliding)     sliding     = GetComponent<Sliding>();
        if (!wallRunning) wallRunning = GetComponent<WallRunning>();
        if (!climbing)    climbing    = GetComponent<Climbing>();
        if (!groundStick) groundStick = GetComponent<GroundStick>();

        RebuildStyles();
    }

    private void Update()
    {
        if (Input.GetKeyDown(toggleOverlayKey))
            showOverlay = !showOverlay;

        if (Input.GetKeyDown(cycleDetailKey))
            CycleDetail();

        if (Input.GetKeyDown(cycleFontKey))
            CycleFont();

        if (Input.GetKeyDown(fontSizeDownKey))
            SetFontSize(fontSize - FontSizeStep);

        if (Input.GetKeyDown(fontSizeUpKey))
            SetFontSize(fontSize + FontSizeStep);

        // playerHealth may spawn after Awake (load order) — keep trying until found
        if (!playerHealth) playerHealth = PlayerHealth.Instance;
    }

    private void OnGUI()
    {
        if (!showOverlay || _detail == DetailLevel.Off) return;

        _style.normal.textColor = textColor;
        _headerStyle.normal.textColor = headerColor;

        _cursorX = PanelPad;
        _cursorY = PanelPad;

        Header("MOVEMENT");
        Draw_Movement();

        Header("HEALTH");
        Draw_Health();

        Header("STAMINA");
        Draw_Stamina();

        if (_detail == DetailLevel.Full)
        {
            Header("ABILITIES");
            Draw_Dash();
            Draw_Slide();
            Draw_WallRun();
            Draw_Climb();
            Draw_GroundStick();

            Header("DEBUG / DISPLAY");
            Row($"Font: {CurrentFontLabel()}  (F3 cycle)", textColor);
            Row($"Size: {fontSize}  ( [ / ] )", textColor);
            Row($"Detail: {_detail}  (F2 cycle)", textColor);
        }
    }

    #endregion

    // ─────────────────────────────────────────────────────────────────────────
    #region Section — Movement
    // ─────────────────────────────────────────────────────────────────────────

    private void Draw_Movement()
    {
        if (!playerMovement)
        {
            Row("PlayerMovement not assigned.", warnColor);
            return;
        }

        Row($"State: {CurrentState}", textColor);
        Row($"Grounded: {IsGrounded}", IsGrounded ? goodColor : warnColor);

        if (_detail == DetailLevel.Full)
        {
            Row($"Velocity: {CurrentVelocity:F2}", textColor);
            Row($"Speed: {CurrentSpeed:F2}", textColor);
            Row($"Flat Speed: {CurrentFlatSpeed:F2}", textColor);
            Row($"Vertical Speed: {VerticalSpeed:F2}", textColor);
        }
    }

    #endregion

    // ─────────────────────────────────────────────────────────────────────────
    #region Section — Health
    // ─────────────────────────────────────────────────────────────────────────

    private void Draw_Health()
    {
        if (!playerHealth)
        {
            Row("PlayerHealth not found.", warnColor);
            return;
        }

        float pct = playerHealth.Pct;
        Color c   = pct <= 0.25f ? warnColor : (pct <= 0.6f ? Color.yellow : goodColor);

        Row($"HP: {playerHealth.HP:F0} / {playerHealth.MaxHP:F0}  ({pct * 100f:F0}%)", c);
    }

    #endregion

    // ─────────────────────────────────────────────────────────────────────────
    #region Section — Stamina
    // ─────────────────────────────────────────────────────────────────────────

    private void Draw_Stamina()
    {
        if (!playerMovement)
        {
            Row("No stamina source.", warnColor);
            return;
        }

        float pct = playerMovement.StaminaPct;
        Color c   = pct <= 0.2f ? warnColor : (pct <= 0.5f ? Color.yellow : goodColor);

        Row($"Stamina: {playerMovement.Stamina:F0} / 100  ({pct * 100f:F0}%)", c);

        if (_detail == DetailLevel.Full)
            Row($"Sprinting: {playerMovement.IsSprinting}", playerMovement.IsSprinting ? goodColor : textColor);
    }

    #endregion

    // ─────────────────────────────────────────────────────────────────────────
    #region Section — Dash
    // ─────────────────────────────────────────────────────────────────────────

    private void Draw_Dash()
    {
        if (!dashing)
        {
            Row("Dash: (not assigned)", textColor);
            return;
        }

        // Charge bank if maxCharges > 1, classic cooldown otherwise — same data either way.
        bool ready = dashing.CanDash;
        Color c    = ready ? goodColor : warnColor;

        if (dashing.MaxCharges > 1)
        {
            Row($"Dash Charges: {dashing.ChargesAvailable}/{dashing.MaxCharges}"
                + (ready ? "" : $"  (next in {dashing.CooldownRemaining:F1}s)"), c);
        }
        else
        {
            Row(ready ? "Dash: READY" : $"Dash: CD {dashing.CooldownRemaining:F1}s", c);
        }

        if (_detail == DetailLevel.Full)
            Row($"Dashing: {dashing.IsDashing}", dashing.IsDashing ? goodColor : textColor);
    }

    #endregion

    // ─────────────────────────────────────────────────────────────────────────
    #region Section — Slide
    // ─────────────────────────────────────────────────────────────────────────

    private void Draw_Slide()
    {
        if (!sliding)
        {
            Row("Slide: (not assigned)", textColor);
            return;
        }

        if (sliding.IsSliding)
            Row($"Sliding: {sliding.SlideTimeRemaining:F2}s left", goodColor);
        else
            Row("Sliding: idle", textColor);
    }

    #endregion

    // ─────────────────────────────────────────────────────────────────────────
    #region Section — Wall Run
    // ─────────────────────────────────────────────────────────────────────────

    private void Draw_WallRun()
    {
        if (!wallRunning)
        {
            Row("WallRun: (not assigned)", textColor);
            return;
        }

        if (wallRunning.IsWallRunning)
            Row($"WallRun: ACTIVE ({(wallRunning.WallOnLeft ? "L" : "R")})", goodColor);
        else if (wallRunning.IsWallSliding)
            Row("WallRun: sliding down wall", Color.yellow);
        else
            Row("WallRun: idle", textColor);
    }

    #endregion

    // ─────────────────────────────────────────────────────────────────────────
    #region Section — Climb
    // ─────────────────────────────────────────────────────────────────────────

    private void Draw_Climb()
    {
        if (!climbing)
        {
            Row("Climb: (not assigned)", textColor);
            return;
        }

        if (climbing.IsClimbing)
            Row("Climb: ACTIVE", goodColor);
        else if (climbing.IsWallSliding)
            Row("Climb: wall-slide", Color.yellow);
        else
            Row("Climb: idle", textColor);
    }

    #endregion

    // ─────────────────────────────────────────────────────────────────────────
    #region Section — Ground Stick
    // ─────────────────────────────────────────────────────────────────────────

    private void Draw_GroundStick()
    {
        if (!groundStick) return; // fully optional system — skip row entirely if absent
        Row($"GroundStick: {groundStick.IsStuck}", groundStick.IsStuck ? goodColor : textColor);
    }

    #endregion

    // ─────────────────────────────────────────────────────────────────────────
    #region Layout Helpers
    // ─────────────────────────────────────────────────────────────────────────

    private float LineHeight => fontSize + 6f;

    private void Header(string label)
    {
        GUI.Label(new Rect(_cursorX, _cursorY, ColumnWidth, LineHeight), label, _headerStyle);
        _cursorY += LineHeight;
    }

    private void Row(string text, Color color)
    {
        _style.normal.textColor = color;
        GUI.Label(new Rect(_cursorX, _cursorY, ColumnWidth + 100f, LineHeight), text, _style);
        _cursorY += LineHeight;
    }

    #endregion

    // ─────────────────────────────────────────────────────────────────────────
    #region Font / Detail Switching
    // ─────────────────────────────────────────────────────────────────────────

    private void RebuildStyles()
    {
        _style = new GUIStyle
        {
            fontSize = fontSize,
            richText = false,
            normal   = { textColor = textColor }
        };

        _headerStyle = new GUIStyle(_style)
        {
            fontStyle = FontStyle.Bold
        };

        if (_activeFont != null)
        {
            _style.font       = _activeFont;
            _headerStyle.font = _activeFont;
        }
    }

    private void SetFontSize(int size)
    {
        fontSize = Mathf.Clamp(size, FontSizeMin, FontSizeMax);
        RebuildStyles();
    }

    /// <summary>
    /// Cycles through osFontCandidates, trying each as a dynamic OS font.
    /// Skips any name the OS doesn't have installed instead of throwing.
    /// Wraps back to "Default" after the last candidate.
    /// </summary>
    private void CycleFont()
    {
        if (osFontCandidates == null || osFontCandidates.Length == 0) return;

        int attempts = 0;
        int next     = _fontIndex;

        while (attempts < osFontCandidates.Length + 1)
        {
            next++;
            attempts++;

            // One extra slot beyond the array = "Default" (no OS font override)
            if (next >= osFontCandidates.Length)
            {
                _fontIndex  = osFontCandidates.Length; // sentinel = default
                _activeFont = null;
                RebuildStyles();
                return;
            }

            Font candidate = TryLoadOsFont(osFontCandidates[next]);
            if (candidate != null)
            {
                _fontIndex  = next;
                _activeFont = candidate;
                RebuildStyles();
                return;
            }
            // else: font not installed on this machine — try the next candidate
        }
    }

    private static Font TryLoadOsFont(string fontName)
    {
        try
        {
            // CreateDynamicFontFromOSFont returns a usable Font even if imperfectly matched
            // on some platforms; wrap in try/catch since behavior varies WebGL/mobile.
            Font f = Font.CreateDynamicFontFromOSFont(fontName, 16);
            return f;
        }
        catch
        {
            return null;
        }
    }

    private string CurrentFontLabel()
    {
        if (_activeFont == null) return "Default";
        if (_fontIndex >= 0 && _fontIndex < (osFontCandidates?.Length ?? 0))
            return osFontCandidates[_fontIndex];
        return _activeFont.name;
    }

    private void CycleDetail()
    {
        _detail = _detail switch
        {
            DetailLevel.Full    => DetailLevel.Compact,
            DetailLevel.Compact => DetailLevel.Off,
            _                   => DetailLevel.Full
        };
    }

    #endregion
}