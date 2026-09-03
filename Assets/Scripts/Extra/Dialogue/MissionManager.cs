using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public class MissionManager : MonoBehaviour
{
    public static MissionManager Instance { get; private set; }

    public static event System.Action<List<MissionData>> OnMissionsChanged;

    [SerializeField] private MinimapRoomHighlight[] roomHighlights;

    [Header("Save / Restore")]
    [Tooltip("Every mission this level can hand out. The save stores mission ids, and " +
             "restoring one has to turn that id back into its MissionData — which is only " +
             "possible for assets listed here. A mission missing from this list survives a " +
             "checkpoint in name only and will not come back on the objective list.\n\n" +
             "Auto-filled in the editor with every MissionData in the project while left " +
             "empty. Assign it by hand to narrow it to a specific level's missions.")]
    [SerializeField] private MissionData[] knownMissions;

    private readonly List<MissionData> activeMissions = new();
    private Dictionary<string, MinimapRoomHighlight> roomLookup;

    /// <summary>Ids of the missions currently on the objective list, for the save system.</summary>
    public IEnumerable<string> ActiveMissionIds => activeMissions.Select(m => m.missionId);

    void Awake()
    {
        Instance = this;
        roomLookup = (roomHighlights ?? System.Array.Empty<MinimapRoomHighlight>())
            .ToDictionary(r => r.RoomId, r => r);
    }

    public void AddMission(MissionData mission)
    {
        if (activeMissions.Any(m => m.missionId == mission.missionId)) return;

        activeMissions.Add(mission);
        SetRoomsLit(mission, true);
        OnMissionsChanged?.Invoke(activeMissions);
    }

    public void CompleteMission(string missionId)
    {
        var mission = activeMissions.FirstOrDefault(m => m.missionId == missionId);
        if (mission == null) return;

        MissionListUI.Instance?.PlayCompleteThenRemove(mission, () =>
        {
            activeMissions.Remove(mission);
            SetRoomsLit(mission, false);
            OnMissionsChanged?.Invoke(activeMissions);
        });
    }

    private void SetRoomsLit(MissionData mission, bool lit)
    {
        if (mission.roomIdsToHighlight == null) return;
        foreach (var roomId in mission.roomIdsToHighlight)
            if (roomLookup.TryGetValue(roomId, out var room))
                room.SetLit(lit);
    }

    public bool IsActive(string missionId) => activeMissions.Any(m => m.missionId == missionId);

    /// <summary>
    /// Replaces the objective list with the set of ids from a save.
    ///
    /// Goes through the same SetRoomsLit path as add/complete rather than just swapping
    /// the list, because the minimap highlights are state living outside this class —
    /// a restore that only fixed the list would leave rooms lit for missions the player
    /// no longer has.
    ///
    /// Bypasses MissionListUI's completion animation on the way out: that animation is
    /// feedback for finishing an objective, and nothing was finished here.
    /// </summary>
    public void RestoreActiveMissions(List<string> missionIds)
    {
        foreach (MissionData mission in activeMissions)
            SetRoomsLit(mission, false);

        activeMissions.Clear();

        if (missionIds != null)
        {
            foreach (string id in missionIds)
            {
                MissionData mission = FindMission(id);
                if (mission == null)
                {
                    Debug.LogWarning($"[MissionManager] Save references mission '{id}', which is " +
                                     "not in Known Missions — it cannot be restored.", this);
                    continue;
                }

                activeMissions.Add(mission);
                SetRoomsLit(mission, true);
            }
        }

        OnMissionsChanged?.Invoke(activeMissions);
    }

    private MissionData FindMission(string missionId)
    {
        if (string.IsNullOrEmpty(missionId) || knownMissions == null) return null;

        return knownMissions.FirstOrDefault(m => m != null && m.missionId == missionId);
    }

#if UNITY_EDITOR
    /// <summary>
    /// Fills Known Missions with every MissionData in the project while it is empty, so
    /// mission restore works without a manual pass over each level.
    ///
    /// Editor-only and only ever when empty: once a designer has narrowed the list by
    /// hand, re-adding everything on the next inspector touch would quietly undo that.
    /// </summary>
    private void OnValidate()
    {
        if (knownMissions != null && knownMissions.Length > 0) return;

        string[] guids = UnityEditor.AssetDatabase.FindAssets("t:MissionData");
        if (guids.Length == 0) return;

        knownMissions = guids
            .Select(UnityEditor.AssetDatabase.GUIDToAssetPath)
            .Select(UnityEditor.AssetDatabase.LoadAssetAtPath<MissionData>)
            .Where(m => m != null)
            .ToArray();

        UnityEditor.EditorUtility.SetDirty(this);
    }
#endif
}