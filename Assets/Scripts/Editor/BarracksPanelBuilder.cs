#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

// Editor tool that constructs the entire BarracksUpgradePanel prefab plus
// its two row prefabs (Hire and Upgrade Units) with every SerializeField
// wired. Placeholder rectangles / plain colours stand in for sprites and
// portraits — swap those for your own art and the panel is done.
//
// Menu: Tools → Aethelgard → Build Barracks Panel
public static class BarracksPanelBuilder
{
    // -------- Paths --------
    private const string TargetFolder = "Assets/Prefabs/UI/Barracks";
    private const string PanelPrefabPath = TargetFolder + "/BarracksUpgradePanel.prefab";
    private const string HireRowPath     = TargetFolder + "/HireRow.prefab";
    private const string UpgradeRowPath  = TargetFolder + "/UpgradeUnitRow.prefab";

    // -------- Palette (from the Figma mockup) --------
    private static readonly Color PANEL_BG      = ParseHex("#1E1B18", 0.94f);
    private static readonly Color PANEL_BORDER  = ParseHex("#5B4E3C");
    private static readonly Color TEXT_CREAM    = ParseHex("#F0E4CB");
    private static readonly Color TEXT_MUTED    = ParseHex("#8A8478");
    private static readonly Color ACCENT_BRASS  = ParseHex("#C7912B");
    private static readonly Color ACCENT_AMBER  = ParseHex("#D9A24A");
    private static readonly Color BTN_TEXT_DARK = ParseHex("#3A2510");
    private static readonly Color CHIP_DARK     = ParseHex("#2A2622");
    private static readonly Color DIAMOND_BLUE  = ParseHex("#A9D4EB");
    private static readonly Color DISABLED_BG   = ParseHex("#444038");
    private static readonly Color DISABLED_TXT  = ParseHex("#7A7367");
    private static readonly Color TAB_ACTIVE_BG = new Color(0f, 0f, 0f, 0.28f);
    private static readonly Color TRANSPARENT   = new Color(0f, 0f, 0f, 0f);

    // -------- Layout constants --------
    private const float PANEL_W = 1080f;
    private const float PANEL_H = 620f;
    private const float PAD_X = 60f;
    private const float PAD_TOP = 44f;
    private const float PAD_BOT = 48f;

    private const float HEADER_H = 100f;
    private const float TAB_H = 60f;

    // -------- Menu entry --------

    [MenuItem("Tools/Aethelgard/Build Barracks Panel")]
    public static void BuildAll()
    {
        EnsureFolder(TargetFolder);

        // Row prefabs first — the main panel references them.
        var hireRow = BuildHireRowPrefab();
        var upgradeRow = BuildUpgradeRowPrefab();

        // Load them back from disk so the panel gets asset references.
        var hireRowPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(HireRowPath);
        var upgradeRowPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(UpgradeRowPath);

        // Main panel
        var panelInstance = BuildMainPanelInstance(hireRowPrefab, upgradeRowPrefab);
        SaveInstanceAsPrefab(panelInstance, PanelPrefabPath);
        Object.DestroyImmediate(panelInstance);

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        // Cleanup temp scene instances of row prefabs (they were left in scene by SaveAsPrefab).
        if (hireRow != null) Object.DestroyImmediate(hireRow);
        if (upgradeRow != null) Object.DestroyImmediate(upgradeRow);

        Debug.Log("[BarracksPanelBuilder] Built:\n  " + PanelPrefabPath + "\n  " + HireRowPath + "\n  " + UpgradeRowPath);
        var asset = AssetDatabase.LoadAssetAtPath<GameObject>(PanelPrefabPath);
        if (asset != null) EditorGUIUtility.PingObject(asset);
    }

    // ==========================================================
    //                MAIN PANEL
    // ==========================================================

