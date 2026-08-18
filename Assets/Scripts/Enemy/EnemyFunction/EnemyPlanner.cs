using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Hands each aggro'd enemy a distinct approach angle so a squad spreads around
/// the player instead of every member beelining the same point.
///
/// Slots are unbounded: the ring has a fixed angular RESOLUTION, but enemies past
/// that count wrap onto a second lap offset by half a step, interleaving with the
/// first rather than stacking on it. That matters because a wave spawner can put
/// far more than one ring's worth of enemies on the field at once — the old fixed
/// 12-slot array handed everyone after the twelfth a -1, and PatrolBehaviour reads
/// -1 as "no slot, walk straight at the player", so a big wave silently collapsed
/// back into the exact beeline this class exists to prevent.
/// </summary>
public static class EnemyFormationCoordinator
{
    /// <summary>Distinct angles in one lap of the ring. Enemies beyond this count
    /// interleave on subsequent laps rather than being turned away.</summary>
    public const int RingResolution = 12;

    private static readonly List<bool> _occupied = new List<bool>();

    // Caps how many enemies simultaneously path to attack from behind the player
    // instead of holding a front-facing ring slot — keeps flanking a spotlighted
    // tactic instead of the default for every enemy at once.
    public static int MaxFlankers = 1;
    private static int _activeFlankers;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetOnLoad()
    {
        _occupied.Clear();
        _activeFlankers = 0;
    }

    public static bool TryClaimFlank()
    {
        if (_activeFlankers >= MaxFlankers) return false;
        _activeFlankers++;
        return true;
    }

    public static void ReleaseFlank()
    {
        if (_activeFlankers > 0) _activeFlankers--;
    }

    /// <summary>Claims the lowest free slot, growing the ring if every existing one
    /// is taken. Never fails — callers no longer need a -1 path.</summary>
    public static int Register()
    {
        for (int i = 0; i < _occupied.Count; i++)
        {
            if (!_occupied[i])
            {
                _occupied[i] = true;
                return i;
            }
        }

        _occupied.Add(true);
        return _occupied.Count - 1;
    }

    public static void Unregister(int slot)
    {
        if (slot >= 0 && slot < _occupied.Count) _occupied[slot] = false;
    }

    /// <summary>
    /// Stable approach direction for a slot. Successive laps are offset by half a
    /// step, so slot 12 sits between slots 0 and 1 rather than on top of slot 0 —
    /// a second lap of enemies fills the gaps in the first instead of doubling up.
    /// </summary>
    public static Vector3 GetSlotDirection(int slot)
    {
        if (slot < 0) return Vector3.forward;

        const float step = 360f / RingResolution;
        int lap   = slot / RingResolution;
        int index = slot % RingResolution;

        float angle = index * step + lap * step * 0.5f;
        return Quaternion.Euler(0f, angle, 0f) * Vector3.forward;
    }
}
