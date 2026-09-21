using System.Reflection;
using UnityEngine;
using UnityEngine.SceneManagement;

// Plays ONE shot out of Trailer_Lvl_1, instead of all of them at once.
//
// ==== WHY THIS EXISTS ====
//
// Trailer_Lvl_1 holds two finished shots that were built at different times and
// never taught about each other: the statue breaking open (LoreTrailer_Statue_Rig,
// TrailerStatueShot with autoPlay on) and the ride through the forest
// (TrailerSequencer, TrailerSequenceDirector). Both of them run their own opening
// in Start, so pressing Play starts BOTH — two cameras fighting over the brain,
// two AudioListeners, two directional lights, and a day/night cycle spinning the
// sun through a shot that was lit for one fixed night.
//
// So the launcher names the shot it wants, and everything belonging to the other
// one is switched off here, in AfterSceneLoad — which runs after every Awake but
// before the first Start, so the shot that is not wanted never opens at all.
// Switching a director off in its own Start would be too late: by then it has
// already taken the camera.
//
// Opening the scene and pressing Play by hand sets no key, and then this does
// nothing whatsoever: the scene plays exactly as it is authored.
public static class TrailerShotSolo
{
    public const string SessionKey = "Trailer.SoloShot";
    public const string StatueShot = "statue";
    public const string RideShot = "ride";

    // The only scene this is allowed to touch.
    public const string TrailerScene = "Trailer_Lvl_1";

    private const string StatueRig = "LoreTrailer_Statue_Rig";
    private const string Sequencer = "TrailerSequencer";
    private const string ActIRig = "LoreTrailer_Rig";
    private const string Part2Rig = "LoreTrailer_Part2_Rig";
    private const string SceneSun = "Directional Light";

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Apply()
    {
        string shot = Wanted();
        if (string.IsNullOrEmpty(shot)) return;

        // The key outlives one launch on purpose (see Wanted), so it is still set
        // the next time Play is pressed — and that might be in CampScene. Nothing
        // below is survivable in a gameplay scene: it switches the sun off, stops
        // the day/night cycle and leaves exactly one camera rendering. So the
        // scene has to say it is the trailer before any of it happens.
        if (SceneManager.GetActiveScene().name != TrailerScene) return;

        // The two fogs would stack. The scene asset already has the built-in one
        // off; this is here so the shot is right even if something switched it
        // back on, which WeatherController and DayNightCycle both do.
        RenderSettings.fog = false;

        if (shot == StatueShot) SoloStatue();
        else if (shot == RideShot) SoloRide();
        else return;

        Debug.Log("[TrailerShotSolo] Playing the '" + shot + "' shot alone. Everything belonging to the other shot " +
                  "is off for this run; the scene asset is untouched. Launch the other one from Tools > Lore Trailer.");
    }

    // Set by the editor launcher just before it enters Play Mode. It is NOT
    // cleared afterwards on purpose: while a shot is being tuned, Play gets
    // pressed dozens of times, and having to go back to the menu each time would
    // make the tool worse than no tool. It lives in SessionState, so it is gone
    // the moment the editor is closed.
    private static string Wanted()
    {
#if UNITY_EDITOR
        return UnityEditor.SessionState.GetString(SessionKey, string.Empty);
#else
        return string.Empty;
#endif
    }

    // ===================== shot 1 — the statue breaks open =====================
    private static void SoloStatue()
    {
        GameObject rig = TrailerFind.ByName(StatueRig);
        if (rig != null) rig.SetActive(true);

        Off(Sequencer);
        OffAll(ActIRig);
        OffAll(Part2Rig);
        Off(SceneSun);                       // the rig carries its own Moonlight

        var ride = Object.FindFirstObjectByType<TrailerHorseRide>(FindObjectsInactive.Include);
        if (ride != null) ride.gameObject.SetActive(false);

        // A fixed night. The cycle would rewrite the fog colour and swing the sun
        // through a shot that is ten seconds long and lit for one moment.
        var cycle = Object.FindFirstObjectByType<DayNightCycle>(FindObjectsInactive.Include);
        if (cycle != null) cycle.enabled = false;

        Light moon = rig != null ? rig.GetComponentInChildren<Light>(true) : null;
        Camera shotCam = null;
        if (rig != null)
        {
            foreach (var c in rig.GetComponentsInChildren<Camera>(true)) { shotCam = c; break; }
        }
        SoloCamera(shotCam);

        // ==== THE FOG HAS TO BE RETUNED FOR A FIVE-METRE SHOT ====
        //
        // Density here is extinction PER WORLD METRE, so what a value means
        // depends entirely on how far away the shot is looking. The fog object in
        // the scene is set for the ride: 0.022/m over a 26 m layer, which is a
        // thin haze that only adds up into weather across a hundred metres of
        // valley. This shot never looks further than fifteen, so those numbers
        // put no fog in frame at all.
        //
        // These are the same air made to read over a tenth of the distance:
        // roughly three times the extinction, a layer that still covers the
        // statue's full height, and the near fade pulled almost to the lens —
        // the push ENDS about five metres out, well inside the ride's 3 m fade.
        //
        // It is not decoration. A light shaft in clean air is invisible in
        // reality and reads as a plastic ribbon in an engine; this is the
        // particulate that turns the ember light coming out of the cracks into
        // a shaft. That is also why it is a fog change and not a fog removal.
        var fog = Fog();
        if (fog != null)
        {
            Set(fog, "density", 0.075f);
            Set(fog, "groundFogHeight", 14f);
            Set(fog, "topSoftness", 0.5f);
            Set(fog, "nearFadeDistance", 1.5f);
            Set(fog, "maximumFogDistance", 120f);
            // Strongly forward-scattering, so the fog lights up along the shafts
            // rather than glowing evenly everywhere.
            Set(fog, "anisotropy", 0.72f);
            Set(fog, "windSpeed", 1.2f);
            // Cold and fixed. Following the scene colour would hand the fog to
            // the day/night gradient that was just switched off, and a cold
            // ground is what makes the ember light read as heat.
            Set(fog, "followSceneFogColor", false);
            Set(fog, "fogColor", new Color(0.34f, 0.40f, 0.52f, 1f));
            Set(fog, "ambientColor", new Color(0.16f, 0.19f, 0.28f, 1f));
            if (moon != null) Set(fog, "directionalLight", moon);
        }
    }

