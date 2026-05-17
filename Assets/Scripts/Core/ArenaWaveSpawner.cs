// ============================================================
//  ArenaWaveSpawner.cs  —  Out of Bullet
//  GDD §7.3 — All 5 encounter types.
//  Chain Opener → Escalation → Pressure Wave → Elite → Arena Clear
// ============================================================
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using OutOfBullet.Core;

namespace OutOfBullet
{
    public enum WaveType
    {
        ChainOpener,    // 4-6 Fodder, scattered — teach the loop
        Escalation,     // 3-4 Fodder + 1 Heavy — introduce heavy as priority
        PressureWave,   // 2 Heavies + Fodder screen — resource prioritization
        Elite,          // 1 Heavy (elite), no Fodder — execution puzzle
        ArenaClear      // Mixed full-room — mastery test
    }

    [System.Serializable]
    public class WaveDefinition
    {
        public WaveType Type;
        public int      FodderCount;
        public int      HeavyCount;
        public bool     IsEliteHeavy;   // future: elite variant
    }

    public class ArenaWaveSpawner : MonoBehaviour
    {
        // ── Inspector ────────────────────────────────────────────
        [Header("Prefabs")]
        public GameObject FodderPrefab;
        public GameObject HeavyPrefab;

        [Header("Spawn Points")]
        [Tooltip("Assign spawn point transforms in scene. Shuffled per wave.")]
        public Transform[] SpawnPoints;

        [Header("Waves")]
        public List<WaveDefinition> Waves;

        [Header("Spawn Settings")]
        [Tooltip("Delay between individual enemy spawns in a wave.")]
        public float SpawnStagger = 0.2f;

        [Tooltip("Start first wave automatically on scene load.")]
        public bool AutoStartOnLoad = true;

        // ── Runtime ──────────────────────────────────────────────
        private int  _currentWaveIndex = -1;
        private int  _aliveEnemyCount  = 0;
        private bool _waveActive       = false;
        private float _waveStartTime;

        // Track active enemies for alive-count management
        private readonly List<GameObject> _activeEnemies = new List<GameObject>();

        // ── Unity ────────────────────────────────────────────────
        private void OnEnable()
        {
            EventBus.Subscribe<EnemyKilledEvent>(OnEnemyKilled);
            EventBus.Subscribe<ArenaResetEvent>(OnArenaReset);
        }

        private void OnDisable()
        {
            EventBus.Unsubscribe<EnemyKilledEvent>(OnEnemyKilled);
            EventBus.Unsubscribe<ArenaResetEvent>(OnArenaReset);
        }

        private void Start()
        {
            if (AutoStartOnLoad)
                StartCoroutine(StartNextWaveRoutine());
        }

        // ── Wave Control ─────────────────────────────────────────
        public void StartNextWave()
        {
            StartCoroutine(StartNextWaveRoutine());
        }

        private IEnumerator StartNextWaveRoutine()
        {
            _currentWaveIndex++;
            if (_currentWaveIndex >= Waves.Count)
            {
                GameManager.Instance?.DebugLog("[Spawner] All waves cleared!");
                yield break;
            }

            WaveDefinition wave = Waves[_currentWaveIndex];
            _waveActive    = true;
            _waveStartTime = Time.time;

            EventBus.Publish(new WaveStartedEvent
            {
                WaveIndex = _currentWaveIndex,
                WaveType  = wave.Type.ToString()
            });

            GameManager.Instance?.DebugLog(
                $"[Spawner] Wave {_currentWaveIndex} starting — {wave.Type}" +
                $" (Fodder:{wave.FodderCount} Heavy:{wave.HeavyCount})");

            // Shuffle spawn points for variety
            ShuffleSpawnPoints();

            int spawnIdx = 0;

            // Spawn Fodder
            for (int i = 0; i < wave.FodderCount; i++)
            {
                SpawnEnemy(FodderPrefab, spawnIdx % SpawnPoints.Length);
                spawnIdx++;
                yield return new WaitForSeconds(SpawnStagger);
            }

            // Spawn Heavies
            for (int i = 0; i < wave.HeavyCount; i++)
            {
                SpawnEnemy(HeavyPrefab, spawnIdx % SpawnPoints.Length);
                spawnIdx++;
                yield return new WaitForSeconds(SpawnStagger);
            }
        }

        private void SpawnEnemy(GameObject prefab, int spawnIdx)
        {
            if (prefab == null || SpawnPoints.Length == 0) return;

            Transform spawnPt = SpawnPoints[spawnIdx];
            var go = Instantiate(prefab, spawnPt.position, spawnPt.rotation);
            _activeEnemies.Add(go);
            _aliveEnemyCount++;
        }

        // ── Wave Clear Check ─────────────────────────────────────
        private void OnEnemyKilled(EnemyKilledEvent evt)
        {
            _activeEnemies.Remove(evt.Enemy);
            _aliveEnemyCount = Mathf.Max(0, _aliveEnemyCount - 1);

            if (_waveActive && _aliveEnemyCount == 0)
            {
                float clearTime = Time.time - _waveStartTime;
                _waveActive = false;

                EventBus.Publish(new WaveClearedEvent
                {
                    WaveIndex        = _currentWaveIndex,
                    ClearTimeSeconds = clearTime
                });

                GameManager.Instance?.DebugLog(
                    $"[Spawner] Wave {_currentWaveIndex} cleared in {clearTime:F1}s");

                // Brief pause then auto-advance (tweak per level design)
                StartCoroutine(AutoAdvanceWave());
            }
        }

        private IEnumerator AutoAdvanceWave()
        {
            yield return new WaitForSeconds(2f);
            if (_currentWaveIndex + 1 < Waves.Count)
                StartCoroutine(StartNextWaveRoutine());
        }

        // ── Reset ────────────────────────────────────────────────
        private void OnArenaReset(ArenaResetEvent evt)
        {
            foreach (var e in _activeEnemies)
                if (e != null) Destroy(e);

            _activeEnemies.Clear();
            _aliveEnemyCount   = 0;
            _currentWaveIndex  = -1;
            _waveActive        = false;

            if (AutoStartOnLoad)
                StartCoroutine(StartNextWaveRoutine());
        }

        // ── Helpers ──────────────────────────────────────────────
        private void ShuffleSpawnPoints()
        {
            for (int i = SpawnPoints.Length - 1; i > 0; i--)
            {
                int j = Random.Range(0, i + 1);
                (SpawnPoints[i], SpawnPoints[j]) = (SpawnPoints[j], SpawnPoints[i]);
            }
        }

        // ── Preset Builder ───────────────────────────────────────
        // Call from editor or code to auto-populate waves per GDD §7.3
        [ContextMenu("Populate GDD Default Waves")]
        private void PopulateDefaultWaves()
        {
            Waves = new List<WaveDefinition>
            {
                new WaveDefinition { Type = WaveType.ChainOpener,  FodderCount = 5, HeavyCount = 0 },
                new WaveDefinition { Type = WaveType.Escalation,   FodderCount = 3, HeavyCount = 1 },
                new WaveDefinition { Type = WaveType.PressureWave, FodderCount = 4, HeavyCount = 2 },
                new WaveDefinition { Type = WaveType.Elite,        FodderCount = 0, HeavyCount = 1, IsEliteHeavy = true },
                new WaveDefinition { Type = WaveType.ArenaClear,   FodderCount = 6, HeavyCount = 3 },
            };
            Debug.Log("[Spawner] GDD default waves populated.");
        }
    }
}
