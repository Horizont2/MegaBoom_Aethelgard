using System.Reflection;
using UnityEngine;

// Takes FMOD's "FMOD Studio Debug" readout off the screen in Play Mode.
//
// ==== WHY THE SETTINGS ASSET ALONE WAS NOT ENOUGH ====
//
// The overlay is a GUI.Window that RuntimeManager draws at the top-left of the
// game view: CPU, memory, channel count, master volume. Useful while wiring
// audio, and it lands squarely in the frame of every trailer take.
//
// It is controlled by one field in FMODStudioSettings — the Play-In-Editor
// platform's Overlay property — and that field IS set to Disabled in the asset.
// But Settings is a ScriptableObject that FMOD loads once and caches, and the
// editor writes to it from several places, so "the value on disk" and "the
// value FMOD is running on" are not reliably the same thing until the editor is
// restarted. That is not a thing to ask someone to do in the middle of
// recording, so the value is also asserted at runtime, where it cannot be stale.
//
// Setting the PROPERTY rather than hiding the window matters: RuntimeManager
// re-reads it every Update in the editor and switches its own drawer back on
// whenever it reads true. Set here, before the first Update, the drawer is never
// created at all.
//
// Editor-only by construction: FMOD only draws the overlay under UNITY_EDITOR or
// in a development build, so in a shipped game this compiles to nothing.
public static class FmodOverlayOff
{
#if UNITY_EDITOR
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Apply()
    {
        var settings = FMODUnity.Settings.Instance;
        if (settings == null) return;

        FMODUnity.Platform platform = settings.PlayInEditorPlatform;
        if (platform == null) return;

        // Properties is protected, and the accessor that would set it is
        // internal to FMODUnity — reflection rather than editing the plugin,
        // which would be undone by the next FMOD update.
        FieldInfo propsField = typeof(FMODUnity.Platform)
            .GetField("Properties", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
        if (propsField == null) return;

        object props = propsField.GetValue(platform);
        if (props == null) return;

        FieldInfo overlayField = props.GetType().GetField("Overlay");
        if (overlayField == null) return;

        object overlay = overlayField.GetValue(props);
        if (overlay == null) return;

        // Property<TriStateBool> is a CLASS, so writing through the reference
        // reaches the object FMOD itself reads — no need to write it back.
        FieldInfo value = overlay.GetType().GetField("Value");
        FieldInfo hasValue = overlay.GetType().GetField("HasValue");
        if (value == null || hasValue == null) return;

        value.SetValue(overlay, FMODUnity.TriStateBool.Disabled);
        hasValue.SetValue(overlay, true);
    }
#endif
}
