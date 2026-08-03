using System.Collections.Generic;
using UnityEngine;

// Thin analytics layer — call Analytics.Event("boss_killed",
// "region", "R4") from anywhere. Ships as a NO-OP by default; hook
// Unity Analytics / Steamworks stats / your own backend once by
// implementing s_sink.
//
// Local buffering (last 100 events) is kept so a dev build can dump
// the log via Analytics.DumpToConsole() during playtests without any
// backend at all.
public static class Analytics
{
    public interface ISink
    {
        void Emit(string eventName, IReadOnlyDictionary<string, object> parameters);
    }

    private static ISink s_sink;
    private static readonly Queue<string> s_recent = new Queue<string>(100);
    private static readonly Dictionary<string, object> s_scratch = new Dictionary<string, object>(8);

    // Set once from your bootstrap code. Passing null reverts to
    // buffer-only mode.
    public static void SetSink(ISink sink) => s_sink = sink;

    // ---- Emit helpers ----
    public static void Event(string name)
    {
        LogOnly(name, null);
        s_sink?.Emit(name, EmptyReadOnly);
    }

    public static void Event(string name, string key, object value)
    {
        s_scratch.Clear();
        s_scratch[key] = value;
        LogOnly(name, s_scratch);
        s_sink?.Emit(name, s_scratch);
    }

    public static void Event(string name, string k1, object v1, string k2, object v2)
    {
        s_scratch.Clear();
        s_scratch[k1] = v1; s_scratch[k2] = v2;
        LogOnly(name, s_scratch);
        s_sink?.Emit(name, s_scratch);
    }

    public static void Event(string name, Dictionary<string, object> parameters)
    {
        LogOnly(name, parameters);
        s_sink?.Emit(name, parameters);
    }

    // ---- Debug ----
    [UnityEngine.Scripting.Preserve]
    public static void DumpToConsole()
    {
        Debug.Log($"[Analytics] Buffer ({s_recent.Count} events):\n" + string.Join("\n", s_recent));
    }

    // ---- Internals ----
    private static readonly Dictionary<string, object> EmptyReadOnly = new Dictionary<string, object>(0);

    private static void LogOnly(string name, IReadOnlyDictionary<string, object> parameters)
    {
        string line;
        if (parameters == null || parameters.Count == 0) line = name;
        else
        {
            var sb = new System.Text.StringBuilder(name.Length + 32);
            sb.Append(name).Append(" [");
            bool first = true;
            foreach (var kv in parameters)
            {
                if (!first) sb.Append(", ");
                first = false;
                sb.Append(kv.Key).Append('=').Append(kv.Value);
            }
            sb.Append(']');
            line = sb.ToString();
        }
        if (s_recent.Count >= 100) s_recent.Dequeue();
        s_recent.Enqueue(line);
    }
}
