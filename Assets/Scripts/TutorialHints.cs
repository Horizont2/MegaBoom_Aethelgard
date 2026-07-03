using UnityEngine;
using System.Collections;
using System.Collections.Generic;

/// <summary>
/// Centralized "first-time-you-need-it" hint system.
///
/// Two display paths are supported:
///   1. Beautiful path: TutorialHintLibrary asset + TutorialPanelUI in scene.
///      Looks up TutorialHintData by key and shows it on the styled panel,
///      including an optional looping video clip (AC-style guide).
///   2. Fallback path: GlobalHUD.ShowPrompt with a "TIP:" prefix when no
///      library/panel is set up. The call site can pass a fallback message.
///
/// Each key fires once per save profile (PlayerPrefs gated).
/// </summary>
public class TutorialHints : MonoBehaviour
{
    public static TutorialHints Instance { get; private set; }

    [Header("Content")]
    [Tooltip("Перетягни сюди TutorialHintLibrary asset. Без нього хінти йдуть через GlobalHUD.ShowPrompt.")]
    public TutorialHintLibrary library;

    [Header("Display")]
    public float defaultDuration = 5f;
    public float minSpacing = 1.5f;
    [Tooltip("Префікс, що додається у fallback-режимі (коли немає TutorialPanelUI)")]
    public string fallbackPrefix = "<color=#FFD86B>TIP:</color> ";

    [Header("Debug")]
    public bool resetOnPlay = false;
    [Tooltip("Друкувати кожен показаний/пропущений хінт у Console")]
    public bool verboseLogging = false;

    private float lastShownAt = -100f;
    private Coroutine currentRoutine;
    private readonly Queue<HintRequest> queue = new Queue<HintRequest>();

    // Cross-system flag: true while any hint (panel OR fallback prompt)
    // is on screen. Cutscene skip handlers gate Space/Enter/Escape on
    // this so dismissing a hint mid-cutscene doesn't double up as a
    // cutscene skip. Also used by PlayerController to freeze movement.
    public static bool IsAnyHintShowing { get; private set; }

    private struct HintRequest
    {
        public string key;
        public string fallbackBody;
        public float fallbackDuration;
    }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        DontDestroyOnLoad(gameObject);

        if (resetOnPlay) ResetAll();
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void EnsureExists()
    {
        if (Instance != null) return;
        GameObject go = new GameObject("[TutorialHints]");
        go.AddComponent<TutorialHints>();
    }

    public bool HasSeen(string key) => PlayerPrefs.GetInt(StorageKey(key), 0) == 1;

    /// <summary>
    /// Queue a hint by key. If a TutorialHintLibrary is wired and contains
    /// the key, the styled panel shows. Otherwise the fallback body is
    /// shown via GlobalHUD.ShowPrompt with the TIP prefix.
    /// </summary>
    public void ShowIfNew(string key, string fallbackBody = null, float fallbackDuration = -1f)
    {
        if (string.IsNullOrEmpty(key)) return;
        // Settings_TutorialHints toggle — silently drop every hint
        // request when the player has tutorials disabled. Default = on.
        if (PlayerPrefs.GetInt("Settings_TutorialHints", 1) != 1)
        {
            if (verboseLogging) Debug.Log($"[TutorialHints] Skip '{key}' (disabled in settings).");
            return;
        }
        if (HasSeen(key))
        {
            if (verboseLogging) Debug.Log($"[TutorialHints] Skip '{key}' (already seen).");
            return;
        }

        PlayerPrefs.SetInt(StorageKey(key), 1);
        PlayerPrefs.Save();

        Enqueue(new HintRequest
        {
            key = key,
            fallbackBody = fallbackBody,
            fallbackDuration = fallbackDuration > 0f ? fallbackDuration : defaultDuration
        });
    }

    /// <summary>Force-show a hint even if already seen. Useful for reminders.</summary>
    public void ShowAlways(string key, string fallbackBody = null, float fallbackDuration = -1f)
    {
        Enqueue(new HintRequest
        {
            key = key,
            fallbackBody = fallbackBody,
            fallbackDuration = fallbackDuration > 0f ? fallbackDuration : defaultDuration
        });
    }

