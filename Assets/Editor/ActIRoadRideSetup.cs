using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;
using UnityEngine.Splines;
using Unity.Mathematics;
using Unity.Cinemachine;

// One-click wiring for Act I (the road ride). Run it AFTER you've drawn a Spline
// along the road (any name containing "road") and built the camera rig
// (Tools ▸ Lore Trailer ▸ Build Camera Rig).
//
//   Tools ▸ Lore Trailer ▸ Setup Act I Road Ride
//
// It does the fiddly wiring so you don't fight splines/positions:
//   * keeps the horse alive (disables Level1_QuestManager / StoryExtractionPoint
//     which hide it, and the EnemySpawner so no enemies clutter the shot),
//   * makes the horse auto-ride the road spline (no Timeline keys),
//   * parks the PLAYER on the horse (parented + physics off) so it stops flying
//     off the map,
//   * turns the tracking cameras into FOLLOW cameras with an offset (NO camera
//     spline needed — that's why cameras fell through the ground), and
//   * places CM_01 low on the road facing forward so the horse gallops past it.
//
// After running: press Play to preview. If the horse runs the wrong way, tick
// Reverse / set Model Yaw Offset 180 on TrailerHorseRide; nudge the camera
// FollowOffset values and the rider saddle height to taste.
public static class ActIRoadRideSetup
{
    // Tunables (sensible defaults; tweak in the scene afterwards).
    private static readonly Vector3 RiderSaddleOffset = new Vector3(0f, 1.15f, 0.05f);
    private static readonly Vector3 Cam02Offset = new Vector3(4.5f, 2.6f, -2.0f);  // alongside
    private static readonly Vector3 Cam03Offset = new Vector3(2.0f, 1.2f, -3.5f);  // low, behind-ish
    private static readonly Vector3 Cam04Offset = new Vector3(0f, 6.5f, -9f);       // crane behind+high
    private const string RigName = "LoreTrailer_Rig";

