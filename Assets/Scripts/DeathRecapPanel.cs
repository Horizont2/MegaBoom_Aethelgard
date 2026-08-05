using System.Collections;
using System.Text;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

// Aethelgard-styled death recap panel. Runtime-built (no scene wiring)
// so DeathCinematicManager can call DeathRecapPanel.Show() from any
// scene. Sits over the black background the cinematic manager already
// fades in; opens between "You have fallen" typewriter and the
// Retry / Return to Camp buttons.
//
// Reads RunSession — the per-run scoreboard that's reset in
// MissionInitializer.Awake and appended to by kill / dodge / diamond
// / level-up call sites. Wood/Stone/Food are DELIBERATELY NOT shown:
// those resources die with the player and listing them next to
// "Slain by …" would just rub it in.
public class DeathRecapPanel : MonoBehaviour
{
    // Palette — Aethelgard gold + parchment against the cinematic's
    // black backdrop.
    private static readonly Color COL_PANEL_BG  = new Color(0.09f, 0.08f, 0.06f, 0.94f);
    private static readonly Color COL_BORDER    = new Color(0.86f, 0.72f, 0.35f, 0.85f);
    private static readonly Color COL_TITLE     = new Color(0.94f, 0.86f, 0.55f, 1f);
    private static readonly Color COL_LABEL     = new Color(0.78f, 0.72f, 0.58f, 0.95f);
    private static readonly Color COL_VALUE     = new Color(1f,    0.92f, 0.72f, 1f);
    private static readonly Color COL_ACCENT    = new Color(0.86f, 0.72f, 0.35f, 1f);
    private static readonly Color COL_CAUSE     = new Color(0.86f, 0.36f, 0.32f, 1f); // muted crimson

    private static DeathRecapPanel s_instance;
    public static bool IsOpen { get; private set; }

    public static void Show()
    {
        if (s_instance != null) { Destroy(s_instance.gameObject); s_instance = null; }
        var go = new GameObject("[DeathRecapPanel]");
        DontDestroyOnLoad(go);
        s_instance = go.AddComponent<DeathRecapPanel>();
        s_instance.Build();
        s_instance.StartCoroutine(s_instance.AnimateIn());
    }

    public static void Close()
    {
        if (s_instance == null) return;
        Destroy(s_instance.gameObject);
        s_instance = null;
        IsOpen = false;
    }

    private CanvasGroup panelGroup;
    private RectTransform panelRT;
    private TextMeshProUGUI titleTMP;
    private TextMeshProUGUI causeTMP;
    private TextMeshProUGUI bodyTMP;

    private struct Line { public string label; public int value; public bool accent; public Line(string l, int v, bool a = false) { label = l; value = v; accent = a; } }
    private System.Collections.Generic.List<Line> lines;

