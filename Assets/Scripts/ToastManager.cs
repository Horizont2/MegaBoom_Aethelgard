using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections.Generic;

// Slide-in toast notification stack for mission complete / achievement
// unlocked / quick-save / region cleared / lore found messages. Builds
// its own UI at runtime parented to GlobalHUD's canvas so no prefab
// wiring is required.
//
// Public API is the single static Show(text, kind) call; the
// singleton MonoBehaviour spawns itself on first access.
public class ToastManager : MonoBehaviour
{
    public enum ToastKind { Info, Achievement, Save, Warning, Lore }

    private static ToastManager s_instance;

    private RectTransform container;
    private readonly List<RectTransform> liveToasts = new List<RectTransform>(8);

    private const float TOAST_HEIGHT = 48f;
    private const float TOAST_SPACING = 8f;

    public static void Show(string text, ToastKind kind = ToastKind.Info)
    {
        Get().SpawnToast(text, kind);
    }

    private static ToastManager Get()
    {
        if (s_instance != null) return s_instance;
        GameObject go = new GameObject("[ToastManager]");
        DontDestroyOnLoad(go);
        s_instance = go.AddComponent<ToastManager>();
        return s_instance;
    }

    private void Awake()
    {
        s_instance = this;
    }

    private void SpawnToast(string text, ToastKind kind)
    {
        EnsureContainer();
        if (container == null) return;

        GameObject go = new GameObject("Toast");
        RectTransform rt = go.AddComponent<RectTransform>();
        rt.SetParent(container, false);
        rt.anchorMin = new Vector2(1f, 1f);
        rt.anchorMax = new Vector2(1f, 1f);
        rt.pivot = new Vector2(1f, 1f);
        rt.sizeDelta = new Vector2(420f, TOAST_HEIGHT);
        rt.anchoredPosition = new Vector2(500f, -(liveToasts.Count * (TOAST_HEIGHT + TOAST_SPACING)));

        // Background pill — solid dark with a thin coloured accent edge
        // that signals the toast type (achievement = gold, save = blue,
        // warning = red, etc.).
        Image bg = go.AddComponent<Image>();
        bg.color = new Color(0.05f, 0.05f, 0.07f, 0.92f);
        bg.raycastTarget = false;

        Color accent = AccentFor(kind);

        // Left edge accent bar
        GameObject bar = new GameObject("Accent");
        RectTransform barRT = bar.AddComponent<RectTransform>();
        barRT.SetParent(rt, false);
        barRT.anchorMin = new Vector2(0f, 0f);
        barRT.anchorMax = new Vector2(0f, 1f);
        barRT.pivot = new Vector2(0f, 0.5f);
        barRT.anchoredPosition = Vector2.zero;
        barRT.sizeDelta = new Vector2(5f, 0f);
        Image barImg = bar.AddComponent<Image>();
        barImg.color = accent;
        barImg.raycastTarget = false;

        // Text
        GameObject txtGO = new GameObject("Text");
        RectTransform txtRT = txtGO.AddComponent<RectTransform>();
        txtRT.SetParent(rt, false);
        txtRT.anchorMin = new Vector2(0f, 0f);
        txtRT.anchorMax = new Vector2(1f, 1f);
        txtRT.offsetMin = new Vector2(14f, 6f);
        txtRT.offsetMax = new Vector2(-12f, -6f);

        TextMeshProUGUI tmp = txtGO.AddComponent<TextMeshProUGUI>();
        tmp.text = text;
        tmp.alignment = TextAlignmentOptions.MidlineLeft;
        tmp.fontSize = 18f;
        tmp.fontStyle = FontStyles.Bold;
        tmp.color = new Color(1f, 0.97f, 0.88f, 1f);
        tmp.outlineWidth = 0.18f;
        tmp.outlineColor = Color.black;
        tmp.raycastTarget = false;

        liveToasts.Add(rt);
        StartCoroutine(AnimateToast(rt, bg, barImg, tmp));

        // Optional click of audio cue for the toast.
        if (AudioManager.Instance != null)
        {
            string sfx = kind == ToastKind.Achievement ? AudioID.UI_LevelUp : AudioID.UI_Click;
            AudioManager.Instance.PlayUI(sfx);
        }
    }

