using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public class MissionManager : MonoBehaviour
{
    public static MissionManager Instance { get; private set; }

    public static event System.Action<List<MissionData>> OnMissionsChanged;

    [SerializeField] private MinimapRoomHighlight[] roomHighlights;

    private readonly List<MissionData> activeMissions = new();
    private Dictionary<string, MinimapRoomHighlight> roomLookup;

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
}