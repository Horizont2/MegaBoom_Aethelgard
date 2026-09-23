using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// Stages a store screenshot and takes it, from one menu press.
//
// ==== WHAT A TOOL CAN AND CANNOT DO HERE ====
//
// Two kinds of shot live on a Steam page, and only one of them can be made by
// a script.
//
// A PLACE can. The castle in the fog, a tundra vista, a shrine on its pad — all
// of those are the world plus a camera plus weather, and every one of those is
// a number. The tool sets the region, waits for the world, clears what is in
// the way, puts the lens where it belongs, chooses the hour and the sky, and
// writes the PNG. Nothing about the result depends on a human hand.
//
// A MOMENT cannot. The frame where the sword is at the top of its arc and the
// fifteenth skeleton is a silhouette against the sun is a judgement, not a
// value, and a script that guessed at it would produce ten near-misses. So for
// those the tool does the half it is good at — it puts the hero somewhere
// photogenic, spawns the horde around him, fixes the weather and the hour, and
// hands the game back with the camera free. You pick the instant and press the
// key. That is the only part a person was ever needed for.
//
// Every shot says which kind it is when it starts, so nobody waits for a
// screenshot that is never coming.
public class ScreenshotDirector : MonoBehaviour
{
    public const string SessionKey = "Store.Screenshot";

    // AUTO shots write a file and leave. STAGED shots set the scene and stop.
    public const string CastleShot = "castle";
    public const string TundraShot = "tundra";
    public const string ReliquaryShot = "reliquary";
    public const string TotemShot = "totem";
    public const string CampShot = "camp";
    public const string HordeShot = "horde";

    /// <summary>The scene each shot has to be taken in.</summary>
    public static string SceneFor(string shot)
    {
        if (shot == CampShot) return "CampScene";
        if (shot == CastleShot || shot == TundraShot || shot == ReliquaryShot ||
            shot == TotemShot || shot == HordeShot) return "GameScene";
        return string.Empty;
    }

    /// <summary>Region to force before the world is generated, or -1 to take whatever is set.</summary>
    public static int RegionFor(string shot)
    {
        if (shot == CastleShot) return 24;      // Location_Castle lives here and nowhere else
        if (shot == TundraShot) return 16;      // Desolate Tundra — the snow biome
        if (shot == ReliquaryShot) return 10;   // mid-map, dressed, and far enough for a shrine to fit
        if (shot == TotemShot) return 6;
        if (shot == HordeShot) return 9;        // Howling Valley — open ground, the horde reads as mass
        return -1;
    }

    private static string Wanted()
    {
#if UNITY_EDITOR
        return UnityEditor.SessionState.GetString(SessionKey, string.Empty);
#else
        return string.Empty;
#endif
    }

    // Set from the generator's own completion event, which is subscribed to in
    // Boot — before the generator has run a line, so the finish cannot be
    // missed no matter how the two objects' Start calls happen to be ordered.
    private static bool s_worldReady;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Boot()
    {
        string shot = Wanted();
        if (string.IsNullOrEmpty(shot)) return;

        // Consumed on read. Without this every later entry into the scene — a
        // normal playtest — would stage a screenshot nobody asked for.
#if UNITY_EDITOR
        UnityEditor.SessionState.EraseString(SessionKey);
#endif
        if (UnityEngine.SceneManagement.SceneManager.GetActiveScene().name != SceneFor(shot)) return;

        // ==== THE REGION HAS TO BE PICKED HERE, NOT IN THE COROUTINE ====
        //
        // WorldGenerator.Start reads MissionInitializer.PendingMissionRegion and
        // then immediately starts generating. This callback runs after every
        // Awake and BEFORE the first Start, so it is the last moment the choice
        // can still be read.
        //
        // It used to be done from the director's own coroutine, which is a Start
        // on an object created right here — queued behind every object the scene
        // itself brought, the generator among them. By the time the region was
        // set the map was already being built from the previous one, so the
        // tool obediently photographed whatever region happened to be loaded
        // last.
        s_worldReady = false;
        WorldGenerator.OnWorldGenerationComplete -= MarkWorldReady;
        WorldGenerator.OnWorldGenerationComplete += MarkWorldReady;
        ForceRegion(RegionFor(shot));

        var go = new GameObject("ScreenshotDirector");
        go.hideFlags = HideFlags.DontSave;
        go.AddComponent<ScreenshotDirector>().shot = shot;
    }

