using System.Linq;
using UnityEditor;
using UnityEngine;

// Locks the scene into moody weather for the trailer, driving the project's own
// DayNightCycle so rain, storm skybox, fog, darker ambient and lightning all
// come on together and cohesively.
//
//   Tools ▸ Lore Trailer ▸ Setup Weather (Storm + Rain)
//   Tools ▸ Lore Trailer ▸ Setup Weather (Light Rain)
//
// DayNightCycle only shows rain when the "biome" is 0, and its weather drifts on
// a timer, so this also forces biome 0 and LOCKS the weather. If the scene's
// DayNightCycle has no rain VFX assigned, it spawns a camera-following rain from
// the pack so the ride is always in-frame.
public static class TrailerWeatherSetup
{
    private const string RainPrefabPath = "Assets/VFX Brady Games/Particle Effect/Heavy Rain.prefab";

    [MenuItem("Tools/Lore Trailer/Setup Weather (Storm + Rain)")]
    public static void StormMenu() { Setup(WeatherState.Storm); }

    [MenuItem("Tools/Lore Trailer/Setup Weather (Light Rain)")]
    public static void RainMenu() { Setup(WeatherState.Precipitation); }

    public static bool Setup(WeatherState state, bool showDialog = true)
    {
        var dnc = Object.FindObjectsByType<DayNightCycle>(FindObjectsInactive.Include, FindObjectsSortMode.None).FirstOrDefault();
        if (dnc == null)
        {
            if (showDialog) EditorUtility.DisplayDialog("Trailer Weather",
                "No DayNightCycle in this scene — can't drive weather.\n" +
                "Add the DayNightCycle prefab from Lvl_1, then run this again.", "OK");
            return false;
        }

        Undo.RecordObject(dnc, "trailer weather");
        dnc.enabled = true;
        dnc.currentWeather = state;
        dnc.isWeatherLocked = true;                 // don't let the timer drift it back to Clear

        // Rain only renders for biome 0 in DayNightCycle.
        PlayerPrefs.SetInt("RegionBiomeType", 0);
        PlayerPrefs.Save();

        // Ensure there's actually a rain system, following the camera.
        bool spawnedRain = false;
        if (dnc.rainVFX == null)
        {
            var ps = SpawnCameraRain();
            if (ps != null) { dnc.rainVFX = ps; spawnedRain = true; }
        }

        // Global fog for depth/atmosphere (DayNightCycle also enables it at Start).
        RenderSettings.fog = true;
        if (RenderSettings.fogMode == FogMode.Linear && RenderSettings.fogEndDistance <= RenderSettings.fogStartDistance)
        {
            RenderSettings.fogStartDistance = 20f;
            RenderSettings.fogEndDistance = 350f;
        }

        EditorUtility.SetDirty(dnc);
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(
            UnityEngine.SceneManagement.SceneManager.GetActiveScene());

        if (!showDialog) return true;
        EditorUtility.DisplayDialog("Trailer Weather",
            $"Weather locked to {state}.\n" +
            $"  • Rain VFX: {(dnc.rainVFX != null ? (spawnedRain ? "spawned camera-following rain" : "using the scene's rain") : "NONE — assign DayNightCycle.rainVFX")}.\n" +
            "  • Biome forced to 0 so rain renders; fog on.\n" +
            "  • Storm skybox / lightning / darker ambient come from DayNightCycle.\n\n" +
            "Press Play to see it. If the storm skybox is missing, assign DayNightCycle ▸ stormSkybox.", "OK");
        return true;
    }

    private static ParticleSystem SpawnCameraRain()
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(RainPrefabPath);
        if (prefab == null) return null;

        var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
        if (go == null) go = Object.Instantiate(prefab);
        go.name = "Trailer_Rain";
        Undo.RegisterCreatedObjectUndo(go, "spawn trailer rain");

        var follow = go.GetComponent<TrailerRainFollow>();
        if (follow == null) follow = Undo.AddComponent<TrailerRainFollow>(go);
        var cam = Camera.main;
        if (cam != null) follow.target = cam.transform;

        return go.GetComponentInChildren<ParticleSystem>(true);
    }
}