    private void Build()
    {
        var canvasGO = new GameObject("Canvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        canvasGO.transform.SetParent(transform, false);
        var canvas = canvasGO.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 31000; // just under top-priority overlays
        var scaler = canvasGO.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;

        var panelGO = new GameObject("Panel", typeof(RectTransform), typeof(Image), typeof(CanvasGroup));
        panelGO.transform.SetParent(canvasGO.transform, false);
        panelRT = (RectTransform)panelGO.transform;
        panelRT.anchorMin = new Vector2(0.5f, 0.5f);
        panelRT.anchorMax = new Vector2(0.5f, 0.5f);
        panelRT.pivot = new Vector2(0.5f, 0.5f);
        panelRT.sizeDelta = new Vector2(720f, 640f);
        panelGO.GetComponent<Image>().color = COL_PANEL_BG;
        panelGroup = panelGO.GetComponent<CanvasGroup>();
        panelGroup.alpha = 0f;

        // Thin gold border via a slightly larger image behind the panel.
        var borderGO = new GameObject("Border", typeof(RectTransform), typeof(Image));
        borderGO.transform.SetParent(panelGO.transform, false);
        var bRT = (RectTransform)borderGO.transform;
        bRT.anchorMin = Vector2.zero; bRT.anchorMax = Vector2.one;
        bRT.offsetMin = new Vector2(-2f, -2f); bRT.offsetMax = new Vector2(2f, 2f);
        borderGO.GetComponent<Image>().color = COL_BORDER;
        borderGO.transform.SetAsFirstSibling();

        // Title — "YOUR TALE ENDS HERE"
        titleTMP = MakeText(panelGO.transform, "Title", LocalizationManager.Tr("DEATH_RECAP_TITLE"),
            new Vector2(0.05f, 0.83f), new Vector2(0.95f, 0.96f),
            fontSize: 48, color: COL_TITLE, align: TextAlignmentOptions.Center, bold: true);

        // Divider line
        var divGO = new GameObject("Divider", typeof(RectTransform), typeof(Image));
        divGO.transform.SetParent(panelGO.transform, false);
        var dRT = (RectTransform)divGO.transform;
        dRT.anchorMin = new Vector2(0.15f, 0.80f);
        dRT.anchorMax = new Vector2(0.85f, 0.815f);
        dRT.offsetMin = Vector2.zero; dRT.offsetMax = Vector2.zero;
        divGO.GetComponent<Image>().color = new Color(COL_ACCENT.r, COL_ACCENT.g, COL_ACCENT.b, 0.4f);

        // Cause of death — "Slain by ___"
        string cause = string.IsNullOrEmpty(RunSession.LastDamageSource)
            ? LocalizationManager.Tr("DEATH_RECAP_CAUSE_UNKNOWN")
            : LocalizationManager.Tr("DEATH_RECAP_CAUSE", LocalizationManager.Tr(RunSession.LastDamageSource));
        causeTMP = MakeText(panelGO.transform, "Cause", cause,
            new Vector2(0.05f, 0.72f), new Vector2(0.95f, 0.79f),
            fontSize: 26, color: COL_CAUSE, align: TextAlignmentOptions.Center, italic: true);

        // Body — the stat table, populated by AnimateIn.
        bodyTMP = MakeText(panelGO.transform, "Body", "",
            new Vector2(0.08f, 0.10f), new Vector2(0.92f, 0.70f),
            fontSize: 28, color: COL_VALUE, align: TextAlignmentOptions.TopLeft);

        // Compose the stat list.
        lines = new System.Collections.Generic.List<Line>();
        int seconds = Mathf.RoundToInt(RunSession.SecondsElapsed);
        lines.Add(new Line("DEATH_RECAP_TIME_SURVIVED", seconds, accent: true));
        lines.Add(new Line("DEATH_RECAP_ENEMIES",       RunSession.Kills));
        if (RunSession.Elites > 0)  lines.Add(new Line("DEATH_RECAP_ELITES",  RunSession.Elites));
        if (RunSession.Bosses > 0)  lines.Add(new Line("DEATH_RECAP_BOSSES",  RunSession.Bosses));
        lines.Add(new Line("DEATH_RECAP_DIAMONDS",      RunSession.DiamondsEarned, accent: true));
        lines.Add(new Line("DEATH_RECAP_LEVEL_REACHED", RunSession.MaxLevelReached));
        lines.Add(new Line("DEATH_RECAP_LEVELS_GAINED", RunSession.LevelUps));
        if (RunSession.PerfectDodges > 0)  lines.Add(new Line("DEATH_RECAP_PERFECT_DODGES", RunSession.PerfectDodges));
        if (RunSession.MissionsCompleted > 0) lines.Add(new Line("DEATH_RECAP_MISSIONS", RunSession.MissionsCompleted));
        if (RunSession.ScrollsFound > 0) lines.Add(new Line("DEATH_RECAP_SCROLLS", RunSession.ScrollsFound));

        IsOpen = true;
    }

    private TextMeshProUGUI MakeText(Transform parent, string name, string text,
                                     Vector2 aMin, Vector2 aMax, float fontSize,
                                     Color color, TextAlignmentOptions align,
                                     bool bold = false, bool italic = false)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
        go.transform.SetParent(parent, false);
        var rt = (RectTransform)go.transform;
        rt.anchorMin = aMin; rt.anchorMax = aMax;
        rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
        var tmp = go.GetComponent<TextMeshProUGUI>();
        tmp.text = text;
        tmp.fontSize = fontSize;
        tmp.color = color;
        tmp.alignment = align;
        tmp.enableWordWrapping = false;
        var style = FontStyles.Normal;
        if (bold) style |= FontStyles.Bold;
        if (italic) style |= FontStyles.Italic;
        tmp.fontStyle = style;
        return tmp;
    }

