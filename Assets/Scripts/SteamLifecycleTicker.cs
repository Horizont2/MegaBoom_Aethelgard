using UnityEngine;

// Persistent GameObject that ticks Steam callbacks each frame and
// flushes save state / shuts Steam down cleanly when the process
// leaves. Auto-created at runtime — no scene wiring needed.
//
// Save discipline:
//   * OnApplicationPause(true) — Steam Deck sleep, alt-tab on some
//     platforms, and mobile "backgrounded" all trigger this. Flush.
//   * OnApplicationQuit — normal exit path. Flush + Steam shutdown.
//   * OnApplicationFocus(false) — belt-and-braces so an alt-tab that
//     doesn't pause the app still persists recent achievements.
[DefaultExecutionOrder(-1000)]
public class SteamLifecycleTicker : MonoBehaviour
{
    private static bool s_bootstrapped;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        if (s_bootstrapped) return;
        s_bootstrapped = true;
        var go = new GameObject("[SteamLifecycleTicker]");
        DontDestroyOnLoad(go);
        go.AddComponent<SteamLifecycleTicker>();
    }

    private void Update() => SteamManager.RunCallbacks();

    private void OnApplicationPause(bool paused)
    {
        if (paused) FlushAll();
    }

    private void OnApplicationFocus(bool hasFocus)
    {
        if (!hasFocus) FlushAll();
    }

    private void OnApplicationQuit()
    {
        FlushAll();
        SteamManager.Shutdown();
    }

    // A single choke-point so we can trace saves and add sinks later
    // (Steam Cloud push, per-slot manager, etc.) without walking the
    // scene for savers.
    private static void FlushAll()
    {
        try { SaveSystem.Save(); } catch (System.Exception e) { Debug.LogWarning($"[Lifecycle] SaveSystem flush failed: {e.Message}"); }
        try { PlayerPrefs.Save(); } catch (System.Exception e) { Debug.LogWarning($"[Lifecycle] PlayerPrefs flush failed: {e.Message}"); }
    }
}
