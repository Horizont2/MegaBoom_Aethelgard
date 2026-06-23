#if UNITY_EDITOR
using UnityEngine;
using UnityEngine.UI;
using UnityEditor;
using TMPro;
using System.Collections.Generic;
using System.Linq;

// Editor-time layout polisher for the SettingsPanel. Takes a tab root
// that already contains a flat pile of Sliders / Toggles / Dropdowns /
// InputFields (the output of SettingsPanelExtender) and rewires it into
// AAA-style structure:
//
//   TabRoot
//     ScrollRect  ─ Scrollbar (auto-hide-when-fits)
//       Viewport (Mask)
//         Content (VerticalLayoutGroup + ContentSizeFitter)
//           [Section Header: "Mouse & Camera"]
//           [Row: Label + Slider + InputField]
//           [Row: Label + Toggle]
//           [Section Header: ...]
//           ...
//
// Each control is wrapped in a Row with a HorizontalLayoutGroup so the
// label and control sit side-by-side at a uniform height. Section
// headers are TMP_Text rows with a thicker font.
public class SettingsPanelPolisher : EditorWindow
{
    [MenuItem("Tools/MegaBoom/Polish Settings Layout")]
    public static void ShowWindow()
    {
        GetWindow<SettingsPanelPolisher>("Settings Polisher");
    }

    private RectTransform tabRoot;
    private TMP_FontAsset headerFont;
    private TMP_FontAsset labelFont;
    private Color headerColor = new Color(1f, 0.84f, 0.3f, 1f);
    private Color labelColor = new Color(0.92f, 0.90f, 0.85f, 1f);
    private int rowHeight = 46;
    private int sectionHeaderHeight = 56;
    private int sectionTopSpacing = 18;
    private int rowSpacing = 8;
    private int padding = 24;
    private float labelWidthFraction = 0.45f;
    private int headerFontSize = 26;
    private int labelFontSize = 18;
    private bool configureInputFields = true;

    private string sectionsRaw =
        "Mouse & Camera | SensitivitySlider, InvertYToggle\n" +
        "Audio | MasterParent, MusicParent, SFXParent, MasterSlider, MusicSlider, SFXSlider\n" +
        "Subtitles | SubtitlesToggle, SubtitleSizeDropdown\n" +
        "Accessibility | ColorblindToggle, LowHpVignetteToggle, HitStopToggle\n" +
        "Graphics | QualityDropdown, RenderScaleSlider, PostFXToggle, DynamicShadowsToggle\n" +
        "Gameplay HUD | DamagePopupsToggle, ScreenShakeToggle, FPSLimit, ShowFPS, LimitFPSToggle, ShowFPSToggle\n" +
        "Language | LanguageDropdown";

    private Vector2 scroll;

    private void OnGUI()
    {
        scroll = EditorGUILayout.BeginScrollView(scroll);

        GUILayout.Label("Settings Panel Polisher", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "1) Open HUD_Canvas.prefab in prefab mode.\n" +
            "2) Drag a tab root (DisplayPanel / ControlsPanel / AudioPanel) into 'Tab Root'.\n" +
            "3) Optionally drag font assets — falls back to the first TMP child's font.\n" +
            "4) Edit the section map below — first column is the visible header, second is a comma-separated list of GameObject-name fragments to put under that section.\n" +
            "5) Click Polish. Re-run for each tab.",
            MessageType.Info);

        tabRoot = (RectTransform)EditorGUILayout.ObjectField("Tab Root", tabRoot, typeof(RectTransform), true);
        headerFont = (TMP_FontAsset)EditorGUILayout.ObjectField("Header Font", headerFont, typeof(TMP_FontAsset), false);
        labelFont = (TMP_FontAsset)EditorGUILayout.ObjectField("Label Font", labelFont, typeof(TMP_FontAsset), false);
        headerColor = EditorGUILayout.ColorField("Header Color", headerColor);
        labelColor = EditorGUILayout.ColorField("Label Color", labelColor);

        GUILayout.Space(6);
        GUILayout.Label("Layout Sizing", EditorStyles.boldLabel);
        rowHeight = EditorGUILayout.IntSlider("Row Height", rowHeight, 30, 80);
        sectionHeaderHeight = EditorGUILayout.IntSlider("Section Header Height", sectionHeaderHeight, 36, 90);
        sectionTopSpacing = EditorGUILayout.IntSlider("Spacing Above Section", sectionTopSpacing, 0, 60);
        rowSpacing = EditorGUILayout.IntSlider("Row Spacing", rowSpacing, 0, 24);
        padding = EditorGUILayout.IntSlider("Content Padding", padding, 0, 60);
        labelWidthFraction = EditorGUILayout.Slider("Label Width %", labelWidthFraction, 0.25f, 0.7f);
        headerFontSize = EditorGUILayout.IntSlider("Header Font Size", headerFontSize, 18, 42);
        labelFontSize = EditorGUILayout.IntSlider("Label Font Size", labelFontSize, 12, 28);
        configureInputFields = EditorGUILayout.Toggle("Configure InputFields", configureInputFields);

        GUILayout.Space(8);
        GUILayout.Label("Section Map (header | name fragments, comma-separated)", EditorStyles.boldLabel);
        sectionsRaw = EditorGUILayout.TextArea(sectionsRaw, GUILayout.MinHeight(140));

        GUILayout.Space(10);
        EditorGUI.BeginDisabledGroup(tabRoot == null);
        if (GUILayout.Button("Polish Layout", GUILayout.Height(40)))
        {
            Polish();
        }
        EditorGUI.EndDisabledGroup();

        EditorGUILayout.EndScrollView();
    }

