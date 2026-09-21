using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections;
using System.Collections.Generic;

// Headless toast bus. Gameplay code calls ToastManager.Show(text, kind)
// without knowing how it's rendered. A ToastUIController in the scene
// subscribes to OnToastRequested and spawns the visuals from your own
// prefab. If no controller is in the scene, Show is a silent no-op so
// gameplay never breaks.
public static class ToastManager
{
    public enum ToastKind { Info, Achievement, Save, Warning, Lore }

    public struct ToastRequest
    {
        public string text;
        public ToastKind kind;
    }

    // ToastUIController subscribes to this in OnEnable, unsubscribes in OnDisable.
    public static event System.Action<ToastRequest> OnToastRequested;

    public static void Show(string text, ToastKind kind = ToastKind.Info)
    {
        if (string.IsNullOrEmpty(text)) return;
        var req = new ToastRequest { text = text, kind = kind };
        OnToastRequested?.Invoke(req);
        // Does nothing when a real ToastUIController is in the scene.
        FallbackToastRenderer.Render(req);
    }
}

// ==== EVERY TOAST IN THE GAME WAS BEING THROWN AWAY ====
//
// The comment above says Show is "a silent no-op" when no ToastUIController is
// in the scene. A GUID search across every .unity and .prefab returns nothing:
// there is no controller, anywhere, and there never has been. So all twelve
// callers — quicksave, quickload, a lore entry found, an achievement unlocked,
// the army being full, a camp-guide step completed, and every mercenary
// campaign result — have been posting to an event with no subscribers.
//
// That is why a campaign's outcome could only be discovered by opening the
// world map: the toast that was supposed to announce it was discarded the
// moment it was raised.
//
// This renders them. It is deliberately plain — a stack in the top right, a
// dark plate, an accent bar in the kind's colour — because it exists to make
// the existing messages VISIBLE, not to be the final art. Authoring a real
// ToastUIController switches it off automatically; it never draws alongside
// one.
public class FallbackToastRenderer : MonoBehaviour
{
    private static FallbackToastRenderer s_instance;
    private static bool s_controllerChecked;
    private static bool s_controllerPresent;

    private RectTransform _stack;
    private readonly List<RectTransform> _live = new List<RectTransform>(6);

    private const int MaxOnScreen = 4;
    private const float Hold = 3.2f;
    private const float Fade = 0.28f;
    private const float Width = 460f;
    private const float Height = 64f;
    private const float Gap = 8f;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Hook()
    {
        UnityEngine.SceneManagement.SceneManager.sceneLoaded -= OnSceneLoaded;
        UnityEngine.SceneManagement.SceneManager.sceneLoaded += OnSceneLoaded;
        s_controllerChecked = false;
    }

    private static void OnSceneLoaded(UnityEngine.SceneManagement.Scene s, UnityEngine.SceneManagement.LoadSceneMode m)
    {
        // A controller may exist in one scene and not the next, so the answer
        // is re-derived per scene rather than cached for the session.
        s_controllerChecked = false;
    }

    public static void Render(ToastManager.ToastRequest req)
    {
        if (!s_controllerChecked)
        {
            s_controllerChecked = true;
            s_controllerPresent = Object.FindFirstObjectByType<ToastUIController>(FindObjectsInactive.Include) != null;
        }
        if (s_controllerPresent) return;

        if (s_instance == null)
        {
            var go = new GameObject("[Toasts]");
            DontDestroyOnLoad(go);
            s_instance = go.AddComponent<FallbackToastRenderer>();
            s_instance.Build(go);
        }
        s_instance.Spawn(req);
    }

    private void Build(GameObject go)
    {
        var canvas = go.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        // Above the HUD, below the victory screen (5000) and the trailer's
        // impact veil, so a toast never covers a scene transition.
        canvas.sortingOrder = 4000;

        var scaler = go.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        var stackGo = new GameObject("Stack", typeof(RectTransform));
        _stack = stackGo.GetComponent<RectTransform>();
        _stack.SetParent(go.transform, false);
        // Top right: the objective and boss bars own the top centre, the
        // prompt owns the bottom centre and resource gains the bottom left.
        _stack.anchorMin = _stack.anchorMax = new Vector2(1f, 1f);
        _stack.pivot = new Vector2(1f, 1f);
        _stack.anchoredPosition = new Vector2(-28f, -28f);
        _stack.sizeDelta = new Vector2(Width, 0f);
    }

