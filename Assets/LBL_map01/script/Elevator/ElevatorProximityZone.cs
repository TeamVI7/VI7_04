using UnityEngine;

public class ElevatorProximityZone : MonoBehaviour
{
    private ElevatorController elevator;

    void Start()
    {
        elevator = GetComponentInParent<ElevatorController>();

        if (elevator == null)
            Debug.LogError("ElevatorProximityZone: Không tìm thấy ElevatorController ở parent!", this);
    }

    void OnTriggerEnter(Collider other)
    {
        if (elevator == null || !other.CompareTag("Player")) return;
        elevator.OnPlayerEnterZone();
    }

    void OnTriggerExit(Collider other)
    {
        if (elevator == null || !other.CompareTag("Player")) return;
        elevator.OnPlayerExitZone();
    }
}