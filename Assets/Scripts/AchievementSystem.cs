using System.Collections.Generic;
using UnityEngine;

// ================================================================
//   Achievement system — 20 base achievements.
// ================================================================
//
// Design goals:
//   1. Zero setup — call AchievementSystem.Unlock("KEY") from
//      anywhere, first time only.
//   2. Persists in the JSON save (GameSave.unlockedAchievements) —
//      not PlayerPrefs.
//   3. Idempotent — repeat unlocks silently skip (no double toast).
//   4. Steam-agnostic — forwards to SteamManager.UnlockAchievement,
//      which no-ops when Steam isn't running.
//   5. Player-facing toast + sound the FIRST time each triggers.
//
// Registration lives in the ACHIEVEMENTS table below — one row per
// achievement, all English + UA copy so it flows through Tr() with
// zero extra loc keys.
public static class AchievementSystem
{
    public struct AchievementDef
    {
        // Key used everywhere — PlayerPrefs / GameSave / Steam API ID.
        public string key;
        // Player-facing title + description.  Passed through Tr().
        public string title;
        public string description;

        public AchievementDef(string k, string t, string d) { key = k; title = t; description = d; }
    }

    private static readonly List<AchievementDef> ACHIEVEMENTS = new List<AchievementDef>
    {
        // === Onboarding beats (early game) ===
        new AchievementDef("FIRST_STEPS",       "First Steps",           "Complete the tutorial."),
        new AchievementDef("HOMESTEAD",         "Homestead",             "Return to the camp for the first time."),
        new AchievementDef("SCOUTS_MAP",        "Scout's Map",           "Upgrade the Scout's Lodge to level 2."),
        new AchievementDef("FIRST_BLOOD",       "First Blood",           "Conquer your first region."),
        new AchievementDef("SUPPLY_LINES",      "Supply Lines",          "Build the Storage Vault."),

        // === Mercenary system ===
        new AchievementDef("FOR_HIRE",          "For Hire",              "Hire your first mercenary."),
        new AchievementDef("MARCH_OF_WAR",      "March of War",          "Send your first army on a campaign."),
        new AchievementDef("VETERANS",          "Veterans",              "Fill your entire mercenary roster (5 units)."),
        new AchievementDef("STRATEGIST",        "Strategist",            "Win an auto-battle with a Siege tactic."),

        // === Mid-game milestones ===
        new AchievementDef("HALFWAY",           "Halfway",               "Conquer 12 regions."),
        new AchievementDef("ALTAR_HUNTER",      "Altar Hunter",          "Purify a roadside altar."),
        new AchievementDef("EXECUTIONER",       "Executioner",           "Perform a Glory Kill on a boss."),
        new AchievementDef("SHOPKEEPER_FRIEND", "The Shopkeeper's Friend", "Spend 500 diamonds in the Shop."),
        new AchievementDef("PERFECT_DODGE",     "Untouchable",           "Land a Perfect Dodge."),
        new AchievementDef("STACK_15",          "Blood in the Air",      "Reach a 15-enemy Stack."),

        // === End-game ===
        new AchievementDef("CITY_SIEGE",        "City Siege",            "Conquer the Citadel Outskirts."),
        new AchievementDef("THRONE_TAKEN",      "The Throne Taken",      "Defeat the Overlord in the Throne Room."),
        new AchievementDef("FULL_MAP",          "Kingdom Restored",      "Conquer every region in Aethelgard."),

        // === Cosmetic / meta ===
        new AchievementDef("LORE_MASTER",       "Lore Master",           "Recover 5 lore scrolls."),
        new AchievementDef("DEEP_POCKETS",      "Deep Pockets",          "Hoard 2000 diamonds at once."),
    };

    // Cached lookup by key.
    private static Dictionary<string, AchievementDef> s_byKey;

    private static void EnsureIndex()
    {
        if (s_byKey != null) return;
        s_byKey = new Dictionary<string, AchievementDef>();
        foreach (var a in ACHIEVEMENTS) s_byKey[a.key] = a;
    }

    // -------------------------------------------------------------
    //  Unlock (main entry point — call from anywhere)
    // -------------------------------------------------------------
    public static void Unlock(string key)
    {
        EnsureIndex();
        if (!s_byKey.TryGetValue(key, out var def))
        {
            Debug.LogWarning($"[AchievementSystem] Unknown key '{key}'. Add it to ACHIEVEMENTS.");
            return;
        }
        if (IsUnlocked(key)) return; // idempotent

        // Persist in the JSON save. Format: comma-separated list of keys
        // for tiny footprint + zero extra fields per achievement.
        var save = SaveSystem.Current;
        if (string.IsNullOrEmpty(save.unlockedAchievements)) save.unlockedAchievements = key;
        else save.unlockedAchievements += "," + key;
        SaveSystem.Save();

        // Forward to Steam (no-op when Steam not running).
        SteamManager.UnlockAchievement(key);

        // Player-facing toast + sound. Achievement class matches the
        // existing ToastKind so it uses the celebratory palette.
        if (ToastManager.Instance != null)
        {
            string body = LocalizationManager.Tr("ACHIEVEMENT_UNLOCKED", LocalizationManager.Tr(def.title));
            ToastManager.Show(body, ToastManager.ToastKind.Achievement);
        }
        if (AudioManager.Instance != null) AudioManager.Instance.PlayUI(AudioID.UI_LevelUp);

        Debug.Log($"[AchievementSystem] Unlocked '{key}': {def.title}");
    }

    public static bool IsUnlocked(string key)
    {
        var save = SaveSystem.Current;
        if (string.IsNullOrEmpty(save.unlockedAchievements)) return false;
        // Fast substring check — keys are unique UPPER_SNAKE, no
        // sub-string collisions between defined achievements.
        string csv = "," + save.unlockedAchievements + ",";
        return csv.Contains("," + key + ",");
    }

    public static int UnlockedCount()
    {
        var save = SaveSystem.Current;
        if (string.IsNullOrEmpty(save.unlockedAchievements)) return 0;
        int count = 1;
        for (int i = 0; i < save.unlockedAchievements.Length; i++)
            if (save.unlockedAchievements[i] == ',') count++;
        return count;
    }

    public static int TotalCount() => ACHIEVEMENTS.Count;

    public static IReadOnlyList<AchievementDef> All()
    {
        return ACHIEVEMENTS;
    }
}
