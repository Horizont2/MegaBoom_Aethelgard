using System;
using System.IO;
using System.Text;
using UnityEngine;

// Writes Unity exception / error logs to a rotating file next to the
// save so a player who reports a bug can attach it.
//
// Two files are kept:
//   * <persistentDataPath>/crash_log.txt      — current session
//   * <persistentDataPath>/crash_log.prev.txt — previous session,
//     preserved even if the current one grows past the size cap.
//
// A single hard cap (256 KB) protects users who leave the game running
// for days from ballooning their disk. When the current file exceeds
// the cap we rotate it to .prev and start fresh.
//
// Only Error + Exception + Assert levels go through — Log/Warning would
// drown the useful stack traces.
[DefaultExecutionOrder(-2000)]
public class CrashLogger : MonoBehaviour
{
    private const string FILE_NAME = "crash_log.txt";
    private const string PREV_NAME = "crash_log.prev.txt";
    private const long   MAX_BYTES = 256 * 1024;

    private static bool s_bootstrapped;
    private static string s_path;
    private static string s_prev;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Bootstrap()
    {
        if (s_bootstrapped) return;
        s_bootstrapped = true;

        s_path = Path.Combine(Application.persistentDataPath, FILE_NAME);
        s_prev = Path.Combine(Application.persistentDataPath, PREV_NAME);

        // Rotate on launch — previous session becomes .prev.
        bool previousDiedSilently = false;
        try
        {
            if (File.Exists(s_path))
            {
                previousDiedSilently = !EndedCleanly(s_path);
                if (File.Exists(s_prev)) File.Delete(s_prev);
                File.Move(s_path, s_prev);
            }
        }
        catch { /* nothing useful we can do — logger itself must not crash. */ }

        // Header identifies the run so support can grep by build+session.
        var header = new StringBuilder();
        header.AppendLine("======================================================");
        header.AppendLine($"session_start   utc={DateTime.UtcNow:o}");
        header.AppendLine($"version         {Application.version}");
        header.AppendLine($"unity           {Application.unityVersion}");
        header.AppendLine($"platform        {Application.platform}");
        header.AppendLine($"device          {SystemInfo.deviceName} ({SystemInfo.deviceModel})");
        header.AppendLine($"os              {SystemInfo.operatingSystem}");
        header.AppendLine($"cpu             {SystemInfo.processorType} x{SystemInfo.processorCount}");
        header.AppendLine($"ram_mb          {SystemInfo.systemMemorySize}");
        header.AppendLine($"gpu             {SystemInfo.graphicsDeviceName} ({SystemInfo.graphicsDeviceType})");
        header.AppendLine("======================================================");

        // ==== THE CRASH THAT LEAVES NOTHING BEHIND ====
        //
        // This file only ever recorded MANAGED errors. A native crash — a lost
        // D3D device on alt-tab, a plugin falling over, running out of memory —
        // produces no Unity log line at all, so the file simply stopped
        // mid-sentence and the next session overwrote the evidence with a fresh
        // header. Every one of those crashes looked, from here, like a session
        // that ended normally.
        //
        // A marker on the way out fixes that. No marker on the previous file
        // means the process died without being asked to, and that fact is now
        // the first line of the next session rather than something nobody can
        // tell afterwards.
        if (previousDiedSilently)
        {
            header.AppendLine("!! PREVIOUS SESSION DID NOT EXIT CLEANLY — it was killed or it crashed natively.");
            header.AppendLine("!! Nothing below in crash_log.prev.txt will say why: a native crash writes no managed log.");
            header.AppendLine("!! The evidence is Unity's own Player.log and the crash dump, both next to this file's");
            header.AppendLine("!! folder. Read the last breadcrumbs in the .prev file to see what it was doing.");
            header.AppendLine("======================================================");
        }
        SafeAppend(header.ToString());

        Application.logMessageReceivedThreaded += HandleLog;

        // ==== BREADCRUMBS, BECAUSE THE STACK TRACE WILL NOT BE THERE ====
        //
        // When the log ends without a reason, the only useful thing it can
        // carry is what the game was doing at the time. These four are the ones
        // that matter for the crashes actually being reported: alt-tab (focus),
        // a display mode change (which is what alt-tab does to an exclusive
        // fullscreen window and the likeliest native fault of the lot), a scene
        // load, and memory pressure.
        Application.quitting += MarkCleanExit;
        Application.focusChanged += OnFocusChanged;
        Application.lowMemory += OnLowMemory;
        UnityEngine.SceneManagement.SceneManager.sceneLoaded += OnSceneLoaded;
    }