    private static GameObject BuildMainPanelInstance(GameObject hireRowPrefab, GameObject upgradeRowPrefab)
    {
        // Root — RectTransform 1920×1080, CanvasGroup, BarracksUpgradePanel script.
        var root = new GameObject("BarracksUpgradePanel",
            typeof(RectTransform), typeof(CanvasGroup), typeof(BarracksUpgradePanel));
        var rootRT = (RectTransform)root.transform;
        SetSize(rootRT, 1920f, 1080f);
        rootRT.anchorMin = new Vector2(0.5f, 0.5f);
        rootRT.anchorMax = new Vector2(0.5f, 0.5f);
        rootRT.pivot = new Vector2(0.5f, 0.5f);
        rootRT.anchoredPosition = Vector2.zero;

        var canvasGroup = root.GetComponent<CanvasGroup>();
        var panelScript = root.GetComponent<BarracksUpgradePanel>();

        // Diamond chip (top-right of the 1920×1080 area).
        var diamondChip = BuildDiamondChip(rootRT);

        // Close hint (bottom-right).
        var closeHint = BuildCloseHint(rootRT);

        // Panel — centred dark card.
        var panelGO = CreateRect("Panel", rootRT, PANEL_W, PANEL_H);
        var panelImg = panelGO.gameObject.AddComponent<Image>();
        panelImg.color = PANEL_BG;
        panelImg.raycastTarget = true;
        AddOutline(panelGO.gameObject, PANEL_BORDER, 1f);
        Center(panelGO);

        // Header (title + swords + level pips) — anchored to top of panel.
        var headerRT = CreateRect("Header", panelGO, PANEL_W - PAD_X * 2, HEADER_H);
        AnchorTopStretch(headerRT, PAD_TOP, PAD_X);

        var headerSpacer = CreateRect("HeaderSpacer", headerRT, 180f, HEADER_H);
        AnchorLeft(headerSpacer, 0);

        var titleBlock = CreateRect("TitleBlock", headerRT, 400f, HEADER_H);
        CenterHorizontal(titleBlock, 0);
        var titleTMP = AddText(titleBlock, "BARRACKS", 60, TEXT_CREAM, TextAlignmentOptions.Center);
        AnchorTopStretch((RectTransform)titleTMP.transform, 0, 0);
        titleTMP.enableAutoSizing = false;
        titleTMP.fontStyle = FontStyles.Bold;
        titleTMP.characterSpacing = 12f;

        var swords = CreateRect("SwordsGlyph", titleBlock, 34f, 34f);
        AnchorBottom(swords, 6f);
        var swordsImg = swords.gameObject.AddComponent<Image>();
        swordsImg.color = ACCENT_BRASS;
        // Simple stand-in — user swaps sprite later.

        // Level pips — 5 helm slots on the right of the header.
        var pipsRow = CreateRect("LevelPips", headerRT, 180f, 32f);
        AnchorRightMiddle(pipsRow);
        var pipsHLG = pipsRow.gameObject.AddComponent<HorizontalLayoutGroup>();
        pipsHLG.spacing = 8f;
        pipsHLG.childControlWidth = false;
        pipsHLG.childControlHeight = false;
        pipsHLG.childForceExpandWidth = false;
        pipsHLG.childForceExpandHeight = false;
        pipsHLG.childAlignment = TextAnchor.MiddleRight;
        pipsHLG.reverseArrangement = false;
        var barracksPipImages = new Image[5];
        for (int i = 0; i < 5; i++)
        {
            var pip = CreateRect($"HelmPip_{i}", pipsRow, 24f, 24f);
            var img = pip.gameObject.AddComponent<Image>();
            img.color = i < 3 ? ACCENT_BRASS : TEXT_MUTED;
            barracksPipImages[i] = img;
        }

        // ---- Tab strip -----------------------------------------
        var tabStrip = CreateRect("TabStrip", panelGO, PANEL_W - PAD_X * 2, TAB_H);
        AnchorTopStretch(tabStrip, PAD_TOP + HEADER_H + 20f, PAD_X);
        // Divider lines above / below.
        AddHairline(tabStrip, top: true, color: new Color(ACCENT_BRASS.r, ACCENT_BRASS.g, ACCENT_BRASS.b, 0.14f));
        AddHairline(tabStrip, top: false, color: new Color(ACCENT_BRASS.r, ACCENT_BRASS.g, ACCENT_BRASS.b, 0.14f));
        var tabHLG = tabStrip.gameObject.AddComponent<HorizontalLayoutGroup>();
        tabHLG.spacing = 12f;
        tabHLG.childControlWidth = true;
        tabHLG.childControlHeight = true;
        tabHLG.childForceExpandWidth = true;
        tabHLG.childForceExpandHeight = true;

        var (hireBtn, hireLabel, hireUnderline, hireActiveBg) = BuildTab(tabStrip, "TabHire", "HIRE", isActive: true);
        var (upBtn,  upLabel,  upUnderline,  upActiveBg)     = BuildTab(tabStrip, "TabUpgradeUnits", "UPGRADE UNITS", isActive: false);
        var (bbBtn,  bbLabel,  bbUnderline,  bbActiveBg)     = BuildTab(tabStrip, "TabUpgradeBarracks", "UPGRADE BARRACKS", isActive: false);

        // ---- Content area (each container overlaps, only one active) ----
        float contentTop = PAD_TOP + HEADER_H + 20f + TAB_H + 12f;
        float contentH = PANEL_H - contentTop - PAD_BOT;
        var contentArea = CreateRect("ContentArea", panelGO, PANEL_W - PAD_X * 2, contentH);
        AnchorTopStretch(contentArea, contentTop, PAD_X);

        var (hireContainer, hireRowParent) = BuildHireContainer(contentArea);
        var (upContainer,   upRowParent)   = BuildUpgradeContainer(contentArea);
        upContainer.SetActive(false);

        var bbBuild = BuildUpgradeBarracksContainer(contentArea);
        bbBuild.container.SetActive(false);

        // ---- Wire every field on BarracksUpgradePanel -----------
        panelScript.rootObject       = root;
        panelScript.canvasGroup      = canvasGroup;
        panelScript.closeButton      = closeHint.GetComponent<Button>();

        panelScript.titleText        = titleTMP;
        panelScript.diamondsText     = diamondChip.numberTMP;
        panelScript.barracksLevelPips = barracksPipImages;
        // pipHelmFilledSprite / pipHelmEmptySprite left null — user assigns.

        panelScript.tabHireButton            = hireBtn;
        panelScript.tabUpgradeUnitsButton    = upBtn;
        panelScript.tabUpgradeBarracksButton = bbBtn;
        panelScript.hireContainer            = hireContainer;
        panelScript.upgradeUnitsContainer    = upContainer;
        panelScript.upgradeBarracksContainer = bbBuild.container;
        panelScript.tabHireUnderline            = hireUnderline;
        panelScript.tabUpgradeUnitsUnderline    = upUnderline;
        panelScript.tabUpgradeBarracksUnderline = bbUnderline;
        panelScript.tabHireActiveBg            = hireActiveBg;
        panelScript.tabUpgradeUnitsActiveBg    = upActiveBg;
        panelScript.tabUpgradeBarracksActiveBg = bbActiveBg;
        panelScript.tabHireLabel            = hireLabel;
        panelScript.tabUpgradeUnitsLabel    = upLabel;
        panelScript.tabUpgradeBarracksLabel = bbLabel;

        panelScript.hireRowParent   = hireRowParent;
        panelScript.hireRowPrefab   = hireRowPrefab;
        panelScript.upgradeRowParent = upRowParent;
        panelScript.upgradeRowPrefab = upgradeRowPrefab;

        panelScript.barracksDioramaImage        = bbBuild.diorama;
        panelScript.barracksCurrentLevelText    = bbBuild.currentLevel;
        panelScript.barracksSummaryText         = bbBuild.summary;
        panelScript.barracksNextLevelText       = bbBuild.nextLevel;
        panelScript.barracksPerksText           = bbBuild.perks;
        panelScript.barracksCostWoodText        = bbBuild.costWood;
        panelScript.barracksCostStoneText       = bbBuild.costStone;
        panelScript.barracksCostFoodText        = bbBuild.costFood;
        panelScript.barracksUpgradeButton       = bbBuild.upgradeBtn;
        panelScript.barracksUpgradeButtonText   = bbBuild.upgradeBtnLabel;
        panelScript.barracksBuildTimeText       = bbBuild.buildTime;

        return root;
    }

    // ==========================================================
    //         DIAMOND CHIP / CLOSE HINT
    // ==========================================================

