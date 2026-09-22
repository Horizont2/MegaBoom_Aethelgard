using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

// Trailer, the last shot — the castle standing in the fog, found by a crane.
//
// ==== WHY IT IS FILMED IN THE LIVE GAME AND NOT IN A BUILT SET ====
//
// The castle is not a prop that can be dropped into a trailer scene. It is
// Location_Castle: nineteen hundred nested pieces, placed by WorldGenerator on a
// hill it raises for it, with the generated roads climbing the slope to reach
// it. Rebuilding that by hand in a scene of its own would be days of dressing to
// arrive at something the game already assembles perfectly well on its own —
// and it would drift out of date the moment the location is edited.
//
// Region 24 is the only region whose regionTotemPrefab is that castle, so
// generating region 24 IS the set. What the game puts around it — enemies,
// points of interest, roadside events, a HUD — is what has to go, and that is
// all this does before it starts filming.
//
// ==== EVERYTHING IS MEASURED, NOTHING IS TYPED ====
//
// The world is different every run: the castle lands somewhere else, on a hill
// of its own height, facing whichever way the generator felt like. So there is
// not a single hand-typed position in this file. The castle's renderer bounds
// are measured, the approach is chosen from the open ground around it, and every
// distance is a multiple of its own radius. It frames itself.
[DisallowMultipleComponent]
public class TrailerCastleShot : MonoBehaviour
{
    [Header("The set")]
    [Tooltip("Region 24 — the only one whose location is the castle. Handed to MissionInitializer before the generator runs, so the world builds itself around it.")]
    public RegionData region;
    [Tooltip("Seconds to wait for the world to finish generating before giving up and saying so.")]
    public float generationTimeout = 45f;
    [Tooltip("Part of the location prefab's name. The instance is called Location_Castle(Clone), so matching on this finds it however the generator renames things later.")]
    public string castleNameContains = "Castle";

    [Header("Clearing the field")]
    public bool removeEnemies = true;
    public bool removePOIs = true;
    public bool removeRoadDressing = true;
    [Tooltip("Names of the generator's own containers to delete whole. These are created by name in WorldGenerator, which is why matching on the name is safe.")]
    public string[] containersToRemove =
    {
        "POIContainer", "RoadDecorations", "CagedAllyEvent",
    };
    public bool hideHUD = true;

    [Header("The hero")]
    // ==== NOBODY IS IN THIS SHOT ====
    //
    // He was going to stand in the foreground for scale. He is not in it: the
    // trailer has just spent four episodes on people, and the last image works
    // because there is nobody left in it — the place, the fog and the dead.
    //
    // Scale comes from the torches instead. They are a known size and they recede
    // in a line toward the gate, which is a better ruler than one figure anyway
    // because it reads at every depth in the frame rather than at one.
    [Tooltip("Take the player out of the shot entirely. The generated world spawns him; this removes him.")]
    public bool removeHero = true;

    [Header("The shot")]
    // ==== THE CASTLE IS HIDDEN, AND THE STORM SHOWS IT TO YOU ====
    //
    // The first build craned in on a castle that was simply there, which is a
    // reveal of something already revealed. This one keeps it BURIED: for most
    // of the shot the snow and the fog are so thick that all the frame holds is
    // a faint mass and a line of fires. Then the sky opens — and for a fifth of
    // a second at a time the lightning puts the whole silhouette on the screen
    // and takes it away again. Only at the end does the camera move at all, and
    // only enough for the castle to come forward out of the fog.
    //
    // Four beats:
    //   HIDDEN  nothing but the torches. The audience is told where to look
    //           before being given anything to look at.
    //   STORM   strikes behind the walls. Each one is a whole castle, briefly.
    //   REVEAL  a modest push, the fog opens, and it stays.
    //   HOLD    it never quite stops.
    [Tooltip("Seconds buried in the snow, with only the fires to show where it is.")]
    public float hiddenSeconds = 4f;
    [Tooltip("Seconds of storm, in which the lightning is the only thing that shows the castle at all.")]
    public float stormSeconds = 5.5f;
    [Tooltip("Seconds of the push in which the fog finally opens.")]
    public float revealSeconds = 5f;
    [Tooltip("Seconds held on the final framing before the fade.")]
    public float holdSeconds = 2.6f;
    public float outFade = 1.8f;

    [Header("The crane")]
    // A dolly in a straight line is a zoom: nothing in frame changes its
    // relationship to anything else, so the brain reads it as the picture
    // getting bigger. What makes a move read as a MOVE is parallax, which needs
    // lateral travel — so the camera arcs. Modestly, here: this shot is about
    // the castle arriving, not about the camera going somewhere.
    [Tooltip("Metres from the castle's centre the shot opens at, as a multiple of the castle's own radius. Small on purpose — the old 3.4 put the lens beyond the edge of the generated terrain, which is where the void starts.")]
    public float startDistance = 2.4f;
    [Tooltip("And where the push ends.")]
    public float endDistance = 1.5f;
    [Tooltip("Metres the camera is kept INSIDE the terrain's edge. Past it there is no world, and a trailer frame with the engine's nothing in the corner of it is unusable.")]
    public float terrainMargin = 45f;
    [Tooltip("Degrees the camera travels AROUND the castle. This is the parallax; kept small because the reveal, not the travel, is the event.")]
    public float arcDegrees = 18f;
    [Tooltip("Degrees per second of continuing arc through the whole shot, so the frame is never locked off.")]
    public float driftDegreesPerSecond = 0.6f;
    public float startHeight = 3f;
    [Tooltip("Height at the end, as a fraction of the castle's own height. Below one: finishing above the towers looks down on them and loses the silhouette the whole shot is built on.")]
    public float endHeightFactor = 0.5f;
    [Tooltip("Metres the lens may be lifted to clear the hill the generator raises under the castle.")]
    public float maxClearLift = 26f;
    public float startFov = 52f;
    public float endFov = 38f;
    [Tooltip("How fast the aim catches up. Low numbers let the framing float, which is what separates an operated camera from a solved one.")]
    public float aimDamping = 1.6f;
    public float handheld = 0.02f;

