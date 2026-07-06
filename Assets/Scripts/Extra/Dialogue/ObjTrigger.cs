using UnityEngine;

public class ObjectiveTrigger : MonoBehaviour
{
    [SerializeField] private ObjectiveData objective;
    [SerializeField] private TriggerZone zone;

    private void Awake() => zone.OnPlayerEnter.AddListener(HandleTrigger);

    private void HandleTrigger()
    {
        DialogueManager.Instance.OnDialogueClosed += FireOnce;
    }

    private void FireOnce()
    {
        DialogueManager.Instance.OnDialogueClosed -= FireOnce;
        ObjectiveManager.Instance.SetObjective(objective);
    }
}