#if UNITY_EDITOR
using UnityEngine;
using UnityEngine.UI;
using UnityEditor;
using TMPro;
using System.Collections.Generic;
using System.Linq;

// Editor-only utility that extends an existing SettingsPanel with the
// 14 controls SettingsUI now declares (sensitivity, invert-Y, subtitles,
// subtitle size, colorblind, low-hp vignette, hit-stop, quality preset,
// render scale, post-FX, dynamic shadows, language). It works by
// cloning the user's existing slider / toggle / dropdown templates so
// every new control inherits the project's colors, fonts, and sprites.
//
// The user opens the HUD_Canvas prefab, selects the SettingsUI
// GameObject, and clicks Tools > MegaBoom > Build Extended Settings.
public class SettingsPanelExtender : EditorWindow
{
    [MenuItem("Tools/MegaBoom/Build Extended Settings")]
    public static void ShowWindow()
    {
        GetWindow<SettingsPanelExtender>("Settings Extender");
    }

    private SettingsUI settingsUI;
    private Slider templateSlider;
    private Toggle templateToggle;
    private TMP_Dropdown templateDropdown;
    private TMP_InputField templateInput;
    private RectTransform gameplayTabPanel;
    private RectTransform graphicsTabPanel;
    private RectTransform accessibilityTabPanel;

    private Vector2 scroll;

    private void OnGUI()
    {
        scroll = EditorGUILayout.BeginScrollView(scroll);

        GUILayout.Label("Settings Panel Extender", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "1) Open HUD_Canvas.prefab in prefab mode.\n" +
            "2) Drag the SettingsUI GameObject (the one with the SettingsUI script) into the slot below.\n" +
            "3) Drag templates: an existing Slider, Toggle, Dropdown, and InputField from inside the SettingsPanel.\n" +
            "4) Optionally drag the panel objects that hold each tab's content (Gameplay / Graphics / Accessibility).\n" +
            "5) Click Build — clones inherit colors / fonts / sprites from the templates.",
            MessageType.Info);

        settingsUI = (SettingsUI)EditorGUILayout.ObjectField("SettingsUI", settingsUI, typeof(SettingsUI), true);
        templateSlider = (Slider)EditorGUILayout.ObjectField("Template Slider", templateSlider, typeof(Slider), true);
        templateToggle = (Toggle)EditorGUILayout.ObjectField("Template Toggle", templateToggle, typeof(Toggle), true);
        templateDropdown = (TMP_Dropdown)EditorGUILayout.ObjectField("Template Dropdown", templateDropdown, typeof(TMP_Dropdown), true);
        templateInput = (TMP_InputField)EditorGUILayout.ObjectField("Template InputField", templateInput, typeof(TMP_InputField), true);

        GUILayout.Space(8);
        GUILayout.Label("Tab Content Roots (optional — falls back to settingsPanel)", EditorStyles.boldLabel);
        gameplayTabPanel = (RectTransform)EditorGUILayout.ObjectField("Gameplay Tab", gameplayTabPanel, typeof(RectTransform), true);
        graphicsTabPanel = (RectTransform)EditorGUILayout.ObjectField("Graphics Tab", graphicsTabPanel, typeof(RectTransform), true);
        accessibilityTabPanel = (RectTransform)EditorGUILayout.ObjectField("Accessibility Tab", accessibilityTabPanel, typeof(RectTransform), true);

        GUILayout.Space(12);
        EditorGUI.BeginDisabledGroup(settingsUI == null);
        if (GUILayout.Button("Build Extended Settings", GUILayout.Height(38)))
        {
            BuildExtension();
        }
        EditorGUI.EndDisabledGroup();

        EditorGUILayout.EndScrollView();
    }

