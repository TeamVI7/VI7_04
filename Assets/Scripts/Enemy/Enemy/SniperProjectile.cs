using UnityEngine;

namespace Enemy
{
    public class SniperProjectile : MonoBehaviour
    {
        [Header("Stats")]
        public float Damage          = 25f;
        public float Speed           = 45f; 
        public float LifeTime        = 4f;

        [Header("VFX")]
        public GameObject HitEffectPrefab;

        [Header("Collision")]
        public LayerMask HitLayers   = ~0;

        [Header("Near-Miss Custom Settings")]
        [Tooltip("Bán kính sượt qua người để kích hoạt tiếng rít gió (Mét)")]
        public float WhizRadius = 3.5f;

        private Vector3 _direction;
        private bool _isInitialized;
        private bool _hit;

        // Quản lý dữ liệu âm thanh
        private AudioClip _explosionClip;
        private AudioClip _whizClip;
        private Transform _playerTransform;
        private bool _hasPlayedWhiz;

        // Hàm nhận đồng thời 2 file âm thanh từ EnemyAudio chuyển sang
        public void AssignSniperClips(AudioClip impactClip, AudioClip whizClip)
        {
            _explosionClip = impactClip;
            _whizClip = whizClip;
        }

        public void Init(Vector3 direction, float damageFromStats)
        {
            Damage = damageFromStats;
            _direction = direction.normalized;
            _isInitialized = true;
            _hit = false;
            _hasPlayedWhiz = false;

            // Tìm vị trí của Player thời gian thực để đo khoảng cách sượt gió
            GameObject playerObj = GameObject.FindWithTag("Player");
            if (playerObj != null)
            {
                _playerTransform = playerObj.transform;
            }

            if (TryGetComponent(out Rigidbody rb))
            {
                rb.isKinematic = true;
            }

            if (_direction != Vector3.zero)
                transform.rotation = Quaternion.LookRotation(_direction);

            Destroy(gameObject, LifeTime);
        }

        private void Update()
        {
            if (!_isInitialized || _hit) return;

            // Tịnh tiến đạn thẳng tắp
            transform.position += _direction * Speed * Time.deltaTime;

            // THÊM MỚI: Logic tính toán kiểm tra rít gió sượt người
            CheckForNearMissWhiz();
        }

        private void CheckForNearMissWhiz()
        {
            if (_hasPlayedWhiz || _whizClip == null || _playerTransform == null) return;

            // Tính khoảng cách bình phương (sqrMagnitude) từ đạn đến Player để tối ưu CPU vượt trội
            Vector3 offset = _playerTransform.position + Vector3.up * 1f - transform.position; // Lấy mốc tầm ngực/tai Player
            float sqrDistance = offset.sqrMagnitude;

            // Nếu lọt vào bán kính WhizRadius (Bình phương lên để so sánh)
            if (sqrDistance <= WhizRadius * WhizRadius)
            {
                _hasPlayedWhiz = true; // Khóa ngay lập tức, chỉ phát duy nhất 1 lần khi đạn lướt qua

                // Tạo Object ảo phát tiếng rít gió 2D trực diện vào tai Player
                GameObject whizDummy = new GameObject("Sniper_Whiz_Sound_Dummy");
                whizDummy.transform.position = transform.position;
                
                AudioSource src = whizDummy.AddComponent<AudioSource>();
                src.spatialBlend = 0f; // Âm thanh 2D toàn dải gây giật mình
                src.clip = _whizClip;
                src.volume = 0.9f;
                src.Play();

                Destroy(whizDummy, _whizClip.length + 0.1f);
            }
        }

        private void OnTriggerEnter(Collider other)
        {
            if (_hit) return;
            if (other.CompareTag("Enemy")) return; 
            if ((HitLayers.value & (1 << other.gameObject.layer)) == 0) return; 

            _hit = true;

            // Kích nổ âm thanh tác động đanh thép ngay tại điểm va chạm
            PlayImpactSoundAtReceiver();

            if (other.TryGetComponent(out IDamageable damageable))
            {
                damageable.TakeDamage(Damage, _direction, transform.position);
            }
            else
            {
                other.GetComponentInParent<PlayerHealth>()?.TakeDamage(Damage);
            }

            if (HitEffectPrefab != null)
            {
                var fx = Instantiate(HitEffectPrefab, transform.position, Quaternion.identity);
                Destroy(fx, 2f);
            }

            Destroy(gameObject);
        }

        private void PlayImpactSoundAtReceiver()
        {
            if (_explosionClip == null) return;

            GameObject audioDummy = new GameObject("Sniper_Impact_Sound_Dummy");
            audioDummy.transform.position = transform.position;

            AudioSource audioSrc = audioDummy.AddComponent<AudioSource>();
            audioSrc.spatialBlend = 0f; // 2D Audio sát tai
            audioSrc.clip = _explosionClip;
            audioSrc.volume = 1.0f; 
            audioSrc.Play();

            Destroy(audioDummy, _explosionClip.length + 0.1f);
        }
    }
}