    [MenuItem("Tools/Lore Trailer/Setup Act I Road Ride")]
    public static void Setup()
    {
        Transform horse = FindByNameContains("evacuationhorse") ?? FindByNameContains("horse");
        if (horse == null) { Warn("No horse found (expected an object containing 'horse')."); return; }

        SplineContainer road = FindRoadSpline();
        if (road == null) { Warn("No road Spline found. Draw one along the road (name it with 'road') and run again."); return; }

        Undo.SetCurrentGroupName("Setup Act I Road Ride");

        // 1) Stop the things that hide the horse / clutter the shot.
        int disabled = DisableInterferers();

        // 2) Horse active + auto-ride.
        if (!horse.gameObject.activeSelf) { Undo.RecordObject(horse.gameObject, "activate horse"); horse.gameObject.SetActive(true); }
        var ride = horse.GetComponent<TrailerHorseRide>();
        if (ride == null) ride = Undo.AddComponent<TrailerHorseRide>(horse.gameObject);
        Undo.RecordObject(ride, "config ride");
        ride.path = road;
        ride.autoFitSeconds = 24f;   // a touch slower + keeps the horse on-road through the crane
        ride.playOnStart = true;
        ride.driveFromTimeline = false;
        ride.faceAlongPath = true;
        ride.loop = false;
        ride.groundSnapOverrun = true;
        if (horse.GetComponent<HorseAudioController>() == null) Undo.AddComponent<HorseAudioController>(horse.gameObject);

        // Hoof dust from the pack (Hovl Studio) — kicked up behind the gallop.
        AddHoofDust(horse);

        // 3) Park the player on the horse so it stops flying off.
        bool riderOk = ParkPlayerOnHorse(horse);

        // 4) Cameras: follow-with-offset (no spline), + a static shot-1 on the road.
        int cams = ConfigureCameras(horse, road);

        // 5) Make the trailer cameras ACTUALLY LIVE. Without this you see the
        //    gameplay Main Camera (which sits low / in the ground) because the
        //    rig is built disabled and its director doesn't auto-play — the #1
        //    reason "the camera just sits in the ground".
        bool rigLive = ActivateRigAndDirector();

        // 6) Stop every OTHER PlayableDirector (the level's intro director) from
        //    auto-playing — it repositions the horse and is what "teleports the
        //    horse back the moment it starts running".
        int killedDirectors = NeutralizeRivalDirectors();

        // 7) Take the gameplay camera driver out of the way so it can't fight the
        //    Cinemachine brain for the Main Camera.
        DisableGameplayCameraFollow();

        // 8) Slow-mo punch on the hoof-strike shot (CM_03), synced to the Timeline.
        AddSlowMoBeats();

        // 9) Cinematic grade + motion blur (the biggest visual lift).
        TrailerCinematicPostFX.Apply(TrailerCinematicPostFX.Preset.RoadMoody, false);

        // 10) Moody storm + rain + fog for atmosphere.
        bool weatherOk = TrailerWeatherSetup.Setup(WeatherState.Storm, false);

        // 11) Trailer soundscape: wind + rain beds, raven cry, distant thunder
        //     (horse hooves/breath/snort come from HorseAudioController above).
        AddTrailerAmbience();

        EditorSceneMarkDirty();

        EditorUtility.DisplayDialog("Act I Road Ride",
            $"Wired up:\n" +
            $"  • Disabled {disabled} interfering component(s) (quest/extraction/spawner).\n" +
            $"  • Neutralized {killedDirectors} rival PlayableDirector(s) (intro timeline that snapped the horse back).\n" +
            $"  • Horse '{horse.name}' auto-rides '{road.name}' over {ride.autoFitSeconds:0}s.\n" +
            $"  • Rider on horse: {(riderOk ? "yes" : "NO player found — place one manually")}.\n" +
            $"  • Configured {cams} camera(s): CM_01 gallop-past + CM_03 low chase have tension shake; CM_02 steady alongside; CM_04 cranes UP over the valley at the end.\n" +
            $"  • Rider set to the seated on-horse pose; hoof dust added; horse rides OFF into the distance at the end (ground-snapped, no running in place / no clipping through hills).\n" +
            $"  • Cinematic grade + motion blur applied; weather: {(weatherOk ? "storm + rain + fog" : "NO DayNightCycle — run 'Setup Weather' after adding one")}.\n" +
            $"  • Trailer rig live + auto-play: {(rigLive ? "YES" : "NOT FOUND — run 'Build Camera Rig' first")}.\n\n" +
            "PRESS PLAY to preview — the Timeline now starts itself, in sync with the horse.\n" +
            "If the horse runs the wrong way → TrailerHorseRide: tick Reverse / set Model Yaw Offset 180 (then rotate CM_01 180° too).\n" +
            "Nudge camera FollowOffset + rider saddle height to taste.", "OK");
    }

    // Enable the LoreTrailer_Rig and switch its PlayableDirector to Play-On-Awake
    // so the cameras go live the instant you press Play (and stay in sync with the
    // horse, which also starts at frame 0).
    private static bool ActivateRigAndDirector()
    {
        var rig = GameObject.Find(RigName);
        if (rig == null)
        {
            // Find-by-name misses inactive roots; scan inactive too.
            rig = Object.FindObjectsByType<PlayableDirector>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                .Select(d => d.gameObject).FirstOrDefault(g => g.name == RigName);
        }
        if (rig == null) return false;

        if (!rig.activeSelf) { Undo.RecordObject(rig, "enable rig"); rig.SetActive(true); }

        var dir = rig.GetComponent<PlayableDirector>();
        if (dir != null)
        {
            Undo.RecordObject(dir, "director play on awake");
            dir.playOnAwake = true;
            dir.timeUpdateMode = DirectorUpdateMode.GameTime;
            EditorUtility.SetDirty(dir);
        }

        // Make sure the Brain that the track is bound to is actually enabled.
        var brain = Object.FindFirstObjectByType<CinemachineBrain>(FindObjectsInactive.Include);
        if (brain != null && !brain.enabled) { Undo.RecordObject(brain, "enable brain"); brain.enabled = true; }
        return true;
    }

