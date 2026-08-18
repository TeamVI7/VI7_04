using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Tracks which MinimapFloorZone the player is currently standing in. Multiple
/// zones can overlap (e.g. an open stairwell visible from two floors) — this
/// resolves that with a simple stack: the most recently entered zone wins,
/// and leaving it falls back to whichever older zone (if any) the player is
/// still inside.
///
/// One of these exists per scene (singleton). MissionPanelController reads
/// ActiveZone every frame in UpdateMinimap() to decide camera height,
/// culling mask, and zoom — this registry doesn't touch the camera itself,
/// it only tracks state, keeping the "what floor is the player on" question
/// separate from "how does the minimap camera render that floor."
///
/// EXTENDING:
///   - If you want distance-to-center tie-breaking instead of most-recent-wins
///     (e.g. for irregularly-shaped overlapping zones), replace the stack in
///     RegisterEnter/RegisterExit with a sorted-by-distance list — the public
///     ActiveZone getter and OnActiveZoneChanged event don't need to change.
///   - Add OnZoneListChanged if floors can be added/removed at runtime
///     (e.g. destructible floors, elevators that open new zones).
/// </summary>
public class MinimapFloorRegistry : MonoBehaviour
{
    // ─────────────────────────────────────────────────────────────────────────
    #region Singleton
    // ─────────────────────────────────────────────────────────────────────────

    public static MinimapFloorRegistry Instance { get; private set; }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    #endregion

    // ─────────────────────────────────────────────────────────────────────────
    #region Events
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>Fired whenever the active floor zone changes. Arg: the new active zone (null if the player left every zone).</summary>
    public event System.Action<MinimapFloorZone> OnActiveZoneChanged;

    #endregion

    // ─────────────────────────────────────────────────────────────────────────
    #region Public State
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>The zone the minimap should currently render. Null if the player is in no registered zone.</summary>
    public MinimapFloorZone ActiveZone => _occupiedZones.Count > 0 ? _occupiedZones[_occupiedZones.Count - 1] : null;

    #endregion

    // ─────────────────────────────────────────────────────────────────────────
    #region Private State
    // ─────────────────────────────────────────────────────────────────────────

    [Header("Debug")]
    [SerializeField] private bool debugLog = false;

    // Stack-like list — most recently entered zone sits at the end and wins.
    private readonly List<MinimapFloorZone> _occupiedZones = new List<MinimapFloorZone>();

    #endregion

    // ─────────────────────────────────────────────────────────────────────────
    #region Public API — called by MinimapFloorZone
    // ─────────────────────────────────────────────────────────────────────────

    public void RegisterEnter(MinimapFloorZone zone)
    {
        if (zone == null || _occupiedZones.Contains(zone)) return;

        _occupiedZones.Add(zone);
        Log($"Enter: {zone.floorName}. Active now: {ActiveZone?.floorName ?? "none"}");
        OnActiveZoneChanged?.Invoke(ActiveZone);
    }

    public void RegisterExit(MinimapFloorZone zone)
    {
        if (zone == null) return;
        bool wasActive = ActiveZone == zone;

        _occupiedZones.Remove(zone);

        if (wasActive)
        {
            Log($"Exit: {zone.floorName}. Active now: {ActiveZone?.floorName ?? "none"}");
            OnActiveZoneChanged?.Invoke(ActiveZone);
        }
        else
        {
            Log($"Exit (non-active): {zone.floorName}.");
        }
    }

    /// <summary>
    /// Defensive cleanup — call if a zone GameObject is destroyed/disabled while
    /// the player is standing in it, since OnTriggerExit won't fire in that case.
    /// </summary>
    public void ForceRemove(MinimapFloorZone zone) => RegisterExit(zone);

    #endregion

    // ─────────────────────────────────────────────────────────────────────────
    #region Debug
    // ─────────────────────────────────────────────────────────────────────────

    [System.Diagnostics.Conditional("UNITY_EDITOR")]
    private void Log(string msg)
    {
        if (debugLog) Debug.Log($"[MinimapFloorRegistry] {msg}", this);
    }

    #endregion
}