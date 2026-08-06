using System.Reflection;
using UnityEngine;

// Runtime-set UI (mission plate title, building info panel, shop item
// detail, barracks tabs, level-up cards) is written once via a Setup/
// Refresh/UpdateUIData method — flipping language in the settings menu
// left them stale until the player reopened the panel.
//
// This bootstrapper listens for LocalizationManager.OnLanguageChanged
// and, without hard-coupling to every panel, uses reflection to invoke
// any parameterless `RefreshLocalisation`, `Refresh`, `UpdateUI`, or
// `RefreshUIData` method on every active MonoBehaviour in the loaded
// scenes. Panels that don't have one silently skip.
//
// Reflection cost: only when the player actually changes language
// (rare, and once), scanning components by type — no per-frame hit.
public static class LocalizationLiveRefresh
{
    // Ordered by preference — the first method found on a given type
    // is invoked. Names cover every existing convention in the project.
    private static readonly string[] s_methodNames =
    {
        "RefreshLocalisation",
        "RefreshLocalization",
        "OnLanguageRefresh",
        "RefreshUIData",
        "UpdateUIData",
        "RefreshUI",
        "Refresh",
    };

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void Bootstrap()
    {
        LocalizationManager.OnLanguageChanged -= BroadcastRefresh;
        LocalizationManager.OnLanguageChanged += BroadcastRefresh;
    }

    private static void BroadcastRefresh()
    {
        // Include inactive so panels currently hidden also get pre-loaded
        // with the new language and don't flash English on next open.
        MonoBehaviour[] all = Object.FindObjectsByType<MonoBehaviour>(
            FindObjectsInactive.Include, FindObjectsSortMode.None);

        int refreshed = 0;
        for (int i = 0; i < all.Length; i++)
        {
            var mb = all[i];
            if (mb == null) continue;
            System.Type t = mb.GetType();
            MethodInfo method = null;
            for (int m = 0; m < s_methodNames.Length; m++)
            {
                method = t.GetMethod(s_methodNames[m],
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance,
                    null, System.Type.EmptyTypes, null);
                if (method != null) break;
            }
            if (method == null) continue;
            try
            {
                method.Invoke(mb, null);
                refreshed++;
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"[LocalizationLiveRefresh] {t.Name}.{method.Name}() threw: {e.Message}");
            }
        }
        GameLog.Info($"[LocalizationLiveRefresh] Language changed — refreshed {refreshed} panels.");
    }
}