    private static void MarkWorldReady() { s_worldReady = true; }

    private string shot;
    private Camera cam;

    private void Start() { StartCoroutine(Run()); }

    private IEnumerator Run()
    {
        TrailerLogGuard.Arm();

        yield return WaitForWorld();

        // The generator reports done while the last of the world is still
        // arriving: locations settle onto their colliders over the following
        // frames, and the hero is still falling from the thousand metres the
        // generator parks him at. Composing a frame in the middle of that is
        // how a shot ends up with a building halfway into the ground.
        yield return new WaitForSecondsRealtime(2f);

        cam = Camera.main;
        if (cam == null) { Debug.LogWarning("[Screenshot] No main camera."); yield break; }

        // The horde shot is handed back to the player, so the gameplay camera
        // has to keep working for it. Every other shot is composed by the tool,
        // and for those the lens stops belonging to the game.
        if (shot != HordeShot) TakeCamera();

        switch (shot)
        {
            case CastleShot:    yield return Castle();    break;
            case TundraShot:    yield return Tundra();    break;
            case ReliquaryShot: yield return Reliquary(); break;
            case TotemShot:     yield return Totem();     break;
            case CampShot:      yield return Camp();      break;
            case HordeShot:     yield return Horde();     break;
        }
    }

    // ===================== the automatic ones =====================

    private IEnumerator Castle()
    {
        Sky(21.5f, WeatherState.Storm);
        Hud(false);
        ClearCombatants();
        HideHero();

        Transform castle = FindLocation("Castle");
        if (castle == null) { Fail("no Location_Castle in this region"); yield break; }

        // Three quarters of the frame, from below the hill so it stands over
        // the lens rather than sitting in it.
        Frame(castle, fill: 0.72f, fov: 42f, pitch: 4f, yawOffset: 28f);
        yield return Settle(90);
        Grab(clean: true);
    }

    private IEnumerator Tundra()
    {
        Sky(9f, WeatherState.Precipitation);
        Hud(false);
        ClearCombatants();
        HideHero();

        // No subject — a vista is the point. High, looking out across the land.
        Terrain t = Terrain.activeTerrain;
        if (t == null) { Fail("no terrain"); yield break; }

        Vector3 size = t.terrainData.size;
        Vector3 at = t.transform.position + new Vector3(size.x * 0.32f, 0f, size.z * 0.38f);
        at.y = t.SampleHeight(at) + t.transform.position.y + 34f;

        Place(at, Quaternion.Euler(9f, 55f, 0f), 58f);

        yield return Settle(90);
        Grab(clean: true);
    }

    private IEnumerator Reliquary()
    {
        Sky(7.5f, WeatherState.Clear);
        Hud(false);
        ClearCombatants();
        HideHero();

        Transform site = FindByType<Reliquary>();
        if (site == null) { Fail("no reliquary was placed in this region — reroll, they are capped at two"); yield break; }

        Frame(site, fill: 0.55f, fov: 46f, pitch: 11f, yawOffset: -35f);
        yield return Settle(90);
        Grab(clean: true);
    }

    private IEnumerator Totem()
    {
        Sky(18.5f, WeatherState.Clear);
        Hud(true);

        Transform totem = FindByType<RegionTotem>();
        if (totem == null) { Fail("no region totem"); yield break; }

        // Wider than the others: the anchors are the subject as much as the
        // totem, and they stand thirteen metres out.
        Frame(totem, fill: 0.34f, fov: 52f, pitch: 14f, yawOffset: 20f);
        yield return Settle(90);
        Grab(clean: false);
    }

    private IEnumerator Camp()
    {
        Sky(19.5f, WeatherState.Clear);
        Hud(true);

        // The camp has no single subject, so the frame is built from everything
        // that has been built: the centre of the buildings' combined bounds.
        Bounds b = default;
        bool any = false;
        foreach (var building in FindObjectsByType<CampBuilding>(FindObjectsSortMode.None))
        {
            if (building == null) continue;
            foreach (var r in building.GetComponentsInChildren<Renderer>(true))
            {
                if (r == null || r is ParticleSystemRenderer) continue;
                if (!any) { b = r.bounds; any = true; } else b.Encapsulate(r.bounds);
            }
        }
        if (!any) { Fail("no camp buildings found"); yield break; }

        FrameBounds(b, fill: 0.62f, fov: 50f, pitch: 22f, yawOffset: 35f);
        yield return Settle(90);
        Grab(clean: false);
    }