    private void Polish()
    {
        if (tabRoot == null) { Debug.LogError("[Polisher] Assign Tab Root first."); return; }

        Undo.SetCurrentGroupName("Polish Settings Layout");
        int undoGroup = Undo.GetCurrentGroup();

        // Snapshot every direct child so we can reparent without losing them
        // when we add the ScrollRect tree underneath.
        List<RectTransform> originalChildren = new List<RectTransform>();
        for (int i = 0; i < tabRoot.childCount; i++)
        {
            RectTransform c = tabRoot.GetChild(i) as RectTransform;
            if (c != null) originalChildren.Add(c);
        }

        // Try to inherit a font if the user didn't specify one.
        if (labelFont == null || headerFont == null)
        {
            TMP_Text firstTmp = tabRoot.GetComponentInChildren<TMP_Text>(true);
            if (firstTmp != null)
            {
                if (labelFont == null) labelFont = firstTmp.font;
                if (headerFont == null) headerFont = firstTmp.font;
            }
        }

        // Build ScrollRect tree as direct children of tabRoot.
        RectTransform viewport, content;
        Scrollbar verticalBar;
        ScrollRect scrollRect = BuildScrollTree(tabRoot, out viewport, out content, out verticalBar);

        // Parse section spec.
        List<(string header, string[] fragments)> sections = ParseSections(sectionsRaw);

        // Each original control goes into the section whose fragment list
        // first matches its GameObject name (case-insensitive).
        Dictionary<int, List<RectTransform>> sectionBuckets = new Dictionary<int, List<RectTransform>>();
        List<RectTransform> unmatched = new List<RectTransform>();

        foreach (RectTransform child in originalChildren)
        {
            int sectionIdx = FindSectionFor(child.name, sections);
            if (sectionIdx < 0) { unmatched.Add(child); continue; }
            if (!sectionBuckets.TryGetValue(sectionIdx, out var bucket))
            {
                bucket = new List<RectTransform>();
                sectionBuckets[sectionIdx] = bucket;
            }
            bucket.Add(child);
        }

        // For each non-empty section: emit a header row, then emit one
        // row per control wrapping it with a label + control layout.
        for (int s = 0; s < sections.Count; s++)
        {
            if (!sectionBuckets.TryGetValue(s, out var bucket) || bucket.Count == 0) continue;

            CreateHeaderRow(content, sections[s].header);
            foreach (RectTransform ctrl in bucket)
            {
                WrapInRow(content, ctrl);
            }
        }

        // Anything that didn't match a section goes under an "Other" header.
        if (unmatched.Count > 0)
        {
            CreateHeaderRow(content, "Other");
            foreach (RectTransform ctrl in unmatched) WrapInRow(content, ctrl);
        }

        if (configureInputFields)
        {
            foreach (TMP_InputField inp in content.GetComponentsInChildren<TMP_InputField>(true))
            {
                Undo.RecordObject(inp, "Config InputField");
                inp.contentType = TMP_InputField.ContentType.IntegerNumber;
                inp.characterLimit = 4;
                if (inp.textComponent != null)
                {
                    Undo.RecordObject(inp.textComponent, "Config InputField text");
                    inp.textComponent.alignment = TextAlignmentOptions.Center;
                    if (inp.textComponent is TextMeshProUGUI tmp) tmp.enableWordWrapping = false;
                }
                EditorUtility.SetDirty(inp);
            }
        }

        Canvas.ForceUpdateCanvases();
        LayoutRebuilder.ForceRebuildLayoutImmediate(content);
        LayoutRebuilder.ForceRebuildLayoutImmediate(tabRoot);

        Undo.CollapseUndoOperations(undoGroup);
        EditorUtility.SetDirty(tabRoot);
        Debug.Log($"[Polisher] '{tabRoot.name}': built {sections.Count} sections, {originalChildren.Count} controls reorganized.");
    }

