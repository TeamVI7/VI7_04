using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public class MissionManager : MonoBehaviour
{
    public static MissionManager Instance { get; private set; }

    public static event System.Action<List<MissionData>> OnMissionsChanged;

    private readonly List<MissionData> activeMissions = new();

    void Awake() => Instance = this;

    public void AddMission(MissionData mission)
    {
        if (activeMissions.Any(m => m.missionId == mission.missionId)) return;

        activeMissions.Add(mission);
        OnMissionsChanged?.Invoke(activeMissions);
    }

    public void CompleteMission(string missionId)
    {
        var mission = activeMissions.FirstOrDefault(m => m.missionId == missionId);
        if (mission == null) return;

        MissionListUI.Instance?.PlayCompleteThenRemove(mission, () =>
        {
            activeMissions.Remove(mission);
            OnMissionsChanged?.Invoke(activeMissions);
        });
    }

    public bool IsActive(string missionId) => activeMissions.Any(m => m.missionId == missionId);
}