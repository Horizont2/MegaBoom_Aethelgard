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

    // ==== A LABEL SOMEBODY ELSE WRITES TO IS NOT OURS TO MANAGE ====
    //
    // This walker captures whatever a label says at OnEnable and treats that
    // literal as its translation key, forever. That is fine for text authored
    // in the prefab and wrong for text a script fills in: the objective cards,
    // the quest lines, anything populated at runtime. On the next language
    // change ApplyAll wrote the captured key straight back over it.
    //
    // For the camp objective card the captured literal was TMP's own
    // placeholder, "New Text", so switching to German replaced a live
    // objective with "Neu Text" - the placeholder, translated. That is both
    // halves of the report at once: a card reading New Text, and other lines
    // that "do not change language" because they were never ours.
    //
    // So each target remembers what we last wrote to it. If the label no
    // longer says that, it has an owner, and we let go of it permanently.
    private sealed class Target
    {
        public TMP_Text tmp;
        public Text legacy;
        public string key;
        public string lastApplied;   // null until we have written once
    }

    private readonly List<Target> tmpTargets = new List<Target>();
    private readonly List<Target> legacyTargets = new List<Target>();
    private bool captured;

    // TMP's placeholder on a freshly added component. A label still saying
    // this was never filled in, and translating it just produces a translated
    // placeholder.
    private const string TmpPlaceholder = "New Text";

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
            // Compare tag string directly — CompareTag(name) throws when
            // the tag isn't registered in TagManager, which spammed the
            // console hundreds of times per scene before the project had
            // a DontLocalize tag set up.
            if (!string.IsNullOrEmpty(ignoreTag) && t.gameObject.tag == ignoreTag) continue;
            if (t.GetComponent<LocalizedText>() != null) continue;
            // Skip labels whose text is driven by code (marked NoAutoLocalize on
            // themselves or an ancestor) — e.g. the menu Continue/Start button.
            if (t.GetComponentInParent<NoAutoLocalize>() != null) continue;
            // Skip TMP_Dropdown's own captionText / itemText — the dropdown
            // owns them and re-renders their text every time the value
            // changes. If we capture them we end up overwriting "Polski"
            // back to "English" (whatever the caption said at capture
            // time) on OnLanguageChanged.
            if (IsPartOfDropdown(t.transform)) continue;
            string key = (t.text ?? "").Trim();
            if (string.IsNullOrEmpty(key)) continue;
            if (key == TmpPlaceholder)
            {
                Debug.LogWarning($"[AutoLocalize] '{t.name}' still says \"{TmpPlaceholder}\" - TMP's placeholder. " +
                                 "Nothing filled it in, so it is left alone rather than translated into a " +
                                 "placeholder in another language.", t);
                continue;
            }
            tmpTargets.Add(new Target { tmp = t, key = key });
        }
        Text[] legacy = GetComponentsInChildren<Text>(true);
        foreach (var t in legacy)
        {
            if (t == null) continue;
            if (!string.IsNullOrEmpty(ignoreTag) && t.gameObject.tag == ignoreTag) continue;
            if (IsPartOfDropdown(t.transform)) continue;
            if (t.GetComponentInParent<NoAutoLocalize>() != null) continue;
            string key = (t.text ?? "").Trim();
            if (string.IsNullOrEmpty(key)) continue;
            legacyTargets.Add(new Target { legacy = t, key = key });
        }
    }

    private static bool IsPartOfDropdown(Transform t)
    {
        Transform p = t;
        while (p != null)
        {
            if (p.GetComponent<TMP_Dropdown>() != null) return true;
            if (p.GetComponent<Dropdown>() != null) return true;
            p = p.parent;
        }
        return false;
    }

    private void ApplyAll()
    {
        for (int i = tmpTargets.Count - 1; i >= 0; i--)
        {
            var t = tmpTargets[i];
            if (t.tmp == null) { tmpTargets.RemoveAt(i); continue; }

            // Changed since we last wrote it, so it belongs to whatever code
            // put that there. Dropped for good - re-capturing would make the
            // runtime string the new key and translate THAT next time.
            if (t.lastApplied != null && t.tmp.text != t.lastApplied) { tmpTargets.RemoveAt(i); continue; }

            t.lastApplied = SmartTranslate(t.key);
            t.tmp.text = t.lastApplied;
        }

        for (int i = legacyTargets.Count - 1; i >= 0; i--)
        {
            var t = legacyTargets[i];
            if (t.legacy == null) { legacyTargets.RemoveAt(i); continue; }
            if (t.lastApplied != null && t.legacy.text != t.lastApplied) { legacyTargets.RemoveAt(i); continue; }

            t.lastApplied = SmartTranslate(t.key);
            t.legacy.text = t.lastApplied;
        }
    }

    // Try the full string first, then fall back to the trailing label
    // word(s) after the icon prefix. AAA category buttons render as
    // "  P   GAMEPLAY" — the dictionary key is "GAMEPLAY". If we don't
    // strip the icon, every category stays in English. Logic: split
    // by 2+ consecutive spaces and try the last non-empty token, then
    // re-prefix with the original icon span so the icon char stays
    // wherever it was.
    private static string SmartTranslate(string raw)
    {
        if (string.IsNullOrEmpty(raw)) return raw;
        string full = LocalizationManager.Tr(raw);
        // Tr returns the key literal when no entry exists. If a
        // translation actually fired, full != raw (different chars)
        // OR full == raw and raw IS an English term we shipped.
        if (full != raw) return full;

        // No exact match — try splitting "<prefix><spaces><label>".
        int lastDouble = raw.LastIndexOf("  ");
        if (lastDouble <= 0) return full;
        string label = raw.Substring(lastDouble).TrimStart();
        if (string.IsNullOrEmpty(label)) return full;
        string translated = LocalizationManager.Tr(label);
        if (translated == label) return full; // still nothing
        string prefix = raw.Substring(0, lastDouble + 2);
        return prefix + translated;
    }
}
