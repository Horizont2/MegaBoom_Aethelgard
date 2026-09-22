using System.Reflection;
using UnityEngine;
using UnityEngine.SceneManagement;

// Plays ONE shot out of a trailer scene, instead of all of them at once.
//
// ==== WHY THIS EXISTS ====
//
// Each trailer scene holds more than one finished shot, built at different times
// and never taught about each other. Trailer_Lvl_1 has the statue breaking open
// (LoreTrailer_Statue_Rig) and the ride through the forest (TrailerSequencer);
// March_TrailerScene has the legion marching (TrailerLegionDirector) and the two
// armies meeting (TrailerClash). Every one of them runs its own opening in
// Start, so pressing Play starts BOTH shots in whichever scene is open — two
// cameras fighting over the brain, two AudioListeners, two directional lights,
// and a day/night cycle spinning the sun through a shot lit for one fixed night.
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
    public const string MarchShot = "march";
    public const string ClashShot = "clash";
    public const string CastleShot = "castle";

    // The only two scenes this is allowed to touch.
    public const string TrailerScene = "Trailer_Lvl_1";
    public const string MarchScene = "March_TrailerScene";
    // The last shot is filmed in the live game: the castle is Location_Castle,
    // nineteen hundred pieces the generator assembles on a hill of its own, and
    // region 24 is the only region that uses it. Generating region 24 IS the set.
    public const string GameScene = "GameScene";

    private const string StatueRig = "LoreTrailer_Statue_Rig";
    private const string Sequencer = "TrailerSequencer";
    private const string ActIRig = "LoreTrailer_Rig";
    private const string Part2Rig = "LoreTrailer_Part2_Rig";
    private const string SceneSun = "Directional Light";
    private const string RoadDressing = "Trailer_RoadDressing";

    /// <summary>The shot this run was launched for, or empty when the scene is simply being played.</summary>
    public static string Active { get; private set; } = string.Empty;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Apply()
    {
        string shot = Wanted();
        if (string.IsNullOrEmpty(shot)) return;

        // ==== A SHOT ONLY RUNS IN ITS OWN SCENE ====
        //
        // The key outlives one launch on purpose (see Wanted), so it is still set
        // the next time Play is pressed — and that might be in a different scene.
        // Checking only that the scene is A trailer scene was not enough: with
        // "statue" still set, opening March_TrailerScene ran SoloStatue there,
        // which switched off an object called "Directional Light" (the march's
        // key light) and then handed SoloCamera a camera it could not find in a
        // scene that has no statue rig. SoloCamera dutifully switched off every
        // camera that was not that one, which is all of them.
        //
        // So the pairing is explicit, and a shot in the wrong scene does nothing
        // at all rather than something approximate.
        string scene = SceneManager.GetActiveScene().name;
        if (scene != SceneFor(shot)) return;

        // Published so a shot can say in its own log whether it was launched or
        // merely played. Pressing Play on an open trailer scene runs EVERY shot
        // it holds at once, and the resulting frame — two cameras, the ride's
        // weather, a season sweep over a ten second cinematic — looks broken in
        // ways that have nothing to do with the shot being looked at. Guessing
        // at that from a screenshot has cost several rounds already.
        Active = shot;

        // The two fogs would stack. The scene asset already has the built-in one
        // off; this is here so the shot is right even if something switched it
        // back on, which WeatherController and DayNightCycle both do.
        RenderSettings.fog = false;

        // Not in the live game: the castle shot runs the real world, and its
        // distance culling is what keeps a generated map affordable. The trailer
        // scenes are hand-built and small enough not to need it.
        if (scene != GameScene) StopCulling();

        if (shot == StatueShot) SoloStatue();
        else if (shot == RideShot) SoloRide();
        else if (shot == MarchShot) SoloMarch();
        else if (shot == ClashShot) SoloClash();
        else if (shot == CastleShot) SoloCastle();
        else return;

        Debug.Log("[TrailerShotSolo] Playing the '" + shot + "' shot alone. Everything belonging to the other shot " +
                  "is off for this run; the scene asset is untouched. Launch the other one from Tools > Lore Trailer.");
    }

    // The one scene each shot belongs to. Anything unrecognised belongs nowhere.
    public static string SceneFor(string shot)
    {
        if (shot == StatueShot || shot == RideShot) return TrailerScene;
        if (shot == MarchShot || shot == ClashShot) return MarchScene;
        if (shot == CastleShot) return GameScene;
        return string.Empty;
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

    // ==== NOTHING IN A CINEMATIC GETS SWITCHED OFF BY A DISTANCE CHECK ====
    //
    // Evacuation_Horse carries an OptimizedObject, and that component does not
    // hide a renderer — it disables the renderers, the Animator, the lights and
    // the colliders together the moment DistanceOptimizer decides the thing is
    // far away. In gameplay that is exactly right; in a shot where the camera
    // deliberately cranes fifty metres off the rider it deletes the subject of
    // the reveal, which is the rider vanishing mid-shot.
    //
    // A trailer camera goes wherever the shot wants, so the distance systems
    // have no say here at all. Switched off rather than retuned: there is no
    // distance that is correct for a camera that is allowed to be anywhere.
    private static void StopCulling()
    {
        int off = 0;
        foreach (var o in Object.FindObjectsByType<OptimizedObject>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (o == null) continue;
            // Put back anything it has already hidden before standing it down —
            // disabling the component does not undo what it did.
            o.SetVisibility(true);
            o.enabled = false;
            off++;
        }

        var dist = Object.FindFirstObjectByType<DistanceOptimizer>(FindObjectsInactive.Include);
        if (dist != null) dist.enabled = false;

        // Fades foliage that stands between the camera and the player. Harmless
        // here only because nothing in this scene is tagged Player; the moment
        // something is, it starts dissolving trees in front of the gallop.
        var occl = Object.FindFirstObjectByType<CameraOcclusion>(FindObjectsInactive.Include);
        if (occl != null) occl.enabled = false;

        KeepHeroDrawn();

        if (off > 0) Debug.Log("[TrailerShotSolo] Distance culling off for " + off + " object(s) — a trailer camera is allowed to be anywhere.");
    }

    // ==== THE OTHER WAY A CHARACTER DISAPPEARS ====
    //
    // A SkinnedMeshRenderer with Update When Offscreen off does not measure its
    // own bounds — it transforms the mesh's authored bounds by the root bone and
    // frustum-tests that box. For a rider that is not where he is: he is posed
    // sitting on a mount that is itself being driven along a spline, with root
    // motion off, so the box and the body do not agree. From a crane fifty
    // metres up he occupies a handful of pixels and a box that is slightly out
    // is the difference between drawn and not drawn — the character blinks out
    // of a shot in which he is plainly visible.
    //
    // Switched on for the trailer only, and only here. It costs a bounds
    // recalculation per ENABLED renderer per frame, which on a modular rig is a
    // few — the two hundred disabled armour variants cost nothing and are set
    // anyway, so a costume change cannot reintroduce the problem mid-shot.
    private static void KeepHeroDrawn()
    {
        GameObject hero = GameObject.FindGameObjectWithTag("Player");
        if (hero == null) return;

        foreach (var smr in hero.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            if (smr != null) smr.updateWhenOffscreen = true;
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

        // The road set: torches, fires, and nine Skeleton_Minions that lie buried
        // waiting for a rider. None of it is in this frame, and with the horse
        // switched off each of those skeletons hunts for a Player tag on every
        // Update for a chase that is never going to start.
        Off(RoadDressing);

        var ride = Object.FindFirstObjectByType<TrailerHorseRide>(FindObjectsInactive.Include);
        if (ride != null) ride.gameObject.SetActive(false);

        // A fixed night. The cycle would rewrite the fog colour and swing the sun
        // through a shot that is ten seconds long and lit for one moment.
        var cycle = Object.FindFirstObjectByType<DayNightCycle>(FindObjectsInactive.Include);
        if (cycle != null) cycle.enabled = false;

        SilenceWeather(rig);

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
            // ==== THE LENS WAS INSIDE THE FOG ====
            //
            // A fourteen metre layer with the near fade pulled to a metre and a
            // half, filmed by a camera standing two and a half metres off the
            // ground: the lens is swimming in it. Every ray starts in dense fog
            // a pace in front of the glass and never gets out, so the frame is a
            // bright wall with a statue somewhere behind it — which is exactly
            // the "big white thing in front of the camera, and the statue is
            // gone" report. It is also a full-resolution raymarch through the
            // thickest part of the volume on every pixel, which is the lag.
            //
            // The fog belongs at the statue's FEET, not around the lens. A
            // couple of metres deep puts it below the camera entirely: mist
            // pooled around the base, the shafts passing down through it, and
            // clear air between the glass and the stone.
            Set(fog, "density", 0.09f);
            // Below the lens, and not level with it. The push ends with the
            // glass 2.4 m off the ground; a layer 2.2 m deep with a soft top put
            // the last second of the shot INSIDE the fog, where every ray starts
            // in the thickest part of the volume and the frame goes pale. A
            // metre and a half leaves the mist around the statue's feet, which
            // is the only place it was ever wanted.
            Set(fog, "groundFogHeight", 1.5f);
            Set(fog, "topSoftness", 0.45f);
            Set(fog, "nearFadeDistance", 6f);
            Set(fog, "maximumFogDistance", 90f);
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

            // Volumetric shadows cost a shadow-map lookup at EVERY raymarch step,
            // and the terrain shadow adds a heightmap march on top of that — the
            // fog's most expensive two settings by a distance, paid per pixel,
            // every frame. What they buy here is the shadow the statue throws
            // through the fog from a 0.35-intensity moon, in a shot that ends in
            // a furnace of ember light. Nothing visible, for the largest single
            // cost in a shot that is already fighting for frames.
            Set(fog, "enableVolumetricShadows", false);
            Set(fog, "terrainCastsFogShadows", false);

            // ==== AND MAKE IT REBUILD NOW, NOT WHEN IT NOTICES ====
            //
            // Those are the component's own serialised fields, written straight
            // in. Its GPU side — the terrain heightmap, the noise volume, the
            // material — is brought up to date by its Update, which does not run
            // until the first frame has already been drawn. So the opening
            // frames of the shot render off whatever the fog happened to have
            // built for the scene as it was authored, with the new density
            // already applied to it. Refresh is public and does exactly this,
            // so the first frame is the fog this shot asked for.
            var refresh = fog.GetType().GetMethod("Refresh", BindingFlags.Instance | BindingFlags.Public);
            if (refresh != null) refresh.Invoke(fog, null);
        }
    }

    // ==== SHOT 1 HAS NO WEATHER ====
    //
    // Trailer_Lvl_1 is dressed for the RIDE. A Heavy Rain volume sits at the
    // scene root and chases the Main Camera every LateUpdate; the buried
    // skeletons breathe vapour; the horse kicks up dust. None of it belongs to a
    // locked-off ten second shot of a statue, and switching off the ride's rigs
    // does not touch any of it, because none of it is parented to them — a
    // volume that FOLLOWS a camera lives wherever it likes in the hierarchy.
    //
    // A camera-following weather volume is also exactly the shape of the fault
    // that has been chased through this shot for four rounds: a large, bright,
    // alpha-blended mass hanging in front of the lens from the first frame, the
    // subject somewhere behind it, and a frame rate that collapses the moment
    // anything else is drawn.
    //
    // So every particle system in the scene that is not part of the statue rig
    // is stopped, cleared and switched off. Nothing is lost: this shot builds
    // its own dust in code, in Start, after this has already run.
    private static void SilenceWeather(GameObject rig)
    {
        Transform keep = rig != null ? rig.transform : null;
        int stopped = 0;

        foreach (var ps in Object.FindObjectsByType<ParticleSystem>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (ps == null) continue;
            if (keep != null && ps.transform.IsChildOf(keep)) continue;

            // Cleared as well as stopped: particles already alive would
            // otherwise hang in the air for the rest of their lifetime, which on
            // a rain volume is most of the shot.
            if (ps.gameObject.activeInHierarchy)
                ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            ps.gameObject.SetActive(false);
            stopped++;
        }

        // The follower has to go too, or it spends the shot dragging an empty
        // volume around after a camera that is no longer rendering.
        foreach (var follow in Object.FindObjectsByType<TrailerRainFollow>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            if (follow != null) follow.enabled = false;

        if (stopped > 0)
            Debug.Log("[TrailerShotSolo] " + stopped + " particle system(s) outside the statue rig switched off — " +
                      "this shot has no rain, no breath and no hoof dust.");
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

        // This shot is the gallop and the crane that lifts off it. The director
        // would otherwise cut from the reveal into Part 2 — the rider thrown, the
        // fall, the skeletons — which is a different beat with its own cut and
        // has no business inside "the ride through the forest". Told here rather
        // than saved into the scene, so the full-trailer path still exists.
        var director = seq != null ? seq.GetComponent<TrailerSequenceDirector>() : null;
        if (director == null) director = Object.FindFirstObjectByType<TrailerSequenceDirector>(FindObjectsInactive.Include);
        if (director != null)
        {
            director.endAfterCrane = true;
            // And the land stays the land it was galloping through: no season
            // turn, no re-tinted trees, no four days of sun under the crane.
            director.holdWorld = true;
        }

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

    // ===================== shot 3 — the legion marches =====================
    //
    // The march scene now holds two shots, and both of them open themselves, so
    // the same rule applies here as in Trailer_Lvl_1: whichever one is not
    // wanted is switched off before its Start can take the camera.
    private static void SoloMarch()
    {
        Off("TrailerClash");

        var legion = Object.FindFirstObjectByType<TrailerLegionDirector>(FindObjectsInactive.Include);
        if (legion != null) legion.enabled = true;
    }

    // ===================== shot 4 — the march, then the two armies meet =====
    //
    // Not the clash on its own. It is the beat AFTER the march and it waits on
    // the march reporting finished, so playing it means playing both: the rise,
    // the march, and then the two lines running at each other. It also has to be
    // that way for the shot to be recordable at all — the clash builds its
    // hundred and fifty bodies during the march so that the cut into it is not a
    // wall of synchronous shader compilation.
    private static void SoloClash()
    {
        var legion = Object.FindFirstObjectByType<TrailerLegionDirector>(FindObjectsInactive.Include);
        if (legion != null) legion.enabled = true;

        GameObject clash = TrailerFind.ByName("TrailerClash");
        if (clash != null) clash.SetActive(true);
        else Debug.LogWarning("[TrailerShotSolo] No 'TrailerClash' object in this scene.");
    }

    // ===================== shot 5 — the castle in the fog =====================
    //
    // Nothing to do. The castle shot arms ITSELF from the same session key, and
    // it has to: its one critical job is naming the region before
    // WorldGenerator's Start reads it, and making that depend on this activating
    // an object at the right moment was a correctness problem waiting to happen.
    // An ordinary Awake on an ordinary active object is guaranteed to be before
    // every Start in the scene; nothing here can improve on that.
    //
    // Nothing is switched off either, because the whole live game has to run —
    // the world does not exist until it has been generated. The shot clears the
    // set once there is a set to clear.
    private static void SoloCastle() { }

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
        // "Keep none" is never what is meant. Called with null it used to switch
        // off every camera in the scene and leave the game view reading
        // "No cameras rendering", which describes the symptom and hides the
        // cause — that the camera this shot wanted was not in this scene.
        if (keep == null)
        {
            Debug.LogWarning("[TrailerShotSolo] This shot's camera is not in this scene — leaving every camera alone.");
            return;
        }

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
