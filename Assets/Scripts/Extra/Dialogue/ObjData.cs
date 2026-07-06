using UnityEngine;

[CreateAssetMenu(menuName = "Objectives/Objective")]
public class ObjectiveData : ScriptableObject
{
    public string objectiveText;        // e.g. "Active the generator"
    public string[] roomIdsToHighlight; // e.g. { "EnergyProcessing" }
}