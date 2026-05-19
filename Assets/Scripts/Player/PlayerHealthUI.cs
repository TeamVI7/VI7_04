// ============================================================
//  PlayerHealthUI.cs  —  Out of Bullet
//  Lắng nghe PlayerHealthChangedEvent từ EventBus để cập nhật Slider UI.
// ============================================================
using UnityEngine;
using UnityEngine.UI;
using OutOfBullet.Core; // Để dùng được EventBus

namespace OutOfBullet.UI
{
    public class PlayerHealthUI : MonoBehaviour
    {
        [Header("UI Components")]
        [SerializeField] private Slider healthSlider;

        private void OnEnable()
        {
            // Đăng ký lắng nghe sự kiện thay đổi máu từ hệ thống
            EventBus.Subscribe<PlayerHealthChangedEvent>(OnPlayerHealthChanged);
        }

        private void OnDisable()
        {
            // Hủy đăng ký khi UI bị ẩn hoặc phá hủy để tránh tràn bộ nhớ
            EventBus.Unsubscribe<PlayerHealthChangedEvent>(OnPlayerHealthChanged);
        }

        private void Start()
        {
            // Đảm bảo lúc vào game, nếu chưa có event nào bắn ra thì slider vẫn ở mức tối đa
            if (healthSlider != null)
            {
                healthSlider.maxValue = 1f;
                healthSlider.value = 1f;
            }
        }

        private void OnPlayerHealthChanged(PlayerHealthChangedEvent evt)
        {
            if (healthSlider == null) return;

            // Tính toán tỷ lệ phần trăm máu (0f đến 1f)
            float healthFraction = evt.CurrentHP / evt.MaxHP;
            
            // Cập nhật giá trị trực tiếp lên thanh Slider
            healthSlider.value = healthFraction;
        }
    }
}