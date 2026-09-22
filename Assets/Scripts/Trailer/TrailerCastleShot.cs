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

    [Header("The crane")]
    // ==== A DOLLY IN A STRAIGHT LINE IS A ZOOM ====
    //
    // The first version travelled straight down one axis with eight degrees of
    // yaw on it, and that is not a camera move — nothing in frame changes its
    // relationship to anything else, so the brain reads the whole thing as the
    // picture getting bigger. Which is exactly what it looked like.
    //
    // What makes a move read as a MOVE is parallax: near things must slide
    // against far things. That needs lateral travel, so the camera ARCS — forty
    // odd degrees around the hill while it rises and closes. The towers separate
    // from each other, the torch line sweeps through the foreground, and the
    // castle turns to show a second face. Every one of those is the shot telling
    // you the camera is somewhere, which a zoom cannot do at any focal length.
    //
    // Three timed sections, and it needs all three:
    //   SETTLE  a beat where almost nothing happens. The castle is a shape in
    //           the fog and you are given time to notice it before being shown
    //           it. Openings that start moving have nothing to open ON.
    //   CRANE   the arc, the rise, the push, the lens.
    //   HOLD    it never quite stops. A camera that halts dead announces that a
    //           move was being executed.
    [Tooltip("Seconds held nearly still at the start, on the silhouette, before the crane begins.")]
    public float settleSeconds = 2.2f;
    [Tooltip("Metres from the castle's centre the shot opens at, as a multiple of the castle's own radius.")]
    public float startDistance = 3.4f;
    [Tooltip("And where the push ends. Smaller is closer.")]
    public float endDistance = 1.8f;
    [Tooltip("Degrees the camera travels AROUND the castle across the move. This is the parallax, and it is the difference between a camera move and a zoom.")]
    public float arcDegrees = 46f;
    [Tooltip("Degrees per second of continuing arc during the settle and the hold, so the frame is never truly locked off.")]
    public float driftDegreesPerSecond = 0.7f;
    public float startHeight = 2.2f;
    [Tooltip("Height at the end, as a fraction of the castle's own height. Deliberately below one: finishing ABOVE the towers looks down on them and loses the silhouette the whole shot is built on. Just under the crown, looking up, is the frame.")]
    public float endHeightFactor = 0.62f;
    [Tooltip("Metres the lens may be lifted to clear the hill. The castle sits on a twenty-two metre plateau the generator raises for it, so from the valley the crane can be looking straight at a slope — this walks the camera up until the crown is actually visible.")]
    public float maxClearLift = 26f;
    public float startFov = 58f;
    public float endFov = 40f;
    [Tooltip("Seconds the crane itself takes. Slow — this is the one shot in the trailer that is allowed to breathe.")]
    public float craneSeconds = 12f;
    [Tooltip("How fast the aim catches up to where it should be. Low numbers let the framing float, which is what separates an operated camera from a solved one.")]
    public float aimDamping = 1.6f;
    public float handheld = 0.02f;
    [Tooltip("Seconds held on the final framing before the fade.")]
    public float holdSeconds = 2.4f;
    public float outFade = 1.8f;

    [Header("Torches")]
    [Tooltip("The game's own torch. In fog, warm points ARE the depth — without something to occlude at known distances the fog is a flat grey card.")]
    public GameObject torchPrefab;
    [Tooltip("Set in an arc across the approach, between the lens and the castle, so every one of them is in frame.")]
    public int approachTorches = 14;
    [Tooltip("How wide the arc spreads, as a multiple of the castle's radius.")]
    public float torchArcWidth = 1.6f;
    [Tooltip("This torch prefab's mesh is authored LYING DOWN and needs minus ninety on X to stand.")]
    public float torchStandUpX = -90f;
    // ==== THE TORCH PREFAB HAS NO LIGHT ON IT ====
    //
    // It is a mesh and a flame particle, nothing else. In daylight that is fine.
    // In the one shot of this trailer that is ABOUT volumetric fog it is a small
    // orange sprite that illuminates nothing — and a light in volumetric fog is
    // not a light, it is a visible cone of glowing air. That glow at a known
    // distance is the entire mechanism by which fog reads as depth rather than
    // as a grey card.
    [Tooltip("Add a point light to each torch. Without it the flame lights nothing and the fog has nothing to carry.")]
    public bool torchesGiveLight = true;
    public Color torchLight = new Color(1f, 0.62f, 0.28f);
    public float torchLightRange = 14f;
    public float torchLightIntensity = 3.2f;
    [Range(0f, 0.6f)]
    [Tooltip("How much the flames breathe, as a fraction of their intensity. Small: a torch that pulses hard reads as a bad effect, and a whole line of them pulsing together reads as a light rig.")]
    public float torchFlicker = 0.18f;

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
    [Tooltip("Extinction per metre at the START, when the castle is meant to be a rumour. Read against the opening distance: at three castle-radii out that is a long way through it.")]
    public float fogDensityStart = 0.055f;
    [Tooltip("And at the end, once the crane is above it. The fog does not clear — it is left below.")]
    public float fogDensityEnd = 0.022f;
    public float fogHeightStart = 30f;
    public float fogHeightEnd = 40f;
    [Range(0f, 1f)]
    [Tooltip("The least density the noise may leave. At zero it carves clear holes, and a hole in the one shot that is ABOUT fog reads as the fog switching off.")]
    public float fogNoiseFloor = 0.5f;
    public Color fogColour = new Color(0.55f, 0.60f, 0.70f);

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
        PlaceTorches();
        RemoveHero();

        var polish = TrailerCinematicPolish.GetOrCreate();
        polish.OpenTrailer();
        TrailerAudio.SilenceStaleBeds();
        Loop(windBed);
        Loop(dreadBed);

        // ---- 4. SETTLE. A shape in the fog, and time to notice it. ----
        float drift = 0f;
        float st = 0f;
        while (st < settleSeconds)
        {
            st += Time.unscaledDeltaTime;
            drift += driftDegreesPerSecond * Time.unscaledDeltaTime;
            PlaceCamera(0f, drift);
            ApplyFog(0f);
            FlickerTorches();
            yield return null;
        }

        // ---- 5. CRANE. The arc, the rise, the push. ----
        float t = 0f;
        bool crowed = false, hushed = false, revealed = false;
        float hushAt = Mathf.Clamp01(revealAt) * craneSeconds - silenceBefore;

        while (t < craneSeconds)
        {
            t += Time.unscaledDeltaTime;
            float k = Mathf.Clamp01(t / Mathf.Max(0.01f, craneSeconds));

            // Smootherstep: zero velocity AND zero acceleration at both ends, so
            // the move never announces its start or its stop.
            float e = k * k * k * (k * (k * 6f - 15f) + 10f);

            drift += driftDegreesPerSecond * Time.unscaledDeltaTime;
            PlaceCamera(e, drift);
            ApplyFog(e);
            FlickerTorches();

            // One cry, early, while there is still nothing to look at. Nothing
            // says abandoned faster, and it wants to be well clear of the sting.
            if (!crowed && k > 0.22f) { crowed = true; Cue(crowsCue); }

            // The beds drop out, and for the best part of a second there is
            // nothing at all. Then the castle is there.
            if (!hushed && t >= hushAt) { hushed = true; DropOut(); }
            if (!revealed && k >= revealAt) { revealed = true; Cue(revealSting); Loop(windBed); }

            yield return null;
        }

        // ---- 6. HOLD. It never quite stops. ----
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

            // Two rows, alternating, marching in from the opening distance to the
            // castle wall — so they pass the lens on both sides and converge.
            float along = Mathf.Lerp(castleRadius * startDistance, castleRadius * 1.05f, k);
            float outward = (i % 2 == 0 ? 1f : -1f) * torchArcWidth * castleRadius * 0.5f * (1f - k * 0.55f);

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

        // It does not clear as the camera rises. It is LEFT BELOW — the layer
        // gets taller while the density drops, so the valley stays full and the
        // crane climbs out of it.
        if (fDensity != null) fDensity.SetValue(fog, Mathf.Lerp(fogDensityStart, fogDensityEnd, k));
        if (fHeight != null) fHeight.SetValue(fog, Mathf.Lerp(fogHeightStart, fogHeightEnd, k));
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
        float high = Mathf.Lerp(startHeight, castleBounds.size.y * endHeightFactor, e);
        p.y = Ground(p) + high;
        return LiftClear(p);
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
