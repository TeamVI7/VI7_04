using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class HealthStaminaHUD : MonoBehaviour
{
    [Header("References")]
    public PlayerMovement playerMovement;

    [Header("Health")]
    public Image healthFill;
    public TMP_Text healthText;
    public Color healthColor = new Color(0.8f, 0.15f, 0.15f);
    public Color lowHealthColor = new Color(1f, 0.1f, 0.1f);
    public float lowHealthThreshold = 0.3f;
    public float lowHealthPulseSpeed = 4f;

    [Header("Stamina")]
    public Image staminaFill;
    public TMP_Text staminaText;
    public CanvasGroup staminaGroup;
    public float staminaHideDelay = 1.5f;
    public float staminaFadeSpeed = 4f;

    [Header("Smoothing")]
    public float fillLerpSpeed = 8f;

    float _targetHealthPct = 1f;
    float _targetStaminaPct = 1f;
    float _lastStaminaChangeTime;
    bool _staminaFull = true;

    void OnEnable()
    {
        PlayerHealth.OnHealthChanged += HandleHealthChanged;
        PlayerHealth.OnDied += HandleDied;

        if (playerMovement != null)
            playerMovement.OnStaminaChanged += HandleStaminaChanged;

        if (PlayerHealth.Instance != null)
            HandleHealthChanged(PlayerHealth.Instance.HP, PlayerHealth.Instance.MaxHP);

        if (playerMovement != null)
            HandleStaminaChanged(playerMovement.Stamina, playerMovement.maxStamina);
    }

    void OnDisable()
    {
        PlayerHealth.OnHealthChanged -= HandleHealthChanged;
        PlayerHealth.OnDied -= HandleDied;

        if (playerMovement != null)
            playerMovement.OnStaminaChanged -= HandleStaminaChanged;
    }

    void HandleHealthChanged(float current, float max)
    {
        _targetHealthPct = max > 0f ? current / max : 0f;
        if (healthText != null)
            healthText.text = $"{Mathf.CeilToInt(current)} / {Mathf.CeilToInt(max)}";
    }

    void HandleDied()
    {
        _targetHealthPct = 0f;
    }

    void HandleStaminaChanged(float current, float max)
    {
        _targetStaminaPct = max > 0f ? current / max : 0f;
        if (staminaText != null)
            staminaText.text = $"{Mathf.CeilToInt(current)} / {Mathf.CeilToInt(max)}";

        _lastStaminaChangeTime = Time.time;
        _staminaFull = current >= max;
    }

    void Update()
    {
        if (healthFill != null)
        {
            healthFill.fillAmount = Mathf.Lerp(healthFill.fillAmount, _targetHealthPct, Time.deltaTime * fillLerpSpeed);
            healthFill.color = _targetHealthPct <= lowHealthThreshold
                ? Color.Lerp(healthColor, lowHealthColor, (Mathf.Sin(Time.time * lowHealthPulseSpeed) + 1f) * 0.5f)
                : healthColor;
        }

        if (staminaFill != null)
            staminaFill.fillAmount = Mathf.Lerp(staminaFill.fillAmount, _targetStaminaPct, Time.deltaTime * fillLerpSpeed);

        if (staminaGroup != null)
        {
            bool shouldShow = !_staminaFull || Time.time - _lastStaminaChangeTime < staminaHideDelay;
            float targetAlpha = shouldShow ? 1f : 0f;
            staminaGroup.alpha = Mathf.MoveTowards(staminaGroup.alpha, targetAlpha, Time.deltaTime * staminaFadeSpeed);
        }
    }
}