    private struct DiamondChipBuild { public GameObject go; public TextMeshProUGUI numberTMP; }
    private static DiamondChipBuild BuildDiamondChip(RectTransform parent)
    {
        var chip = CreateRect("DiamondChip", parent, 130f, 50f);
        chip.anchorMin = new Vector2(1f, 1f);
        chip.anchorMax = new Vector2(1f, 1f);
        chip.pivot = new Vector2(1f, 1f);
        chip.anchoredPosition = new Vector2(-80f, -60f);

        var bg = chip.gameObject.AddComponent<Image>();
        bg.color = CHIP_DARK;
        AddOutline(chip.gameObject, PANEL_BORDER, 1f);

        var hlg = chip.gameObject.AddComponent<HorizontalLayoutGroup>();
        hlg.spacing = 8f;
        hlg.padding = new RectOffset(18, 18, 10, 10);
        hlg.childControlWidth = true;
        hlg.childControlHeight = true;
        hlg.childForceExpandWidth = false;
        hlg.childForceExpandHeight = false;
        hlg.childAlignment = TextAnchor.MiddleCenter;

        var glyph = AddText(chip, "◆", 22, DIAMOND_BLUE, TextAlignmentOptions.Center);
        var number = AddText(chip, "68", 22, TEXT_CREAM, TextAlignmentOptions.Center);
        glyph.gameObject.name = "Glyph";
        number.gameObject.name = "Number";

        return new DiamondChipBuild { go = chip.gameObject, numberTMP = number };
    }

    private static GameObject BuildCloseHint(RectTransform parent)
    {
        var hint = CreateRect("CloseHint", parent, 180f, 30f);
        hint.anchorMin = new Vector2(1f, 0f);
        hint.anchorMax = new Vector2(1f, 0f);
        hint.pivot = new Vector2(1f, 0f);
        hint.anchoredPosition = new Vector2(-60f, 40f);

        var tmp = AddText(hint, "[F] CLOSE", 14, new Color(TEXT_CREAM.r, TEXT_CREAM.g, TEXT_CREAM.b, 0.35f), TextAlignmentOptions.MidlineRight);
        // Make it clickable so the panel Close button can be wired.
        var btn = hint.gameObject.AddComponent<Button>();
        var img = hint.gameObject.AddComponent<Image>();
        img.color = TRANSPARENT;
        img.raycastTarget = true;
        btn.targetGraphic = img;

        return hint.gameObject;
    }

    // ==========================================================
    //         TABS
    // ==========================================================

    private static (Button, TextMeshProUGUI, GameObject, GameObject) BuildTab(RectTransform parent, string name, string label, bool isActive)
    {
        var tab = CreateRect(name, parent, 300f, TAB_H);
        var img = tab.gameObject.AddComponent<Image>();
        img.color = TRANSPARENT;
        img.raycastTarget = true;

        var btn = tab.gameObject.AddComponent<Button>();
        btn.targetGraphic = img;

        // Active dark background — separate GameObject so we can toggle it.
        var activeBg = CreateRect("ActiveBg", tab, 300f, TAB_H);
        var activeImg = activeBg.gameObject.AddComponent<Image>();
        activeImg.color = TAB_ACTIVE_BG;
        AnchorFill(activeBg);
        activeBg.gameObject.SetActive(isActive);

        // Label
        var lbl = AddText(tab, label, 22, isActive ? TEXT_CREAM : TEXT_MUTED, TextAlignmentOptions.Center);
        lbl.gameObject.name = "Label";
        lbl.fontStyle = FontStyles.Bold;
        lbl.characterSpacing = 14f;
        var lblRT = (RectTransform)lbl.transform;
        AnchorFill(lblRT);

        // Underline — thin brass bar under the tab.
        var underline = CreateRect("Underline", tab, 180f, 2f);
        var ulImg = underline.gameObject.AddComponent<Image>();
        ulImg.color = ACCENT_BRASS;
        underline.anchorMin = new Vector2(0.5f, 0f);
        underline.anchorMax = new Vector2(0.5f, 0f);
        underline.pivot = new Vector2(0.5f, 0f);
        underline.anchoredPosition = new Vector2(0f, 0f);
        underline.sizeDelta = new Vector2(180f, 2f);
        underline.gameObject.SetActive(isActive);

        return (btn, lbl, underline.gameObject, activeBg.gameObject);
    }

    // ==========================================================
    //         HIRE CONTAINER
    // ==========================================================

    private static (GameObject container, Transform rowParent) BuildHireContainer(RectTransform contentArea)
    {
        var container = CreateRect("HireContainer", contentArea, 0f, 0f);
        AnchorFill(container);

        var rowParent = CreateRect("HireRowParent", container, 0f, 0f);
        AnchorFill(rowParent);
        var vlg = rowParent.gameObject.AddComponent<VerticalLayoutGroup>();
        vlg.spacing = 6f;
        vlg.padding = new RectOffset(0, 0, 8, 8);
        vlg.childControlHeight = false;
        vlg.childControlWidth = true;
        vlg.childForceExpandHeight = false;
        vlg.childForceExpandWidth = true;
        vlg.childAlignment = TextAnchor.UpperCenter;

        return (container.gameObject, rowParent);
    }

    // ==========================================================
    //         UPGRADE UNITS CONTAINER
    // ==========================================================

    private static (GameObject container, Transform rowParent) BuildUpgradeContainer(RectTransform contentArea)
    {
        var container = CreateRect("UpgradeUnitsContainer", contentArea, 0f, 0f);
        AnchorFill(container);

        var rowParent = CreateRect("UpgradeRowParent", container, 0f, 0f);
        AnchorFill(rowParent);
        var vlg = rowParent.gameObject.AddComponent<VerticalLayoutGroup>();
        vlg.spacing = 6f;
        vlg.padding = new RectOffset(0, 0, 8, 8);
        vlg.childControlHeight = false;
        vlg.childControlWidth = true;
        vlg.childForceExpandHeight = false;
        vlg.childForceExpandWidth = true;
        vlg.childAlignment = TextAnchor.UpperCenter;

        return (container.gameObject, rowParent);
    }

    // ==========================================================
    //         UPGRADE BARRACKS CONTAINER
    // ==========================================================

