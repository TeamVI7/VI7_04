// ============================================================
//  EnemyHealthUI.cs  —  Out of Bullet
//  Hiển thị thanh máu Slider trên đầu Enemy.
//  FIX: Đọc đúng MaxHP/CurrentHP sau khi EnemyBase.Awake() chạy xong.
// ============================================================
using UnityEngine;
using UnityEngine.UI;
using OutOfBullet.Enemy;

public class EnemyHealthUI : MonoBehaviour
{
    [SerializeField] private Slider healthSlider;
    private EnemyBase _enemyBase;

    private void Start()
    {
        _enemyBase = GetComponentInParent<EnemyBase>();

        if (_enemyBase == null)
        {
            Debug.LogWarning("[EnemyHealthUI] Không tìm thấy EnemyBase ở object cha!");
            return;
        }

        healthSlider.maxValue = _enemyBase.MaxHP;
        healthSlider.value    = _enemyBase.CurrentHP;

        Debug.Log($"[EnemyHealthUI] Init — MaxHP: {_enemyBase.MaxHP}  CurrentHP: {_enemyBase.CurrentHP}");

        _enemyBase.OnHealthChanged += UpdateHealthBar;
    }

    private void UpdateHealthBar(float currentHP)
    {
        if (healthSlider != null)
            healthSlider.value = currentHP;
    }

    private void OnDestroy()
    {
        if (_enemyBase != null)
            _enemyBase.OnHealthChanged -= UpdateHealthBar;
    }

    private void LateUpdate()
    {
        if (Camera.main != null)
        {
            transform.LookAt(
                transform.position + Camera.main.transform.rotation * Vector3.forward,
                Camera.main.transform.rotation * Vector3.up);
        }
    }
}