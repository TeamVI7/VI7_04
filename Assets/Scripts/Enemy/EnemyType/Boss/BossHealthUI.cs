// BossHealthUI.cs
// Dark Souls-style boss health bar. Builds its own UI at runtime -- no prefab
// or manual Canvas setup required. Drop this on any GameObject (an empty
// "BossUI" object works fine), assign targetHealth (and optionally bossBrain /
// weakpoint), press play, and it wires itself up.
//
// Visual language:
//   - Black bordered bar, red "current HP" fill that snaps instantly on damage.
//   - White "chip" fill that lingers, then drains down to match after a short
//     delay -- the classic Souls damage-taken readout.
//   - Boss name above the bar.
//   - Thin tick marks at each phase threshold (read from MechBossBrain).
//   - Bar flashes gold while staggered, punches on phase change, and pulses
//     cyan while an assigned MechWeakpoint is exposed.
//   - Fades in when the boss intro finishes, fades out a few seconds after death.
//
// Expandability notes:
//   - All colors/sizes/timings are Inspector-exposed.
//   - Show()/Hide() are public so other systems (multi-boss encounters, gates,
//     cutscenes) can drive visibility directly.
//   - UI is built from plain Image/Text primitives (no sprites required beyond
//     the built-in white pixel), so swapping in TextMeshPro or 9-sliced
//     sprites later is a drop-in change inside the "UI Construction" region
//     only -- event wiring is untouched.

using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using DG.Tweening;

public class BossHealthUI : MonoBehaviour
{
    #region Inspector

    [Header("Target")]
    [Tooltip("Boss health source. Auto-found on parent if left empty.")]
    public EnemyHealth targetHealth;
    [Tooltip("Optional. Drag a Canvas here to force it. Leave empty to auto-find/create.")]
    public Canvas targetCanvas;
    [Tooltip("Optional. Drives phase dividers, stagger flash, and auto show-on-intro.")]
    public MechBossBrain bossBrain;
    [Tooltip("Optional. Bar pulses cyan while this weakpoint is exposed.")]
    public MechWeakpoint weakpoint;
    [Tooltip("Optional. Drives the token/mana bar under the health bar. Auto-found on the boss if left empty; with no pool on the boss the token bar isn't built at all.")]
    public MechTokenPool tokenPool;
    [Tooltip("Optional. Only used to read the ultimate cost threshold for the marker tick on the token bar. Auto-found alongside the pool.")]
    public MechAttackSelector attackSelector;

    [Header("Name")]
    public string bossNameOverride;
    public Font nameFont;
    public int nameFontSize = 22;

    [Header("Layout")]
    public Vector2 barSize = new Vector2(600f, 22f);
    public float borderThickness = 3f;
    public Vector2 anchoredPosition = new Vector2(0f, -50f);

    [Header("Colors")]
    public Color borderColor = Color.black;
    public Color backgroundColor = new Color(0.08f, 0.08f, 0.08f);
    public Color normalFillColor = new Color(0.68f, 0.05f, 0.08f);
    public Color staggerFillColor = new Color(0.95f, 0.8f, 0.15f);
    public Color chipFillColor = new Color(0.9f, 0.9f, 0.75f);
    public Color weakpointPulseColor = new Color(0.2f, 0.9f, 1f);
    public Color nameColor = new Color(0.9f, 0.85f, 0.7f);

    [Header("Token Bar")]
    [Tooltip("Show the boss's attack-token pool as a mana bar under the health bar. Reading it is how the player learns that a quiet boss is charging something, rather than the ultimate feeling random.")]
    public bool showTokenBar = true;
    public float tokenBarHeight = 8f;
    [Tooltip("Vertical gap between the health bar and the token bar.")]
    public float tokenBarGap = 4f;
    [Tooltip("Token bar is inset this much on each side so it reads as secondary to the health bar. 0 makes them the same width.")]
    public float tokenBarInset = 0f;
    public Color tokenFillColor = new Color(0.25f, 0.55f, 0.95f);
    [Tooltip("Flashed for an instant when the boss spends tokens, so the drop is visible even when the attack that caused it is off-screen.")]
    public Color tokenSpendFlashColor = new Color(0.8f, 0.9f, 1f);
    [Tooltip("Colour the bar pulses once the boss can afford an ultimate — the tell that a big move is now on the table.")]
    public Color tokenReadyColor = new Color(0.95f, 0.4f, 0.9f);
    [Tooltip("Draw a tick at the ultimate cost, read from MechAttackSelector. Turns the bar into a countdown the player can actually read.")]
    public bool showUltimateMarker = true;
    public float tokenGainDuration = 0.25f;
    public float tokenSpendDuration = 0.12f;

