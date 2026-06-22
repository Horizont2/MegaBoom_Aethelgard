using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using TMPro;
using System.Collections.Generic;

// Adds new "Extended" settings controls (sensitivity, subtitles
// on/off, subtitle size, language switcher, colorblind toggle,
// quality preset) WITHOUT touching the existing wired SettingsUI
// prefab. The extension panel auto-builds at runtime, parented to
// the same GlobalHUD canvas, and pops open alongside the regular
// settings panel.
//
// Visual style mirrors the rest of the project: dark almost-black
// backplate, gold accent text, white labels, large hit areas. Open
// via SettingsExtensionUI.Toggle() — hotkey F11 by default, or wire
// a button to call it.
public class SettingsExtensionUI : MonoBehaviour
{
    private static SettingsExtensionUI s_instance;

    private GameObject root;
    private CanvasGroup canvasGroup;
    private bool built;

    private Slider sensSlider;
    private TMP_InputField sensInput;
    private Toggle subtitlesToggle;
    private TMP_Dropdown subtitleSizeDD;
    private TMP_Dropdown languageDD;
    private Toggle colorblindToggle;
    private TMP_Dropdown qualityDD;
    private TextMeshProUGUI achLabel;
    private TextMeshProUGUI loreLabel;
    private TextMeshProUGUI statsLabel;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Install()
    {
        if (s_instance != null) return;
        GameObject go = new GameObject("[SettingsExtensionUI]");
        DontDestroyOnLoad(go);
        s_instance = go.AddComponent<SettingsExtensionUI>();
    }

    public static void Toggle()
    {
        if (s_instance == null) return;
        s_instance.BuildIfNeeded();
        if (s_instance.root == null) return;
        bool active = !s_instance.root.activeSelf;
        s_instance.root.SetActive(active);
        if (active) s_instance.Refresh();
    }

    public static void Open() { if (s_instance == null) return; s_instance.BuildIfNeeded(); if (s_instance.root != null) { s_instance.root.SetActive(true); s_instance.Refresh(); } }
    public static void Close() { if (s_instance == null || s_instance.root == null) return; s_instance.root.SetActive(false); }

    private void Update()
    {
        if (Input.GetKeyDown(KeyCode.F11)) Toggle();
        if (root != null && root.activeSelf && Input.GetKeyDown(KeyCode.Escape)) Close();
    }

    private void BuildIfNeeded()
    {
        if (built) return;
        if (GlobalHUD.Instance == null) return;
        RectTransform hudRect = GlobalHUD.Instance.GetComponent<RectTransform>();
        if (hudRect == null) return;

        Color goldAccent = new Color(0.85f, 0.7f, 0.4f);
        Color labelGold = new Color(1f, 0.84f, 0f, 0.85f);
        Color panelDark = new Color(0.08f, 0.07f, 0.06f, 0.95f);
        Color textWhite = new Color(0.95f, 0.93f, 0.88f);

        root = new GameObject("ExtendedSettingsPanel");
        RectTransform rRT = root.AddComponent<RectTransform>();
        rRT.SetParent(hudRect, false);
        rRT.anchorMin = Vector2.zero;
        rRT.anchorMax = Vector2.one;
        rRT.offsetMin = Vector2.zero;
        rRT.offsetMax = Vector2.zero;
        Image dim = root.AddComponent<Image>();
        dim.color = new Color(0f, 0f, 0f, 0.7f);
        canvasGroup = root.AddComponent<CanvasGroup>();

        // window
        GameObject win = MakeRect(root.transform, "Window", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(720f, 720f));
        Image winBg = win.AddComponent<Image>();
        winBg.color = panelDark;
        Outline outline = win.AddComponent<Outline>();
        outline.effectColor = goldAccent;
        outline.effectDistance = new Vector2(2f, 2f);

        // header
        GameObject hdr = MakeRect(win.transform, "Header", new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f), Vector2.zero, new Vector2(0f, 56f));
        TextMeshProUGUI hdrTxt = hdr.AddComponent<TextMeshProUGUI>();
        hdrTxt.text = "ADVANCED SETTINGS";
        hdrTxt.fontSize = 26f;
        hdrTxt.fontStyle = FontStyles.Bold | FontStyles.SmallCaps;
        hdrTxt.alignment = TextAlignmentOptions.Center;
        hdrTxt.color = goldAccent;
        hdrTxt.outlineWidth = 0.18f;
        hdrTxt.outlineColor = Color.black;
        RectTransform hdrRT = hdr.GetComponent<RectTransform>();
        hdrRT.anchoredPosition = new Vector2(0f, -8f);

