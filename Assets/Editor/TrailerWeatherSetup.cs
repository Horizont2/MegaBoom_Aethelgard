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
        // God rays only if a sunshaft object is actually assigned (else it no-ops).
        if (dnc.godRaysObject != null) dnc.enableGodRays = true;

        // Fill in the DayNightCycle data that's blank/default in the trailer scene
        // (empty intensity curves make the sun go black; white fog gradients wash
        // everything out).
        ConfigureDayNightData(dnc);

        // Wind so the vegetation (terrain trees / grass) moves in the storm.
        EnsureWindZone();

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
            "  • DayNightCycle data filled in: sun/moon intensity curves (empty = black scene) + fog/sun colour gradients (were white).\n" +
            "  • Wind zone added; storm skybox / lightning / darker ambient come from DayNightCycle.\n\n" +
            "Press Play to see it. If the storm skybox is missing, assign DayNightCycle ▸ stormSkybox.", "OK");
        return true;
    }

    // Author the DayNightCycle's curves + gradients if they're blank/default.
    private static void ConfigureDayNightData(DayNightCycle dnc)
    {
        // Sun intensity across the day (0..1 = midnight..midnight). Empty curve
        // evaluates to 0 -> pitch black; this is the main "nothing is visible" fix.
        if (dnc.sunIntensity == null || dnc.sunIntensity.length == 0)
            dnc.sunIntensity = Smooth(new[]
            {
                new Keyframe(0f, 0.05f), new Keyframe(0.25f, 0.7f),
                new Keyframe(0.5f, 1.15f), new Keyframe(0.75f, 0.7f), new Keyframe(1f, 0.05f),
            });

        if (dnc.moonIntensity == null || dnc.moonIntensity.length == 0)
            dnc.moonIntensity = Smooth(new[]
            {
                new Keyframe(0f, 0.5f), new Keyframe(0.25f, 0.1f),
                new Keyframe(0.5f, 0f), new Keyframe(0.75f, 0.1f), new Keyframe(1f, 0.5f),
            });

        // Fog / sun colours (default is white -> washed out).
        if (IsBlankGradient(dnc.fogColorClear))
            dnc.fogColorClear = Flat(new Color(0.62f, 0.68f, 0.74f));   // soft cool haze
        if (IsBlankGradient(dnc.fogColorStorm))
            dnc.fogColorStorm = Flat(new Color(0.30f, 0.34f, 0.40f));   // dark blue-grey, still readable
        if (IsBlankGradient(dnc.sunColor))
            dnc.sunColor = Flat(new Color(1.0f, 0.96f, 0.9f));          // warm daylight

        // Sensible fog range for atmosphere without hiding the valley.
        dnc.fogStartDistance = 15f;
        dnc.fogEndDistance = 400f;
    }

    private static AnimationCurve Smooth(Keyframe[] keys)
    {
        var c = new AnimationCurve(keys);
        for (int i = 0; i < c.length; i++) c.SmoothTangents(i, 0f);
        return c;
    }

    private static Gradient Flat(Color c)
    {
        var g = new Gradient();
        g.SetKeys(
            new[] { new GradientColorKey(c, 0f), new GradientColorKey(c, 1f) },
            new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) });
        return g;
    }

    // A default/unconfigured Gradient is null or plain white — treat those as blank.
    private static bool IsBlankGradient(Gradient g)
    {
        if (g == null) return true;
        var keys = g.colorKeys;
        if (keys == null || keys.Length == 0) return true;
        foreach (var k in keys)
            if (k.color.r < 0.97f || k.color.g < 0.97f || k.color.b < 0.97f) return false;
        return true;   // all keys ~white => default
    }

    private static void EnsureWindZone()
    {
        if (Object.FindObjectsByType<WindZone>(FindObjectsInactive.Include, FindObjectsSortMode.None).Length > 0) return;
        var go = new GameObject("Trailer_Wind");
        Undo.RegisterCreatedObjectUndo(go, "trailer wind");
        go.transform.rotation = Quaternion.Euler(15f, 40f, 0f);
        var wz = go.AddComponent<WindZone>();
        wz.mode = WindZoneMode.Directional;
        wz.windMain = 1.4f;
        wz.windTurbulence = 1.0f;
        wz.windPulseMagnitude = 0.6f;
        wz.windPulseFrequency = 0.2f;
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