    [Header("Chip Damage")]
    public float chipDelay = 0.4f;
    public float chipDuration = 0.6f;
    public Ease chipEase = Ease.OutQuad;

    [Header("Show/Hide")]
    public float fadeDuration = 0.5f;
    public float hideDelayAfterDeath = 3f;
    [Tooltip("If true, shows immediately on Start instead of waiting for the boss intro to finish.")]
    public bool showImmediately = false;

    [Header("Debug")]
    [SerializeField] private bool showDebugLogs = false;

    #endregion

    #region Runtime Refs

    private CanvasGroup _group;
    private Image _immediateFill;
    private Image _chipFill;
    private Image _borderPulse;
    private Text _nameText;
    private RectTransform _fillArea;

    private Image _tokenFill;
    private RectTransform _tokenBorderRT;

    private Tween _chipTween;
    private Tween _pulseTween;
    private Tween _tokenTween;
    private Tween _tokenReadyTween;
    private Coroutine _hideRoutine;
    private bool _tokenReadyShown;

    #endregion

    #region Unity Lifecycle

    private void Awake()
    {
        if (targetHealth == null) targetHealth = GetComponentInParent<EnemyHealth>();
        if (bossBrain == null && targetHealth != null) bossBrain = targetHealth.GetComponent<MechBossBrain>();
        if (tokenPool == null && targetHealth != null) tokenPool = targetHealth.GetComponent<MechTokenPool>();
        if (attackSelector == null && targetHealth != null) attackSelector = targetHealth.GetComponent<MechAttackSelector>();

        BuildUI();

        if (tokenPool != null) tokenPool.OnTokensChanged += HandleTokensChanged;

        if (targetHealth == null)
        {
            Debug.LogWarning("[BossHealthUI] No EnemyHealth assigned or found -- bar will not update.", this);
            return;
        }

        targetHealth.OnDamaged += HandleDamaged;
        targetHealth.OnDied += _ => HandleDied();

        if (bossBrain != null)
        {
            bossBrain.OnIntroComplete += Show;
            bossBrain.OnStateChanged += HandleStateChanged;
            bossBrain.OnPhaseChanged += HandlePhaseChanged;
        }

        if (weakpoint != null)
        {
            weakpoint.OnWeakpointExposed += HandleWeakpointExposed;
            weakpoint.OnWeakpointClosed += HandleWeakpointClosed;
        }
    }

    private void Start()
    {
        // Seeded here rather than from the pool's first event: script execution
        // order decides whether that event fired before this component existed,
        // but every Awake has run by the time any Start does, so the pool's
        // starting balance is readable now.
        if (_tokenFill != null && tokenPool != null) SetTokenFillImmediate(tokenPool.Normalized);

        if (showImmediately) Show();
    }

    private void OnDestroy()
    {
        if (targetHealth != null) targetHealth.OnDamaged -= HandleDamaged;
        if (tokenPool != null) tokenPool.OnTokensChanged -= HandleTokensChanged;

        if (bossBrain != null)
        {
            bossBrain.OnIntroComplete -= Show;
            bossBrain.OnStateChanged -= HandleStateChanged;
            bossBrain.OnPhaseChanged -= HandlePhaseChanged;
        }

        if (weakpoint != null)
        {
            weakpoint.OnWeakpointExposed -= HandleWeakpointExposed;
            weakpoint.OnWeakpointClosed -= HandleWeakpointClosed;
        }
    }

    #endregion