    [Header("Lightning")]
    [Tooltip("The scene's key light, taken white for each strike. Left empty the render settings' sun is used.")]
    public Light keyLight;
    [Tooltip("How many fall during the storm beat.")]
    public int strikes = 4;
    [Tooltip("Metres BEHIND the castle each bolt lands, as a multiple of its radius. Behind, not among — a bolt inside the walls lights stonework; a bolt behind them makes the whole castle a silhouette, which is the only thing worth seeing through this much fog.")]
    public float strikeBehind = 1.4f;
    public Color lightningColour = new Color(0.82f, 0.88f, 1f);
    public float lightningIntensity = 16f;

    [Header("Torches")]
    // ==== THEY WERE INVISIBLE, AND THE ARITHMETIC SAYS WHY ====
    //
    // They were strung from the castle wall out to the OPENING camera distance,
    // so most of them sat sixty to a hundred metres away — and at the fog density
    // this shot runs, a sixty metre sightline passes about a tenth of the light
    // through. A torch that faint, three pixels across, is nothing.
    //
    // They start near the lens now and march away from it, so the closest few
    // are barely fogged at all and read as fire; the far ones dissolve into the
    // murk, which is the point — that dissolve IS the depth cue. And the lights
    // are much stronger, because in this shot they are the only warm thing in
    // the frame and they are competing with a snowstorm.
    [Tooltip("The game's own torch. In fog, warm points ARE the depth — without something to occlude at known distances the fog is a flat grey card.")]
    public GameObject torchPrefab;
    public int approachTorches = 16;
    [Tooltip("Where the NEAREST torch sits, as a fraction of the camera's opening distance. High, so the first ones are right by the lens.")]
    [Range(0.2f, 1f)] public float torchNearest = 0.85f;
    [Tooltip("How wide the two rows spread, as a multiple of the castle's radius.")]
    public float torchArcWidth = 1.1f;
    [Tooltip("This torch prefab's mesh is authored LYING DOWN and needs minus ninety on X to stand.")]
    public float torchStandUpX = -90f;
    // The torch prefab is a mesh and a flame particle, nothing else — no Light
    // anywhere on it. In the one shot of this trailer that is ABOUT volumetric
    // fog, a torch that illuminates nothing is a small orange sprite. A light in
    // volumetric fog is not a light, it is a visible cone of glowing air, and
    // that glow at a known distance is the whole mechanism by which fog reads as
    // depth rather than as a grey card.
    [Tooltip("Add a point light to each torch. Without it the flame lights nothing and the fog has nothing to carry.")]
    public bool torchesGiveLight = true;
    public Color torchLight = new Color(1f, 0.58f, 0.24f);
    public float torchLightRange = 26f;
    public float torchLightIntensity = 9f;
    [Range(0f, 0.6f)]
    [Tooltip("How much the flames breathe. Small: a torch that pulses hard reads as a bad effect, and a whole line of them pulsing together reads as a light rig.")]
    public float torchFlicker = 0.2f;

    [Header("Environment")]
    [Tooltip("Stop the day/night cycle and hold the hour the shot is lit for. Left running it walks the sun through the take.")]
    public bool holdTimeOfDay = true;
    [Tooltip("Sun elevation in degrees. Low — a raking light is what gives a silhouette an edge.")]
    public float sunElevation = 8f;
    public Color sunColour = new Color(0.72f, 0.78f, 1f);
    public float sunIntensity = 0.45f;
    public Color ambientSky = new Color(0.20f, 0.24f, 0.32f);
    public Color ambientEquator = new Color(0.14f, 0.16f, 0.22f);
    public Color ambientGround = new Color(0.06f, 0.06f, 0.08f);

    [Header("Fog")]
    // Held THICK for the first two beats and opened only in the reveal. It used
    // to thin steadily from the first frame, which meant the castle was always
    // arriving and never hidden — and a reveal of something already visible is
    // not a reveal.
    [Tooltip("Extinction per metre while it is buried. Thick enough that a hundred metres of it is a wall, which is what makes the lightning worth anything.")]
    public float fogDensityHidden = 0.085f;
    [Tooltip("And once it has come forward. It never fully clears — this is the castle emerging, not the weather ending.")]
    public float fogDensityRevealed = 0.03f;
    public float fogHeightHidden = 34f;
    public float fogHeightRevealed = 30f;
    [Range(0f, 1f)]
    [Tooltip("The least density the noise may leave. At zero it carves clear holes, and a hole in the one shot that is ABOUT fog reads as the fog switching off.")]
    public float fogNoiseFloor = 0.55f;
    public Color fogColour = new Color(0.58f, 0.63f, 0.72f);

    [Header("Weather")]
    // ==== REGION 24 IS A WINTER REGION ====
    //
    // Its regionBiome is 2. The snow in the take is not a bug and not a
    // coincidence — it is what this region is, and it is a better last image
    // than the rain I was about to force on it: snow drifting through a fogged
    // valley, lit by torches, above a dead castle.
    //
    // So no weather is spawned here. What IS done is making sure the game's own
    // winter branch actually engages: DayNightCycle reads the biome from
    // PlayerPrefs, which MissionInitializer normally writes on the way in from
    // the map. Coming straight into GameScene from a menu item skips that, so
    // the region's biome would be whatever was last played.
    [Tooltip("Write the region's biome to the PlayerPrefs key the game reads, since launching straight into GameScene skips the code that normally does it. Off, the weather is whatever the last real run left behind.")]
    public bool applyRegionBiome = true;

