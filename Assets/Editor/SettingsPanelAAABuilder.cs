#if UNITY_EDITOR
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Events;
using UnityEditor;
using TMPro;
using System.Collections.Generic;

// Editor-time builder for the full-screen AAA Settings overlay.
//
//   Header (80) — title + close
//   ┌────────────┬──────────────────────────────┬──────────────┐
//   │ Sidebar    │ Center (ScrollRect + Content)│ Right Rail   │
//   │ 6 buttons  │ Section headers + Rows       │ Description  │
//   └────────────┴──────────────────────────────┴──────────────┘
//   Footer (90) — Reset / Discard / Apply
//
// Drops the whole panel under the canvas the user picks, builds 6
// per-category content roots (General / Gameplay / Audio / Display /
// Controls / Accessibility / Language — collapsed where overlap), and
// clones existing controls into the matching category.
public class SettingsPanelAAABuilder : EditorWindow
{
    [MenuItem("Tools/MegaBoom/Build AAA Settings Panel")]
    public static void ShowWindow() { GetWindow<SettingsPanelAAABuilder>("AAA Settings Builder"); }

    // ----- Inputs -----
    private Canvas targetCanvas;
    private SettingsUI settingsUI;
    private TMP_FontAsset font;

    private Slider templateSlider;
    private Toggle templateToggle;
    private TMP_Dropdown templateDropdown;
    private TMP_InputField templateInput;

    private Sprite panelBgSprite;
    private Sprite buttonSprite;
    private Sprite handleSprite;

    // ----- Theme -----
    // Tuned to match the painted-card mockup style: semi-transparent dark
    // panels over a scene background, gold reserved for title + active
    // sidebar accent + APPLY button, soft cyan reserved for slider fill.
    private Color colBg = new Color(0.03f, 0.03f, 0.05f, 0.55f);
    private Color colPanel = new Color(0.09f, 0.10f, 0.13f, 0.92f);
    private Color colAccent = new Color(1f, 0.82f, 0.24f, 1f);
    private Color colSlider = new Color(0.36f, 0.75f, 0.87f, 1f);
    private Color colText = new Color(0.95f, 0.94f, 0.90f, 1f);
    private Color colTextDim = new Color(0.72f, 0.70f, 0.65f, 1f);
    private Color colBorder = new Color(1f, 0.82f, 0.24f, 0.35f);
    private Color colTrack = new Color(0.16f, 0.18f, 0.22f, 1f);

    private Vector2 scroll;

    private struct CatSpec { public string id; public string label; public string icon; }

    // The categories that show up in the sidebar. Icon is a single
    // glyph TMP renders as a sprite-like prefix.
    private static readonly CatSpec[] Categories = new CatSpec[]
    {
        new CatSpec { id = "General",       label = "GENERAL",       icon = "▎" },
        new CatSpec { id = "Gameplay",      label = "GAMEPLAY",      icon = "⚔" },
        new CatSpec { id = "Audio",         label = "AUDIO",         icon = "♪" },
        new CatSpec { id = "Video",         label = "VIDEO",         icon = "▦" },
        new CatSpec { id = "Graphics",      label = "GRAPHICS",      icon = "□" },
        new CatSpec { id = "Controls",      label = "CONTROLS",      icon = "⌖" },
        new CatSpec { id = "Accessibility", label = "ACCESSIBILITY", icon = "✦" },
        new CatSpec { id = "Language",      label = "LANGUAGE",      icon = "✎" },
    };

    private void OnGUI()
    {
        scroll = EditorGUILayout.BeginScrollView(scroll);

        GUILayout.Label("AAA Settings Builder", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "Builds a full-screen settings overlay on the chosen Canvas.\n" +
            "Wires the new controls into the SettingsUI script you point at.\n" +
            "Templates (Slider/Toggle/Dropdown/InputField) get cloned per row.\n" +
            "Leave sprites empty for default flat UI fill.",
            MessageType.Info);

        targetCanvas = (Canvas)EditorGUILayout.ObjectField("Target Canvas", targetCanvas, typeof(Canvas), true);
        settingsUI = (SettingsUI)EditorGUILayout.ObjectField("SettingsUI", settingsUI, typeof(SettingsUI), true);
        font = (TMP_FontAsset)EditorGUILayout.ObjectField("Font", font, typeof(TMP_FontAsset), false);

        GUILayout.Space(6);
        GUILayout.Label("Control Templates (cloned for each row)", EditorStyles.boldLabel);
        templateSlider = (Slider)EditorGUILayout.ObjectField("Slider", templateSlider, typeof(Slider), true);
        templateToggle = (Toggle)EditorGUILayout.ObjectField("Toggle", templateToggle, typeof(Toggle), true);
        templateDropdown = (TMP_Dropdown)EditorGUILayout.ObjectField("Dropdown", templateDropdown, typeof(TMP_Dropdown), true);
        templateInput = (TMP_InputField)EditorGUILayout.ObjectField("InputField", templateInput, typeof(TMP_InputField), true);

        GUILayout.Space(6);
        GUILayout.Label("Optional Sprites", EditorStyles.boldLabel);
        panelBgSprite = (Sprite)EditorGUILayout.ObjectField("Panel BG", panelBgSprite, typeof(Sprite), false);
        buttonSprite = (Sprite)EditorGUILayout.ObjectField("Button BG", buttonSprite, typeof(Sprite), false);
        handleSprite = (Sprite)EditorGUILayout.ObjectField("Slider Handle", handleSprite, typeof(Sprite), false);

        GUILayout.Space(6);
        GUILayout.Label("Theme Colors", EditorStyles.boldLabel);
        colBg = EditorGUILayout.ColorField("Overlay BG", colBg);
        colPanel = EditorGUILayout.ColorField("Panel Fill", colPanel);
        colAccent = EditorGUILayout.ColorField("Accent (gold)", colAccent);
        colSlider = EditorGUILayout.ColorField("Slider Fill (cyan)", colSlider);
        colText = EditorGUILayout.ColorField("Text", colText);
        colTextDim = EditorGUILayout.ColorField("Dim Text", colTextDim);
        colBorder = EditorGUILayout.ColorField("Border", colBorder);
        colTrack = EditorGUILayout.ColorField("Slider Track", colTrack);

        GUILayout.Space(10);
        EditorGUI.BeginDisabledGroup(targetCanvas == null || settingsUI == null);
        if (GUILayout.Button("Build AAA Settings Panel", GUILayout.Height(42))) Build();
        EditorGUI.EndDisabledGroup();

        EditorGUILayout.EndScrollView();
    }