    #region Public API

    public void Show()
    {
        if (_hideRoutine != null) { StopCoroutine(_hideRoutine); _hideRoutine = null; }
        SetFillImmediate(1f);
        _group.DOKill();
        _group.blocksRaycasts = false; // bar never intercepts input
        _group.DOFade(1f, fadeDuration).SetEase(Ease.OutQuad);
        Log("Shown.");
    }

    public void Hide()
    {
        _group.DOKill();
        _group.DOFade(0f, fadeDuration).SetEase(Ease.InQuad);
        Log("Hidden.");
    }

    #endregion

    #region Health Events

    private void HandleDamaged(float amount, float curHP, float maxHP, Vector3 dir, Vector3 point)
    {
        if (maxHP <= 0f) return;
        float fraction = Mathf.Clamp01(curHP / maxHP);
        _immediateFill.fillAmount = fraction;
        QueueChipDrain(fraction);
    }

    private void HandleDied()
    {
        _immediateFill.fillAmount = 0f;
        _chipTween?.Kill();
        _chipFill.fillAmount = 0f;

        // A bar left mid-pulse under a dead boss reads as "it's still charging".
        if (_tokenFill != null)
        {
            _tokenReadyTween?.Kill();
            _tokenReadyShown = false;
            _tokenTween?.Kill();
            _tokenFill.DOKill();
            _tokenFill.color = tokenFillColor;
            _tokenTween = DOTween.To(() => _tokenFill.fillAmount, v => _tokenFill.fillAmount = v, 0f, 0.4f);
        }

        Log("Boss died -- hiding bar after delay.");
        if (_hideRoutine != null) StopCoroutine(_hideRoutine);
        _hideRoutine = StartCoroutine(Co_HideAfterDelay());
    }

    private IEnumerator Co_HideAfterDelay()
    {
        yield return new WaitForSeconds(hideDelayAfterDeath);
        Hide();
    }

    #endregion

    #region Brain Events

    private void HandleStateChanged(MechBossState prev, MechBossState next)
    {
        _immediateFill.DOKill();
        Color target = next == MechBossState.Staggered ? staggerFillColor : normalFillColor;
        _immediateFill.DOColor(target, 0.25f);
    }

    private void HandlePhaseChanged(int prev, int next)
    {
        Log($"Phase UI: {prev} -> {next}");
        _fillArea.DOKill();
        _fillArea.DOPunchScale(new Vector3(0f, 0.5f, 0f), 0.35f, 6, 0.6f);
    }

    #endregion

    #region Weakpoint Events

    private void HandleWeakpointExposed()
    {
        _pulseTween?.Kill();
        _borderPulse.gameObject.SetActive(true);
        _borderPulse.color = new Color(weakpointPulseColor.r, weakpointPulseColor.g, weakpointPulseColor.b, 0f);
        _pulseTween = _borderPulse.DOFade(0.9f, 0.4f).SetLoops(-1, LoopType.Yoyo);
    }

    private void HandleWeakpointClosed()
    {
        _pulseTween?.Kill();
        _borderPulse.gameObject.SetActive(false);
    }

    #endregion

    #region Token Events

    private void HandleTokensChanged(int current, int max)
    {
        if (_tokenFill == null) return;

        float target = max > 0 ? Mathf.Clamp01(current / (float)max) : 0f;
        bool spent = target < _tokenFill.fillAmount;

        _tokenTween?.Kill();
        _tokenTween = DOTween.To(() => _tokenFill.fillAmount, v => _tokenFill.fillAmount = v, target,
                                 spent ? tokenSpendDuration : tokenGainDuration)
            .SetEase(spent ? Ease.OutQuad : Ease.Linear); // regen is linear because it *is* linear — a curve would lie about the pacing

        // Ready-state first, flash second: dropping out of "ready" resets the bar
        // colour, and that's exactly the moment the ultimate fires — the frame the
        // flash matters most. Flashing first would have it wiped the same frame.
        UpdateTokenReadyPulse(current);
        if (spent) FlashTokenSpend();
    }

