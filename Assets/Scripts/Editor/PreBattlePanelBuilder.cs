#if UNITY_EDITOR
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

// Editor tool that constructs the ARMY DEPLOYMENT (PreBattlePanel) prefab
// plus its unit-row sub-prefab. Every field on PreBattlePanel /
// PreBattleUnitRow is wired at build time. User swaps placeholder colours
// for real sprites.
//
// Menu: Tools → Aethelgard → Build PreBattle Panel
public static class PreBattlePanelBuilder
{
    private const string TargetFolder = "Assets/Prefabs/UI/Barracks";
    private const string PanelPrefabPath = TargetFolder + "/PreBattlePanel.prefab";
    private const string RowPrefabPath   = TargetFolder + "/PreBattleUnitRow.prefab";

    // Palette matches the barracks builder so both panels feel like one UI.
    private static readonly Color PANEL_BG      = ParseHex("#1E1B18", 0.94f);
    private static readonly Color PANEL_BORDER  = ParseHex("#5B4E3C");
    private static readonly Color TEXT_CREAM    = ParseHex("#F0E4CB");
    private static readonly Color TEXT_MUTED    = ParseHex("#8A8478");
    private static readonly Color ACCENT_BRASS  = ParseHex("#C7912B");
    private static readonly Color ACCENT_AMBER  = ParseHex("#D9A24A");
    private static readonly Color BTN_TEXT_DARK = ParseHex("#3A2510");
    private static readonly Color CHIP_DARK     = ParseHex("#2A2622");
    private static readonly Color WIN_GREEN     = ParseHex("#8CD98C");
    private static readonly Color TRANSPARENT   = new Color(0f, 0f, 0f, 0f);

    private const float PANEL_W = 900f;
    private const float PANEL_H = 500f;
    private const float PAD_X = 40f;
    private const float PAD_TOP = 32f;
    private const float PAD_BOT = 32f;

    [MenuItem("Tools/Aethelgard/Build PreBattle Panel")]
    public static void BuildAll()
    {
        EnsureFolder(TargetFolder);
        BuildUnitRowPrefab();
        var rowPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(RowPrefabPath);

        var instance = BuildMainPanelInstance(rowPrefab);
        PrefabUtility.SaveAsPrefabAsset(instance, PanelPrefabPath);
        Object.DestroyImmediate(instance);

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        GameLog.Info("[PreBattlePanelBuilder] Built:\n  " + PanelPrefabPath + "\n  " + RowPrefabPath);
        var asset = AssetDatabase.LoadAssetAtPath<GameObject>(PanelPrefabPath);
        if (asset != null) EditorGUIUtility.PingObject(asset);
    }

    // ==================== MAIN PANEL ====================