    // ===================== the staged one =====================

    private IEnumerator Horde()
    {
        Sky(16f, WeatherState.Clear);
        Hud(true);

        Transform hero = FindPlayer();
        if (hero == null) { Fail("no player in the scene"); yield break; }

        // Everything a script can set is set; the instant is yours.
        EnemySpawner.AmbientThrottle = 3f;
        EnemySpawner.IsSpawningBlocked = false;

        yield return Settle(30);

        Debug.Log("[Screenshot] STAGED, not automatic. Midday, clear, spawning turned up, HUD on.\n" +
                  "Fight until the Stack counter is high, then press F10 for the frame with the HUD in it. " +
                  "A script cannot pick the moment a sword is at the top of its arc — that part is yours.");
    }

    // ===================== staging =====================

    // ==== THE REGION ASSETS ARE NOT REACHABLE AT RUNTIME ====
    //
    // They live in Assets/RegionData, which is not a Resources folder, so
    // Resources.Load cannot see them and FindObjectsOfTypeAll only finds the
    // ones some scene object happens to be holding — which in GameScene is
    // whichever region was played last, if any.
    //
    // This whole tool only exists in the editor, so it asks the asset database
    // directly. That is not a shortcut around a runtime problem; the shipped
    // game never runs a line of this.
    private static void ForceRegion(int regionNumber)
    {
        if (regionNumber < 0) return;

        RegionData region = LoadRegion(regionNumber);
        if (region == null)
        {
            Debug.LogWarning($"[Screenshot] Region {regionNumber} was not found — generating whatever is set " +
                             "instead, so the shot may not be the place it was meant to be.");
            return;
        }

        MissionInitializer.PendingMissionRegion = region;
        PlayerPrefs.SetInt("RegionBiomeType", (int)region.regionBiome);
        PlayerPrefs.SetInt("IsRegionMission", 1);

        // The generator asks GameManager first and only falls back to the
        // pending region, so a GameManager left over from the camp would
        // quietly win this argument.
        if (GameManager.Instance != null) GameManager.Instance.currentRegion = region;

        Debug.Log($"[Screenshot] Region {regionNumber} — {region.regionName}.");
    }

    private static RegionData LoadRegion(int regionNumber)
    {
#if UNITY_EDITOR
        // Named Region_1 … Region_24 on disk, and regionID is that minus one.
        var byPath = UnityEditor.AssetDatabase.LoadAssetAtPath<RegionData>(
            $"Assets/RegionData/Region_{regionNumber}.asset");
        if (byPath != null) return byPath;

        // The file may have been renamed; fall back to the field that matters.
        foreach (string guid in UnityEditor.AssetDatabase.FindAssets("t:RegionData"))
        {
            var r = UnityEditor.AssetDatabase.LoadAssetAtPath<RegionData>(
                UnityEditor.AssetDatabase.GUIDToAssetPath(guid));
            if (r != null && r.regionID == regionNumber - 1) return r;
        }
#endif
        // Last resort, and the only one a build would have: whatever is loaded.
        foreach (var r in Resources.FindObjectsOfTypeAll<RegionData>())
            if (r != null && r.regionID == regionNumber - 1) return r;

        return null;
    }

    private IEnumerator WaitForWorld()
    {
        // One frame first, so every Start in the scene has run — including the
        // generator's, which is what clears IsGenerationDone from the previous
        // run. Polling the flag before that reads the last session's answer and
        // walks straight past a world that has not been built yet.
        yield return null;

        if (Object.FindFirstObjectByType<WorldGenerator>() == null && !s_worldReady)
        {
            Debug.Log("[Screenshot] No WorldGenerator in this scene — nothing to wait for.");
            yield break;
        }

        float waited = 0f, logged = -1f;
        while (!s_worldReady && !WorldGenerator.IsGenerationDone && waited < 300f)
        {
            if (WorldGenerator.CurrentProgress - logged >= 0.25f)
            {
                logged = WorldGenerator.CurrentProgress;
                Debug.Log($"[Screenshot] Building the world… {Mathf.RoundToInt(logged * 100f)}%");
            }
            waited += Time.unscaledDeltaTime;
            yield return null;
        }

        if (!s_worldReady && !WorldGenerator.IsGenerationDone)
            Debug.LogWarning($"[Screenshot] The world was still building after {waited:0}s. Shooting anyway — " +
                             "expect the frame to be missing some of it.");
        else
            Debug.Log($"[Screenshot] World ready after {waited:0.0}s.");
    }