    // ============================================================
    // Build pipeline
    // ============================================================

    private RectTransform centerScrollContent;
    private GameObject descriptionPanel;
    private TextMeshProUGUI descriptionText;
    private Dictionary<string, RectTransform> categoryRoots = new Dictionary<string, RectTransform>();
    private Dictionary<string, Button> categoryButtons = new Dictionary<string, Button>();
    private List<SettingsAAARuntime.HoverRow> hoverRows = new List<SettingsAAARuntime.HoverRow>();
    private List<Image> categoryStripes = new List<Image>();

    private void Build()
    {
        if (font == null)
        {
            TMP_Text any = targetCanvas.GetComponentInChildren<TMP_Text>(true);
            if (any != null) font = any.font;
        }

        Undo.SetCurrentGroupName("Build AAA Settings");
        int undoGroup = Undo.GetCurrentGroup();

        // Root: full-screen overlay sitting under the canvas.
        GameObject rootGO = new GameObject("SettingsPanelAAA",
            typeof(RectTransform), typeof(CanvasGroup), typeof(Image));
        Undo.RegisterCreatedObjectUndo(rootGO, "Create AAA Root");
        rootGO.transform.SetParent(targetCanvas.transform, false);
        RectTransform rootRT = rootGO.GetComponent<RectTransform>();
        StretchFull(rootRT);

        Image rootBg = rootGO.GetComponent<Image>();
        rootBg.color = colBg;
        rootBg.raycastTarget = true;

        CanvasGroup rootCg = rootGO.GetComponent<CanvasGroup>();
        rootCg.alpha = 1f;
        rootCg.interactable = true;
        rootCg.blocksRaycasts = true;

        // Header.
        RectTransform header = BuildHeader(rootRT);

        // Footer.
        RectTransform footer = BuildFooter(rootRT);

        // Middle band fills the gap between header and footer with three
        // columns: sidebar (240), center scroll, right rail (360).
        RectTransform mid = NewRect("Mid", rootRT);
        mid.anchorMin = new Vector2(0f, 0f);
        mid.anchorMax = new Vector2(1f, 1f);
        mid.offsetMin = new Vector2(40f, 110f);
        mid.offsetMax = new Vector2(-40f, -90f);

        RectTransform sidebar = BuildSidebar(mid);
        RectTransform center = BuildCenter(mid);
        RectTransform rightRail = BuildRightRail(mid);

        // Per-category content panels stacked inside the scroll content.
        // Only the selected category is enabled at a time.
        BuildCategoryContents();

        // Make sure every dropdown that lives on this panel has the
        // canonical option list before any row tries to bind to it.
        PrepopulateDropdownOptions();

        // Populate each category with rows pulled from the existing
        // SettingsUI references.
        PopulateAllSections();

        // Attach runtime controller that drives:
        //  - category switching
        //  - description on row hover
        //  - apply / discard / reset
        SettingsAAARuntime runtime = rootGO.AddComponent<SettingsAAARuntime>();
        runtime.settingsUI = settingsUI;
        runtime.canvasGroup = rootCg;
        runtime.descriptionText = descriptionText;
        runtime.categoryRoots = new List<RectTransform>();
        runtime.categoryStripes = new List<Image>();
        foreach (var spec in Categories)
        {
            if (categoryRoots.TryGetValue(spec.id, out var rt)) runtime.categoryRoots.Add(rt);
            else runtime.categoryRoots.Add(null);
        }
        foreach (var s in categoryStripes) runtime.categoryStripes.Add(s);
        runtime.accentColor = colAccent;
        runtime.dimAccent = new Color(colAccent.r, colAccent.g, colAccent.b, 0.12f);
        runtime.hoverRows = hoverRows;
        runtime.Initialise();

        // Wire SettingsUI panel reference so existing OpenSettings()
        // logic activates this new root.
        SerializedObject so = new SerializedObject(settingsUI);
        so.FindProperty("settingsPanel").objectReferenceValue = rootGO;
        so.FindProperty("panelCanvasGroup").objectReferenceValue = rootCg;
        so.FindProperty("panelRect").objectReferenceValue = rootRT;
        so.ApplyModifiedProperties();

        Undo.CollapseUndoOperations(undoGroup);
        EditorUtility.SetDirty(rootGO);
        EditorUtility.SetDirty(settingsUI);
        rootGO.SetActive(false);

        Debug.Log("[AAA Builder] Settings panel built. Re-open Settings menu to see it.");
    }

    // ----- Sections -----

    private RectTransform BuildHeader(RectTransform parent)
    {
        RectTransform h = NewRect("Header", parent);
        h.anchorMin = new Vector2(0f, 1f);
        h.anchorMax = new Vector2(1f, 1f);
        h.pivot = new Vector2(0.5f, 1f);
        h.offsetMin = new Vector2(40f, -80f);
        h.offsetMax = new Vector2(-40f, -20f);
        AddPanelBg(h, colPanel);
        AddBorder(h, colBorder);

        TextMeshProUGUI title = AddText(h, "TitleText", "S E T T I N G S", 38, FontStyles.Bold);
        title.alignment = TextAlignmentOptions.Center;
        title.color = colAccent;
        StretchFull(title.rectTransform);

        Button back = MakeFlatButton(h, "BackButton", "◄ BACK");
        RectTransform brt = back.GetComponent<RectTransform>();
        brt.anchorMin = new Vector2(0f, 0.5f);
        brt.anchorMax = new Vector2(0f, 0.5f);
        brt.pivot = new Vector2(0f, 0.5f);
        brt.sizeDelta = new Vector2(140f, 44f);
        brt.anchoredPosition = new Vector2(20f, 0f);
        back.onClick.AddListener(() => settingsUI.CloseSettings());

        Button close = MakeFlatButton(h, "CloseButton", "✕");
        RectTransform crt = close.GetComponent<RectTransform>();
        crt.anchorMin = new Vector2(1f, 0.5f);
        crt.anchorMax = new Vector2(1f, 0.5f);
        crt.pivot = new Vector2(1f, 0.5f);
        crt.sizeDelta = new Vector2(48f, 44f);
        crt.anchoredPosition = new Vector2(-20f, 0f);
        close.onClick.AddListener(() => settingsUI.CloseSettings());

        return h;
    }

