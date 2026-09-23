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

        var go = new GameObject("ScreenshotDirector");
        go.hideFlags = HideFlags.DontSave;
        go.AddComponent<ScreenshotDirector>().shot = shot;
    }

    private string shot;
    private Camera cam;

    private void Start() { StartCoroutine(Run()); }

    private IEnumerator Run()
    {
        TrailerLogGuard.Arm();

        // The region has to be chosen before anything generates, and Boot runs
        // after Awake — so this is the last moment it can still matter, and it
        // only matters when the world has not started yet.
        ForceRegion(RegionFor(shot));

        yield return WaitForWorld();
        yield return null;

        cam = Camera.main;
        if (cam == null) { Debug.LogWarning("[Screenshot] No main camera."); yield break; }

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

        Transform castle = FindByName("Castle");
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

        // No subject — a vista is the point. High, looking out across the land.
        Terrain t = Terrain.activeTerrain;
        if (t == null) { Fail("no terrain"); yield break; }

        Vector3 size = t.terrainData.size;
        Vector3 at = t.transform.position + new Vector3(size.x * 0.32f, 0f, size.z * 0.38f);
        at.y = t.SampleHeight(at) + t.transform.position.y + 34f;

        cam.transform.position = at;
        cam.transform.rotation = Quaternion.Euler(9f, 55f, 0f);
        cam.fieldOfView = 58f;

        yield return Settle(90);
        Grab(clean: true);
    }

    private IEnumerator Reliquary()
    {
        Sky(7.5f, WeatherState.Clear);
        Hud(false);

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
            foreach (var r in building.GetComponentsInChildren<Renderer>())
            {
                if (r == null || !r.enabled) continue;
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
    private void ForceRegion(int regionNumber)
    {
        if (regionNumber < 0) return;
        if (WorldGenerator.IsGenerationDone) return;      // too late to matter, and nothing to fix

        RegionData region = LoadRegion(regionNumber);
        if (region == null)
        {
            Debug.LogWarning($"[Screenshot] Region {regionNumber} was not found — generating whatever is set " +
                             "instead, so the shot may not be the place it was meant to be.");
            return;
        }

        MissionInitializer.PendingMissionRegion = region;
        PlayerPrefs.SetInt("RegionBiomeType", (int)region.regionBiome);
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
        if (Object.FindFirstObjectByType<WorldGenerator>() == null) yield break;

        float waited = 0f, logged = -1f;
        while (!WorldGenerator.IsGenerationDone && waited < 180f)
        {
            if (WorldGenerator.CurrentProgress - logged >= 0.25f)
            {
                logged = WorldGenerator.CurrentProgress;
                Debug.Log($"[Screenshot] Building the world… {Mathf.RoundToInt(logged * 100f)}%");
            }
            waited += Time.unscaledDeltaTime;
            yield return null;
        }
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
        Bounds b = default;
        bool any = false;
        foreach (var r in subject.GetComponentsInChildren<Renderer>())
        {
            if (r == null || !r.enabled || r is ParticleSystemRenderer) continue;
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

        Quaternion yaw = Quaternion.Euler(0f, yawOffset, 0f);
        Vector3 back = yaw * Vector3.forward;

        Vector3 at = b.center - back * distance + Vector3.up * (distance * Mathf.Tan(pitch * Mathf.Deg2Rad));
        at.y = Mathf.Max(at.y, GroundAt(at) + 2f);        // never underground

        cam.transform.position = at;
        cam.transform.rotation = Quaternion.LookRotation((b.center - at).normalized);
        cam.fieldOfView = fov;
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

    private static Transform FindByName(string contains)
    {
        string want = contains.ToLowerInvariant();
        foreach (var t in Object.FindObjectsByType<Transform>(FindObjectsSortMode.None))
            if (t != null && t.parent == null && t.name.ToLowerInvariant().Contains(want)) return t;

        // Second pass including children — a location is often nested under a
        // container the generator made.
        foreach (var t in Object.FindObjectsByType<Transform>(FindObjectsSortMode.None))
            if (t != null && t.name.ToLowerInvariant().Contains(want)) return t;
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
        if (clean) PosterFrame.GrabClean();
        else PosterFrame.GrabFramed();

        Debug.Log($"[Screenshot] '{shot}' written to the PosterFrames folder beside the project. " +
                  "Halve it to 1920x1080 before uploading — that downscale is the antialiasing.");
    }

    private void Fail(string why)
    {
        Debug.LogWarning($"[Screenshot] '{shot}' could not be staged: {why}. Nothing was written.");
    }
}
