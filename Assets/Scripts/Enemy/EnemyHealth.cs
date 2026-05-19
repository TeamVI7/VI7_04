using UnityEngine;

namespace OutOfBullet.Enemy
{
    public class EnemyHealth : MonoBehaviour
    {
        [Header("Stats")]
        [SerializeField] private float maxHP = 100f;
        private float currentHP;

        public float CurrentHP => currentHP;
        public float MaxHP => maxHP;

        // Event này dùng để báo cho thanh UI biết khi nào máu thay đổi (chúng ta sẽ dùng ở Bước 3)
        public System.Action<float> OnHealthChanged;

        private void Awake()
        {
            currentHP = maxHP;
        }

        public void TakeDamage(float damage)
        {
            if (currentHP <= 0) return;

            currentHP -= damage;
            currentHP = Mathf.Clamp(currentHP, 0f, maxHP);

            // Kích hoạt event cập nhật UI
            OnHealthChanged?.Invoke(currentHP);

            Debug.Log($"[Combat] Player hit Enemy! Enemy HP: {currentHP}/{maxHP}");

            if (currentHP <= 0)
            {
                Die();
            }
        }

        private void Die()
        {
            Debug.Log("[Combat] Enemy đã bị tiêu diệt!");
            // Sau này cậu có thể gọi hàm Ragdoll hoặc nổ hiệu ứng ở đây
            // Tạm thời ẩn Enemy đi khi chết
            gameObject.SetActive(false); 
        }
    }
}