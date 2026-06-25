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

        // THÊM MỚI: Biến cache lưu trữ file âm thanh nổ được truyền sang từ quái
        private AudioClip _explosionClip;

        // THÊM MỚI: Hàm nhận file âm thanh từ EnemyAudio gọi qua
        public void AssignExplosionClip(AudioClip clip)
        {
            _explosionClip = clip;
        }

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

            // THÊM MỚI: Kích nổ âm thanh to rõ ngay tại vị trí va chạm (Sát tai Player) trước khi xóa đạn
            PlayImpactSoundAtReceiver();

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

        // THÊM MỚI: Hàm sinh Object phát âm thanh độc lập tại điểm đạn chạm trúng đích
        private void PlayImpactSoundAtReceiver()
        {
            if (_explosionClip == null) return;

            // Tạo một GameObject ảo ngay tại tọa độ va chạm thời gian thực của viên đạn
            GameObject audioDummy = new GameObject("Sniper_Impact_Sound_Dummy");
            audioDummy.transform.position = transform.position;

            AudioSource audioSrc = audioDummy.AddComponent<AudioSource>();
            
            // THIẾT QUÂN LUẬT: Ép spatialBlend = 0f biến thành âm thanh 2D toàn dải
            // Đảm bảo dù quái ở cách xa 100m, đạn chạm cạnh người Player nghe vẫn to, đanh và giật mình
            audioSrc.spatialBlend = 0f; 
            audioSrc.clip = _explosionClip;
            audioSrc.volume = 1.0f; 
            audioSrc.Play();

            // Tự động xóa dọn rác Object âm thanh ảo này sau khi phát xong tiếng nổ
            Destroy(audioDummy, _explosionClip.length + 0.1f);
        }
    }
}