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

    /// <summary>
    /// Raised with the world position the damage came from, for DamageDirectionHUD.
    ///
    /// Only fires for damage that actually has a direction. Sources with no meaningful
    /// bearing — killzones, the wire-puzzle shock, debug damage, bleed damage-over-time —
    /// call the one-argument TakeDamage and deliberately raise nothing, so the player
    /// never gets an arc pointing at something they cannot turn to face.
    /// </summary>
    public static event System.Action<Vector3> OnDamagedFrom;

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

    /// <summary>Damage with no direction — traps, hazards, bleed. Fires no damage-direction arc.</summary>
    public void TakeDamage(float dmg) => TakeDamage(dmg, null);

    /// <summary>
    /// Damage from a known world position — an enemy, a projectile, a blast centre.
    /// Raises OnDamagedFrom so DamageDirectionHUD can point at it.
    /// </summary>
    public void TakeDamage(float dmg, Vector3 sourcePosition) => TakeDamage(dmg, (Vector3?)sourcePosition);

    /// <summary>
    /// The real implementation. sourcePosition is nullable rather than defaulting to
    /// Vector3.zero because zero is a perfectly valid world position — a sourceless hit
    /// defaulting to it would aim every trap and debug hit at the map origin.
    /// </summary>
    private void TakeDamage(float dmg, Vector3? sourcePosition)
    {
        if (_dead) return;

        _hp = Mathf.Clamp(_hp - dmg, 0f, maxHP);
        _lastHitTime = Time.time;

        OnHealthChanged?.Invoke(_hp, maxHP);
        FlashOverlay(dmg);

        if (sourcePosition.HasValue) OnDamagedFrom?.Invoke(sourcePosition.Value);

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

    // Full-heal respawn. Kept for callers that want a clean slate rather than a
    // restored one — the checkpoint path goes through RestoreHealth instead.
    public void Respawn() => RestoreHealth(maxHP);

    /// <summary>
    /// Puts health back to a specific value and lifts the dead flag — the restore half
    /// of a checkpoint rewind.
    ///
    /// Clearing _dead here is the important part: it is what re-opens TakeDamage and
    /// regen, both of which early-out while dead. A restore that only wrote _hp would
    /// hand back a player at full health who could never be hurt again.
    /// </summary>
    public void RestoreHealth(float hp)
    {
        _dead = false;
        _hp   = Mathf.Clamp(hp, 0f, maxHP);

        // Otherwise the next tick of regen counts the entire time the player spent dead
        // as "time since last hit" — or doesn't, depending on how long the fade ran.
        _lastHitTime = Time.time;

        _overlaySeq?.Kill();
        SetAlpha(damageOverlay, 0f);

        OnHealthChanged?.Invoke(_hp, maxHP);
    }

    static void SetAlpha(Image img, float a)
    {
        if (!img) return;
        Color c = img.color; c.a = a; img.color = c;
    }
}