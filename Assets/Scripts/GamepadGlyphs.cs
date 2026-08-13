using System.Text.RegularExpressions;

// Rewrites keyboard key hints in interaction prompts to gamepad button
// glyphs when a controller is connected — required for Steam Deck
// verification (a Deck player must never see "[E]" / "Press F").
//
// Mapping follows InputCompat (Xbox layout; Steam Input remaps to any
// pad):
//   E / Submit / Space / interact-talk-open  -> A   (South)
//   F / inspect / execute                     -> X   (West)
//   Escape / cancel                           -> B   (East)
//   R / secondary                             -> Y   (North)
//   G / grenade                               -> RB  (Right shoulder)
//   Shift / dash                              -> LT  (Left trigger)
//
// Glyphs are drawn as bracketed letters ([A], [X], …) tinted a soft
// blue — asset-free, always renders in any TMP font, and reads clearly
// as a button. Bold-wrapped so it pops from the surrounding text.
public static class GamepadGlyphs
{
    private const string COL = "#8fd3ff"; // soft controller-blue

    private static string G(string label) => $"<b><color={COL}>[{label}]</color></b>";

    // Precompiled patterns — key tokens are stable regardless of the
    // localised verb around them, so we match the token, not the phrase.
    private static readonly (Regex rx, string glyph)[] s_rules =
    {
        // Bracketed forms first (most prompts use these).
        (new Regex(@"\[E\]", RegexOptions.Compiled), "A"),
        (new Regex(@"\[F\]", RegexOptions.Compiled), "X"),
        (new Regex(@"\[G\]", RegexOptions.Compiled), "RB"),
        (new Regex(@"\[R\]", RegexOptions.Compiled), "Y"),
        // Bold key names used in cinematic prompts.
        (new Regex(@"<b>\s*SPACE\s*</b>", RegexOptions.Compiled | RegexOptions.IgnoreCase), "A"),
        (new Regex(@"<b>\s*SHIFT\s*</b>", RegexOptions.Compiled | RegexOptions.IgnoreCase), "LT"),
        (new Regex(@"<b>\s*E\s*</b>", RegexOptions.Compiled), "A"),
        (new Regex(@"<b>\s*F\s*</b>", RegexOptions.Compiled), "X"),
        // English "Press E/F/Space" forms (bare, no brackets). The
        // localised verbs ("Натисни", "Pulsa"…) keep their own key
        // letter which the token rules above already caught when
        // bracketed; these cover the English bare forms.
        (new Regex(@"\bPress\s+E\b", RegexOptions.Compiled), "Press {G}"),
        (new Regex(@"\bPress\s+F\b", RegexOptions.Compiled), "Press {G}"),
    };

    public static string Apply(string s)
    {
        if (string.IsNullOrEmpty(s)) return s;
        if (!InputCompat.AnyControllerConnected) return s;

        foreach (var (rx, glyph) in s_rules)
        {
            if (glyph == "Press {G}")
            {
                // The "Press E/F" rules: figure out which glyph from the
                // matched letter.
                s = rx.Replace(s, m => "Press " + G(m.Value.EndsWith("F") ? "X" : "A"));
            }
            else
            {
                s = rx.Replace(s, G(glyph));
            }
        }
        return s;
    }
}
