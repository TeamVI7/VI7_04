using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Singleton object pool for impact decals (bullet holes, blood splatter, etc).
/// Supports any number of distinct decal prefabs simultaneously, same pattern as BulletCasingPool.
/// </summary>
public class ImpactDecalPool : MonoBehaviour
{
    private static ImpactDecalPool _instance;
    public static ImpactDecalPool Instance
    {
        get
        {
            if (_instance == null)
            {
                // Self-bootstrapping, same as PlayerActionLock — no scene wiring required.
                var go = new GameObject("ImpactDecalPool (auto)");
                _instance = go.AddComponent<ImpactDecalPool>();
                DontDestroyOnLoad(go);
            }
            return _instance;
        }
    }

    [Header("Debug")]
    public bool verboseLogging = false;

    private readonly Dictionary<GameObject, Stack<ImpactDecal>> _pools    = new Dictionary<GameObject, Stack<ImpactDecal>>();
    private readonly Dictionary<GameObject, Transform>          _poolRoots = new Dictionary<GameObject, Transform>();

    private void Awake()
    {
        if (_instance != null && _instance != this)
        {
            Destroy(gameObject);
            return;
        }
        _instance = this;
        DontDestroyOnLoad(gameObject);
    }

    /// <summary>Retrieve a decal instance for this prefab (or create one if the pool is empty).</summary>
    public ImpactDecal Get(GameObject prefab)
    {
        if (prefab == null)
        {
            Debug.LogError("[ImpactDecalPool] Get called with null prefab.");
            return null;
        }

        EnsurePoolExists(prefab);
        var stack = _pools[prefab];

        ImpactDecal decal = stack.Count > 0 ? stack.Pop() : CreateInstance(prefab);
        Log($"Get '{prefab.name}' — pool remaining: {stack.Count}");
        return decal;
    }

    public void Return(GameObject prefab, ImpactDecal decal)
    {
        if (prefab == null || decal == null) return;

        EnsurePoolExists(prefab);
        decal.ResetForPool();
        decal.transform.SetParent(_poolRoots[prefab]);
        _pools[prefab].Push(decal);

        Log($"Return '{prefab.name}' — pool size: {_pools[prefab].Count}");
    }

    private ImpactDecal CreateInstance(GameObject prefab)
    {
        var go    = Instantiate(prefab, _poolRoots[prefab]);
        var decal = go.GetComponent<ImpactDecal>();

        if (decal == null)
        {
            Debug.LogError($"[ImpactDecalPool] Prefab '{prefab.name}' is missing an ImpactDecal component.");
            Destroy(go);
            return null;
        }

        decal.SetSourcePrefab(prefab);
        return decal;
    }

    private void EnsurePoolExists(GameObject prefab)
    {
        if (_pools.ContainsKey(prefab)) return;

        _pools[prefab] = new Stack<ImpactDecal>();

        var root = new GameObject($"Pool_{prefab.name}");
        root.transform.SetParent(transform);
        _poolRoots[prefab] = root.transform;
    }

    private void Log(string msg)
    {
        if (verboseLogging) Debug.Log($"[ImpactDecalPool] {msg}");
    }
}
