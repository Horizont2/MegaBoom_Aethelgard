using System;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;

// The two ways to get FMOD's sound out of this project, under one menu.
//
// ==== WHY THE RECORDER'S AUDIO CHECKBOX DOES NOTHING ====
//
// Unity Recorder's Audio input records the output of UNITY's audio engine — the
// AudioListener bus. FMOD does not go through Unity's audio engine at all:
// RuntimeManager opens its own output device and mixes straight to it. So the
// Recorder faithfully captures the Unity bus, which in this project is silent,
// and writes a movie with no sound. Nothing is misconfigured; the two engines
// simply never meet.
//
// INTO UNITY is the one to use. It hangs a tap on FMOD's master bus, pushes
// every block through Unity's own output, and the Recorder picks it up with the
// picture, in one file, with nothing to line up afterwards. It is the newer of
// the two and the reason this file was rewritten.
//
// TO A WAV FILE is the old way and stays because it cannot fail: FMOD's own
// WAVWRITER output writes the whole master mix to disk and nothing has to
// bridge anywhere. It costs you a second file to lay against the video by hand.
//
// They are mutually exclusive, and they say so. Two audio paths fighting over
// one mix is the kind of thing that eats an afternoon.
public static class FmodRecordMode
{
    private const string SettingsPath = "Assets/Plugins/FMOD/Resources/FMODStudioSettings.asset";
    private const string EditorPlatformType = "PlatformPlayInEditor";
    private const string OutputField = "OutputTypeName";

    // FMOD's realtime WAV writer. Chosen over WAVWRITER_NRT deliberately: the
    // non-realtime writer produces one mixer block per update() call rather than
    // per unit of time, so its length tracks FRAME COUNT, not seconds, and it
    // only stays in step with the video if the DSP buffer happens to match the
    // frame duration exactly. The realtime writer runs on the wall clock, which
    // is the same clock Unity Recorder uses when it is left to record in real
    // time — so the two match without anyone having to compute anything.
    private const string RecordOutput = "WAVWRITER";

    private const string BridgeMenu = "Tools/Trailer/Audio for Recorder/Into Unity (recorded with the video)";
    private const string WavMenu = "Tools/Trailer/Audio for Recorder/To a WAV file (line up by hand)";

    // ===================== into Unity =====================

    [MenuItem(BridgeMenu, priority = 0)]
    private static void ToggleBridge()
    {
        bool turningOn = !SessionState.GetBool(FmodRecorderBridge.SessionKey, false);

        if (turningOn && WavIsOn())
        {
            // WAVWRITER takes FMOD off the device entirely. The tap would still
            // see the mix, so both would "work", and the result is a movie with
            // sound AND a wav nobody needed — plus an explanation to find later.
            SetWav(false);
            Debug.Log("[FMOD capture] The WAV file mode was on; switched off, because only one of the two is wanted.");
        }

        SessionState.SetBool(FmodRecorderBridge.SessionKey, turningOn);
        Menu.SetChecked(BridgeMenu, turningOn);

        if (turningOn)
        {
            EditorUtility.DisplayDialog("FMOD goes through Unity",
                "FMOD's master mix is now routed into Unity's audio output, so Unity Recorder records it " +
                "together with the picture — one file, nothing to line up.\n\n" +
                "  • Press Play. It takes effect on entering Play Mode, not now.\n" +
                "  • You still hear everything, but through Unity rather than straight from FMOD.\n" +
                "  • The sound lands about 40 ms behind the picture. That is under two frames at 24fps; " +
                "the exact figure is printed to the console when it starts.\n" +
                "  • If you hear crackle, raise CushionFrames in FmodRecorderBridge.\n\n" +
                "This lasts for this editor session only.", "Got it");
        }
        else
        {
            EditorUtility.DisplayDialog("FMOD goes straight to the speakers again",
                "The tap is off from the next Play. Unity Recorder will record silence again.", "OK");
        }
    }