    [Header("Audio")]
    public string windBed = AudioID.Trailer_WindDesolate;
    [Tooltip("The low bed under the whole shot. It is what makes the wind feel like weather rather than like an empty track.")]
    public string dreadBed = AudioID.Trailer_Dread;
    [Tooltip("Lands as the castle resolves out of the fog.")]
    public string revealSting = AudioID.Trailer_ThunderClose;
    public string crowsCue = AudioID.Trailer_Crows;
    // ==== THE SILENCE IS THE CUE ====
    //
    // A sting on top of a bed that is already running is just louder. Cutting the
    // bed for a moment BEFORE it is what makes the sting arrive — the ear notices
    // the absence, and then something fills it. It is the cheapest trick in the
    // book and there is no substitute for it.
    [Tooltip("Seconds of held silence before the reveal sting.")]
    public float silenceBefore = 0.7f;
    [Range(0f, 1f)]
    [Tooltip("How far through the crane the reveal lands.")]
    public float revealAt = 0.55f;

    [Header("Title card")]
    // ==== THE LAST SHOT OF A TRAILER IS NOT THE LAST FRAME ====
    //
    // This one ended on a fade and nothing else, and until now the trailer has
    // never said what the game is called. The name is read from the project
    // itself — Application.productName — rather than typed here, so it cannot
    // drift away from what the build is actually called.
    [Tooltip("Show the game's name after the fade.")]
    public bool showTitle = true;
    [Tooltip("Artwork, if there ever is any. Left empty the name is set as text, which is also what a title card mostly is.")]
    public Sprite titleSprite;
    public float titleFadeIn = 1.2f;
    public float titleHold = 2.4f;
    public float titleFadeOut = 1.4f;
    public int titleFontSize = 96;
    public Color titleColour = new Color(0.92f, 0.9f, 0.86f);

    [Header("Diagnostics")]
    public bool autoPlay = true;
    public bool IsFinished { get; private set; }

    // ---- runtime -------------------------------------------------------------

    private Transform castle;
    private Bounds castleBounds;
    private float castleRadius;
    private Vector3 approach;          // horizontal, from the castle toward the lens
    private Camera cam;
    private readonly List<Transform> torches = new List<Transform>();
    private readonly List<Light> flames = new List<Light>();
    private readonly List<float> flamePhase = new List<float>();

    // ==== IT ARMS ITSELF, RATHER THAN BEING SWITCHED ON ====
    //
    // This used to sit inactive in the scene and be activated by the launcher in
    // AfterSceneLoad. That works in theory — AfterSceneLoad is after every Awake
    // and before the first Start — but it makes the one thing this component MUST
    // do, naming the region before WorldGenerator's Start reads it, depend on a
    // subtlety of activation ordering. It is the kind of thing that is correct
    // until it is not, and when it is not the symptom is a world generated for
    // the wrong region with no error anywhere.
    //
    // So the object is simply ACTIVE in the scene and asks for itself whether
    // this run is the castle shot. An ordinary Awake on an ordinary active
    // object is guaranteed to be before every Start in the scene; there is
    // nothing left to get wrong.
    private bool armed;

    private void Awake()
    {
        armed = Armed();
        if (!armed) { enabled = false; return; }

        // The only moment this is any use: it is what the generator reads in its
        // Start to decide which location to build the world around.
        if (region != null)
        {
            MissionInitializer.PendingMissionRegion = region;
            // Winter, for region 24 — and written here because coming straight
            // into GameScene skips MissionInitializer, which is what normally
            // writes it. Without it the snow, the winter lighting and the
            // ground drift are whichever region was played last.
            if (applyRegionBiome) PlayerPrefs.SetInt("RegionBiomeType", (int)region.regionBiome);
        }
        else Debug.LogError("[TrailerCastleShot] No Region assigned — the generator will build whatever region it was already going to, and there will be no castle.");
    }

    private static bool Armed()
    {
#if UNITY_EDITOR
        return UnityEditor.SessionState.GetString(TrailerShotSolo.SessionKey, string.Empty) == TrailerShotSolo.CastleShot;
#else
        return false;
#endif
    }

    private void Start()
    {
        if (armed && autoPlay) StartCoroutine(Run());
    }

