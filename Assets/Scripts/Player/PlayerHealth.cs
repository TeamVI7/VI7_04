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

    float _hp;
    float _lastHitTime;
    bool _dead;
    Sequence _overlaySeq;

    public float HP  => _hp;
    public float Pct => _hp / maxHP;

    public static event System.Action<float, float> OnHealthChanged; // (current, max)
    public static event System.Action OnDied;

    void Awake()
    {
        _hp = maxHP;
        SetAlpha(damageOverlay, 0f);
    }

    void Update()
    {
        if (_dead || !canRegen || _hp >= maxHP) return;
        if (Time.time - _lastHitTime < regenDelay) return;

        _hp = Mathf.Min(_hp + regenRate * Time.deltaTime, maxHP);
        OnHealthChanged?.Invoke(_hp, maxHP);
    }

    public void TakeDamage(float dmg)
    {
        if (_dead) return;

        _hp = Mathf.Clamp(_hp - dmg, 0f, maxHP);
        _lastHitTime = Time.time;

        OnHealthChanged?.Invoke(_hp, maxHP);
        FlashOverlay(dmg);

        if (_hp <= 0f) Die();
    }

    public void Heal(float amount)
    {
        if (_dead) return;
        
        _hp = Mathf.Min(_hp + amount, maxHP);
        OnHealthChanged?.Invoke(_hp, maxHP);
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