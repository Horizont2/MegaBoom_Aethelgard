using UnityEngine;

// Persistent GameObject that ticks Steam callbacks each frame and
// shuts Steam down cleanly on application quit. Auto-created at
// runtime — no scene wiring needed.
[DefaultExecutionOrder(-1000)]
public class SteamLifecycleTicker : MonoBehaviour
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        var go = new GameObject("[SteamLifecycleTicker]");
        DontDestroyOnLoad(go);
        go.AddComponent<SteamLifecycleTicker>();
    }

    private void Update() => SteamManager.RunCallbacks();
    private void OnApplicationQuit() => SteamManager.Shutdown();
}