    private void FlashTokenSpend()
    {
        // DOKill hits every colour tween on this Image, the looping ready-pulse
        // included, so the flash has to hand the pulse back when it finishes —
        // otherwise one spend silently ends the pulse for the rest of the fight.
        _tokenReadyTween?.Kill();
        _tokenFill.DOKill();
        _tokenFill.color = tokenSpendFlashColor;
        _tokenFill.DOColor(tokenFillColor, 0.25f).OnComplete(RestartReadyPulseIfNeeded);
    }

    private void RestartReadyPulseIfNeeded()
    {
        if (!_tokenReadyShown || _tokenFill == null) return;
        _tokenReadyTween?.Kill();
        _tokenFill.color = tokenFillColor;
        _tokenReadyTween = _tokenFill.DOColor(tokenReadyColor, 0.6f).SetLoops(-1, LoopType.Yoyo);
    }

    /// <summary>Pulses the bar once the boss can pay for an ultimate. This is the
    /// only warning the player gets that the pool is no longer just filling up, so
    /// it's driven by the same threshold the selector actually spends against.</summary>
    private void UpdateTokenReadyPulse(int current)
    {
        bool ready = attackSelector != null && current >= attackSelector.ultimateCostThreshold;
        if (ready == _tokenReadyShown) return;
        _tokenReadyShown = ready;

        _tokenReadyTween?.Kill();
        _tokenFill.DOKill();

        if (ready) RestartReadyPulseIfNeeded();
        else _tokenFill.color = tokenFillColor;
    }

    private void SetTokenFillImmediate(float fraction)
    {
        _tokenTween?.Kill();
        _tokenFill.fillAmount = fraction;
        if (tokenPool != null) UpdateTokenReadyPulse(tokenPool.Current);
    }

    #endregion

    #region Fill Helpers

    private void SetFillImmediate(float fraction)
    {
        _immediateFill.fillAmount = fraction;
        _chipFill.fillAmount = fraction;
    }

    private void QueueChipDrain(float targetFraction)
    {
        _chipTween?.Kill();
        _chipTween = DOVirtual.DelayedCall(chipDelay, () =>
        {
            _chipTween = DOTween.To(() => _chipFill.fillAmount, v => _chipFill.fillAmount = v, targetFraction, chipDuration)
                .SetEase(chipEase);
        });
    }

    #endregion

    #region UI Construction

