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
// ==== ONE SOURCE OF TRUTH, AND IT IS THE FILE ====
//
// The App ID used to be a constant here AND a number in steam_appid.txt, which
// is two places to change and therefore one place to forget. Steam itself reads
// steam_appid.txt when the game is launched outside the client, so that file has
// to be right regardless; the code now reads the same file and falls back to the
// constant only when it is missing.
//
// Fallback480 is Valve's "Spacewar" test app. It lets Steamworks initialise and
// exercise the whole achievement / rich-presence / cloud pipeline before the game
// is registered — and it is WRONG to ship: achievements land in Valve's test app,
// the ownership check means nothing, and the overlay belongs to another product.
// So a release build that resolves to 480 says so, loudly, every launch.
//
// When the app is registered: put the real number in steam_appid.txt and nowhere
// else. The shipped build gets its App ID injected by the Steam client, so the
// file is a development convenience — do NOT ship it in the final depot.
public static class SteamManager
{
    private const uint Fallback480 = 480;

    private static uint ResolveAppId()
    {
        try
        {
            // Beside the executable in a build, and the project root in the
            // editor — dataPath's parent is both.
            string dir = System.IO.Directory.GetParent(Application.dataPath)?.FullName;
            if (!string.IsNullOrEmpty(dir))
            {
                string file = System.IO.Path.Combine(dir, "steam_appid.txt");
                if (System.IO.File.Exists(file) &&
                    uint.TryParse(System.IO.File.ReadAllText(file).Trim(), out uint fromFile) && fromFile != 0)
                    return fromFile;
            }
        }
        catch (System.Exception e)
        {
            Debug.LogWarning("[SteamManager] Could not read steam_appid.txt: " + e.Message);
        }
        return Fallback480;
    }

    private static bool s_initialised;
    private static bool s_running;

    public static bool IsRunning => s_running;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Bootstrap()
    {
        if (s_initialised) return;
        s_initialised = true;

        uint appId = ResolveAppId();
        if (appId == Fallback480)
        {
            // Not a warning anyone can miss, and not one that fires in the
            // editor a hundred times a day either: it matters when it ships.
            string where = Application.isEditor ? "in the editor" : "IN A BUILD";
            Debug.LogWarning($"[SteamManager] Running on Valve's Spacewar test App ID (480) {where}. " +
                             "Achievements go to Valve's test app, the ownership check means nothing and the " +
                             "overlay belongs to another product. Put the real App ID in steam_appid.txt " +
                             "before shipping.");
        }

        try
        {
#if FACEPUNCH_STEAMWORKS && (UNITY_STANDALONE || UNITY_EDITOR)
            // asyncCallbacks:false → we pump callbacks ourselves each
            // frame from SteamLifecycleTicker.RunCallbacks(). Init throws
            // if the Steam client isn't running / the app isn't owned —
            // the catch below drops us to standalone mode cleanly.
            Steamworks.SteamClient.Init(appId, asyncCallbacks: false);
            s_running = Steamworks.SteamClient.IsValid;
            if (s_running)
                GameLog.Info($"[SteamManager] Steam initialised (AppID {appId}, user '{Steamworks.SteamClient.Name}').");
            else
                GameLog.Info("[SteamManager] SteamClient.Init returned invalid — running standalone.");
#elif STEAMWORKS_NET && (UNITY_STANDALONE || UNITY_EDITOR)
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
#if FACEPUNCH_STEAMWORKS && (UNITY_STANDALONE || UNITY_EDITOR)
            var ach = new Steamworks.Data.Achievement(steamAchievementID);
            if (!ach.State) ach.Trigger();
#elif STEAMWORKS_NET && (UNITY_STANDALONE || UNITY_EDITOR)
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
#if FACEPUNCH_STEAMWORKS && (UNITY_STANDALONE || UNITY_EDITOR)
            // Steamworks.SteamRemoteStorage.FileWrite("save_v1.json", bytes);
#elif STEAMWORKS_NET && (UNITY_STANDALONE || UNITY_EDITOR)
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
#if FACEPUNCH_STEAMWORKS && (UNITY_STANDALONE || UNITY_EDITOR)
        try { Steamworks.SteamClient.RunCallbacks(); }
        catch (System.Exception e) { Debug.LogWarning($"[SteamManager] RunCallbacks failed: {e.Message}"); }
#elif STEAMWORKS_NET && (UNITY_STANDALONE || UNITY_EDITOR)
        // Steamworks.SteamAPI.RunCallbacks();
#endif
    }

    public static void Shutdown()
    {
        if (!s_running) return;
#if FACEPUNCH_STEAMWORKS && (UNITY_STANDALONE || UNITY_EDITOR)
        try { Steamworks.SteamClient.Shutdown(); }
        catch (System.Exception e) { Debug.LogWarning($"[SteamManager] Shutdown failed: {e.Message}"); }
#elif STEAMWORKS_NET && (UNITY_STANDALONE || UNITY_EDITOR)
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
#if FACEPUNCH_STEAMWORKS && (UNITY_STANDALONE || UNITY_EDITOR)
            // "status" is the free-form fallback shown before a partner-side
            // localisation table exists. "steam_display" would point at a
            // localisation token — omitted until that table is uploaded so
            // Steam doesn't show a raw "#Status" token to friends.
            Steamworks.SteamFriends.SetRichPresence("status", status);
#elif STEAMWORKS_NET && (UNITY_STANDALONE || UNITY_EDITOR)
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
