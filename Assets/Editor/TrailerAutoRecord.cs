using System;
using System.Reflection;
using UnityEditor;
using UnityEngine;

// Starts and stops Unity Recorder around a shot launched from Tools > Lore Trailer.
//
// ==== WHY THE FIRST SECOND WAS ALWAYS RUINED ====
//
// The order by hand is: pick the shot, the editor enters Play Mode, then reach
// for the Recorder and press record. By the time that press lands the shot has
// been running for a second or more — and the first second is the one that
// matters, because it is where the fade from black, the establish and the whole
// reason for the framing live. What got recorded was the middle of a shot that
// had already started, on frames the engine was still warming up through.
//
// There is nothing to fix in the Recorder; it records exactly when it is told.
// The fix is to tell it at the right moment — and that moment is not the first
// frame of Play Mode either. Every one of these shots opens by calling
// OpenTrailer, which puts the frame to black and fades up; the castle shot gets
// there several seconds after Play, once region 24 has been generated. So the
// recording waits for THAT frame, which no hand could hit and no fixed delay
// could predict.
//
// ==== WHY IT DRIVES THE WINDOW AND NOT THE API ====
//
// RecorderController lets a script build its own recorder from nothing:
// resolution, codec, bitrate, audio, output path. It would also mean this file
// deciding all of those, and then quietly overriding whatever was set up in the
// Recorder window — so the settings on screen would stop being the settings
// being used, which is a worse trap than pressing record late.
//
// So it presses the same button. Whatever is configured in the Recorder window
// is what gets recorded; this only decides WHEN.
//
// Reached by reflection, like the FMOD and fog settings elsewhere in this
// project: if the Recorder package is ever removed, this reports it in a
// sentence instead of taking the editor's compilation down with it.
[InitializeOnLoad]
public static class TrailerAutoRecord
{
    private const string MenuPath = "Tools/Lore Trailer/Record the shot automatically";
    private const string EnabledKey = "Trailer.AutoRecord";          // a preference, so it persists
    private const string PendingKey = "Trailer.AutoRecord.Shot";     // one launch, so it does not

    // After the shot says it is done. The last beat of a shot is a held black or
    // a closing shutter, and cutting the file at the exact frame IsFinished
    // flips would lose it.
    private const double TailSeconds = 0.6;

    // Nothing here runs for three minutes. If a shot never reports finished —
    // it threw, it is waiting on generation that failed, it was left paused —
    // the recording stops itself rather than filling a disk overnight.
    private const double HardStop = 180.0;

    private static string watching;
    private static bool rolling;
    private static double armedAt;
    private static double finishedAt = -1.0;

    static TrailerAutoRecord()
    {
        EditorApplication.playModeStateChanged += OnPlayModeChanged;
    }

    public static bool Enabled
    {
        get { return EditorPrefs.GetBool(EnabledKey, true); }
        set { EditorPrefs.SetBool(EnabledKey, value); }
    }

    [MenuItem(MenuPath, priority = 20)]
    private static void Toggle()
    {
        Enabled = !Enabled;
        Menu.SetChecked(MenuPath, Enabled);

        if (Enabled)
        {
            // Opened now rather than at the moment of recording: the window has
            // to exist and be configured before there is anything to press, and
            // finding that out while a shot is already playing is too late.
            Window(true);
            Debug.Log("[Lore Trailer] Recording will start on the shot's first frame and stop when it ends. " +
                      "Set the format, resolution and output path in the Recorder window as usual — " +
                      "this only decides when the button is pressed.");
        }
        else Debug.Log("[Lore Trailer] Automatic recording off. Press record in the Recorder window yourself.");
    }

    [MenuItem(MenuPath, true)]
    private static bool ToggleValidate()
    {
        Menu.SetChecked(MenuPath, Enabled);
        return true;
    }

    /// <summary>Called by the launcher, just before it enters Play Mode.</summary>
    public static void Arm(string shot)
    {
        if (!Enabled) return;

        if (Window(true) == null)
        {
            Debug.LogWarning("[Lore Trailer] Unity Recorder is not installed, so the shot will play without " +
                             "being recorded. Switch this off in Tools > Lore Trailer if you do not want it.");
            return;
        }

        SessionState.SetString(PendingKey, shot);
    }

    private static void OnPlayModeChanged(PlayModeStateChange change)
    {
        if (change == PlayModeStateChange.EnteredPlayMode)
        {
            string shot = SessionState.GetString(PendingKey, string.Empty);
            SessionState.EraseString(PendingKey);      // one launch, one recording
            if (!string.IsNullOrEmpty(shot)) Begin(shot);
        }
        else if (change == PlayModeStateChange.ExitingPlayMode)
        {
            // Stopping Play by hand is a perfectly good way to end a take, and
            // the file still has to be closed.
            End("Play Mode ended");
        }
    }