    private void BuildUI()
    {
        Canvas canvas = FindOrCreateCanvas();

        RectTransform root = CreateUIObject("BossHealthUI_Root", canvas.transform).GetComponent<RectTransform>();
        root.anchorMin = new Vector2(0.5f, 1f);
        root.anchorMax = new Vector2(0.5f, 1f);
        root.pivot = new Vector2(0.5f, 1f);
        root.anchoredPosition = anchoredPosition;
        root.sizeDelta = new Vector2(barSize.x, barSize.y + 30f);
        _group = root.gameObject.AddComponent<CanvasGroup>();
        _group.alpha = 0f;
        _group.blocksRaycasts = false;

        // Name label, above the bar.
        GameObject nameGO = CreateUIObject("Name", root);
        _nameText = nameGO.AddComponent<Text>();
        Font font = nameFont;
        if (font == null) font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        if (font == null) font = Resources.GetBuiltinResource<Font>("Arial.ttf");
        if (font == null) font = Font.CreateDynamicFontFromOSFont("Arial", nameFontSize);
        if (font == null) Debug.LogWarning("[BossHealthUI] No font resolved -- name text will be invisible.", this);
        _nameText.font = font;
        _nameText.fontSize = nameFontSize;
        _nameText.alignment = TextAnchor.MiddleCenter;
        _nameText.color = nameColor;
        _nameText.text = !string.IsNullOrEmpty(bossNameOverride)
            ? bossNameOverride
            : (bossBrain != null ? bossBrain.gameObject.name : (targetHealth != null ? targetHealth.gameObject.name : "Boss"));
        RectTransform nameRT = nameGO.GetComponent<RectTransform>();
        nameRT.anchorMin = new Vector2(0f, 1f);
        nameRT.anchorMax = new Vector2(1f, 1f);
        nameRT.pivot = new Vector2(0.5f, 1f);
        nameRT.anchoredPosition = Vector2.zero;
        nameRT.sizeDelta = new Vector2(0f, 24f);

        // Border / background frame for the bar.
        GameObject borderGO = CreateUIObject("Border", root);
        Image border = borderGO.AddComponent<Image>();
        border.color = borderColor;
        RectTransform borderRT = borderGO.GetComponent<RectTransform>();
        borderRT.anchorMin = new Vector2(0.5f, 1f);
        borderRT.anchorMax = new Vector2(0.5f, 1f);
        borderRT.pivot = new Vector2(0.5f, 1f);
        borderRT.anchoredPosition = new Vector2(0f, -28f);
        borderRT.sizeDelta = barSize;

        // Weakpoint glow ring -- larger than the border, sits behind it, hidden by default.
        GameObject pulseGO = CreateUIObject("WeakpointPulse", root);
        _borderPulse = pulseGO.AddComponent<Image>();
        _borderPulse.color = new Color(weakpointPulseColor.r, weakpointPulseColor.g, weakpointPulseColor.b, 0f);
        RectTransform pulseRT = pulseGO.GetComponent<RectTransform>();
        pulseRT.anchorMin = borderRT.anchorMin;
        pulseRT.anchorMax = borderRT.anchorMax;
        pulseRT.pivot = borderRT.pivot;
        pulseRT.anchoredPosition = borderRT.anchoredPosition;
        pulseRT.sizeDelta = barSize + new Vector2(borderThickness * 2f, borderThickness * 2f);
        pulseGO.SetActive(false);
        pulseGO.transform.SetSiblingIndex(borderGO.transform.GetSiblingIndex()); // draw behind the border

        // Inner fill area, inset from the border by borderThickness.
        GameObject fillAreaGO = CreateUIObject("FillArea", borderRT);
        _fillArea = fillAreaGO.GetComponent<RectTransform>();
        _fillArea.anchorMin = Vector2.zero;
        _fillArea.anchorMax = Vector2.one;
        _fillArea.offsetMin = new Vector2(borderThickness, borderThickness);
        _fillArea.offsetMax = new Vector2(-borderThickness, -borderThickness);

        Image bg = fillAreaGO.AddComponent<Image>();
        bg.color = backgroundColor;

        // Chip fill drains slowly behind the immediate fill.
        _chipFill = CreateFillImage("ChipFill", _fillArea, chipFillColor);
        // Immediate fill snaps instantly and sits on top.
        _immediateFill = CreateFillImage("ImmediateFill", _fillArea, normalFillColor);

        BuildPhaseDividers(_fillArea);
        BuildTokenBar(root, borderRT);
    }

    /// <summary>Thin mana-style bar hung under the health bar, sharing its frame
    /// language so the two read as one readout of the same boss.</summary>
    private void BuildTokenBar(RectTransform root, RectTransform healthBorderRT)
    {
        if (!showTokenBar || tokenPool == null) return;

        float width = Mathf.Max(1f, barSize.x - tokenBarInset * 2f);
        float y = healthBorderRT.anchoredPosition.y - barSize.y - tokenBarGap;
        // Border thickness is halved here: the full health-bar border around a
        // ~8px bar leaves almost no room for the fill itself.
        float thickness = Mathf.Min(borderThickness * 0.5f, tokenBarHeight * 0.25f);

        GameObject borderGO = CreateUIObject("TokenBorder", root);
        Image border = borderGO.AddComponent<Image>();
        border.color = borderColor;
        _tokenBorderRT = borderGO.GetComponent<RectTransform>();
        _tokenBorderRT.anchorMin = new Vector2(0.5f, 1f);
        _tokenBorderRT.anchorMax = new Vector2(0.5f, 1f);
        _tokenBorderRT.pivot = new Vector2(0.5f, 1f);
        _tokenBorderRT.anchoredPosition = new Vector2(0f, y);
        _tokenBorderRT.sizeDelta = new Vector2(width, tokenBarHeight);

        GameObject areaGO = CreateUIObject("TokenFillArea", _tokenBorderRT);
        RectTransform area = areaGO.GetComponent<RectTransform>();
        area.anchorMin = Vector2.zero;
        area.anchorMax = Vector2.one;
        area.offsetMin = new Vector2(thickness, thickness);
        area.offsetMax = new Vector2(-thickness, -thickness);

        Image bg = areaGO.AddComponent<Image>();
        bg.color = backgroundColor;

        _tokenFill = CreateFillImage("TokenFill", area, tokenFillColor);
        _tokenFill.fillAmount = 0f;

        BuildUltimateMarker(area);

        // Grow the root so the token bar is inside it — otherwise anything that
        // later lays this out by height (or a layout group added above it) would
        // clip the bar off the bottom.
        root.sizeDelta = new Vector2(barSize.x, barSize.y + 30f + tokenBarGap + tokenBarHeight);
    }

