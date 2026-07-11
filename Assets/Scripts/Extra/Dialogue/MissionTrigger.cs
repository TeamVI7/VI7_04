// MissionTrigger.cs — add mission on enter
using UnityEngine;

public class MissionTrigger : MonoBehaviour
{
    [SerializeField] private MissionData mission;
    [SerializeField] private TriggerZone zone;

    void Awake() => zone.OnPlayerEnter.AddListener(
        () => MissionManager.Instance.AddMission(mission));
}