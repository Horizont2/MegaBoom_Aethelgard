using UnityEngine;

// Small static helper that lets any camp AI ask "what time of day is it?"
// without threading DayNightCycle references through every script.
// Caches the DayNightCycle on first lookup — the value is a scene-lifetime
// singleton in practice, so a stale cache is only possible on scene reload
// which invalidates the reference automatically.
public static class CampSchedule
{
    private static DayNightCycle cached;

    public static float TimeOfDay()
    {
        var dnc = GetCycle();
        return dnc != null ? dnc.timeOfDay : 12f;
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
        if (cached != null) return cached;
        cached = Object.FindFirstObjectByType<DayNightCycle>();
        return cached;
    }
}