    // ===================== taking the lens away from the game =====================

    // ==== NOTHING ELSE MAY MOVE THIS CAMERA ====
    //
    // CameraFollow sits on the Main Camera and rewrites its position, rotation
    // and field of view in LateUpdate, every single frame, from the hero's
    // shoulder. The director composes its frame from a coroutine, which runs in
    // the Update phase — so the shot was composed and then thrown away again
    // before it was ever drawn. That is the whole reason every screenshot came
    // out of the gameplay camera no matter where the tool pointed it.
    //
    // isCinematicMode is the project's own way of saying the camera is not the
    // game's right now, and the trailer and every cutscene use it. It is set
    // here too, but it is not trusted on its own: the composed pose is put back
    // from Application.onBeforeRender, which fires after every LateUpdate in
    // the frame and immediately before anything is drawn. Whatever else in this
    // project ever decides to move the main camera, it moves it earlier than
    // that, and the shot wins.
    private bool posePinned;
    private Vector3 pinPos;
    private Quaternion pinRot;
    private Transform distanceProxy;

    private void TakeCamera()
    {
        var follow = cam.GetComponent<CameraFollow>();
        if (follow != null) follow.isCinematicMode = true;

        var collision = cam.GetComponent<CameraCollision>();
        if (collision != null) collision.isCinematicMode = true;

        // Occlusion fades whatever stands between the lens and the hero. With
        // the lens somewhere else entirely that is an arbitrary list of
        // buildings going half-transparent in the middle of the shot.
        var occlusion = cam.GetComponent<CameraOcclusion>();
        if (occlusion != null) occlusion.enabled = false;

        var bob = cam.GetComponent<CameraBobbing>();
        if (bob != null) bob.enabled = false;

        // Minimap markers live on their own layer, so the whole set leaves the
        // frame in one line rather than being hunted down object by object.
        int minimap = LayerMask.NameToLayer("MinimapOnly");
        if (minimap >= 0) cam.cullingMask &= ~(1 << minimap);

        FollowTheLensInsteadOfTheHero();

        Application.onBeforeRender -= Pin;
        Application.onBeforeRender += Pin;
    }

    // ==== THE WORLD SWITCHES ITSELF OFF AROUND THE HERO, NOT THE CAMERA ====
    //
    // DistanceOptimizer deactivates every registered object further than
    // disableDistance from the PLAYER — 155m on the lowest foliage preset. Fly
    // the camera to a castle on the far side of the region and everything
    // around it is already switched off, so the tool photographs a bare hill
    // with a castle on it.
    //
    // It only ever reads that one transform, so handing it a stand-in that sits
    // where the lens sits is enough. Nothing about the hero changes, and the
    // proxy dies with this object.
    private void FollowTheLensInsteadOfTheHero()
    {
        if (DistanceOptimizer.Instance == null) return;

        var go = new GameObject("ScreenshotDistanceProxy");
        go.hideFlags = HideFlags.DontSave;
        distanceProxy = go.transform;
        distanceProxy.position = cam.transform.position;
        DistanceOptimizer.Instance.player = distanceProxy;

        // A normal sweep is five hundred objects a frame, which is right when
        // the reference point drifts a metre at a time. This one jumps across
        // the region in one step, and the whole list has to catch up before the
        // shutter — the frame budget does not matter to a screenshot.
        DistanceOptimizer.Instance.checksPerFrame = int.MaxValue;
    }

    /// <summary>Puts the lens somewhere and keeps it there until the shot is written.</summary>
    private void Place(Vector3 position, Quaternion rotation, float fov)
    {
        cam.transform.SetPositionAndRotation(position, rotation);
        cam.fieldOfView = fov;

        pinPos = position;
        pinRot = rotation;
        posePinned = true;

        if (distanceProxy != null) distanceProxy.position = position;
    }

    private void Pin()
    {
        if (!posePinned || cam == null) return;
        cam.transform.SetPositionAndRotation(pinPos, pinRot);
    }

    private void OnDestroy()
    {
        Application.onBeforeRender -= Pin;
        if (distanceProxy != null) Destroy(distanceProxy.gameObject);
    }

