using UnityEngine;
using System.Collections.Generic;

// In-engine capture toolkit for shooting the game trailer. Hotkeys let you
// detach a free-fly camera, hide the HUD, stage a horde for the STACK typhoon,
// control time / weather / time-of-day, freeze enemies for posing, and fire the
// region-victory cinematic on demand.
//
// EDITOR / DEV-BUILD ONLY — compiled out of release builds so the debug hotkeys
// can never reach players. Self-bootstraps; no scene wiring needed.
//
// Keys:
//   F1  toggle free-fly camera (WASD move, Q/E down/up, RMB+mouse look,
//       Shift = fast, scroll = speed)
//   F2  toggle HUD (all canvases)
//   F3  spawn a horde around the player (for the STACK typhoon)
//   F4  toggle slow-motion (0.25x)
//   F5  cycle time of day (dawn / noon / dusk / night)
//   F6  cycle weather (clear / storm / rain)
//   F7  fire the region-victory cinematic
//   F8  freeze / unfreeze all enemies (posing)
//   F9  kill all enemies
//   F12 toggle this help overlay
#if UNITY_EDITOR || DEVELOPMENT_BUILD
public class TrailerToolkit : MonoBehaviour
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        if (s_instance != null) return;
        var go = new GameObject("TrailerToolkit");
        s_instance = go.AddComponent<TrailerToolkit>();
        DontDestroyOnLoad(go);
    }

    private static TrailerToolkit s_instance;

    private bool freeCam;
    private bool hudHidden;
    private bool slowMo;
    private bool showHelp = true;

    private Camera cam;
    private MonoBehaviour camFollow;         // CameraFollow, disabled while free-flying
    private float flySpeed = 12f;
    private float yaw, pitch;

    private int todIndex = 1;                // 0 dawn,1 noon,2 dusk,3 night
    private int weatherIndex;

    private readonly List<Canvas> hiddenCanvases = new List<Canvas>();
    private GUIStyle helpStyle;

    private void Update()
    {
        if (Input.GetKeyDown(KeyCode.F1)) ToggleFreeCam();
        if (Input.GetKeyDown(KeyCode.F2)) ToggleHUD();
        if (Input.GetKeyDown(KeyCode.F3)) SpawnHorde(25);
        if (Input.GetKeyDown(KeyCode.F4)) ToggleSlowMo();
        if (Input.GetKeyDown(KeyCode.F5)) CycleTimeOfDay();
        if (Input.GetKeyDown(KeyCode.F6)) CycleWeather();
        if (Input.GetKeyDown(KeyCode.F7)) TriggerVictory();
        if (Input.GetKeyDown(KeyCode.F8)) ToggleFreezeEnemies();
        if (Input.GetKeyDown(KeyCode.F9)) KillAllEnemies();
        if (Input.GetKeyDown(KeyCode.F12)) showHelp = !showHelp;

        // Authored self-running cinematic shots (auto camera + staging).
        if (Input.GetKeyDown(KeyCode.Alpha1)) CinematicSequencer.Play("hero");
        if (Input.GetKeyDown(KeyCode.Alpha2)) CinematicSequencer.Play("encircle");
        if (Input.GetKeyDown(KeyCode.Alpha3)) CinematicSequencer.Play("orbit");
        if (Input.GetKeyDown(KeyCode.Alpha4)) CinematicSequencer.Play("crane");

        // Don't fight a running authored sequence for the camera.
        if (freeCam && !CinematicSequencer.IsPlaying) DriveFreeCam();
    }

    // ---------------- Free-fly camera ----------------
    private void ToggleFreeCam()
    {
        if (cam == null) cam = Camera.main;
        if (cam == null) return;

        freeCam = !freeCam;
        if (camFollow == null)
        {
            var cf = cam.GetComponent("CameraFollow") as MonoBehaviour;
            camFollow = cf;
        }
        if (camFollow != null) camFollow.enabled = !freeCam;

        if (freeCam)
        {
            Vector3 e = cam.transform.eulerAngles;
            yaw = e.y; pitch = e.x;
        }
    }

    private void DriveFreeCam()
    {
        if (cam == null) { freeCam = false; return; }
        var t = cam.transform;

        // Mouse look only while holding RMB so you can still click the editor.
        if (Input.GetMouseButton(1))
        {
            yaw += Input.GetAxis("Mouse X") * 3f;
            pitch -= Input.GetAxis("Mouse Y") * 3f;
            pitch = Mathf.Clamp(pitch, -89f, 89f);
            t.rotation = Quaternion.Euler(pitch, yaw, 0f);
        }

        flySpeed = Mathf.Clamp(flySpeed + Input.mouseScrollDelta.y * 2f, 1f, 120f);
        float sp = flySpeed * (Input.GetKey(KeyCode.LeftShift) ? 3f : 1f) * Time.unscaledDeltaTime;

        Vector3 move = Vector3.zero;
        if (Input.GetKey(KeyCode.W)) move += t.forward;
        if (Input.GetKey(KeyCode.S)) move -= t.forward;
        if (Input.GetKey(KeyCode.D)) move += t.right;
        if (Input.GetKey(KeyCode.A)) move -= t.right;
        if (Input.GetKey(KeyCode.E)) move += Vector3.up;
        if (Input.GetKey(KeyCode.Q)) move -= Vector3.up;
        t.position += move.normalized * sp;
    }

    // ---------------- HUD ----------------
    private void ToggleHUD()
    {
        hudHidden = !hudHidden;
        if (hudHidden)
        {
            hiddenCanvases.Clear();
            foreach (var c in FindObjectsByType<Canvas>(FindObjectsSortMode.None))
            {
                if (c == null || !c.enabled) continue;
                if (c.gameObject == gameObject) continue;
                hiddenCanvases.Add(c);
                c.enabled = false;
            }
        }
        else
        {
            foreach (var c in hiddenCanvases) if (c != null) c.enabled = true;
            hiddenCanvases.Clear();
        }
    }

    // ---------------- Horde ----------------
    private void SpawnHorde(int count)
    {
        var spawner = FindFirstObjectByType<EnemySpawner>();
        Transform player = GameObject.FindGameObjectWithTag("Player")?.transform;
        if (player == null) return;

        var prefabs = new List<GameObject>();
        if (spawner != null && spawner.enemyPool != null)
            foreach (var e in spawner.enemyPool) if (e != null && e.enemyPrefab != null) prefabs.Add(e.enemyPrefab);
        if (prefabs.Count == 0) return;

        for (int i = 0; i < count; i++)
        {
            Vector2 c = Random.insideUnitCircle.normalized * Random.Range(3f, 9f);
            Vector3 p = player.position + new Vector3(c.x, 0f, c.y);
            if (Physics.Raycast(p + Vector3.up * 12f, Vector3.down, out var hit, 30f)) p.y = hit.point.y;
            else if (Terrain.activeTerrain != null) p.y = Terrain.activeTerrain.SampleHeight(p) + Terrain.activeTerrain.transform.position.y;
            Instantiate(prefabs[Random.Range(0, prefabs.Count)], p, Quaternion.Euler(0f, Random.Range(0f, 360f), 0f));
        }
    }

    private void KillAllEnemies()
    {
        foreach (var e in FindObjectsByType<EnemyAI>(FindObjectsSortMode.None))
            if (e != null) Destroy(e.gameObject);
    }

    private void ToggleFreezeEnemies() => EnemyAI.GlobalFreeze = !EnemyAI.GlobalFreeze;

    // ---------------- Time / weather ----------------
    private void ToggleSlowMo()
    {
        slowMo = !slowMo;
        Time.timeScale = slowMo ? 0.25f : 1f;
        Time.fixedDeltaTime = 0.02f * Mathf.Max(0.01f, Time.timeScale);
    }

    private void CycleTimeOfDay()
    {
        var dnc = FindFirstObjectByType<DayNightCycle>();
        if (dnc == null) return;
        todIndex = (todIndex + 1) % 4;
        dnc.timeOfDay = todIndex switch { 0 => 6.5f, 1 => 12f, 2 => 18.5f, _ => 22f };
    }

    private void CycleWeather()
    {
        var dnc = FindFirstObjectByType<DayNightCycle>();
        if (dnc == null) return;
        weatherIndex = (weatherIndex + 1) % 3;
        dnc.isWeatherLocked = true;
        dnc.ForceWeather((WeatherState)weatherIndex);
    }

    private void TriggerVictory()
    {
        var rm = FindFirstObjectByType<RegionManager>();
        if (rm != null) rm.DebugTriggerVictoryCinematic();
    }

    // ---------------- Help overlay ----------------
    private void OnGUI()
    {
        if (!showHelp) return;
        if (helpStyle == null)
            helpStyle = new GUIStyle(GUI.skin.box) { alignment = TextAnchor.UpperLeft, fontSize = 12, richText = true };

        string s =
            "<b>TRAILER TOOLKIT</b>  (F12 hide)\n" +
            $"F1 free-cam: {(freeCam ? "ON (RMB look, WASD/QE, Shift fast, scroll speed)" : "off")}\n" +
            $"F2 HUD: {(hudHidden ? "hidden" : "shown")}\n" +
            "F3 spawn horde (25)   F9 kill all\n" +
            $"F4 slow-mo: {(slowMo ? "0.25x" : "1x")}\n" +
            $"F5 time of day: {todIndex switch { 0 => "dawn", 1 => "noon", 2 => "dusk", _ => "night" }}\n" +
            $"F6 weather: {(WeatherState)weatherIndex}\n" +
            "F7 victory cinematic\n" +
            $"F8 freeze enemies: {(EnemyAI.GlobalFreeze ? "FROZEN" : "off")}\n" +
            "<b>Auto shots:</b> 1 hero · 2 encircle · 3 orbit · 4 crane";
        GUI.Label(new Rect(12, 12, 380, 210), s, helpStyle);
    }
}
#endif
