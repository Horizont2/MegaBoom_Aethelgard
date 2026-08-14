using UnityEngine;

// Thin wrapper around Steam initialisation + achievement / cloud
// hooks. The project imports Facepunch.Steamworks (Assets/Plugins/
// Facepunch.Steamworks.Win64.dll + steam_api64.dll) and defines
// FACEPUNCH_STEAMWORKS in Player Settings → Scripting Define Symbols.
// Every entry point is still wrapped so the codebase can call
// SteamManager.UnlockAchievement / SteamManager.IsRunning with no
// conditional compilation at the call sites, and the game degrades to
// a standalone (Steam-less) mode if the client isn't running.
//
// ─────────────────────────────────────────────────────────────────
//  App ID
// ─────────────────────────────────────────────────────────────────
// STEAM_APP_ID is 480 (Valve's "Spacewar" test app) as a DEVELOPMENT
// PLACEHOLDER — it lets Steamworks initialise and exercise the whole
// achievement / rich-presence / cloud pipeline before the game has its
// own Steamworks App ID. When the app is registered on the Steamworks
// partner site, replace 480 with the real App ID here (one line) — and
// update steam_appid.txt to match. steam_appid.txt is only needed while
// launching OUTSIDE Steam (editor / direct exe); the shipped build gets
// its App ID injected by the Steam client, so do NOT ship steam_appid.txt
// in the final depot.
public static class SteamManager
{
    // TODO: replace 480 with the real Steamworks App ID once the game is
    // registered on the partner site (also update steam_appid.txt).
    private const uint STEAM_APP_ID = 480;

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
            // asyncCallbacks:false → we pump callbacks ourselves each
            // frame from SteamLifecycleTicker.RunCallbacks(). Init throws
            // if the Steam client isn't running / the app isn't owned —
            // the catch below drops us to standalone mode cleanly.
            Steamworks.SteamClient.Init(STEAM_APP_ID, asyncCallbacks: false);
            s_running = Steamworks.SteamClient.IsValid;
            if (s_running)
                GameLog.Info($"[SteamManager] Steam initialised (AppID {STEAM_APP_ID}, user '{Steamworks.SteamClient.Name}').");
            else
                GameLog.Info("[SteamManager] SteamClient.Init returned invalid — running standalone.");
#elif STEAMWORKS_NET
            // ##STEAM_HOOK## Steamworks.NET init — use the official
            // SteamManager MonoBehaviour prefab or SteamAPI.Init().
            s_running = false;
#else
            s_running = false;
            GameLog.Info("[SteamManager] No Steamworks package present — running standalone. Achievements + Cloud are local-only stubs.");
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
            var ach = new Steamworks.Data.Achievement(steamAchievementID);
            if (!ach.State) ach.Trigger();
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
        try { Steamworks.SteamClient.RunCallbacks(); }
        catch (System.Exception e) { Debug.LogWarning($"[SteamManager] RunCallbacks failed: {e.Message}"); }
#elif STEAMWORKS_NET
        // Steamworks.SteamAPI.RunCallbacks();
#endif
    }

    public static void Shutdown()
    {
        if (!s_running) return;
#if FACEPUNCH_STEAMWORKS
        try { Steamworks.SteamClient.Shutdown(); }
        catch (System.Exception e) { Debug.LogWarning($"[SteamManager] Shutdown failed: {e.Message}"); }
#elif STEAMWORKS_NET
        // Steamworks.SteamAPI.Shutdown();
#endif
        s_running = false;
    }

    // -------------------------------------------------------------
    //  Rich Presence — shown in the Steam Friends list under the
    //  player's game entry ("In Camp", "Region 12: Forest…").
    //  Steam has a set of well-known keys (see Rich Presence docs):
    //   * "steam_display" — token in Steam Partner localisation table
    //   * "status"        — free-form fallback string
    //  We push both so a plain-text status shows even before the
    //  partner-side localisation is uploaded.
    // -------------------------------------------------------------
    public static void SetRichPresence(string status)
    {
        if (!s_running || string.IsNullOrEmpty(status)) return;
        try
        {
#if FACEPUNCH_STEAMWORKS
            // "status" is the free-form fallback shown before a partner-side
            // localisation table exists. "steam_display" would point at a
            // localisation token — omitted until that table is uploaded so
            // Steam doesn't show a raw "#Status" token to friends.
            Steamworks.SteamFriends.SetRichPresence("status", status);
#elif STEAMWORKS_NET
            // Steamworks.SteamFriends.SetRichPresence("steam_display", "#Status");
            // Steamworks.SteamFriends.SetRichPresence("status", status);
#endif
        }
        catch (System.Exception e)
        {
            Debug.LogWarning($"[SteamManager] SetRichPresence failed: {e.Message}");
        }
    }
}