    // Animated in: fade + scale the panel up, then reveal stat lines
    // one by one with count-up numbers so each entry earns a beat.
    private IEnumerator AnimateIn()
    {
        // 1. Fade + scale the whole panel in.
        panelRT.localScale = new Vector3(0.90f, 0.90f, 1f);
        for (float t = 0f; t < 0.5f; t += Time.unscaledDeltaTime)
        {
            float k = Mathf.Clamp01(t / 0.5f);
            float e = 1f - (1f - k) * (1f - k);
            panelGroup.alpha = e;
            panelRT.localScale = Vector3.LerpUnclamped(new Vector3(0.90f, 0.90f, 1f), Vector3.one, e);
            yield return null;
        }
        panelGroup.alpha = 1f;
        panelRT.localScale = Vector3.one;

        yield return new WaitForSecondsRealtime(0.25f);

        // 2. Reveal stat lines with count-up on each value.
        var sb = new StringBuilder(512);
        for (int i = 0; i < lines.Count; i++)
        {
            var line = lines[i];
            // Freeze what's already revealed
            string frozen = sb.ToString();
            // Count-up from 0 → line.value over ~0.35s
            int steps = Mathf.Clamp(Mathf.CeilToInt(line.value / 20f) + 8, 8, 30);
            for (int s = 0; s <= steps; s++)
            {
                float k = (float)s / steps;
                int shown = Mathf.RoundToInt(Mathf.Lerp(0f, line.value, k));
                string valueStr = FormatValue(line.label, shown);
                bodyTMP.text = frozen + BuildLine(line.label, valueStr, line.accent);
                yield return new WaitForSecondsRealtime(0.35f / Mathf.Max(1, steps));
            }
            // Finalize this line and append
            string finalValue = FormatValue(line.label, line.value);
            sb.Append(BuildLine(line.label, finalValue, line.accent));
            bodyTMP.text = sb.ToString();

            // Soft chime on each line if audio is available
            if (AudioManager.Instance != null && (i % 2 == 0))
                AudioManager.Instance.PlayUI(AudioID.UI_Click);

            yield return new WaitForSecondsRealtime(0.12f);
        }
    }

    // "Time Survived" formats as m:ss; everything else is the raw int.
    private string FormatValue(string labelKey, int val)
    {
        if (labelKey == "DEATH_RECAP_TIME_SURVIVED")
        {
            int m = val / 60;
            int s = val % 60;
            return $"{m}:{s:D2}";
        }
        return val.ToString();
    }

    private string BuildLine(string labelKey, string valueStr, bool accent)
    {
        string labelText = LocalizationManager.Tr(labelKey);
        string valueColor = ColorToHex(accent ? COL_ACCENT : COL_VALUE);
        // Aligned via TMP's <line-indent> + <align> won't play nice
        // with per-line color; use a simple label ... value pattern.
        return $"<color={ColorToHex(COL_LABEL)}>{labelText}</color>  <color={valueColor}><b>{valueStr}</b></color>\n";
    }

    private static string ColorToHex(Color c)
    {
        return string.Format("#{0:X2}{1:X2}{2:X2}",
            Mathf.RoundToInt(c.r * 255f),
            Mathf.RoundToInt(c.g * 255f),
            Mathf.RoundToInt(c.b * 255f));
    }
}
