using UnityEngine;

public static class EnemySquadCoordinator
{
    public static int MaxConcurrentShooters = 3;
    public static float DetectBarkCooldown = 0.35f;

    private static int   _activeShooters;
    private static float _lastDetectBarkTime = float.NegativeInfinity;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetOnLoad()
    {
        _activeShooters = 0;
        _lastDetectBarkTime = float.NegativeInfinity;
    }

    public static bool TryAcquireFireSlot()
    {
        if (_activeShooters >= MaxConcurrentShooters) return false;
        _activeShooters++;
        return true;
    }

    public static void ReleaseFireSlot()
    {
        if (_activeShooters > 0) _activeShooters--;
    }

    public static bool TryPlayDetectBark()
    {
        if (Time.time - _lastDetectBarkTime < DetectBarkCooldown) return false;
        _lastDetectBarkTime = Time.time;
        return true;
    }
}