    private static GameObject BuildMainPanelInstance(GameObject rowPrefab)
    {
        var root = new GameObject("PreBattlePanel",
            typeof(RectTransform), typeof(CanvasGroup), typeof(PreBattlePanel));
        var rootRT = (RectTransform)root.transform;
        SetSize(rootRT, 1920f, 1080f);
        Center(rootRT);

        var canvasGroup = root.GetComponent<CanvasGroup>();
        var panel = root.GetComponent<PreBattlePanel>();

        // Main dark card
        var card = CreateRect("Panel", rootRT, PANEL_W, PANEL_H);
        var cardImg = card.gameObject.AddComponent<Image>();
        cardImg.color = PANEL_BG;
        AddOutline(card.gameObject, PANEL_BORDER, 1f);
        Center(card);

        // ---- HEADER ----
        var header = CreateRect("Header", card, PANEL_W - PAD_X * 2, 60f);
        AnchorTopStretch(header, PAD_TOP, PAD_X);
        var headerHLG = header.gameObject.AddComponent<HorizontalLayoutGroup>();
        headerHLG.spacing = 20f;
        headerHLG.childAlignment = TextAnchor.MiddleCenter;
        headerHLG.childControlWidth = false;
        headerHLG.childControlHeight = false;
        headerHLG.childForceExpandWidth = false;
        headerHLG.childForceExpandHeight = false;

        var spacer = CreateRect("HeaderSpacerLeft", header, 200f, 40f);

        var title = AddText(header, "ARMY DEPLOYMENT", 30, TEXT_CREAM, TextAlignmentOptions.Center);
        title.gameObject.name = "TitleText";
        title.fontStyle = FontStyles.Bold;
        title.characterSpacing = 10f;
        var titleLE = title.gameObject.AddComponent<LayoutElement>();
        titleLE.preferredWidth = 380f;

        // Level pips row on the right
        var pipsRow = CreateRect("LevelPips", header, 200f, 30f);
        var pipsHLG = pipsRow.gameObject.AddComponent<HorizontalLayoutGroup>();
        pipsHLG.spacing = 8f;
        pipsHLG.childAlignment = TextAnchor.MiddleRight;
        pipsHLG.childControlWidth = false;
        pipsHLG.childControlHeight = false;
        pipsHLG.childForceExpandWidth = false;
        pipsHLG.childForceExpandHeight = false;
        var pipImages = new Image[5];
        for (int i = 0; i < 5; i++)
        {
            var pip = CreateRect($"HelmPip_{i}", pipsRow, 24f, 24f);
            var img = pip.gameObject.AddComponent<Image>();
            img.color = i < 3 ? ACCENT_BRASS : TEXT_MUTED;
            pipImages[i] = img;
        }

        // ---- BODY : left = unit list, right = forecast + CTA ----
        var body = CreateRect("Body", card, PANEL_W - PAD_X * 2, PANEL_H - PAD_TOP - 60f - PAD_BOT);
        AnchorTopStretch(body, PAD_TOP + 80f, PAD_X);
        var bodyHLG = body.gameObject.AddComponent<HorizontalLayoutGroup>();
        bodyHLG.spacing = 30f;
        bodyHLG.childControlWidth = false;
        bodyHLG.childControlHeight = true;
        bodyHLG.childForceExpandWidth = false;
        bodyHLG.childForceExpandHeight = true;

        // -- LEFT: unit row parent --
        var leftCol = CreateRect("LeftColumn", body, 470f, 0f);
        var leftLE = leftCol.gameObject.AddComponent<LayoutElement>();
        leftLE.preferredWidth = 470f;
        var leftVLG = leftCol.gameObject.AddComponent<VerticalLayoutGroup>();
        leftVLG.spacing = 12f;
        leftVLG.childControlWidth = true;
        leftVLG.childControlHeight = false;
        leftVLG.childForceExpandWidth = true;
        leftVLG.childForceExpandHeight = false;
        leftVLG.childAlignment = TextAnchor.UpperCenter;

        // Row parent placeholder — script instantiates row prefabs into here.
        var rowParent = CreateRect("UnitRowParent", leftCol, 470f, 0f);
        var rpVLG = rowParent.gameObject.AddComponent<VerticalLayoutGroup>();
        rpVLG.spacing = 10f;
        rpVLG.childControlWidth = true;
        rpVLG.childControlHeight = false;
        rpVLG.childForceExpandWidth = true;
        rpVLG.childForceExpandHeight = false;

        // Small CANCEL text link under the row list
        var cancelText = CreateRect("CancelTextButton", leftCol, 100f, 30f);
        var cancelImg = cancelText.gameObject.AddComponent<Image>();
        cancelImg.color = TRANSPARENT;
        cancelImg.raycastTarget = true;
        var closeBtn = cancelText.gameObject.AddComponent<Button>();
        closeBtn.targetGraphic = cancelImg;
        var cancelLE = cancelText.gameObject.AddComponent<LayoutElement>();
        cancelLE.preferredHeight = 30f;
        var cancelLbl = AddText(cancelText, "CANCEL", 14, TEXT_MUTED, TextAlignmentOptions.MidlineRight);
        cancelLbl.characterSpacing = 12f;
        AnchorFill((RectTransform)cancelLbl.transform);

        // -- RIGHT: win prob gauge, travel, enemy power, DEPLOY, CANCEL --
        var rightCol = CreateRect("RightColumn", body, 320f, 0f);
        var rightLE = rightCol.gameObject.AddComponent<LayoutElement>();
        rightLE.preferredWidth = 320f;
        var rightVLG = rightCol.gameObject.AddComponent<VerticalLayoutGroup>();
        rightVLG.spacing = 14f;
        rightVLG.childControlWidth = true;
        rightVLG.childControlHeight = false;
        rightVLG.childForceExpandWidth = true;
        rightVLG.childForceExpandHeight = false;
        rightVLG.childAlignment = TextAnchor.UpperCenter;

        // Win Probability label
        var wpLabel = AddText(rightCol, "Win Probability", 18, TEXT_CREAM, TextAlignmentOptions.Center);
        wpLabel.characterSpacing = 6f;
        wpLabel.gameObject.name = "WinProbLabel";

        // Circular gauge — ring + percent text
        var gauge = CreateRect("WinProbGauge", rightCol, 180f, 180f);
        var gaugeLE = gauge.gameObject.AddComponent<LayoutElement>();
        gaugeLE.preferredHeight = 180f;
        // Background ring
        var bg = CreateRect("Ring_BG", gauge, 180f, 180f);
        AnchorFill(bg);
        var bgImg = bg.gameObject.AddComponent<Image>();
        bgImg.color = new Color(TEXT_MUTED.r, TEXT_MUTED.g, TEXT_MUTED.b, 0.25f);
        bgImg.type = Image.Type.Simple;
        // Foreground fill (radial)
        var fillRT = CreateRect("Ring_Fill", gauge, 180f, 180f);
        AnchorFill(fillRT);
        var fillImg = fillRT.gameObject.AddComponent<Image>();
        fillImg.color = WIN_GREEN;
        fillImg.type = Image.Type.Filled;
        fillImg.fillMethod = Image.FillMethod.Radial360;
        fillImg.fillOrigin = 2; // Top
        fillImg.fillClockwise = true;
        fillImg.fillAmount = 0.85f;
        // Percent text
        var pct = AddText(gauge, "85%", 42, WIN_GREEN, TextAlignmentOptions.Center);
        pct.gameObject.name = "WinProbabilityText";
        pct.fontStyle = FontStyles.Bold;
        AnchorFill((RectTransform)pct.transform);

        // Travel Time / Enemy Power info
        var infoBlock = CreateRect("InfoBlock", rightCol, 320f, 60f);
        var infoVLG = infoBlock.gameObject.AddComponent<VerticalLayoutGroup>();
        infoVLG.spacing = 4f;
        infoVLG.childAlignment = TextAnchor.UpperCenter;
        infoVLG.childControlWidth = true;
        infoVLG.childControlHeight = false;
        infoVLG.childForceExpandWidth = true;
        infoVLG.childForceExpandHeight = false;
        var infoLE = infoBlock.gameObject.AddComponent<LayoutElement>();
        infoLE.preferredHeight = 60f;
        var travel = AddText(infoBlock, "Travel Time: 15m 00s", 15, TEXT_MUTED, TextAlignmentOptions.Center);
        travel.gameObject.name = "TravelTimeText";
        var enemyPow = AddText(infoBlock, "Enemy Power: 450", 15, TEXT_MUTED, TextAlignmentOptions.Center);
        enemyPow.gameObject.name = "EnemyPowerText";

        // DEPLOY ARMY button
        var deploy = CreateRect("DeployButton", rightCol, 260f, 46f);
        var deployImg = deploy.gameObject.AddComponent<Image>();
        deployImg.color = ACCENT_AMBER;
        var deployBtn = deploy.gameObject.AddComponent<Button>();
        deployBtn.targetGraphic = deployImg;
        var deployLE = deploy.gameObject.AddComponent<LayoutElement>();
        deployLE.preferredHeight = 46f;
        var deployLbl = AddText(deploy, "DEPLOY ARMY", 20, BTN_TEXT_DARK, TextAlignmentOptions.Center);
        deployLbl.gameObject.name = "Label";
        deployLbl.fontStyle = FontStyles.Bold;
        deployLbl.characterSpacing = 14f;
        AnchorFill((RectTransform)deployLbl.transform);

        // Secondary CANCEL button under DEPLOY
        var cancelBtn = CreateRect("CancelButton", rightCol, 260f, 36f);
        var cbImg = cancelBtn.gameObject.AddComponent<Image>();
        cbImg.color = new Color(ACCENT_AMBER.r, ACCENT_AMBER.g, ACCENT_AMBER.b, 0.55f);
        var cancelBtnComp = cancelBtn.gameObject.AddComponent<Button>();
        cancelBtnComp.targetGraphic = cbImg;
        var cbLE = cancelBtn.gameObject.AddComponent<LayoutElement>();
        cbLE.preferredHeight = 36f;
        var cancelBtnLbl = AddText(cancelBtn, "CANCEL", 16, BTN_TEXT_DARK, TextAlignmentOptions.Center);
        cancelBtnLbl.characterSpacing = 12f;
        AnchorFill((RectTransform)cancelBtnLbl.transform);

        // ---- Wire fields ----
        panel.rootObject = root;
        panel.canvasGroup = canvasGroup;
        panel.closeButton = closeBtn;
        panel.cancelButton = cancelBtnComp;

        panel.titleText = title;
        panel.barracksLevelPips = pipImages;

        panel.regionNameText = null; // no dedicated region-name field in mockup (title reused)
        panel.enemyStrengthText = enemyPow;   // legacy fallback points at same TMP
        panel.enemyPowerText = enemyPow;
        panel.travelTimeText = travel;

        panel.unitRowParent = rowParent;
        panel.unitRowPrefab = rowPrefab;

        panel.winProbabilityText = pct;
        panel.winProbabilityFillImage = fillImg;

        panel.confirmButton = deployBtn;
        panel.confirmButtonText = deployLbl;

        return root;
    }

