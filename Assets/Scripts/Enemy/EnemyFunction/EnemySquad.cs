using System.Collections.Generic;
using UnityEngine;

public static class EnemySquadCoordinator
{
    public static int MaxConcurrentShooters = 3;
    public static float DetectBarkCooldown = 0.35f;

    // Caps how many enemies can be mid-dodge at once — without this, sweeping the
    // crosshair across a group makes all of them flinch in lockstep, which reads
    // as robotic instead of alive.
    public static int MaxConcurrentDodgers = 2;

    private static int   _activeShooters;
    private static int   _activeDodgers;
    private static float _lastDetectBarkTime = float.NegativeInfinity;

    // Registry for the avenge reaction — a plain list instead of physics overlap
    // since squads are small and this only runs once per death, not per frame.
    private static readonly List<EnemyAvengeReaction> _avengers = new List<EnemyAvengeReaction>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetOnLoad()
    {
        _activeShooters = 0;
        _activeDodgers = 0;
        _lastDetectBarkTime = float.NegativeInfinity;
        _avengers.Clear();
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

    public static bool TryAcquireDodgeSlot()
    {
        if (_activeDodgers >= MaxConcurrentDodgers) return false;
        _activeDodgers++;
        return true;
    }

    public static void ReleaseDodgeSlot()
    {
        if (_activeDodgers > 0) _activeDodgers--;
    }

    public static bool TryPlayDetectBark()
    {
        if (Time.time - _lastDetectBarkTime < DetectBarkCooldown) return false;
        _lastDetectBarkTime = Time.time;
        return true;
    }

    public static void RegisterAvenger(EnemyAvengeReaction reaction)
    {
        if (!_avengers.Contains(reaction)) _avengers.Add(reaction);
    }

    public static void UnregisterAvenger(EnemyAvengeReaction reaction)
    {
        _avengers.Remove(reaction);
    }

    public static void NotifyAllyDied(Vector3 deathPosition, EnemyAvengeReaction dead, float radius)
    {
        float sqrRadius = radius * radius;
        for (int i = 0; i < _avengers.Count; i++)
        {
            EnemyAvengeReaction a = _avengers[i];
            if (a == null || a == dead) continue;
            if ((a.transform.position - deathPosition).sqrMagnitude > sqrRadius) continue;
            a.ReceiveAvengeSignal();
        }
    }
}