using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// World-space health bar. Subscribes to EnemyHealth events.
/// </summary>
[RequireComponent(typeof(EnemyHealth))]
public class EnemyHealthUI : MonoBehaviour
{
    [SerializeField] private Slider _slider;

    private void Start()
    {
        var health = GetComponent<EnemyHealth>();
        _slider.maxValue = health.MaxHP;
        _slider.value    = health.CurrentHP;
        health.OnDamaged += (current, _) => _slider.value = current;
        health.OnHealed  += current       => _slider.value = current;
    }

    private void LateUpdate()
    {
        if (Camera.main == null) return;
        transform.LookAt(transform.position + Camera.main.transform.rotation * Vector3.forward,
                         Camera.main.transform.rotation * Vector3.up);
    }
}