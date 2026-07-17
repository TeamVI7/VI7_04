using UnityEngine;

[RequireComponent(typeof(Collider))]
public class CheckpointZone : MonoBehaviour
{
    [SerializeField] bool oneShot = true;

    bool _used;

    void OnTriggerEnter(Collider other)
    {
        if (oneShot && _used) return;
        if (!other.CompareTag("Player")) return;

        Quaternion uprightRot = Quaternion.Euler(0f, transform.eulerAngles.y, 0f);
        CheckpointManager.Instance.SetCheckpoint(transform.position, uprightRot);
        Debug.Log($"[Checkpoint] set at {transform.position}");
        _used = true;
    }
}