    private void BuildExtension()
    {
        if (settingsUI == null) { Debug.LogError("[SettingsExtender] SettingsUI not assigned."); return; }
        if (templateSlider == null || templateToggle == null || templateDropdown == null)
        {
            Debug.LogError("[SettingsExtender] Need at least Slider / Toggle / Dropdown templates.");
            return;
        }

        Undo.SetCurrentGroupName("Build Extended Settings");
        int undoGroup = Undo.GetCurrentGroup();

        // Fallback parent: panel root (works even without tab refs).
        RectTransform fallback = settingsUI.settingsPanel != null
            ? settingsUI.settingsPanel.GetComponent<RectTransform>()
            : (RectTransform)settingsUI.transform;

        RectTransform gameplayParent = gameplayTabPanel != null ? gameplayTabPanel : fallback;
        RectTransform graphicsParent = graphicsTabPanel != null ? graphicsTabPanel : fallback;
        RectTransform accessibilityParent = accessibilityTabPanel != null ? accessibilityTabPanel : fallback;

        SerializedObject so = new SerializedObject(settingsUI);

        // Gameplay tab — mouse / camera
        Slider sens = CloneSlider("SensitivitySlider", gameplayParent, 0);
        TMP_InputField sensInput = CloneInput("SensitivityInput", gameplayParent, 1);
        Toggle invertY = CloneToggle("InvertYToggle", gameplayParent, 2);

        // Accessibility tab — subtitles
        Toggle subs = CloneToggle("SubtitlesToggle", accessibilityParent, 0);
        TMP_Dropdown subSize = CloneDropdown("SubtitleSizeDropdown", accessibilityParent, 1,
            new[] { "Small", "Medium", "Large" });

        // Accessibility tab — visual aids
        Toggle colorblind = CloneToggle("ColorblindToggle", accessibilityParent, 2);
        Toggle vignette = CloneToggle("LowHpVignetteToggle", accessibilityParent, 3);
        Toggle hitStop = CloneToggle("HitStopToggle", accessibilityParent, 4);

        // Graphics tab — quality preset, render scale, post fx, shadows
        TMP_Dropdown quality = CloneDropdown("QualityDropdown", graphicsParent, 0,
            new[] { "Low", "Medium", "High", "Ultra" });
        Slider renderScale = CloneSlider("RenderScaleSlider", graphicsParent, 1);
        TMP_InputField renderInput = CloneInput("RenderScaleInput", graphicsParent, 2);
        Toggle postFX = CloneToggle("PostFXToggle", graphicsParent, 3);
        Toggle dynShadows = CloneToggle("DynamicShadowsToggle", graphicsParent, 4);

        // Graphics tab — language
        TMP_Dropdown language = CloneDropdown("LanguageDropdown", graphicsParent, 5,
            new[] { "English", "Українська" });

        // Wire references through SerializedObject so we record undo
        // properly and the prefab is marked dirty.
        Assign(so, "sensitivitySlider", sens);
        Assign(so, "sensitivityInput", sensInput);
        Assign(so, "invertYToggle", invertY);
        Assign(so, "invertYCheckmark", FindCheckmarkGraphic(invertY));
        Assign(so, "subtitlesToggle", subs);
        Assign(so, "subtitlesCheckmark", FindCheckmarkGraphic(subs));
        Assign(so, "subtitleSizeDropdown", subSize);
        Assign(so, "colorblindToggle", colorblind);
        Assign(so, "colorblindCheckmark", FindCheckmarkGraphic(colorblind));
        Assign(so, "lowHpVignetteToggle", vignette);
        Assign(so, "lowHpVignetteCheckmark", FindCheckmarkGraphic(vignette));
        Assign(so, "hitStopToggle", hitStop);
        Assign(so, "hitStopCheckmark", FindCheckmarkGraphic(hitStop));
        Assign(so, "qualityDropdown", quality);
        Assign(so, "renderScaleSlider", renderScale);
        Assign(so, "renderScaleInput", renderInput);
        Assign(so, "postFXToggle", postFX);
        Assign(so, "postFXCheckmark", FindCheckmarkGraphic(postFX));
        Assign(so, "dynamicShadowsToggle", dynShadows);
        Assign(so, "dynamicShadowsCheckmark", FindCheckmarkGraphic(dynShadows));
        Assign(so, "languageDropdown", language);
        so.ApplyModifiedProperties();

        SetLabels(new Dictionary<Object, string>
        {
            { sens.gameObject, "Mouse Sensitivity" },
            { invertY.gameObject, "Invert Y Axis" },
            { subs.gameObject, "Subtitles" },
            { subSize.gameObject, "Subtitle Size" },
            { colorblind.gameObject, "Colorblind Mode" },
            { vignette.gameObject, "Low HP Vignette" },
            { hitStop.gameObject, "Hit-Stop FX" },
            { quality.gameObject, "Quality Preset" },
            { renderScale.gameObject, "Render Scale (%)" },
            { postFX.gameObject, "Post Processing" },
            { dynShadows.gameObject, "Dynamic Shadows" },
            { language.gameObject, "Language" },
        });

        Undo.CollapseUndoOperations(undoGroup);
        EditorUtility.SetDirty(settingsUI);
        if (PrefabUtility.IsPartOfPrefabInstance(settingsUI))
            PrefabUtility.RecordPrefabInstancePropertyModifications(settingsUI);

        Debug.Log("[SettingsExtender] Built 14 controls. Now reposition them inside each tab using a VerticalLayoutGroup or move them by hand.");
    }

