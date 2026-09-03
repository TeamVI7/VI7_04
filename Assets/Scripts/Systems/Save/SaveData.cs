using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The serialized shape of one save. Written as JSON by <see cref="SaveStorage"/>,
/// produced and consumed by <see cref="SaveSystem"/>.
///
/// Everything here is a plain [Serializable] class with public fields, because
/// JsonUtility ignores properties, private fields, dictionaries and interfaces.
/// Keep it that way — the moment a field stops round-tripping, saves silently
/// lose that piece of state instead of failing loudly.
///
/// VERSIONING: bump <see cref="CurrentVersion"/> whenever a change would make an
/// older file restore *wrongly* (a field's meaning changes, a list's ordering
/// becomes significant). Purely additive fields do not need a bump — JsonUtility
/// leaves them at their defaults when reading an older file.
/// </summary>
[Serializable]
public class SaveData
{
    /// <summary>Bump on breaking layout changes. <see cref="SaveSystem"/> refuses to
    /// restore a file whose version it does not recognise, rather than half-applying it.</summary>
    public const int CurrentVersion = 1;

    // ── Header ───────────────────────────────────────────────────────────────
    // Read on its own by the menu, so keep these first and cheap.

    public int    version = CurrentVersion;
    public string sceneName = "";
    public string missionDisplayName = "";
    public string checkpointId = "";
    public float  playtimeSeconds;
    public long   savedAtUtcTicks;

    // ── Body ─────────────────────────────────────────────────────────────────

    public PlayerSaveState player = new PlayerSaveState();
    public WorldSaveState  world  = new WorldSaveState();

    public DateTime SavedAtUtc => new DateTime(savedAtUtcTicks, DateTimeKind.Utc);

    public DateTime SavedAtLocal => SavedAtUtc.ToLocalTime();

    /// <summary>A file written by a newer build than this one, or by a version whose
    /// layout this build no longer understands.</summary>
    public bool IsCompatible => version == CurrentVersion;
}

[Serializable]
public class PlayerSaveState
{
    public Vector3    position;
    public Quaternion rotation = Quaternion.identity;

    public float health;
    public float maxHealth;

    public int currentWeaponIndex;

    /// <summary>One entry per slot in WeaponSwitcherProcedural.weapons, in list order.</summary>
    public List<WeaponSaveState> weapons = new List<WeaponSaveState>();
}

[Serializable]
public class WeaponSaveState
{
    public int    slotIndex;
    /// <summary>GameObject name of the weapon, recorded purely so a save that no longer
    /// lines up with the scene's weapon list can be diagnosed from the file itself.</summary>
    public string weaponName = "";
    public int    clip;
    public int    reserve;
    public bool   chambered;
    public bool   unlocked;
}

[Serializable]
public class WorldSaveState
{
    public List<SpawnAreaSaveState> spawnAreas       = new List<SpawnAreaSaveState>();
    public List<string>             collectedPickups = new List<string>();
    public List<string>             activeMissions   = new List<string>();

    /// <summary>Escape hatch for anything implementing <see cref="ISaveable"/> —
    /// level scripts, puzzles, doors — without this file needing to know about them.</summary>
    public List<CustomSaveState> custom = new List<CustomSaveState>();
}

[Serializable]
public class SpawnAreaSaveState
{
    public string id = "";
    public bool   hasFired;
    public bool   cleared;
    public int    waveIndex = -1;
}

[Serializable]
public class CustomSaveState
{
    public string id   = "";
    public string json = "";
}
