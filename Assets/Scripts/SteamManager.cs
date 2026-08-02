using UnityEngine;

// Thin wrapper around Steam initialisation + achievement / cloud
// hooks. Does NOT ship with a Steamworks package embedded — the
// project has to import Facepunch.Steamworks or Steamworks.NET
// separately. Until that happens this file compiles as a no-op stub
// so the rest of the codebase can call SteamManager.UnlockAchievement
// / SteamManager.IsRunning without conditional compilation everywhere.
//
// When you install Facepunch.Steamworks:
//   1. Add STEAMWORKS_NET (or FACEPUNCH_STEAMWORKS) to Player Settings
//      → Scripting Define Symbols.
//   2. Drop steam_appid.txt (contains your Steam App ID) into
//      Assets/StreamingAssets/ AND into the built game folder.
//   3. Replace the ##STEAM_HOOK## blocks below with real calls (see
//      the inline TODOs).
//
// Runtime shape stays the same either way, so callers never change.
public static class SteamManager
{
    private static bool s_initialised;
    private static bool s_running;

    public static bool IsRunning => s_running;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Bootstrap()
    {
        if (s_initialised) return;
        s_initialised = true;
        try
        {
#if FACEPUNCH_STEAMWORKS
            // ##STEAM_HOOK## Facepunch.Steamworks init:
            // Steamworks.SteamClient.Init(YOUR_APP_ID, asyncCallbacks: true);
            // s_running = Steamworks.SteamClient.IsValid;
            s_running = false; // remove once real init lands
#elif STEAMWORKS_NET
            // ##STEAM_HOOK## Steamworks.NET init — use the official
            // SteamManager MonoBehaviour prefab or SteamAPI.Init().
            s_running = false;
#else
            s_running = false;
            Debug.Log("[SteamManager] No Steamworks package present — running standalone. Achievements + Cloud are local-only stubs.");
#endif
        }
        catch (System.Exception e)
        {
            s_running = false;
            Debug.LogWarning($"[SteamManager] Init failed — falling back to standalone mode. {e.Message}");
        }
    }

    // -------------------------------------------------------------
    //  Achievements
    // -------------------------------------------------------------
    // AchievementSystem calls this whenever an achievement newly
    // unlocks. Steam handles idempotency internally — safe to call
    // even if already unlocked.
    public static void UnlockAchievement(string steamAchievementID)
    {
        if (string.IsNullOrEmpty(steamAchievementID)) return;
        if (!s_running) return; // silent no-op standalone
        try
        {
#if FACEPUNCH_STEAMWORKS
            // var ach = new Steamworks.Data.Achievement(steamAchievementID);
            // if (!ach.State) ach.Trigger();
#elif STEAMWORKS_NET
            // Steamworks.SteamUserStats.SetAchievement(steamAchievementID);
            // Steamworks.SteamUserStats.StoreStats();
#endif
        }
        catch (System.Exception e)
        {
            Debug.LogWarning($"[SteamManager] UnlockAchievement '{steamAchievementID}' failed: {e.Message}");
        }
    }

    // -------------------------------------------------------------
    //  Cloud save hook (no-op stub — Steam Auto-Cloud picks the
    //  save_v1.json file automatically once configured in the web
    //  dashboard, so this is mostly a signal for manual sync).
    // -------------------------------------------------------------
    public static void FlushSaveToCloud()
    {
        if (!s_running) return;
        try
        {
#if FACEPUNCH_STEAMWORKS
            // Steamworks.SteamRemoteStorage.FileWrite("save_v1.json", bytes);
#elif STEAMWORKS_NET
            // Steamworks.SteamRemoteStorage.FileWrite("save_v1.json", bytes, bytes.Length);
#endif
        }
        catch (System.Exception e)
        {
            Debug.LogWarning($"[SteamManager] FlushSaveToCloud failed: {e.Message}");
        }
    }

    // -------------------------------------------------------------
    //  Called from a "keep alive" Update on a persistent object.
    // -------------------------------------------------------------
    public static void RunCallbacks()
    {
        if (!s_running) return;
#if FACEPUNCH_STEAMWORKS
        // Steamworks.SteamClient.RunCallbacks();
#elif STEAMWORKS_NET
        // Steamworks.SteamAPI.RunCallbacks();
#endif
    }

    public static void Shutdown()
    {
        if (!s_running) return;
#if FACEPUNCH_STEAMWORKS
        // Steamworks.SteamClient.Shutdown();
#elif STEAMWORKS_NET
        // Steamworks.SteamAPI.Shutdown();
#endif
        s_running = false;
    }
}
