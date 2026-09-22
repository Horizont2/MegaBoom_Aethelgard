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

    [Header("The hero in frame")]
    [Tooltip("Stand the player in the foreground, small, facing the castle. He is the only thing in frame whose size the audience already knows, which is the whole reason the castle reads as big.")]
    public bool placeHero = true;
    [Tooltip("Metres in front of the camera's opening position.")]
    public float heroAhead = 7f;
    [Tooltip("Metres to the side of the lens axis, so he is not dead centre.")]
    public float heroOffCentre = 2.4f;

    [Header("The crane")]
    // Three moves, in the order they are read:
    //   LOW    the castle is a shape in the fog and the hero is a figure below
    //          it. You are told there is something there before you are shown it.
    //   RISE   the camera leaves the ground and the fog thins under it.
    //   PUSH   it closes, the lens tightens, and the castle fills the frame.
    [Tooltip("Metres from the castle's edge the shot opens at, as a multiple of the castle's own radius.")]
    public float startDistance = 3.2f;
    [Tooltip("And where the push ends. Smaller is closer.")]
    public float endDistance = 1.9f;
    public float startHeight = 2.2f;
    [Tooltip("Height at the end, as a multiple of the castle's height. Above one it looks down on the walls.")]
    public float endHeightFactor = 0.85f;
    public float startFov = 58f;
    public float endFov = 42f;
    [Tooltip("Seconds the whole move takes. Slow — this is the one shot in the trailer that is allowed to breathe.")]
    public float craneSeconds = 12f;
    [Tooltip("Degrees of yaw drift across the move. A crane that only goes up reads as a lift; a few degrees of turn reads as operated.")]
    public float yawDrift = 8f;
    public float handheld = 0.02f;
    [Tooltip("Seconds held on the final framing before the fade.")]
    public float holdSeconds = 2f;
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

    [Header("Audio")]
    public string windBed = AudioID.Trailer_WindDesolate;
    public string revealSting = AudioID.Trailer_Dread;
    public string crowsCue = AudioID.Trailer_Crows;

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

    private void Awake()
    {
        // Before WorldGenerator's Start, which is the only moment this is any
        // use: it is what the generator reads to decide which location to build
        // the world around.
        if (region != null) MissionInitializer.PendingMissionRegion = region;
    }

    private void Start()
    {
        if (autoPlay) StartCoroutine(Run());
    }

    private IEnumerator Run()
    {
        TrailerLogGuard.Arm();

        // ---- 1. wait for the world ----
        float waited = 0f;
        while (castle == null && waited < generationTimeout)
        {
            castle = FindCastle();
            if (castle != null) break;
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
        PlaceHero();

        var polish = TrailerCinematicPolish.GetOrCreate();
        polish.OpenTrailer();
        TrailerAudio.SilenceStaleBeds();
        Loop(windBed);
        Cue(revealSting);

        // ---- 4. the crane ----
        float t = 0f;
        bool crowed = false;
        while (t < craneSeconds)
        {
            t += Time.unscaledDeltaTime;
            float k = Mathf.Clamp01(t / Mathf.Max(0.01f, craneSeconds));

            // Smootherstep: zero velocity AND zero acceleration at both ends, so
            // the move never announces its start or its stop.
            float e = k * k * k * (k * (k * 6f - 15f) + 10f);

            PlaceCamera(e);
            ApplyFog(e);

            // One cry, as the castle resolves. Nothing says abandoned faster.
            if (!crowed && k > 0.45f) { crowed = true; Cue(crowsCue); }
            yield return null;
        }

        // ---- 5. hold, and out ----
        float hold = 0f;
        while (hold < holdSeconds)
        {
            hold += Time.unscaledDeltaTime;
            PlaceCamera(1f);
            yield return null;
        }

        polish.FadeToBlack(outFade);
        yield return new WaitForSecondsRealtime(outFade);
        DropOut();
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
    // "front" to point at. What there IS, reliably, is a road: the generator runs
    // one up the hill to reach the location. Filming down the road means filming
    // the way the place is approached, which is both the most composed angle
    // available and the one with clear ground to put a crane on.
    //
    // Falling back to the player's own position if no road is found — he was
    // spawned somewhere sane, so the line from him to the castle is at least
    // walkable.
    private void ChooseApproach()
    {
        Transform roads = FindByName("RoadsContainer");
        Vector3 from = Vector3.zero;
        bool found = false;

        if (roads != null)
        {
            // The road point nearest the castle but outside its footprint: that
            // is where the approach arrives from.
            float best = float.MaxValue;
            foreach (Transform seg in roads)
            {
                float d = Vector3.Distance(seg.position, castleBounds.center);
                if (d < castleRadius * 1.2f || d > best) continue;
                best = d; from = seg.position; found = true;
            }
        }

        if (!found)
        {
            GameObject hero = GameObject.FindGameObjectWithTag("Player");
            if (hero != null) { from = hero.transform.position; found = true; }
        }

        approach = found ? (from - castleBounds.center) : Vector3.forward * castleRadius;
        approach.y = 0f;
        approach = approach.sqrMagnitude > 0.01f ? approach.normalized : Vector3.forward;
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

    private void PlaceHero()
    {
        if (!placeHero) return;

        GameObject hero = GameObject.FindGameObjectWithTag("Player");
        if (hero == null) return;

        // Everything that would walk him out of the shot, exactly as the clash
        // does it — Strip knows about enemies, not about the player.
        foreach (var c in hero.GetComponentsInChildren<PlayerController>(true)) if (c != null) DestroyImmediate(c);
        foreach (var c in hero.GetComponentsInChildren<CharacterController>(true)) if (c != null) c.enabled = false;
        foreach (var smr in hero.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            if (smr != null) smr.updateWhenOffscreen = true;

        Vector3 lens = CameraPosition(0f);
        Vector3 toCastle = (castleBounds.center - lens); toCastle.y = 0f;
        toCastle = toCastle.sqrMagnitude > 0.01f ? toCastle.normalized : Vector3.forward;
        Vector3 across = new Vector3(toCastle.z, 0f, -toCastle.x);

        Vector3 p = lens + toCastle * heroAhead + across * heroOffCentre;
        p.y = Ground(p);
        hero.transform.position = p;
        hero.transform.rotation = Quaternion.LookRotation(toCastle);
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
            torches.Add(go.transform);
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

    private Vector3 CameraPosition(float e)
    {
        float dist = Mathf.Lerp(castleRadius * startDistance, castleRadius * endDistance, e);
        float yaw = Mathf.Lerp(0f, yawDrift, e);
        Vector3 dir = Quaternion.Euler(0f, yaw, 0f) * approach;

        Vector3 p = castleBounds.center + dir * dist;
        float high = Mathf.Lerp(startHeight, castleBounds.size.y * endHeightFactor, e);
        p.y = Ground(p) + high;
        return p;
    }

    private void PlaceCamera(float e)
    {
        Vector3 p = CameraPosition(e);

        // Aimed at the castle's middle at the start and at its crown by the end,
        // so the rise is felt as looking further UP the building rather than as
        // the horizon dropping.
        Vector3 aim = castleBounds.center;
        aim.y = Mathf.Lerp(castleBounds.center.y, castleBounds.max.y - castleBounds.size.y * 0.15f, e);

        float amp = handheld * (1f - e * 0.4f);
        float tt = Time.unscaledTime;
        Vector3 shake = new Vector3(Mathf.PerlinNoise(tt * 1.1f, 0f) - 0.5f,
                                    Mathf.PerlinNoise(0f, tt * 1.7f) - 0.5f,
                                    Mathf.PerlinNoise(tt * 0.9f, 5f) - 0.5f) * (amp * 2f);

        cam.transform.position = p + shake;
        cam.transform.rotation = Quaternion.LookRotation((aim - p).normalized);
        cam.fieldOfView = Mathf.Lerp(startFov, endFov, e);
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