    private const string CleanMarker = "session_end     clean";

    private static bool EndedCleanly(string path)
    {
        try
        {
            // The marker is the last thing written, so only the tail matters —
            // and the tail is all that can be read cheaply from a file that may
            // be a quarter of a megabyte.
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            int take = (int)Math.Min(fs.Length, 2048);
            fs.Seek(-take, SeekOrigin.End);
            var buf = new byte[take];
            int read = fs.Read(buf, 0, take);
            return Encoding.UTF8.GetString(buf, 0, read).Contains(CleanMarker);
        }
        catch { return true; }   // unreadable is not evidence of a crash
    }

    private static void MarkCleanExit()
    {
        SafeAppend($"[{DateTime.UtcNow:HH:mm:ss.fff}] {CleanMarker} utc={DateTime.UtcNow:o}\n");
    }

    private static void OnFocusChanged(bool focused)
    {
        // The display mode goes in the same line on purpose. Exclusive
        // fullscreen is the one that forces a real mode change when focus
        // moves, and if a crash always follows this line with that mode in it,
        // the answer is in the line.
        Breadcrumb($"focus {(focused ? "gained" : "LOST")} — mode {Screen.fullScreenMode}, " +
                   $"{Screen.width}x{Screen.height}");
    }

    private static void OnLowMemory()
    {
        Breadcrumb("LOW MEMORY warning from the OS — a crash shortly after this one is an out-of-memory.");
    }

    private static void OnSceneLoaded(UnityEngine.SceneManagement.Scene scene,
                                      UnityEngine.SceneManagement.LoadSceneMode mode)
    {
        Breadcrumb($"scene loaded '{scene.name}' ({mode})");
    }

    /// <summary>A line of context, not an error. Call it from anywhere worth marking.</summary>
    public static void Breadcrumb(string what)
    {
        SafeAppend($"[{DateTime.UtcNow:HH:mm:ss.fff}] .  {what}\n");
    }

    private static void HandleLog(string message, string stackTrace, LogType type)
    {
        if (type != LogType.Error && type != LogType.Exception && type != LogType.Assert) return;

        try
        {
            // Rotate if the current file is getting huge.
            var info = new FileInfo(s_path);
            if (info.Exists && info.Length > MAX_BYTES)
            {
                if (File.Exists(s_prev)) File.Delete(s_prev);
                File.Move(s_path, s_prev);
            }
        }
        catch { }

        var sb = new StringBuilder(message.Length + (stackTrace?.Length ?? 0) + 64);
        sb.Append('[').Append(DateTime.UtcNow.ToString("HH:mm:ss.fff")).Append("] ")
          .Append(type).Append(": ").AppendLine(message);
        if (!string.IsNullOrEmpty(stackTrace)) sb.AppendLine(stackTrace);
        SafeAppend(sb.ToString());
    }

    // File.AppendAllText can throw on disk-full / permissions — swallow so
    // the logger never turns into the crashing thing.
    private static void SafeAppend(string text)
    {
        try { File.AppendAllText(s_path, text); }
        catch { }
    }

    // Handy for a "Send bug report" button.
    public static string CurrentLogPath => s_path ?? Path.Combine(Application.persistentDataPath, FILE_NAME);
    public static string PreviousLogPath => s_prev ?? Path.Combine(Application.persistentDataPath, PREV_NAME);
}
