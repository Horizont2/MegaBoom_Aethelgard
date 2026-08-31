using UnityEngine;
using UnityEngine.SceneManagement;
using TMPro;
using UnityEngine.UI;
using System.Collections.Generic;

// Scene-wide TMP / UGUI translator.
//
// Most of the game's UI labels are authored English in the scene or
// prefab (button texts like "Achievements", "Settings", panel titles
// like "CAMP STASH", tab labels like "CONQUER REWARDS"). Those never
// pass through Tr(), so switching to Ukrainian left them stuck in
// English.
//
// This walker runs on every scene load: it scans every TMP_Text /
// TextMeshProUGUI / legacy UGUI Text, treats each text as a candidate
// loc key, and if LocalizationManager.HasKey(text) — replaces the
// label with Tr(text). Texts that aren't registered fall through
// untouched (numbers, dynamic values, user names, etc).
//
// A tiny LocalizedLabelKey component is attached to each successfully-
// translated label so a language change can re-translate from the
// ORIGINAL key (not from the already-translated Ukrainian text, which
// wouldn't map back).
public class LocalizedLabelKey : MonoBehaviour
{
    public string key;
}

// Ticker MonoBehaviour that periodically re-triggers the scene walker.
// Catches the case where a panel opens after the initial walker pass
// and resets its labels to Inspector-authored English via its own
// Refresh method — the walker's next tick puts them back on the
// active language.
public class AutoLocalizeRepeater : MonoBehaviour
{
    private const float INTERVAL = 0.5f;
    private float nextRun;

    private void Update()
    {
        if (Time.unscaledTime < nextRun) return;
        nextRun = Time.unscaledTime + INTERVAL;
        AutoLocalizeScene.RefreshAllScenesPublic();
    }
}

public static class AutoLocalizeScene
{
    private static readonly List<TMP_Text> s_tmpBuffer = new List<TMP_Text>(256);
    private static readonly List<Text> s_uguiBuffer = new List<Text>(64);

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
        LocalizationManager.OnLanguageChanged -= RefreshAllScenes;
        LocalizationManager.OnLanguageChanged += RefreshAllScenes;

        // Also translate whatever's already loaded (the bootstrap fires
        // AFTER SceneLoad in the initial scene, so its sceneLoaded event
        // has already come and gone).
        RefreshAllScenes();

        // Panels open, refresh themselves, and overwrite the walker's
        // translation with their Inspector-authored English text —
        // Settings/Quit/etc. flashed back to English right after the
        // walker's initial pass. Ticker re-scans every 500ms so any
        // reset gets caught within one frame of the player noticing.
        // The scan is cheap: HasKey is O(1), most labels early-out on
        // the stored-key path, so ~200 TMPs cost well under 1ms.
        if (s_ticker == null)
        {
            var go = new GameObject("[AutoLocalizeScene.Ticker]");
            Object.DontDestroyOnLoad(go);
            s_ticker = go.AddComponent<AutoLocalizeRepeater>();
        }
    }

    private static AutoLocalizeRepeater s_ticker;

    private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        TranslateScene(scene);
    }

    private static void RefreshAllScenes()
    {
        for (int i = 0; i < SceneManager.sceneCount; i++)
        {
            var s = SceneManager.GetSceneAt(i);
            if (s.isLoaded) TranslateScene(s);
        }
    }

    // Exposed for the AutoLocalizeRepeater ticker.
    public static void RefreshAllScenesPublic() => RefreshAllScenes();

    private static void TranslateScene(Scene scene)
    {
        // Gather all labels — include inactive so panels hidden at
        // load-time still get translated before the player opens them.
        var roots = scene.GetRootGameObjects();
        s_tmpBuffer.Clear();
        s_uguiBuffer.Clear();
        for (int r = 0; r < roots.Length; r++)
        {
            roots[r].GetComponentsInChildren(true, s_tmpBuffer);
            for (int i = 0; i < s_tmpBuffer.Count; i++) TryTranslateTMP(s_tmpBuffer[i]);
            s_tmpBuffer.Clear();

            roots[r].GetComponentsInChildren(true, s_uguiBuffer);
            for (int i = 0; i < s_uguiBuffer.Count; i++) TryTranslateUgui(s_uguiBuffer[i]);
            s_uguiBuffer.Clear();
        }
    }

    private static void TryTranslateTMP(TMP_Text label)
    {
        if (label == null) return;
        // Respect the NoAutoLocalize marker. Code-driven labels (shop upgrade
        // price, building BUILD/UPGRADE hint, notice-board mission text) set
        // their own text every refresh; without this the 0.5s repeater kept
        // re-applying Tr(capturedKey) over the live value, so the text visibly
        // flipped every half-second. CompareTag-free ancestor check.
        if (label.GetComponentInParent<NoAutoLocalize>() != null) return;
        // Already-translated labels carry the original key on a sibling
        // component. Re-use it for language changes.
        var stored = label.GetComponent<LocalizedLabelKey>();
        string key = stored != null ? stored.key : label.text;
        if (string.IsNullOrWhiteSpace(key)) return;
        if (!LocalizationManager.HasKey(key)) return;
        if (stored == null)
        {
            stored = label.gameObject.AddComponent<LocalizedLabelKey>();
            stored.key = key;
        }
        string translated = LocalizationManager.Tr(key);
        if (label.text != translated) label.text = translated;
    }

    private static void TryTranslateUgui(Text label)
    {
        if (label == null) return;
        if (label.GetComponentInParent<NoAutoLocalize>() != null) return;
        var stored = label.GetComponent<LocalizedLabelKey>();
        string key = stored != null ? stored.key : label.text;
        if (string.IsNullOrWhiteSpace(key)) return;
        if (!LocalizationManager.HasKey(key)) return;
        if (stored == null)
        {
            stored = label.gameObject.AddComponent<LocalizedLabelKey>();
            stored.key = key;
        }
        string translated = LocalizationManager.Tr(key);
        if (label.text != translated) label.text = translated;
    }
}