    private IEnumerator Run()
    {
        TrailerLogGuard.Arm();

        // ---- 1. wait for the world ----
        //
        // On the generator's own flag rather than on the castle appearing. The
        // location is placed early in a build that goes on for a while after it,
        // so watching for the castle meant measuring and dressing a world that
        // was still being assembled around it.
        float waited = 0f;
        float lastLogged = -1f;
        while (waited < generationTimeout)
        {
            if (WorldGenerator.IsGenerationDone)
            {
                castle = FindCastle();
                if (castle != null) break;
            }

            // Generating region 24 takes a while and a silent editor looks hung.
            if (WorldGenerator.CurrentProgress - lastLogged >= 0.25f)
            {
                lastLogged = WorldGenerator.CurrentProgress;
                Debug.Log("[TrailerCastleShot] Building region 24… " +
                          Mathf.RoundToInt(WorldGenerator.CurrentProgress * 100f) + "%");
            }

            waited += Time.unscaledDeltaTime;
            yield return null;
        }

        if (castle == null)
        {
            Debug.LogError("[TrailerCastleShot] No location named '" + castleNameContains + "' after " +
                           generationTimeout + "s. Region 24 is the one whose regionTotemPrefab is Location_Castle — " +
                           "check the Region field, and that the generator actually ran.");
            IsFinished = true;
            yield break;
        }

        // A couple of frames for the generator to finish grounding and dressing
        // it before anything is measured off it.
        yield return null;
        yield return null;

        Measure();

        // ---- 2. clear the field ----
        ClearField();

        // ---- 3. dress and light ----
        cam = Camera.main;
        if (cam == null)
        {
            Debug.LogError("[TrailerCastleShot] No main camera.");
            IsFinished = true;
            yield break;
        }
        TakeCamera();
        ChooseApproach();
        SetEnvironment();
        BuildLightning();
        ReleaseLightning();
        PlaceTorches();
        RemoveHero();

        var polish = TrailerCinematicPolish.GetOrCreate();
        polish.OpenTrailer();
        TrailerAudio.SilenceStaleBeds();
        Loop(windBed);
        Loop(dreadBed);

        // ---- 4. HIDDEN. Nothing but the fires. ----
        yield return Beat(hiddenSeconds, 0f, 0f);

        // ---- 5. STORM. Each bolt is a whole castle, briefly. ----
        Cue(crowsCue);
        StartCoroutine(StormBeat());
        yield return Beat(stormSeconds, 0f, 0f);

        // ---- 6. REVEAL. The push, and the fog opens. ----
        //
        // The beds drop out first and there is most of a second of nothing. A
        // sting on top of a bed that is already running is just louder; the
        // silence is what makes it arrive.
        DropOut();
        yield return new WaitForSecondsRealtime(silenceBefore);
        Cue(revealSting);
        Loop(windBed);

        float t = 0f;
        while (t < revealSeconds)
        {
            t += Time.unscaledDeltaTime;
            float k = Mathf.Clamp01(t / Mathf.Max(0.01f, revealSeconds));

            // Smootherstep: zero velocity AND zero acceleration at both ends, so
            // the move never announces its start or its stop.
            float e = k * k * k * (k * (k * 6f - 15f) + 10f);

            drift += driftDegreesPerSecond * Time.unscaledDeltaTime;
            PlaceCamera(e, drift);
            ApplyFog(e);
            FlickerTorches();
            yield return null;
        }

        // ---- 7. HOLD. It never quite stops. ----
        float hold = 0f;
        while (hold < holdSeconds)
        {
            hold += Time.unscaledDeltaTime;
            drift += driftDegreesPerSecond * Time.unscaledDeltaTime;
            PlaceCamera(1f, drift);
            FlickerTorches();
            yield return null;
        }

        polish.FadeToBlack(outFade);
        yield return new WaitForSecondsRealtime(outFade);
        DropOut();

        if (showTitle) yield return StartCoroutine(TitleCard());
        IsFinished = true;
    }

    // A stretch in which the camera does not travel: it only keeps its drift, so
    // the frame breathes without going anywhere. Used for the two beats before
    // the reveal, which are about waiting rather than about moving.
    private float drift;

    private IEnumerator Beat(float seconds, float e, float fogK)
    {
        float t = 0f;
        while (t < seconds)
        {
            t += Time.unscaledDeltaTime;
            drift += driftDegreesPerSecond * Time.unscaledDeltaTime;
            PlaceCamera(e, drift);
            ApplyFog(fogK);
            FlickerTorches();
            yield return null;
        }
    }

    // ---- the set ---------------------------------------------------------------

    private Transform FindCastle()
    {
        // By the component first: a generated location always carries one, and
        // that is a far better filter than a name.
        foreach (var sc in Object.FindObjectsByType<SelfContainedLocation>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
        {
            if (sc == null) continue;
            if (string.IsNullOrEmpty(castleNameContains) ||
                sc.name.IndexOf(castleNameContains, System.StringComparison.OrdinalIgnoreCase) >= 0)
                return sc.transform;
        }
        return null;
    }

    private void Measure()
    {
        castleBounds = new Bounds(castle.position, Vector3.one);
        bool any = false;
        foreach (var r in castle.GetComponentsInChildren<Renderer>())
        {
            if (r == null || r is ParticleSystemRenderer) continue;
            if (!any) { castleBounds = r.bounds; any = true; }
            else castleBounds.Encapsulate(r.bounds);
        }
        castleRadius = Mathf.Max(8f, new Vector2(castleBounds.extents.x, castleBounds.extents.z).magnitude);
    }

    // ==== WHICH SIDE TO FILM IT FROM ====
    //
    // The generator puts the castle down facing wherever it likes, so there is no
    // "front" to point at, and it raises a twenty-two metre hill under it — which
    // is the thing that actually decides the shot. From the wrong side the first
    // half of the crane is looking at a slope.
    //
    // This used to pick the nearest road segment, which was a guess about how
    // WorldGenerator names and splits its road meshes rather than a measurement.
    // The terrain itself answers the question and cannot be wrong about it: sample
    // a ring around the castle and take the direction the land falls away
    // FURTHEST. That is the open valley side, the longest clear sightline up to
    // the walls, and the side the roads climb anyway.
    private void ChooseApproach()
    {
        Vector3 centre = castleBounds.center;
        float bestDrop = float.NegativeInfinity;
        Vector3 best = Vector3.forward;

        const int Samples = 24;
        for (int i = 0; i < Samples; i++)
        {
            float a = i / (float)Samples * Mathf.PI * 2f;
            Vector3 dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));

            // Averaged over three distances out, so a single dip or boulder near
            // the wall cannot win the vote.
            float drop = 0f;
            for (int r = 1; r <= 3; r++)
            {
                Vector3 p = centre + dir * (castleRadius * (1f + r * 0.9f));
                drop += centre.y - Ground(p);
            }

            if (drop > bestDrop) { bestDrop = drop; best = dir; }
        }

