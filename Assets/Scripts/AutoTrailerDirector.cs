using UnityEngine;
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
    private string titleMain = "AETHELGARD";
    private string titleSub  = "Cleanse the cursed land";
    private GUIStyle mainStyle, subStyle, stackStyle;
    private Texture2D solid;
    private bool showStack;

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
        var pgo = GameObject.FindGameObjectWithTag("Player");
        player = pgo != null ? pgo.GetComponent<PlayerController>() : null;
        hero = pgo != null ? pgo.transform : null;
        if (cam == null || hero == null) { Cleanup(); yield break; }

        follow = cam.GetComponent("CameraFollow") as MonoBehaviour;
        followWas = follow != null && follow.enabled;
        if (follow != null) follow.enabled = false;
        fov0 = cam.fieldOfView;

        SetHUD(false);
        if (player != null) { player.isControlBlocked = true; player.isCinematicInvincible = true; }

        var dnc = FindFirstObjectByType<DayNightCycle>();

        // ===== BEAT 0 — the cursed land (storm dusk, slow drift) =====
        if (dnc != null) { dnc.isWeatherLocked = true; dnc.timeOfDay = 18.7f; dnc.ForceWeather(WeatherState.Storm); }
        EnemyAI.GlobalFreeze = true;
        SpawnRing(6, 6f, 11f);
        {
            Vector3 c = hero.position;
            Vector3 a = c + new Vector3(-16f, 12f, -16f);
            Vector3 b = c + new Vector3(10f, 9f, -18f);
            yield return Move(a, b, c + Vector3.up * 1.2f, 55f, 50f, 8.5f, false);
        }

        // ===== BEAT 1 — the spark (first blows) =====
        if (dnc != null) dnc.ForceWeather(WeatherState.Precipitation);
        EnemyAI.GlobalFreeze = false;
        yield return Orbit(hero, 4.2f, 5f, 20f, 120f, 2.3f, 1.3f, 46f, true);

        // ===== BEAT 2 — become the storm (STACK typhoon) =====
        EnemyAI.GlobalFreeze = true;
        SpawnRing(30, 4.5f, 9f);                                              // varied crowd (round-robin types)
        yield return Orbit(hero, 1.4f, 7f, 30f, 70f, 3.2f, 1.4f, 52f, false); // pose on the frozen crowd
        EnemyAI.GlobalFreeze = false;
        if (dnc != null) dnc.ForceWeather(WeatherState.Clear);
        showStack = true;                                                    // stylised on-screen STACK counter
        yield return Orbit(hero, 4.5f, 6.5f, 70f, 250f, 2.8f, 1.35f, 55f, true);
        StartCoroutine(SlowMoPulse(0.35f, 0.9f));                            // punch the x5 peak
        yield return Orbit(hero, 4.0f, 5.5f, 250f, 430f, 2.4f, 1.3f, 52f, true);
        showStack = false;

        // ===== BEAT 2b — the boss (big elite + low-angle slow-mo execution) =====
        var boss = SpawnRing(1, 4f, 4f);
        if (boss != null) { boss.transform.localScale *= 2.5f; var ba = boss.GetComponent<EnemyAI>(); if (ba != null) ba.maxHealth *= 2.5f; }
        yield return Orbit(hero, 3.5f, 4.5f, 0f, 150f, 1.6f, 1.7f, 42f, true);  // low, tense
        StartCoroutine(SlowMoPulse(0.2f, 1.2f));                                // execution slow-mo
        yield return Orbit(hero, 1.6f, 3.5f, 150f, 200f, 1.3f, 1.6f, 40f, true);

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
            titleMain = LocalizationManager.Tr("AETHELGARD");
            titleSub  = LocalizationManager.Tr("Cleanse the cursed land");
            float ft = 0f;
            while (ft < 1.5f) { ft += Time.unscaledDeltaTime; titleAlpha = Mathf.Clamp01(ft / 1.5f); yield return null; }
            yield return new WaitForSecondsRealtime(5f);
        }

        Cleanup();
    }

    // ---------- camera beats ----------
    private IEnumerator Move(Vector3 from, Vector3 to, Vector3 look, float fovA, float fovB, float dur, bool attack)
    {
        float t = 0f;
        while (t < dur)
        {
            t += Time.unscaledDeltaTime;
            float k = Mathf.Clamp01(t / dur); float e = k * k * (3f - 2f * k);
            Vector3 p = Vector3.Lerp(from, to, e);
            cam.transform.position = p;
            cam.transform.rotation = Quaternion.LookRotation(look - p, Vector3.up);
            cam.fieldOfView = Mathf.Lerp(fovA, fovB, e);
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
            cam.transform.position = p;
            cam.transform.rotation = Quaternion.LookRotation((focus.position + Vector3.up * lookH) - p, Vector3.up);
            cam.fieldOfView = fov;
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

    private float nextSwing;
    private void DriveHero()
    {
        if (player == null) return;
        if (Time.unscaledTime >= nextSwing)
        {
            player.TrailerAutoAttack();
            nextSwing = Time.unscaledTime + 0.35f;
        }
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
        EnemyAI.GlobalFreeze = false;
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
        }
        // dim vignette behind the title
        GUI.color = new Color(0f, 0f, 0f, 0.55f * titleAlpha);
        GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), solid);
        GUI.color = new Color(1f, 1f, 1f, titleAlpha);
        GUI.Label(new Rect(0, Screen.height * 0.40f, Screen.width, Screen.height * 0.14f), titleMain, mainStyle);
        GUI.Label(new Rect(0, Screen.height * 0.54f, Screen.width, Screen.height * 0.06f), titleSub, subStyle);
        GUI.color = Color.white;
    }
}
