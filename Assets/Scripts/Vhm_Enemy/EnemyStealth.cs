// ============================================================
//   EnemyStealth.cs  —  Out of Bullet
//   Quái tàng hình áp sát gây sát thương theo thời gian.
//   Hỗ trợ tàng hình đồng bộ Thân + 6 Chân nhện (Mảng Renderers).
// ============================================================

using UnityEngine;
using System.Collections;
using OutOfBullet.Player; 

namespace OutOfBullet.Enemy
{
    public class EnemyStealth : EnemyFodder
    {
        public enum StealthState { Visible, Cloaked }

        [Header("Stealth Loop Settings")]
        public float VisibleDuration = 3f;  
        public float CloakedDuration = 20f; 

        [Header("Render & Material Settings")]
        [Tooltip("Thay đổi thành Mảng: Kéo Thân (Cube.001) và 6 Chân (Cube.002 -> Cube.007) vào đây.")]
        public Renderer[] EnemyRenderers; // Đã nâng cấp lên mảng để chứa cả thân lẫn chân

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

        // Lưu trữ mảng vật liệu gốc và vật liệu tàng hình runtime cho từng bộ phận
        private Material[] _originalMaterials; 
        private Material[] _runtimeStealthMats; 
        private float _targetAlpha = 1f;
        private float _currentAlpha = 1f;

        private StealthState _currentState = StealthState.Visible;
        private float _stateTimer = 0f;

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
            // Khởi tạo kích thước mảng lưu trữ dựa theo số lượng Renderer cậu kéo vào
            if (EnemyRenderers != null && EnemyRenderers.Length > 0)
            {
                _originalMaterials = new Material[EnemyRenderers.Length];
                _runtimeStealthMats = new Material[EnemyRenderers.Length];

                for (int i = 0; i < EnemyRenderers.Length; i++)
                {
                    if (EnemyRenderers[i] != null)
                    {
                        // Lưu vật liệu gốc ban đầu của từng bộ phận (Thân/Chân)
                        _originalMaterials[i] = EnemyRenderers[i].sharedMaterial;

                        // Tạo instance vật liệu tàng hình độc lập cho từng bộ phận để chỉnh Alpha không bị lỗi lây nhau
                        if (StealthMaterial != null)
                        {
                            _runtimeStealthMats[i] = new Material(StealthMaterial);
                        }
                    }
                }
            }
        }

        protected override void Update()
        {
            base.Update(); 

            // 1. XỬ LÝ LERP ALPHA ĐỒNG BỘ CHO CẢ THÂN VÀ CHÂN
            if (_currentState == StealthState.Cloaked && _runtimeStealthMats != null && EnemyRenderers != null)
            {
                _currentAlpha = Mathf.Lerp(_currentAlpha, _targetAlpha, Time.deltaTime * FadeSpeed);

                for (int i = 0; i < EnemyRenderers.Length; i++)
                {
                    if (EnemyRenderers[i] != null && _runtimeStealthMats[i] != null)
                    {
                        Color currentColor = _runtimeStealthMats[i].color;
                        currentColor.a = _currentAlpha;
                        _runtimeStealthMats[i].color = currentColor;

                        if (_runtimeStealthMats[i].HasProperty("_BaseColor"))
                        {
                            _runtimeStealthMats[i].SetColor("_BaseColor", currentColor);
                        }
                    }
                }
            }

            // 2. BỘ ĐẾM THỜI GIAN VÒNG LẶP CHUYỂN TRẠNG THÁI
            _stateTimer -= Time.deltaTime;
            if (_stateTimer <= 0f)
            {
                SwitchStealthState();
            }

            // 3. CƠ CHẾ SÁT THƯƠNG VÀ BLEEDING
            HandleProximityDamage();
        }

        private void SwitchStealthState()
        {
            if (EnemyRenderers == null || EnemyRenderers.Length == 0) return;

            if (_currentState == StealthState.Visible)
            {
                _currentState = StealthState.Cloaked;
                _stateTimer = CloakedDuration;
                _currentAlpha = 1f;       
                _targetAlpha = 0.05f;     

                // Quét qua mảng ép toàn bộ Thân + Chân sang vật liệu tàng hình
                for (int i = 0; i < EnemyRenderers.Length; i++)
                {
                    if (EnemyRenderers[i] != null && _runtimeStealthMats[i] != null)
                    {
                        EnemyRenderers[i].material = _runtimeStealthMats[i];
                    }
                }
            }
            else
            {
                _currentState = StealthState.Visible;
                _stateTimer = VisibleDuration;

                // Quét qua mảng trả lại vật liệu gốc (màu đen cơ khí) cho toàn bộ bộ phận
                for (int i = 0; i < EnemyRenderers.Length; i++)
                {
                    if (EnemyRenderers[i] != null && _originalMaterials[i] != null)
                    {
                        EnemyRenderers[i].material = _originalMaterials[i];
                    }
                }
            }
        }

        private void HandleProximityDamage()
        {
            if (_player == null) return;

            float distance = Vector3.Distance(transform.position, _player.position);
            var playerHealth = _player.GetComponent<PlayerHealth>();

            if (playerHealth == null) return;

            if (distance <= DamageRange)
            {
                playerHealth.TakeDamage(DamagePerSecond * Time.deltaTime);
                
                if (_activeBleedCoroutine != null)
                {
                    StopCoroutine(_activeBleedCoroutine);
                    _activeBleedCoroutine = null;
                }

                _playerWasInZone = true; 
            }
            else
            {
                if (_playerWasInZone)
                {
                    _playerWasInZone = false; 

                    if (_activeBleedCoroutine != null) StopCoroutine(_activeBleedCoroutine);
                    _activeBleedCoroutine = StartCoroutine(PlayerBleedingRoutine(playerHealth));
                }
            }
        }

        private IEnumerator PlayerBleedingRoutine(PlayerHealth targetHealth)
        {
            float elapsed = 0f;

            while (elapsed < BleedDuration)
            {
                yield return new WaitForSeconds(BleedInterval);
                elapsed += BleedInterval;

                if (targetHealth != null && targetHealth.IsAlive)
                {
                    targetHealth.TakeDamage(BleedDamagePerTick);
                    Debug.Log($"[Stealth Poison] Player dính độc rỉ máu! Giật -{BleedDamagePerTick} HP. Progress: {elapsed}/{BleedDuration}s");
                }
                else
                {
                    break; 
                }
            }

            _activeBleedCoroutine = null;
        }
    }
}