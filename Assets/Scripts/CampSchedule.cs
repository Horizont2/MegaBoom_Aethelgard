using UnityEngine;

// Small static helper that lets any camp AI ask "what time of day is it?"
// without threading DayNightCycle references through every script.
// Caches the DayNightCycle on first lookup — the value is a scene-lifetime
// singleton in practice, so a stale cache is only possible on scene reload
// which invalidates the reference automatically.
//
// When no DayNightCycle exists in the scene (CampScene, ShopScene, menu…),
// CampSchedule falls back to a self-driven synthetic time-of-day so camp
// NPCs still cycle between day work and night gathering. The synthetic
// day length matches DayNightCycle's default (300 s), so time flows the
// same speed in camp as in a mission.
public static class CampSchedule
{
    private const float SYNTHETIC_DAY_SECONDS = 300f;
    // Start the synthetic day at 07:30 so a fresh camp session opens on a
    // morning, not the middle of the night. Deep-night hits ~9-10 minutes
    // in, giving the player time to see the day/night cycle in action.
    private const float SYNTHETIC_START_HOUR = 7.5f;
    private static float syntheticStartRealtime = -1f;
    private static DayNightCycle cached;

    public static float TimeOfDay()
    {
        var dnc = GetCycle();
        if (dnc != null) return dnc.timeOfDay;

        // No cycle in the scene — synthesize one so IsDusk / IsDeepNight
        // actually flip. Uses Time.time (scaled) so camp pauses freeze
        // time-of-day alongside the rest of the game.
        if (syntheticStartRealtime < 0f) syntheticStartRealtime = Time.time;
        float elapsedHours = ((Time.time - syntheticStartRealtime) / SYNTHETIC_DAY_SECONDS) * 24f;
        float t = (SYNTHETIC_START_HOUR + elapsedHours) % 24f;
        if (t < 0f) t += 24f;
        return t;
    }

    // Matches DayNightCycle's own definition (< 5 || > 19).
    public static bool IsNight()
    {
        float t = TimeOfDay();
        return t < 5f || t > 19f;
    }

    // Dusk = the golden-hour window 17-20. Workers still bustle, Elias
    // starts his patrol.
    public static bool IsDusk()
    {
        float t = TimeOfDay();
        return t >= 17f && t <= 20f;
    }

    // Deep night — 21-4 — everyone gathers at the fire.
    public static bool IsDeepNight()
    {
        float t = TimeOfDay();
        return t >= 21f || t < 4f;
    }

    private static DayNightCycle GetCycle()
    {
        // Unity's overloaded == operator returns null for destroyed refs,
        // so a scene reload that tears down the previous DayNightCycle
        // silently re-triggers the FindFirstObjectByType lookup next call.
        if (cached != null) return cached;
        cached = Object.FindFirstObjectByType<DayNightCycle>();
        return cached;
    }
}