    [MenuItem(BridgeMenu, true)]
    private static bool ValidateBridge()
    {
        Menu.SetChecked(BridgeMenu, SessionState.GetBool(FmodRecorderBridge.SessionKey, false));
        return true;
    }

    // ===================== to a WAV file =====================

    [MenuItem(WavMenu, priority = 1)]
    private static void ToggleWav()
    {
        bool turningOn = !WavIsOn();

        if (turningOn && SessionState.GetBool(FmodRecorderBridge.SessionKey, false))
        {
            SessionState.SetBool(FmodRecorderBridge.SessionKey, false);
            Menu.SetChecked(BridgeMenu, false);
            Debug.Log("[FMOD capture] The Unity route was on; switched off, because only one of the two is wanted.");
        }

        if (!SetWav(turningOn))
        {
            EditorUtility.DisplayDialog("FMOD capture",
                "Could not find the Play-In-Editor platform inside " + SettingsPath +
                ", or its output field has been renamed by an FMOD update. Set Output Type to WAVWRITER by hand in " +
                "FMOD > Edit Settings > Play In Editor.", "OK");
            return;
        }

        Menu.SetChecked(WavMenu, turningOn);
        string wav = Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "fmodoutput.wav"));

        if (turningOn)
        {
            EditorUtility.DisplayDialog("FMOD writes a WAV",
                "FMOD will write its whole mix to a file instead of the speakers.\n\n" +
                "  • You will hear NOTHING in the editor while this is on.\n" +
                "  • Entering Play Mode writes:\n      " + wav + "\n" +
                "  • The file is overwritten on every play, so copy the take out before the next one.\n\n" +
                "Record the picture with Unity Recorder set to record in REAL TIME (not a constant frame rate) " +
                "so the video runs on the same clock the WAV does, then line them up in your editor.\n\n" +
                "Turn this off from the same menu when you are done.", "Got it");
        }
        else
        {
            EditorUtility.DisplayDialog("FMOD capture is OFF",
                "Output is back to the default device. Sound in the editor works again.", "OK");
        }
    }

    [MenuItem(WavMenu, true)]
    private static bool ValidateWav()
    {
        Menu.SetChecked(WavMenu, WavIsOn());
        return true;
    }

    private static bool WavIsOn()
    {
        FieldInfo field;
        object platform = FindEditorPlatform(out field);
        if (platform == null || field == null) return false;
        return string.Equals(field.GetValue(platform) as string, RecordOutput, StringComparison.OrdinalIgnoreCase);
    }

    private static bool SetWav(bool on)
    {
        FieldInfo field;
        object platform = FindEditorPlatform(out field);
        if (platform == null || field == null) return false;

        field.SetValue(platform, on ? RecordOutput : string.Empty);
        EditorUtility.SetDirty((UnityEngine.Object)platform);
        AssetDatabase.SaveAssets();
        return true;
    }

    // Reached by reflection on purpose. OutputTypeName is `internal` to the
    // FMODUnity assembly, so a normal reference would not compile — and going
    // through reflection also means this tool cannot break the build if a future
    // FMOD version moves or renames it. It reports the problem instead.
    private static object FindEditorPlatform(out FieldInfo field)
    {
        field = null;

        UnityEngine.Object[] all = AssetDatabase.LoadAllAssetsAtPath(SettingsPath);
        if (all == null) return null;

        foreach (var o in all)
        {
            if (o == null) continue;
            Type t = o.GetType();
            if (t.Name != EditorPlatformType) continue;

            field = FindField(t, OutputField);
            return field != null ? o : null;
        }
        return null;
    }

    private static FieldInfo FindField(Type t, string name)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        for (Type cur = t; cur != null; cur = cur.BaseType)
        {
            FieldInfo f = cur.GetField(name, flags);
            if (f != null && f.FieldType == typeof(string)) return f;
        }
        return null;
    }
}