        // scroll content container
        GameObject content = MakeRect(win.transform, "Content", new Vector2(0f, 0f), new Vector2(1f, 1f), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
        RectTransform contentRT = content.GetComponent<RectTransform>();
        contentRT.offsetMin = new Vector2(40f, 80f);
        contentRT.offsetMax = new Vector2(-40f, -70f);
        VerticalLayoutGroup vlg = content.AddComponent<VerticalLayoutGroup>();
        vlg.spacing = 14f;
        vlg.childForceExpandHeight = false;
        vlg.childForceExpandWidth = true;
        vlg.childAlignment = TextAnchor.UpperCenter;

        // controls
        AddSectionHeader(content.transform, "GAMEPLAY", labelGold);
        sensSlider = AddSlider(content.transform, "MOUSE SENSITIVITY", 25f, 300f, MouseSensitivitySettings.Multiplier * 100f, textWhite, goldAccent, out sensInput);
        sensSlider.onValueChanged.AddListener(v => { MouseSensitivitySettings.Multiplier = v / 100f; sensInput.text = Mathf.RoundToInt(v).ToString(); });
        sensInput.onEndEdit.AddListener(t => { if (float.TryParse(t, out float r)) { sensSlider.value = Mathf.Clamp(r, sensSlider.minValue, sensSlider.maxValue); } });

        AddSectionHeader(content.transform, "SUBTITLES", labelGold);
        subtitlesToggle = AddToggle(content.transform, "SHOW SUBTITLES", SubtitleSettings.Enabled, textWhite, goldAccent);
        subtitlesToggle.onValueChanged.AddListener(v => SubtitleSettings.Enabled = v);
        subtitleSizeDD = AddDropdown(content.transform, "SIZE", new List<string> { "Small", "Medium", "Large" }, (int)SubtitleSettings.CurrentSize, textWhite, goldAccent);
        subtitleSizeDD.onValueChanged.AddListener(v => SubtitleSettings.CurrentSize = (SubtitleSettings.Size)v);

        AddSectionHeader(content.transform, "ACCESSIBILITY", labelGold);
        colorblindToggle = AddToggle(content.transform, "COLORBLIND MODE", PlayerPrefs.GetInt("Settings_Colorblind", 0) == 1, textWhite, goldAccent);
        colorblindToggle.onValueChanged.AddListener(v =>
        {
            PlayerPrefs.SetInt("Settings_Colorblind", v ? 1 : 0);
            PlayerPrefs.Save();
        });

        AddSectionHeader(content.transform, "LANGUAGE & QUALITY", labelGold);
        languageDD = AddDropdown(content.transform, "LANGUAGE", new List<string> { "English", "Українська" }, (int)LocalizationManager.CurrentLanguage, textWhite, goldAccent);
        languageDD.onValueChanged.AddListener(v => LocalizationManager.CurrentLanguage = (LocalizationManager.Lang)v);
        qualityDD = AddDropdown(content.transform, "GRAPHICS QUALITY", new List<string> { "Low", "Medium", "High", "Ultra" }, Mathf.Clamp(QualitySettings.GetQualityLevel(), 0, 3), textWhite, goldAccent);
        qualityDD.onValueChanged.AddListener(v => QualitySettings.SetQualityLevel(v, true));

        AddSectionHeader(content.transform, "PROGRESS", labelGold);
        achLabel = AddInfoLine(content.transform, "Achievements: -", textWhite);
        loreLabel = AddInfoLine(content.transform, "Lore: -", textWhite);
        statsLabel = AddInfoLine(content.transform, "Stats: -", textWhite);

        // codex button
        AddButton(content.transform, "VIEW CHRONICLES OF AETHELGARD", goldAccent, panelDark, () => { LoreCodexUI.Open(); Close(); });

        // close button bottom
        GameObject closeBtn = MakeRect(win.transform, "CloseBtn", new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 20f), new Vector2(220f, 42f));
        Image cbg = closeBtn.AddComponent<Image>();
        cbg.color = new Color(0.2f, 0.07f, 0.07f, 0.9f);
        Outline co = closeBtn.AddComponent<Outline>();
        co.effectColor = goldAccent;
        co.effectDistance = new Vector2(1.5f, 1.5f);
        Button cb = closeBtn.AddComponent<Button>();
        cb.onClick.AddListener(Close);
        GameObject ct = MakeRect(closeBtn.transform, "Label", Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
        TextMeshProUGUI cbTxt = ct.AddComponent<TextMeshProUGUI>();
        cbTxt.text = "CLOSE";
        cbTxt.fontSize = 18f;
        cbTxt.fontStyle = FontStyles.Bold;
        cbTxt.alignment = TextAlignmentOptions.Center;
        cbTxt.color = labelGold;
        cb.gameObject.AddComponent<AutoButtonAnimator>().Setup(cbTxt, true);

        built = true;
        root.SetActive(false);
    }