    private Slider CloneSlider(string newName, RectTransform parent, int siblingIndex)
    {
        Slider clone = (Slider)PrefabUtility.InstantiatePrefab(templateSlider, parent) ?? Instantiate(templateSlider, parent);
        clone.gameObject.name = newName;
        clone.transform.SetSiblingIndex(parent.childCount - 1);
        Undo.RegisterCreatedObjectUndo(clone.gameObject, "Clone Slider");
        OffsetY(clone.GetComponent<RectTransform>(), siblingIndex);
        return clone;
    }

    private Toggle CloneToggle(string newName, RectTransform parent, int siblingIndex)
    {
        Toggle clone = Instantiate(templateToggle, parent);
        clone.gameObject.name = newName;
        Undo.RegisterCreatedObjectUndo(clone.gameObject, "Clone Toggle");
        OffsetY(clone.GetComponent<RectTransform>(), siblingIndex);
        return clone;
    }

    private TMP_Dropdown CloneDropdown(string newName, RectTransform parent, int siblingIndex, string[] options)
    {
        TMP_Dropdown clone = Instantiate(templateDropdown, parent);
        clone.gameObject.name = newName;
        clone.ClearOptions();
        clone.AddOptions(options.ToList());
        Undo.RegisterCreatedObjectUndo(clone.gameObject, "Clone Dropdown");
        OffsetY(clone.GetComponent<RectTransform>(), siblingIndex);
        return clone;
    }

    private TMP_InputField CloneInput(string newName, RectTransform parent, int siblingIndex)
    {
        if (templateInput == null) return null;
        TMP_InputField clone = Instantiate(templateInput, parent);
        clone.gameObject.name = newName;
        Undo.RegisterCreatedObjectUndo(clone.gameObject, "Clone Input");
        OffsetY(clone.GetComponent<RectTransform>(), siblingIndex);
        return clone;
    }

    // Stack new controls vertically with 60 px spacing so they don't
    // pile on top of the template. The user will fine-tune via
    // VerticalLayoutGroup in the Editor.
    private static void OffsetY(RectTransform rect, int siblingIndex)
    {
        if (rect == null) return;
        Vector2 pos = rect.anchoredPosition;
        pos.y -= siblingIndex * 60f;
        rect.anchoredPosition = pos;
    }

    // The SettingsUI script animates a checkmark Graphic per toggle.
    // Find the first Image / Graphic under the toggle's "Checkmark"
    // child (Unity's default toggle layout).
    private static Graphic FindCheckmarkGraphic(Toggle toggle)
    {
        if (toggle == null) return null;
        if (toggle.graphic != null) return toggle.graphic;
        Transform check = FindChildRecursive(toggle.transform, "Checkmark");
        return check != null ? check.GetComponent<Graphic>() : null;
    }

    private static Transform FindChildRecursive(Transform parent, string name)
    {
        for (int i = 0; i < parent.childCount; i++)
        {
            Transform c = parent.GetChild(i);
            if (c.name.Equals(name, System.StringComparison.OrdinalIgnoreCase)) return c;
            Transform deep = FindChildRecursive(c, name);
            if (deep != null) return deep;
        }
        return null;
    }

    private static void Assign(SerializedObject so, string field, Object value)
    {
        SerializedProperty p = so.FindProperty(field);
        if (p != null) p.objectReferenceValue = value;
    }

    // Look for a TMP child labelled "Label" and set readable text on it.
    private static void SetLabels(Dictionary<Object, string> map)
    {
        foreach (var kv in map)
        {
            GameObject go = kv.Key as GameObject;
            if (go == null) continue;
            TextMeshProUGUI[] labels = go.GetComponentsInChildren<TextMeshProUGUI>(true);
            foreach (var l in labels)
            {
                if (l.name.ToLower().Contains("label") || l.name.ToLower().Contains("text"))
                {
                    l.text = kv.Value;
                    break;
                }
            }
        }
    }
}
#endif
