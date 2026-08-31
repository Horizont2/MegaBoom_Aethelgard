using UnityEngine;
using UnityEngine.SceneManagement;
using System.Collections;
using System.Collections.Generic;

// One-button, self-filming trailer. Runs the whole beat sheet automatically:
// sets weather/time, hides the HUD, drives Camera.main, stages "actors"
// (spawns enemies), makes the hero fight on its own, fires the region-victory
// cinematic, and reveals a title card. Just point the game at a generated
// region, press the key, and screen-record.
//
// It's a best-effort automatic pass — timings/positions are tuned blind, so
// expect to nudge a few numbers after the first watch.
public class AutoTrailerDirector : MonoBehaviour
{
    public static bool IsPlaying { get; private set; }

    private Camera cam;
    private MonoBehaviour follow;
    private bool followWas;
    private float fov0;
    private PlayerController player;
    private Transform hero;

    private float titleAlpha;
    private string titleMain = "Hollow Siege";
    private string titleSub  = "Cleanse the cursed land";
    // Trailer end-card call-to-action. Shown under the title on the final
    // beat so the reveal doubles as a Steam wishlist prompt.
    private string titleWishlist = "WISHLIST NOW ON STEAM";
    private GUIStyle mainStyle, subStyle, stackStyle, wishlistStyle;
    private Texture2D solid;
    private bool showStack;
    private int _hintsWere = 1;

    // Cached enemy list for the hero's combat AI — refreshed a few times a
    // second instead of FindObjects every frame.
    private readonly List<EnemyAI> liveEnemies = new List<EnemyAI>(64);
    private float nextEnemyScan;

    public static void Play()
    {
        if (IsPlaying) return;
        // NOT DontDestroyOnLoad: the victory cinematic fades to CampScene at its
        // end, and letting the scene change destroy this director is the clean
        // outro — the title overlay disappears with the region.
        var go = new GameObject("AutoTrailerDirector");
        go.AddComponent<AutoTrailerDirector>();
    }

    private void Start() => StartCoroutine(Run());

