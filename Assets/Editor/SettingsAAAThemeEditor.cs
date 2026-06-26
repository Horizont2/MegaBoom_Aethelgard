#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(SettingsAAATheme))]
public class SettingsAAAThemeEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();
        GUILayout.Space(8);
        EditorGUILayout.HelpBox(
            "Drag exported Figma sprites into the slots above, then press Apply Theme. " +
            "Empty slots are skipped — replace pieces one at a time without breaking the panel.",
            MessageType.Info);
        if (GUILayout.Button("Apply Theme", GUILayout.Height(36)))
        {
            var t = (SettingsAAATheme)target;
            Undo.SetCurrentGroupName("Apply Settings Theme");
            t.ApplyAll();
            EditorUtility.SetDirty(t);
        }
    }
}
#endif
