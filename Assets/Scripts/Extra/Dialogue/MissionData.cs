using UnityEngine;

[CreateAssetMenu(menuName = "Missions/Mission")]
public class MissionData : ScriptableObject
{
    public string missionId;      // unique, e.g. "ActivateGenerator"
    [TextArea] public string missionText; // "Activate the generator"
}