    /// <summary>Clear the "seen" flag for a single key. For per-mechanic reset.</summary>
    public void ResetKey(string key)
    {
        PlayerPrefs.DeleteKey(StorageKey(key));
        PlayerPrefs.Save();
    }

    /// <summary>Clear seen flags for every hint in the wired library.</summary>
    [ContextMenu("Reset all hint flags")]
    public void ResetAll()
    {
        if (library == null || library.hints == null)
        {
            Debug.LogWarning("[TutorialHints] No library wired — nothing to reset. " +
                             "Drop a TutorialHintLibrary asset into the 'library' field first.", this);
            return;
        }
        int count = 0;
        for (int i = 0; i < library.hints.Length; i++)
        {
            TutorialHintData d = library.hints[i];
            if (d != null && !string.IsNullOrEmpty(d.key))
            {
                PlayerPrefs.DeleteKey(StorageKey(d.key));
                count++;
            }
        }
        PlayerPrefs.Save();
        Debug.Log($"[TutorialHints] Reset {count} hint flag(s). All hints will replay on their next trigger.");
    }

    private void Enqueue(HintRequest req)
    {
        queue.Enqueue(req);
        if (currentRoutine == null) currentRoutine = StartCoroutine(DrainRoutine());
    }

    private IEnumerator DrainRoutine()
    {
        while (queue.Count > 0)
        {
            float since = Time.unscaledTime - lastShownAt;
            if (since < minSpacing)
                yield return new WaitForSecondsRealtime(minSpacing - since);

            HintRequest req = queue.Dequeue();
            yield return ShowOne(req);
            lastShownAt = Time.unscaledTime;
        }
        currentRoutine = null;
    }

    private IEnumerator ShowOne(HintRequest req)
    {
        TutorialHintData data = library != null ? library.FindByKey(req.key) : null;

        // ==========================================
        // ФІКС: Створюємо дані "на льоту", якщо їх немає в бібліотеці, але передано текст.
        // Це дозволить Path 1 (TutorialPanelUI) підхопити підказку!
        // ==========================================
        if (data == null && !string.IsNullOrEmpty(req.fallbackBody))
        {
            data = ScriptableObject.CreateInstance<TutorialHintData>();
            data.key = req.key;
            data.title = "TIP";
            data.body = req.fallbackBody;
            data.duration = req.fallbackDuration;
            data.waitForInput = false;
        }

        // Flip the shared "any hint visible" flag for the entire
        // display window so cutscene skip handlers and player movement
        // know to ignore inputs until the hint is dismissed.
        IsAnyHintShowing = true;
        try
        {

            // Path 1: styled panel
            if (data != null && TutorialPanelUI.Instance != null)
            {
                if (verboseLogging) Debug.Log($"[TutorialHints] Show '{req.key}' via TutorialPanelUI.");
                TutorialPanelUI.Instance.Show(data);

                float waitFor = data.waitForInput ? 60f : data.duration + 1f;
                float deadline = Time.unscaledTime + waitFor;
                while (TutorialPanelUI.Instance != null && TutorialPanelUI.Instance.IsVisible && Time.unscaledTime < deadline)
                    yield return null;
                yield break;
            }

            // Path 2: data exists but no panel — feed body into the prompt at least
            if (data != null && GlobalHUD.Instance != null)
            {
                string text = fallbackPrefix + (string.IsNullOrEmpty(data.body) ? data.title : data.body);
                GlobalHUD.Instance.ShowPrompt(text);
                yield return new WaitForSecondsRealtime(data.duration);
                GlobalHUD.Instance.HidePrompt();
                yield break;
            }

            // Path 3: no library entry — fall back to whatever the call site passed
            if (GlobalHUD.Instance != null && !string.IsNullOrEmpty(req.fallbackBody))
            {
                string text = fallbackPrefix + req.fallbackBody;
                GlobalHUD.Instance.ShowPrompt(text);
                yield return new WaitForSecondsRealtime(req.fallbackDuration);
                GlobalHUD.Instance.HidePrompt();
                yield break;
            }

            if (verboseLogging)
                Debug.LogWarning($"[TutorialHints] No display path for '{req.key}' (no library entry, no fallback body, or no GlobalHUD).");

        }
        finally
        {
            IsAnyHintShowing = false;
        }
    }

    private static string StorageKey(string key) => "TutShown_" + key;
}