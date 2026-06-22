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
        OnToastRequested?.Invoke(new ToastRequest { text = text, kind = kind });
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
