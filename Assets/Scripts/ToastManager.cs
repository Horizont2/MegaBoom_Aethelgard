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

// ==== TOASTS GO IN THE SAME FEED AS EVERYTHING ELSE ====
//
// There is no ToastUIController in any scene - a GUID search across every
// .unity and .prefab returns nothing - so every caller (quicksave, quickload,
// a lore entry found, an achievement unlocked, the army being full, a camp
// guide step, every mercenary campaign result) was posting to an event with no
// subscribers, and the messages were discarded.
//
// The first attempt at fixing that built its own canvas at runtime and stacked
// dark plates in the top right. It made the messages visible and it was wrong:
// the game already has a place where it tells you things, the feed in the
// bottom left that prints "+12 Wood" when you pick something up, and a second
// notification system in a second corner in a second style reads as two
// different games.
//
// So a toast is now a line in that feed, in the same container and the same
// font as a felled tree or a chest, tinted by kind. Nothing about the callers
// changes. Authoring a real ToastUIController still switches this off.
public static class FallbackToastRenderer
{
    private static bool s_controllerChecked;
    private static bool s_controllerPresent;

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

        // The menu has no HUD, and a message with nowhere to go should still
        // leave a trace rather than disappear the way these used to.
        if (GlobalHUD.Instance == null)
        {
            Debug.Log("[Toast] " + req.text);
            return;
        }

        GlobalHUD.Instance.ShowPickupPopup(req.text, TintFor(req.kind));
    }

    // Kind still reads at a glance, but as the colour of the line rather than
    // as a coloured bar on a plate of its own.
    private static Color TintFor(ToastManager.ToastKind kind)
    {
        switch (kind)
        {
            case ToastManager.ToastKind.Achievement: return new Color(0.98f, 0.82f, 0.38f);
            case ToastManager.ToastKind.Save:        return new Color(0.55f, 0.82f, 0.98f);
            case ToastManager.ToastKind.Warning:     return new Color(0.98f, 0.52f, 0.40f);
            case ToastManager.ToastKind.Lore:        return new Color(0.78f, 0.64f, 0.98f);
            default:                                  return new Color(0.93f, 0.92f, 0.89f);
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