    private IEnumerator Run()
    {
        IsPlaying = true;
        cam = Camera.main;
        if (cam == null) cam = FindFirstObjectByType<Camera>();   // shop/menu cams aren't always tagged MainCamera
        if (cam == null) { Cleanup(); yield break; }

        follow = cam.GetComponent("CameraFollow") as MonoBehaviour;
        followWas = follow != null && follow.enabled;
        if (follow != null) follow.enabled = false;
        fov0 = cam.fieldOfView;

        // One key, scene-aware: camp tour, shop showcase, or the region beat sheet.
        string scene = SceneManager.GetActiveScene().name;
        if (scene == "CampScene") { yield return CampTour(); Cleanup(); yield break; }
        if (scene == "ShopScene") { yield return ShopTour(); Cleanup(); yield break; }

        var pgo = GameObject.FindGameObjectWithTag("Player");
        player = pgo != null ? pgo.GetComponent<PlayerController>() : null;
        hero = pgo != null ? pgo.transform : null;
        if (hero == null) { Cleanup(); yield break; }

        SetHUD(false);
        // Silence what would interfere with the capture: tutorial hints, the
        // level-up modal (guarded in LevelUpManager via IsPlaying), and the
        // crowd's roars/growls.
        _hintsWere = PlayerPrefs.GetInt("Settings_TutorialHints", 1);
        PlayerPrefs.SetInt("Settings_TutorialHints", 0);
        EnemyAI.SuppressCombatVocals = true;
        if (player != null) { player.isControlBlocked = true; player.isCinematicInvincible = true; }

        var dnc = FindFirstObjectByType<DayNightCycle>();

        // ONE consistent weather for the whole action run — the game's own rain
        // (not a director-made effect), set to a readable daytime so the scene
        // isn't a murky dusk. It never flips to Clear and back mid-trailer, which
        // read as a glitch before.
        if (dnc != null) { dnc.isWeatherLocked = true; dnc.timeOfDay = 14f; dnc.ForceWeather(WeatherState.Precipitation); }

        // ===== BEAT -1 — THE RISE (cinematic cold open) =====================
        // Camera cranes up from just behind the hero to reveal a frozen
        // skeleton formation arrayed ahead of them. Deliberately a SMALL, tidy
        // block (4×4) — the first pass spawned ~45 and read as visual soup.
        EnemyAI.GlobalFreeze = true;
        SpawnArmyInFront(4, 4, 9f, 2.8f);   // 16 skeletons, clean rows
        {
            Vector3 h = hero.position;
            Vector3 fwd = hero.forward; fwd.y = 0f;
            if (fwd.sqrMagnitude < 0.01f) fwd = Vector3.forward;
            fwd.Normalize();
            Vector3 lookAt = h + fwd * 11f + Vector3.up * 1.3f;   // the army ahead
            Vector3 from   = h - fwd * 4.5f + Vector3.up * 0.5f;  // low, tucked behind the hero
            Vector3 to     = h - fwd * 8.5f + Vector3.up * 7f;    // craned up and back
            yield return Move(from, to, lookAt, 36f, 50f, 6.5f, false);
        }

        // ===== BEAT 0 — the cursed land (slow menacing drift) =====
        {
            Vector3 c = hero.position;
            Vector3 a = c + new Vector3(-13f, 9f, -13f);
            Vector3 b = c + new Vector3(9f, 7f, -15f);
            yield return Move(a, b, c + Vector3.up * 1.3f, 50f, 46f, 5.5f, false);
        }

        // ===== BEAT 1 — the spark (grenade + first blows) =====
        EnemyAI.GlobalFreeze = false;
        {
            // Frame the hero from a 3/4 side angle BEFORE the throw so the
            // grenade toss is actually ON CAMERA — the first pass threw it during
            // a camera transition and you couldn't see it happen.
            Vector3 h = hero.position;
            Vector3 side = Vector3.Cross(Vector3.up, hero.forward).normalized;
            Vector3 framePos = h + side * 4.5f + hero.forward * 2.2f + Vector3.up * 2.3f;
            yield return Move(cam.transform.position, framePos, h + hero.forward * 4f + Vector3.up * 1.2f, 44f, 40f, 1.3f, false);
            if (player != null) player.TrailerThrowGrenade();   // now clearly in frame
            yield return new WaitForSecondsRealtime(0.9f);      // watch the arc + the blast land
        }
        StartCoroutine(SlowMoPulse(0.4f, 0.5f));                     // impact beat
        yield return Orbit(hero, 4.2f, 5.2f, 40f, 150f, 2.3f, 1.3f, 46f, true);

        // ===== BEAT 2 — become the storm (STACK typhoon) =====
        // Crowd trimmed 30 → 16 so the fight reads instead of turning to soup.
        // Weather stays consistent — no jarring flip to Clear and back.
        EnemyAI.GlobalFreeze = true;
        SpawnRing(16, 5f, 9f);                                                // varied crowd (round-robin types)
        yield return Orbit(hero, 1.6f, 7.5f, 30f, 80f, 3.4f, 1.4f, 54f, false); // pose on the frozen crowd
        EnemyAI.GlobalFreeze = false;
        showStack = true;                                                    // stylised on-screen STACK counter
        yield return Orbit(hero, 5.0f, 6.8f, 80f, 250f, 2.8f, 1.35f, 56f, true);
        StartCoroutine(SlowMoPulse(0.35f, 0.9f));                            // punch the x5 peak
        yield return Orbit(hero, 4.2f, 5.6f, 250f, 400f, 2.4f, 1.3f, 52f, true);
        showStack = false;

        // ===== BEAT 2b — the boss (big elite + low-angle slow-mo execution) =====
        var boss = SpawnRing(1, 4.5f, 4.5f);
        if (boss != null) { boss.transform.localScale *= 2.5f; var ba = boss.GetComponent<EnemyAI>(); if (ba != null) ba.maxHealth *= 2.5f; }
        yield return Orbit(hero, 3.8f, 4.6f, 0f, 150f, 1.6f, 1.7f, 42f, true);  // low, tense
        StartCoroutine(SlowMoPulse(0.2f, 1.2f));                                // execution slow-mo
        yield return Orbit(hero, 1.8f, 3.6f, 150f, 205f, 1.3f, 1.6f, 40f, true);

        // ===== BEAT 3 — the curse lifts (victory reveal) + title over it =====
        // The victory cinematic OWNS Camera.main (bird-flight reveal, bloom) and
        // plays its own "REGION CONQUERED" card, then fades to camp. We don't
        // touch the camera here — we just overlay the GAME title (OnGUI) over the
        // reveal's visual peak.
        var rm = FindFirstObjectByType<RegionManager>();
        if (rm != null)
        {
            if (follow != null) follow.enabled = true;   // cinematic sets isCinematicMode; it yields
            rm.DebugTriggerVictoryCinematic();

            // Fly the reveal to its bloom peak, then bring up the game title.
            yield return new WaitForSecondsRealtime(10f);
            // Brand name — the final game title. NOT localized (it's a proper
            // noun / logo), and deliberately "Hollow Siege" rather than the
            // in-world "Aethelgard" so the end card reads as the store title.
            titleMain = "Hollow Siege";
            titleSub  = LocalizationManager.Tr("Cleanse the cursed land");
            float ft = 0f;
            while (ft < 1.5f) { ft += Time.unscaledDeltaTime; titleAlpha = Mathf.Clamp01(ft / 1.5f); yield return null; }
            yield return new WaitForSecondsRealtime(5f);
        }

        Cleanup();
    }

