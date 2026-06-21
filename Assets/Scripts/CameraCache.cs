using UnityEngine;
using UnityEngine.SceneManagement;

// Centralised Camera.main lookup. Camera.main walks every camera tagged
// "MainCamera" in the scene on each call — cheap on its own but ~50 different
// scripts in the project read it every frame from Update/LateUpdate, which
// piles up quickly. CameraCache resolves once per scene and hands out the
// transform without paying that cost again.
public static class CameraCache
{
    private static Camera s_main;
    private static Transform s_mainTransform;
    private static bool s_initialised;

    public static Camera Main
    {
        get
        {
            if (s_main == null || !s_initialised) Refresh();
            return s_main;
        }
    }

    public static Transform MainTransform
    {
        get
        {
            if (s_mainTransform == null || !s_initialised) Refresh();
            return s_mainTransform;
        }
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void HookSceneEvents()
    {
        SceneManager.sceneLoaded += OnSceneLoaded;
        SceneManager.sceneUnloaded += OnSceneUnloaded;
    }

    private static void OnSceneLoaded(Scene scene, LoadSceneMode mode) => Invalidate();
    private static void OnSceneUnloaded(Scene scene) => Invalidate();

    public static void Invalidate()
    {
        s_main = null;
        s_mainTransform = null;
        s_initialised = false;
    }

    private static void Refresh()
    {
        s_main = Camera.main;
        s_mainTransform = s_main != null ? s_main.transform : null;
        s_initialised = true;
    }
}