    private struct BarracksTabBuild
    {
        public GameObject container;
        public Image diorama;
        public TextMeshProUGUI currentLevel;
        public TextMeshProUGUI summary;
        public TextMeshProUGUI nextLevel;
        public TextMeshProUGUI perks;
        public TextMeshProUGUI costWood;
        public TextMeshProUGUI costStone;
        public TextMeshProUGUI costFood;
        public Button upgradeBtn;
        public TextMeshProUGUI upgradeBtnLabel;
        public TextMeshProUGUI buildTime;
    }

    private static BarracksTabBuild BuildUpgradeBarracksContainer(RectTransform contentArea)
    {
        var b = new BarracksTabBuild();
        var container = CreateRect("UpgradeBarracksContainer", contentArea, 0f, 0f);
        AnchorFill(container);
        b.container = container.gameObject;

        // Two columns: left = diorama + labels, right = next-level card + resource chips + CTA.
        var body = CreateRect("Body", container, 0f, 0f);
        AnchorFill(body);
        var hlg = body.gameObject.AddComponent<HorizontalLayoutGroup>();
        hlg.spacing = 30f;
        hlg.padding = new RectOffset(4, 4, 8, 8);
        hlg.childControlWidth = false;
        hlg.childControlHeight = true;
        hlg.childForceExpandWidth = false;
        hlg.childForceExpandHeight = true;
        hlg.childAlignment = TextAnchor.MiddleLeft;

        // --- Left column ---
        var leftCol = CreateRect("DioramaColumn", body, 320f, 0f);
        var leftVLG = leftCol.gameObject.AddComponent<VerticalLayoutGroup>();
        leftVLG.spacing = 12f;
        leftVLG.childControlHeight = false;
        leftVLG.childControlWidth = true;
        leftVLG.childForceExpandHeight = false;
        leftVLG.childForceExpandWidth = true;
        leftVLG.childAlignment = TextAnchor.UpperCenter;
        var leftLE = leftCol.gameObject.AddComponent<LayoutElement>();
        leftLE.minWidth = 320f; leftLE.preferredWidth = 320f;

        // Diorama circle (placeholder — user assigns real sprite)
        var diorama = CreateRect("Diorama", leftCol, 240f, 240f);
        b.diorama = diorama.gameObject.AddComponent<Image>();
        b.diorama.color = new Color(0.43f, 0.34f, 0.22f, 1f);
        AddOutline(diorama.gameObject, new Color(0.427f, 0.333f, 0.208f, 1f), 3f);

        // "LEVEL 3 / 5"
        var currentLvl = AddText(leftCol, "LEVEL 3 / 5", 34, TEXT_CREAM, TextAlignmentOptions.Center);
        currentLvl.gameObject.name = "CurrentLevelText";
        currentLvl.fontStyle = FontStyles.Bold;
        currentLvl.characterSpacing = 12f;
        b.currentLevel = currentLvl;

        // Summary
        var summary = AddText(leftCol, "MAX SIZE 5 UNITS · KNIGHT TIER UNLOCKED", 14, TEXT_MUTED, TextAlignmentOptions.Center);
        summary.gameObject.name = "SummaryText";
        summary.characterSpacing = 12f;
        summary.enableWordWrapping = true;
        b.summary = summary;

        // --- Right column ---
        var rightCol = CreateRect("RightColumn", body, 0f, 0f);
        var rightVLG = rightCol.gameObject.AddComponent<VerticalLayoutGroup>();
        rightVLG.spacing = 14f;
        rightVLG.childControlHeight = false;
        rightVLG.childControlWidth = true;
        rightVLG.childForceExpandHeight = false;
        rightVLG.childForceExpandWidth = true;
        var rightLE = rightCol.gameObject.AddComponent<LayoutElement>();
        rightLE.flexibleWidth = 1f;

        // Next level card
        var card = CreateRect("NextCard", rightCol, 0f, 130f);
        var cardImg = card.gameObject.AddComponent<Image>();
        cardImg.color = new Color(0f, 0f, 0f, 0.3f);
        AddOutline(card.gameObject, new Color(ACCENT_BRASS.r, ACCENT_BRASS.g, ACCENT_BRASS.b, 0.18f), 1f);
        var cardLE = card.gameObject.AddComponent<LayoutElement>();
        cardLE.preferredHeight = 130f;
        var cardVLG = card.gameObject.AddComponent<VerticalLayoutGroup>();
        cardVLG.spacing = 8f;
        cardVLG.padding = new RectOffset(24, 24, 18, 20);
        cardVLG.childControlWidth = true;
        cardVLG.childControlHeight = false;
        cardVLG.childForceExpandWidth = true;
        cardVLG.childForceExpandHeight = false;

        var nextLvl = AddText(card, "LEVEL 4", 28, TEXT_CREAM, TextAlignmentOptions.MidlineLeft);
        nextLvl.gameObject.name = "NextLevelText";
        nextLvl.fontStyle = FontStyles.Bold;
        nextLvl.characterSpacing = 10f;
        b.nextLevel = nextLvl;

        var perks = AddText(card, "◆ +2 army capacity\n◆ Ambush & Siege tactics unlocked", 16,
                            new Color(TEXT_CREAM.r, TEXT_CREAM.g, TEXT_CREAM.b, 0.78f), TextAlignmentOptions.MidlineLeft);
        perks.gameObject.name = "PerksText";
        perks.enableWordWrapping = true;
        b.perks = perks;

        // Resource chip trio
        var chipStrip = CreateRect("ResourceStrip", rightCol, 0f, 60f);
        var stripHLG = chipStrip.gameObject.AddComponent<HorizontalLayoutGroup>();
        stripHLG.spacing = 14f;
        stripHLG.childControlWidth = true;
        stripHLG.childControlHeight = true;
        stripHLG.childForceExpandWidth = true;
        stripHLG.childForceExpandHeight = true;
        var stripLE = chipStrip.gameObject.AddComponent<LayoutElement>();
        stripLE.preferredHeight = 60f;

        b.costWood  = BuildResourceChip(chipStrip, "WoodChip", "220");
        b.costStone = BuildResourceChip(chipStrip, "StoneChip", "140");
        b.costFood  = BuildResourceChip(chipStrip, "FoodChip", "90");

        // CTA row
        var ctaRow = CreateRect("CtaRow", rightCol, 0f, 80f);
        var ctaHLG = ctaRow.gameObject.AddComponent<HorizontalLayoutGroup>();
        ctaHLG.spacing = 12f;
        ctaHLG.childControlWidth = false;
        ctaHLG.childControlHeight = true;
        ctaHLG.childForceExpandWidth = false;
        ctaHLG.childForceExpandHeight = true;
        var ctaLE = ctaRow.gameObject.AddComponent<LayoutElement>();
        ctaLE.preferredHeight = 80f;

        // Big UPGRADE button
        var upgrade = CreateRect("UpgradeButton", ctaRow, 0f, 80f);
        var upImg = upgrade.gameObject.AddComponent<Image>();
        upImg.color = ACCENT_AMBER;
        b.upgradeBtn = upgrade.gameObject.AddComponent<Button>();
        b.upgradeBtn.targetGraphic = upImg;
        var upLE = upgrade.gameObject.AddComponent<LayoutElement>();
        upLE.flexibleWidth = 1f;
        var upLbl = AddText(upgrade, "UPGRADE", 30, BTN_TEXT_DARK, TextAlignmentOptions.Center);
        upLbl.gameObject.name = "Label";
        upLbl.fontStyle = FontStyles.Bold;
        upLbl.characterSpacing = 18f;
        AnchorFill((RectTransform)upLbl.transform);
        b.upgradeBtnLabel = upLbl;

        // Build time chip on the side
        var timeChip = CreateRect("BuildTimeChip", ctaRow, 100f, 80f);
        var timeImg = timeChip.gameObject.AddComponent<Image>();
        timeImg.color = ACCENT_AMBER;
        var timeLE = timeChip.gameObject.AddComponent<LayoutElement>();
        timeLE.preferredWidth = 100f;
        timeLE.minWidth = 100f;
        var timeVLG = timeChip.gameObject.AddComponent<VerticalLayoutGroup>();
        timeVLG.spacing = 4f;
        timeVLG.padding = new RectOffset(0, 0, 14, 14);
        timeVLG.childAlignment = TextAnchor.MiddleCenter;
        timeVLG.childControlHeight = false;
        timeVLG.childControlWidth = true;
        timeVLG.childForceExpandHeight = false;
        timeVLG.childForceExpandWidth = true;

        // Clock icon placeholder
        var clock = CreateRect("ClockIcon", timeChip, 20f, 20f);
        var clockImg = clock.gameObject.AddComponent<Image>();
        clockImg.color = BTN_TEXT_DARK;

        var timeTxt = AddText(timeChip, "4:00", 18, BTN_TEXT_DARK, TextAlignmentOptions.Center);
        timeTxt.gameObject.name = "BuildTimeText";
        timeTxt.fontStyle = FontStyles.Bold;
        b.buildTime = timeTxt;

        return b;
    }

