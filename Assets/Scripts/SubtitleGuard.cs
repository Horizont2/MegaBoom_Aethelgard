// Shared ownership token for the on-screen subtitle label.
//
// Several systems can target the SAME TMP subtitle object (the camp intro
// CampDirector and the NPC CampNPC_Elias literally share one label, and the
// menu intro drives its own). Without a guard, two typewriters can run at once
// and the text flickers between them, leaving a frozen leftover at the end.
//
// Each typewriter calls Claim() when it starts a line and then bails out the
// instant another writer Claims a newer token — so only the most-recently-
// started writer ever drives the label. Cheap, allocation-free, no per-object
// wiring needed.
public static class SubtitleGuard
{
    private static int s_token;

    // Take ownership; returns the caller's token.
    public static int Claim()
    {
        return ++s_token;
    }

    // True while this token is still the active owner.
    public static bool Owns(int token)
    {
        return s_token == token;
    }
}
