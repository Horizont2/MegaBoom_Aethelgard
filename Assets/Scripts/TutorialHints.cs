using UnityEngine;
using System.Collections;
using System.Collections.Generic;

/// <summary>
/// Centralized "first-time-you-need-it" hint system. Call ShowIfNew(key, message)
/// from gameplay code at the moment a mechanic becomes relevant; each key only
/// fires once per save profile.
/// </summary>
public class TutorialHints : MonoBehaviour
{
    public static TutorialHints Instance { get; private set; }

    [Header("Display")]
    [Tooltip("За скільки секунд хінт фейдиться")]
    public float defaultDuration = 5f;
    [Tooltip("Скільки секунд мінімум між двома хінтами")]
    public float minSpacing = 1.5f;
    [Tooltip("Префікс, що додається до тексту, щоб хінт відрізнявся від інших prompt-ів")]
    public string tipPrefix = "<color=#FFD86B>TIP:</color> ";

    [Header("Debug")]
    [Tooltip("Скинути усі ключі при старті (для тестування)")]
    public bool resetOnPlay = false;

    private float lastShownAt = -100f;
    private Coroutine currentRoutine;
    private readonly Queue<HintRequest> queue = new Queue<HintRequest>();

    private struct HintRequest
    {
        public string key;
        public string message;
        public float duration;
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

    public bool HasSeen(string key)
    {
        return PlayerPrefs.GetInt(StorageKey(key), 0) == 1;
    }

    /// <summary>
    /// Queues a hint to show only if its key has never been displayed before.
    /// Safe to call every frame — duplicates and already-seen hints are ignored.
    /// </summary>
    public void ShowIfNew(string key, string message, float duration = -1f)
    {
        if (string.IsNullOrEmpty(key)) return;
        if (HasSeen(key)) return;

        PlayerPrefs.SetInt(StorageKey(key), 1);
        PlayerPrefs.Save();

        Enqueue(new HintRequest
        {
            key = key,
            message = message,
            duration = duration > 0f ? duration : defaultDuration
        });
    }

    /// <summary>Forces a hint to show even if its key was already marked seen. Use for reminders/debug.</summary>
    public void ShowAlways(string key, string message, float duration = -1f)
    {
        Enqueue(new HintRequest
        {
            key = key,
            message = message,
            duration = duration > 0f ? duration : defaultDuration
        });
    }

    public void ResetAll()
    {
        // Best-effort: PlayerPrefs doesn't enumerate, so we delete the bookkeeping
        // index. Individual TutShown_* keys remain — a clean slate is achieved
        // by clearing PlayerPrefs from outside (debug menu).
        PlayerPrefs.DeleteKey(IndexKey);
        PlayerPrefs.Save();
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
        if (GlobalHUD.Instance == null) yield break;

        string text = tipPrefix + req.message;
        GlobalHUD.Instance.ShowPrompt(text);
        yield return new WaitForSecondsRealtime(req.duration);
        GlobalHUD.Instance.HidePrompt();
    }

    private const string IndexKey = "TutHints_Index";
    private static string StorageKey(string key) => "TutShown_" + key;
}