    private static TextMeshProUGUI BuildResourceChip(RectTransform parent, string name, string amount)
    {
        var chip = CreateRect(name, parent, 0f, 60f);
        var img = chip.gameObject.AddComponent<Image>();
        img.color = CHIP_DARK;
        AddOutline(chip.gameObject, new Color(PANEL_BORDER.r, PANEL_BORDER.g, PANEL_BORDER.b, 0.6f), 1f);

        var hlg = chip.gameObject.AddComponent<HorizontalLayoutGroup>();
        hlg.spacing = 14f;
        hlg.padding = new RectOffset(18, 18, 12, 12);
        hlg.childControlWidth = false;
        hlg.childControlHeight = true;
        hlg.childForceExpandWidth = false;
        hlg.childForceExpandHeight = true;
        hlg.childAlignment = TextAnchor.MiddleLeft;

        // Icon slot placeholder (user swaps sprite for their wood/stone/food icon)
        var slot = CreateRect("IconSlot", chip, 30f, 30f);
        var slotImg = slot.gameObject.AddComponent<Image>();
        slotImg.color = new Color(ACCENT_BRASS.r, ACCENT_BRASS.g, ACCENT_BRASS.b, 0.2f);
        var slotLE = slot.gameObject.AddComponent<LayoutElement>();
        slotLE.preferredWidth = 30f;
        slotLE.preferredHeight = 30f;

        var num = AddText(chip, amount, 22, TEXT_CREAM, TextAlignmentOptions.MidlineLeft);
        num.gameObject.name = "Amount";
        return num;
    }

    // ==========================================================
    //         HIRE ROW PREFAB
    // ==========================================================

