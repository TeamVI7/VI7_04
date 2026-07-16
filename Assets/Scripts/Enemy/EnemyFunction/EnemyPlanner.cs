using UnityEngine;

public static class EnemyFormationCoordinator
{
    private const int SlotCount = 12;
    private static readonly bool[] _occupied = new bool[SlotCount];

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetOnLoad()
    {
        for (int i = 0; i < SlotCount; i++) _occupied[i] = false;
    }

    public static int Register()
    {
        for (int i = 0; i < SlotCount; i++)
        {
            if (!_occupied[i])
            {
                _occupied[i] = true;
                return i;
            }
        }
        return -1;
    }

    public static void Unregister(int slot)
    {
        if (slot >= 0 && slot < SlotCount) _occupied[slot] = false;
    }

    public static Vector3 GetSlotDirection(int slot)
    {
        if (slot < 0) return Vector3.forward;
        float angle = slot * (360f / SlotCount);
        return Quaternion.Euler(0f, angle, 0f) * Vector3.forward;
    }
}