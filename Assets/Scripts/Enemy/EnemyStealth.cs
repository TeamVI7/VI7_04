using UnityEngine;
using OutOfBullet.Player; // Đảm bảo gọi đúng namespace chứa PlayerHealth của nhóm cậu

namespace OutOfBullet.Enemy
{
    public class EnemyStealth : EnemyFodder
    {
        public enum StealthState { Visible, Cloaked }

        [Header("Stealth Loop Settings")]
        public float VisibleDuration = 3f;  // Thời gian hiện màu gốc
        public float CloakedDuration = 20f; // Thời gian tàng hình

        [Header("Render & Material Settings")]
        [Tooltip("Kéo Object con tên 'Cube' (Thân nhện) vào đây.")]
        public Renderer EnemyRenderer; 

        [Tooltip("Kéo file asset 'Mat_Stealth' (Đã chỉnh sang Transparent) vào đây.")]
        public Material StealthMaterial;

        [Tooltip("Tốc độ mờ dần khi chuyển đổi tàng hình.")]
        public float FadeSpeed = 2f;

        [Header("Cận Chiến / Sát Thương Áp Sát (Giống Nhện Heavy)")]
        [Tooltip("Khoảng cách đứng gần Player để bắt đầu gây sát thương.")]
        public float DamageRange = 2f; 
        
        [Tooltip("Sát thương gây ra cho Player mỗi giây.")]
        public float DamagePerSecond = 15f; 

        // Các biến xử lý ngầm
        private Material _originalMaterial; 
        private Material _runtimeStealthMat; 
        private float _targetAlpha = 1f;
        private float _currentAlpha = 1f;

        private StealthState _currentState = StealthState.Visible;
        private float _stateTimer = 0f;

        protected override void Awake()
        {
            base.Awake(); 
            _currentState = StealthState.Visible;
            _stateTimer = VisibleDuration;
        }

        void Start()
        {
            if (EnemyRenderer != null)
            {
                _originalMaterial = EnemyRenderer.sharedMaterial;

                if (StealthMaterial != null)
                {
                    _runtimeStealthMat = new Material(StealthMaterial);
                }
            }
        }

        protected override void Update()
        {
            base.Update(); // Giữ nguyên AI di chuyển, tìm đường đuổi Player của EnemyFodder

            // 1. XỬ LÝ LERP ALPHA (Khi đang tàng hình)
            if (_currentState == StealthState.Cloaked && _runtimeStealthMat != null && EnemyRenderer != null)
            {
                _currentAlpha = Mathf.Lerp(_currentAlpha, _targetAlpha, Time.deltaTime * FadeSpeed);
                Color currentColor = _runtimeStealthMat.color;
                currentColor.a = _currentAlpha;
                _runtimeStealthMat.color = currentColor;

                if (_runtimeStealthMat.HasProperty("_BaseColor"))
                {
                    _runtimeStealthMat.SetColor("_BaseColor", currentColor);
                }
            }

            // 2. BỘ ĐẾM THỜI GIAN VÒNG LẶP CHUYỂN TRẠNG THÁI
            _stateTimer -= Time.deltaTime;
            if (_stateTimer <= 0f)
            {
                SwitchStealthState();
            }

            // 3. CƠ CHẾ TỰ ĐỘNG TRỪ MÁU PLAYER KHI ĐẾN GẦN (Copy chuẩn từ Heavy)
            HandleProximityDamage();
        }

        private void SwitchStealthState()
        {
            if (EnemyRenderer == null) return;

            if (_currentState == StealthState.Visible)
            {
                _currentState = StealthState.Cloaked;
                _stateTimer = CloakedDuration;
                _currentAlpha = 1f;       
                _targetAlpha = 0.05f;     

                if (_runtimeStealthMat != null) EnemyRenderer.material = _runtimeStealthMat;
            }
            else
            {
                _currentState = StealthState.Visible;
                _stateTimer = VisibleDuration;

                if (_originalMaterial != null) EnemyRenderer.material = _originalMaterial;
            }
        }

        // Hàm xử lý gây sát thương liên tục khi áp sát Player
        private void HandleProximityDamage()
        {
            if (_player == null) return;

            // Tính khoảng cách thực tế giữa nhện và Player
            float distance = Vector3.Distance(transform.position, _player.position);

            // Nếu Player đi vào vùng nguy hiểm của nhện
            if (distance <= DamageRange)
            {
                // Lấy component máu của Player ra để trừ
                var playerHealth = _player.GetComponent<PlayerHealth>();
                if (playerHealth != null)
                {
                    // Trừ máu theo thời gian thực (mỗi giây trừ đúng lượng DamagePerSecond)
                    playerHealth.TakeDamage(DamagePerSecond * Time.deltaTime);
                }
            }
        }
    }
}