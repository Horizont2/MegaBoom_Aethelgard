using System;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;

// Makes FMOD's audio recordable, because Unity Recorder cannot hear it.
//
// ==== WHY THE CHECKBOX DOES NOTHING ====
//
// Unity Recorder's Audio input records the output of Unity's own audio engine -
// the AudioListener bus. FMOD does not go through Unity's audio engine at all:
// RuntimeManager opens its own output device and mixes straight to it. So the
// Recorder faithfully captures the Unity bus, which in this project is silent,
// and writes a movie with no sound. Nothing is misconfigured; the two systems
// simply never meet.
//
// FMOD has its own answer, and it is one setting: the WAVWRITER output. In that
// mode FMOD stops driving the speakers and writes its entire master mix to a
// WAV file instead. Record the picture with Unity Recorder as usual, take the
// sound from that file, and lay them together in an editor - which is what a
// trailer is cut in anyway.
//
// Two things make that awkward enough to be worth a menu item rather than a
// note in a document: while it is on you hear NOTHING in the editor, and
// forgetting to switch it back means a week of wondering why the game is mute.
// So it is a toggle with a tick next to it, and it says what it did.
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
    // time - so the two match without anyone having to compute anything.
    private const string RecordOutput = "WAVWRITER";

    private const string MenuPath = "Tools/Trailer/FMOD capture for Recorder";

    [MenuItem(MenuPath)]
    private static void Toggle()
    {
        object platform = FindEditorPlatform(out FieldInfo field);
        if (platform == null || field == null)
        {
            EditorUtility.DisplayDialog("FMOD capture",
                "Could not find the Play-In-Editor platform inside " + SettingsPath +
                ", or its output field has been renamed by an FMOD update. Set Output Type to WAVWRITER by hand in " +
                "FMOD > Edit Settings > Play In Editor.", "OK");
            return;
        }

        bool turningOn = !IsOn(field, platform);
        field.SetValue(platform, turningOn ? RecordOutput : string.Empty);

        EditorUtility.SetDirty((UnityEngine.Object)platform);
        AssetDatabase.SaveAssets();
        Menu.SetChecked(MenuPath, turningOn);

        string wav = Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "fmodoutput.wav"));

        if (turningOn)
        {
            EditorUtility.DisplayDialog("FMOD capture is ON",
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

    [MenuItem(MenuPath, true)]
    private static bool ToggleValidate()
    {
        object platform = FindEditorPlatform(out FieldInfo field);
        Menu.SetChecked(MenuPath, platform != null && field != null && IsOn(field, platform));
        return true;
    }

    private static bool IsOn(FieldInfo field, object platform)
    {
        return string.Equals(field.GetValue(platform) as string, RecordOutput, StringComparison.OrdinalIgnoreCase);
    }

    // Reached by reflection on purpose. OutputTypeName is `internal` to the
    // FMODUnity assembly, so a normal reference would not compile - and going
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
