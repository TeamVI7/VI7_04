// ============================================================
//  AcidProjectile.cs  —  Out of Bullet
//  Viên acid bay từ Enemy đến Player (Có tính năng HOMING khóa mục tiêu).
// ============================================================

using UnityEngine;
using OutOfBullet.Player;

namespace OutOfBullet.Enemy
{
    public class AcidProjectile : MonoBehaviour
    {
        [Header("Stats")]
        public float Damage   = 10f;
        public float Speed    = 12f;
        public float LifeTime = 3f;

        [Header("Trajectory (Legacy)")]
        public bool UseGravity = false;
        public float GravityScale = 1.5f;

        [Header("VFX")]
        [Tooltip("Gắn particle GoopSpray vào đây")]
        public GameObject HitEffectPrefab; 

        [Header("Homing Settings")]
        [Tooltip("Tốc độ bẻ lái đuổi theo Player. Càng cao đạn lượn càng gắt.")]
        public float HomingStrength = 5f;

        [Tooltip("Khoảng thời gian (giây) đạn bay tự do trước khi bắt đầu đuổi theo Player.")]
        public float DelayHomingTime = 0.4f;

        private Transform _playerTarget;
        private Rigidbody _rb;
        private float     _spawnTime;
        private bool      _hit;

        public void Init(Vector3 direction)
        {
            _rb = GetComponent<Rigidbody>();
            _spawnTime = Time.time;

            if (_rb != null)
            {
                // Cho phép bộ vật lý hoạt động lúc đầu để tạo đà vòm cung
                _rb.isKinematic = false;
                _rb.useGravity = true;
                _rb.AddForce(direction.normalized * Speed, ForceMode.VelocityChange);
            }

            // Tự động tìm Player dựa vào Tag "Player" trong Scene
            GameObject player = GameObject.FindGameObjectWithTag("Player");
            if (player != null)
            {
                _playerTarget = player.transform;
            }

            Destroy(gameObject, LifeTime);
        }

        private void FixedUpdate()
        {
            if (_hit || _playerTarget == null || _rb == null) return;

            // Đợi một khoảng thời gian ngắn cho đạn bay vòm lên trời rồi mới bắt đầu bẻ lái đuổi theo
            if (Time.time - _spawnTime > DelayHomingTime)
            {
                // Tắt trọng lực để đạn không bị kéo ghì xuống đất, tập trung lao vào Player
                _rb.useGravity = false;

                // Tính toán hướng từ vị trí hiện tại của đạn đến Player
                Vector3 targetDirection = (_playerTarget.position - transform.position).normalized;

                // Tính toán vận tốc mong muốn đuổi theo mục tiêu
                Vector3 desiredVelocity = targetDirection * Speed;

                // Dùng hàm Lerp để bẻ lái dần dần mượt mà hướng đi hiện tại sang hướng mong muốn
                _rb.linearVelocity = Vector3.Lerp(_rb.linearVelocity, desiredVelocity, HomingStrength * Time.fixedDeltaTime);
            }

            // Xoay đầu viên đạn theo hướng vector vận tốc thực tế
            if (_rb.linearVelocity != Vector3.zero)
            {
                transform.rotation = Quaternion.LookRotation(_rb.linearVelocity);
            }
        }

        private void OnTriggerEnter(Collider other)
        {
            if (_hit) return;

            // Bỏ qua va chạm với quái hoặc khiên
            if (other.CompareTag("Enemy") || other.name.Contains("Shield") || other.name.Contains("Enemy")) 
                return;

            _hit = true;

            var health = other.GetComponentInParent<PlayerHealth>();
            if (health != null)
            {
                health.TakeDamage(Damage);
            }

            // Sinh hiệu ứng nổ Acid GoopSpray tại chỗ va chạm
            SpawnEffect(HitEffectPrefab);

            Destroy(gameObject);
        }

        private void SpawnEffect(GameObject prefab)
        {
            if (prefab != null)
            {
                var fx = Instantiate(prefab, transform.position, Quaternion.identity);
                Destroy(fx, 2f);
            }
        }
    }
}