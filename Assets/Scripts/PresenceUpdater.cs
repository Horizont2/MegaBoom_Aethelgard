using UnityEngine;
using UnityEngine.SceneManagement;

// Turns scene loads into Steam Rich Presence + Analytics events.
// Auto-created at runtime, persistent, no scene wiring.
//
// Presence strings are deliberately short and neutral (no spoilers) so
// they read cleanly in a Steam friend's list.
public static class PresenceUpdater
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        SceneManager.sceneLoaded += OnSceneLoaded;
        // Fire once for the currently-active scene so a player who
        // launched directly into a level still gets presence.
        OnSceneLoaded(SceneManager.GetActiveScene(), LoadSceneMode.Single);
    }

    private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        string status = StatusFor(scene.name);
        SteamManager.SetRichPresence(status);
        Analytics.Event("scene_loaded", "scene", scene.name);
    }

    private static string StatusFor(string sceneName)
    {
        if (string.IsNullOrEmpty(sceneName)) return "In Aethelgard";
        switch (sceneName)
        {
            case "Menu":
            case "MainMenu":  return "In the Main Menu";
            case "CampScene": return "In Camp";
            case "ShopScene": return "At the Shop";
            case "GameScene": return "On a Mission";
            case "Lvl_1":     return "In the Prologue";
            default:          return "In " + sceneName;
        }
    }
}
