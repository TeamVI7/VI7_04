using UnityEngine;

// =============================================================================
//  FPSDebug.cs
//  Centralized debug logger for the entire FPS system.
//  Toggle FPSDebug.GlobalEnabled at runtime or set it false for builds.
//
//  Usage:
//      FPSDebug.Log("Weapon fired", this);
//      FPSDebug.LogWarning("No ammo", this);
//      FPSDebug.LogError("Missing WeaponData", this);   // always shows
// =============================================================================

public static class FPSDebug
{
#if UNITY_EDITOR
    public static bool GlobalEnabled = true;
#else
    public static bool GlobalEnabled = false;   // silent in builds by default
#endif

    private const string Prefix = "[FPS] ";

    public static void Log(string message, Object context = null)
    {
        if (!GlobalEnabled) return;
        Debug.Log(Prefix + message, context);
    }

    public static void LogWarning(string message, Object context = null)
    {
        if (!GlobalEnabled) return;
        Debug.LogWarning(Prefix + message, context);
    }

    /// <summary>Errors always appear regardless of GlobalEnabled.</summary>
    public static void LogError(string message, Object context = null)
    {
        Debug.LogError(Prefix + message, context);
    }
}