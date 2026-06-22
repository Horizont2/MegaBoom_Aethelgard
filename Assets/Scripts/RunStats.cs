using UnityEngine;

// Persistent gameplay statistics — what the player did over their
// entire save. Wired through PlayerPrefs (single global profile for
// now; SaveSlotManager extends this to per-slot below).
//
// Increment via RunStats.Add(Stat, n) from gameplay code. Read via
// RunStats.Get(Stat) in summary screens / codex / death recap.
public static class RunStats
{
    public enum Stat
    {
        EnemiesKilled,
        ElitesKilled,
        BossesKilled,
        RegionsConquered,
        MissionsCompleted,
        DiamondsEarned,
        WoodEarned,
        StoneEarned,
        FoodEarned,
        DeathsCount,
        PerfectDodges,
        DistanceMeters,
        TimePlayedSeconds,
        LevelUps,
        ScrollsFound,
        GrenadesThrown,
        NewGamePlusCount,
    }

    private const string Prefix = "RunStat_";

    public static int Get(Stat s) => PlayerPrefs.GetInt(Prefix + s, 0);

    public static void Add(Stat s, int amount = 1)
    {
        if (amount == 0) return;
        int next = PlayerPrefs.GetInt(Prefix + s, 0) + amount;
        if (next < 0) next = 0;
        PlayerPrefs.SetInt(Prefix + s, next);
        // No PlayerPrefs.Save() per call — Unity flushes on quit and
        // SaveSlotManager.Commit explicitly saves after major events.
    }

    public static void Set(Stat s, int value)
    {
        PlayerPrefs.SetInt(Prefix + s, value);
    }

    public static string GetSummaryText()
    {
        int t = Get(Stat.TimePlayedSeconds);
        int hours = t / 3600;
        int minutes = (t % 3600) / 60;

        return string.Format(
            "Time Played: {0}h {1}m\n" +
            "Enemies Felled: {2}\n" +
            "Elites Felled: {3}\n" +
            "Bosses Felled: {4}\n" +
            "Regions Conquered: {5}\n" +
            "Missions Completed: {6}\n" +
            "Diamonds Earned: {7}\n" +
            "Perfect Dodges: {8}\n" +
            "Scrolls Found: {9}\n" +
            "Level-Ups: {10}\n" +
            "Deaths: {11}",
            hours, minutes,
            Get(Stat.EnemiesKilled),
            Get(Stat.ElitesKilled),
            Get(Stat.BossesKilled),
            Get(Stat.RegionsConquered),
            Get(Stat.MissionsCompleted),
            Get(Stat.DiamondsEarned),
            Get(Stat.PerfectDodges),
            Get(Stat.ScrollsFound),
            Get(Stat.LevelUps),
            Get(Stat.DeathsCount));
    }

    public static void ResetAllForDebug()
    {
        foreach (Stat s in System.Enum.GetValues(typeof(Stat)))
            PlayerPrefs.DeleteKey(Prefix + s);
        PlayerPrefs.Save();
    }
}

// Drop-in MonoBehaviour that ticks TimePlayedSeconds while gameplay is
// running. Stays alive across scenes via DontDestroyOnLoad.
public class RunStatsTimer : MonoBehaviour
{
    private static RunStatsTimer s_instance;
    private float accumulator;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Install()
    {
        if (s_instance != null) return;
        GameObject go = new GameObject("[RunStatsTimer]");
        DontDestroyOnLoad(go);
        s_instance = go.AddComponent<RunStatsTimer>();
    }

    private void Update()
    {
        if (Time.timeScale <= 0.01f) return;
        accumulator += Time.unscaledDeltaTime;
        if (accumulator >= 1f)
        {
            int whole = Mathf.FloorToInt(accumulator);
            accumulator -= whole;
            RunStats.Add(RunStats.Stat.TimePlayedSeconds, whole);
        }
    }
}
