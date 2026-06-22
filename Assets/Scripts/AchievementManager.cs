using UnityEngine;
using System.Collections.Generic;

// Compact, event-driven achievement framework. Each achievement is a
// simple ID + counter + threshold + localised name/description key.
// Game code calls AchievementManager.Notify(id, amount) (e.g. on enemy
// kill, scroll pickup) and the manager tallies it against the
// threshold. When the threshold is reached the achievement unlocks,
// PlayerPrefs records it, and a toast notification fires.
//
// Definitions are in code so we don't need a ScriptableObject pipeline
// — add a new achievement by appending an entry to s_defs.
public static class AchievementManager
{
    public enum Tracker
    {
        EnemyKilled,
        RegionConquered,
        PlayerLevel,
        BossSlain,
        ScrollFound,
        PerfectDodge,
        DiamondsEarned,
        NewGamePlusStarted,
    }

    private struct AchDef
    {
        public string id;
        public Tracker tracker;
        public int threshold;
        public string nameKey;
    }

    private static readonly AchDef[] s_defs = new[]
    {
        new AchDef { id = "FIRST_BLOOD",      tracker = Tracker.EnemyKilled,        threshold = 1,    nameKey = "ACH_FIRST_BLOOD" },
        new AchDef { id = "FIRST_REGION",     tracker = Tracker.RegionConquered,    threshold = 1,    nameKey = "ACH_FIRST_REGION" },
        new AchDef { id = "FIVE_REGIONS",     tracker = Tracker.RegionConquered,    threshold = 5,    nameKey = "ACH_FIVE_REGIONS" },
        new AchDef { id = "ALL_REGIONS",      tracker = Tracker.RegionConquered,    threshold = 24,   nameKey = "ACH_ALL_REGIONS" },
        new AchDef { id = "LEVEL_10",         tracker = Tracker.PlayerLevel,        threshold = 10,   nameKey = "ACH_LEVEL_10" },
        new AchDef { id = "LEVEL_25",         tracker = Tracker.PlayerLevel,        threshold = 25,   nameKey = "ACH_LEVEL_25" },
        new AchDef { id = "BOSS_SLAIN",       tracker = Tracker.BossSlain,          threshold = 1,    nameKey = "ACH_BOSS_SLAIN" },
        new AchDef { id = "SCROLLS_5",        tracker = Tracker.ScrollFound,        threshold = 5,    nameKey = "ACH_SCROLLS_5" },
        new AchDef { id = "SCROLLS_ALL",      tracker = Tracker.ScrollFound,        threshold = 8,    nameKey = "ACH_SCROLLS_ALL" },
        new AchDef { id = "DODGE_10",         tracker = Tracker.PerfectDodge,       threshold = 10,   nameKey = "ACH_PERFECT_DODGE_10" },
        new AchDef { id = "DIAMOND_HOARDER",  tracker = Tracker.DiamondsEarned,     threshold = 5000, nameKey = "ACH_DIAMOND_HOARDER" },
        new AchDef { id = "NG_PLUS",          tracker = Tracker.NewGamePlusStarted, threshold = 1,    nameKey = "ACH_NG_PLUS" },
    };

    public static System.Action<string, string> OnAchievementUnlocked; // (id, localizedName)

    private const string CounterPrefix = "Ach_Count_";
    private const string UnlockedPrefix = "Ach_Unlocked_";

    // Single entry point: gameplay just says "I killed an enemy" / "I found a
    // scroll" without knowing which achievements care.
    public static void Notify(Tracker tracker, int amount = 1)
    {
        // For tier-tracking achievements (kills/regions/etc.) the counter
        // is shared — every achievement that watches this tracker sees
        // the same total. For per-level achievements we accept a raw
        // value rather than an increment.
        string counterKey = CounterPrefix + tracker;
        int prev = PlayerPrefs.GetInt(counterKey, 0);
        int next;
        switch (tracker)
        {
            case Tracker.PlayerLevel:
                next = Mathf.Max(prev, amount); // amount is absolute level
                break;
            default:
                next = prev + amount;
                break;
        }
        PlayerPrefs.SetInt(counterKey, next);

        for (int i = 0; i < s_defs.Length; i++)
        {
            AchDef d = s_defs[i];
            if (d.tracker != tracker) continue;
            if (IsUnlocked(d.id)) continue;
            if (next >= d.threshold) Unlock(d);
        }

        PlayerPrefs.Save();
    }

    public static bool IsUnlocked(string id) => PlayerPrefs.GetInt(UnlockedPrefix + id, 0) == 1;

    public static int GetUnlockedCount()
    {
        int n = 0;
        for (int i = 0; i < s_defs.Length; i++) if (IsUnlocked(s_defs[i].id)) n++;
        return n;
    }

    public static int TotalCount => s_defs.Length;

    public static IEnumerable<(string id, string nameKey, bool unlocked)> All()
    {
        for (int i = 0; i < s_defs.Length; i++)
        {
            yield return (s_defs[i].id, s_defs[i].nameKey, IsUnlocked(s_defs[i].id));
        }
    }

    private static void Unlock(AchDef d)
    {
        PlayerPrefs.SetInt(UnlockedPrefix + d.id, 1);
        string locName = LocalizationManager.Tr(d.nameKey);
        OnAchievementUnlocked?.Invoke(d.id, locName);
        ToastManager.Show(LocalizationManager.Tr("TOAST_ACHIEVEMENT", locName), ToastManager.ToastKind.Achievement);
    }

    // ----------------- DEBUG -----------------
    public static void ResetAllForDebug()
    {
        for (int i = 0; i < s_defs.Length; i++)
        {
            PlayerPrefs.DeleteKey(UnlockedPrefix + s_defs[i].id);
        }
        // Counters
        foreach (Tracker t in System.Enum.GetValues(typeof(Tracker)))
            PlayerPrefs.DeleteKey(CounterPrefix + t);
        PlayerPrefs.Save();
    }
}
