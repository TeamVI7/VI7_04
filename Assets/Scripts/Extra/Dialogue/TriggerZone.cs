using UnityEngine;
using UnityEngine.Events;

[RequireComponent(typeof(Collider))]
public class TriggerZone : MonoBehaviour
{
    [SerializeField] private string playerTag = "Player";
    [SerializeField] private bool oneShot = true;

    public UnityEvent OnPlayerEnter;

    private bool triggered;

    private void Reset()
    {
        GetComponent<Collider>().isTrigger = true;
    }

    private void OnTriggerEnter(Collider other)
    {
        if (triggered && oneShot) return;
        if (!other.CompareTag(playerTag)) return;

        triggered = true;
        OnPlayerEnter?.Invoke();
    }
}