    // ---------- camera beats ----------
    // Apply the base framing PLUS a cinematic layer: subtle handheld drift, a
    // gentle roll, and FOV breathing so shots feel filmed, not on rails.
    private float _prevYaw; private bool _yawSet; private float _bankRoll;
    private void SetCamCinematic(Vector3 pos, Quaternion lookRot, float fov)
    {
        float tt = Time.unscaledTime;
        // Handheld positional micro-drift (Perlin, unscaled so slow-mo doesn't freeze it).
        Vector3 drift = new Vector3(
            Mathf.PerlinNoise(tt * 0.7f, 0.0f) - 0.5f,
            Mathf.PerlinNoise(0.0f, tt * 0.6f) - 0.5f,
            Mathf.PerlinNoise(tt * 0.5f, 3.3f) - 0.5f) * 0.14f;
        cam.transform.position = pos + drift;

        // Bank into the turn: roll toward the direction the aim is swinging, plus
        // a whisper of handheld roll noise.
        float yaw = lookRot.eulerAngles.y;
        if (!_yawSet) { _prevYaw = yaw; _yawSet = true; }
        float dYaw = Mathf.DeltaAngle(_prevYaw, yaw); _prevYaw = yaw;
        _bankRoll = Mathf.Lerp(_bankRoll, Mathf.Clamp(-dYaw * 0.6f, -6f, 6f), Time.unscaledDeltaTime * 3f);
        float noiseRoll = (Mathf.PerlinNoise(tt * 0.35f, 8.1f) - 0.5f) * 1.4f;
        cam.transform.rotation = lookRot * Quaternion.Euler(0f, 0f, _bankRoll + noiseRoll);

        // FOV breathing.
        cam.fieldOfView = fov + Mathf.Sin(tt * 0.7f) * 0.7f;
    }

    private IEnumerator Move(Vector3 from, Vector3 to, Vector3 look, float fovA, float fovB, float dur, bool attack)
    {
        float t = 0f;
        while (t < dur)
        {
            t += Time.unscaledDeltaTime;
            float k = Mathf.Clamp01(t / dur); float e = k * k * (3f - 2f * k);
            Vector3 p = Vector3.Lerp(from, to, e);
            SetCamCinematic(p, Quaternion.LookRotation(look - p, Vector3.up), Mathf.Lerp(fovA, fovB, e));
            if (attack) DriveHero();
            yield return null;
        }
    }