    private void Refresh()
    {
        if (sensSlider != null) sensSlider.value = MouseSensitivitySettings.Multiplier * 100f;
        if (subtitlesToggle != null) subtitlesToggle.isOn = SubtitleSettings.Enabled;
        if (subtitleSizeDD != null) subtitleSizeDD.value = (int)SubtitleSettings.CurrentSize;
        if (colorblindToggle != null) colorblindToggle.isOn = PlayerPrefs.GetInt("Settings_Colorblind", 0) == 1;
        if (languageDD != null) languageDD.value = (int)LocalizationManager.CurrentLanguage;
        if (qualityDD != null) qualityDD.value = Mathf.Clamp(QualitySettings.GetQualityLevel(), 0, 3);

        if (achLabel != null)
            achLabel.text = $"Achievements: <color=#D4AF37>{AchievementManager.GetUnlockedCount()}/{AchievementManager.TotalCount}</color>";
        if (loreLabel != null)
            loreLabel.text = $"Lore Recovered: <color=#D4AF37>{LoreCodexManager.UnlockedCount}/{LoreCodexManager.TotalCount}</color>";
        if (statsLabel != null)
            statsLabel.text = $"<size=14>{RunStats.GetSummaryText()}</size>";
    }

    // ============ procedural UI builders ============

    private GameObject MakeRect(Transform parent, string name, Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot, Vector2 anchoredPos, Vector2 size)
    {
        GameObject go = new GameObject(name);
        RectTransform rt = go.AddComponent<RectTransform>();
        rt.SetParent(parent, false);
        rt.anchorMin = anchorMin;
        rt.anchorMax = anchorMax;
        rt.pivot = pivot;
        rt.anchoredPosition = anchoredPos;
        rt.sizeDelta = size;
        return go;
    }

    private void AddSectionHeader(Transform parent, string text, Color color)
    {
        GameObject row = new GameObject("Section_" + text);
        RectTransform rt = row.AddComponent<RectTransform>();
        rt.SetParent(parent, false);
        LayoutElement le = row.AddComponent<LayoutElement>();
        le.minHeight = 26f;
        TextMeshProUGUI tmp = row.AddComponent<TextMeshProUGUI>();
        tmp.text = text;
        tmp.fontSize = 14f;
        tmp.fontStyle = FontStyles.Bold | FontStyles.SmallCaps;
        tmp.alignment = TextAlignmentOptions.MidlineLeft;
        tmp.color = color;
    }