    private RectTransform BuildFooter(RectTransform parent)
    {
        RectTransform f = NewRect("Footer", parent);
        f.anchorMin = new Vector2(0f, 0f);
        f.anchorMax = new Vector2(1f, 0f);
        f.pivot = new Vector2(0.5f, 0f);
        f.offsetMin = new Vector2(40f, 20f);
        f.offsetMax = new Vector2(-40f, 90f);
        AddPanelBg(f, colPanel);
        AddBorder(f, colBorder);

        Button reset = MakeFlatButton(f, "ResetButton", "↺  RESET DEFAULTS");
        PlaceFooterButton(reset, 0f);
        Button discard = MakeFlatButton(f, "DiscardButton", "✕  DISCARD");
        PlaceFooterButton(discard, 0.5f);
        Button apply = MakeFlatButton(f, "ApplyButton", "✓  APPLY & CLOSE", true);
        PlaceFooterButton(apply, 1f);

        // Wire to runtime in Build() via reflection-free named refs.
        reset.gameObject.name = "ResetButton";
        discard.gameObject.name = "DiscardButton";
        apply.gameObject.name = "ApplyButton";

        return f;
    }

    private void PlaceFooterButton(Button b, float anchorX)
    {
        RectTransform rt = b.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(anchorX, 0.5f);
        rt.anchorMax = new Vector2(anchorX, 0.5f);
        rt.pivot = new Vector2(anchorX, 0.5f);
        rt.sizeDelta = new Vector2(260f, 54f);
        float offset = anchorX == 0f ? 30f : (anchorX == 1f ? -30f : 0f);
        rt.anchoredPosition = new Vector2(offset, 0f);
    }

    private RectTransform BuildSidebar(RectTransform parent)
    {
        RectTransform s = NewRect("Sidebar", parent);
        s.anchorMin = new Vector2(0f, 0f);
        s.anchorMax = new Vector2(0f, 1f);
        s.pivot = new Vector2(0f, 0.5f);
        s.sizeDelta = new Vector2(240f, 0f);
        s.anchoredPosition = Vector2.zero;
        AddPanelBg(s, colPanel);
        AddBorder(s, colBorder);

        GameObject layoutGO = new GameObject("Buttons", typeof(RectTransform), typeof(VerticalLayoutGroup));
        layoutGO.transform.SetParent(s, false);
        RectTransform layoutRT = layoutGO.GetComponent<RectTransform>();
        StretchFull(layoutRT);
        layoutRT.offsetMin = new Vector2(0f, 20f);
        layoutRT.offsetMax = new Vector2(0f, -20f);

        VerticalLayoutGroup vlg = layoutGO.GetComponent<VerticalLayoutGroup>();
        vlg.padding = new RectOffset(12, 12, 12, 12);
        vlg.spacing = 6;
        vlg.childAlignment = TextAnchor.UpperCenter;
        vlg.childControlWidth = true;
        vlg.childControlHeight = false;
        vlg.childForceExpandWidth = true;
        vlg.childForceExpandHeight = false;

        for (int i = 0; i < Categories.Length; i++)
        {
            var spec = Categories[i];
            GameObject btnGO = new GameObject("Cat_" + spec.id,
                typeof(RectTransform), typeof(LayoutElement), typeof(Image), typeof(Button));
            btnGO.transform.SetParent(layoutRT, false);

            LayoutElement le = btnGO.GetComponent<LayoutElement>();
            le.preferredHeight = 60f;
            le.flexibleHeight = 0f;

            Image bg = btnGO.GetComponent<Image>();
            bg.color = new Color(0f, 0f, 0f, 0.001f); // invisible by default
            bg.raycastTarget = true;

            Button btn = btnGO.GetComponent<Button>();
            ColorBlock cb = btn.colors;
            cb.normalColor = new Color(1f, 1f, 1f, 1f);
            cb.highlightedColor = new Color(colAccent.r, colAccent.g, colAccent.b, 0.10f);
            cb.pressedColor = new Color(colAccent.r, colAccent.g, colAccent.b, 0.25f);
            cb.selectedColor = new Color(colAccent.r, colAccent.g, colAccent.b, 0.10f);
            btn.colors = cb;

            // Gold stripe on the left edge — visible only when active.
            GameObject stripeGO = new GameObject("Stripe",
                typeof(RectTransform), typeof(Image));
            stripeGO.transform.SetParent(btnGO.transform, false);
            RectTransform srt = stripeGO.GetComponent<RectTransform>();
            srt.anchorMin = new Vector2(0f, 0f);
            srt.anchorMax = new Vector2(0f, 1f);
            srt.pivot = new Vector2(0f, 0.5f);
            srt.sizeDelta = new Vector2(4f, 0f);
            srt.anchoredPosition = new Vector2(0f, 0f);
            Image stripeImg = stripeGO.GetComponent<Image>();
            stripeImg.color = colAccent;
            stripeImg.enabled = (i == 0);
            categoryStripes.Add(stripeImg);

            // Icon + label.
            TextMeshProUGUI t = AddText(btn.GetComponent<RectTransform>(), "Text",
                $"  {spec.icon}   {spec.label}", 20, FontStyles.Bold);
            t.alignment = TextAlignmentOptions.MidlineLeft;
            t.color = (i == 0) ? colAccent : colTextDim;
            StretchFull(t.rectTransform);

            int captured = i;
            btn.onClick.AddListener(() => SwitchCategoryRuntime(captured));

            categoryButtons[spec.id] = btn;
        }
        return s;
    }