    // A figure standing wherever the generator happened to drop him is not part
    // of an architectural shot, and he is never where you would have put him.
    // Hiding the renderers rather than moving him leaves his actual position
    // and state alone.
    private void HideHero()
    {
        Transform hero = FindPlayer();
        if (hero == null) return;
        foreach (var r in hero.GetComponentsInChildren<Renderer>(true))
            if (r != null) r.enabled = false;
    }

    private void Sky(float hour, WeatherState weather)
    {
        var cycle = Object.FindFirstObjectByType<DayNightCycle>(FindObjectsInactive.Include);
        if (cycle == null) return;

        cycle.timeOfDay = hour;
        cycle.currentWeather = weather;
        // Frozen, or the sun moves between the frame being set up and the frame
        // being taken — which on a long generation is several minutes.
        cycle.enabled = false;
    }

    private void Hud(bool visible)
    {
        if (GlobalHUD.Instance == null) return;
        var group = GlobalHUD.Instance.GetComponent<CanvasGroup>();
        if (group == null) group = GlobalHUD.Instance.gameObject.AddComponent<CanvasGroup>();
        group.alpha = visible ? 1f : 0f;
    }

    // Enemies wandering through an architectural shot are the one thing that
    // makes a place photograph badly, and they are never where you want them.
    private void ClearCombatants()
    {
        EnemySpawner.IsSpawningBlocked = true;
        int gone = 0;
        foreach (var e in Object.FindObjectsByType<EnemyAI>(FindObjectsSortMode.None))
            if (e != null) { Destroy(e.gameObject); gone++; }
        if (gone > 0) Debug.Log($"[Screenshot] Cleared {gone} enemies out of the frame.");
    }

    // ===================== framing =====================

    private void Frame(Transform subject, float fill, float fov, float pitch, float yawOffset)
    {
        // Inactive and disabled renderers count, because what is switched off
        // right now says nothing about how big the thing is. DistanceOptimizer
        // deactivates whatever is far from the camera and LODGroup disables
        // renderers outright at range, so measuring only what is currently
        // drawing gives the size of the nearest corner of a castle rather than
        // the castle. Their bounds are still correct — a renderer's bounds come
        // from its mesh and its transform, neither of which cares whether it is
        // being drawn.
        Bounds b = default;
        bool any = false;
        foreach (var r in subject.GetComponentsInChildren<Renderer>(true))
        {
            if (r == null || r is ParticleSystemRenderer) continue;
            if (!any) { b = r.bounds; any = true; } else b.Encapsulate(r.bounds);
        }
        if (!any) { b = new Bounds(subject.position, Vector3.one * 8f); }
        FrameBounds(b, fill, fov, pitch, yawOffset);
    }

    // To make a subject of height h fill a fraction f of the frame at vertical
    // fov t, the lens has to sit h / (2f * tan(t/2)) away. Guessing a distance
    // instead is what puts a camera inside a castle.
    private void FrameBounds(Bounds b, float fill, float fov, float pitch, float yawOffset)
    {
        float height = Mathf.Max(b.size.y, 2f);
        float distance = height / (2f * Mathf.Max(0.05f, fill) * Mathf.Tan(fov * 0.5f * Mathf.Deg2Rad));
        distance = Mathf.Max(distance, new Vector2(b.extents.x, b.extents.z).magnitude + 6f);

        // yawOffset is a nudge off the open side, not a compass bearing — see
        // OpenSide. A fixed world yaw was as likely to point into a hillside as
        // along the valley, and from inside a hill the lens gets shoved up above
        // the subject and ends up looking down at the ground.
        Vector3 back = Quaternion.Euler(0f, yawOffset, 0f) * OpenSide(b);

        Vector3 at = b.center - back * distance + Vector3.up * (distance * Mathf.Tan(pitch * Mathf.Deg2Rad));

        float ground = GroundAt(at);
        if (at.y < ground + 2f) at.y = ground + 2f;       // never underground

        Place(at, Quaternion.LookRotation((b.center - at).normalized), fov);

        Debug.Log($"[Screenshot] Framed a subject {b.size.y:0.0}m tall and {b.size.x:0.0}x{b.size.z:0.0}m across " +
                  $"from {distance:0}m, {at.y - ground:0.0}m above the ground under the lens.");
    }

