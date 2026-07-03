using UnityEngine;
using UnityEngine.UI;
using DG.Tweening;
using System.Collections; // Đã thêm để chạy Coroutine dính sát thương theo thời gian

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

    [Header("Burning VFX Custom Settings")]
    [Tooltip("Kéo Object VFX Cháy (đang là con của Player) vào đây")]
    [SerializeField] GameObject burningVFX;

    float _hp;
    float _lastHitTime;
    bool _dead;
    Sequence _overlaySeq;
    private bool _isBurning = false; // Flag kiểm tra trạng thái cháy

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

    // --- LOGIC MỚI: Kích hoạt trạng thái cháy từ bên ngoài ---
    public void ApplyBurning()
    {
        if (_dead) return;

        // Nếu đang cháy rồi thì tự reset thời gian bằng cách ngắt Coroutine cũ và chạy lại
        if (_isBurning)
        {
            StopCoroutine("BurningRoutine");
        }

        StartCoroutine(BurningRoutine(4f, 10f)); // 4 giây, tổng mất 10 HP từ từ
    }

    private IEnumerator BurningRoutine(float duration, float totalDamage)
    {
        _isBurning = true;

        // Bật hiệu ứng VFX Cháy
        if (burningVFX != null) burningVFX.SetActive(true);

        float timer = 0f;
        float damagePerSecond = totalDamage / duration; // Tính lượng máu giảm mỗi giây (2.5 HP/s)

        while (timer < duration)
        {
            if (_dead) break;

            // Giảm máu mượt mà dựa theo deltaTime thay vì giật cục theo từng giây
            _hp = Mathf.Clamp(_hp - (damagePerSecond * Time.deltaTime), 0f, maxHP);
            OnHealthChanged?.Invoke(_hp, maxHP);

            if (_hp <= 0f)
            {
                Die();
                break;
            }

            timer += Time.deltaTime;
            yield return null; // Chờ sang khung hình tiếp theo
        }

        // Tắt hiệu ứng VFX Cháy sau khi hết thời gian
        if (burningVFX != null) burningVFX.SetActive(false);

        _isBurning = false;
    }
}