    private static GameObject BuildHireRowPrefab()
    {
        var row = new GameObject("HireRow",
            typeof(RectTransform), typeof(Image), typeof(HorizontalLayoutGroup),
            typeof(LayoutElement), typeof(BarracksHireRow));
        var rowRT = (RectTransform)row.transform;
        SetSize(rowRT, PANEL_W - PAD_X * 2, 110f);

        var rowImg = row.GetComponent<Image>();
        rowImg.color = TRANSPARENT;
        rowImg.raycastTarget = true;

        var hlg = row.GetComponent<HorizontalLayoutGroup>();
        hlg.spacing = 24f;
        hlg.padding = new RectOffset(8, 8, 8, 8);
        hlg.childControlWidth = false;
        hlg.childControlHeight = true;
        hlg.childForceExpandWidth = false;
        hlg.childForceExpandHeight = true;
        hlg.childAlignment = TextAnchor.MiddleLeft;

        var rowLE = row.GetComponent<LayoutElement>();
        rowLE.preferredHeight = 110f;
        rowLE.minHeight = 110f;

        // Portrait circle placeholder
        var portrait = CreateRect("Portrait", rowRT, 88f, 88f);
        var portraitImg = portrait.gameObject.AddComponent<Image>();
        portraitImg.color = new Color(0.42f, 0.27f, 0.19f, 1f);
        AddOutline(portrait.gameObject, new Color(0.541f, 0.373f, 0.259f, 1f), 2f);
        var portraitLE = portrait.gameObject.AddComponent<LayoutElement>();
        portraitLE.preferredWidth = 88f; portraitLE.preferredHeight = 88f;

        // Middle text block
        var mid = CreateRect("MiddleText", rowRT, 480f, 90f);
        var midLE = mid.gameObject.AddComponent<LayoutElement>();
        midLE.flexibleWidth = 1f;
        var midVLG = mid.gameObject.AddComponent<VerticalLayoutGroup>();
        midVLG.spacing = 4f;
        midVLG.childControlHeight = false;
        midVLG.childControlWidth = true;
        midVLG.childForceExpandHeight = false;
        midVLG.childForceExpandWidth = true;
        midVLG.childAlignment = TextAnchor.MiddleLeft;

        var name = AddText(mid, "UNIT NAME", 30, TEXT_CREAM, TextAlignmentOptions.MidlineLeft);
        name.gameObject.name = "NameText";
        name.fontStyle = FontStyles.Bold;
        name.characterSpacing = 8f;

        var desc = AddText(mid, "Flavour description text goes here.", 16, TEXT_MUTED, TextAlignmentOptions.TopLeft);
        desc.gameObject.name = "DescriptionText";
        desc.fontStyle = FontStyles.Italic;
        desc.enableWordWrapping = true;

        // Right block (owned + cost + button)
        var right = CreateRect("RightBlock", rowRT, 360f, 90f);
        var rightLE = right.gameObject.AddComponent<LayoutElement>();
        rightLE.preferredWidth = 360f;
        var rightHLG = right.gameObject.AddComponent<HorizontalLayoutGroup>();
        rightHLG.spacing = 20f;
        rightHLG.childControlHeight = true;
        rightHLG.childControlWidth = false;
        rightHLG.childForceExpandHeight = true;
        rightHLG.childForceExpandWidth = false;
        rightHLG.childAlignment = TextAnchor.MiddleRight;

        var owned = AddText(right, "OWNED: 0", 13, TEXT_MUTED, TextAlignmentOptions.Right);
        owned.gameObject.name = "OwnedText";
        owned.characterSpacing = 16f;

        var costChip = CreateRect("CostChip", right, 90f, 44f);
        var costBg = costChip.gameObject.AddComponent<Image>();
        costBg.color = CHIP_DARK;
        AddOutline(costChip.gameObject, new Color(PANEL_BORDER.r, PANEL_BORDER.g, PANEL_BORDER.b, 0.5f), 1f);
        var costLE = costChip.gameObject.AddComponent<LayoutElement>();
        costLE.preferredWidth = 90f;
        var costHLG = costChip.gameObject.AddComponent<HorizontalLayoutGroup>();
        costHLG.spacing = 6f;
        costHLG.padding = new RectOffset(12, 12, 6, 6);
        costHLG.childAlignment = TextAnchor.MiddleCenter;
        costHLG.childControlHeight = true;
        costHLG.childControlWidth = false;
        costHLG.childForceExpandHeight = true;
        costHLG.childForceExpandWidth = false;
        var costGlyph = AddText(costChip, "◆", 18, DIAMOND_BLUE, TextAlignmentOptions.Center);
        costGlyph.gameObject.name = "Glyph";
        var costText = AddText(costChip, "0", 20, TEXT_CREAM, TextAlignmentOptions.Center);
        costText.gameObject.name = "CostText";

        // HIRE button
        var hireBtn = CreateRect("HireButton", right, 130f, 44f);
        var hireImg = hireBtn.gameObject.AddComponent<Image>();
        hireImg.color = ACCENT_AMBER;
        var btnComp = hireBtn.gameObject.AddComponent<Button>();
        btnComp.targetGraphic = hireImg;
        var btnLE = hireBtn.gameObject.AddComponent<LayoutElement>();
        btnLE.preferredWidth = 130f;
        var hireLbl = AddText(hireBtn, "HIRE", 22, BTN_TEXT_DARK, TextAlignmentOptions.Center);
        hireLbl.gameObject.name = "Label";
        hireLbl.fontStyle = FontStyles.Bold;
        hireLbl.characterSpacing = 16f;
        AnchorFill((RectTransform)hireLbl.transform);

        // Wire BarracksHireRow fields
        var rowScript = row.GetComponent<BarracksHireRow>();
        rowScript.iconImage       = portraitImg;
        rowScript.nameText        = name;
        rowScript.descriptionText = desc;
        rowScript.ownedText       = owned;
        rowScript.costText        = costText;
        rowScript.hireButton      = btnComp;

        SaveInstanceAsPrefab(row, HireRowPath);
        return row;
    }

    // ==========================================================
    //         UPGRADE UNIT ROW PREFAB
    // ==========================================================