    private IEnumerator Orbit(Transform focus, float dur, float radius, float a0, float a1, float height, float lookH, float fov, bool attack)
    {
        float t = 0f;
        while (t < dur)
        {
            t += Time.unscaledDeltaTime;
            float k = Mathf.Clamp01(t / dur); float e = k * k * (3f - 2f * k);
            if (focus == null) yield break;
            float ang = Mathf.Lerp(a0, a1, e) * Mathf.Deg2Rad;
            Vector3 p = focus.position + new Vector3(Mathf.Cos(ang) * radius, height, Mathf.Sin(ang) * radius);
            SetCamCinematic(p, Quaternion.LookRotation((focus.position + Vector3.up * lookH) - p, Vector3.up), fov);
            if (attack) DriveHero();
            yield return null;
        }
    }

    private IEnumerator SlowMoPulse(float scale, float dur)
    {
        Time.timeScale = scale;
        Time.fixedDeltaTime = 0.02f * Mathf.Max(0.01f, scale);
        yield return new WaitForSecondsRealtime(dur);
        Time.timeScale = 1f;
        Time.fixedDeltaTime = 0.02f;
    }

    private float nextSwing, nextDash;
    private void DriveHero()
    {
        if (player == null || hero == null) return;
        float t = Time.unscaledTime;

        // Find the nearest LIVING enemy and the crowd's centre so every action
        // is aimed at something real — the old code swung at empty air and
        // dashed in random directions, which read as the hero flailing.
        Transform nearest = NearestEnemy(out float nearDist);
        Vector3 crowd = CrowdCentroid();

        if (nearest == null)
        {
            // Nothing to fight — drift toward where the crowd is forming instead
            // of swinging at nothing.
            if (crowd != Vector3.zero) player.TrailerRun(crowd - hero.position, 0.6f);
            return;
        }

        const float meleeReach = 3.2f;

        if (nearDist > meleeReach + 0.6f)
        {
            // Close the gap: RUN at the nearest enemy (no air-swings while out of
            // range). A periodic dash covers big gaps fast and reads as an
            // aggressive gap-closer — always TOWARD the fight, never outward.
            Vector3 toEnemy = nearest.position - hero.position; toEnemy.y = 0f;
            if (t >= nextDash && nearDist > 6f)
            {
                player.TrailerDash(toEnemy);
                nextDash = t + Random.Range(2.8f, 4.2f);
            }
            else
            {
                player.TrailerRun(toEnemy, 0.9f);
            }
            return;
        }

        // In range: swing at the nearest enemy (TrailerAutoAttack faces it and
        // only lands on real targets). Occasionally dash THROUGH the crowd to a
        // flanking angle so the hero weaves the fight instead of standing rooted.
        if (t >= nextSwing) { player.TrailerAutoAttack(); nextSwing = t + 0.42f; }
        if (t >= nextDash)
        {
            // Dash laterally around the crowd centre (perpendicular to the
            // crowd direction) so we stay in the action and keep enemies framed.
            Vector3 toCrowd = crowd - hero.position; toCrowd.y = 0f;
            if (toCrowd.sqrMagnitude < 0.01f) toCrowd = hero.forward;
            Vector3 lateral = Vector3.Cross(Vector3.up, toCrowd.normalized) * (Random.value < 0.5f ? 1f : -1f);
            player.TrailerDash((toCrowd.normalized * 0.35f + lateral).normalized);
            nextDash = t + Random.Range(3.2f, 4.5f);
        }
    }

    // ---------- enemy queries (for the hero AI) ----------
    private void RefreshEnemies()
    {
        liveEnemies.Clear();
        foreach (var e in FindObjectsByType<EnemyAI>(FindObjectsSortMode.None))
            if (e != null && e.isActiveAndEnabled) liveEnemies.Add(e);
    }

    private Transform NearestEnemy(out float dist)
    {
        if (Time.unscaledTime >= nextEnemyScan) { RefreshEnemies(); nextEnemyScan = Time.unscaledTime + 0.25f; }
        Transform best = null; dist = float.MaxValue;
        if (hero == null) return null;
        Vector3 hp = hero.position;
        foreach (var e in liveEnemies)
        {
            if (e == null) continue;
            float d = Vector3.Distance(hp, e.transform.position);
            if (d < dist) { dist = d; best = e.transform; }
        }
        return best;
    }

