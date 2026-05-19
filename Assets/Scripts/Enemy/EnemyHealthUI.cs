using UnityEngine;
using UnityEngine.UI;

public class EnemyHealthUI : MonoBehaviour
{
    [SerializeField] private Slider healthSlider;
    private OutOfBullet.Enemy.EnemyHealth enemyHealth;

    private void Start()
    {
        // Tìm EnemyHealth ở object cha
        enemyHealth = GetComponentInParent<OutOfBullet.Enemy.EnemyHealth>();
        
        if (enemyHealth != null)
        {
            // Khởi tạo giá trị Slider ban đầu
            healthSlider.maxValue = enemyHealth.MaxHP;
            healthSlider.value = enemyHealth.CurrentHP;

            // Đăng ký nhận sự kiện khi Enemy bị trừ máu
            enemyHealth.OnHealthChanged += UpdateHealthBar;
        }
    }

    private void UpdateHealthBar(float currentHP)
    {
        healthSlider.value = currentHP;
    }

    private void OnDestroy()
    {
        if (enemyHealth != null)
        {
            enemyHealth.OnHealthChanged -= UpdateHealthBar;
        }
    }

    private void LateUpdate()
    {
        // Mẹo nhỏ: Giúp thanh máu luôn quay mặt về phía Camera của Player để không bị lật ngược khi Enemy di chuyển
        if (Camera.main != null)
        {
            transform.LookAt(transform.position + Camera.main.transform.rotation * Vector3.forward,
                             Camera.main.transform.rotation * Vector3.up);
        }
    }
}