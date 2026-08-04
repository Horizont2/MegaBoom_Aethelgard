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
        try
        {
            if (File.Exists(s_path))
            {
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
        SafeAppend(header.ToString());

        Application.logMessageReceivedThreaded += HandleLog;
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