    private Vector3 CrowdCentroid()
    {
        if (liveEnemies.Count == 0) return Vector3.zero;
        Vector3 sum = Vector3.zero; int n = 0;
        foreach (var e in liveEnemies) { if (e == null) continue; sum += e.transform.position; n++; }
        return n > 0 ? sum / n : Vector3.zero;
    }

    // ---------- meta scenes (one-button camp / shop showcase) ----------
    private IEnumerator CampTour()
    {
        SetHUD(false);
        // 1. Reveal the whole base — a low sweep that RISES into a wide orbit,
        // so it plays as a reveal instead of a flat turntable spin.
        if (CampBounds(out Vector3 c, out float r))
        {
            float rad = Mathf.Max(16f, r * 1.5f), h = Mathf.Max(12f, r * 0.85f);
            Vector3 lowStart = c + new Vector3(-rad * 0.7f, 2.5f, -rad * 0.7f); // near ground
            Vector3 highMid  = c + new Vector3(rad * 0.25f, h, -rad);           // craned up
            yield return Move(lowStart, highMid, c + Vector3.up * 2f, 40f, 52f, 4.5f, false);
            yield return OrbitPoint(c, 7f, rad, 205f, 360f, h, 1.6f, 55f);
        }
        // 2. The Notice Board — dolly in and open the missions. Force a restock
        // first so fresh mission papers are actually on the board for the shot.
        var board = FindFirstObjectByType<NoticeBoardManager>();
        if (board != null)
        {
            PlayerPrefs.DeleteKey("LastMissionRestockTime");   // encourage fresh papers
            yield return LookAt(board.transform.position, 3f, 4.5f, 2.2f, 42f);
            SetHUD(true);                 // the board is UI
            board.OpenBoard();
            yield return new WaitForSecondsRealtime(4.5f);
            board.CloseBoard();           // MUST close, or it stays open under the map
            yield return new WaitForSecondsRealtime(0.4f);
            SetHUD(false);
        }
        // 3. The region table — open the world map and hold on all the regions.
        var mapTable = FindFirstObjectByType<MapTableInteract>();
        if (mapTable != null)
        {
            yield return LookAt(mapTable.transform.position, 3f, 4f, 1.8f, 42f);
            SetHUD(true);
            mapTable.TrailerOpenMap();
            yield return new WaitForSecondsRealtime(1.2f);
            if (!mapTable.IsMapOpen) mapTable.TrailerOpenMap();   // retry once if it didn't take
            yield return new WaitForSecondsRealtime(0.8f);

            // Scroll the UI map to reveal the regions with a CONTINUOUS eased
            // sweep — the first pass snapped between a few anchors, which read as
            // jerky. Now the target glides frame-by-frame so the map dollies and
            // zooms like a filmed move.
            var viewer = FindFirstObjectByType<MapInteractiveViewer>();
            if (viewer != null)
            {
                viewer.TrailerSetView(Vector2.zero, viewer.MinZoom);            // establish: all regions
                yield return new WaitForSecondsRealtime(1.6f);
                yield return MapCinematicSweep(viewer);
                viewer.TrailerSetView(Vector2.zero, viewer.MinZoom);            // pull back to all regions
                yield return new WaitForSecondsRealtime(1.4f);
            }
            else yield return new WaitForSecondsRealtime(6f);

            mapTable.TrailerCloseMap();
            yield return new WaitForSecondsRealtime(1f);
        }
    }

