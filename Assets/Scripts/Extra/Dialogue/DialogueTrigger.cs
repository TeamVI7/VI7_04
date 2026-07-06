using UnityEngine;

public class DialogueTrigger : MonoBehaviour
{
    [SerializeField] private DialogueData dialogue;
    [SerializeField] private TriggerZone zone;

    private void Awake() => zone.OnPlayerEnter.AddListener(Fire);

    private void Fire() => DialogueManager.Instance.Play(dialogue);
}