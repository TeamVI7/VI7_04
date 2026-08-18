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
    [SerializeField] Image damageOverlay;
    [SerializeField] float overlayMaxAlpha = 0.5f;
    [SerializeField] float overlayFadeOut = 1f;

    float _hp;
    float _lastHitTime;
    bool _dead;
    Sequence _overlaySeq;
    Rigidbody _rb;

    public float HP    => _hp;
    public float Pct   => _hp / maxHP;
    public float MaxHP => maxHP;

    // Static reference — enemies read these instead of FindGameObjectWithTag
    public static Transform    Transform { get; private set; }
    public static PlayerHealth Instance  { get; private set; }

    public static event System.Action<float, float> OnHealthChanged; // (current, max)
    public static event System.Action OnDied;

    void Awake()
    {
        Transform = transform;
        Instance  = this;
        _rb = GetComponent<Rigidbody>();

        _hp = maxHP;
        SetAlpha(damageOverlay, 0f);
    }

    void OnDestroy()
    {
        Transform = null;
        Instance  = null;
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

    // PlayerMovement drives movement with rb.AddForce, so a plain impulse here
    // composes with it the same way — no separate "external velocity" channel needed.
    public void ApplyKnockback(Vector3 force)
    {
        if (_dead || _rb == null) return;
        _rb.AddForce(force, ForceMode.Impulse);
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

    // Called by DeathCamera.Respawn() once checkpoint teleport completes.
    public void Respawn()
    {
        _dead = false;
        _hp = maxHP;
        SetAlpha(damageOverlay, 0f);
        OnHealthChanged?.Invoke(_hp, maxHP);
    }

    static void SetAlpha(Image img, float a)
    {
        if (!img) return;
        Color c = img.color; c.a = a; img.color = c;
    }
}