    private Slider AddSlider(Transform parent, string label, float min, float max, float value, Color labelColor, Color fillColor, out TMP_InputField inputField)
    {
        GameObject row = new GameObject("Row_" + label);
        RectTransform rt = row.AddComponent<RectTransform>();
        rt.SetParent(parent, false);
        LayoutElement le = row.AddComponent<LayoutElement>(); le.minHeight = 38f;

        GameObject lab = new GameObject("Label");
        RectTransform lRT = lab.AddComponent<RectTransform>();
        lRT.SetParent(rt, false);
        lRT.anchorMin = new Vector2(0f, 0f); lRT.anchorMax = new Vector2(0.4f, 1f);
        lRT.offsetMin = Vector2.zero; lRT.offsetMax = Vector2.zero;
        TextMeshProUGUI lTxt = lab.AddComponent<TextMeshProUGUI>();
        lTxt.text = label;
        lTxt.fontSize = 15f; lTxt.alignment = TextAlignmentOptions.MidlineLeft; lTxt.color = labelColor;

        GameObject sl = new GameObject("Slider");
        RectTransform sRT = sl.AddComponent<RectTransform>();
        sRT.SetParent(rt, false);
        sRT.anchorMin = new Vector2(0.4f, 0.15f); sRT.anchorMax = new Vector2(0.85f, 0.85f);
        sRT.offsetMin = Vector2.zero; sRT.offsetMax = Vector2.zero;
        Image sBg = sl.AddComponent<Image>();
        sBg.color = new Color(0.15f, 0.13f, 0.10f, 0.9f);
        Slider slider = sl.AddComponent<Slider>();
        slider.minValue = min; slider.maxValue = max; slider.value = value;
        slider.targetGraphic = sBg;
        // fill area
        GameObject fillArea = new GameObject("FillArea");
        RectTransform faRT = fillArea.AddComponent<RectTransform>();
        faRT.SetParent(sRT, false);
        faRT.anchorMin = new Vector2(0f, 0.25f); faRT.anchorMax = new Vector2(1f, 0.75f);
        faRT.offsetMin = new Vector2(4f, 0f); faRT.offsetMax = new Vector2(-4f, 0f);
        GameObject fill = new GameObject("Fill");
        RectTransform fRT = fill.AddComponent<RectTransform>();
        fRT.SetParent(faRT, false);
        fRT.anchorMin = Vector2.zero; fRT.anchorMax = Vector2.one;
        fRT.offsetMin = Vector2.zero; fRT.offsetMax = Vector2.zero;
        Image fImg = fill.AddComponent<Image>();
        fImg.color = fillColor;
        slider.fillRect = fRT;

        // numeric input
        GameObject ip = new GameObject("Input");
        RectTransform ipRT = ip.AddComponent<RectTransform>();
        ipRT.SetParent(rt, false);
        ipRT.anchorMin = new Vector2(0.87f, 0.1f); ipRT.anchorMax = new Vector2(1f, 0.9f);
        ipRT.offsetMin = Vector2.zero; ipRT.offsetMax = Vector2.zero;
        Image ipBg = ip.AddComponent<Image>();
        ipBg.color = new Color(0.12f, 0.10f, 0.08f, 0.9f);
        TMP_InputField input = ip.AddComponent<TMP_InputField>();
        input.targetGraphic = ipBg;

        GameObject txtA = new GameObject("Text Area");
        RectTransform tART = txtA.AddComponent<RectTransform>();
        tART.SetParent(ipRT, false);
        tART.anchorMin = Vector2.zero; tART.anchorMax = Vector2.one;
        tART.offsetMin = new Vector2(6f, 2f); tART.offsetMax = new Vector2(-6f, -2f);
        RectMask2D mask = txtA.AddComponent<RectMask2D>();
        GameObject txtGO = new GameObject("Text");
        RectTransform txtRT = txtGO.AddComponent<RectTransform>();
        txtRT.SetParent(tART, false);
        txtRT.anchorMin = Vector2.zero; txtRT.anchorMax = Vector2.one;
        txtRT.offsetMin = Vector2.zero; txtRT.offsetMax = Vector2.zero;
        TextMeshProUGUI ipTxt = txtGO.AddComponent<TextMeshProUGUI>();
        ipTxt.fontSize = 14f; ipTxt.alignment = TextAlignmentOptions.Center; ipTxt.color = labelColor;
        input.textComponent = ipTxt;
        input.text = Mathf.RoundToInt(value).ToString();
        inputField = input;

        return slider;
    }

