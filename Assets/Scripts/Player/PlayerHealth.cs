using UnityEngine;
using UnityEngine.UI;
using DG.Tweening;

public class PlayerHealth : MonoBehaviour
{
    [Header("Stats")]
    [SerializeField] float maxHP = 100f;
    [SerializeField] float regenDelay = 3f;
    [SerializeField] float regenRate = 15f;
    [SerializeField] bool canRegen = false;

    [Header("Overlay")]
    [SerializeField] Image damageOverlay;       // full-screen red Image on Canvas
    [SerializeField] float overlayMaxAlpha = 0.5f;
    [SerializeField] float overlayFadeOut = 1f;

    [Header("HUD")]
    [SerializeField] CanvasGroup healthHUD;     // CanvasGroup wrapping health bar
    [SerializeField] Slider healthSlider;
    [SerializeField] Image healthFill;
    [SerializeField] float hudShowDuration = 2f;
    [SerializeField] float hudFadeDuration = 0.4f;

    static readonly Color _green  = Color.green;
    static readonly Color _yellow = Color.yellow;
    static readonly Color _red    = Color.red;
    float _hp;
    float _lastHitTime;
    bool _dead;
    Sequence _overlaySeq;
    Sequence _hudSeq;

    public float HP  => _hp;
    public float Pct => _hp / maxHP;

    public static event System.Action<float, float> OnHealthChanged; // (current, max)
    public static event System.Action OnDied;

    void Awake()
    {
        _hp = maxHP;
        SetAlpha(damageOverlay, 0f);
        if (healthHUD)    healthHUD.alpha = 0f;
        if (healthSlider) healthSlider.value = 1f;
    }

    void SyncSlider()
    {
        if (healthSlider) healthSlider.value = _hp / maxHP;
        if (healthFill)   healthFill.color   = GetHealthColor(_hp / maxHP);
    }

    static Color GetHealthColor(float pct)
    {
        if (pct > 0.5f) return Color.Lerp(_yellow, _green, (pct - 0.5f) * 2f);
        else            return Color.Lerp(_red,    _yellow,  pct         * 2f);
    }

    void Update()
    {
        if (_dead || !canRegen || _hp >= maxHP) return;
        if (Time.time - _lastHitTime < regenDelay) return;

        _hp = Mathf.Min(_hp + regenRate * Time.deltaTime, maxHP);
        SyncSlider();
        OnHealthChanged?.Invoke(_hp, maxHP);
    }

    public void TakeDamage(float dmg)
    {
        if (_dead) return;

        _hp = Mathf.Clamp(_hp - dmg, 0f, maxHP);
        _lastHitTime = Time.time;

        SyncSlider();
        OnHealthChanged?.Invoke(_hp, maxHP);
        FlashOverlay(dmg);
        ShowHUD();

        if (_hp <= 0f) Die();
    }

    public void Heal(float amount)
    {
        if (_dead) return;
        _hp = Mathf.Min(_hp + amount, maxHP);
        SyncSlider();
        OnHealthChanged?.Invoke(_hp, maxHP);
        ShowHUD();
    }

    void FlashOverlay(float dmg)
    {
        if (!damageOverlay) return;
        float peak = Mathf.Max(dmg / maxHP * overlayMaxAlpha, 0.15f);

        _overlaySeq?.Kill();
        _overlaySeq = DOTween.Sequence()
            .Append(damageOverlay.DOFade(peak, 0.05f))
            .Append(damageOverlay.DOFade(0f, overlayFadeOut).SetEase(Ease.OutQuad));
    }

    void ShowHUD()
    {
        if (!healthHUD) return;

        _hudSeq?.Kill();
        _hudSeq = DOTween.Sequence()
            .Append(healthHUD.DOFade(1f, 0.08f))
            .AppendInterval(hudShowDuration)
            .Append(healthHUD.DOFade(0f, hudFadeDuration).SetEase(Ease.InQuad));
    }

    void Die()
    {
        _dead = true;
        OnDied?.Invoke();
    }

    static void SetAlpha(Image img, float a)
    {
        if (!img) return;
        Color c = img.color; c.a = a; img.color = c;
    }
}