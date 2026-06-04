using UnityEngine;

public class TriggerRelay : MonoBehaviour
{
    public ServerMinigameManager manager;

    private void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag("Player"))
            manager.OnPlayerEnterTrigger();
    }
}