        approach = best;
    }

    private void ClearField()
    {
        int killed = 0;

        if (removeEnemies)
        {
            EnemySpawner.IsSpawningBlocked = true;
            foreach (var s in Object.FindObjectsByType<EnemySpawner>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (s != null) s.enabled = false;

            // DestroyImmediate for the same reason TrailerPuppet.Strip uses it:
            // a deferred Destroy still lets Start run, and an EnemyAI's Start is
            // where it registers, wakes its agent and puts a health bar on
            // screen — over the shot.
            foreach (var e in Object.FindObjectsByType<EnemyAI>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (e != null) { DestroyImmediate(e.gameObject); killed++; }

            foreach (var b in Object.FindObjectsByType<TutorialBossAI>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (b != null) { DestroyImmediate(b.gameObject); killed++; }
        }

        if (removePOIs || removeRoadDressing)
        {
            foreach (string n in containersToRemove)
            {
                if (string.IsNullOrEmpty(n)) continue;
                if (!removePOIs && n == "POIContainer") continue;
                if (!removeRoadDressing && n != "POIContainer") continue;

                foreach (var go in AllByName(n)) { Destroy(go); killed++; }
            }
        }

        // ==== THE TOTEM FIRES A COLUMN OF LIGHT INTO THE SKY ====
        //
        // Location_Castle carries three RegionTotems, and a totem's job in the
        // game is to be visible from anywhere on the map — so each one throws a
        // sky beam. Three pillars of particles rising out of the towers is a
        // gameplay marker, and it is the loudest thing in a frame that is
        // otherwise a silhouette in a snowstorm.
        foreach (var totem in Object.FindObjectsByType<RegionTotem>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (totem == null) continue;
            if (totem.skyBeamVFX != null)
            {
                totem.skyBeamVFX.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                totem.skyBeamVFX.gameObject.SetActive(false);
            }
            // The component too: its Update re-enables the beam on its own cues,
            // and nothing about a capture objective belongs in this shot.
            totem.enabled = false;
            killed++;
        }

        if (hideHUD)
        {
            // Every screen-space canvas in the scene, which is the HUD, the
            // minimap and anything else that draws over the world. The trailer's
            // own overlay is created after this and is not caught.
            foreach (var c in Object.FindObjectsByType<Canvas>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
                if (c != null && c.renderMode != RenderMode.WorldSpace) c.gameObject.SetActive(false);
        }

        Debug.Log("[TrailerCastleShot] Cleared " + killed + " object(s) off the set. The castle and the land are what is left.");
    }

    // The camera belongs to the shot from here. CameraFollow would drag it back
    // to the player on the very next frame, and the occlusion fader would start
    // dissolving whatever stands between the lens and him.
    private void TakeCamera()
    {
        foreach (var f in cam.GetComponentsInChildren<MonoBehaviour>(true))
        {
            if (f == null) continue;
            if (f is CameraFollow || f is CameraOcclusion) f.enabled = false;
        }
    }

    private void RemoveHero()
    {
        if (!removeHero) return;

        GameObject hero = GameObject.FindGameObjectWithTag("Player");
        if (hero == null) return;

        // Destroyed rather than hidden. CameraFollow and the occlusion fader both
        // hunt for him by tag, several systems keep him alive, and a hidden
        // player still has a CharacterController quietly sliding down the hill
        // for the length of the take.
        DestroyImmediate(hero);
    }

    // ---- torches ---------------------------------------------------------------

    // In fog, warm points ARE the depth. Without something to occlude at known
    // distances, volumetric fog is a flat grey card — the eye has nothing to
    // measure it against. A line of fires receding toward the gate does more for
    // the sense of scale than any amount of density tuning.
    private void PlaceTorches()
    {
        if (torchPrefab == null || approachTorches <= 0) return;

        for (int i = 0; i < approachTorches; i++)
        {
            float k = approachTorches > 1 ? i / (float)(approachTorches - 1) : 0.5f;

            // Two rows, alternating, running from just in front of the lens all
            // the way in to the wall — so the near ones pass the camera on both
            // sides and the far ones converge on the gate.
            float along = Mathf.Lerp(castleRadius * startDistance * torchNearest, castleRadius * 1.05f, k);
            float outward = (i % 2 == 0 ? 1f : -1f) * torchArcWidth * castleRadius * 0.5f * (1f - k * 0.6f);

            Vector3 across = new Vector3(approach.z, 0f, -approach.x);
            Vector3 p = castleBounds.center + approach * along + across * outward;
            p.y = Ground(p);

            var go = Instantiate(torchPrefab, p, Quaternion.Euler(torchStandUpX, Random.Range(0f, 360f), 0f), transform);
            go.name = "Castle_Torch_" + i;
            foreach (var c in go.GetComponentsInChildren<Collider>(true)) if (c != null) c.enabled = false;

            if (torchesGiveLight)
            {
                var lgo = new GameObject("Flame");
                lgo.transform.SetParent(go.transform, true);
                // Placed in WORLD space, because the prop is rotated ninety
                // degrees to stand up and the flame belongs above its head, not
                // out of the side of it.
                lgo.transform.position = p + Vector3.up * 1.6f;

                var l = lgo.AddComponent<Light>();
                l.type = LightType.Point;
                l.color = torchLight;
                l.range = torchLightRange;
                l.intensity = torchLightIntensity;
                // No shadows. Fourteen shadowed point lights is fourteen cube
                // maps, and in fog the glow is doing the work, not the shadow.
                l.shadows = LightShadows.None;
                flames.Add(l);
                flamePhase.Add(Random.Range(0f, 10f));
            }

            torches.Add(go.transform);
        }
    }

    // Perlin rather than random, and at a different rate per torch, so the line
    // never pulses as one. Random flicker reads as a fault in a bulb; Perlin at
    // incommensurate rates reads as fire.
    private void FlickerTorches()
    {
        if (torchFlicker <= 0.001f) return;
        float t = Time.unscaledTime;
        for (int i = 0; i < flames.Count; i++)
        {
            if (flames[i] == null) continue;
            float n = Mathf.PerlinNoise(flamePhase[i], t * 2.3f) - 0.5f;
            flames[i].intensity = torchLightIntensity * (1f + n * 2f * torchFlicker);
        }
    }

    // ---- environment -----------------------------------------------------------

    private void SetEnvironment()
    {
        if (holdTimeOfDay)
        {
            var cycle = Object.FindFirstObjectByType<DayNightCycle>(FindObjectsInactive.Include);
            if (cycle != null) cycle.enabled = false;
        }

        Light sun = RenderSettings.sun;
        if (sun == null)
        {
            foreach (var l in Object.FindObjectsByType<Light>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
                if (l != null && l.type == LightType.Directional) { sun = l; break; }
        }

        if (sun != null)
        {
            // Raking, and from BEHIND the castle: a silhouette needs its edge lit
            // from the far side, and front-lighting a fog shot flattens it into
            // grey. Aimed down the approach, so the light comes over the walls
            // toward the lens.
            sun.transform.rotation = Quaternion.LookRotation(approach) * Quaternion.Euler(sunElevation, 0f, 0f);
            sun.color = sunColour;
            sun.intensity = sunIntensity;
            sun.shadows = LightShadows.Soft;
        }

        RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
        RenderSettings.ambientSkyColor = ambientSky;
        RenderSettings.ambientEquatorColor = ambientEquator;
        RenderSettings.ambientGroundColor = ambientGround;
        RenderSettings.fogColor = fogColour;
        RenderSettings.fog = false;          // the volumetric one, or the two stack
    }

    // Pure Volumetric Fog lives in its own assembly, so a direct reference would
    // stop this file compiling for anyone who removes the package. Reached by
    // name and written through reflection, the same way DayNightCycle does it.
    private Component fog;
    private FieldInfo fDensity, fHeight, fFloor, fFollowColour, fColour;

    private void ApplyFog(float k)
    {
        if (fog == null)
        {
            System.Type ty = System.Type.GetType("BKPureNature.PureVolumetricFog, BKPureNature.PureVolumetricFog");
            if (ty == null) return;
            fog = Object.FindFirstObjectByType(ty) as Component;
            if (fog == null) return;

            fDensity = Field("density");
            fHeight = Field("groundFogHeight");
            fFloor = Field("noiseFloor");
            fFollowColour = Field("followSceneFogColor");
            fColour = Field("fogColor");

            // Held even rather than left to the noise. This is the one shot in
            // the trailer that is ABOUT fog, and a carved hole in it reads as the
            // fog switching off rather than thinning.
            if (fFloor != null) fFloor.SetValue(fog, fogNoiseFloor);
            if (fFollowColour != null) fFollowColour.SetValue(fog, false);
            if (fColour != null) fColour.SetValue(fog, fogColour);
        }

        // Held at the hidden value for the first two beats — k is zero through
        // both — and opened only across the reveal. It never reaches clear: this
        // is the castle coming forward, not the weather ending.
        if (fDensity != null) fDensity.SetValue(fog, Mathf.Lerp(fogDensityHidden, fogDensityRevealed, k));
        if (fHeight != null) fHeight.SetValue(fog, Mathf.Lerp(fogHeightHidden, fogHeightRevealed, k));
    }

    private FieldInfo Field(string name)
    {
        return fog.GetType().GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
    }

    // ---- camera ----------------------------------------------------------------

    // `e` is the eased progress of the crane, 0..1. `drift` is the extra arc the
    // settle and the hold keep adding, in degrees, so the frame is never locked.
    private Vector3 CameraPosition(float e, float drift)
    {
        float dist = Mathf.Lerp(castleRadius * startDistance, castleRadius * endDistance, e);

        // The arc is what makes this a move. Weighted toward the second half —
        // the opening wants to be nearly still, and the travel wants to be
        // happening while the castle is already resolving out of the fog.
        float yaw = arcDegrees * (e * e * (3f - 2f * e)) + drift;
        Vector3 dir = Quaternion.Euler(0f, yaw, 0f) * approach;

        Vector3 p = castleBounds.center + dir * dist;
        p = KeepOnTerrain(p);
        float high = Mathf.Lerp(startHeight, castleBounds.size.y * endHeightFactor, e);
        p.y = Ground(p) + high;
        return LiftClear(p);
    }

    // ==== PAST THE TERRAIN THERE IS NO WORLD ====
    //
    // The castle can land anywhere the generator likes, including near the rim,
    // and the opening framing was three and a bit castle-radii out — which on a
    // rim placement is beyond the edge of the heightmap. There is nothing out
    // there: no ground, no fog volume, no skybox behind the border mountains,
    // just the engine's clear colour in the corner of a trailer frame.
    //
    // So the lens is pulled back inside, with a margin. Pulled rather than
    // clamped per-axis, because clamping X and Z independently slides the camera
    // along the edge and quietly changes the angle the whole shot was composed
    // on; moving it back along its own line toward the castle keeps the
    // composition and only loses a little distance.
    private Vector3 KeepOnTerrain(Vector3 p)
    {
        Terrain t = Terrain.activeTerrain;
        if (t == null || t.terrainData == null) return p;

        Vector3 o = t.transform.position;
        Vector3 size = t.terrainData.size;
        float minX = o.x + terrainMargin, maxX = o.x + size.x - terrainMargin;
        float minZ = o.z + terrainMargin, maxZ = o.z + size.z - terrainMargin;

        if (p.x >= minX && p.x <= maxX && p.z >= minZ && p.z <= maxZ) return p;

        Vector3 centre = castleBounds.center;
        Vector3 away = p - centre; away.y = 0f;
        float full = away.magnitude;
        if (full < 0.01f) return p;

        // Walk in along the same line until it is inside, in a few steps. Cheap,
        // exact enough, and it cannot change the bearing.
        Vector3 dir = away / full;
        for (float d = full; d > castleRadius * 1.1f; d -= 2f)
        {
            Vector3 q = centre + dir * d;
            if (q.x >= minX && q.x <= maxX && q.z >= minZ && q.z <= maxZ)
                return new Vector3(q.x, p.y, q.z);
        }
        return p;
    }

    // ==== THE HILL IS BETWEEN THE LENS AND THE CASTLE ====
    //
    // Location_Castle is placed with raiseHill on and a hillHeight of twenty-two,
    // so the generator builds a plateau under it. Opening three castle-radii out
    // and two metres off the valley floor therefore aims the first half of the
    // crane straight into a slope — the castle is up there, behind the ground.
    //
    // Rather than guess a starting height that works on one seed, the camera asks
    // whether it can see the crown and walks upward until it can. Self-correcting
    // on any terrain the generator produces, which is the only kind of answer
    // worth having when the set is different every run.
    private Vector3 LiftClear(Vector3 p)
    {
        Vector3 crown = castleBounds.center;
        crown.y = castleBounds.max.y - castleBounds.size.y * 0.2f;

        const int Steps = 14;
        float step = maxClearLift / Steps;

        for (int i = 0; i <= Steps; i++)
        {
            Vector3 from = p + Vector3.up * (step * i);
            Vector3 to = crown - from;

            // Clear if nothing is in the way, or if the first thing in the way is
            // the castle itself — which is exactly what should be hit.
            if (!Physics.Raycast(from, to.normalized, out RaycastHit hit, to.magnitude, ~0, QueryTriggerInteraction.Ignore))
                return from;
            if (hit.transform != null && hit.transform.IsChildOf(castle)) return from;
        }

        // Nothing cleared inside the allowance: take the highest tried rather than
        // the lowest, so a bad seed gives a high wide rather than a shot of mud.
        return p + Vector3.up * maxClearLift;
    }

    private Vector3 aimNow;
    private bool aimSeeded;

    private void PlaceCamera(float e, float drift)
    {
        Vector3 p = CameraPosition(e, drift);

        // Aimed at the castle's middle at the start and at its crown by the end,
        // so the rise is felt as looking further UP the building rather than as
        // the horizon dropping.
        // Aimed at the middle at the start and at the CROWN by the end. With the
        // camera finishing below the towers that means the last framing looks
        // slightly up at them, which is what keeps them standing over the lens
        // instead of being surveyed from above.
        Vector3 aim = castleBounds.center;
        aim.y = Mathf.Lerp(castleBounds.center.y, castleBounds.max.y, e);

        float amp = handheld * (1f - e * 0.4f);
        float tt = Time.unscaledTime;
        Vector3 shake = new Vector3(Mathf.PerlinNoise(tt * 1.1f, 0f) - 0.5f,
                                    Mathf.PerlinNoise(0f, tt * 1.7f) - 0.5f,
                                    Mathf.PerlinNoise(tt * 0.9f, 5f) - 0.5f) * (amp * 2f);

        // Damped, so the framing FLOATS toward where it should be instead of
        // being solved exactly every frame. A camera whose aim is always already
        // correct is the single clearest sign that nobody is holding it.
        if (!aimSeeded) { aimNow = aim; aimSeeded = true; }
        aimNow = Vector3.Lerp(aimNow, aim, 1f - Mathf.Exp(-aimDamping * Time.unscaledDeltaTime));

        cam.transform.position = p + shake;
        cam.transform.rotation = Quaternion.LookRotation((aimNow - p).normalized);
        cam.fieldOfView = Mathf.Lerp(startFov, endFov, e);
    }

    // ---- the storm ---------------------------------------------------------------

    private TrailerLightningStrike bolt;
    private LineRenderer boltLine;

    // Built during the wait for the world, parked as a millimetre of line two
    // metres in front of the lens: inside the frustum, sub-pixel, so its material
    // is compiled long before the first strike. TrailerLightningStrike keeps its
    // renderer off until it fires, which would otherwise make that first bolt a
    // cold draw — the same synchronous shader compile that used to lock the
    // editor on the statue's debris.
    private void BuildLightning()
    {
        if (cam == null) return;

        var go = new GameObject("Castle_Lightning");
        go.transform.SetParent(cam.transform, false);

        // The LineRenderer first: TrailerLightningStrike requires one, and its
        // Awake — which runs the instant AddComponent returns — configures it.
        boltLine = go.AddComponent<LineRenderer>();
        bolt = go.AddComponent<TrailerLightningStrike>();
        bolt.thunderId = AudioID.Trailer_ThunderClose;
        bolt.height = Mathf.Max(60f, castleBounds.size.y * 2.5f);

        boltLine.useWorldSpace = false;
        boltLine.positionCount = 2;
        boltLine.SetPosition(0, new Vector3(0f, 0f, 2f));
        boltLine.SetPosition(1, new Vector3(0f, 0.001f, 2f));
        boltLine.widthMultiplier = 0.0004f;
        boltLine.enabled = true;
    }

    private void ReleaseLightning()
    {
        if (bolt == null) return;
        boltLine.enabled = false;
        boltLine.useWorldSpace = true;
        boltLine.widthMultiplier = bolt.boltWidth;
        bolt.transform.SetParent(null, true);
    }

    // Bolts spaced across the storm beat, each one BEHIND the walls so the whole
    // castle becomes a silhouette for a fifth of a second and then is gone again.
    // Irregular on purpose: a storm on a metronome is a strobe.
    private IEnumerator StormBeat()
    {
        int n = Mathf.Max(1, strikes);
        for (int i = 0; i < n; i++)
        {
            float wait = stormSeconds / n * Random.Range(0.55f, 1.35f);
            yield return new WaitForSecondsRealtime(wait);
            StartCoroutine(Strike());
        }
    }

    private IEnumerator Strike()
    {
        if (bolt != null)
        {
            Vector3 at = castleBounds.center - approach * (castleRadius * strikeBehind)
                       + new Vector3(Random.Range(-1f, 1f), 0f, Random.Range(-1f, 1f)) * castleRadius * 0.6f;
            at.y = Ground(at);
            bolt.Strike(at);
        }

        Light key = keyLight != null ? keyLight : RenderSettings.sun;
        if (key == null) yield break;

        Color c0 = key.color;
        float i0 = key.intensity;

        // Real lightning is not one flash. It is two or three inside a tenth of a
        // second, which is the difference between a light being switched on and
        // something happening in the sky.
        int flickers = Random.Range(2, 4);
        for (int i = 0; i < flickers; i++)
        {
            key.color = lightningColour;
            key.intensity = lightningIntensity * Random.Range(0.6f, 1f);
            yield return new WaitForSecondsRealtime(Random.Range(0.03f, 0.07f));
            key.color = c0;
            key.intensity = i0;
            yield return new WaitForSecondsRealtime(Random.Range(0.02f, 0.06f));
        }

        key.color = c0;
        key.intensity = i0;
    }

    // ---- title card --------------------------------------------------------------

    private IEnumerator TitleCard()
    {
        var canvasGO = new GameObject("TrailerTitle");
        var canvas = canvasGO.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 6000;                       // above the polish veil
        var scaler = canvasGO.AddComponent<UnityEngine.UI.CanvasScaler>();
        scaler.uiScaleMode = UnityEngine.UI.CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);

        var go = new GameObject("Title", typeof(RectTransform));
        go.transform.SetParent(canvasGO.transform, false);
        var rt = (RectTransform)go.transform;
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;

        var group = go.AddComponent<CanvasGroup>();
        group.alpha = 0f;

        if (titleSprite != null)
        {
            var img = go.AddComponent<UnityEngine.UI.Image>();
            img.sprite = titleSprite;
            img.preserveAspect = true;
            img.raycastTarget = false;
        }
        else
        {
            // The name is READ FROM THE PROJECT, not typed here, so a card cannot
            // end up saying something the build does not.
            var text = go.AddComponent<TMPro.TextMeshProUGUI>();
            // Whatever font the project already uses. Assigned only if there is
            // one — handing TextMeshPro a null font asset throws rather than
            // falling back, and it picks its own default perfectly well when the
            // field is simply left alone.
            if (TMPro.TMP_Settings.defaultFontAsset != null) text.font = TMPro.TMP_Settings.defaultFontAsset;
            text.text = Application.productName;
            text.fontSize = titleFontSize;
            text.color = titleColour;
            text.alignment = TMPro.TextAlignmentOptions.Center;
            text.characterSpacing = 14f;                  // a title breathes
            text.raycastTarget = false;
        }

        yield return FadeGroup(group, 0f, 1f, titleFadeIn);
        yield return new WaitForSecondsRealtime(titleHold);
        yield return FadeGroup(group, 1f, 0f, titleFadeOut);
    }

    private static IEnumerator FadeGroup(CanvasGroup g, float from, float to, float seconds)
    {
        float t = 0f;
        while (t < seconds)
        {
            t += Time.unscaledDeltaTime;
            g.alpha = Mathf.Lerp(from, to, Mathf.Clamp01(t / Mathf.Max(0.01f, seconds)));
            yield return null;
        }
        g.alpha = to;
    }

    // ---- helpers ---------------------------------------------------------------

    private float Ground(Vector3 p)
    {
        Terrain[] all = Terrain.activeTerrains;
        if (all != null)
        {
            foreach (var t in all)
            {
                if (t == null || t.terrainData == null) continue;
                Vector3 o = t.transform.position, s = t.terrainData.size;
                if (p.x < o.x || p.x > o.x + s.x || p.z < o.z || p.z > o.z + s.z) continue;
                return t.SampleHeight(p) + o.y;
            }
        }

        // The castle sits on a plateau the generator raised, and that plateau is
        // terrain — but its own floor is not, so a point on the courtyard needs
        // the raycast.
        if (Physics.Raycast(p + Vector3.up * 400f, Vector3.down, out RaycastHit hit, 900f, ~0, QueryTriggerInteraction.Ignore))
            return hit.point.y;

        return castleBounds.min.y;
    }

    private static Transform FindByName(string name)
    {
        foreach (var t in Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            if (t != null && t.name == name && t.gameObject.scene.IsValid()) return t;
        return null;
    }

    private static List<GameObject> AllByName(string name)
    {
        var list = new List<GameObject>();
        foreach (var t in Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            if (t != null && t.name == name && t.gameObject.scene.IsValid()) list.Add(t.gameObject);
        return list;
    }

    // ---- audio -----------------------------------------------------------------

    private readonly List<string> beds = new List<string>();

    private void Cue(string id)
    {
        if (AudioManager.Instance == null || string.IsNullOrEmpty(id)) return;
        AudioManager.Instance.PlaySFX(id);
    }

    private void Loop(string id)
    {
        if (AudioManager.Instance == null || string.IsNullOrEmpty(id)) return;
        AudioManager.Instance.PlaySFX(id);
        beds.Add(id);
    }

    private void DropOut()
    {
        if (AudioManager.Instance == null) return;
        for (int i = 0; i < beds.Count; i++) AudioManager.Instance.StopLoopedBed(beds[i]);
        beds.Clear();
    }
}
