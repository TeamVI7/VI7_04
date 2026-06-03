// ============================================================
//  EnemyDrone.cs  —  Out of Bullet
//  Kế thừa EnemyFodder để tái sử dụng AI Radar & Di chuyển.
//  Tự động ép Line Renderer chạy World Space để không bị lệch tâm.
// ============================================================
using UnityEngine;
using OutOfBullet.Player;

namespace OutOfBullet.Enemy
{
    public class EnemyDrone : EnemyFodder
    {
        [Header("Drone — Laser Tracking Settings")]
        [Tooltip("Kéo Prefab chứa thành phần Line Renderer vào đây.")]
        public LineRenderer LaserPrefab;

        [Tooltip("Sát thương gây ra cho Player mỗi giây (DPS).")]
        public float DamagePerSecond = 15f;

        [Tooltip("Layer vật cản môi trường (Tường, Cột, Địa hình...).")]
        public LayerMask ObstacleLayers;

        [Tooltip("Độ cao tia laser nhắm vào cơ thể Player (1.0f = Ngực, 0.5f = Bụng, 0f = Gót chân).")]
        public float LaserTargetHeightOffset = 0.5f; // <--- THÊM BIẾN NÀY ĐỂ DỄ ĐIỀU CHỈNH ĐỘ CAO

        private LineRenderer _spawnedLaser;
        private bool _isLaserActive = false;

        protected override void Awake()
        {
            base.Awake();

            // Tự động sinh ra bản sao tia laser bên trong Drone
            if (LaserPrefab != null)
            {
                _spawnedLaser = Instantiate(LaserPrefab, transform);
                _spawnedLaser.positionCount = 2;
                
                // ÉP BUỘC LINE RENDERER CHẠY WORLD SPACE ĐỂ KHÔNG BỊ LỆCH THEO TRANSFORM CON
                _spawnedLaser.useWorldSpace = true; 
                
                _spawnedLaser.enabled = false;
            }
        }

        private void LateUpdate()
        {
            // Kiểm tra trạng thái NavMeshAgent để ngắt laser lập tức nếu Drone bị khựng/chết
            if (_nav == null || !_nav.enabled)
            {
                TurnOffLaser();
                return;
            }

            // Nếu trạng thái bắn đang bật, liên tục cập nhật vị trí tia laser đuổi theo Player
            if (_isLaserActive)
            {
                if (_player == null)
                {
                    GameObject targetPlayer = GameObject.FindGameObjectWithTag("Player");
                    if (targetPlayer != null) 
                    {
                        _player = targetPlayer.transform;
                    }
                }

                if (_player != null)
                {
                    UpdateLaserBeam();
                }
                else
                {
                    TurnOffLaser();
                }
            }
        }

        /// <summary>
        /// Ghi đè hàm bắn của Fodder để kích hoạt hệ thống khóa mục tiêu bằng laser liên tục.
        /// </summary>
        protected override void TryFireHorizontal(float dist)
        {
            if (dist > AcidRange)
            {
                TurnOffLaser();
                return;
            }

            if (!_isLaserActive)
            {
                TurnOnLaser();
            }
        }

        private void UpdateLaserBeam()
        {
            if (_spawnedLaser == null || FirePoint == null || _player == null) return;

            // Điểm gốc (0) bắt buộc đóng đinh tại vị trí chính xác của FirePoint thế giới
            Vector3 origin = FirePoint.position;
            _spawnedLaser.SetPosition(0, origin);

            // ĐÃ SỬA: Hạ độ cao bằng cách nhân với biến Offset mới thay vì ép cứng 1.0f
            Vector3 targetCenter = _player.position + Vector3.up * LaserTargetHeightOffset;
            Vector3 direction = (targetCenter - origin).normalized;

            RaycastHit hit;

            // Kiểm tra vật cản che chắn giữa Drone và Player
            if (Physics.Raycast(origin, direction, out hit, AcidRange, ObstacleLayers))
            {
                _spawnedLaser.SetPosition(1, hit.point);

                if (hit.collider.CompareTag("Player"))
                {
                    ApplyDamage(hit.collider.gameObject);
                }
            }
            else
            {
                // Nếu không có vật cản, tia laser chiếu thẳng tới vị trí Player
                _spawnedLaser.SetPosition(1, targetCenter);
                
                RaycastHit playerHit;
                if (Physics.Raycast(origin, direction, out playerHit, AcidRange))
                {
                    if (playerHit.collider.CompareTag("Player"))
                    {
                        ApplyDamage(playerHit.collider.gameObject);
                    }
                }
            }
        }

        private void ApplyDamage(GameObject playerObj)
        {
            var playerHealth = playerObj.GetComponent<PlayerHealth>();
            if (playerHealth != null)
            {
                playerHealth.TakeDamage(DamagePerSecond * Time.deltaTime);
            }
        }

        private void TurnOnLaser()
        {
            _isLaserActive = true;
            if (_spawnedLaser != null) _spawnedLaser.enabled = true;
        }

        private void TurnOffLaser()
        {
            _isLaserActive = false;
            if (_spawnedLaser != null) _spawnedLaser.enabled = false;
        }

        private void OnDisable()
        {
            TurnOffLaser();
        }
    }
}