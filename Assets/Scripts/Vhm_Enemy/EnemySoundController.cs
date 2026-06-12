using UnityEngine;
using System.Collections;

namespace OutOfBullet.Enemy
{
    public class EnemySoundController : MonoBehaviour
    {
        [Header("Loại sound enemy này dùng")]
        [SerializeField] private bool hasWalking;
        [SerializeField] private bool hasLaser;
        [SerializeField] private bool hasBomb;

        private EnemyBase     _enemy;
        private EnemyDrone    _drone;
        private EnemyShielder _shielder;

        private AudioSource _walkSource;
        private bool        _isDead;
        private EnemyState  _lastState;

        // Laser
        private System.Reflection.FieldInfo _laserField;
        private bool _lastLaserActive;

        // Bomb — track bằng coroutine hook vào _isBursting
        private System.Reflection.FieldInfo _burstingField;
        private bool _lastBursting;
        private System.Reflection.FieldInfo _burstCountField;

        // Die — dùng cả 2 cách để chắc chắn
        private float _checkDeadTimer;

        void Start()
        {
            _enemy    = GetComponent<EnemyBase>();
            _drone    = GetComponent<EnemyDrone>();
            _shielder = GetComponent<EnemyShielder>();

            // Setup walking source
            if (hasWalking)
            {
                _walkSource = gameObject.AddComponent<AudioSource>();
                SoundManager.Instance.SetupWalkingSource(_walkSource);
            }

            // Cache reflection fields — chỉ gọi 1 lần trong Start, không tốn gì trong Update
            if (hasLaser && _drone != null)
            {
                _laserField = typeof(EnemyDrone).GetField(
                    "_isLaserActive",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance
                );
            }

            if (hasBomb && _shielder != null)
            {
                _burstingField = typeof(EnemyShielder).GetField(
                    "_isBursting",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance
                );
                _burstCountField = typeof(EnemyShielder).GetField(
                    "BurstCount",
                    System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance
                );
            }

            if (_enemy != null)
                _lastState = _enemy.State;
        }

        void Update()
        {
            if (_enemy == null) return;

            HandleDie();
            if (_isDead) return;

            HandleWalking();
            HandleLaser();
            HandleBomb();
        }

        // ── Die ──────────────────────────────────────────────────
        // Dùng 2 điều kiện OR để không bỏ sót
        private void HandleDie()
        {
            if (_isDead) return;

            bool died = !_enemy.IsAlive || _enemy.State == EnemyState.Ragdoll;
            if (!died) return;

            _isDead = true;
            StopWalking();
            SoundManager.Instance.PlaySFX(SFXType.Die, transform.position);
        }

        // ── Walking ──────────────────────────────────────────────
        private void HandleWalking()
        {
            if (!hasWalking) return;

            EnemyState current = _enemy.State;
            if (current == _lastState) return;
            _lastState = current;

            if (current == EnemyState.Aggro)
                StartWalking();
            else
                StopWalking();
        }

        private void StartWalking()
        {
            if (_walkSource == null) return;
            if (!_walkSource.isPlaying) _walkSource.Play();
        }

        private void StopWalking()
        {
            if (_walkSource != null && _walkSource.isPlaying)
                _walkSource.Stop();
        }

        // ── Laser ─────────────────────────────────────────────────
        private void HandleLaser()
        {
            if (!hasLaser || _drone == null || _laserField == null) return;

            bool laserNow = (bool)_laserField.GetValue(_drone);

            // Chỉ phát đúng 1 lần khi laser vừa bật
            if (laserNow && !_lastLaserActive)
                SoundManager.Instance.PlaySFX(SFXType.Laser, transform.position);

            _lastLaserActive = laserNow;
        }

        // ── Bomb ──────────────────────────────────────────────────
        // Poll _isBursting: khi vừa chuyển false→true = burst mới bắt đầu
        // Mỗi burst bắn BurstCount viên, phát sound BurstCount lần qua coroutine
        private void HandleBomb()
        {
            if (!hasBomb || _shielder == null || _burstingField == null) return;

            bool burstingNow = (bool)_burstingField.GetValue(_shielder);

            // Vừa bắt đầu burst mới
            if (burstingNow && !_lastBursting)
            {
                int count = _burstCountField != null ? (int)_burstCountField.GetValue(_shielder) : 1;
                StartCoroutine(PlayBombBurst(count));
            }

            _lastBursting = burstingNow;
        }

        // Phát sound theo đúng nhịp burst — mỗi viên 1 tiếng
        private IEnumerator PlayBombBurst(int count)
        {
            // Lấy BurstInterval từ Shielder để sync đúng nhịp
            var intervalField = typeof(EnemyShielder).GetField(
                "BurstInterval",
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance
            );
            float interval = intervalField != null ? (float)intervalField.GetValue(_shielder) : 0.25f;

            for (int i = 0; i < count; i++)
            {
                if (_isDead) yield break;
                SoundManager.Instance.PlaySFX(SFXType.Bomb, transform.position);
                yield return new WaitForSeconds(interval);
            }
        }
    }
}