    // ==================== UNIT ROW ====================

    private static void BuildUnitRowPrefab()
    {
        var row = new GameObject("PreBattleUnitRow",
            typeof(RectTransform), typeof(Image), typeof(HorizontalLayoutGroup),
            typeof(LayoutElement), typeof(PreBattleUnitRow));
        var rowRT = (RectTransform)row.transform;
        SetSize(rowRT, 470f, 64f);
        var rowImg = row.GetComponent<Image>();
        rowImg.color = new Color(0f, 0f, 0f, 0.28f);

        var hlg = row.GetComponent<HorizontalLayoutGroup>();
        hlg.spacing = 14f;
        hlg.padding = new RectOffset(10, 10, 8, 8);
        hlg.childControlWidth = false;
        hlg.childControlHeight = true;
        hlg.childForceExpandWidth = false;
        hlg.childForceExpandHeight = true;
        hlg.childAlignment = TextAnchor.MiddleLeft;
        var rowLE = row.GetComponent<LayoutElement>();
        rowLE.preferredHeight = 64f;
        rowLE.minHeight = 64f;

        // Portrait
        var portrait = CreateRect("Portrait", rowRT, 48f, 48f);
        var portraitImg = portrait.gameObject.AddComponent<Image>();
        portraitImg.color = new Color(0.42f, 0.27f, 0.19f, 1f);
        AddOutline(portrait.gameObject, new Color(0.541f, 0.373f, 0.259f, 1f), 2f);
        var pLE = portrait.gameObject.AddComponent<LayoutElement>();
        pLE.preferredWidth = 48f; pLE.preferredHeight = 48f;

        // Name text
        var name = AddText(rowRT, "Unit Name", 18, TEXT_CREAM, TextAlignmentOptions.MidlineLeft);
        name.gameObject.name = "NameText";
        var nameLE = name.gameObject.AddComponent<LayoutElement>();
        nameLE.flexibleWidth = 1f;

        // Minus button
        var minus = CreateRect("MinusButton", rowRT, 36f, 36f);
        var minusImg = minus.gameObject.AddComponent<Image>();
        minusImg.color = CHIP_DARK;
        AddOutline(minus.gameObject, new Color(PANEL_BORDER.r, PANEL_BORDER.g, PANEL_BORDER.b, 0.6f), 1f);
        var minusBtn = minus.gameObject.AddComponent<Button>();
        minusBtn.targetGraphic = minusImg;
        var mLE = minus.gameObject.AddComponent<LayoutElement>();
        mLE.preferredWidth = 36f;
        var mLbl = AddText(minus, "−", 22, TEXT_CREAM, TextAlignmentOptions.Center);
        mLbl.fontStyle = FontStyles.Bold;
        AnchorFill((RectTransform)mLbl.transform);

        // Count
        var count = AddText(rowRT, "0", 22, TEXT_CREAM, TextAlignmentOptions.Center);
        count.gameObject.name = "CountText";
        count.fontStyle = FontStyles.Bold;
        var countLE = count.gameObject.AddComponent<LayoutElement>();
        countLE.preferredWidth = 40f;

        // Plus button
        var plus = CreateRect("PlusButton", rowRT, 36f, 36f);
        var plusImg = plus.gameObject.AddComponent<Image>();
        plusImg.color = CHIP_DARK;
        AddOutline(plus.gameObject, new Color(PANEL_BORDER.r, PANEL_BORDER.g, PANEL_BORDER.b, 0.6f), 1f);
        var plusBtn = plus.gameObject.AddComponent<Button>();
        plusBtn.targetGraphic = plusImg;
        var pLE2 = plus.gameObject.AddComponent<LayoutElement>();
        pLE2.preferredWidth = 36f;
        var pLbl = AddText(plus, "+", 22, TEXT_CREAM, TextAlignmentOptions.Center);
        pLbl.fontStyle = FontStyles.Bold;
        AnchorFill((RectTransform)pLbl.transform);

        // Optional "Available: N" hidden slot — mockup doesn't show it, but
        // the script still writes to availableText if wired. Add a hidden
        // TMP so the field can be optionally shown by resizing / repositioning.
        var avail = AddText(rowRT, "Available: 0", 11, TEXT_MUTED, TextAlignmentOptions.MidlineRight);
        avail.gameObject.name = "AvailableText";
        avail.gameObject.SetActive(false);
        var availLE = avail.gameObject.AddComponent<LayoutElement>();
        availLE.ignoreLayout = true;

        // Wire PreBattleUnitRow fields
        var rowScript = row.GetComponent<PreBattleUnitRow>();
        rowScript.iconImage = portraitImg;
        rowScript.nameText = name;
        rowScript.availableText = avail;
        rowScript.countText = count;
        rowScript.plusButton = plusBtn;
        rowScript.minusButton = minusBtn;

        PrefabUtility.SaveAsPrefabAsset(row, RowPrefabPath);
        Object.DestroyImmediate(row);
    }

