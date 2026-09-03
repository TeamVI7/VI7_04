using System;
using System.Collections.Generic;

/// <summary>
/// Campaign-wide progress, separate from any single save file: which missions are
/// unlocked, best time and rank per mission, total time in the field.
///
/// This is deliberately NOT part of <see cref="SaveData"/>. Loading an old
/// checkpoint should not re-lock a mission the player already beat, and deleting a
/// save should not wipe their records. One profile, many saves.
///
/// Replaces the loose PlayerPrefs keys ("Unlocked_", "BestTime_", "BestRank_",
/// "BoardRank", "Playtime") that BIOSPlayPanel used to read and write directly.
/// </summary>
[Serializable]
public class ProfileData
{
    public const int CurrentVersion = 1;

    public int    version = CurrentVersion;
    public string boardRank = "PAWN";
    public float  totalPlaytimeSeconds;

    /// <summary>Scene names the player has unlocked. Missions flagged
    /// unlockedByDefault do not need to appear here.</summary>
    public List<string> unlockedScenes = new List<string>();

    public List<MissionRecord> records = new List<MissionRecord>();

    // ── Unlocks ──────────────────────────────────────────────────────────────

    public bool IsUnlocked(string sceneName) =>
        !string.IsNullOrEmpty(sceneName) && unlockedScenes.Contains(sceneName);

    /// <summary>Returns true if this actually unlocked something new, so the caller
    /// can decide whether the profile is worth writing back to disk.</summary>
    public bool Unlock(string sceneName)
    {
        if (string.IsNullOrEmpty(sceneName) || unlockedScenes.Contains(sceneName)) return false;

        unlockedScenes.Add(sceneName);
        return true;
    }

    // ── Records ──────────────────────────────────────────────────────────────

    public MissionRecord GetRecord(string sceneName)
    {
        if (string.IsNullOrEmpty(sceneName)) return null;

        foreach (MissionRecord r in records)
            if (r.sceneName == sceneName) return r;

        return null;
    }

    /// <summary>Files a completion. Best time is lowest-wins; the rank stored alongside
    /// is the rank from that best run, not the most recent one. Returns true if this
    /// run beat the existing record.</summary>
    public bool SubmitResult(string sceneName, float timeSeconds, string rank)
    {
        if (string.IsNullOrEmpty(sceneName)) return false;

        MissionRecord record = GetRecord(sceneName);
        if (record == null)
        {
            record = new MissionRecord { sceneName = sceneName };
            records.Add(record);
        }

        record.completions++;

        bool isBest = !record.hasBestTime || timeSeconds < record.bestTimeSeconds;
        if (isBest)
        {
            record.hasBestTime     = true;
            record.bestTimeSeconds = timeSeconds;
            record.bestRank        = rank ?? "";
        }

        return isBest;
    }
}

[Serializable]
public class MissionRecord
{
    public string sceneName = "";
    public bool   hasBestTime;
    public float  bestTimeSeconds;
    public string bestRank = "";
    public int    completions;
}
