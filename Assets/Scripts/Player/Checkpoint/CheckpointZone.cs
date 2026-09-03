using UnityEngine;

/// <summary>
/// A volume that takes a checkpoint when the player walks into it.
///
/// The respawn pose is this object's position and flattened Y rotation by default —
/// assign <see cref="respawnPoint"/> to put the player somewhere other than the
/// trigger itself, which matters for a wide volume where the trigger's own centre
/// could be inside geometry.
/// </summary>
[RequireComponent(typeof(Collider))]
public class CheckpointZone : MonoBehaviour
{
    [Header("Identity")]
    [Tooltip("Stable id recorded in the save. Leave empty to use this GameObject's name. " +
             "Two checkpoints in one level must not share an id.")]
    [SerializeField] private string checkpointId = "";

    [Header("Respawn Pose")]
    [Tooltip("Where the player reappears. Leave empty to respawn at this object's own " +
             "position, facing its forward direction.")]
    [SerializeField] private Transform respawnPoint;

    [Header("Behaviour")]
    [Tooltip("Fire once per level. Off means walking back through re-takes the checkpoint, " +
             "which also re-captures the world as it stands right now.")]
    [SerializeField] private bool oneShot = true;

    [Tooltip("Skip this checkpoint if one has already been taken further along. Prevents " +
             "backtracking through an earlier zone from rewinding progress on the next death.")]
    [SerializeField] private bool ignoreIfAlreadyPassed = false;

    private bool _used;

    /// <summary>
    /// How many of the player's colliders are currently inside this trigger.
    ///
    /// A player is rarely one collider — a body capsule, a ground probe, a crouch volume
    /// all carry the Player tag and each raises its own OnTriggerEnter. Firing per-collider
    /// made a single walk-through capture the checkpoint twice, writing the autosave twice;
    /// the second write pushed the genuinely-previous checkpoint out of the .bak and
    /// replaced it with a copy of the current one, so the backup stopped being a fallback.
    ///
    /// Counting occupancy means the zone fires on the first collider in and re-arms only
    /// once the last one has left.
    /// </summary>
    private int _occupants;

    public string CheckpointId =>
        string.IsNullOrWhiteSpace(checkpointId) ? SaveableId.Resolve(this) : checkpointId;

    private void Reset()
    {
        var col = GetComponent<Collider>();
        if (col != null) col.isTrigger = true;
    }

    // Death deactivates the player GameObject and a restore teleports it away. Both should
    // deliver OnTriggerExit, but a missed one would strand _occupants above zero and leave
    // this zone unable to ever fire again — a far worse failure than an extra reset.
    private void OnEnable()  => PlayerHealth.OnDied += ClearOccupants;
    private void OnDisable() => PlayerHealth.OnDied -= ClearOccupants;

    private void ClearOccupants() => _occupants = 0;

    private void OnTriggerExit(Collider other)
    {
        if (!other.CompareTag("Player")) return;

        _occupants = Mathf.Max(0, _occupants - 1);
    }

    private void OnTriggerEnter(Collider other)
    {
        if (!other.CompareTag("Player")) return;

        // Only the collider that takes occupancy from 0 to 1 counts as the player arriving.
        _occupants++;
        if (_occupants > 1) return;

        if (oneShot && _used) return;

        CheckpointManager manager = CheckpointManager.Instance;
        if (manager == null)
        {
            Debug.LogWarning($"[CheckpointZone] '{name}' fired but there is no CheckpointManager " +
                             "in the scene — the checkpoint was lost.", this);
            return;
        }

        if (ignoreIfAlreadyPassed && manager.HasCheckpoint && manager.CurrentCheckpoint != CheckpointId)
            return;

        GetRespawnPose(out Vector3 pos, out Quaternion rot);
        manager.SetCheckpoint(CheckpointId, pos, rot);

        _used = true;
    }

    private void GetRespawnPose(out Vector3 pos, out Quaternion rot)
    {
        Transform source = respawnPoint != null ? respawnPoint : transform;

        pos = source.position;

        // Flattened: a checkpoint marker tilted to sit flush with a ramp would otherwise
        // respawn the player leaning at the same angle.
        rot = Quaternion.Euler(0f, source.eulerAngles.y, 0f);
    }

    private void OnDrawGizmos()
    {
        Transform source = respawnPoint != null ? respawnPoint : transform;

        Gizmos.color = new Color(0.2f, 0.9f, 1f, 0.75f);
        Gizmos.DrawWireSphere(source.position, 0.4f);
        Gizmos.DrawRay(source.position, Quaternion.Euler(0f, source.eulerAngles.y, 0f) * Vector3.forward * 1.5f);

        if (respawnPoint != null)
            Gizmos.DrawLine(transform.position, respawnPoint.position);
    }
}