    // ===================== shot 2 — the ride through the forest =====================
    private static void SoloRide()
    {
        // The whole statue rig goes, and its camera, its AudioListener and its
        // Moonlight go with it — which is also how the ride stops being lit by
        // two directional lights at once.
        OffAll(StatueRig);

        GameObject seq = TrailerFind.ByName(Sequencer);
        if (seq != null) seq.SetActive(true);

        // TrailerSequenceDirector owns the rigs from here: it wakes them, parks
        // their cameras and cuts between them itself. Nothing else touches them.
        // By name, not Camera.main: BOTH cameras in this scene are tagged
        // MainCamera, so Camera.main returns whichever one Unity reaches first
        // and has already been the wrong one.
        GameObject camGo = TrailerFind.ByName("Main Camera");
        Camera main = camGo != null ? camGo.GetComponent<Camera>() : Camera.main;
        SoloCamera(main);

        // The fog keeps the scene's own tuning here — that is what it was set up
        // for, and the inspector stays the place to tune the ride. The one thing
        // worth making certain of is which light it scatters, because the season
        // ride drives that light through the summer-to-winter turn and the fog
        // should turn with it.
        var fog = Fog();
        var season = Object.FindFirstObjectByType<TrailerSeasonRide>(FindObjectsInactive.Include);
        if (fog != null && season != null && season.sun != null) Set(fog, "directionalLight", season.sun);
    }

    // ===================== helpers =====================

    private static void Off(string name)
    {
        GameObject go = TrailerFind.ByName(name);
        if (go != null) go.SetActive(false);
    }

    // Earlier setup tools left duplicates of some rigs in the scene, so switching
    // off "the" one by name has already missed a second copy once.
    private static void OffAll(string name)
    {
        foreach (var go in TrailerFind.AllByName(name)) if (go != null) go.SetActive(false);
    }

    // Exactly one camera renders and exactly one AudioListener hears. Cameras are
    // left in the hierarchy and only their components are switched off, so a
    // GameObject that also carries something the shot needs — the FMOD
    // StudioListener sits on Main Camera — keeps working.
    private static void SoloCamera(Camera keep)
    {
        foreach (var cam in Object.FindObjectsByType<Camera>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (cam == null || !cam.gameObject.scene.IsValid()) continue;
            bool mine = cam == keep;
            cam.enabled = mine;
            var listener = cam.GetComponent<AudioListener>();
            if (listener != null) listener.enabled = mine;
        }
        if (keep != null) keep.gameObject.SetActive(true);
    }

    // Pure Volumetric Fog lives in its own assembly, so a direct reference would
    // stop this file compiling for anyone who removes the package. Found by name
    // and written through reflection, the same way DayNightCycle reaches it.
    private static Component Fog()
    {
        System.Type t = System.Type.GetType("BKPureNature.PureVolumetricFog, BKPureNature.PureVolumetricFog");
        if (t == null) return null;
        return Object.FindFirstObjectByType(t) as Component;
    }

    private static void Set(Component fog, string field, object value)
    {
        if (fog == null) return;
        // Its settings are [SerializeField] private, so NonPublic is not optional.
        FieldInfo f = fog.GetType().GetField(field, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (f == null) { Debug.LogWarning("[TrailerShotSolo] PureVolumetricFog has no field '" + field + "' any more."); return; }
        f.SetValue(fog, value);
    }
}
