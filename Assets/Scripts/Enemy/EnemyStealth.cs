// ============================================================
//  EnemyStealth.cs  —  Out of Bullet
//  Quái tàng hình áp sát gây sát thương theo thời gian.
//  Khi Player chạy thoát sẽ bị dính độc Bleeding giật 10 HP/s trong 5s.
// ============================================================

using UnityEngine;
using System.Collections;
using OutOfBullet.Player; // Gọi đúng namespace chứa PlayerHealth gốc của nhóm

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

        [Header("Cận Chiến / Sát Thương Áp Sát")]
        [Tooltip("Khoảng cách đứng gần Player để bắt đầu gây sát thương.")]
        public float DamageRange = 2f; 
        
        [Tooltip("Sát thương gây ra cho Player mỗi giây khi đứng gần.")]
        public float DamagePerSecond = 15f; 

        [Header("Bleeding Settings (Upgrade)")]
        [Tooltip("Thời gian bị chảy máu sau khi chạy thoát khỏi quái (giây).")]
        public float BleedDuration = 5f;
        [Tooltip("Sát thương chảy máu mỗi lần giật.")]
        public float BleedDamagePerTick = 10f;
        [Tooltip("Khoảng cách thời gian giữa mỗi lần giật máu (1s là chuẩn).")]
        public float BleedInterval = 1f;

        // Các biến xử lý ngầm
        private Material _originalMaterial; 
        private Material _runtimeStealthMat; 
        private float _targetAlpha = 1f;
        private float _currentAlpha = 1f;

        private StealthState _currentState = StealthState.Visible;
        private float _stateTimer = 0f;

        // Quản lý trạng thái Bleeding độc lập cho Player
        private bool _playerWasInZone = false;
        private static Coroutine _activeBleedCoroutine; 

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
            base.Update(); // Giữ nguyên AI di chuyển và tìm đường của EnemyFodder

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

            // 3. CƠ CHẾ TỰ ĐỘNG TRỪ MÁU PLAYER KHI ĐẾN GẦN & KÍCH HOẠT BLEEDING KHI CHẠY XA
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

        // Hàm xử lý gây sát thương liên tục và bắt trạng thái kích hoạt Bleeding
        private void HandleProximityDamage()
        {
            if (_player == null) return;

            // Tính khoảng cách thực tế giữa nhện Stealth và Player
            float distance = Vector3.Distance(transform.position, _player.position);
            var playerHealth = _player.GetComponent<PlayerHealth>();

            if (playerHealth == null) return;

            if (distance <= DamageRange)
            {
                // Tình huống 1: Player đang ở TRONG vùng nguy hiểm
                playerHealth.TakeDamage(DamagePerSecond * Time.deltaTime);
                
                // Nếu đang đứng gần quái thì tạm thời tắt hiệu ứng giật độc chạy xa (hoặc reset)
                if (_activeBleedCoroutine != null)
                {
                    StopCoroutine(_activeBleedCoroutine);
                    _activeBleedCoroutine = null;
                }

                _playerWasInZone = true; // Đánh dấu Player đã từng lọt vào tầm cào
            }
            else
            {
                // Tình huống 2: Player vừa CHẠY THOÁT ra ngoài tầm sát thương
                if (_playerWasInZone)
                {
                    _playerWasInZone = false; // Reset cờ hiệu

                    // Kích hoạt hiệu ứng Bleeding kéo dài 5 giây độc lập
                    if (_activeBleedCoroutine != null) StopCoroutine(_activeBleedCoroutine);
                    _activeBleedCoroutine = StartCoroutine(PlayerBleedingRoutine(playerHealth));
                }
            }
        }

        // Coroutine găm trực tiếp sát thương thời gian vào Player
        private IEnumerator PlayerBleedingRoutine(PlayerHealth targetHealth)
        {
            float elapsed = 0f;

            while (elapsed < BleedDuration)
            {
                yield return new WaitForSeconds(BleedInterval);
                elapsed += BleedInterval;

                if (targetHealth != null && targetHealth.IsAlive)
                {
                    // Trừ chuẩn 10 máu mỗi tick thông qua hàm TakeDamage gốc của PlayerHealth
                    targetHealth.TakeDamage(BleedDamagePerTick);
                    Debug.Log($"[Stealth Poison] Player dính độc rỉ máu! Giật -{BleedDamagePerTick} HP. Progress: {elapsed}/{BleedDuration}s");
                }
                else
                {
                    break; // Ngừng giật nếu Player đã hi sinh
                }
            }

            _activeBleedCoroutine = null;
        }
    }
}