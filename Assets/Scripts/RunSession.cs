using UnityEngine;

// Per-RUN scoreboard, distinct from RunStats (which is persistent /
// career total). The player sees these values on the death recap so
// they know what this specific run earned before falling.
//
// Reset by Begin() when a run starts (mission enter / arena load).
// Read on death, then optionally rolled into RunStats via
// CommitToCareer() so the career totals stay accurate too.
//
// Deliberately does NOT track wood / stone / food — those resources
// are lost on death anyway; showing "Wood Gathered: 400" next to
// "You lost it all" would just twist the knife.
public static class RunSession
{
    private static float s_startTime;
    private static bool s_active;

    public static int Kills;
    public static int Elites;
    public static int Bosses;
    public static int DiamondsEarned;
    public static int LevelUps;
    public static int PerfectDodges;
    public static int RegionsConquered;
    public static int MissionsCompleted;
    public static int ScrollsFound;
    public static int MaxLevelReached;
    public static string LastDamageSource;

    // Seconds spent in this run — unscaled so slow-mo / pause don't
    // deflate it.
    public static float SecondsElapsed => s_active ? Time.unscaledTime - s_startTime : 0f;
    public static bool IsActive => s_active;

    // Call when a run starts (mission load / arena spawn).
    public static void Begin()
    {
        s_active = true;
        s_startTime = Time.unscaledTime;
        Kills = 0;
        Elites = 0;
        Bosses = 0;
        DiamondsEarned = 0;
        LevelUps = 0;
        PerfectDodges = 0;
        RegionsConquered = 0;
        MissionsCompleted = 0;
        ScrollsFound = 0;
        MaxLevelReached = 1;
        LastDamageSource = null;
    }

    public static void End() => s_active = false;

    // Fold this run's counters into the persistent career stats.
    // Called on death recap close AND on successful extract.
    public static void CommitToCareer()
    {
        if (!s_active) return;
        RunStats.Add(RunStats.Stat.EnemiesKilled, Kills);
        RunStats.Add(RunStats.Stat.ElitesKilled, Elites);
        RunStats.Add(RunStats.Stat.BossesKilled, Bosses);
        RunStats.Add(RunStats.Stat.DiamondsEarned, DiamondsEarned);
        RunStats.Add(RunStats.Stat.LevelUps, LevelUps);
        RunStats.Add(RunStats.Stat.PerfectDodges, PerfectDodges);
        RunStats.Add(RunStats.Stat.RegionsConquered, RegionsConquered);
        RunStats.Add(RunStats.Stat.MissionsCompleted, MissionsCompleted);
        RunStats.Add(RunStats.Stat.ScrollsFound, ScrollsFound);
    }

    // Increment helpers (fluent-ish). Each is a one-liner at call sites
    // so gameplay code doesn't have to remember whether the run is
    // active — no-ops until Begin() is called.
    public static void AddKill(bool isElite = false, bool isBoss = false)
    {
        if (!s_active) return;
        Kills++;
        if (isElite) Elites++;
        if (isBoss)  Bosses++;
    }
    public static void AddDiamonds(int n)     { if (s_active && n > 0) DiamondsEarned += n; }
    public static void AddLevelUp(int newLevel = 0) { if (!s_active) return; LevelUps++; if (newLevel > MaxLevelReached) MaxLevelReached = newLevel; }
    public static void AddPerfectDodge()      { if (s_active) PerfectDodges++; }
    public static void AddRegion()            { if (s_active) RegionsConquered++; }
    public static void AddMission()           { if (s_active) MissionsCompleted++; }
    public static void AddScroll()            { if (s_active) ScrollsFound++; }
    public static void NoteDamageSource(string source)
    {
        if (!s_active || string.IsNullOrEmpty(source)) return;
        LastDamageSource = source;
    }
}
