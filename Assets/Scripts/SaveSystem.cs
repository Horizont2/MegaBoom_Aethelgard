using System;
using System.IO;
using UnityEngine;

// ================================================================
//   AAA-style save system — file-based JSON at persistentDataPath.
// ================================================================
//
// Replaces (long-term) the ~120 PlayerPrefs sites scattered across the
// project. PlayerPrefs is:
//   - Not atomic → crash between two SetInt = corruption.
//   - Not versioned → adding a field breaks old saves.
//   - Not portable → doesn't move between machines (Steam Cloud only
//     covers files, not the Windows registry / macOS defaults that
//     PlayerPrefs lives in).
//
// New model:
//   - ONE file: <persistentDataPath>/save_v1.json
//   - Backed up before overwrite (save_v1.json.bak) so a mid-write
//     crash still leaves a readable version.
//   - Versioned via GameSave.saveVersion — Load() migrates or resets.
//   - Steam Auto-Cloud picks the whole persistentDataPath folder up
//     via a single glob in the Steamworks web dashboard (*.json).
//
// This file is INTENTIONALLY additive: the existing PlayerPrefs code
// keeps working. Callers that want the AAA path use SaveSystem; older
// call-sites migrate on their own schedule.
public static class SaveSystem
{
    private const int SAVE_VERSION = 1;
    private const string FILE_NAME = "save_v1.json";
    private const string BACKUP_NAME = "save_v1.json.bak";

    private static GameSave s_cached;
    private static bool s_loaded;

    public static GameSave Current
    {
        get
        {
            if (!s_loaded) Load();
            return s_cached;
        }
    }

    // -------------------------------------------------------------
    //  Load
    // -------------------------------------------------------------
    public static void Load()
    {
        s_loaded = true;
        string path = FilePath();
        string backup = BackupPath();

        s_cached = TryReadFile(path) ?? TryReadFile(backup) ?? MigrateFromPlayerPrefs();
        if (s_cached == null) s_cached = new GameSave { saveVersion = SAVE_VERSION };

        // Version migration hook — bump SAVE_VERSION when the schema
        // changes and add explicit up-migrators here.
        if (s_cached.saveVersion < SAVE_VERSION)
        {
            GameLog.Info($"[SaveSystem] Migrating save {s_cached.saveVersion} → {SAVE_VERSION}");
            s_cached.saveVersion = SAVE_VERSION;
            Save();
        }
    }

    private static GameSave TryReadFile(string path)
    {
        if (!File.Exists(path)) return null;
        try
        {
            string json = File.ReadAllText(path);
            var save = JsonUtility.FromJson<GameSave>(json);
            if (save == null) return null;
            return save;
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[SaveSystem] {path} unreadable: {e.Message}");
            return null;
        }
    }

    // First launch after the JSON-save migration lands: pull the
    // legacy PlayerPrefs values into the new format so nobody loses
    // their camp progress. Safe to call repeatedly — Save() overwrites
    // the file with whatever's in memory after this.
    private static GameSave MigrateFromPlayerPrefs()
    {
        if (!PlayerPrefs.HasKey("Stash_Wood") && !PlayerPrefs.HasKey("PlayerDiamonds")
            && !PlayerPrefs.HasKey("TotalConqueredRegions")) return null;

        var save = new GameSave
        {
            saveVersion = SAVE_VERSION,
            stashWood = PlayerPrefs.GetInt("Stash_Wood", 0),
            stashStone = PlayerPrefs.GetInt("Stash_Stone", 0),
            stashFood = PlayerPrefs.GetInt("Stash_Food", 0),
            diamonds = PlayerPrefs.GetInt("PlayerDiamonds", 0),
            totalConqueredRegions = PlayerPrefs.GetInt("TotalConqueredRegions", 0),
            mapSeed = PlayerPrefs.GetInt("MapSeed", 0),
            hasCampSave = PlayerPrefs.GetInt("HasCampSave", 0) == 1,
            campPosX = PlayerPrefs.GetFloat("CampPosX", 0f),
            campPosY = PlayerPrefs.GetFloat("CampPosY", 0f),
            campPosZ = PlayerPrefs.GetFloat("CampPosZ", 0f),
            tutorialCompleted = PlayerPrefs.GetInt("TutorialCompleted", 0) == 1,
            campTutorialPlayed = PlayerPrefs.GetInt("CampTutorialPlayed", 0) == 1,
        };
        GameLog.Info("[SaveSystem] Migrated legacy PlayerPrefs → JSON save.");
        return save;
    }

    // -------------------------------------------------------------
    //  Save (atomic-ish: write .tmp → move → keep .bak)
    // -------------------------------------------------------------
    public static void Save()
    {
        if (s_cached == null) return;
        s_cached.saveVersion = SAVE_VERSION;
        string path = FilePath();
        string tmp = path + ".tmp";
        string backup = BackupPath();
        try
        {
            string json = JsonUtility.ToJson(s_cached, prettyPrint: true);
            File.WriteAllText(tmp, json);
            // Backup previous version before overwrite so a crash
            // between File.Delete and File.Move still leaves .bak.
            if (File.Exists(path))
            {
                if (File.Exists(backup)) File.Delete(backup);
                File.Copy(path, backup);
            }
            if (File.Exists(path)) File.Delete(path);
            File.Move(tmp, path);
        }
        catch (Exception e)
        {
            Debug.LogError($"[SaveSystem] Save failed: {e.Message}");
        }
    }

    // Wipe — used by "New Game" flow.
    public static void Reset()
    {
        s_cached = new GameSave { saveVersion = SAVE_VERSION };
        Save();
    }

    private static string FilePath() => Path.Combine(Application.persistentDataPath, FILE_NAME);
    private static string BackupPath() => Path.Combine(Application.persistentDataPath, BACKUP_NAME);
}

// Everything that lives in the save file. Add new fields at the END —
// removing / renaming existing fields is a version bump.
[Serializable]
public class GameSave
{
    public int saveVersion = 1;

    // === Stash resources ===
    public int stashWood;
    public int stashStone;
    public int stashFood;
    public int diamonds;

    // === Progression ===
    public int totalConqueredRegions;
    public int mapSeed;
    public bool hasCampSave;
    public float campPosX;
    public float campPosY;
    public float campPosZ;

    // === Tutorial flags ===
    public bool tutorialCompleted;
    public bool campTutorialPlayed;

    // === Achievements (bitfield string of unlocked achievement keys) ===
    public string unlockedAchievements = "";
}