    private void Spawn(ToastManager.ToastRequest req)
    {
        while (_live.Count >= MaxOnScreen)
        {
            if (_live[0] != null) Destroy(_live[0].gameObject);
            _live.RemoveAt(0);
        }

        var go = new GameObject("Toast", typeof(RectTransform));
        var rt = go.GetComponent<RectTransform>();
        rt.SetParent(_stack, false);
        rt.anchorMin = rt.anchorMax = new Vector2(1f, 1f);
        rt.pivot = new Vector2(1f, 1f);
        rt.sizeDelta = new Vector2(Width, Height);

        var group = go.AddComponent<CanvasGroup>();
        group.alpha = 0f;
        group.blocksRaycasts = false;
        group.interactable = false;

        var plate = go.AddComponent<Image>();
        plate.color = new Color(0.06f, 0.06f, 0.07f, 0.9f);
        plate.raycastTarget = false;

        var barGo = new GameObject("Accent", typeof(RectTransform));
        var barRt = barGo.GetComponent<RectTransform>();
        barRt.SetParent(rt, false);
        barRt.anchorMin = new Vector2(0f, 0f);
        barRt.anchorMax = new Vector2(0f, 1f);
        barRt.pivot = new Vector2(0f, 0.5f);
        barRt.sizeDelta = new Vector2(5f, 0f);
        barRt.anchoredPosition = Vector2.zero;
        var bar = barGo.AddComponent<Image>();
        bar.color = AccentFor(req.kind);
        bar.raycastTarget = false;

        var labelGo = new GameObject("Label", typeof(RectTransform));
        var labelRt = labelGo.GetComponent<RectTransform>();
        labelRt.SetParent(rt, false);
        labelRt.anchorMin = Vector2.zero;
        labelRt.anchorMax = Vector2.one;
        labelRt.offsetMin = new Vector2(18f, 6f);
        labelRt.offsetMax = new Vector2(-14f, -6f);
        var label = labelGo.AddComponent<TextMeshProUGUI>();
        label.text = req.text;
        label.fontSize = 22f;
        label.alignment = TextAlignmentOptions.MidlineLeft;
        label.overflowMode = TextOverflowModes.Ellipsis;
        label.color = new Color(0.93f, 0.92f, 0.89f);
        label.raycastTarget = false;
        // The text is already localised by the caller; do not let the canvas
        // walker re-capture it as a key and put English back.
        labelGo.AddComponent<NoAutoLocalize>();

        _live.Add(rt);
        Relayout();
        StartCoroutine(Life(rt, group));
    }

    private void Relayout()
    {
        for (int i = 0; i < _live.Count; i++)
        {
            if (_live[i] == null) continue;
            _live[i].anchoredPosition = new Vector2(0f, -i * (Height + Gap));
        }
    }

    private IEnumerator Life(RectTransform rt, CanvasGroup group)
    {
        // Unscaled throughout: a toast that announces a campaign result must
        // still be readable with the map open, the game paused, or a level-up
        // card holding timeScale at zero.
        float t = 0f;
        Vector2 home = rt.anchoredPosition;
        while (t < 1f && rt != null)
        {
            t += Time.unscaledDeltaTime / Fade;
            float k = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t));
            group.alpha = k;
            rt.anchoredPosition = home + new Vector2(Mathf.Lerp(40f, 0f, k), 0f);
            yield return null;
        }
        if (rt != null) { group.alpha = 1f; rt.anchoredPosition = home; }

        float hold = 0f;
        while (hold < Hold) { hold += Time.unscaledDeltaTime; yield return null; }

        t = 0f;
        while (t < 1f && rt != null)
        {
            t += Time.unscaledDeltaTime / Fade;
            group.alpha = 1f - Mathf.Clamp01(t);
            yield return null;
        }

        if (rt != null)
        {
            _live.Remove(rt);
            Destroy(rt.gameObject);
            Relayout();
        }
    }

    private static Color AccentFor(ToastManager.ToastKind kind)
    {
        switch (kind)
        {
            case ToastManager.ToastKind.Achievement: return new Color(0.95f, 0.78f, 0.35f);
            case ToastManager.ToastKind.Save:        return new Color(0.45f, 0.78f, 0.95f);
            case ToastManager.ToastKind.Warning:     return new Color(0.95f, 0.45f, 0.35f);
            case ToastManager.ToastKind.Lore:        return new Color(0.72f, 0.58f, 0.95f);
            default:                                  return new Color(0.7f, 0.72f, 0.74f);
        }
    }
}

