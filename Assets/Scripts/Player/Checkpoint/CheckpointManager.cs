using UnityEngine;

public class CheckpointManager : MonoBehaviour
{
    [SerializeField] float holdBlackScreen = 0.5f; // extra beat after fade-to-black before respawn

    Vector3    _checkpointPos;
    Quaternion _checkpointRot;
    bool       _hasCheckpoint;

    public static CheckpointManager Instance { get; private set; }

    void Awake()
    {
        Instance = this;
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    void OnEnable()  => PlayerHealth.OnDied += HandleDeath;
    void OnDisable() => PlayerHealth.OnDied -= HandleDeath;

    public void SetCheckpoint(Vector3 pos, Quaternion rot)
    {
        _checkpointPos = pos;
        _checkpointRot = rot;
        _hasCheckpoint = true;
    }

    void HandleDeath()
    {
        if (!_hasCheckpoint) return; // no checkpoint hit yet — death stands, no auto-respawn

        float delay = DeathCamera.Instance.DeathToBlackDuration + holdBlackScreen;
        Invoke(nameof(Respawn), delay);
    }

    void Respawn()
    {
        Debug.Log($"[Checkpoint] respawning at {_checkpointPos}");
        DeathCamera.Instance.Respawn(_checkpointPos, _checkpointRot);
    }
}