    // ==================== HELPERS ====================

    private static RectTransform CreateRect(string name, Transform parent, float w, float h)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var rt = (RectTransform)go.transform;
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = Vector2.zero;
        SetSize(rt, w, h);
        return rt;
    }
    private static void SetSize(RectTransform rt, float w, float h) => rt.sizeDelta = new Vector2(w, h);
    private static void Center(RectTransform rt)
    {
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = Vector2.zero;
    }
    private static void AnchorFill(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
    }
    private static void AnchorTopStretch(RectTransform rt, float top, float padX)
    {
        rt.anchorMin = new Vector2(0f, 1f); rt.anchorMax = new Vector2(1f, 1f);
        rt.pivot = new Vector2(0.5f, 1f);
        rt.offsetMin = new Vector2(padX, -(top + rt.sizeDelta.y));
        rt.offsetMax = new Vector2(-padX, -top);
    }

    private static TextMeshProUGUI AddText(Transform parent, string text, int size, Color color, TextAlignmentOptions align)
    {
        var go = new GameObject("Text_TMP", typeof(RectTransform), typeof(TextMeshProUGUI));
        go.transform.SetParent(parent, false);
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

    private static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        var parts = path.Split('/');
        string cur = parts[0];
        for (int i = 1; i < parts.Length; i++)
        {
            string next = cur + "/" + parts[i];
            if (!AssetDatabase.IsValidFolder(next)) AssetDatabase.CreateFolder(cur, parts[i]);
            cur = next;
        }
    }

    private static Color ParseHex(string hex, float alpha = 1f)
    {
        if (ColorUtility.TryParseHtmlString(hex, out var c)) { c.a = alpha; return c; }
        return Color.magenta;
    }
}
#endif
