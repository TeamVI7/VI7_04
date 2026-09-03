using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

/// <summary>
/// All disk I/O for the save system. Nothing above this layer knows where files
/// live or what format they are in.
///
/// Layout under Application.persistentDataPath:
///
///     profile.json        campaign progress — unlocks, best times, board rank
///     saves/auto.json     autosave, rewritten at every checkpoint
///     saves/slot_1.json   manual saves from the pause menu
///     saves/slot_2.json
///     ...
///
/// Writes are atomic: the payload goes to a .tmp file that is flushed and closed
/// before it replaces the real one. A crash or a pulled power cable mid-write can
/// therefore lose the *new* save, but can never leave a half-written file where a
/// good one used to be — which is the failure that actually ruins a playthrough.
/// </summary>
public static class SaveStorage
{
    public const int AutoSlot    = 0;
    public const int ManualSlots = 3;

    private const string SavesFolder  = "saves";
    private const string ProfileFile  = "profile.json";
    private const string AutoFile     = "auto.json";
    private const string TempSuffix   = ".tmp";
    private const string BackupSuffix = ".bak";

    // ── Paths ────────────────────────────────────────────────────────────────

    private static string Root       => Application.persistentDataPath;
    private static string SavesDir   => Path.Combine(Root, SavesFolder);
    public  static string ProfilePath => Path.Combine(Root, ProfileFile);

    public static bool IsValidSlot(int slot) => slot >= AutoSlot && slot <= ManualSlots;

    public static string SlotPath(int slot) =>
        Path.Combine(SavesDir, slot == AutoSlot ? AutoFile : $"slot_{slot}.json");

    /// <summary>Label for a slot as the UI should show it.</summary>
    public static string SlotLabel(int slot) =>
        slot == AutoSlot ? "AUTOSAVE" : $"SLOT {slot}";

    // ── Queries ──────────────────────────────────────────────────────────────

    public static bool SlotExists(int slot) => IsValidSlot(slot) && File.Exists(SlotPath(slot));

    public static IEnumerable<int> AllSlots()
    {
        for (int i = AutoSlot; i <= ManualSlots; i++) yield return i;
    }

    // ── Save ─────────────────────────────────────────────────────────────────

    public static bool WriteSlot(int slot, SaveData data)
    {
        if (!IsValidSlot(slot))
        {
            Debug.LogError($"[SaveStorage] Refusing to write slot {slot} — out of range.");
            return false;
        }
        if (data == null)
        {
            Debug.LogError("[SaveStorage] Refusing to write a null SaveData.");
            return false;
        }

        return WriteJson(SlotPath(slot), JsonUtility.ToJson(data, prettyPrint: true));
    }

    public static bool WriteProfile(ProfileData profile)
    {
        if (profile == null) return false;
        return WriteJson(ProfilePath, JsonUtility.ToJson(profile, prettyPrint: true));
    }

    // ── Load ─────────────────────────────────────────────────────────────────

    /// <summary>Reads a slot, or null if it is missing, unreadable, or not valid JSON.
    /// Never throws — a corrupt save should degrade to "no save", not crash the menu.</summary>
    public static SaveData ReadSlot(int slot)
    {
        if (!IsValidSlot(slot)) return null;

        string json = ReadJson(SlotPath(slot));
        if (string.IsNullOrEmpty(json)) return null;

        try
        {
            SaveData data = JsonUtility.FromJson<SaveData>(json);

            // FromJson returns a default-constructed object for JSON that parses but
            // isn't this shape at all (e.g. "{}" or a file from another system), so an
            // empty scene name is the real "this is not a usable save" signal.
            if (data == null || string.IsNullOrEmpty(data.sceneName))
            {
                Debug.LogWarning($"[SaveStorage] {SlotLabel(slot)} parsed but has no scene — treating as empty.");
                return null;
            }

            return data;
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[SaveStorage] {SlotLabel(slot)} is corrupt and will be ignored: {e.Message}");
            return null;
        }
    }

    public static ProfileData ReadProfile()
    {
        string json = ReadJson(ProfilePath);
        if (string.IsNullOrEmpty(json)) return new ProfileData();

        try
        {
            ProfileData profile = JsonUtility.FromJson<ProfileData>(json);
            return profile ?? new ProfileData();
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[SaveStorage] profile.json is corrupt, starting a fresh profile: {e.Message}");
            return new ProfileData();
        }
    }

    // ── Delete ───────────────────────────────────────────────────────────────

    public static bool DeleteSlot(int slot)
    {
        if (!SlotExists(slot)) return false;

        try
        {
            string path = SlotPath(slot);
            File.Delete(path);

            // The .bak is the previous good copy of the slot that was just deleted. Leaving
            // it behind means a deleted save still has a shadow on disk, and the next write
            // to this slot would sit next to a stale backup from a different playthrough.
            string backup = path + BackupSuffix;
            if (File.Exists(backup)) File.Delete(backup);

            return true;
        }
        catch (Exception e)
        {
            Debug.LogError($"[SaveStorage] Could not delete {SlotLabel(slot)}: {e.Message}");
            return false;
        }
    }

    /// <summary>Wipes every save but keeps the profile — "New Deployment" should reset
    /// the run, not the player's unlocks and records.</summary>
    public static void DeleteAllSlots()
    {
        foreach (int slot in AllSlots()) DeleteSlot(slot);
    }

    // ── Primitives ───────────────────────────────────────────────────────────

    private static bool WriteJson(string path, string json)
    {
        string tmp = path + TempSuffix;

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));

            // Separate write-then-swap. WriteAllText on the real path would truncate
            // the existing file before the new bytes land.
            File.WriteAllText(tmp, json);

            if (File.Exists(path))
            {
                // File.Replace is the atomic swap on both Windows and modern Unity
                // players. The .bak it leaves behind is the previous good save, which
                // is worth keeping around anyway.
                File.Replace(tmp, path, path + BackupSuffix);
            }
            else
            {
                File.Move(tmp, path);
            }

            return true;
        }
        catch (Exception e)
        {
            Debug.LogError($"[SaveStorage] Write failed for '{path}': {e.Message}");

            // Don't leave a stray .tmp sitting next to a good save.
            try { if (File.Exists(tmp)) File.Delete(tmp); } catch { /* nothing useful to do */ }

            return false;
        }
    }

    private static string ReadJson(string path)
    {
        try
        {
            if (!File.Exists(path)) return null;
            return File.ReadAllText(path);
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[SaveStorage] Read failed for '{path}': {e.Message}");
            return null;
        }
    }
}
