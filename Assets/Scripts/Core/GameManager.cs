// ============================================================
//  GameManager.cs  —  Out of Bullet
//  Singleton. Owns scene lifecycle, global pause, debug mode.
//  Does NOT own gameplay logic — that lives in feature systems.
//  FIX: DebugTimeScale default 1f, tránh vô tình set timeScale = 0.1f
// ============================================================
using UnityEngine;
using OutOfBullet.Core;

namespace OutOfBullet.Core
{
    public class GameManager : MonoBehaviour
    {
        // ── Singleton ────────────────────────────────────────────
        public static GameManager Instance { get; private set; }

        // ── Inspector ────────────────────────────────────────────
        [Header("Debug")]
        [Tooltip("Enable to show debug overlays and verbose logs.")]
        public bool DebugMode = false;

        // FIX: Default 1f thay vì 0.1f — tránh vô tình làm chậm game
        [Tooltip("Slow motion scale for debugging momentum chains.")]
        [Range(0.1f, 1f)]
        public float DebugTimeScale = 1f;

        [Header("Restart")]
        [Tooltip("Seconds from death trigger to arena restart.")]
        public float RestartDelay = 0.5f;

        // ── State ────────────────────────────────────────────────
        public bool IsPaused { get; private set; }
        public bool IsGameOver { get; private set; }

        private float _cachedTimeScale = 1f;

        // ── Unity ────────────────────────────────────────────────
        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            DontDestroyOnLoad(gameObject);

            Application.targetFrameRate = 120;
            QualitySettings.vSyncCount = 0;

            // FIX: Đảm bảo timeScale luôn = 1 khi khởi động
            Time.timeScale = 1f;

            if (DebugMode)
                Debug.Log("[GameManager] Debug mode ON");
        }

        private void OnEnable()
        {
            EventBus.Subscribe<PlayerDiedEvent>(OnPlayerDied);
        }

        private void OnDisable()
        {
            EventBus.Unsubscribe<PlayerDiedEvent>(OnPlayerDied);
        }

        // ── Pause ────────────────────────────────────────────────
        public void Pause()
        {
            if (IsPaused) return;
            _cachedTimeScale = Time.timeScale;
            Time.timeScale = 0f;
            IsPaused = true;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            DebugLog("[GameManager] Paused");
        }

        public void Resume()
        {
            if (!IsPaused) return;
            Time.timeScale = DebugMode ? DebugTimeScale : _cachedTimeScale;
            IsPaused = false;
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
            DebugLog("[GameManager] Resumed");
        }

        // ── Death / Restart ──────────────────────────────────────
        private void OnPlayerDied(PlayerDiedEvent evt)
        {
            IsGameOver = true;
            DebugLog("[GameManager] Player died — queuing restart");
            Invoke(nameof(RestartArena), RestartDelay);
        }

        private void RestartArena()
        {
            IsGameOver = false;
            EventBus.Publish(new ArenaResetEvent());
            var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            UnityEngine.SceneManagement.SceneManager.LoadScene(scene.buildIndex);
        }

        // ── Debug Helpers ────────────────────────────────────────
        public void DebugLog(string msg)
        {
            if (DebugMode) Debug.Log(msg);
        }

        public void DebugLogWarning(string msg)
        {
            if (DebugMode) Debug.LogWarning(msg);
        }

        // ── Frame Timing ─────────────────────────────────────────
        private void Update()
        {
            // BẪY: Phát hiện ngay khi timeScale bị thay đổi
            if (Time.timeScale != 1f && !IsPaused)
            {
                Debug.LogWarning($"[BẪY] timeScale = {Time.timeScale} — StackTrace:", this);
                Debug.LogWarning(System.Environment.StackTrace);
                Time.timeScale = 1f; // Reset về 1 ngay lập tức
            }

#if UNITY_EDITOR
            if (DebugMode && !IsPaused)
                Time.timeScale = DebugTimeScale;
#endif
        }
    }
}