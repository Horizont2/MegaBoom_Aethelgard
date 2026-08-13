using UnityEngine;

// Central reader for the accessibility / gameplay settings toggles.
//
// Before this, these keys were written by SettingsUI when the player
// flipped a toggle but read by NOTHING — "phantom" settings that looked
// functional but did nothing (worst of all for Photosensitivity, which
// is a safety feature). Every consumer now routes through here so the
// toggles actually take effect.
//
// PlayerPrefs.GetInt/GetFloat read from an in-memory cache in Unity
// (only writes touch disk), so direct reads even from per-frame code
// paths are cheap — no manual caching needed.
public static class GameplaySettings
{
    // Reduce non-essential motion: camera shake, screen wobble, big
    // cinematic camera moves. Default OFF (full motion).
    public static bool ReduceMotion => PlayerPrefs.GetInt("Settings_ReduceMotion", 0) == 1;

    // Suppress / heavily dim bright full-screen flashes + strobes
    // (damage flash, glory-kill flash, intro strobe). Default OFF.
    public static bool Photosensitive => PlayerPrefs.GetInt("Settings_Photosensitivity", 0) == 1;

    // Boost UI / damage-number / telegraph contrast. Default OFF.
    public static bool HighContrast => PlayerPrefs.GetInt("Settings_HighContrast", 0) == 1;

    // Gamepad rumble on hits / dodges / boss beats. Default ON.
    public static bool ControllerVibration => PlayerPrefs.GetInt("Settings_ControllerVibration", 1) == 1;

    // Periodic camp autosave heartbeat. Default ON. (On-quit save is
    // separate and always runs — this only gates the 30s heartbeat.)
    public static bool AutoSave => PlayerPrefs.GetInt("Settings_AutoSave", 1) == 1;

    // Show a translucent box behind subtitle text for legibility.
    // Default ON.
    public static bool SubtitleBackground => PlayerPrefs.GetInt("Settings_SubtitleBg", 1) == 1;

    // Grenade aim input mode. The toggle key is historically named
    // "HoldToggleSprint" (there is no sprint in this game); it now
    // controls whether the grenade aim is HELD (0, default) or TOGGLED
    // (1). Relabel the toggle text to "Hold to Aim" in the settings
    // prefab to match.
    public static bool GrenadeAimHold => PlayerPrefs.GetInt("Settings_HoldToggleSprint", 0) == 0;

    // Aim-assist strength 0..1 for grenade throws (snaps the aim toward
    // the nearest enemy within a cone). 0 = off. Default 0.
    public static float AimAssist => Mathf.Clamp01(PlayerPrefs.GetFloat("Settings_AimAssist", 0f));

    // Convenience: a global motion scalar callers can multiply into
    // shake amplitudes etc. 1 = full, 0.2 = reduced.
    public static float MotionScale => ReduceMotion ? 0.2f : 1f;

    // Convenience: a flash-alpha scalar. 0 when photosensitive-safe so
    // callers can multiply it into their flash colours to kill strobes.
    public static float FlashScale => Photosensitive ? 0f : 1f;
}