    private Toggle AddToggle(Transform parent, string label, bool value, Color labelColor, Color tickColor)
    {
        GameObject row = new GameObject("Row_" + label);
        RectTransform rt = row.AddComponent<RectTransform>();
        rt.SetParent(parent, false);
        LayoutElement le = row.AddComponent<LayoutElement>(); le.minHeight = 32f;

        GameObject lab = new GameObject("Label");
        RectTransform lRT = lab.AddComponent<RectTransform>();
        lRT.SetParent(rt, false);
        lRT.anchorMin = new Vector2(0f, 0f); lRT.anchorMax = new Vector2(0.78f, 1f);
        lRT.offsetMin = Vector2.zero; lRT.offsetMax = Vector2.zero;
        TextMeshProUGUI lTxt = lab.AddComponent<TextMeshProUGUI>();
        lTxt.text = label;
        lTxt.fontSize = 15f; lTxt.alignment = TextAlignmentOptions.MidlineLeft; lTxt.color = labelColor;

        GameObject tg = new GameObject("Toggle");
        RectTransform tRT = tg.AddComponent<RectTransform>();
        tRT.SetParent(rt, false);
        tRT.anchorMin = new Vector2(0.78f, 0.2f); tRT.anchorMax = new Vector2(0.88f, 0.8f);
        tRT.offsetMin = Vector2.zero; tRT.offsetMax = Vector2.zero;
        Image tBg = tg.AddComponent<Image>();
        tBg.color = new Color(0.12f, 0.10f, 0.08f, 0.95f);
        Toggle toggle = tg.AddComponent<Toggle>();
        toggle.targetGraphic = tBg;
        toggle.isOn = value;

        GameObject check = new GameObject("Check");
        RectTransform cRT = check.AddComponent<RectTransform>();
        cRT.SetParent(tRT, false);
        cRT.anchorMin = new Vector2(0.15f, 0.15f); cRT.anchorMax = new Vector2(0.85f, 0.85f);
        cRT.offsetMin = Vector2.zero; cRT.offsetMax = Vector2.zero;
        Image cImg = check.AddComponent<Image>();
        cImg.color = tickColor;
        toggle.graphic = cImg;

        return toggle;
    }

