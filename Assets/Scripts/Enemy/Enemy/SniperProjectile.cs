using UnityEngine;

namespace Enemy
{
    public class SniperProjectile : MonoBehaviour
    {
        [Header("Stats")]
        public float Damage          = 25f;
        public float Speed           = 45f; // Tốc độ bay thẳng tắp cực nhanh của bắn tỉa
        public float LifeTime        = 4f;

        [Header("VFX")]
        public GameObject HitEffectPrefab;

        [Header("Collision")]
        public LayerMask HitLayers   = ~0;

        private Vector3 _direction;
        private bool _isInitialized;
        private bool _hit;

        public void Init(Vector3 direction, float damageFromStats)
        {
            // Đồng bộ damage truyền qua từ Stats SO thông qua hành vi của StealthEnemy
            Damage = damageFromStats;
            _direction = direction.normalized;
            _isInitialized = true;
            _hit = false;

            // Đạn bắn tỉa bay thẳng tắp không cần dùng Rigidbody vật lý/trọng lực để tránh bị trĩu xuống
            if (TryGetComponent(out Rigidbody rb))
            {
                rb.isKinematic = true;
            }

            // Xoay viên đạn hướng thẳng về mục tiêu ngay khi bay ra
            if (_direction != Vector3.zero)
                transform.rotation = Quaternion.LookRotation(_direction);

            Destroy(gameObject, LifeTime);
        }

        private void Update()
        {
            if (!_isInitialized || _hit) return;

            // Tịnh tiến thẳng tắp với vận tốc cao
            transform.position += _direction * Speed * Time.deltaTime;
        }

        private void OnTriggerEnter(Collider other)
        {
            if (_hit) return;
            if (other.CompareTag("Enemy")) return; // Không bắn nhầm đồng đội
            if ((HitLayers.value & (1 << other.gameObject.layer)) == 0) return; // Chỉ va chạm layer được phép

            _hit = true;

            // Gây sát thương cho Player (Dùng IDamageable hoặc PlayerHealth tùy hệ thống của cậu)
            if (other.TryGetComponent(out IDamageable damageable))
            {
                damageable.TakeDamage(Damage, _direction, transform.position);
            }
            else
            {
                other.GetComponentInParent<PlayerHealth>()?.TakeDamage(Damage);
            }

            // Spawn hiệu ứng nổ/va chạm tại điểm trúng đích
            if (HitEffectPrefab != null)
            {
                var fx = Instantiate(HitEffectPrefab, transform.position, Quaternion.identity);
                Destroy(fx, 2f);
            }

            Destroy(gameObject);
        }
    }
}