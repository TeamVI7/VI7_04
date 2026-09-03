using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Opt-in hook for anything that wants to survive a checkpoint restore or a save
/// without <see cref="SaveData"/> having to grow a field for it — doors, puzzles,
/// level scripts, one-off scripted state.
///
/// Implement it on a MonoBehaviour, register in OnEnable, unregister in OnDisable:
///
///     void OnEnable()  => SaveableRegistry.Register(this);
///     void OnDisable() => SaveableRegistry.Unregister(this);
///
///     public string SaveId => "WirePuzzle_Reactor";
///     public string CaptureState() => JsonUtility.ToJson(new MyState { solved = _solved });
///     public void   RestoreState(string json) { ... }
///
/// Ids must be stable across sessions and unique within a scene. A hand-written
/// constant is the safest choice; <see cref="SaveableId"/> generates one for
/// objects where that is impractical.
/// </summary>
public interface ISaveable
{
    string SaveId { get; }

    /// <summary>Serialize this object's state. Return null or empty to record nothing.</summary>
    string CaptureState();

    /// <summary>Reapply state produced by <see cref="CaptureState"/>. Only ever called
    /// with a non-empty string that this same component wrote.</summary>
    void RestoreState(string json);
}

/// <summary>
/// Tracks the <see cref="ISaveable"/>s alive in the current scene.
///
/// Registration is explicit rather than a FindObjectsOfType sweep at save time,
/// because a sweep misses anything currently disabled — and "disabled" is exactly
/// the state a door that has already opened, or a puzzle mid-teardown, tends to be in.
/// </summary>
public static class SaveableRegistry
{
    private static readonly List<ISaveable> Registered = new List<ISaveable>();

    public static IReadOnlyList<ISaveable> All => Registered;

    // Statics survive Play Mode entry when domain reload is off, which would leave
    // this holding destroyed components from the previous run.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void Reset() => Registered.Clear();

    public static void Register(ISaveable saveable)
    {
        if (saveable == null || Registered.Contains(saveable)) return;

        if (string.IsNullOrWhiteSpace(saveable.SaveId))
        {
            Debug.LogWarning($"[SaveableRegistry] {saveable.GetType().Name} has a blank SaveId " +
                             "and will not be saved.");
            return;
        }

        Registered.Add(saveable);
    }

    public static void Unregister(ISaveable saveable) => Registered.Remove(saveable);

    public static List<CustomSaveState> CaptureAll()
    {
        var states = new List<CustomSaveState>(Registered.Count);

        foreach (ISaveable saveable in Registered)
        {
            if (saveable == null) continue;

            string json = saveable.CaptureState();
            if (string.IsNullOrEmpty(json)) continue;

            states.Add(new CustomSaveState { id = saveable.SaveId, json = json });
        }

        return states;
    }

    public static void RestoreAll(List<CustomSaveState> states)
    {
        if (states == null) return;

        foreach (CustomSaveState state in states)
        {
            if (state == null || string.IsNullOrEmpty(state.json)) continue;

            foreach (ISaveable saveable in Registered)
            {
                if (saveable == null || saveable.SaveId != state.id) continue;

                saveable.RestoreState(state.json);
                break;
            }
        }
    }
}
