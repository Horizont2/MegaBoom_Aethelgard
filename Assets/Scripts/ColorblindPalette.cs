using UnityEngine;

// Colorblind-safe recolour helper. The accessibility toggle used to
// write Settings_Colorblind to PlayerPrefs and nothing read it back —
// shipping an unimplemented accessibility promise is worse than not
// shipping the toggle at all. This helper reads the flag and remaps
// red/green damage/health colours to a deuteranopia-friendly
// orange/blue pair. Any UI that shows damage / health / status colour
// swaps should ask through here instead of hard-coding Color.red /
// Color.green.
public static class ColorblindPalette
{
    public static bool Enabled => PlayerPrefs.GetInt("Settings_Colorblind", 0) == 1;

    // "Red" — enemy damage popups, low-HP tint, negative status.
    // CVD-safe orange (well separated from the blue below on all three
    // common colour-vision deficiencies).
    public static Color Danger => Enabled
        ? new Color(1.00f, 0.55f, 0.10f, 1f)    // vivid orange
        : new Color(0.90f, 0.20f, 0.20f, 1f);   // red

    // "Green" — heal popups, positive status, safe.
    public static Color Safe => Enabled
        ? new Color(0.20f, 0.55f, 1.00f, 1f)    // cyan-blue
        : new Color(0.40f, 0.85f, 0.35f, 1f);   // green

    // "Yellow / gold" — neutral / caution. Kept the same in both modes
    // since yellow reads correctly under all CVD types.
    public static Color Caution => new Color(1f, 0.85f, 0.30f, 1f);
}
