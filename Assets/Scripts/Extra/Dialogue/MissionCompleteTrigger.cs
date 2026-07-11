// MissionCompleteTrigger.cs — completes a mission by id on enter
// (e.g. put this on the generator's actual "activate" interactable, not the doorway)
using UnityEngine;

public class MissionCompleteTrigger : MonoBehaviour
{
    [SerializeField] private string missionId;
    [SerializeField] private TriggerZone zone;

    void Awake() => zone.OnPlayerEnter.AddListener(
        () => MissionManager.Instance.CompleteMission(missionId));
}