    // Any PlayableDirector that is NOT our trailer rig (the level intro director)
    // will auto-play on Awake and drive/reposition the horse or camera. Stop them.
    private static int NeutralizeRivalDirectors()
    {
        int n = 0;
        foreach (var dir in Object.FindObjectsByType<PlayableDirector>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (dir == null || dir.gameObject.name == RigName) continue;
            Undo.RecordObject(dir, "neutralize director");
            dir.playOnAwake = false;
            dir.Stop();
            dir.enabled = false;
            EditorUtility.SetDirty(dir);
            n++;
        }
        return n;
    }

    // Turn off the gameplay CameraFollow so it doesn't keep steering the Main
    // Camera underground while the Cinemachine brain is trying to drive it.
    private static void DisableGameplayCameraFollow()
    {
        foreach (var mb in Object.FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (mb == null || !mb.enabled) continue;
            if (mb.GetType().Name == "CameraFollow")
            {
                Undo.RecordObject(mb, "disable gameplay camera");
                mb.enabled = false;
                EditorUtility.SetDirty(mb);
            }
        }
    }

    // --- horse / scene ---

    private static int DisableInterferers()
    {
        int n = 0;
        string[] typeNames = { "Level1_QuestManager", "StoryExtractionPoint", "EnemySpawner" };
        foreach (var mb in Object.FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (mb == null || !mb.enabled) continue;
            if (typeNames.Contains(mb.GetType().Name))
            {
                Undo.RecordObject(mb, "disable interferer");
                mb.enabled = false;
                n++;
            }
        }
        return n;
    }

    private static bool ParkPlayerOnHorse(Transform horse)
    {
        // Prefer a rider the user already placed UNDER the horse (e.g.
        // "Player(OnHorse)") — respect their hand-tuned seat position.
        Transform existingRider = FindExistingRider(horse);
        var playerGo = existingRider != null ? existingRider.gameObject
                                             : GameObject.FindGameObjectWithTag("Player");
        if (playerGo == null) return false;
        Undo.RegisterFullObjectHierarchyUndo(playerGo, "park rider");

        if (!playerGo.activeSelf) playerGo.SetActive(true);

        // Kill physics / control so it can't fly off.
        var pc = playerGo.GetComponent("PlayerController") as MonoBehaviour;
        if (pc != null) pc.enabled = false;
        var cc = playerGo.GetComponent<CharacterController>();
        if (cc != null) cc.enabled = false;
        var rb = playerGo.GetComponent<Rigidbody>();
        if (rb != null) { rb.isKinematic = true; rb.linearVelocity = Vector3.zero; }

        // Put the rider into the seated riding pose so it looks like it's really
        // on the horse (not standing/idle), and kill its root motion so it can't
        // drift off the saddle.
        ApplyRiderPose(playerGo);

        // Already seated on the horse (by the user or a previous run): leave the
        // transform EXACTLY where it is. The horse is scaled 0.5, so re-writing a
        // local offset here would shrink the rider and sink it into the saddle —
        // which is the "rider still not on the horse properly" bug.
        if (playerGo.transform.IsChildOf(horse)) return true;

        // Not on the horse yet: parent keeping WORLD transform (so the 0.5 horse
        // scale doesn't halve the rider) and seat it with a WORLD-space offset
        // (unaffected by the horse's scale), facing the horse's forward.
        playerGo.transform.SetParent(horse, true);
        playerGo.transform.position = horse.position
                                     + Vector3.up * RiderSaddleOffset.y
                                     + horse.forward * RiderSaddleOffset.z
                                     + horse.right * RiderSaddleOffset.x;
        playerGo.transform.rotation = horse.rotation;
        return true;
    }