    private RectTransform BuildCenter(RectTransform parent)
    {
        RectTransform c = NewRect("Center", parent);
        c.anchorMin = new Vector2(0f, 0f);
        c.anchorMax = new Vector2(1f, 1f);
        c.offsetMin = new Vector2(260f, 0f);
        c.offsetMax = new Vector2(-380f, 0f);
        AddPanelBg(c, colPanel);
        AddBorder(c, colBorder);

        GameObject scrollGO = new GameObject("CenterScroll",
            typeof(RectTransform), typeof(ScrollRect), typeof(Image));
        scrollGO.transform.SetParent(c, false);
        RectTransform sRT = scrollGO.GetComponent<RectTransform>();
        StretchFull(sRT);
        sRT.offsetMin = new Vector2(16f, 16f);
        sRT.offsetMax = new Vector2(-16f, -16f);
        Image sBg = scrollGO.GetComponent<Image>();
        sBg.color = new Color(0f, 0f, 0f, 0.001f);
        sBg.raycastTarget = true;

        GameObject viewportGO = new GameObject("Viewport",
            typeof(RectTransform), typeof(Image), typeof(Mask));
        viewportGO.transform.SetParent(sRT, false);
        RectTransform vRT = viewportGO.GetComponent<RectTransform>();
        StretchFull(vRT);
        vRT.offsetMax = new Vector2(-14f, 0f);
        Image vImg = viewportGO.GetComponent<Image>();
        vImg.color = new Color(1f, 1f, 1f, 0.01f);
        Mask m = viewportGO.GetComponent<Mask>();
        m.showMaskGraphic = false;

        GameObject contentGO = new GameObject("Content",
            typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
        contentGO.transform.SetParent(vRT, false);
        RectTransform contentRT = contentGO.GetComponent<RectTransform>();
        contentRT.anchorMin = new Vector2(0f, 1f);
        contentRT.anchorMax = new Vector2(1f, 1f);
        contentRT.pivot = new Vector2(0.5f, 1f);
        contentRT.anchoredPosition = Vector2.zero;
        contentRT.sizeDelta = Vector2.zero;

        VerticalLayoutGroup vlg = contentGO.GetComponent<VerticalLayoutGroup>();
        vlg.padding = new RectOffset(16, 16, 16, 16);
        vlg.spacing = 4;
        vlg.childAlignment = TextAnchor.UpperCenter;
        vlg.childControlWidth = true;
        vlg.childControlHeight = false;
        vlg.childForceExpandWidth = true;

        ContentSizeFitter csf = contentGO.GetComponent<ContentSizeFitter>();
        csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        // Scrollbar.
        GameObject sbGO = BuildScrollbar(sRT, out RectTransform sbHandle);
        ScrollRect sr = scrollGO.GetComponent<ScrollRect>();
        sr.viewport = vRT;
        sr.content = contentRT;
        sr.horizontal = false;
        sr.vertical = true;
        sr.verticalScrollbar = sbGO.GetComponent<Scrollbar>();
        sr.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHideAndExpandViewport;
        sr.scrollSensitivity = 35f;

        centerScrollContent = contentRT;
        return c;
    }

    private GameObject BuildScrollbar(RectTransform parent, out RectTransform handle)
    {
        GameObject barGO = new GameObject("Scrollbar",
            typeof(RectTransform), typeof(Image), typeof(Scrollbar));
        barGO.transform.SetParent(parent, false);
        RectTransform bRT = barGO.GetComponent<RectTransform>();
        bRT.anchorMin = new Vector2(1f, 0f);
        bRT.anchorMax = new Vector2(1f, 1f);
        bRT.pivot = new Vector2(1f, 0.5f);
        bRT.sizeDelta = new Vector2(10f, 0f);
        bRT.anchoredPosition = Vector2.zero;
        Image bImg = barGO.GetComponent<Image>();
        bImg.color = colTrack;

        GameObject slidingGO = new GameObject("Sliding Area", typeof(RectTransform));
        slidingGO.transform.SetParent(bRT, false);
        RectTransform slidingRT = slidingGO.GetComponent<RectTransform>();
        StretchFull(slidingRT);
        slidingRT.offsetMin = new Vector2(1f, 1f);
        slidingRT.offsetMax = new Vector2(-1f, -1f);

        GameObject hGO = new GameObject("Handle",
            typeof(RectTransform), typeof(Image));
        hGO.transform.SetParent(slidingRT, false);
        RectTransform hRT = hGO.GetComponent<RectTransform>();
        StretchFull(hRT);
        Image hImg = hGO.GetComponent<Image>();
        hImg.color = colAccent;

        Scrollbar sb = barGO.GetComponent<Scrollbar>();
        sb.direction = Scrollbar.Direction.BottomToTop;
        sb.handleRect = hRT;
        sb.targetGraphic = hImg;

        handle = hRT;
        return barGO;
    }

    private RectTransform BuildRightRail(RectTransform parent)
    {
        RectTransform r = NewRect("RightRail", parent);
        r.anchorMin = new Vector2(1f, 0f);
        r.anchorMax = new Vector2(1f, 1f);
        r.pivot = new Vector2(1f, 0.5f);
        r.sizeDelta = new Vector2(360f, 0f);
        r.anchoredPosition = Vector2.zero;
        AddPanelBg(r, colPanel);
        AddBorder(r, colBorder);

        // Preview placeholder (top half).
        RectTransform preview = NewRect("Preview", r);
        preview.anchorMin = new Vector2(0f, 0.5f);
        preview.anchorMax = new Vector2(1f, 1f);
        preview.offsetMin = new Vector2(16f, 8f);
        preview.offsetMax = new Vector2(-16f, -16f);
        AddPanelBg(preview, colTrack);
        AddBorder(preview, colBorder);

        TextMeshProUGUI pTitle = AddText(preview, "PreviewTitle", "PREVIEW", 18, FontStyles.Bold);
        pTitle.alignment = TextAlignmentOptions.Top;
        pTitle.color = colAccent;
        RectTransform pTRT = pTitle.rectTransform;
        pTRT.anchorMin = new Vector2(0f, 1f);
        pTRT.anchorMax = new Vector2(1f, 1f);
        pTRT.pivot = new Vector2(0.5f, 1f);
        pTRT.sizeDelta = new Vector2(0f, 30f);
        pTRT.anchoredPosition = new Vector2(0f, -10f);

        TextMeshProUGUI pHint = AddText(preview, "PreviewBody",
            "<Hover a setting on the left to see a live preview here.>",
            14, FontStyles.Italic);
        pHint.color = colTextDim;
        pHint.alignment = TextAlignmentOptions.Center;
        StretchFull(pHint.rectTransform);

        // Description box (bottom half).
        RectTransform desc = NewRect("Description", r);
        desc.anchorMin = new Vector2(0f, 0f);
        desc.anchorMax = new Vector2(1f, 0.5f);
        desc.offsetMin = new Vector2(16f, 16f);
        desc.offsetMax = new Vector2(-16f, -8f);
        AddPanelBg(desc, colTrack);
        AddBorder(desc, colBorder);
        descriptionPanel = desc.gameObject;

        TextMeshProUGUI dTitle = AddText(desc, "DescTitle", "DESCRIPTION", 18, FontStyles.Bold);
        dTitle.alignment = TextAlignmentOptions.Top;
        dTitle.color = colAccent;
        RectTransform dTRT = dTitle.rectTransform;
        dTRT.anchorMin = new Vector2(0f, 1f);
        dTRT.anchorMax = new Vector2(1f, 1f);
        dTRT.pivot = new Vector2(0.5f, 1f);
        dTRT.sizeDelta = new Vector2(0f, 30f);
        dTRT.anchoredPosition = new Vector2(0f, -10f);

        descriptionText = AddText(desc, "DescBody",
            "Mouse over any option to read what it does.",
            15, FontStyles.Normal);
        descriptionText.color = colText;
        descriptionText.alignment = TextAlignmentOptions.TopLeft;
        descriptionText.enableWordWrapping = true;
        RectTransform dBRT = descriptionText.rectTransform;
        dBRT.anchorMin = new Vector2(0f, 0f);
        dBRT.anchorMax = new Vector2(1f, 1f);
        dBRT.offsetMin = new Vector2(16f, 16f);
        dBRT.offsetMax = new Vector2(-16f, -48f);

        return r;
    }

    private void BuildCategoryContents()
    {
        foreach (var spec in Categories)
        {
            GameObject catGO = new GameObject(spec.id + "Content",
                typeof(RectTransform), typeof(VerticalLayoutGroup));
            catGO.transform.SetParent(centerScrollContent, false);

            RectTransform rt = catGO.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot = new Vector2(0.5f, 1f);

            VerticalLayoutGroup vlg = catGO.GetComponent<VerticalLayoutGroup>();
            vlg.spacing = 6;
            vlg.padding = new RectOffset(0, 0, 0, 12);
            vlg.childAlignment = TextAnchor.UpperCenter;
            vlg.childControlWidth = true;
            vlg.childControlHeight = false;
            vlg.childForceExpandWidth = true;

            catGO.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            categoryRoots[spec.id] = rt;
            catGO.SetActive(spec.id == Categories[0].id);
        }
    }

    private void PrepopulateDropdownOptions()
    {
        var s = settingsUI;
        // Editor-time options; SettingsUI also (re)populates resolution
        // / refresh / monitor at runtime where actual monitor data lives.
        FillDropdown(s.qualityDropdown,        new[] { "Low", "Medium", "High", "Ultra" });
        FillDropdown(s.antiAliasingDropdown,   new[] { "Off", "FXAA", "SMAA", "TAA" });
        FillDropdown(s.textureQualityDropdown, new[] { "Low", "Medium", "High", "Ultra" });
        FillDropdown(s.shadowQualityDropdown,  new[] { "Off", "Hard", "Soft Low", "Soft High" });
        FillDropdown(s.difficultyDropdown,     new[] { "Easy", "Normal", "Hard", "Hardcore" });
        FillDropdown(s.subtitleSizeDropdown,   new[] { "Small", "Medium", "Large" });
        FillDropdown(s.languageDropdown,       new[] { "English", "Українська" });
        FillDropdown(s.voiceLanguageDropdown,  new[] { "English", "Українська" });
        FillDropdown(s.windowModeDropdown,     new[] { "Fullscreen", "Borderless", "Windowed" });
        FillDropdown(s.fpsCapDropdown,         new[] { "Unlimited", "30", "60", "90", "120", "144", "240" });
    }

    private void FillDropdown(TMP_Dropdown d, string[] opts)
    {
        if (d == null) return;
        d.ClearOptions();
        d.AddOptions(new List<string>(opts));
    }

    private void PopulateAllSections()
    {
        var s = settingsUI;

        // ===== GENERAL =====
        AddSectionHeader("General", "HUD");
        AddRow("General", "Show FPS", s.showFPSToggle, null,
            "Toggle the on-screen frames-per-second counter.");
        AddRow("General", "Limit FPS", s.limitFPSToggle, null,
            "Quick on/off cap. Use the FPS Cap dropdown in Video for exact values.");
        AddSectionHeader("General", "SAVE");
        AddRow("General", "Auto-Save", s.autoSaveToggle, null,
            "Periodically save progress without prompting.");

        // ===== GAMEPLAY =====
        AddSectionHeader("Gameplay", "DIFFICULTY");
        AddDropdown("Gameplay", "Difficulty", s.difficultyDropdown,
            "Combat scaling. Easy / Normal / Hard / Hardcore. Hardcore disables checkpoints.");
        AddSectionHeader("Gameplay", "FEEDBACK");
        AddRow("Gameplay", "Damage Popups", s.damagePopupsToggle, null,
            "Show floating damage numbers above enemies you hit.");
        AddRow("Gameplay", "Screen Shake", s.screenShakeToggle, null,
            "Camera shake on impacts and explosions. Disable if it causes discomfort.");
        AddRow("Gameplay", "Hit-Stop FX", s.hitStopToggle, null,
            "Brief freeze on heavy hits for impact. Disable for smoother combat.");
        AddRow("Gameplay", "Low HP Vignette", s.lowHpVignetteToggle, null,
            "Red edge tint when health is critical. Disable to reduce visual noise.");
        AddSectionHeader("Gameplay", "TUTORIAL");
        AddRow("Gameplay", "Tutorial Hints", s.tutorialHintsToggle, null,
            "Show contextual hint popups when new mechanics appear.");
        AddRow("Gameplay", "Hold to Sprint", s.holdToggleSprintToggle, null,
            "OFF = hold Shift to sprint. ON = press once to toggle sprint state.");

        // ===== AUDIO =====
        AddSectionHeader("Audio", "MIX");
        AddSlider("Audio", "Master",      s.masterSlider,  s.masterInput,  "Overall game volume — affects every channel.");
        AddSlider("Audio", "Music",       s.musicSlider,   s.musicInput,   "Background music and ambient score.");
        AddSlider("Audio", "Sound FX",    s.sfxSlider,     s.sfxInput,     "Combat and world impact sounds.");
        AddSlider("Audio", "Voice",       s.voiceSlider,   s.voiceInput,   "Dialogue and narration.");
        AddSlider("Audio", "UI",          s.uiSlider,      s.uiInput,      "Menu, button, and HUD sounds.");
        AddSlider("Audio", "Ambient",     s.ambientSlider, s.ambientInput, "World ambience — wind, fire, water.");
        AddSectionHeader("Audio", "BEHAVIOUR");
        AddRow("Audio", "Mute When Unfocused", s.muteWhenUnfocusedToggle, null,
            "Silence the game when the window loses focus (Alt-Tab).");

        // ===== VIDEO =====
        AddSectionHeader("Video", "DISPLAY");
        AddDropdown("Video", "Resolution",     s.resolutionDropdown,
            "Render and output resolution. Lower for performance, higher for sharpness.");
        AddDropdown("Video", "Window Mode",    s.windowModeDropdown,
            "Fullscreen (exclusive), Borderless (snappy alt-tab), or Windowed.");
        AddDropdown("Video", "Refresh Rate",   s.refreshRateDropdown,
            "Monitor refresh rate target — only meaningful in Fullscreen.");
        AddDropdown("Video", "Monitor",        s.monitorDropdown,
            "Which display the game uses on multi-monitor setups.");
        AddDropdown("Video", "FPS Cap",        s.fpsCapDropdown,
            "Hard frame-rate cap. Unlimited = no cap. Lower caps reduce heat / battery.");
        AddRow("Video", "V-Sync", s.vsyncToggle, null,
            "Synchronise rendering to monitor refresh. Eliminates tearing but adds input lag.");
        AddSectionHeader("Video", "CAMERA");
        AddSlider("Video", "Field of View",    s.fovSlider,        s.fovInput,        "Wider FOV shows more peripheral vision but distorts edges. Default 75.");
        AddSlider("Video", "Brightness",       s.brightnessSlider, s.brightnessInput, "Scene brightness multiplier. 100 = default.");
        AddSlider("Video", "Gamma",            s.gammaSlider,      s.gammaInput,      "Mid-tone luminance. Lift shadows or crush highlights.");

        // ===== GRAPHICS =====
        AddSectionHeader("Graphics", "QUALITY PRESET");
        AddDropdown("Graphics", "Preset",          s.qualityDropdown,
            "Master quality preset. Overrides individual tiers below.");
        AddSlider("Graphics", "Render Scale (%)",  s.renderScaleSlider, s.renderScaleInput,
            "Internal resolution as % of output. Lower for more FPS, higher for super-sampling.");
        AddSectionHeader("Graphics", "TIERS");
        AddDropdown("Graphics", "Anti-Aliasing",    s.antiAliasingDropdown,
            "Smooths jagged edges. FXAA cheap, SMAA balanced, TAA highest quality.");
        AddDropdown("Graphics", "Texture Quality",  s.textureQualityDropdown,
            "Texture mip level. Low saves VRAM, Ultra gives sharpest surfaces.");
        AddDropdown("Graphics", "Shadow Quality",   s.shadowQualityDropdown,
            "Off / Hard / Soft Low / Soft High. Biggest single-toggle performance lever.");
        AddSlider("Graphics", "Shadow Distance",    s.shadowDistanceSlider, null,
            "How far from the camera shadows are rendered, in metres.");
        AddSectionHeader("Graphics", "POST-FX");
        AddRow("Graphics", "Post Processing", s.postFXToggle, null,
            "Master post-processing toggle (bloom + grading + DOF).");
        AddRow("Graphics", "Dynamic Shadows", s.dynamicShadowsToggle, null,
            "Realtime shadows on minor lights (torches, candles).");
        AddRow("Graphics", "Motion Blur",     s.motionBlurToggle, null,
            "Per-object motion blur on fast movement. Off for clarity.");
        AddRow("Graphics", "Depth of Field",  s.depthOfFieldToggle, null,
            "Cinematic focus blur during cutscenes and aim.");
        AddRow("Graphics", "Bloom",           s.bloomToggle, null,
            "Soft glow around bright pixels.");
        AddRow("Graphics", "Ambient Occlusion", s.ambientOcclusionToggle, null,
            "Soft shading in crevices and corners — adds depth, costs frames.");
        AddRow("Graphics", "Volumetric Lighting", s.volumetricsToggle, null,
            "God rays through fog. Expensive on lower-end GPUs.");

        // ===== CONTROLS =====
        AddSectionHeader("Controls", "MOUSE & KEYBOARD");
        AddSlider("Controls", "Mouse Sensitivity", s.sensitivitySlider, s.sensitivityInput,
            "Camera sensitivity multiplier. 100 = default. Raise for snappy aim.");
        AddRow("Controls", "Invert Y Axis", s.invertYToggle, null,
            "Flip vertical look direction. Down on the mouse looks up.");
        AddSectionHeader("Controls", "GAMEPAD");
        AddRow("Controls", "Controller Vibration", s.controllerVibrationToggle, null,
            "Rumble on impacts when using a gamepad.");
        AddSlider("Controls", "Aim Assist", s.aimAssistSlider, s.aimAssistInput,
            "Sticky-aim strength on gamepad. 0 = off, 100 = strong magnet.");
        AddSectionHeader("Controls", "BINDINGS");
        TextMeshProUGUI placeholder = AddText(categoryRoots["Controls"], "ControlsPlaceholder",
            "Custom key bindings coming soon.", 16, FontStyles.Italic);
        placeholder.color = colTextDim;
        placeholder.alignment = TextAlignmentOptions.Center;
        placeholder.gameObject.AddComponent<LayoutElement>().preferredHeight = 60f;

        // ===== ACCESSIBILITY =====
        AddSectionHeader("Accessibility", "SUBTITLES");
        AddRow("Accessibility", "Subtitles", s.subtitlesToggle, null,
            "Show on-screen text for dialogue and narration.");
        AddDropdown("Accessibility", "Subtitle Size", s.subtitleSizeDropdown,
            "How large subtitle text is rendered.");
        AddRow("Accessibility", "Subtitle Background", s.subtitleBackgroundToggle, null,
            "Render subtitles on a translucent dark plate for readability.");
        AddSectionHeader("Accessibility", "VISUAL AIDS");
        AddRow("Accessibility", "Colorblind Mode", s.colorblindToggle, null,
            "Recolor highlights for color-vision deficiencies.");
        AddRow("Accessibility", "High Contrast UI", s.highContrastToggle, null,
            "Boost contrast on HUD elements for visibility.");
        AddRow("Accessibility", "Reduce Motion", s.reduceMotionToggle, null,
            "Disable camera bob, screen sway, and aggressive transitions.");
        AddRow("Accessibility", "Photosensitivity Safe Mode", s.photosensitivityToggle, null,
            "Replace strobing flashes with steady glows.");
        AddSectionHeader("Accessibility", "UI");
        AddSlider("Accessibility", "UI Scale (%)", s.uiScaleSlider, s.uiScaleInput,
            "Scale the HUD up to 150% for readability.");

        // ===== LANGUAGE =====
        AddSectionHeader("Language", "TEXT");
        AddDropdown("Language", "Game Language",  s.languageDropdown,
            "Game text language. Voice remains in its original language unless changed below.");
        AddDropdown("Language", "Voice Language", s.voiceLanguageDropdown,
            "Spoken dialogue language for cutscenes and barks.");
    }

    // ----- Row construction -----

    private void AddSectionHeader(string categoryId, string text)
    {
        RectTransform parent = categoryRoots[categoryId];

        GameObject hGO = new GameObject("Header_" + text,
            typeof(RectTransform), typeof(LayoutElement), typeof(VerticalLayoutGroup));
        hGO.transform.SetParent(parent, false);
        LayoutElement le = hGO.GetComponent<LayoutElement>();
        le.preferredHeight = 64f;

        VerticalLayoutGroup vlg = hGO.GetComponent<VerticalLayoutGroup>();
        vlg.padding = new RectOffset(0, 0, 14, 6);
        vlg.spacing = 4;
        vlg.childAlignment = TextAnchor.LowerLeft;
        vlg.childControlWidth = true;
        vlg.childControlHeight = false;
        vlg.childForceExpandWidth = true;
        vlg.childForceExpandHeight = false;

        TextMeshProUGUI label = AddText(hGO.GetComponent<RectTransform>(), "Label",
            text.ToUpperInvariant(), 22, FontStyles.Bold);
        label.color = colAccent;
        label.alignment = TextAlignmentOptions.MidlineLeft;
        LayoutElement labLE = label.gameObject.AddComponent<LayoutElement>();
        labLE.preferredHeight = 30f;

        GameObject ruleGO = new GameObject("Rule",
            typeof(RectTransform), typeof(LayoutElement), typeof(Image));
        ruleGO.transform.SetParent(hGO.transform, false);
        LayoutElement rule_le = ruleGO.GetComponent<LayoutElement>();
        rule_le.preferredHeight = 2f;
        Image r = ruleGO.GetComponent<Image>();
        r.color = colBorder;
    }

    private GameObject AddRow(string categoryId, string label, Toggle togSrc, TMP_InputField inputSrc, string description)
    {
        if (togSrc == null && inputSrc == null) return null;
        RectTransform parent = categoryRoots[categoryId];
        GameObject row = BuildRowShell(parent, label, description);

        if (togSrc != null)
        {
            Undo.SetTransformParent(togSrc.transform, row.transform, "Move Toggle");
            RectTransform tt = togSrc.GetComponent<RectTransform>();
            tt.anchorMin = new Vector2(1f, 0.5f);
            tt.anchorMax = new Vector2(1f, 0.5f);
            tt.pivot = new Vector2(1f, 0.5f);
            tt.anchoredPosition = new Vector2(-12f, 0f);
        }
        return row;
    }

    private GameObject AddSlider(string categoryId, string label, Slider sliderSrc, TMP_InputField inputSrc, string description)
    {
        if (sliderSrc == null) return null;
        RectTransform parent = categoryRoots[categoryId];
        GameObject row = BuildRowShell(parent, label, description);

        Undo.SetTransformParent(sliderSrc.transform, row.transform, "Move Slider");
        RectTransform st = sliderSrc.GetComponent<RectTransform>();
        st.anchorMin = new Vector2(1f, 0.5f);
        st.anchorMax = new Vector2(1f, 0.5f);
        st.pivot = new Vector2(1f, 0.5f);
        st.sizeDelta = new Vector2(260f, 20f);
        st.anchoredPosition = new Vector2(inputSrc != null ? -90f : -12f, 0f);
        RecolorSlider(sliderSrc);

        if (inputSrc != null)
        {
            Undo.SetTransformParent(inputSrc.transform, row.transform, "Move Input");
            RectTransform it = inputSrc.GetComponent<RectTransform>();
            it.anchorMin = new Vector2(1f, 0.5f);
            it.anchorMax = new Vector2(1f, 0.5f);
            it.pivot = new Vector2(1f, 0.5f);
            it.sizeDelta = new Vector2(70f, 36f);
            it.anchoredPosition = new Vector2(-12f, 0f);
            inputSrc.contentType = TMP_InputField.ContentType.IntegerNumber;
            inputSrc.characterLimit = 4;
            if (inputSrc.textComponent != null) inputSrc.textComponent.alignment = TextAlignmentOptions.Center;
        }
        return row;
    }

    private GameObject AddDropdown(string categoryId, string label, TMP_Dropdown ddSrc, string description)
    {
        if (ddSrc == null) return null;
        RectTransform parent = categoryRoots[categoryId];
        GameObject row = BuildRowShell(parent, label, description);

        Undo.SetTransformParent(ddSrc.transform, row.transform, "Move Dropdown");
        RectTransform dt = ddSrc.GetComponent<RectTransform>();
        dt.anchorMin = new Vector2(1f, 0.5f);
        dt.anchorMax = new Vector2(1f, 0.5f);
        dt.pivot = new Vector2(1f, 0.5f);
        dt.sizeDelta = new Vector2(220f, 36f);
        dt.anchoredPosition = new Vector2(-12f, 0f);
        return row;
    }

    // Stylise the cloned slider so every category's controls share the
    // mockup palette regardless of the template's original colours.
    private void RecolorSlider(Slider s)
    {
        if (s == null) return;
        Transform bg = s.transform.Find("Background");
        if (bg != null) { Image bgi = bg.GetComponent<Image>(); if (bgi != null) bgi.color = colTrack; }
        if (s.fillRect != null)
        {
            Image fi = s.fillRect.GetComponent<Image>();
            if (fi != null) fi.color = colSlider;
        }
        if (s.handleRect != null)
        {
            Image hi = s.handleRect.GetComponent<Image>();
            if (hi != null) hi.color = colSlider;
        }
    }

    private GameObject BuildRowShell(RectTransform parent, string label, string description)
    {
        GameObject row = new GameObject("Row_" + label,
            typeof(RectTransform), typeof(LayoutElement), typeof(Image));
        row.transform.SetParent(parent, false);
        LayoutElement le = row.GetComponent<LayoutElement>();
        le.preferredHeight = 56f;
        le.flexibleHeight = 0f;

        Image bg = row.GetComponent<Image>();
        bg.color = new Color(1f, 1f, 1f, 0.025f);
        bg.raycastTarget = true;

        TextMeshProUGUI t = AddText(row.GetComponent<RectTransform>(), "Label", label, 18, FontStyles.Normal);
        t.alignment = TextAlignmentOptions.MidlineLeft;
        t.color = colText;
        RectTransform trt = t.rectTransform;
        trt.anchorMin = new Vector2(0f, 0f);
        trt.anchorMax = new Vector2(0.5f, 1f);
        trt.offsetMin = new Vector2(16f, 0f);
        trt.offsetMax = new Vector2(0f, 0f);

        SettingsAAARowHover hover = row.AddComponent<SettingsAAARowHover>();
        hover.description = description;
        hoverRows.Add(new SettingsAAARuntime.HoverRow { row = row, description = description });
        return row;
    }

    // ----- Utility -----

    private void SwitchCategoryRuntime(int idx)
    {
        // Editor-mode preview switch; runtime handles switch at play.
        int i = 0;
        foreach (var spec in Categories)
        {
            if (categoryRoots.TryGetValue(spec.id, out var rt) && rt != null)
                rt.gameObject.SetActive(i == idx);
            if (i < categoryStripes.Count && categoryStripes[i] != null)
                categoryStripes[i].enabled = (i == idx);
            i++;
        }
    }

    private RectTransform NewRect(string name, Transform parent)
    {
        GameObject go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        return go.GetComponent<RectTransform>();
    }

    private void AddPanelBg(RectTransform rt, Color color)
    {
        Image img = rt.gameObject.GetComponent<Image>();
        if (img == null) img = rt.gameObject.AddComponent<Image>();
        img.color = color;
        if (panelBgSprite != null) { img.sprite = panelBgSprite; img.type = Image.Type.Sliced; }
        img.raycastTarget = true;
    }

    private void AddBorder(RectTransform rt, Color color)
    {
        GameObject borderGO = new GameObject("Border",
            typeof(RectTransform), typeof(Image), typeof(Outline));
        borderGO.transform.SetParent(rt, false);
        RectTransform brt = borderGO.GetComponent<RectTransform>();
        StretchFull(brt);
        Image bImg = borderGO.GetComponent<Image>();
        bImg.color = new Color(0f, 0f, 0f, 0f);
        bImg.raycastTarget = false;
        Outline o = borderGO.GetComponent<Outline>();
        o.effectColor = color;
        o.effectDistance = new Vector2(1f, -1f);
    }

    private TextMeshProUGUI AddText(RectTransform parent, string name, string content, int size, FontStyles style)
    {
        GameObject go = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
        go.transform.SetParent(parent, false);
        TextMeshProUGUI t = go.GetComponent<TextMeshProUGUI>();
        t.text = content;
        t.fontSize = size;
        t.fontStyle = style;
        t.color = colText;
        if (font != null) t.font = font;
        t.enableWordWrapping = false;
        return t;
    }

    private Button MakeFlatButton(RectTransform parent, string name, string label, bool primary = false)
    {
        GameObject go = new GameObject(name,
            typeof(RectTransform), typeof(Image), typeof(Button));
        go.transform.SetParent(parent, false);
        Image bg = go.GetComponent<Image>();
        // Primary action (APPLY): warmer gold tint. Secondary actions: neutral dark.
        bg.color = primary
            ? new Color(colAccent.r, colAccent.g, colAccent.b, 0.18f)
            : new Color(1f, 1f, 1f, 0.05f);
        if (buttonSprite != null) { bg.sprite = buttonSprite; bg.type = Image.Type.Sliced; }
        Button btn = go.GetComponent<Button>();
        ColorBlock cb = btn.colors;
        cb.normalColor = new Color(1f, 1f, 1f, 1f);
        cb.highlightedColor = primary
            ? new Color(colAccent.r, colAccent.g, colAccent.b, 0.35f)
            : new Color(1f, 1f, 1f, 0.12f);
        cb.pressedColor = primary
            ? new Color(colAccent.r, colAccent.g, colAccent.b, 0.55f)
            : new Color(1f, 1f, 1f, 0.22f);
        btn.colors = cb;
        AddBorder(go.GetComponent<RectTransform>(), primary ? colBorder : new Color(1f, 1f, 1f, 0.18f));
        TextMeshProUGUI t = AddText(go.GetComponent<RectTransform>(), "Label", label, 18, FontStyles.Bold);
        t.alignment = TextAlignmentOptions.Center;
        t.color = primary ? colAccent : colText;
        StretchFull(t.rectTransform);
        return btn;
    }

    private static void StretchFull(RectTransform rt)
    {
        rt.anchorMin = new Vector2(0f, 0f);
        rt.anchorMax = new Vector2(1f, 1f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
    }
}
#endif
