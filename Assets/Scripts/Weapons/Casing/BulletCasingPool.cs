using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Singleton object pool for bullet casings.
/// Supports multiple BulletCasingData types simultaneously.
/// Place one instance in a persistent scene or bootstrap via lazy instantiation.
/// </summary>
public class BulletCasingPool : MonoBehaviour
{
    // ── Singleton ─────────────────────────────────────────────────────────────
    public static BulletCasingPool Instance { get; private set; }

    [Header("Debug")]
    [Tooltip("Log pool activity to Console.")]
    public bool verboseLogging = false;

    // ── Internal: one stack per prefab ────────────────────────────────────────
    private readonly Dictionary<GameObject, Stack<BulletCasing>> _pools
        = new Dictionary<GameObject, Stack<BulletCasing>>();

    private readonly Dictionary<GameObject, Transform> _poolRoots
        = new Dictionary<GameObject, Transform>();

    // ─────────────────────────────────────────────────────────────────────────
    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    // ─────────────────────────────────────────────────────────────────────────
    /// <summary>Pre-warm the pool for a given casing data asset.</summary>
    public void Prewarm(BulletCasingData data)
    {
        if (data == null || data.casingPrefab == null)
        {
            Debug.LogWarning("[CasingPool] Prewarm called with null data or prefab.");
            return;
        }

        for (int i = 0; i < data.prewarmCount; i++)
            ReturnToPool(CreateInstance(data));

        Log($"Prewarmed {data.prewarmCount}x '{data.casingPrefab.name}'");
    }

    // ─────────────────────────────────────────────────────────────────────────
    /// <summary>Retrieve a casing from the pool (or create one if empty).</summary>
    public BulletCasing Get(BulletCasingData data)
    {
        if (data == null || data.casingPrefab == null)
        {
            Debug.LogError("[CasingPool] Get called with null data or prefab.");
            return null;
        }

        EnsurePoolExists(data.casingPrefab);
        var stack = _pools[data.casingPrefab];

        BulletCasing casing = stack.Count > 0
            ? stack.Pop()
            : CreateInstance(data);

        casing.gameObject.SetActive(true);
        Log($"Get '{data.casingPrefab.name}' — pool remaining: {stack.Count}");
        return casing;
    }

    // ─────────────────────────────────────────────────────────────────────────
    private void ReturnToPool(BulletCasing casing)
    {
        var prefab = GetPrefabKey(casing);
        if (prefab == null) return;

        casing.gameObject.SetActive(false);
        casing.transform.SetParent(_poolRoots[prefab]);
        _pools[prefab].Push(casing);

        Log($"Return '{prefab.name}' — pool size: {_pools[prefab].Count}");
    }

    // ─────────────────────────────────────────────────────────────────────────
    private BulletCasing CreateInstance(BulletCasingData data)
    {
        EnsurePoolExists(data.casingPrefab);

        var go     = Instantiate(data.casingPrefab, _poolRoots[data.casingPrefab]);
        var casing = go.GetComponent<BulletCasing>();

        if (casing == null)
        {
            Debug.LogError($"[CasingPool] Prefab '{data.casingPrefab.name}' is missing BulletCasing component.");
            Destroy(go);
            return null;
        }

        // Wire the return callback
        casing.OnReturnToPool = ReturnToPool;

        // Track which prefab this instance came from
        _instanceToPrefab[casing] = data.casingPrefab;
        return casing;
    }

    // ─────────────────────────────────────────────────────────────────────────
    private void EnsurePoolExists(GameObject prefab)
    {
        if (_pools.ContainsKey(prefab)) return;

        _pools[prefab] = new Stack<BulletCasing>();

        var root = new GameObject($"Pool_{prefab.name}");
        root.transform.SetParent(transform);
        _poolRoots[prefab] = root.transform;
    }

    // ── Instance → prefab lookup ──────────────────────────────────────────────
    private readonly Dictionary<BulletCasing, GameObject> _instanceToPrefab
        = new Dictionary<BulletCasing, GameObject>();

    private GameObject GetPrefabKey(BulletCasing casing)
    {
        if (_instanceToPrefab.TryGetValue(casing, out var prefab)) return prefab;
        Debug.LogWarning($"[CasingPool] Unknown casing instance '{casing.name}' — cannot return.");
        return null;
    }

    // ─────────────────────────────────────────────────────────────────────────
    private void Log(string msg)
    {
        if (verboseLogging) Debug.Log($"[CasingPool] {msg}");
    }
}