    // ==== WHICH SIDE TO STAND ON ====
    //
    // The generator raises a hill under a location and faces it wherever it
    // likes, so there is no side that is reliably the front. Sample a ring
    // around the subject and take the direction the land falls away furthest:
    // that is the open valley side, the longest clear sightline, and the side
    // the generated roads climb anyway.
    //
    // Same measurement the trailer's castle shot makes, for the same reason.
    private static Vector3 OpenSide(Bounds b)
    {
        if (Terrain.activeTerrain == null) return Vector3.forward;   // camp, or any scene with no terrain

        float radius = Mathf.Max(8f, new Vector2(b.extents.x, b.extents.z).magnitude);
        Vector3 centre = b.center;

        float bestDrop = float.NegativeInfinity;
        Vector3 best = Vector3.forward;

        const int Samples = 24;
        for (int i = 0; i < Samples; i++)
        {
            float a = i / (float)Samples * Mathf.PI * 2f;
            Vector3 dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));

            // Averaged over three distances out, so one dip or boulder near the
            // wall cannot win the vote.
            float drop = 0f;
            for (int r = 1; r <= 3; r++)
                drop += centre.y - GroundAt(centre + dir * (radius * (1f + r * 0.9f)));

            if (drop > bestDrop) { bestDrop = drop; best = dir; }
        }
        return best;
    }

    private static float GroundAt(Vector3 p)
    {
        Terrain t = Terrain.activeTerrain;
        if (t == null) return p.y;
        return t.SampleHeight(p) + t.transform.position.y;
    }

    // ===================== finding things =====================

    private static Transform FindByType<T>() where T : Component
    {
        var found = Object.FindFirstObjectByType<T>();
        return found != null ? found.transform : null;
    }

    // ==== A NAME IS NOT A WAY TO FIND A LOCATION ====
    //
    // This used to search every Transform in the scene for one whose name
    // contained "Castle", root objects first and then anything at all. The
    // generated castle is Location_Castle(Clone) and it is NOT a root — it hangs
    // under a container the generator makes — so the first pass always missed
    // and the second pass returned whichever of its nineteen hundred nested
    // pieces happened to come back first. Frame a single flagstone and you get
    // a camera six metres from a flagstone, which is exactly the floor the shot
    // came back as.
    //
    // A generated location always carries SelfContainedLocation, which is what
    // the trailer's own castle shot matches on. That finds the location, not a
    // brick inside it.
    private static Transform FindLocation(string contains)
    {
        string want = contains.ToLowerInvariant();

        foreach (var sc in Object.FindObjectsByType<SelfContainedLocation>(
                     FindObjectsInactive.Exclude, FindObjectsSortMode.None))
        {
            if (sc == null) continue;
            if (sc.name.ToLowerInvariant().Contains(want)) return sc.transform;
        }

        // Nothing carrying the component matched, so fall back to a name — but
        // only on an object with no SelfContainedLocation above it, which keeps
        // a nested piece from being mistaken for the whole thing.
        foreach (var t in Object.FindObjectsByType<Transform>(FindObjectsSortMode.None))
        {
            if (t == null || !t.name.ToLowerInvariant().Contains(want)) continue;
            if (t.GetComponentInParent<SelfContainedLocation>() != null) continue;
            return t;
        }
        return null;
    }

    private static Transform FindPlayer()
    {
        GameObject go = GameObject.FindGameObjectWithTag("Player");
        return go != null ? go.transform : null;
    }

    // ===================== the exposure =====================

    private IEnumerator Settle(int frames)
    {
        // Streaming, LODs, grass, the fog's own warm-up and every shader variant
        // this view needs all resolve over the first frames after a camera
        // moves. A screenshot taken immediately catches at least one of them
        // half-done, and it is never the same one twice.
        for (int i = 0; i < frames; i++) yield return null;
        yield return new WaitForEndOfFrame();
    }

    private void Grab(bool clean)
    {
        // Handed the camera by name rather than letting PosterFrame guess at the
        // one with the highest depth — the shot is composed on this one and no
        // other.
        if (clean) PosterFrame.GrabClean(cam);
        else PosterFrame.GrabFramed();

        Debug.Log($"[Screenshot] '{shot}' written to the PosterFrames folder beside the project, " +
                  "1920x1080 and ready to upload.\nThe camera stays where the shot was taken, so F9 gives the " +
                  "same frame again. Stop Play Mode to give it back to the game.");
    }

    private void Fail(string why)
    {
        Debug.LogWarning($"[Screenshot] '{shot}' could not be staged: {why}. Nothing was written.");
    }
}