    private void BuildUltimateMarker(RectTransform tokenArea)
    {
        if (!showUltimateMarker || attackSelector == null) return;

        float at = attackSelector.ultimateCostThreshold / (float)tokenPool.Max;
        if (at <= 0f || at >= 1f) return; // off the end of the bar — nothing useful to draw

        GameObject go = CreateUIObject("UltimateMarker", tokenArea);
        Image img = go.AddComponent<Image>();
        img.color = new Color(tokenReadyColor.r, tokenReadyColor.g, tokenReadyColor.b, 0.85f);

        RectTransform rt = go.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(at, 0f);
        rt.anchorMax = new Vector2(at, 1f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = new Vector2(2f, 0f);
        rt.anchoredPosition = Vector2.zero;
    }

    private Image CreateFillImage(string name, Transform parent, Color color)
    {
        GameObject go = CreateUIObject(name, parent);
        RectTransform rt = go.GetComponent<RectTransform>();
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;

        Image img = go.AddComponent<Image>();
        img.sprite = GetWhiteSprite(); // required -- Image ignores fillAmount entirely with no sprite
        img.color = color;
        img.type = Image.Type.Filled;
        img.fillMethod = Image.FillMethod.Horizontal;
        img.fillOrigin = (int)Image.OriginHorizontal.Left;
        img.fillAmount = 1f;
        return img;
    }

    private static Sprite _whiteSprite;
    private static Sprite GetWhiteSprite()
    {
        if (_whiteSprite == null)
        {
            Texture2D tex = Texture2D.whiteTexture;
            _whiteSprite = Sprite.Create(tex, new Rect(0f, 0f, tex.width, tex.height), new Vector2(0.5f, 0.5f));
        }
        return _whiteSprite;
    }

    private void BuildPhaseDividers(RectTransform fillArea)
    {
        if (bossBrain == null || bossBrain.phaseHealthThresholds == null) return;

        foreach (float t in bossBrain.phaseHealthThresholds)
        {
            GameObject go = CreateUIObject("PhaseDivider", fillArea);
            Image img = go.AddComponent<Image>();
            img.color = new Color(0f, 0f, 0f, 0.6f);

            RectTransform rt = go.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(Mathf.Clamp01(t), 0f);
            rt.anchorMax = new Vector2(Mathf.Clamp01(t), 1f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(2f, 0f);
            rt.anchoredPosition = Vector2.zero;
        }
    }

    private GameObject CreateUIObject(string name, Transform parent)
    {
        GameObject go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        return go;
    }

    private Canvas FindOrCreateCanvas()
    {
        if (targetCanvas != null) return targetCanvas;

        Canvas existing = GetComponentInParent<Canvas>();
        if (existing != null) return existing;

        GameObject canvasGO = new GameObject("BossHealthCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        Canvas canvas = canvasGO.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        CanvasScaler scaler = canvasGO.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        return canvas;
    }

    #endregion

    [System.Diagnostics.Conditional("UNITY_EDITOR")]
    private void Log(string msg)
    {
        if (showDebugLogs) Debug.Log($"[BossHealthUI] {msg}", this);
    }
}
