using System.Reflection;
using UnityEngine;
using UnityEngine.SceneManagement;
using TMPro;

// Auto-attaches a SubtitleBinding to every subtitle label in the scene
// so the player's subtitle size + background settings actually apply.
//
// The subtitle displays (Elias, CampDirector, Level1, IntroCinematic,
// CinematicTitleUI) each expose a `subtitleText` TextMeshProUGUI field.
// The audit found SubtitleBinding was attached nowhere, so those
// settings were phantom. Rather than edit five Start() methods, this
// bootstrap reflects the well-known field name on scene load and
// registers whatever it finds — new subtitle displays that follow the
// same convention are covered automatically.
public static class SubtitleAutoRegister
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
        RegisterAll();
    }

    private static void OnSceneLoaded(Scene s, LoadSceneMode m) => RegisterAll();

    private static void RegisterAll()
    {
        var all = Object.FindObjectsByType<MonoBehaviour>(
            FindObjectsInactive.Include, FindObjectsSortMode.None);
        for (int i = 0; i < all.Length; i++)
        {
            var mb = all[i];
            if (mb == null) continue;
            var f = mb.GetType().GetField("subtitleText",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (f == null || f.FieldType != typeof(TextMeshProUGUI)) continue;
            var label = f.GetValue(mb) as TextMeshProUGUI;
            if (label != null)
            {
                SubtitleSettings.Register(label);
                // Subtitle text is code-driven: the typewriter routines already
                // run each line through LocalizationManager.Tr() before typing.
                // Mark the label NoAutoLocalize so the scene walker /
                // AutoLocalizeRepeater (which re-stamps Tr(capturedText) over
                // every known-key label twice a second) can't overwrite a line
                // that's mid-typewriter with a *different* (earlier-captured)
                // subtitle line — that was the "text changes while typing" bug.
                if (label.GetComponent<NoAutoLocalize>() == null)
                    label.gameObject.AddComponent<NoAutoLocalize>();
            }
        }
    }
}
