using System.Collections.Generic;
using UnityEngine;
using TMPro;
using UnityEngine.UI;

// Bulk localiser. Attach to a UI root (Canvas, panel, screen) and on
// Enable it auto-discovers every TMP_Text / TextMeshProUGUI / legacy
// Text in its subtree, takes the CURRENT text as the localisation key,
// and re-pulls translations every time LocalizationManager fires
// OnLanguageChanged.
//
// Excludes:
//   * Texts whose GameObject is tagged with ignoreTag (default
//     "DontLocalize") — use this for game title, player names,
//     dynamic counters, etc.
//   * Any TextMeshProUGUI that already has a LocalizedText sibling
//     (those are explicitly authored with a key — leave them alone).
//
// Designer flow:
//   1. Add a SETTINGS / PauseMenu / MainMenu canvas.
//   2. Drop AutoLocalize on the root.
//   3. Make sure LocalizationManager.Seed() has entries for the visible
//      text values used as keys (e.g. Add("CONTINUE", "CONTINUE",
//      "ПРОДОВЖИТИ");). Missing keys fall through to the original text
//      so nothing breaks if a translation isn't ready yet.
[DisallowMultipleComponent]
public class AutoLocalize : MonoBehaviour
{
    // On every scene load, walk every top-level Canvas in the scene and
    // attach AutoLocalize automatically so designers don't have to add
    // the component manually to every menu, pause panel, codex, etc.
    // Anything that doesn't want to be localised — game title, dynamic
    // counters, player-typed names — should tag its GameObject
    // "DontLocalize" and AutoLocalize skips it.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void AutoAttachToCanvases()
    {
        UnityEngine.SceneManagement.SceneManager.sceneLoaded -= OnSceneLoadedStatic;
        UnityEngine.SceneManagement.SceneManager.sceneLoaded += OnSceneLoadedStatic;
        AttachToAllCanvasesInScene();
    }

    private static void OnSceneLoadedStatic(UnityEngine.SceneManagement.Scene s, UnityEngine.SceneManagement.LoadSceneMode m)
    {
        AttachToAllCanvasesInScene();
    }

    private static void AttachToAllCanvasesInScene()
    {
        Canvas[] canvases = Object.FindObjectsByType<Canvas>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        foreach (var c in canvases)
        {
            if (c == null) continue;
            // Only attach to root canvases — children inherit text discovery
            // via GetComponentsInChildren from the root component.
            if (!c.isRootCanvas) continue;
            if (c.GetComponent<AutoLocalize>() != null) continue;
            c.gameObject.AddComponent<AutoLocalize>();
        }
    }

    [Tooltip("GameObjects with this tag are skipped — use for game name, dynamic counters, player input. Leave empty to localise everything.")]
    public string ignoreTag = "DontLocalize";

    private readonly List<(TMP_Text tmp, string key)> tmpTargets = new List<(TMP_Text, string)>();
    private readonly List<(Text legacy, string key)> legacyTargets = new List<(Text, string)>();
    private bool captured;

    private void OnEnable()
    {
        if (!captured) Capture();
        LocalizationManager.OnLanguageChanged += ApplyAll;
        ApplyAll();
    }

    private void OnDisable()
    {
        LocalizationManager.OnLanguageChanged -= ApplyAll;
    }

    [ContextMenu("Re-Capture Texts")]
    public void Recapture()
    {
        captured = false;
        tmpTargets.Clear();
        legacyTargets.Clear();
        Capture();
        ApplyAll();
    }

    private void Capture()
    {
        captured = true;
        TMP_Text[] tmps = GetComponentsInChildren<TMP_Text>(true);
        foreach (var t in tmps)
        {
            if (t == null) continue;
            if (!string.IsNullOrEmpty(ignoreTag) && t.gameObject.CompareTag(ignoreTag)) continue;
            if (t.GetComponent<LocalizedText>() != null) continue;
            string key = (t.text ?? "").Trim();
            if (string.IsNullOrEmpty(key)) continue;
            tmpTargets.Add((t, key));
        }
        Text[] legacy = GetComponentsInChildren<Text>(true);
        foreach (var t in legacy)
        {
            if (t == null) continue;
            if (!string.IsNullOrEmpty(ignoreTag) && t.gameObject.CompareTag(ignoreTag)) continue;
            string key = (t.text ?? "").Trim();
            if (string.IsNullOrEmpty(key)) continue;
            legacyTargets.Add((t, key));
        }
    }

    private void ApplyAll()
    {
        foreach (var (tmp, key) in tmpTargets)
        {
            if (tmp == null) continue;
            tmp.text = LocalizationManager.Tr(key);
        }
        foreach (var (legacy, key) in legacyTargets)
        {
            if (legacy == null) continue;
            legacy.text = LocalizationManager.Tr(key);
        }
    }
}