    // Continuous eased pan+zoom across the world map. Drives the viewer's
    // target every frame (the viewer smooths behind it) so the map glides
    // instead of snapping between a handful of fixed views.
    private IEnumerator MapCinematicSweep(MapInteractiveViewer viewer)
    {
        Vector2 vp = viewer.ViewportSize;
        float zAll = viewer.MinZoom;
        float zClose = Mathf.Lerp(viewer.MinZoom, viewer.MaxZoom, 0.5f);

        // A gentle S across the map: dive into the newest front, drift to the
        // far side, then ease back. Anchored-position offsets + matching zoom.
        Vector2[] pts = {
            Vector2.zero,
            new Vector2(vp.x * 0.30f, vp.y * 0.16f),
            new Vector2(-vp.x * 0.28f, -vp.y * 0.12f),
            new Vector2(vp.x * 0.10f, -vp.y * 0.04f),
        };
        float[] zooms = { zAll, zClose, zClose, Mathf.Lerp(zAll, zClose, 0.5f) };

        const float segDur = 2.8f;
        for (int i = 0; i < pts.Length - 1; i++)
        {
            float t = 0f;
            while (t < segDur)
            {
                t += Time.unscaledDeltaTime;
                float e = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t / segDur));
                viewer.TrailerSetView(Vector2.Lerp(pts[i], pts[i + 1], e), Mathf.Lerp(zooms[i], zooms[i + 1], e));
                yield return null;
            }
        }
    }

    private IEnumerator ShopTour()
    {
        // Show what the shop actually IS: selecting items, their stats/prices,
        // and the Buy/Upgrade buttons. Drive the real UI (ShopManager owns the
        // showcase since it can reach the private item list). A gentle camera
        // drift underneath keeps the hero model alive behind the panels.
        var shop = FindFirstObjectByType<ShopManager>();
        Vector3 c = cam.transform.position + cam.transform.forward * 4f;
        if (shop != null)
        {
            StartCoroutine(OrbitPoint(c, 30f, 4f, 205f, 320f, 1.6f, 1.4f, 46f)); // slow bg drift
            yield return shop.TrailerShowcaseRoutine();
        }
        else
        {
            yield return OrbitPoint(c, 9f, 4f, 200f, 250f, 1.6f, 1.4f, 46f);
        }
    }

    // Orbit the camera around a WORLD POINT (no Transform needed).
    private IEnumerator OrbitPoint(Vector3 center, float dur, float radius, float a0, float a1, float height, float lookH, float fov)
    {
        float t = 0f;
        while (t < dur)
        {
            t += Time.unscaledDeltaTime;
            float k = Mathf.Clamp01(t / dur); float e = k * k * (3f - 2f * k);
            float ang = Mathf.Lerp(a0, a1, e) * Mathf.Deg2Rad;
            Vector3 p = center + new Vector3(Mathf.Cos(ang) * radius, height, Mathf.Sin(ang) * radius);
            SetCamCinematic(p, Quaternion.LookRotation((center + Vector3.up * lookH) - p, Vector3.up), fov);
            yield return null;
        }
    }

    // Ease the camera to a framing of `target` and settle.
    private IEnumerator LookAt(Vector3 target, float dur, float dist, float height, float fov)
    {
        Vector3 from = cam.transform.position;
        Vector3 dir = (cam.transform.position - target); dir.y = 0f;
        if (dir.sqrMagnitude < 0.01f) dir = Vector3.back;
        Vector3 to = target + dir.normalized * dist + Vector3.up * height;
        float t = 0f;
        while (t < dur)
        {
            t += Time.unscaledDeltaTime;
            float k = Mathf.Clamp01(t / dur); float e = k * k * (3f - 2f * k);
            Vector3 p = Vector3.Lerp(from, to, e);
            SetCamCinematic(p, Quaternion.LookRotation((target + Vector3.up * 0.8f) - p, Vector3.up), fov);
            yield return null;
        }
    }

    private bool CampBounds(out Vector3 center, out float radius)
    {
        center = Vector3.zero; radius = 12f;
        var buildings = FindObjectsByType<CampBuilding>(FindObjectsSortMode.None);
        if (buildings == null || buildings.Length == 0) return false;
        Vector3 sum = Vector3.zero; int n = 0;
        foreach (var b in buildings) { if (b == null) continue; sum += b.transform.position; n++; }
        if (n == 0) return false;
        center = sum / n;
        float maxD = 0f;
        foreach (var b in buildings) { if (b == null) continue; maxD = Mathf.Max(maxD, Vector3.Distance(center, b.transform.position)); }
        radius = Mathf.Max(10f, maxD);
        return true;
    }

    // ---------- staging ----------
    private GameObject SpawnRing(int count, float minR, float maxR)
    {
        var spawner = FindFirstObjectByType<EnemySpawner>();
        if (hero == null) return null;
        var prefabs = new List<GameObject>();
        if (spawner != null && spawner.enemyPool != null)
            foreach (var e in spawner.enemyPool) if (e != null && e.enemyPrefab != null) prefabs.Add(e.enemyPrefab);
        if (prefabs.Count == 0) return null;

        GameObject last = null;
        for (int i = 0; i < count; i++)
        {
            float ang = (360f / Mathf.Max(1, count)) * i + Random.Range(-6f, 6f);
            float r = Random.Range(minR, maxR);
            Vector3 dir = Quaternion.Euler(0f, ang, 0f) * Vector3.forward;
            Vector3 p = hero.position + dir * r;
            if (Physics.Raycast(p + Vector3.up * 14f, Vector3.down, out var hit, 40f)) p.y = hit.point.y;
            else if (Terrain.activeTerrain != null) p.y = Terrain.activeTerrain.SampleHeight(p) + Terrain.activeTerrain.transform.position.y;
            // Round-robin through the distinct enemy types so the crowd reads as
            // VARIED (skeletons, archers, brutes, ...) instead of 30 clones.
            last = Instantiate(prefabs[i % prefabs.Count], p, Quaternion.LookRotation(-dir));
        }
        return last;
    }

    // Spawn a frozen formation of enemies AHEAD of the hero (a grid of rows),
    // each facing back toward the hero — the "army before you" cold-open shot.
    // Uses the same enemy pool as SpawnRing so the horde reads as varied.
    private void SpawnArmyInFront(int rows, int perRow, float startDist, float spacing)
    {
        var spawner = FindFirstObjectByType<EnemySpawner>();
        if (hero == null) return;
        var prefabs = new List<GameObject>();
        if (spawner != null && spawner.enemyPool != null)
            foreach (var e in spawner.enemyPool) if (e != null && e.enemyPrefab != null) prefabs.Add(e.enemyPrefab);
        if (prefabs.Count == 0) return;

        Vector3 fwd = hero.forward; fwd.y = 0f;
        if (fwd.sqrMagnitude < 0.01f) fwd = Vector3.forward;
        fwd.Normalize();
        Vector3 right = Vector3.Cross(Vector3.up, fwd);

        int idx = 0;
        for (int r = 0; r < rows; r++)
        {
            float rowDist = startDist + r * spacing * 1.6f;
            for (int c = 0; c < perRow; c++)
            {
                float lateral = (c - (perRow - 1) * 0.5f) * spacing;
                Vector3 p = hero.position + fwd * rowDist + right * lateral;
                // Slight scatter so the rows don't read as a rigid checkerboard.
                p += fwd * Random.Range(-0.4f, 0.4f) + right * Random.Range(-0.4f, 0.4f);
                if (Physics.Raycast(p + Vector3.up * 20f, Vector3.down, out var hit, 60f)) p.y = hit.point.y;
                else if (Terrain.activeTerrain != null) p.y = Terrain.activeTerrain.SampleHeight(p) + Terrain.activeTerrain.transform.position.y;

                // Face back toward the hero — an army confronting the player.
                Vector3 face = hero.position - p; face.y = 0f;
                Quaternion rot = face.sqrMagnitude > 0.01f ? Quaternion.LookRotation(face) : Quaternion.identity;
                Instantiate(prefabs[idx % prefabs.Count], p, rot);
                idx++;
            }
        }
    }

    // ---------- HUD ----------
    private readonly List<Canvas> hidden = new List<Canvas>();
    private void SetHUD(bool on)
    {
        if (on) { foreach (var c in hidden) if (c != null) c.enabled = true; hidden.Clear(); return; }
        foreach (var c in FindObjectsByType<Canvas>(FindObjectsSortMode.None))
        {
            if (c == null || !c.enabled || c.gameObject == gameObject) continue;
            hidden.Add(c); c.enabled = false;
        }
    }

    private void Cleanup()
    {
        Time.timeScale = 1f;
        Time.fixedDeltaTime = 0.02f;
        EnemyAI.GlobalFreeze = false;
        EnemyAI.SuppressCombatVocals = false;
        PlayerPrefs.SetInt("Settings_TutorialHints", _hintsWere);
        SetHUD(true);
        if (player != null) { player.isControlBlocked = false; player.isCinematicInvincible = false; }
        if (cam != null) cam.fieldOfView = fov0;
        if (follow != null) follow.enabled = followWas;
        IsPlaying = false;
        Destroy(gameObject);
    }

    private void OnGUI()
    {
        // Stylised STACK hook — a big multiplier readout during the typhoon so
        // the game's signature mechanic is visible without the full cluttered HUD.
        if (showStack && player != null && player.currentMultiplier > 1)
        {
            if (stackStyle == null)
            {
                stackStyle = new GUIStyle { alignment = TextAnchor.UpperCenter, fontStyle = FontStyle.Bold, richText = true,
                    fontSize = Mathf.RoundToInt(Screen.height * 0.075f) };
            }
            Color c = player.currentMultiplier >= 5 ? new Color(1f, 0.35f, 0.25f)
                    : player.currentMultiplier >= 4 ? new Color(1f, 0.75f, 0.2f)
                    : new Color(1f, 0.95f, 0.6f);
            stackStyle.normal.textColor = c;
            float pulse = 1f + 0.05f * Mathf.Sin(Time.unscaledTime * 8f);
            var r = new Rect(0, Screen.height * (0.10f / pulse), Screen.width, Screen.height * 0.2f);
            GUI.Label(r, $"STACK ×{player.currentMultiplier}", stackStyle);
        }

        if (titleAlpha <= 0.001f) return;
        if (solid == null) { solid = new Texture2D(1, 1); solid.SetPixel(0, 0, Color.white); solid.Apply(); }
        if (mainStyle == null)
        {
            mainStyle = new GUIStyle { alignment = TextAnchor.MiddleCenter, fontSize = Mathf.RoundToInt(Screen.height * 0.09f), fontStyle = FontStyle.Bold };
            mainStyle.normal.textColor = Color.white;
            subStyle = new GUIStyle { alignment = TextAnchor.MiddleCenter, fontSize = Mathf.RoundToInt(Screen.height * 0.03f) };
            subStyle.normal.textColor = new Color(0.85f, 0.9f, 1f);
            wishlistStyle = new GUIStyle { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold,
                fontSize = Mathf.RoundToInt(Screen.height * 0.032f) };
            wishlistStyle.normal.textColor = new Color(0.32f, 0.75f, 1f); // Steam blue
        }
        // dim vignette behind the title
        GUI.color = new Color(0f, 0f, 0f, 0.55f * titleAlpha);
        GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), solid);
        GUI.color = new Color(1f, 1f, 1f, titleAlpha);
        GUI.Label(new Rect(0, Screen.height * 0.40f, Screen.width, Screen.height * 0.14f), titleMain, mainStyle);
        GUI.Label(new Rect(0, Screen.height * 0.54f, Screen.width, Screen.height * 0.06f), titleSub, subStyle);
        // Wishlist CTA — pulses gently so it draws the eye on the end card.
        if (!string.IsNullOrEmpty(titleWishlist))
        {
            float wl = 0.6f + 0.4f * Mathf.Abs(Mathf.Sin(Time.unscaledTime * 2f));
            GUI.color = new Color(1f, 1f, 1f, titleAlpha * wl);
            GUI.Label(new Rect(0, Screen.height * 0.66f, Screen.width, Screen.height * 0.06f),
                "⭐ " + titleWishlist + " ⭐", wishlistStyle);
        }
        GUI.color = Color.white;
    }
}