    private System.Collections.IEnumerator AnimateToast(RectTransform rt, Image bg, Image bar, TextMeshProUGUI tmp)
    {
        const float SLIDE_IN = 0.35f;
        const float HOLD     = 2.5f;
        const float FADE_OUT = 0.55f;

        // Slide in.
        Vector2 startPos = rt.anchoredPosition;
        Vector2 endPos = new Vector2(-12f, rt.anchoredPosition.y);
        Restack();
        endPos.y = rt.anchoredPosition.y;

        float t = 0f;
        while (t < SLIDE_IN)
        {
            t += Time.unscaledDeltaTime;
            float k = Mathf.Clamp01(t / SLIDE_IN);
            float ease = 1f - Mathf.Pow(1f - k, 3f);
            rt.anchoredPosition = Vector2.Lerp(startPos, endPos, ease);
            yield return null;
        }
        rt.anchoredPosition = endPos;

        yield return new WaitForSecondsRealtime(HOLD);

        // Fade out + drift right.
        Vector2 fadeStart = rt.anchoredPosition;
        Vector2 fadeEnd = new Vector2(fadeStart.x + 90f, fadeStart.y);
        Color bgC = bg.color;
        Color barC = bar.color;
        Color txtC = tmp.color;

        t = 0f;
        while (t < FADE_OUT)
        {
            t += Time.unscaledDeltaTime;
            float k = Mathf.Clamp01(t / FADE_OUT);
            float a = 1f - k;
            bg.color = new Color(bgC.r, bgC.g, bgC.b, bgC.a * a);
            bar.color = new Color(barC.r, barC.g, barC.b, barC.a * a);
            tmp.color = new Color(txtC.r, txtC.g, txtC.b, txtC.a * a);
            rt.anchoredPosition = Vector2.Lerp(fadeStart, fadeEnd, k);
            yield return null;
        }

        liveToasts.Remove(rt);
        Destroy(rt.gameObject);
        Restack();
    }

    private void Restack()
    {
        // Top toast at 0, then stack downward.
        for (int i = 0; i < liveToasts.Count; i++)
        {
            RectTransform rt = liveToasts[i];
            if (rt == null) continue;
            Vector2 p = rt.anchoredPosition;
            p.y = Mathf.Lerp(p.y, -(i * (TOAST_HEIGHT + TOAST_SPACING)), 0.6f);
            rt.anchoredPosition = p;
        }
    }

    private Color AccentFor(ToastKind kind)
    {
        switch (kind)
        {
            case ToastKind.Achievement: return new Color(1f, 0.84f, 0.28f);
            case ToastKind.Save:        return new Color(0.4f, 0.75f, 1f);
            case ToastKind.Warning:     return new Color(1f, 0.4f, 0.35f);
            case ToastKind.Lore:        return new Color(0.85f, 0.6f, 1f);
            default:                    return new Color(0.8f, 0.85f, 0.95f);
        }
    }

    private void EnsureContainer()
    {
        if (container != null) return;
        GlobalHUD hud = GlobalHUD.Instance;
        if (hud == null) return;
        RectTransform hudRect = hud.GetComponent<RectTransform>();
        if (hudRect == null) return;

        GameObject go = new GameObject("ToastStack");
        RectTransform rt = go.AddComponent<RectTransform>();
        rt.SetParent(hudRect, false);
        rt.anchorMin = new Vector2(1f, 1f);
        rt.anchorMax = new Vector2(1f, 1f);
        rt.pivot = new Vector2(1f, 1f);
        rt.anchoredPosition = new Vector2(-30f, -100f);
        rt.sizeDelta = new Vector2(420f, 420f);
        container = rt;
    }
}