    // Add hoof dust behind the gallop, using a dust prefab from the pack.
    private static readonly string[] DustPrefabPaths =
    {
        "Assets/Hovl Studio/Magic effects pack/Prefabs/Smoke effects/Dust loop.prefab",
        "Assets/Hovl Studio/Magic effects pack/Prefabs/Smoke effects/Dust ground.prefab",
    };
    private static void AddHoofDust(Transform horse)
    {
        var dust = horse.GetComponent<HorseDustController>();
        if (dust == null) dust = Undo.AddComponent<HorseDustController>(horse.gameObject);
        Undo.RecordObject(dust, "config hoof dust");
        if (dust.dustPrefab == null)
        {
            foreach (var path in DustPrefabPaths)
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (prefab != null) { dust.dustPrefab = prefab; break; }
            }
        }
        EditorUtility.SetDirty(dust);
    }

    // Give the rider the game's on-horse (crouch-idle) pose and disable its root
    // motion so it stays glued to the saddle while the horse moves.
    private static void ApplyRiderPose(GameObject rider)
    {
        var anim = rider.GetComponentInChildren<Animator>();
        if (anim == null) return;
        Undo.RecordObject(anim, "rider pose");
        var ctrl = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>("Assets/Animators/OnHorseAnimator.controller");
        if (ctrl != null) anim.runtimeAnimatorController = ctrl;
        anim.applyRootMotion = false;
        EditorUtility.SetDirty(anim);
    }

    // Look for a rider already parented under the horse — a Player-tagged child,
    // or an object whose name hints it's the seated rider/dummy.
    private static Transform FindExistingRider(Transform horse)
    {
        foreach (Transform t in horse.GetComponentsInChildren<Transform>(true))
        {
            if (t == horse) continue;
            if (t.CompareTag("Player")) return t;
            string n = t.name.ToLowerInvariant();
            if (n.Contains("onhorse") || n.Contains("rider") || n.Contains("player"))
                return t;
        }
        return null;
    }

    // --- cameras ---

    private static int ConfigureCameras(Transform horse, SplineContainer road)
    {
        int count = 0;
        foreach (var cam in Object.FindObjectsByType<CinemachineCamera>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (cam == null) continue;
            string n = cam.name;

            // Slightly longer lenses (lower FOV) = compression = more cinematic.
            // Heavier damping on the wide crane so it drifts, not darts. CLOSE
            // cameras (CM_01 gallop-past, CM_03 low chase) get a little tension
            // shake to sell the speed/danger; the wider CM_02/CM_04 stay steady.
            if (n.Contains("CM_02")) { MakeFollowCam(cam, horse, Cam02Offset, 38f, new Vector3(0.7f, 0.7f, 1.0f)); count++; }
            else if (n.Contains("CM_03")) { MakeFollowCam(cam, horse, Cam03Offset, 40f, new Vector3(0.6f, 0.5f, 0.9f)); AddTensionNoise(cam.gameObject, 0.5f, 0.4f); count++; }
            else if (n.Contains("CM_04")) { MakeFollowCam(cam, horse, Cam04Offset, 46f, new Vector3(1.6f, 1.4f, 2.0f)); AddCraneReveal(cam, Cam04Offset); count++; }
            else if (n.Contains("CM_01")) { MakeStaticGallopPast(cam, road); AddTensionNoise(cam.gameObject, 0.6f, 0.5f); count++; }
        }
        return count;
    }

    // Follow the horse at a fixed offset + aim at it, KCD2-style: the camera is
    // LOCKED to the horse's heading (so it stays a steady chase, not a swinging
    // orbit) with smooth position + aim DAMPING so it glides instead of snapping,
    // and NO handheld noise (that "disco" shake is what made it feel frantic).
    private static void MakeFollowCam(CinemachineCamera cam, Transform horse, Vector3 offset,
                                      float fov, Vector3 posDamping)
    {
        Undo.RecordObject(cam, "make follow cam");
        var t = cam.Target; t.TrackingTarget = horse; cam.Target = t;

        RemoveIfPresent<CinemachineSplineDolly>(cam.gameObject);
        RemoveIfPresent<CinemachineOrbitalFollow>(cam.gameObject);
        KillHandheldNoise(cam.gameObject);   // steady, realistic — no shake

        var follow = cam.GetComponent<CinemachineFollow>();
        if (follow == null) follow = Undo.AddComponent<CinemachineFollow>(cam.gameObject);
        follow.FollowOffset = offset;
        // Leave BindingMode at its default (LockToTargetWithWorldUp) — a steady
        // chase relative to the horse's heading — and just add damping so the
        // camera glides instead of snapping. (Both damping fields are Vector3.)
        var ts = follow.TrackerSettings;
        ts.PositionDamping = posDamping;      // smooth glide instead of a rigid lock
        ts.RotationDamping = new Vector3(0.6f, 0.6f, 0.6f);
        follow.TrackerSettings = ts;

        var composer = cam.GetComponent<CinemachineRotationComposer>();
        if (composer == null) composer = Undo.AddComponent<CinemachineRotationComposer>(cam.gameObject);
        composer.Damping = new Vector2(0.55f, 0.55f);   // aim lags gently, no whip

        cam.Lens.FieldOfView = fov;
        EditorUtility.SetDirty(cam);
        if (follow != null) EditorUtility.SetDirty(follow);
        if (composer != null) EditorUtility.SetDirty(composer);
    }

    private static void KillHandheldNoise(GameObject go)
    {
        RemoveIfPresent<CinemachineBasicMultiChannelPerlin>(go);
    }

    // A little handheld tension shake for the CLOSE shots — enough to convey the
    // speed and danger of the ride without the frantic "disco" wobble. Idempotent
    // (removes any existing noise first so re-running doesn't stack it).
    private static void AddTensionNoise(GameObject go, float amp, float freq)
    {
        RemoveIfPresent<CinemachineBasicMultiChannelPerlin>(go);
        var noise = Undo.AddComponent<CinemachineBasicMultiChannelPerlin>(go);
        noise.AmplitudeGain = amp;
        noise.FrequencyGain = freq;
        var prof = FindNoiseProfile();
        if (prof != null) noise.NoiseProfile = prof;
        EditorUtility.SetDirty(noise);
    }

    private static NoiseSettings FindNoiseProfile()
    {
        string[] guids = AssetDatabase.FindAssets("t:NoiseSettings Handheld");
        if (guids == null || guids.Length == 0) guids = AssetDatabase.FindAssets("t:NoiseSettings");
        if (guids != null && guids.Length > 0)
            return AssetDatabase.LoadAssetAtPath<NoiseSettings>(AssetDatabase.GUIDToAssetPath(guids[0]));
        return null;
    }

    // Attach the "rise over the valley" crane to the final follow cam. It lerps
    // the follow offset from the chase pose up to a high wide vista, timed to the
    // real CM_04 clip on the Timeline so it fires exactly when the shot cuts in.
    private static void AddCraneReveal(CinemachineCamera cam, Vector3 startOffset)
    {
        var crane = cam.GetComponent<TrailerCraneReveal>();
        if (crane == null) crane = Undo.AddComponent<TrailerCraneReveal>(cam.gameObject);
        Undo.RecordObject(crane, "config crane");
        crane.startOffset = startOffset;
        crane.endOffset = new Vector3(0f, 26f, -42f);
        if (GetShotTiming("CM_04", out float start, out float dur)) { crane.startDelay = start; crane.duration = dur; }
        EditorUtility.SetDirty(crane);
    }

    // Add the trailer soundscape (wind/rain beds, raven, distant thunder) on the rig.
    private static void AddTrailerAmbience()
    {
        var rig = GameObject.Find(RigName) ??
                  Object.FindObjectsByType<PlayableDirector>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                        .Select(d => d.gameObject).FirstOrDefault(g => g.name == RigName);
        if (rig == null) return;
        if (rig.GetComponent<TrailerAmbience>() == null) Undo.AddComponent<TrailerAmbience>(rig);
    }

    // Add a Timeline-synced slow-mo punch on the hoof-strike shot (CM_03).
    private static void AddSlowMoBeats()
    {
        var rig = GameObject.Find(RigName) ??
                  Object.FindObjectsByType<PlayableDirector>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                        .Select(d => d.gameObject).FirstOrDefault(g => g.name == RigName);
        var dir = rig != null ? rig.GetComponent<PlayableDirector>() : null;
        if (dir == null) return;

        var ramp = rig.GetComponent<TrailerTimeRamp>();
        if (ramp == null) ramp = Undo.AddComponent<TrailerTimeRamp>(rig);
        Undo.RecordObject(ramp, "config slow-mo");
        ramp.director = dir;

        // Punch just after the hoof shot cuts in.
        float at = 15f;
        if (GetShotTiming("CM_03", out float s, out float d)) at = s + Mathf.Min(0.6f, d * 0.25f);
        ramp.pulses = new[]
        {
            new TrailerTimeRamp.Pulse { atTime = at, rampIn = 0.25f, hold = 0.35f, rampOut = 0.6f, minScale = 0.45f },
        };
        EditorUtility.SetDirty(ramp);
    }

    // Read a shot's start time + duration off the Timeline by its clip name, so
    // the crane fires in sync with the Cinemachine cut (the director plays on
    // awake at t=0, same as the horse ride).
    private static bool GetShotTiming(string camFragment, out float start, out float duration)
    {
        start = 15f; duration = 5f;   // sensible fallback if the clip isn't found
        var rig = GameObject.Find(RigName) ??
                  Object.FindObjectsByType<PlayableDirector>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                        .Select(d => d.gameObject).FirstOrDefault(g => g.name == RigName);
        var dir = rig != null ? rig.GetComponent<PlayableDirector>() : null;
        var ta = dir != null ? dir.playableAsset as TimelineAsset : null;
        if (ta == null) return false;
        foreach (var track in ta.GetOutputTracks())
            foreach (var clip in track.GetClips())
                if (!string.IsNullOrEmpty(clip.displayName) && clip.displayName.Contains(camFragment))
                {
                    start = (float)clip.start;
                    duration = (float)clip.duration;
                    return true;
                }
        return false;
    }

    // Static camera sitting low ON the road early along the spline, facing the
    // direction of travel, so the horse gallops from behind, over the camera,
    // and away down the road.
    private static void MakeStaticGallopPast(CinemachineCamera cam, SplineContainer road)
    {
        Undo.RecordObject(cam.transform, "place CM_01");
        RemoveIfPresent<CinemachineSplineDolly>(cam.gameObject);
        RemoveIfPresent<CinemachineOrbitalFollow>(cam.gameObject);
        RemoveIfPresent<CinemachineFollow>(cam.gameObject);
        RemoveIfPresent<CinemachineRotationComposer>(cam.gameObject);   // static aim
        KillHandheldNoise(cam.gameObject);                             // rock-steady
        var tt = cam.Target; tt.TrackingTarget = null; cam.Target = tt;

        float3 p = road.EvaluatePosition(0.14f);
        float3 tan = road.EvaluateTangent(0.14f);
        Vector3 dir = new Vector3(tan.x, 0f, tan.z);
        // Sit ~1.3 m up (a low, dramatic angle that still clears the road surface;
        // +0.6 was effectively buried) and aim slightly up the road toward chest
        // height so the horse gallops into frame and over the lens.
        cam.transform.position = new Vector3(p.x, p.y + 1.3f, p.z);
        if (dir.sqrMagnitude > 0.0001f)
            cam.transform.rotation = Quaternion.LookRotation((dir.normalized + Vector3.up * 0.08f).normalized);
        cam.Lens.FieldOfView = 38f;
        EditorUtility.SetDirty(cam);
    }

    // --- helpers ---

    private static void RemoveIfPresent<T>(GameObject go) where T : Component
    {
        var c = go.GetComponent<T>();
        if (c != null) Undo.DestroyObjectImmediate(c);
    }

    private static Transform FindByNameContains(string needle)
    {
        return Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None)
            .FirstOrDefault(t => t.name.Replace("_", "").ToLowerInvariant().Contains(needle));
    }

    private static SplineContainer FindRoadSpline()
    {
        var splines = Object.FindObjectsByType<SplineContainer>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        if (splines == null || splines.Length == 0) return null;
        var road = splines.FirstOrDefault(s => s.name.ToLowerInvariant().Contains("road"));
        if (road != null) return road;
        var nonCam = splines.FirstOrDefault(s =>
        {
            string x = s.name.ToLowerInvariant();
            return !x.Contains("paralel") && !x.Contains("parallel") && !x.Contains("cam");
        });
        return nonCam ?? splines[0];
    }

    private static void EditorSceneMarkDirty()
    {
        var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(scene);
    }

    private static void Warn(string msg) => EditorUtility.DisplayDialog("Act I Road Ride", msg, "OK");
}
