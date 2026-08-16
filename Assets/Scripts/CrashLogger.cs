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

        // On-screen overlay — DEVELOPMENT BUILDS ONLY. WebGL on a phone has
        // no browser dev-console, so a tester can't see what crashed. In a
        // Development Build we mirror Errors/Exceptions straight onto the
        // screen (via OnGUI) so the crash is readable — and screenshottable
        // — on any device. Debug.isDebugBuild is false in the shipping Steam
        // build, so this never appears for real players.
        if (Debug.isDebugBuild)
        {
            Application.logMessageReceived += HandleLogMainThread;
            var go = new GameObject("[CrashLogger.Overlay]");
            DontDestroyOnLoad(go);
            go.AddComponent<CrashLogger>();
        }
    }

    // ---- On-screen overlay (dev builds only) ----
    private static readonly System.Collections.Generic.List<string> s_onScreen =
        new System.Collections.Generic.List<string>();
    private const int MAX_ON_SCREEN = 16;
    private GUIStyle _overlayStyle;

    // Main-thread handler — OnGUI can only safely read data touched on the
    // main thread, so the overlay buffer is fed from the non-threaded event.
    private static void HandleLogMainThread(string message, string stackTrace, LogType type)
    {
        if (type != LogType.Error && type != LogType.Exception && type != LogType.Assert) return;
        // Keep the first stack frame too — enough to locate the throwing line.
        string firstFrame = "";
        if (!string.IsNullOrEmpty(stackTrace))
        {
            int nl = stackTrace.IndexOf('\n');
            firstFrame = "   " + (nl > 0 ? stackTrace.Substring(0, nl) : stackTrace);
        }
        s_onScreen.Add($"{type}: {message}{firstFrame}");
        while (s_onScreen.Count > MAX_ON_SCREEN) s_onScreen.RemoveAt(0);
    }

    private void OnGUI()
    {
        if (s_onScreen.Count == 0) return;

        if (_overlayStyle == null)
        {
            _overlayStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 13,
                wordWrap = true,
                richText = false,
                alignment = TextAnchor.UpperLeft
            };
            _overlayStyle.normal.textColor = new Color(1f, 0.55f, 0.5f);
        }

        float w = Mathf.Min(Screen.width - 20f, 960f);
        var box = new Rect(10f, 10f, w, Screen.height - 20f);

        GUI.color = new Color(0f, 0f, 0f, 0.8f);
        GUI.DrawTexture(box, Texture2D.whiteTexture);
        GUI.color = Color.white;

        var sb = new StringBuilder();
        sb.AppendLine("ON-SCREEN ERROR LOG  (development build)");
        sb.AppendLine("----------------------------------------");
        for (int i = 0; i < s_onScreen.Count; i++)
            sb.AppendLine(s_onScreen[i]);

        GUI.Label(new Rect(box.x + 8f, box.y + 8f, box.width - 16f, box.height - 16f),
                  sb.ToString(), _overlayStyle);
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
