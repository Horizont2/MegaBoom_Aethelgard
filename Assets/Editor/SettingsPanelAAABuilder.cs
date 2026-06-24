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

    private Sprite panelBgSprite;
    private Sprite buttonSprite;
    private Sprite handleSprite;

    // ----- Theme -----
    // Tuned to match the painted-card mockup: near-black panels over a
    // dimmed scene, gold reserved for title + active sidebar accent +
    // APPLY button, soft cyan reserved for slider fill / toggle on.
    private Color colBg = new Color(0.02f, 0.025f, 0.03f, 0.65f);     // overlay darkens scene
    private Color colPanel = new Color(0.07f, 0.075f, 0.09f, 0.96f);   // card surface
    private Color colAccent = new Color(1f, 0.82f, 0.24f, 1f);
    private Color colSlider = new Color(0.36f, 0.75f, 0.87f, 1f);
    private Color colText = new Color(0.95f, 0.94f, 0.90f, 1f);
    private Color colTextDim = new Color(0.65f, 0.63f, 0.58f, 1f);
    private Color colBorder = new Color(0f, 0f, 0f, 0.6f);             // dark painted edge
    private Color colTrack = new Color(0.11f, 0.12f, 0.14f, 1f);

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
            "Builds a full-screen AAA settings overlay on the chosen Canvas.\n" +
            "Every slider / toggle / dropdown is generated procedurally and\n" +
            "auto-bound into matching SettingsUI public fields — no scene\n" +
            "templates needed. Leave sprites empty for default flat UI fill.",
            MessageType.Info);

        targetCanvas = (Canvas)EditorGUILayout.ObjectField("Target Canvas", targetCanvas, typeof(Canvas), true);
        settingsUI = (SettingsUI)EditorGUILayout.ObjectField("SettingsUI", settingsUI, typeof(SettingsUI), true);
        font = (TMP_FontAsset)EditorGUILayout.ObjectField("Font", font, typeof(TMP_FontAsset), false);

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

        // Tear down any previous build (and the legacy "SettingsPanel"
        // GameObject from the old miniwindow flow) so we don't end up
        // with two overlapping panels in the scene.
        for (int i = targetCanvas.transform.childCount - 1; i >= 0; i--)
        {
            Transform c = targetCanvas.transform.GetChild(i);
            if (c == null) continue;
            if (c.name == "SettingsPanelAAA" || c.name == "SettingsPanel")
                Undo.DestroyObjectImmediate(c.gameObject);
        }

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

        // Middle band — three card columns float over the scene with
        // generous breathing room on every side. Sidebar 360, right rail
        // 460, center fills the remaining width. Matches the painted
        // mockup proportions (cards take ~70% of screen height, with
        // ~140px transparent strips top + bottom for title and footer
        // buttons that sit directly over the scene art).
        RectTransform mid = NewRect("Mid", rootRT);
        mid.anchorMin = new Vector2(0f, 0f);
        mid.anchorMax = new Vector2(1f, 1f);
        mid.offsetMin = new Vector2(150f, 200f);
        mid.offsetMax = new Vector2(-150f, -200f);

        RectTransform sidebar = BuildSidebar(mid);
        RectTransform center = BuildCenter(mid);
        RectTransform rightRail = BuildRightRail(mid);

        // Per-category content panels stacked inside the scroll content.
        // Only the selected category is enabled at a time.
        BuildCategoryContents();

        // Build every row procedurally and bind each control back into
        // the matching SettingsUI public field so the existing
        // OpenSettings / CloseSettings logic still picks them up.
        PopulateAllSections();

        // Attach runtime controller that drives:
        //  - category switching
        //  - description on row hover
        //  - apply / discard / reset
        // Theme swapper — empty by default, user drags Figma sprites in.
        rootGO.AddComponent<SettingsAAATheme>();

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
        // Transparent header band — title + ✕ float directly over the
        // scene background. Matches the painted-mockup reference where
        // no card hides the game art behind the title bar.
        RectTransform h = NewRect("Header", parent);
        h.anchorMin = new Vector2(0f, 1f);
        h.anchorMax = new Vector2(1f, 1f);
        h.pivot = new Vector2(0.5f, 1f);
        h.offsetMin = new Vector2(0f, -140f);
        h.offsetMax = new Vector2(0f, 0f);

        TextMeshProUGUI title = AddText(h, "TitleText", "S E T T I N G S", 54, FontStyles.Bold);
        title.alignment = TextAlignmentOptions.Center;
        title.color = colAccent;
        title.characterSpacing = 16f;
        StretchFull(title.rectTransform);

        Button close = MakeFlatButton(h, "CloseButton", "✕");
        RectTransform crt = close.GetComponent<RectTransform>();
        crt.anchorMin = new Vector2(1f, 0.5f);
        crt.anchorMax = new Vector2(1f, 0.5f);
        crt.pivot = new Vector2(1f, 0.5f);
        crt.sizeDelta = new Vector2(56f, 56f);
        crt.anchoredPosition = new Vector2(-60f, 0f);
        // Make CLOSE almost transparent — just the glyph reads, no chrome.
        Image cImg = close.GetComponent<Image>();
        if (cImg != null) cImg.color = new Color(0f, 0f, 0f, 0f);
        close.onClick.AddListener(() => settingsUI.CloseSettings());

        return h;
    }

    private RectTransform BuildFooter(RectTransform parent)
    {
        // Transparent footer band — buttons sit directly over the scene.
        RectTransform f = NewRect("Footer", parent);
        f.anchorMin = new Vector2(0f, 0f);
        f.anchorMax = new Vector2(1f, 0f);
        f.pivot = new Vector2(0.5f, 0f);
        f.offsetMin = new Vector2(0f, 0f);
        f.offsetMax = new Vector2(0f, 110f);

        Button reset = MakeFlatButton(f, "ResetButton", "↺  RESET DEFAULTS");
        PlaceFooterButton(reset, 0f);
        Button discard = MakeFlatButton(f, "DiscardButton", "✕  DISCARD");
        PlaceFooterButton(discard, 0.7f);
        Button apply = MakeFlatButton(f, "ApplyButton", "✓  APPLY & CLOSE", true);
        PlaceFooterButton(apply, 1f);

        // Strip chrome off the secondary buttons so they read as text-only
        // like the reference; only APPLY keeps its painted plate.
        StripBg(reset); StripBg(discard);

        reset.gameObject.name = "ResetButton";
        discard.gameObject.name = "DiscardButton";
        apply.gameObject.name = "ApplyButton";

        return f;
    }

    private static void StripBg(Button b)
    {
        Image bg = b.GetComponent<Image>();
        if (bg != null) bg.color = new Color(0f, 0f, 0f, 0f);
        Transform border = b.transform.Find("Border");
        if (border != null) Object.DestroyImmediate(border.gameObject);
    }

    private void PlaceFooterButton(Button b, float anchorX)
    {
        RectTransform rt = b.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(anchorX, 0.5f);
        rt.anchorMax = new Vector2(anchorX, 0.5f);
        rt.pivot = new Vector2(anchorX, 0.5f);
        rt.sizeDelta = new Vector2(280f, 56f);
        float offset = anchorX == 0f ? 80f : (anchorX == 1f ? -80f : 0f);
        rt.anchoredPosition = new Vector2(offset, 0f);
    }

    private RectTransform BuildSidebar(RectTransform parent)
    {
        RectTransform s = NewRect("Sidebar", parent);
        s.anchorMin = new Vector2(0f, 0f);
        s.anchorMax = new Vector2(0f, 1f);
        s.pivot = new Vector2(0f, 0.5f);
        s.sizeDelta = new Vector2(290f, 0f);
        s.anchoredPosition = Vector2.zero;
        AddPanelBg(s, colPanel);
        AddBorder(s, colBorder);

        GameObject layoutGO = new GameObject("Buttons", typeof(RectTransform), typeof(VerticalLayoutGroup));
        layoutGO.transform.SetParent(s, false);
        RectTransform layoutRT = layoutGO.GetComponent<RectTransform>();
        StretchFull(layoutRT);
        layoutRT.offsetMin = new Vector2(0f, 24f);
        layoutRT.offsetMax = new Vector2(0f, -24f);

        VerticalLayoutGroup vlg = layoutGO.GetComponent<VerticalLayoutGroup>();
        vlg.padding = new RectOffset(20, 20, 8, 8);
        vlg.spacing = 8;
        vlg.childAlignment = TextAnchor.UpperCenter;
        vlg.childControlWidth = true;
        vlg.childControlHeight = false;
        vlg.childForceExpandWidth = true;
        vlg.childForceExpandHeight = false;

        for (int i = 0; i < Categories.Length; i++)
        {
            var spec = Categories[i];
            // Two-layer card: fill Image (this object) + Stroke child Image.
            // SettingsAAACategoryButton drives both layers' colours from the
            // theme's state palette so we don't need ColorBlock tinting.
            GameObject btnGO = new GameObject("Cat_" + spec.id,
                typeof(RectTransform), typeof(LayoutElement), typeof(Image), typeof(Button));
            btnGO.transform.SetParent(layoutRT, false);

            LayoutElement le = btnGO.GetComponent<LayoutElement>();
            le.preferredHeight = 64f;
            le.flexibleHeight = 0f;

            Image bg = btnGO.GetComponent<Image>();
            // First category renders selected, others render default.
            // Colours mirror SettingsAAATheme defaults so the panel looks
            // right immediately without anyone hitting Apply Theme.
            bg.color = (i == 0)
                ? new Color(0.42f, 0.33f, 0.10f, 1f)
                : new Color(0.105f, 0.115f, 0.13f, 1f);
            bg.raycastTarget = true;

            Button btn = btnGO.GetComponent<Button>();
            ColorBlock cb = btn.colors;
            cb.normalColor = new Color(1f, 1f, 1f, 1f);
            cb.highlightedColor = new Color(1f, 1f, 1f, 1f);
            cb.pressedColor = new Color(1f, 1f, 1f, 1f);
            cb.selectedColor = new Color(1f, 1f, 1f, 1f);
            btn.colors = cb;

            // Stroke overlay — sits on top of fill, no raycast.
            GameObject strokeGO = new GameObject("Stroke",
                typeof(RectTransform), typeof(Image));
            strokeGO.transform.SetParent(btnGO.transform, false);
            RectTransform strokeRT = strokeGO.GetComponent<RectTransform>();
            StretchFull(strokeRT);
            Image strokeImg = strokeGO.GetComponent<Image>();
            // No sprite yet → keep stroke transparent so we don't paint
            // a dark plate over the fill. Once a sprite is supplied via
            // SettingsAAATheme, EnsureStrokeChild fills it in.
            strokeImg.color = (i == 0)
                ? new Color(1f, 0.823f, 0.247f, 1f)
                : new Color(0f, 0f, 0f, 0f);
            strokeImg.raycastTarget = false;
            categoryStripes.Add(strokeImg); // kept for backwards-compat with runtime.categoryStripes

            // Icon + label as one TMP block.
            TextMeshProUGUI t = AddText(btn.GetComponent<RectTransform>(), "Text",
                $"  {spec.icon}   {spec.label}", 20, FontStyles.Bold);
            t.alignment = TextAlignmentOptions.MidlineLeft;
            t.color = (i == 0)
                ? new Color(1f, 0.92f, 0.55f, 1f)
                : new Color(0.96f, 0.94f, 0.90f, 1f);
            StretchFull(t.rectTransform);

            // State driver — wires both layers' colours from the theme.
            var stateBtn = btnGO.AddComponent<SettingsAAACategoryButton>();
            stateBtn.fillImg = bg;
            stateBtn.strokeImg = strokeImg;
            stateBtn.labelTMP = t;
            stateBtn.isSelected = (i == 0);

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
        c.offsetMin = new Vector2(420f, 0f);
        c.offsetMax = new Vector2(-510f, 0f);
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
        bRT.sizeDelta = new Vector2(6f, 0f);
        bRT.anchoredPosition = Vector2.zero;
        Image bImg = barGO.GetComponent<Image>();
        bImg.color = new Color(1f, 1f, 1f, 0.04f);

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
        hImg.color = new Color(1f, 1f, 1f, 0.18f);

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
        r.sizeDelta = new Vector2(380f, 0f);
        r.anchoredPosition = Vector2.zero;
        // Right rail is a TRANSPARENT layout container — the two
        // children (PREVIEW + DESCRIPTION) are the real cards each
        // with their own painted plate. Matches the reference where
        // you see two distinct cards instead of one wrapping panel.

        // Preview card (top half) — full painted card.
        RectTransform preview = NewRect("Preview", r);
        preview.anchorMin = new Vector2(0f, 0.5f);
        preview.anchorMax = new Vector2(1f, 1f);
        preview.offsetMin = new Vector2(0f, 16f);
        preview.offsetMax = new Vector2(0f, 0f);
        AddPanelBg(preview, colPanel);
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

        // Description card (bottom half) — separate painted card.
        RectTransform desc = NewRect("Description", r);
        desc.anchorMin = new Vector2(0f, 0f);
        desc.anchorMax = new Vector2(1f, 0.5f);
        desc.offsetMin = new Vector2(0f, 0f);
        desc.offsetMax = new Vector2(0f, -16f);
        AddPanelBg(desc, colPanel);
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

    private void PopulateAllSections()
    {
        string[] qualityOpts    = { "Low", "Medium", "High", "Ultra" };
        string[] aaOpts         = { "Off", "FXAA", "SMAA", "TAA" };
        string[] shadowOpts     = { "Off", "Hard", "Soft Low", "Soft High" };
        string[] difficultyOpts = { "Easy", "Normal", "Hard", "Hardcore" };
        string[] subtitleSize   = { "Small", "Medium", "Large" };
        string[] languageOpts   = { "English", "Українська", "Русский", "Español", "Deutsch", "Français" };
        string[] windowOpts     = { "Fullscreen", "Borderless", "Windowed" };
        string[] resOpts        = { "1920×1080", "2560×1440", "3840×2160" };
        string[] refreshOpts    = { "60 Hz", "120 Hz", "144 Hz", "165 Hz", "240 Hz" };
        string[] monitorOpts    = { "Primary" };
        string[] fpsCapOpts     = { "Unlimited", "30", "60", "90", "120", "144", "240" };

        // ===== GENERAL =====
        AddSectionHeader("General", "HUD");
        AddToggleField("General", "Show FPS", "showFPSToggle", false,
            "Toggle the on-screen frames-per-second counter.");
        AddToggleField("General", "Limit FPS", "limitFPSToggle", true,
            "Quick on/off cap. Use the FPS Cap dropdown in Video for exact values.");
        AddSectionHeader("General", "SAVE");
        AddToggleField("General", "Auto-Save", "autoSaveToggle", true,
            "Periodically save progress without prompting.");

        // ===== GAMEPLAY =====
        AddSectionHeader("Gameplay", "DIFFICULTY");
        AddDropdownField("Gameplay", "Difficulty", "difficultyDropdown", difficultyOpts, 1,
            "Combat scaling. Easy / Normal / Hard / Hardcore. Hardcore disables checkpoints.");
        AddSectionHeader("Gameplay", "FEEDBACK");
        AddToggleField("Gameplay", "Damage Popups", "damagePopupsToggle", true,
            "Show floating damage numbers above enemies you hit.");
        AddToggleField("Gameplay", "Screen Shake", "screenShakeToggle", true,
            "Camera shake on impacts and explosions. Disable if it causes discomfort.");
        AddToggleField("Gameplay", "Hit-Stop FX", "hitStopToggle", true,
            "Brief freeze on heavy hits for impact. Disable for smoother combat.");
        AddToggleField("Gameplay", "Low HP Vignette", "lowHpVignetteToggle", true,
            "Red edge tint when health is critical. Disable to reduce visual noise.");
        AddSectionHeader("Gameplay", "TUTORIAL");
        AddToggleField("Gameplay", "Tutorial Hints", "tutorialHintsToggle", true,
            "Show contextual hint popups when new mechanics appear.");

        // ===== AUDIO =====
        AddSectionHeader("Audio", "MIX");
        AddSliderField("Audio", "Master",  "masterSlider",  "masterInput",  0f, 100f, 100f, "Overall game volume — affects every channel.");
        AddSliderField("Audio", "Music",   "musicSlider",   "musicInput",   0f, 100f, 100f, "Background music and ambient score.");
        AddSliderField("Audio", "Sound FX","sfxSlider",     "sfxInput",     0f, 100f, 100f, "Combat and world impact sounds.");
        AddSliderField("Audio", "Voice",   "voiceSlider",   "voiceInput",   0f, 100f, 100f, "Dialogue and narration.");
        AddSliderField("Audio", "UI",      "uiSlider",      "uiInput",      0f, 100f, 100f, "Menu, button, and HUD sounds.");
        AddSliderField("Audio", "Ambient", "ambientSlider", "ambientInput", 0f, 100f, 100f, "World ambience — wind, fire, water.");
        AddSectionHeader("Audio", "BEHAVIOUR");
        AddToggleField("Audio", "Mute When Unfocused", "muteWhenUnfocusedToggle", true,
            "Silence the game when the window loses focus (Alt-Tab).");

        // ===== VIDEO =====
        AddSectionHeader("Video", "DISPLAY");
        AddDropdownField("Video", "Resolution",   "resolutionDropdown",  resOpts, 0,
            "Render and output resolution. Lower for performance, higher for sharpness.");
        AddDropdownField("Video", "Window Mode",  "windowModeDropdown",  windowOpts, 0,
            "Fullscreen (exclusive), Borderless (snappy alt-tab), or Windowed.");
        AddDropdownField("Video", "Refresh Rate", "refreshRateDropdown", refreshOpts, 0,
            "Monitor refresh rate target — only meaningful in Fullscreen.");
        AddDropdownField("Video", "Monitor",      "monitorDropdown",     monitorOpts, 0,
            "Which display the game uses on multi-monitor setups.");
        AddDropdownField("Video", "FPS Cap",      "fpsCapDropdown",      fpsCapOpts, 2,
            "Hard frame-rate cap. Unlimited = no cap. Lower caps reduce heat / battery.");
        AddToggleField("Video", "V-Sync", "vsyncToggle", false,
            "Synchronise rendering to monitor refresh. Eliminates tearing but adds input lag.");
        AddSectionHeader("Video", "CAMERA");
        AddSliderField("Video", "Field of View", "fovSlider",        "fovInput",        60f,  120f, 75f,  "Wider FOV shows more peripheral vision but distorts edges. Default 75.");
        AddSliderField("Video", "Brightness",    "brightnessSlider", "brightnessInput", 0.5f, 1.5f, 1f,   "Scene brightness multiplier. 1 = default.");
        AddSliderField("Video", "Gamma",         "gammaSlider",      "gammaInput",      0.5f, 1.5f, 1f,   "Mid-tone luminance. Lift shadows or crush highlights.");

        // ===== GRAPHICS =====
        AddSectionHeader("Graphics", "QUALITY PRESET");
        AddDropdownField("Graphics", "Preset",         "qualityDropdown", qualityOpts, 2,
            "Master quality preset. Overrides individual tiers below.");
        AddSliderField("Graphics", "Render Scale (%)", "renderScaleSlider", "renderScaleInput",
            50f, 200f, 100f,
            "Internal resolution as % of output. Lower for more FPS, higher for super-sampling.");
        AddSectionHeader("Graphics", "TIERS");
        AddDropdownField("Graphics", "Anti-Aliasing",   "antiAliasingDropdown", aaOpts, 1,
            "Smooths jagged edges. FXAA cheap, SMAA balanced, TAA highest quality.");
        AddDropdownField("Graphics", "Texture Quality", "textureQualityDropdown", qualityOpts, 2,
            "Texture mip level. Low saves VRAM, Ultra gives sharpest surfaces.");
        AddDropdownField("Graphics", "Shadow Quality",  "shadowQualityDropdown", shadowOpts, 2,
            "Off / Hard / Soft Low / Soft High. Biggest single-toggle performance lever.");
        AddSliderField("Graphics", "Shadow Distance",   "shadowDistanceSlider", null,
            10f, 200f, 50f,
            "How far from the camera shadows are rendered, in metres.");
        AddSectionHeader("Graphics", "POST-FX");
        AddToggleField("Graphics", "Post Processing",    "postFXToggle", true,
            "Master post-processing toggle (bloom + grading + DOF).");
        AddToggleField("Graphics", "Dynamic Shadows",    "dynamicShadowsToggle", true,
            "Realtime shadows on minor lights (torches, candles).");
        AddToggleField("Graphics", "Motion Blur",        "motionBlurToggle", false,
            "Per-object motion blur on fast movement. Off for clarity.");
        AddToggleField("Graphics", "Depth of Field",     "depthOfFieldToggle", false,
            "Cinematic focus blur during cutscenes and aim.");
        AddToggleField("Graphics", "Bloom",              "bloomToggle", true,
            "Soft glow around bright pixels.");
        AddToggleField("Graphics", "Ambient Occlusion",  "ambientOcclusionToggle", true,
            "Soft shading in crevices and corners — adds depth, costs frames.");
        AddToggleField("Graphics", "Volumetric Lighting","volumetricsToggle", false,
            "God rays through fog. Expensive on lower-end GPUs.");

        // ===== CONTROLS =====
        AddSectionHeader("Controls", "MOUSE & KEYBOARD");
        AddSliderField("Controls", "Mouse Sensitivity", "sensitivitySlider", "sensitivityInput",
            0.1f, 5f, 1f,
            "Camera sensitivity multiplier. 1 = default. Raise for snappy aim.");
        AddToggleField("Controls", "Invert Y Axis", "invertYToggle", false,
            "Flip vertical look direction. Down on the mouse looks up.");
        AddSectionHeader("Controls", "GAMEPAD");
        AddToggleField("Controls", "Controller Vibration", "controllerVibrationToggle", true,
            "Rumble on impacts when using a gamepad.");
        AddSliderField("Controls", "Aim Assist", "aimAssistSlider", "aimAssistInput",
            0f, 1f, 0.4f,
            "Sticky-aim strength on gamepad. 0 = off, 1 = strong magnet.");
        AddSectionHeader("Controls", "BINDINGS");
        TextMeshProUGUI placeholder = AddText(categoryRoots["Controls"], "ControlsPlaceholder",
            "Custom key bindings coming soon.", 16, FontStyles.Italic);
        placeholder.color = colTextDim;
        placeholder.alignment = TextAlignmentOptions.Center;
        placeholder.gameObject.AddComponent<LayoutElement>().preferredHeight = 60f;

        // ===== ACCESSIBILITY =====
        AddSectionHeader("Accessibility", "SUBTITLES");
        AddToggleField("Accessibility", "Subtitles", "subtitlesToggle", true,
            "Show on-screen text for dialogue and narration.");
        AddDropdownField("Accessibility", "Subtitle Size", "subtitleSizeDropdown", subtitleSize, 1,
            "How large subtitle text is rendered.");
        AddToggleField("Accessibility", "Subtitle Background", "subtitleBackgroundToggle", true,
            "Render subtitles on a translucent dark plate for readability.");
        AddSectionHeader("Accessibility", "VISUAL AIDS");
        AddToggleField("Accessibility", "Colorblind Mode", "colorblindToggle", false,
            "Recolor highlights for color-vision deficiencies.");
        AddToggleField("Accessibility", "High Contrast UI", "highContrastToggle", false,
            "Boost contrast on HUD elements for visibility.");
        AddToggleField("Accessibility", "Reduce Motion", "reduceMotionToggle", false,
            "Disable camera bob, screen sway, and aggressive transitions.");
        AddToggleField("Accessibility", "Photosensitivity Safe Mode", "photosensitivityToggle", false,
            "Replace strobing flashes with steady glows.");
        AddSectionHeader("Accessibility", "UI");
        AddSliderField("Accessibility", "UI Scale", "uiScaleSlider", "uiScaleInput",
            0.75f, 1.5f, 1f,
            "Scale the HUD up to 150% for readability.");

        // ===== LANGUAGE =====
        AddSectionHeader("Language", "TEXT");
        AddDropdownField("Language", "Game Language",  "languageDropdown",      languageOpts, 0,
            "Game text language. Voice remains in its original language unless changed below.");
        AddDropdownField("Language", "Voice Language", "voiceLanguageDropdown", languageOpts, 0,
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
        rule_le.preferredHeight = 1f;
        Image r = ruleGO.GetComponent<Image>();
        r.color = new Color(1f, 1f, 1f, 0.08f);
    }

    // ===== Procedural control creators =====
    // We build sliders / toggles / dropdowns from scratch (instead of
    // reparenting whatever the user assigned to SettingsUI) so the panel
    // looks the same regardless of how the scene was set up. The created
    // control is also written back into the matching SettingsUI field via
    // reflection, so existing OpenSettings/CloseSettings code still works.

    private Slider CreateSlider(Transform parent, float min, float max, float value)
    {
        GameObject sGO = new GameObject("Slider",
            typeof(RectTransform), typeof(Slider));
        sGO.transform.SetParent(parent, false);
        RectTransform sRT = sGO.GetComponent<RectTransform>();
        sRT.sizeDelta = new Vector2(260f, 20f);
        sRT.localScale = Vector3.one;

        // Background track.
        GameObject bgGO = new GameObject("Background",
            typeof(RectTransform), typeof(Image));
        bgGO.transform.SetParent(sRT, false);
        RectTransform bgRT = bgGO.GetComponent<RectTransform>();
        bgRT.anchorMin = new Vector2(0f, 0.5f);
        bgRT.anchorMax = new Vector2(1f, 0.5f);
        bgRT.sizeDelta = new Vector2(0f, 6f);
        bgRT.anchoredPosition = Vector2.zero;
        Image bgImg = bgGO.GetComponent<Image>();
        bgImg.color = colTrack;

        // Fill area + fill.
        GameObject faGO = new GameObject("Fill Area", typeof(RectTransform));
        faGO.transform.SetParent(sRT, false);
        RectTransform faRT = faGO.GetComponent<RectTransform>();
        faRT.anchorMin = new Vector2(0f, 0.5f);
        faRT.anchorMax = new Vector2(1f, 0.5f);
        faRT.offsetMin = new Vector2(8f, -3f);
        faRT.offsetMax = new Vector2(-8f, 3f);

        GameObject fGO = new GameObject("Fill",
            typeof(RectTransform), typeof(Image));
        fGO.transform.SetParent(faRT, false);
        RectTransform fRT = fGO.GetComponent<RectTransform>();
        fRT.anchorMin = new Vector2(0f, 0f);
        fRT.anchorMax = new Vector2(1f, 1f);
        fRT.offsetMin = Vector2.zero;
        fRT.offsetMax = Vector2.zero;
        Image fImg = fGO.GetComponent<Image>();
        fImg.color = colSlider;

        // Handle area + handle.
        GameObject haGO = new GameObject("Handle Slide Area", typeof(RectTransform));
        haGO.transform.SetParent(sRT, false);
        RectTransform haRT = haGO.GetComponent<RectTransform>();
        haRT.anchorMin = new Vector2(0f, 0f);
        haRT.anchorMax = new Vector2(1f, 1f);
        haRT.offsetMin = new Vector2(10f, 0f);
        haRT.offsetMax = new Vector2(-10f, 0f);

        GameObject hGO = new GameObject("Handle",
            typeof(RectTransform), typeof(Image));
        hGO.transform.SetParent(haRT, false);
        RectTransform hRT = hGO.GetComponent<RectTransform>();
        hRT.sizeDelta = new Vector2(18f, 18f);
        Image hImg = hGO.GetComponent<Image>();
        hImg.color = new Color(colSlider.r * 1.3f, colSlider.g * 1.3f, colSlider.b * 1.3f, 1f);
        if (handleSprite != null) hImg.sprite = handleSprite;

        Slider sld = sGO.GetComponent<Slider>();
        sld.fillRect = fRT;
        sld.handleRect = hRT;
        sld.targetGraphic = hImg;
        sld.direction = Slider.Direction.LeftToRight;
        sld.minValue = min;
        sld.maxValue = max;
        sld.value = value;
        return sld;
    }

    private Toggle CreateToggle(Transform parent, bool value)
    {
        GameObject tGO = new GameObject("Toggle",
            typeof(RectTransform), typeof(Toggle));
        tGO.transform.SetParent(parent, false);
        RectTransform tRT = tGO.GetComponent<RectTransform>();
        tRT.sizeDelta = new Vector2(56f, 28f);
        tRT.localScale = Vector3.one;

        // Pill background.
        GameObject pGO = new GameObject("Background",
            typeof(RectTransform), typeof(Image));
        pGO.transform.SetParent(tRT, false);
        RectTransform pRT = pGO.GetComponent<RectTransform>();
        StretchFull(pRT);
        Image pImg = pGO.GetComponent<Image>();
        pImg.color = colTrack;

        // Knob (sliding circle).
        GameObject kGO = new GameObject("Checkmark",
            typeof(RectTransform), typeof(Image));
        kGO.transform.SetParent(pRT, false);
        RectTransform kRT = kGO.GetComponent<RectTransform>();
        kRT.sizeDelta = new Vector2(22f, 22f);
        kRT.anchorMin = new Vector2(value ? 1f : 0f, 0.5f);
        kRT.anchorMax = new Vector2(value ? 1f : 0f, 0.5f);
        kRT.pivot = new Vector2(value ? 1f : 0f, 0.5f);
        kRT.anchoredPosition = new Vector2(value ? -3f : 3f, 0f);
        Image kImg = kGO.GetComponent<Image>();
        kImg.color = value ? colSlider : new Color(0.6f, 0.6f, 0.65f, 1f);

        Toggle tog = tGO.GetComponent<Toggle>();
        tog.targetGraphic = pImg;
        // NOTE: deliberately not setting tog.graphic — Unity hides the
        // assigned graphic when isOn = false, which made the knob vanish
        // instead of sliding. SettingsAAAToggleKnob animates it instead.
        tog.isOn = value;
        ColorBlock cb = tog.colors;
        cb.normalColor = Color.white;
        cb.highlightedColor = new Color(1f, 1f, 1f, 0.9f);
        tog.colors = cb;

        var anim = tGO.AddComponent<SettingsAAAToggleKnob>();
        anim.toggle = tog;
        anim.knob = kImg;
        anim.onColor = colSlider;
        anim.offColor = new Color(0.55f, 0.58f, 0.62f, 1f);
        return tog;
    }

    private TMP_Dropdown CreateDropdown(Transform parent, string[] options, int selected)
    {
        GameObject dGO = new GameObject("Dropdown",
            typeof(RectTransform), typeof(Image), typeof(TMP_Dropdown));
        dGO.transform.SetParent(parent, false);
        RectTransform dRT = dGO.GetComponent<RectTransform>();
        dRT.sizeDelta = new Vector2(220f, 36f);
        dRT.localScale = Vector3.one;
        Image dImg = dGO.GetComponent<Image>();
        dImg.color = colTrack;

        // Label.
        GameObject lGO = new GameObject("Label",
            typeof(RectTransform), typeof(TextMeshProUGUI));
        lGO.transform.SetParent(dRT, false);
        RectTransform lRT = lGO.GetComponent<RectTransform>();
        StretchFull(lRT);
        lRT.offsetMin = new Vector2(12f, 2f);
        lRT.offsetMax = new Vector2(-30f, -2f);
        TextMeshProUGUI lTxt = lGO.GetComponent<TextMeshProUGUI>();
        lTxt.text = options.Length > 0 ? options[Mathf.Clamp(selected, 0, options.Length - 1)] : "";
        lTxt.color = colText;
        lTxt.fontSize = 16;
        lTxt.alignment = TextAlignmentOptions.MidlineLeft;
        if (font != null) lTxt.font = font;

        // Caret arrow.
        GameObject cGO = new GameObject("Arrow",
            typeof(RectTransform), typeof(TextMeshProUGUI));
        cGO.transform.SetParent(dRT, false);
        RectTransform cRT = cGO.GetComponent<RectTransform>();
        cRT.anchorMin = new Vector2(1f, 0.5f);
        cRT.anchorMax = new Vector2(1f, 0.5f);
        cRT.pivot = new Vector2(1f, 0.5f);
        cRT.sizeDelta = new Vector2(24f, 24f);
        cRT.anchoredPosition = new Vector2(-8f, 0f);
        TextMeshProUGUI cTxt = cGO.GetComponent<TextMeshProUGUI>();
        cTxt.text = "▼";
        cTxt.color = colAccent;
        cTxt.fontSize = 14;
        cTxt.alignment = TextAlignmentOptions.Center;
        if (font != null) cTxt.font = font;

        // Template (collapsed).
        GameObject tplGO = new GameObject("Template",
            typeof(RectTransform), typeof(Image), typeof(ScrollRect));
        tplGO.transform.SetParent(dRT, false);
        RectTransform tplRT = tplGO.GetComponent<RectTransform>();
        tplRT.anchorMin = new Vector2(0f, 0f);
        tplRT.anchorMax = new Vector2(1f, 0f);
        tplRT.pivot = new Vector2(0.5f, 1f);
        tplRT.sizeDelta = new Vector2(0f, 150f);
        tplRT.anchoredPosition = new Vector2(0f, 2f);
        Image tplImg = tplGO.GetComponent<Image>();
        tplImg.color = new Color(colTrack.r, colTrack.g, colTrack.b, 0.98f);
        ScrollRect tplSR = tplGO.GetComponent<ScrollRect>();

        GameObject vpGO = new GameObject("Viewport",
            typeof(RectTransform), typeof(Image), typeof(Mask));
        vpGO.transform.SetParent(tplRT, false);
        RectTransform vpRT = vpGO.GetComponent<RectTransform>();
        StretchFull(vpRT);
        Image vpImg = vpGO.GetComponent<Image>(); vpImg.color = new Color(1f, 1f, 1f, 0.01f);
        Mask vpMask = vpGO.GetComponent<Mask>(); vpMask.showMaskGraphic = false;

        GameObject contentGO = new GameObject("Content", typeof(RectTransform));
        contentGO.transform.SetParent(vpRT, false);
        RectTransform contentRT = contentGO.GetComponent<RectTransform>();
        contentRT.anchorMin = new Vector2(0f, 1f);
        contentRT.anchorMax = new Vector2(1f, 1f);
        contentRT.pivot = new Vector2(0.5f, 1f);
        contentRT.sizeDelta = new Vector2(0f, 28f);

        GameObject itemGO = new GameObject("Item",
            typeof(RectTransform), typeof(Toggle));
        itemGO.transform.SetParent(contentRT, false);
        RectTransform itemRT = itemGO.GetComponent<RectTransform>();
        itemRT.anchorMin = new Vector2(0f, 0.5f);
        itemRT.anchorMax = new Vector2(1f, 0.5f);
        itemRT.sizeDelta = new Vector2(0f, 24f);

        GameObject itemBgGO = new GameObject("Item Background",
            typeof(RectTransform), typeof(Image));
        itemBgGO.transform.SetParent(itemRT, false);
        RectTransform itemBgRT = itemBgGO.GetComponent<RectTransform>();
        StretchFull(itemBgRT);
        Image itemBgImg = itemBgGO.GetComponent<Image>();
        itemBgImg.color = new Color(colSlider.r, colSlider.g, colSlider.b, 0.4f);

        GameObject itemLblGO = new GameObject("Item Label",
            typeof(RectTransform), typeof(TextMeshProUGUI));
        itemLblGO.transform.SetParent(itemRT, false);
        RectTransform itemLblRT = itemLblGO.GetComponent<RectTransform>();
        StretchFull(itemLblRT);
        itemLblRT.offsetMin = new Vector2(12f, 0f);
        itemLblRT.offsetMax = new Vector2(-12f, 0f);
        TextMeshProUGUI itemLbl = itemLblGO.GetComponent<TextMeshProUGUI>();
        itemLbl.text = "Option";
        itemLbl.color = colText;
        itemLbl.fontSize = 15;
        itemLbl.alignment = TextAlignmentOptions.MidlineLeft;
        if (font != null) itemLbl.font = font;

        Toggle itemTog = itemGO.GetComponent<Toggle>();
        itemTog.targetGraphic = itemBgImg;

        tplSR.viewport = vpRT;
        tplSR.content = contentRT;
        tplSR.horizontal = false;
        tplSR.vertical = true;

        TMP_Dropdown dd = dGO.GetComponent<TMP_Dropdown>();
        dd.template = tplRT;
        dd.captionText = lTxt;
        dd.itemText = itemLbl;
        dd.targetGraphic = dImg;
        dd.ClearOptions();
        dd.AddOptions(new List<string>(options));
        dd.value = Mathf.Clamp(selected, 0, Mathf.Max(0, options.Length - 1));
        dd.RefreshShownValue();
        tplGO.SetActive(false);
        return dd;
    }

    private TMP_InputField CreateInputField(Transform parent, string text)
    {
        GameObject iGO = new GameObject("Input",
            typeof(RectTransform), typeof(Image), typeof(TMP_InputField));
        iGO.transform.SetParent(parent, false);
        RectTransform iRT = iGO.GetComponent<RectTransform>();
        iRT.sizeDelta = new Vector2(70f, 32f);
        iRT.localScale = Vector3.one;
        Image bg = iGO.GetComponent<Image>();
        bg.color = colTrack;

        GameObject taGO = new GameObject("Text Area",
            typeof(RectTransform), typeof(RectMask2D));
        taGO.transform.SetParent(iRT, false);
        RectTransform taRT = taGO.GetComponent<RectTransform>();
        StretchFull(taRT);
        taRT.offsetMin = new Vector2(8f, 2f);
        taRT.offsetMax = new Vector2(-8f, -2f);

        GameObject txtGO = new GameObject("Text",
            typeof(RectTransform), typeof(TextMeshProUGUI));
        txtGO.transform.SetParent(taRT, false);
        RectTransform txtRT = txtGO.GetComponent<RectTransform>();
        StretchFull(txtRT);
        TextMeshProUGUI txt = txtGO.GetComponent<TextMeshProUGUI>();
        txt.text = text;
        txt.color = colText;
        txt.fontSize = 16;
        txt.alignment = TextAlignmentOptions.Center;
        if (font != null) txt.font = font;

        TMP_InputField inf = iGO.GetComponent<TMP_InputField>();
        inf.textViewport = taRT;
        inf.textComponent = txt;
        inf.text = text;
        inf.contentType = TMP_InputField.ContentType.IntegerNumber;
        inf.characterLimit = 4;
        return inf;
    }

    // Assigns the freshly built control back to the SettingsUI public
    // field named `fieldName`, so OpenSettings/CloseSettings can still
    // read/write it via the original references.
    private void Bind(string fieldName, UnityEngine.Object value)
    {
        if (string.IsNullOrEmpty(fieldName) || value == null) return;
        var so = new SerializedObject(settingsUI);
        var prop = so.FindProperty(fieldName);
        if (prop != null) { prop.objectReferenceValue = value; so.ApplyModifiedProperties(); return; }
        var fi = settingsUI.GetType().GetField(fieldName,
            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
        if (fi != null) fi.SetValue(settingsUI, value);
    }

    // ===== Field-name based row helpers (procedural) =====
    private void AddSliderField(string categoryId, string label, string sliderField,
                                string inputField, float min, float max, float defaultValue,
                                string description)
    {
        RectTransform parent = categoryRoots[categoryId];
        GameObject row = BuildRowShell(parent, label, description);

        Slider sld = CreateSlider(row.transform, min, max, defaultValue);
        RectTransform sRT = sld.GetComponent<RectTransform>();
        sRT.anchorMin = new Vector2(1f, 0.5f);
        sRT.anchorMax = new Vector2(1f, 0.5f);
        sRT.pivot = new Vector2(1f, 0.5f);
        sRT.anchoredPosition = new Vector2(inputField != null ? -90f : -12f, 0f);
        Bind(sliderField, sld);

        if (inputField != null)
        {
            TMP_InputField inf = CreateInputField(row.transform, Mathf.RoundToInt(defaultValue).ToString());
            RectTransform iRT = inf.GetComponent<RectTransform>();
            iRT.anchorMin = new Vector2(1f, 0.5f);
            iRT.anchorMax = new Vector2(1f, 0.5f);
            iRT.pivot = new Vector2(1f, 0.5f);
            iRT.anchoredPosition = new Vector2(-12f, 0f);
            Bind(inputField, inf);
        }
    }

    private void AddToggleField(string categoryId, string label, string toggleField,
                                bool defaultValue, string description)
    {
        RectTransform parent = categoryRoots[categoryId];
        GameObject row = BuildRowShell(parent, label, description);

        Toggle tog = CreateToggle(row.transform, defaultValue);
        RectTransform tRT = tog.GetComponent<RectTransform>();
        tRT.anchorMin = new Vector2(1f, 0.5f);
        tRT.anchorMax = new Vector2(1f, 0.5f);
        tRT.pivot = new Vector2(1f, 0.5f);
        tRT.anchoredPosition = new Vector2(-12f, 0f);
        Bind(toggleField, tog);
    }

    private void AddDropdownField(string categoryId, string label, string ddField,
                                  string[] options, int defaultSelected, string description)
    {
        RectTransform parent = categoryRoots[categoryId];
        GameObject row = BuildRowShell(parent, label, description);

        TMP_Dropdown dd = CreateDropdown(row.transform, options, defaultSelected);
        RectTransform dRT = dd.GetComponent<RectTransform>();
        dRT.anchorMin = new Vector2(1f, 0.5f);
        dRT.anchorMax = new Vector2(1f, 0.5f);
        dRT.pivot = new Vector2(1f, 0.5f);
        dRT.anchoredPosition = new Vector2(-12f, 0f);
        Bind(ddField, dd);
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
        // Painted edge effect — 1px outline, subdued so it reads as a
        // soft edge rather than a hard gold rule. Real edge styling
        // comes from the Panel BG sprite once supplied via Theme.
        GameObject borderGO = new GameObject("Border",
            typeof(RectTransform), typeof(Image), typeof(Outline));
        borderGO.transform.SetParent(rt, false);
        RectTransform brt = borderGO.GetComponent<RectTransform>();
        StretchFull(brt);
        Image bImg = borderGO.GetComponent<Image>();
        bImg.color = new Color(0f, 0f, 0f, 0f);
        bImg.raycastTarget = false;
        Outline o = borderGO.GetComponent<Outline>();
        o.effectColor = new Color(color.r, color.g, color.b, color.a * 0.5f);
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
