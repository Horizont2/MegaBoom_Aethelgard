using UnityEngine;

// The switch that decides whether developer shortcuts exist.
//
// ==== WHY THIS HAD TO BECOME A THING ====
//
// Two debug keys were live in every build: F10 toggled noclip, so a player
// could walk through the world, and F8 skipped the tutorial straight to its
// ending. Neither was behind any condition at all. A player hits one by
// accident and their run is broken with no way to tell what happened; a
// streamer finds both on day one and they are the video.
//
// Deleting them would be the wrong fix — they are genuinely useful, and the
// next bug is always easier to reproduce with a camera that can fly. So they
// live, and they stop existing in a release build.
//
// OFF in a shipped build, ON in the editor and in a development build. A
// shipped build can still be asked for them with -devcheats on the command
// line, which is how QA gets them without a second build being made.
public static class DevCheats
{
    private const string Argument = "-devcheats";

    private static bool s_resolved;
    private static bool s_enabled;

    public static bool Enabled
    {
        get
        {
            if (s_resolved) return s_enabled;
            s_resolved = true;

#if UNITY_EDITOR || DEVELOPMENT_BUILD
            s_enabled = true;
#else
            s_enabled = HasArgument();
            if (s_enabled)
                Debug.LogWarning("[DevCheats] Developer shortcuts are ON because this build was started with " +
                                 Argument + ". Noclip is F10 and the tutorial skip is F8.");
#endif
            return s_enabled;
        }
    }

    private static bool HasArgument()
    {
        string[] args = System.Environment.GetCommandLineArgs();
        if (args == null) return false;
        for (int i = 0; i < args.Length; i++)
            if (string.Equals(args[i], Argument, System.StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }
}
