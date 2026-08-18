using System;
using UnityEngine;

/// <summary>
/// The roster an <see cref="EnemySpawnArea"/> draws from. One asset per encounter
/// flavour ("Corridor Grunts", "Rooftop Overwatch") — the same table can feed any
/// number of spawn areas, so archetype costs stay tuned in exactly one place.
///
/// Costs are the whole point: a wave gets a threat budget and buys from this table
/// until it can't afford anything left. Change the budget and the same table
/// produces a different fight, which is what makes the encounter procedural rather
/// than a hand-written list replayed identically every run.
///
/// Create via: Right-click > Create > Enemy > Spawn Table
/// </summary>
[CreateAssetMenu(menuName = "Enemy/Spawn Table", fileName = "EnemySpawnTable_New")]
public class EnemySpawnTable : ScriptableObject
{
    [Serializable]
    public class Entry
    {
        [Tooltip("Enemy prefab. Its ROOT must have an EnemyHealth — the spawn area tracks wave clearing through it, and an entry without one is skipped with a warning.")]
        public GameObject Prefab;

        [Tooltip("Threat points this archetype costs against a wave's budget. Keep the cheapest grunt at 1 and price everything else relative to it.")]
        [Min(1)] public int Cost = 1;

        [Tooltip("Relative pick chance among everything the wave can currently afford. 0 means never picked procedurally — useful for archetypes you only ever want to place as a wave Anchor.")]
        [Range(0f, 10f)] public float Weight = 1f;

        [Tooltip("Never more than this many of this archetype alive at once in the area. 0 = no limit. This is what stops one sniper becoming four.")]
        [Min(0)] public int MaxConcurrent = 0;

        [Tooltip("Not selectable until this wave number (1-based). Lets one table escalate across an encounter — grunts from wave 1, heavies from wave 3 — without needing a second table.")]
        [Min(1)] public int MinWave = 1;
    }

    public Entry[] Entries;

    /// <summary>Cheapest usable entry cost, used by the spawn area to know when a
    /// wave's leftover budget can't buy anything and filling should stop. Returns
    /// int.MaxValue for an empty/unusable table so the fill loop exits immediately.</summary>
    public int CheapestCost(int waveNumber)
    {
        int cheapest = int.MaxValue;
        if (Entries == null) return cheapest;

        foreach (var e in Entries)
        {
            if (!IsUsable(e, waveNumber)) continue;
            if (e.Cost < cheapest) cheapest = e.Cost;
        }
        return cheapest;
    }

    /// <summary>Selectable at all right now — has a prefab, is unlocked for this
    /// wave, and can actually be rolled procedurally (weight above zero).</summary>
    public static bool IsUsable(Entry e, int waveNumber) =>
        e != null && e.Prefab != null && e.Weight > 0f && waveNumber >= e.MinWave;

#if UNITY_EDITOR
    private void OnValidate()
    {
        if (Entries == null) return;

        foreach (var e in Entries)
        {
            if (e?.Prefab == null) continue;
            if (e.Prefab.GetComponent<EnemyHealth>() == null)
                Debug.LogWarning($"[EnemySpawnTable] '{name}': entry '{e.Prefab.name}' has no EnemyHealth on its root — the spawn area can't track it and will skip it.", this);
        }
    }
#endif
}