    private TMP_Dropdown AddDropdown(Transform parent, string label, List<string> options, int value, Color labelColor, Color accentColor)
    {
        GameObject row = new GameObject("Row_" + label);
        RectTransform rt = row.AddComponent<RectTransform>();
        rt.SetParent(parent, false);
        LayoutElement le = row.AddComponent<LayoutElement>(); le.minHeight = 36f;

        GameObject lab = new GameObject("Label");
        RectTransform lRT = lab.AddComponent<RectTransform>();
        lRT.SetParent(rt, false);
        lRT.anchorMin = new Vector2(0f, 0f); lRT.anchorMax = new Vector2(0.4f, 1f);
        lRT.offsetMin = Vector2.zero; lRT.offsetMax = Vector2.zero;
        TextMeshProUGUI lTxt = lab.AddComponent<TextMeshProUGUI>();
        lTxt.text = label;
        lTxt.fontSize = 15f; lTxt.alignment = TextAlignmentOptions.MidlineLeft; lTxt.color = labelColor;

        GameObject dd = new GameObject("Dropdown");
        RectTransform dRT = dd.AddComponent<RectTransform>();
        dRT.SetParent(rt, false);
        dRT.anchorMin = new Vector2(0.4f, 0.1f); dRT.anchorMax = new Vector2(1f, 0.9f);
        dRT.offsetMin = Vector2.zero; dRT.offsetMax = Vector2.zero;
        Image dBg = dd.AddComponent<Image>();
        dBg.color = new Color(0.12f, 0.10f, 0.08f, 0.95f);
        TMP_Dropdown drop = dd.AddComponent<TMP_Dropdown>();
        drop.targetGraphic = dBg;
        drop.ClearOptions();
        drop.AddOptions(options);
        drop.value = Mathf.Clamp(value, 0, options.Count - 1);

        // selected label
        GameObject txtGO = new GameObject("Label");
        RectTransform tRT = txtGO.AddComponent<RectTransform>();
        tRT.SetParent(dRT, false);
        tRT.anchorMin = Vector2.zero; tRT.anchorMax = Vector2.one;
        tRT.offsetMin = new Vector2(10f, 0f); tRT.offsetMax = new Vector2(-22f, 0f);
        TextMeshProUGUI dTxt = txtGO.AddComponent<TextMeshProUGUI>();
        dTxt.fontSize = 14f; dTxt.alignment = TextAlignmentOptions.MidlineLeft; dTxt.color = labelColor;
        drop.captionText = dTxt;

        // arrow
        GameObject ar = new GameObject("Arrow");
        RectTransform aRT = ar.AddComponent<RectTransform>();
        aRT.SetParent(dRT, false);
        aRT.anchorMin = new Vector2(1f, 0.5f); aRT.anchorMax = new Vector2(1f, 0.5f);
        aRT.pivot = new Vector2(1f, 0.5f);
        aRT.sizeDelta = new Vector2(20f, 20f);
        aRT.anchoredPosition = new Vector2(-6f, 0f);
        TextMeshProUGUI aTxt = ar.AddComponent<TextMeshProUGUI>();
        aTxt.text = "▼"; aTxt.fontSize = 14f; aTxt.alignment = TextAlignmentOptions.Center; aTxt.color = accentColor;

        // template (required by TMP_Dropdown)
        GameObject tpl = new GameObject("Template");
        RectTransform tplRT = tpl.AddComponent<RectTransform>();
        tplRT.SetParent(dRT, false);
        tplRT.anchorMin = new Vector2(0f, 0f); tplRT.anchorMax = new Vector2(1f, 0f);
        tplRT.pivot = new Vector2(0.5f, 1f);
        tplRT.sizeDelta = new Vector2(0f, 110f);
        tpl.SetActive(false);
        Image tplBg = tpl.AddComponent<Image>();
        tplBg.color = new Color(0.08f, 0.07f, 0.06f, 0.98f);
        ScrollRect sr = tpl.AddComponent<ScrollRect>();
        sr.horizontal = false;

        GameObject viewport = new GameObject("Viewport");
        RectTransform vpRT = viewport.AddComponent<RectTransform>();
        vpRT.SetParent(tplRT, false);
        vpRT.anchorMin = Vector2.zero; vpRT.anchorMax = Vector2.one;
        vpRT.offsetMin = Vector2.zero; vpRT.offsetMax = Vector2.zero;
        Image vpImg = viewport.AddComponent<Image>();
        vpImg.color = new Color(0f, 0f, 0f, 0.001f);
        Mask vpMask = viewport.AddComponent<Mask>();
        vpMask.showMaskGraphic = false;
        sr.viewport = vpRT;

        GameObject contentGO = new GameObject("Content");
        RectTransform cRT = contentGO.AddComponent<RectTransform>();
        cRT.SetParent(vpRT, false);
        cRT.anchorMin = new Vector2(0f, 1f); cRT.anchorMax = new Vector2(1f, 1f);
        cRT.pivot = new Vector2(0.5f, 1f); cRT.sizeDelta = new Vector2(0f, 28f);
        sr.content = cRT;

        GameObject item = new GameObject("Item");
        RectTransform itRT = item.AddComponent<RectTransform>();
        itRT.SetParent(cRT, false);
        itRT.anchorMin = new Vector2(0f, 0.5f); itRT.anchorMax = new Vector2(1f, 0.5f);
        itRT.sizeDelta = new Vector2(0f, 26f);
        Toggle itToggle = item.AddComponent<Toggle>();
        Image itBg = item.AddComponent<Image>();
        itBg.color = new Color(0.18f, 0.15f, 0.10f, 0.5f);
        itToggle.targetGraphic = itBg;
        GameObject itCheck = new GameObject("Checkmark");
        RectTransform icRT = itCheck.AddComponent<RectTransform>();
        icRT.SetParent(itRT, false);
        icRT.anchorMin = new Vector2(0f, 0.5f); icRT.anchorMax = new Vector2(0f, 0.5f);
        icRT.pivot = new Vector2(0f, 0.5f); icRT.sizeDelta = new Vector2(20f, 20f);
        icRT.anchoredPosition = new Vector2(8f, 0f);
        Image icImg = itCheck.AddComponent<Image>();
        icImg.color = accentColor;
        itToggle.graphic = icImg;
        GameObject itLab = new GameObject("Item Label");
        RectTransform ilRT = itLab.AddComponent<RectTransform>();
        ilRT.SetParent(itRT, false);
        ilRT.anchorMin = Vector2.zero; ilRT.anchorMax = Vector2.one;
        ilRT.offsetMin = new Vector2(28f, 0f); ilRT.offsetMax = new Vector2(-8f, 0f);
        TextMeshProUGUI ilTxt = itLab.AddComponent<TextMeshProUGUI>();
        ilTxt.fontSize = 13f; ilTxt.alignment = TextAlignmentOptions.MidlineLeft; ilTxt.color = labelColor;

        drop.template = tplRT;
        drop.itemText = ilTxt;

        return drop;
    }