    private ScrollRect BuildScrollTree(RectTransform parent, out RectTransform viewport, out RectTransform content, out Scrollbar verticalBar)
    {
        // Make the tab fill its own parent so the ScrollRect has a real
        // working rect — most settings panels anchor children manually,
        // which leaves the tab root with a zero size.
        parent.anchorMin = new Vector2(0f, 0f);
        parent.anchorMax = new Vector2(1f, 1f);
        parent.offsetMin = Vector2.zero;
        parent.offsetMax = Vector2.zero;

        GameObject scrollGO = new GameObject("ScrollRect", typeof(RectTransform), typeof(ScrollRect), typeof(Image));
        RectTransform scrollRT = scrollGO.GetComponent<RectTransform>();
        scrollRT.SetParent(parent, false);
        StretchFull(scrollRT);
        Image scrollBg = scrollGO.GetComponent<Image>();
        scrollBg.color = new Color(0f, 0f, 0f, 0f);
        scrollBg.raycastTarget = false;
        Undo.RegisterCreatedObjectUndo(scrollGO, "Create ScrollRect");

        GameObject viewportGO = new GameObject("Viewport", typeof(RectTransform), typeof(Image), typeof(Mask));
        RectTransform viewportRT = viewportGO.GetComponent<RectTransform>();
        viewportRT.SetParent(scrollRT, false);
        StretchFull(viewportRT);
        viewportRT.offsetMin = new Vector2(0f, 0f);
        viewportRT.offsetMax = new Vector2(-12f, 0f); // leave room for scrollbar
        Image vpImg = viewportGO.GetComponent<Image>();
        vpImg.color = new Color(1f, 1f, 1f, 0.01f); // mask needs an Image
        vpImg.raycastTarget = false;
        Mask mask = viewportGO.GetComponent<Mask>();
        mask.showMaskGraphic = false;
        Undo.RegisterCreatedObjectUndo(viewportGO, "Create Viewport");

        GameObject contentGO = new GameObject("Content", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
        RectTransform contentRT = contentGO.GetComponent<RectTransform>();
        contentRT.SetParent(viewportRT, false);
        contentRT.anchorMin = new Vector2(0f, 1f);
        contentRT.anchorMax = new Vector2(1f, 1f);
        contentRT.pivot = new Vector2(0.5f, 1f);
        contentRT.anchoredPosition = Vector2.zero;
        contentRT.sizeDelta = new Vector2(0f, 0f);

        VerticalLayoutGroup vlg = contentGO.GetComponent<VerticalLayoutGroup>();
        vlg.padding = new RectOffset(padding, padding, padding, padding);
        vlg.spacing = rowSpacing;
        vlg.childAlignment = TextAnchor.UpperCenter;
        vlg.childControlWidth = true;
        vlg.childControlHeight = false;
        vlg.childForceExpandWidth = true;
        vlg.childForceExpandHeight = false;

        ContentSizeFitter csf = contentGO.GetComponent<ContentSizeFitter>();
        csf.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
        csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        Undo.RegisterCreatedObjectUndo(contentGO, "Create Content");

        // Vertical scrollbar.
        GameObject barGO = new GameObject("Scrollbar Vertical",
            typeof(RectTransform), typeof(Image), typeof(Scrollbar));
        RectTransform barRT = barGO.GetComponent<RectTransform>();
        barRT.SetParent(scrollRT, false);
        barRT.anchorMin = new Vector2(1f, 0f);
        barRT.anchorMax = new Vector2(1f, 1f);
        barRT.pivot = new Vector2(1f, 0.5f);
        barRT.sizeDelta = new Vector2(12f, 0f);
        barRT.anchoredPosition = Vector2.zero;
        Image barBg = barGO.GetComponent<Image>();
        barBg.color = new Color(0f, 0f, 0f, 0.35f);

        verticalBar = barGO.GetComponent<Scrollbar>();
        verticalBar.direction = Scrollbar.Direction.BottomToTop;

        // Sliding handle for the scrollbar.
        GameObject slidingArea = new GameObject("Sliding Area", typeof(RectTransform));
        RectTransform slidingRT = slidingArea.GetComponent<RectTransform>();
        slidingRT.SetParent(barRT, false);
        StretchFull(slidingRT);
        slidingRT.offsetMin = new Vector2(2f, 2f);
        slidingRT.offsetMax = new Vector2(-2f, -2f);

        GameObject handleGO = new GameObject("Handle", typeof(RectTransform), typeof(Image));
        RectTransform handleRT = handleGO.GetComponent<RectTransform>();
        handleRT.SetParent(slidingRT, false);
        StretchFull(handleRT);
        Image handleImg = handleGO.GetComponent<Image>();
        handleImg.color = new Color(1f, 0.84f, 0.3f, 0.9f);

        verticalBar.handleRect = handleRT;
        verticalBar.targetGraphic = handleImg;
        Undo.RegisterCreatedObjectUndo(barGO, "Create Scrollbar");

        ScrollRect sr = scrollGO.GetComponent<ScrollRect>();
        sr.viewport = viewportRT;
        sr.content = contentRT;
        sr.horizontal = false;
        sr.vertical = true;
        sr.movementType = ScrollRect.MovementType.Elastic;
        sr.inertia = true;
        sr.decelerationRate = 0.135f;
        sr.scrollSensitivity = 30f;
        sr.verticalScrollbar = verticalBar;
        sr.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHideAndExpandViewport;

        viewport = viewportRT;
        content = contentRT;
        return sr;
    }

    private void CreateHeaderRow(RectTransform contentRoot, string text)
    {
        // Header row uses a layout element so VerticalLayoutGroup gives it
        // sectionHeaderHeight (plus sectionTopSpacing on top via padding).
        GameObject rowGO = new GameObject("Section_" + text.Replace(' ', '_'),
            typeof(RectTransform), typeof(LayoutElement), typeof(TextMeshProUGUI));
        rowGO.transform.SetParent(contentRoot, false);

        LayoutElement le = rowGO.GetComponent<LayoutElement>();
        le.minHeight = sectionHeaderHeight;
        le.preferredHeight = sectionHeaderHeight;
        le.flexibleHeight = 0f;

        TextMeshProUGUI tmp = rowGO.GetComponent<TextMeshProUGUI>();
        tmp.text = text.ToUpperInvariant();
        tmp.color = headerColor;
        tmp.fontSize = headerFontSize;
        tmp.fontStyle = FontStyles.Bold;
        tmp.alignment = TextAlignmentOptions.Left;
        tmp.enableWordWrapping = false;
        if (headerFont != null) tmp.font = headerFont;
        tmp.margin = new Vector4(0f, sectionTopSpacing, 0f, 0f);

        // Subtle underline using a sibling Image — gives the AAA "rule"
        // beneath each section header.
        GameObject ruleGO = new GameObject("Rule",
            typeof(RectTransform), typeof(Image));
        ruleGO.transform.SetParent(rowGO.transform, false);
        RectTransform rrt = ruleGO.GetComponent<RectTransform>();
        rrt.anchorMin = new Vector2(0f, 0f);
        rrt.anchorMax = new Vector2(1f, 0f);
        rrt.pivot = new Vector2(0.5f, 0f);
        rrt.sizeDelta = new Vector2(0f, 2f);
        rrt.anchoredPosition = new Vector2(0f, 0f);
        Image ruleImg = ruleGO.GetComponent<Image>();
        ruleImg.color = new Color(headerColor.r, headerColor.g, headerColor.b, 0.45f);

        Undo.RegisterCreatedObjectUndo(rowGO, "Create Section Header");
    }

    private void WrapInRow(RectTransform contentRoot, RectTransform control)
    {
        GameObject rowGO = new GameObject("Row_" + control.name,
            typeof(RectTransform),
            typeof(LayoutElement),
            typeof(HorizontalLayoutGroup));
        rowGO.transform.SetParent(contentRoot, false);
        rowGO.transform.SetSiblingIndex(contentRoot.childCount - 1);

        LayoutElement le = rowGO.GetComponent<LayoutElement>();
        le.minHeight = rowHeight;
        le.preferredHeight = rowHeight;
        le.flexibleHeight = 0f;

        HorizontalLayoutGroup hlg = rowGO.GetComponent<HorizontalLayoutGroup>();
        hlg.padding = new RectOffset(8, 8, 4, 4);
        hlg.spacing = 12;
        hlg.childAlignment = TextAnchor.MiddleLeft;
        hlg.childControlWidth = true;
        hlg.childControlHeight = true;
        hlg.childForceExpandWidth = false;
        hlg.childForceExpandHeight = false;

        // Label on the left — derive a human-readable name from the
        // GameObject (strip suffixes like "Toggle"/"Slider"/"Dropdown").
        GameObject labelGO = new GameObject("Label",
            typeof(RectTransform),
            typeof(LayoutElement),
            typeof(TextMeshProUGUI));
        labelGO.transform.SetParent(rowGO.transform, false);
        LayoutElement labelLE = labelGO.GetComponent<LayoutElement>();
        labelLE.preferredWidth = -1f;
        labelLE.flexibleWidth = labelWidthFraction;
        TextMeshProUGUI labelTmp = labelGO.GetComponent<TextMeshProUGUI>();
        labelTmp.text = HumanizeName(control.name);
        labelTmp.color = labelColor;
        labelTmp.fontSize = labelFontSize;
        labelTmp.alignment = TextAlignmentOptions.MidlineLeft;
        labelTmp.enableWordWrapping = false;
        if (labelFont != null) labelTmp.font = labelFont;
        Undo.RegisterCreatedObjectUndo(labelGO, "Create Row Label");

        // Reparent the control into the row. The HorizontalLayoutGroup
        // controls width via the LayoutElement we attach here.
        Undo.SetTransformParent(control, rowGO.transform, "Reparent Control");
        control.SetSiblingIndex(rowGO.transform.childCount - 1);
        control.anchorMin = new Vector2(0f, 0.5f);
        control.anchorMax = new Vector2(0f, 0.5f);
        control.pivot = new Vector2(0f, 0.5f);

        LayoutElement ctrlLE = control.GetComponent<LayoutElement>();
        if (ctrlLE == null) ctrlLE = Undo.AddComponent<LayoutElement>(control.gameObject);
        ctrlLE.flexibleWidth = 1f - labelWidthFraction;
        ctrlLE.preferredWidth = -1f;
        ctrlLE.preferredHeight = rowHeight - 8f;

        // If the control is a Slider, give it a sibling InputField only if
        // its name suggests numeric input — caller already gave us
        // SensitivityInput / RenderScaleInput as separate controls
        // though, so we typically don't need to add one here.
    }

    private static void StretchFull(RectTransform rt)
    {
        rt.anchorMin = new Vector2(0f, 0f);
        rt.anchorMax = new Vector2(1f, 1f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
    }

    private static List<(string header, string[] fragments)> ParseSections(string raw)
    {
        List<(string header, string[] fragments)> result = new List<(string, string[])>();
        string[] lines = raw.Split(new[] { '\n', '\r' }, System.StringSplitOptions.RemoveEmptyEntries);
        foreach (string line in lines)
        {
            int pipe = line.IndexOf('|');
            if (pipe < 0) continue;
            string header = line.Substring(0, pipe).Trim();
            string[] frags = line.Substring(pipe + 1)
                .Split(',')
                .Select(f => f.Trim().ToLowerInvariant())
                .Where(f => f.Length > 0)
                .ToArray();
            result.Add((header, frags));
        }
        return result;
    }

    private static int FindSectionFor(string controlName, List<(string header, string[] fragments)> sections)
    {
        string lowered = controlName.ToLowerInvariant();
        for (int i = 0; i < sections.Count; i++)
        {
            foreach (string frag in sections[i].fragments)
            {
                if (lowered.Contains(frag)) return i;
            }
        }
        return -1;
    }

    // "SensitivitySlider" -> "Sensitivity", "InvertYToggle" -> "Invert Y",
    // "LowHpVignetteToggle" -> "Low Hp Vignette", "RenderScaleInput" -> "Render Scale".
    private static string HumanizeName(string raw)
    {
        if (string.IsNullOrEmpty(raw)) return raw;
        string trimmed = raw;
        string[] suffixes = { "Slider", "Toggle", "Dropdown", "Input", "InputField", "(TMP)" };
        foreach (string suffix in suffixes)
        {
            if (trimmed.EndsWith(suffix, System.StringComparison.OrdinalIgnoreCase))
                trimmed = trimmed.Substring(0, trimmed.Length - suffix.Length);
        }
        System.Text.StringBuilder sb = new System.Text.StringBuilder();
        for (int i = 0; i < trimmed.Length; i++)
        {
            char c = trimmed[i];
            if (i > 0 && char.IsUpper(c) && !char.IsUpper(trimmed[i - 1])) sb.Append(' ');
            sb.Append(c);
        }
        return sb.ToString().Trim();
    }
}
#endif