    private static void Begin(string shot)
    {
        if (Window(true) == null) return;

        watching = shot;
        rolling = false;
        armedAt = EditorApplication.timeSinceStartup;
        finishedAt = -1.0;
        EditorApplication.update += Watch;

        Debug.Log("[Lore Trailer] Waiting for the '" + shot + "' shot to open, then recording it. " +
                  "It stops by itself when the shot ends.");
    }

    private static void Watch()
    {
        if (!EditorApplication.isPlaying) { End("Play Mode ended"); return; }

        double now = EditorApplication.timeSinceStartup;
        if (now - armedAt > HardStop) { End("nothing happened for three minutes"); return; }

        // ---- 1. wait for the picture to start ----
        //
        // NOT the first frame of Play Mode. The castle shot spends several
        // seconds generating region 24 before there is anything to look at, and
        // every shot spends a moment loading; recorded from Play, the take opens
        // on a progress log. TrailerCinematicPolish.Opened is the frame the fade
        // from black begins, which is the first frame of every one of these
        // shots by construction.
        if (!rolling)
        {
            if (!TrailerCinematicPolish.Opened) return;

            if (!Invoke(Window(true), "StartRecording"))
            {
                Debug.LogWarning("[Lore Trailer] The Recorder window has no StartRecording() any more — the " +
                                 "package's API has changed. Press record by hand, or switch this off in " +
                                 "Tools > Lore Trailer.");
                End("the Recorder could not be started");
                return;
            }

            rolling = true;
            Debug.Log("[Lore Trailer] Recording from the shot's first frame.");
            return;
        }

        // ---- 2. wait for it to end ----
        if (finishedAt < 0.0)
        {
            if (!ShotFinished(watching)) return;
            finishedAt = now;                       // let the last beat land
            return;
        }

        if (now - finishedAt < TailSeconds) return;

        End("the shot finished");
        // The shot is over and so is the reason to be in Play Mode. Left running,
        // the next thing in the file would be whatever the scene does afterwards.
        EditorApplication.isPlaying = false;
    }

    private static void End(string why)
    {
        if (watching == null) return;

        EditorApplication.update -= Watch;
        watching = null;
        rolling = false;
        finishedAt = -1.0;

        object w = Window(false);
        if (w != null && IsRecording(w))
        {
            Invoke(w, "StopRecording");
            Debug.Log("[Lore Trailer] Recording stopped — " + why + ". The file is where the Recorder window says.");
        }
    }

    // Each shot already publishes whether it is done; this only has to ask the
    // right one. Kept as a switch rather than an interface because the five
    // directors were written months apart and share nothing else.
    private static bool ShotFinished(string shot)
    {
        if (shot == TrailerShotSolo.StatueShot)
        {
            var d = UnityEngine.Object.FindFirstObjectByType<TrailerStatueShot>();
            return d != null && d.IsFinished;
        }
        if (shot == TrailerShotSolo.RideShot)
        {
            var d = UnityEngine.Object.FindFirstObjectByType<TrailerSequenceDirector>();
            return d != null && d.IsFinished;
        }
        if (shot == TrailerShotSolo.MarchShot)
        {
            var d = UnityEngine.Object.FindFirstObjectByType<TrailerLegionDirector>();
            return d != null && d.IsFinished;
        }
        if (shot == TrailerShotSolo.ClashShot)
        {
            var d = UnityEngine.Object.FindFirstObjectByType<TrailerClashDirector>();
            return d != null && d.IsFinished;
        }
        if (shot == TrailerShotSolo.CastleShot)
        {
            var d = UnityEngine.Object.FindFirstObjectByType<TrailerCastleShot>();
            return d != null && d.IsFinished;
        }
        return false;
    }

    // ===================== the Recorder, at arm's length =====================

    private static object Window(bool openIfClosed)
    {
        Type t = FindType("UnityEditor.Recorder.RecorderWindow");
        if (t == null) return null;

        if (!openIfClosed)
        {
            UnityEngine.Object[] open = Resources.FindObjectsOfTypeAll(t);
            return open != null && open.Length > 0 ? open[0] : null;
        }

        // focus: false — the Game view is what should be in front while a shot
        // plays, and a window stealing focus at the moment Play begins is one
        // more thing between the launcher and a clean take.
        return EditorWindow.GetWindow(t, false, "Recorder", false);
    }

    private static bool Invoke(object window, string method)
    {
        if (window == null) return false;
        MethodInfo m = window.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.Public);
        if (m == null) return false;
        m.Invoke(window, null);
        return true;
    }

    private static bool IsRecording(object window)
    {
        if (window == null) return false;
        MethodInfo m = window.GetType().GetMethod("IsRecording", BindingFlags.Instance | BindingFlags.Public);
        if (m == null) return false;
        object r = m.Invoke(window, null);
        return r is bool && (bool)r;
    }

    private static Type FindType(string fullName)
    {
        foreach (Assembly a in AppDomain.CurrentDomain.GetAssemblies())
        {
            Type t = a.GetType(fullName);
            if (t != null) return t;
        }
        return null;
    }
}
