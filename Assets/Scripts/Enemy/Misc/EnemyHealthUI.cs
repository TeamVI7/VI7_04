using UnityEngine;
using UnityEngine.UI;

public class EnemyHealthUI : MonoBehaviour
{
    [SerializeField] private Slider healthSlider;
    private EnemyBase _enemyBase;

    private void Start()
    {
        _enemyBase = GetComponentInParent<EnemyBase>();
        if (_enemyBase == null) return;

        healthSlider.maxValue = _enemyBase.MaxHP;
        healthSlider.value = _enemyBase.CurrentHP;

        _enemyBase.OnHealthChanged += UpdateHealthBar;
    }

    private void UpdateHealthBar(float currentHP)
    {
        if (healthSlider != null) healthSlider.value = currentHP;
    }

    private void OnDestroy()
    {
        if (_enemyBase != null) _enemyBase.OnHealthChanged -= UpdateHealthBar;
    }

    private void LateUpdate()
    {
        if (Camera.main != null)
        {
            transform.LookAt(transform.position + Camera.main.transform.rotation * Vector3.forward,
                             Camera.main.transform.rotation * Vector3.up);
        }
    }
}