    private static GameObject BuildUpgradeRowPrefab()
    {
        var row = new GameObject("UpgradeUnitRow",
            typeof(RectTransform), typeof(Image), typeof(HorizontalLayoutGroup),
            typeof(LayoutElement), typeof(BarracksUpgradeUnitRow));
        var rowRT = (RectTransform)row.transform;
        SetSize(rowRT, PANEL_W - PAD_X * 2, 110f);

        var rowImg = row.GetComponent<Image>();
        rowImg.color = TRANSPARENT;

        var hlg = row.GetComponent<HorizontalLayoutGroup>();
        hlg.spacing = 20f;
        hlg.padding = new RectOffset(8, 8, 8, 8);
        hlg.childControlWidth = false;
        hlg.childControlHeight = true;
        hlg.childForceExpandWidth = false;
        hlg.childForceExpandHeight = true;
        hlg.childAlignment = TextAnchor.MiddleLeft;

        var rowLE = row.GetComponent<LayoutElement>();
        rowLE.preferredHeight = 110f;
        rowLE.minHeight = 110f;

        // Portrait
        var portrait = CreateRect("Portrait", rowRT, 88f, 88f);
        var portraitImg = portrait.gameObject.AddComponent<Image>();
        portraitImg.color = new Color(0.42f, 0.27f, 0.19f, 1f);
        AddOutline(portrait.gameObject, new Color(0.541f, 0.373f, 0.259f, 1f), 2f);
        var portraitLE = portrait.gameObject.AddComponent<LayoutElement>();
        portraitLE.preferredWidth = 88f; portraitLE.preferredHeight = 88f;

        // Left text block (name + desc)
        var mid = CreateRect("MiddleText", rowRT, 350f, 90f);
        var midLE = mid.gameObject.AddComponent<LayoutElement>();
        midLE.flexibleWidth = 1f;
        var midVLG = mid.gameObject.AddComponent<VerticalLayoutGroup>();
        midVLG.spacing = 4f;
        midVLG.childControlHeight = false;
        midVLG.childControlWidth = true;
        midVLG.childForceExpandHeight = false;
        midVLG.childForceExpandWidth = true;
        midVLG.childAlignment = TextAnchor.MiddleLeft;

        var name = AddText(mid, "UNIT NAME", 30, TEXT_CREAM, TextAlignmentOptions.MidlineLeft);
        name.gameObject.name = "NameText";
        name.fontStyle = FontStyles.Bold;
        name.characterSpacing = 8f;

        var desc = AddText(mid, "Flavour description text goes here.", 15, TEXT_MUTED, TextAlignmentOptions.TopLeft);
        desc.gameObject.name = "DescriptionText";
        desc.fontStyle = FontStyles.Italic;
        desc.enableWordWrapping = true;

        // Middle: pips + stats
        var statCol = CreateRect("StatColumn", rowRT, 260f, 90f);
        var statLE = statCol.gameObject.AddComponent<LayoutElement>();
        statLE.preferredWidth = 260f;
        var statVLG = statCol.gameObject.AddComponent<VerticalLayoutGroup>();
        statVLG.spacing = 8f;
        statVLG.childControlHeight = false;
        statVLG.childControlWidth = true;
        statVLG.childForceExpandHeight = false;
        statVLG.childForceExpandWidth = true;
        statVLG.childAlignment = TextAnchor.MiddleCenter;

        // Pip row
        var pipsRow = CreateRect("LevelPips", statCol, 200f, 20f);
        var pipsHLG = pipsRow.gameObject.AddComponent<HorizontalLayoutGroup>();
        pipsHLG.spacing = 8f;
        pipsHLG.childControlWidth = false;
        pipsHLG.childControlHeight = false;
        pipsHLG.childForceExpandWidth = false;
        pipsHLG.childForceExpandHeight = false;
        pipsHLG.childAlignment = TextAnchor.MiddleCenter;
        var pipLE = pipsRow.gameObject.AddComponent<LayoutElement>();
        pipLE.preferredHeight = 20f;
        var levelPips = new Image[5];
        for (int i = 0; i < 5; i++)
        {
            var pip = CreateRect($"Pip_{i}", pipsRow, 16f, 16f);
            var pimg = pip.gameObject.AddComponent<Image>();
            pimg.color = i == 0 ? ACCENT_BRASS : TEXT_MUTED;
            levelPips[i] = pimg;
        }

        // Stat block (ATK / HP with current → next)
        var statBlock = CreateRect("StatBlock", statCol, 220f, 50f);
        var sbGrid = statBlock.gameObject.AddComponent<GridLayoutGroup>();
        sbGrid.cellSize = new Vector2(60f, 22f);
        sbGrid.spacing = new Vector2(8f, 4f);
        sbGrid.childAlignment = TextAnchor.MiddleCenter;
        sbGrid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
        sbGrid.constraintCount = 4;

        var atkLabel = AddText(statBlock, "ATK", 16, TEXT_MUTED, TextAlignmentOptions.MidlineRight);
        atkLabel.gameObject.name = "ATKLabel";
        var atkCur = AddText(statBlock, "0", 16, TEXT_MUTED, TextAlignmentOptions.Center);
        atkCur.gameObject.name = "ATKCurrent";
        var atkArrow = AddText(statBlock, "→", 16, ACCENT_BRASS, TextAlignmentOptions.Center);
        atkArrow.gameObject.name = "ATKArrow";
        var atkNxt = AddText(statBlock, "0", 16, TEXT_CREAM, TextAlignmentOptions.Center);
        atkNxt.gameObject.name = "ATKNext";

        var hpLabel = AddText(statBlock, "HP", 16, TEXT_MUTED, TextAlignmentOptions.MidlineRight);
        hpLabel.gameObject.name = "HPLabel";
        var hpCur = AddText(statBlock, "0", 16, TEXT_MUTED, TextAlignmentOptions.Center);
        hpCur.gameObject.name = "HPCurrent";
        var hpArrow = AddText(statBlock, "→", 16, ACCENT_BRASS, TextAlignmentOptions.Center);
        hpArrow.gameObject.name = "HPArrow";
        var hpNxt = AddText(statBlock, "0", 16, TEXT_CREAM, TextAlignmentOptions.Center);
        hpNxt.gameObject.name = "HPNext";

        // Right side: cost + upgrade button
        var right = CreateRect("RightBlock", rowRT, 250f, 90f);
        var rightLE = right.gameObject.AddComponent<LayoutElement>();
        rightLE.preferredWidth = 250f;
        var rightHLG = right.gameObject.AddComponent<HorizontalLayoutGroup>();
        rightHLG.spacing = 16f;
        rightHLG.childControlHeight = true;
        rightHLG.childControlWidth = false;
        rightHLG.childForceExpandHeight = true;
        rightHLG.childForceExpandWidth = false;
        rightHLG.childAlignment = TextAnchor.MiddleRight;

        var costChip = CreateRect("CostChip", right, 90f, 44f);
        var costBg = costChip.gameObject.AddComponent<Image>();
        costBg.color = CHIP_DARK;
        AddOutline(costChip.gameObject, new Color(PANEL_BORDER.r, PANEL_BORDER.g, PANEL_BORDER.b, 0.5f), 1f);
        var costLE = costChip.gameObject.AddComponent<LayoutElement>();
        costLE.preferredWidth = 90f;
        var costHLG = costChip.gameObject.AddComponent<HorizontalLayoutGroup>();
        costHLG.spacing = 6f;
        costHLG.padding = new RectOffset(12, 12, 6, 6);
        costHLG.childAlignment = TextAnchor.MiddleCenter;
        costHLG.childControlHeight = true;
        costHLG.childControlWidth = false;
        costHLG.childForceExpandHeight = true;
        costHLG.childForceExpandWidth = false;
        var costGlyph = AddText(costChip, "◆", 18, DIAMOND_BLUE, TextAlignmentOptions.Center);
        costGlyph.gameObject.name = "Glyph";
        var costText = AddText(costChip, "0", 20, TEXT_CREAM, TextAlignmentOptions.Center);
        costText.gameObject.name = "CostText";

        var upBtn = CreateRect("UpgradeButton", right, 140f, 44f);
        var upImg = upBtn.gameObject.AddComponent<Image>();
        upImg.color = ACCENT_AMBER;
        var btnComp = upBtn.gameObject.AddComponent<Button>();
        btnComp.targetGraphic = upImg;
        var btnLE = upBtn.gameObject.AddComponent<LayoutElement>();
        btnLE.preferredWidth = 140f;
        var upLbl = AddText(upBtn, "UPGRADE", 20, BTN_TEXT_DARK, TextAlignmentOptions.Center);
        upLbl.gameObject.name = "Label";
        upLbl.fontStyle = FontStyles.Bold;
        upLbl.characterSpacing = 16f;
        AnchorFill((RectTransform)upLbl.transform);

        // Wire BarracksUpgradeUnitRow fields
        var rowScript = row.GetComponent<BarracksUpgradeUnitRow>();
        rowScript.iconImage         = portraitImg;
        rowScript.nameText          = name;
        rowScript.descriptionText   = desc;
        rowScript.levelPips         = levelPips;
        rowScript.atkCurrentText    = atkCur;
        rowScript.atkNextText       = atkNxt;
        rowScript.hpCurrentText     = hpCur;
        rowScript.hpNextText        = hpNxt;
        rowScript.costText          = costText;
        rowScript.upgradeButton     = btnComp;
        rowScript.upgradeButtonText = upLbl;

        SaveInstanceAsPrefab(row, UpgradeRowPath);
        return row;
    }