    private TextMeshProUGUI AddInfoLine(Transform parent, string text, Color color)
    {
        GameObject row = new GameObject("Info");
        RectTransform rt = row.AddComponent<RectTransform>();
        rt.SetParent(parent, false);
        LayoutElement le = row.AddComponent<LayoutElement>(); le.minHeight = 24f;
        TextMeshProUGUI tmp = row.AddComponent<TextMeshProUGUI>();
        tmp.text = text; tmp.fontSize = 14f;
        tmp.alignment = TextAlignmentOptions.MidlineLeft; tmp.color = color;
        return tmp;
    }

    private void AddButton(Transform parent, string label, Color accent, Color bg, UnityEngine.Events.UnityAction onClick)
    {
        GameObject row = new GameObject("Btn_" + label);
        RectTransform rt = row.AddComponent<RectTransform>();
        rt.SetParent(parent, false);
        LayoutElement le = row.AddComponent<LayoutElement>(); le.minHeight = 42f;
        Image img = row.AddComponent<Image>();
        img.color = new Color(bg.r * 1.2f, bg.g * 1.2f, bg.b * 1.2f, 0.9f);
        Outline o = row.AddComponent<Outline>();
        o.effectColor = accent;
        o.effectDistance = new Vector2(1.5f, 1.5f);
        Button btn = row.AddComponent<Button>();
        btn.onClick.AddListener(onClick);
        GameObject t = new GameObject("Label");
        RectTransform tRT = t.AddComponent<RectTransform>();
        tRT.SetParent(rt, false);
        tRT.anchorMin = Vector2.zero; tRT.anchorMax = Vector2.one;
        tRT.offsetMin = Vector2.zero; tRT.offsetMax = Vector2.zero;
        TextMeshProUGUI tmp = t.AddComponent<TextMeshProUGUI>();
        tmp.text = label; tmp.fontSize = 15f; tmp.fontStyle = FontStyles.Bold;
        tmp.alignment = TextAlignmentOptions.Center; tmp.color = accent;
        btn.gameObject.AddComponent<AutoButtonAnimator>().Setup(tmp, true);
    }
}
