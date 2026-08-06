using System.Diagnostics;

// Central verbose-logging gate.
//
// Info() is compiled IN only when UNITY_EDITOR or DEVELOPMENT_BUILD is
// defined. In a release Steam build BOTH symbols are absent, so every
// GameLog.Info(...) call — INCLUDING the evaluation of its arguments —
// is stripped by the compiler via [Conditional]. That means no string
// interpolation runs, no GC alloc, and nothing spams the player's
// Player.log. Warnings and errors are deliberately NOT gated: they
// stay in release, reach the CrashLogger, and matter for bug reports.
//
// Migration: replace informational `Debug.Log(...)` with
// `GameLog.Info(...)`. Leave `Debug.LogWarning` / `Debug.LogError`
// alone.
public static class GameLog
{
    [Conditional("UNITY_EDITOR")]
    [Conditional("DEVELOPMENT_BUILD")]
    public static void Info(object message)
    {
        UnityEngine.Debug.Log(message);
    }

    [Conditional("UNITY_EDITOR")]
    [Conditional("DEVELOPMENT_BUILD")]
    public static void Info(object message, UnityEngine.Object context)
    {
        UnityEngine.Debug.Log(message, context);
    }
}