    // ==========================================================
    //         HELPERS
    // ==========================================================

    private static RectTransform CreateRect(string name, Transform parent, float w, float h)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var rt = (RectTransform)go.transform;
        rt.anchorMin = new Vector2(0.5f, 0.5f);
        rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = Vector2.zero;
        SetSize(rt, w, h);
        return rt;
    }

    private static void SetSize(RectTransform rt, float w, float h)
    {
        rt.sizeDelta = new Vector2(w, h);
    }

    private static void Center(RectTransform rt)
    {
        rt.anchorMin = new Vector2(0.5f, 0.5f);
        rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = Vector2.zero;
    }

    private static void CenterHorizontal(RectTransform rt, float y)
    {
        rt.anchorMin = new Vector2(0.5f, 1f);
        rt.anchorMax = new Vector2(0.5f, 1f);
        rt.pivot = new Vector2(0.5f, 1f);
        rt.anchoredPosition = new Vector2(0f, -y);
    }

    private static void AnchorFill(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
    }

    private static void AnchorTopStretch(RectTransform rt, float top, float padX)
    {
        rt.anchorMin = new Vector2(0f, 1f);
        rt.anchorMax = new Vector2(1f, 1f);
        rt.pivot = new Vector2(0.5f, 1f);
        rt.offsetMin = new Vector2(padX, -(top + rt.sizeDelta.y));
        rt.offsetMax = new Vector2(-padX, -top);
    }

    private static void AnchorLeft(RectTransform rt, float x)
    {
        rt.anchorMin = new Vector2(0f, 0.5f);
        rt.anchorMax = new Vector2(0f, 0.5f);
        rt.pivot = new Vector2(0f, 0.5f);
        rt.anchoredPosition = new Vector2(x, 0f);
    }

    private static void AnchorRightMiddle(RectTransform rt)
    {
        rt.anchorMin = new Vector2(1f, 0.5f);
        rt.anchorMax = new Vector2(1f, 0.5f);
        rt.pivot = new Vector2(1f, 0.5f);
        rt.anchoredPosition = Vector2.zero;
    }

    private static void AnchorBottom(RectTransform rt, float y)
    {
        rt.anchorMin = new Vector2(0.5f, 0f);
        rt.anchorMax = new Vector2(0.5f, 0f);
        rt.pivot = new Vector2(0.5f, 0f);
        rt.anchoredPosition = new Vector2(0f, y);
    }

    private static TextMeshProUGUI AddText(Transform parent, string text, int size, Color color, TextAlignmentOptions align)
    {
        var go = new GameObject("Text_TMP", typeof(RectTransform), typeof(TextMeshProUGUI));
        go.transform.SetParent(parent, false);
        var rt = (RectTransform)go.transform;
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
        var tmp = go.GetComponent<TextMeshProUGUI>();
        tmp.text = text;
        tmp.fontSize = size;
        tmp.color = color;
        tmp.alignment = align;
        tmp.raycastTarget = false;
        return tmp;
    }

    private static void AddOutline(GameObject go, Color color, float thickness)
    {
        var outline = go.AddComponent<Outline>();
        outline.effectColor = color;
        outline.effectDistance = new Vector2(thickness, -thickness);
    }

    // Thin brass hairline at the top or bottom of a rect.
    private static void AddHairline(RectTransform parent, bool top, Color color)
    {
        var line = CreateRect(top ? "TopHairline" : "BottomHairline", parent, 0f, 1f);
        line.anchorMin = new Vector2(0f, top ? 1f : 0f);
        line.anchorMax = new Vector2(1f, top ? 1f : 0f);
        line.pivot = new Vector2(0.5f, top ? 1f : 0f);
        line.offsetMin = new Vector2(0f, top ? -1f : 0f);
        line.offsetMax = new Vector2(0f, top ? 0f : 1f);
        var img = line.gameObject.AddComponent<Image>();
        img.color = color;
        img.raycastTarget = false;
    }

    private static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        var parts = path.Split('/');
        string cur = parts[0];
        for (int i = 1; i < parts.Length; i++)
        {
            string next = cur + "/" + parts[i];
            if (!AssetDatabase.IsValidFolder(next))
                AssetDatabase.CreateFolder(cur, parts[i]);
            cur = next;
        }
    }

    private static void SaveInstanceAsPrefab(GameObject instance, string assetPath)
    {
        // Overwrite existing prefab if present so re-running the tool refreshes cleanly.
        PrefabUtility.SaveAsPrefabAsset(instance, assetPath);
    }

    private static Color ParseHex(string hex, float alpha = 1f)
    {
        if (ColorUtility.TryParseHtmlString(hex, out var c))
        {
            c.a = alpha;
            return c;
        }
        return Color.magenta;
    }
}
#endif