// Wire ONE of these in your HUD canvas to actually render toasts.
//
// Inspector fields:
//   toastTemplate     — a deactivated prefab/template in the canvas
//                       hierarchy. Must contain a TMP_Text labelled
//                       "Label" (or assign labelOverride) and
//                       optionally an Image labelled "AccentBar" /
//                       assign accentBarOverride.
//   container         — RectTransform that newly spawned toasts are
//                       parented to. Often a VerticalLayoutGroup so the
//                       stack auto-flows; otherwise toasts get stacked
//                       at fixed offsets calculated by toastSpacing.
//   maxOnScreen       — older toasts are recycled past this count.
//
// Kind → accent color mapping is in inspector for full art control.
public class ToastUIController : MonoBehaviour
{
    [Header("Required references")]
    [Tooltip("Inactive template the controller will Instantiate per toast. Anchor / size / fonts come from this prefab.")]
    public GameObject toastTemplate;
    [Tooltip("Where instantiated toasts are parented. A VerticalLayoutGroup here will auto-stack them.")]
    public RectTransform container;

    [Header("Optional component overrides")]
    [Tooltip("If null, child named 'Label' is searched on the template.")]
    public TMP_Text labelOverride;
    [Tooltip("If null, child named 'AccentBar' is searched.")]
    public Image accentBarOverride;
    [Tooltip("If null, no audio is played on toast spawn.")]
    public AudioSource sfxSource;

    [Header("Behaviour")]
    [Tooltip("How long the toast holds at full opacity before fading out.")]
    public float holdSeconds = 2.5f;
    [Tooltip("Fade in / fade out duration.")]
    public float fadeSeconds = 0.4f;
    [Range(1, 12)] public int maxOnScreen = 5;

    [Header("Per-kind accents")]
    public Color infoAccent = new Color(0.8f, 0.85f, 0.95f);
    public Color achievementAccent = new Color(1f, 0.84f, 0.28f);
    public Color saveAccent = new Color(0.4f, 0.75f, 1f);
    public Color warningAccent = new Color(1f, 0.4f, 0.35f);
    public Color loreAccent = new Color(0.85f, 0.6f, 1f);

    private readonly List<GameObject> liveToasts = new List<GameObject>(8);

    private void OnEnable()
    {
        ToastManager.OnToastRequested += HandleRequest;
    }

    private void OnDisable()
    {
        ToastManager.OnToastRequested -= HandleRequest;
    }

    private void HandleRequest(ToastManager.ToastRequest req)
    {
        if (toastTemplate == null || container == null) return;

        // Recycle if at cap
        while (liveToasts.Count >= maxOnScreen)
        {
            GameObject oldest = liveToasts[0];
            liveToasts.RemoveAt(0);
            if (oldest != null) Destroy(oldest);
        }

        GameObject toast = Instantiate(toastTemplate, container);
        toast.SetActive(true);

        TMP_Text label = labelOverride;
        if (label == null) label = FindChildComponent<TMP_Text>(toast.transform, "Label");
        if (label != null) label.text = req.text;

        Image accent = accentBarOverride;
        if (accent == null) accent = FindChildComponent<Image>(toast.transform, "AccentBar");
        if (accent != null) accent.color = ColorFor(req.kind);

        if (sfxSource != null) sfxSource.Play();

        liveToasts.Add(toast);
        StartCoroutine(LifecycleRoutine(toast));
    }

    private IEnumerator LifecycleRoutine(GameObject toast)
    {
        CanvasGroup cg = toast.GetComponent<CanvasGroup>();
        if (cg == null) cg = toast.AddComponent<CanvasGroup>();

        // Fade in
        float t = 0f;
        cg.alpha = 0f;
        while (t < fadeSeconds)
        {
            t += Time.unscaledDeltaTime;
            cg.alpha = Mathf.Clamp01(t / fadeSeconds);
            yield return null;
        }
        cg.alpha = 1f;

        yield return new WaitForSecondsRealtime(holdSeconds);

        t = 0f;
        while (t < fadeSeconds)
        {
            t += Time.unscaledDeltaTime;
            cg.alpha = 1f - Mathf.Clamp01(t / fadeSeconds);
            yield return null;
        }

        liveToasts.Remove(toast);
        Destroy(toast);
    }

    private Color ColorFor(ToastManager.ToastKind kind)
    {
        switch (kind)
        {
            case ToastManager.ToastKind.Achievement: return achievementAccent;
            case ToastManager.ToastKind.Save:        return saveAccent;
            case ToastManager.ToastKind.Warning:     return warningAccent;
            case ToastManager.ToastKind.Lore:        return loreAccent;
            default:                                  return infoAccent;
        }
    }

    private static T FindChildComponent<T>(Transform root, string name) where T : Component
    {
        if (root == null) return null;
        foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
        {
            if (t.name == name)
            {
                T c = t.GetComponent<T>();
                if (c != null) return c;
            }
        }
        // Fallback: any T in subtree
        return root.GetComponentInChildren<T>(true);
    }
}
