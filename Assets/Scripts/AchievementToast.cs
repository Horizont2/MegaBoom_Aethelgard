using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

// Runtime-built achievement toast — no scene wiring needed.
//
// AchievementSystem.Unlock() calls AchievementToast.Show(title,
// description) which drops a Steam-style gold banner in the top-right
// for a few seconds. Persists across scene loads, ignores Time.timeScale
// (so unlocks during a hitstop still animate), and stacks up to 4
// toasts vertically before recycling.
//
// If a scene has a wired ToastUIController it still gets the event via
// ToastManager.Show — this is additive, not a replacement.
public class AchievementToast : MonoBehaviour
{
    private const int MAX_ON_SCREEN = 4;
    private const float HOLD_SECONDS = 4.5f;
    private const float SLIDE_SECONDS = 0.35f;
    private const float FADE_SECONDS = 0.5f;

    private static AchievementToast s_instance;
    private static Transform s_container;
    private static readonly List<GameObject> s_live = new List<GameObject>(MAX_ON_SCREEN);

    public static void Show(string title, string description)
    {
        EnsureInstance();
        // Recycle oldest if we're at cap.
        while (s_live.Count >= MAX_ON_SCREEN)
        {
            var oldest = s_live[0];
            s_live.RemoveAt(0);
            if (oldest != null) Destroy(oldest);
        }
        var go = Build(title, description);
        s_live.Add(go);
        s_instance.StartCoroutine(s_instance.Lifecycle(go));

        // Also fan out through the existing headless bus so a scene
        // that authored its own ToastUIController still gets the event.
        ToastManager.Show(title, ToastManager.ToastKind.Achievement);
    }

    private static void EnsureInstance()
    {
        if (s_instance != null) return;
        var go = new GameObject("[AchievementToast]");
        DontDestroyOnLoad(go);
        s_instance = go.AddComponent<AchievementToast>();

        var canvas = go.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 32000;
        var scaler = go.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;
        go.AddComponent<GraphicRaycaster>();

        var containerGO = new GameObject("Container", typeof(RectTransform));
        containerGO.transform.SetParent(go.transform, false);
        var cRT = containerGO.GetComponent<RectTransform>();
        cRT.anchorMin = new Vector2(1f, 1f);
        cRT.anchorMax = new Vector2(1f, 1f);
        cRT.pivot = new Vector2(1f, 1f);
        cRT.anchoredPosition = new Vector2(-40f, -40f);
        cRT.sizeDelta = new Vector2(500f, 500f);
        var layout = containerGO.AddComponent<VerticalLayoutGroup>();
        layout.childAlignment = TextAnchor.UpperRight;
        layout.spacing = 12f;
        layout.childControlHeight = false;
        layout.childControlWidth = false;
        layout.childForceExpandHeight = false;
        layout.childForceExpandWidth = false;
        s_container = containerGO.transform;
    }

    private static GameObject Build(string title, string description)
    {
        var toast = new GameObject("Toast", typeof(Image), typeof(CanvasGroup));
        toast.transform.SetParent(s_container, false);
        var rt = toast.GetComponent<RectTransform>();
        rt.sizeDelta = new Vector2(460f, 96f);
        var bg = toast.GetComponent<Image>();
        bg.color = new Color(0.09f, 0.09f, 0.11f, 0.96f);

        // gold accent bar on the left
        var accent = new GameObject("Accent", typeof(Image));
        accent.transform.SetParent(toast.transform, false);
        var aRT = accent.GetComponent<RectTransform>();
        aRT.anchorMin = new Vector2(0f, 0f);
        aRT.anchorMax = new Vector2(0f, 1f);
        aRT.pivot = new Vector2(0f, 0.5f);
        aRT.sizeDelta = new Vector2(6f, 0f);
        aRT.anchoredPosition = Vector2.zero;
        accent.GetComponent<Image>().color = new Color(1f, 0.84f, 0.28f, 1f);

        // "ACHIEVEMENT UNLOCKED" heading
        var headGO = new GameObject("Head", typeof(TextMeshProUGUI));
        headGO.transform.SetParent(toast.transform, false);
        var head = headGO.GetComponent<TextMeshProUGUI>();
        head.text = LocalizationManager.Tr("ACHIEVEMENT UNLOCKED");
        head.fontSize = 16f;
        head.color = new Color(1f, 0.84f, 0.28f, 1f);
        head.fontStyle = FontStyles.UpperCase | FontStyles.Bold;
        head.alignment = TextAlignmentOptions.Left;
        var hRT = head.GetComponent<RectTransform>();
        hRT.anchorMin = new Vector2(0f, 1f);
        hRT.anchorMax = new Vector2(1f, 1f);
        hRT.pivot = new Vector2(0f, 1f);
        hRT.anchoredPosition = new Vector2(20f, -10f);
        hRT.sizeDelta = new Vector2(-30f, 24f);

        // Title
        var titleGO = new GameObject("Title", typeof(TextMeshProUGUI));
        titleGO.transform.SetParent(toast.transform, false);
        var titleTMP = titleGO.GetComponent<TextMeshProUGUI>();
        titleTMP.text = title;
        titleTMP.fontSize = 26f;
        titleTMP.color = new Color(0.96f, 0.94f, 0.88f, 1f);
        titleTMP.alignment = TextAlignmentOptions.Left;
        titleTMP.enableWordWrapping = false;
        titleTMP.overflowMode = TextOverflowModes.Ellipsis;
        var tRT = titleTMP.GetComponent<RectTransform>();
        tRT.anchorMin = new Vector2(0f, 0f);
        tRT.anchorMax = new Vector2(1f, 1f);
        tRT.pivot = new Vector2(0f, 0.5f);
        tRT.anchoredPosition = new Vector2(20f, -4f);
        tRT.sizeDelta = new Vector2(-30f, -32f);

        // Slide-in start offset — handled by lifecycle.
        toast.GetComponent<CanvasGroup>().alpha = 0f;
        return toast;
    }

    private IEnumerator Lifecycle(GameObject go)
    {
        if (go == null) yield break;
        var cg = go.GetComponent<CanvasGroup>();
        var rt = go.GetComponent<RectTransform>();
        Vector2 endPos = rt.anchoredPosition;
        Vector2 startPos = endPos + new Vector2(80f, 0f);
        rt.anchoredPosition = startPos;

        // Slide + fade in
        for (float t = 0f; t < SLIDE_SECONDS; t += Time.unscaledDeltaTime)
        {
            if (go == null) yield break;
            float p = Mathf.Clamp01(t / SLIDE_SECONDS);
            float e = 1f - (1f - p) * (1f - p);
            rt.anchoredPosition = Vector2.LerpUnclamped(startPos, endPos, e);
            cg.alpha = e;
            yield return null;
        }
        if (go == null) yield break;
        rt.anchoredPosition = endPos;
        cg.alpha = 1f;

        yield return new WaitForSecondsRealtime(HOLD_SECONDS);
        if (go == null) yield break;

        // Fade out
        for (float t = 0f; t < FADE_SECONDS; t += Time.unscaledDeltaTime)
        {
            if (go == null) yield break;
            cg.alpha = 1f - Mathf.Clamp01(t / FADE_SECONDS);
            yield return null;
        }
        s_live.Remove(go);
        if (go != null) Destroy(go);
    }
}
