using System.Linq;
using UnityEditor;
using UnityEngine;
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
    private static readonly Vector3 Cam02Offset = new Vector3(4.5f, 2.3f, -1.5f);  // alongside
    private static readonly Vector3 Cam03Offset = new Vector3(1.8f, 0.6f, -3.2f);  // low, behind-ish
    private static readonly Vector3 Cam04Offset = new Vector3(0f, 6.5f, -9f);       // crane behind+high

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
        ride.autoFitSeconds = 18f;
        ride.playOnStart = true;
        ride.driveFromTimeline = false;
        ride.faceAlongPath = true;
        ride.loop = false;
        if (horse.GetComponent<HorseAudioController>() == null) Undo.AddComponent<HorseAudioController>(horse.gameObject);

        // 3) Park the player on the horse so it stops flying off.
        bool riderOk = ParkPlayerOnHorse(horse);

        // 4) Cameras: follow-with-offset (no spline), + a static shot-1 on the road.
        int cams = ConfigureCameras(horse, road);

        EditorSceneMarkDirty();

        EditorUtility.DisplayDialog("Act I Road Ride",
            $"Wired up:\n" +
            $"  • Disabled {disabled} interfering component(s) (quest/extraction/spawner).\n" +
            $"  • Horse '{horse.name}' auto-rides '{road.name}' over {ride.autoFitSeconds:0}s.\n" +
            $"  • Rider on horse: {(riderOk ? "yes" : "NO player found — place one manually")}.\n" +
            $"  • Configured {cams} camera(s): CM_01 static gallop-past, CM_02/03/04 follow the horse (no spline).\n\n" +
            "PRESS PLAY to preview.\n" +
            "If the horse runs the wrong way → TrailerHorseRide: tick Reverse / set Model Yaw Offset 180 (then rotate CM_01 180° too).\n" +
            "Nudge camera FollowOffset + rider saddle height to taste.", "OK");
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
        var playerGo = GameObject.FindGameObjectWithTag("Player");
        if (playerGo == null) return false;
        Undo.RegisterFullObjectHierarchyUndo(playerGo, "park rider");

        // Kill physics / control so it can't fly off.
        var pc = playerGo.GetComponent("PlayerController") as MonoBehaviour;
        if (pc != null) pc.enabled = false;
        var cc = playerGo.GetComponent<CharacterController>();
        if (cc != null) cc.enabled = false;
        var rb = playerGo.GetComponent<Rigidbody>();
        if (rb != null) { rb.isKinematic = true; rb.linearVelocity = Vector3.zero; }

        playerGo.transform.SetParent(horse, false);
        playerGo.transform.localPosition = RiderSaddleOffset;
        playerGo.transform.localRotation = Quaternion.identity;
        return true;
    }

    // --- cameras ---

    private static int ConfigureCameras(Transform horse, SplineContainer road)
    {
        int count = 0;
        foreach (var cam in Object.FindObjectsByType<CinemachineCamera>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (cam == null) continue;
            string n = cam.name;

            if (n.Contains("CM_02")) { MakeFollowCam(cam, horse, Cam02Offset); count++; }
            else if (n.Contains("CM_03")) { MakeFollowCam(cam, horse, Cam03Offset); count++; }
            else if (n.Contains("CM_04")) { MakeFollowCam(cam, horse, Cam04Offset); count++; }
            else if (n.Contains("CM_01")) { MakeStaticGallopPast(cam, road); count++; }
        }
        return count;
    }

    // Follow the horse at a fixed offset + aim at it. Removes any spline-dolly /
    // orbital body so the camera can't dive through the ground on a floor spline.
    private static void MakeFollowCam(CinemachineCamera cam, Transform horse, Vector3 offset)
    {
        Undo.RecordObject(cam, "make follow cam");
        var t = cam.Target; t.TrackingTarget = horse; cam.Target = t;

        RemoveIfPresent<CinemachineSplineDolly>(cam.gameObject);
        RemoveIfPresent<CinemachineOrbitalFollow>(cam.gameObject);

        var follow = cam.GetComponent<CinemachineFollow>();
        if (follow == null) follow = Undo.AddComponent<CinemachineFollow>(cam.gameObject);
        follow.FollowOffset = offset;

        if (cam.GetComponent<CinemachineRotationComposer>() == null)
            Undo.AddComponent<CinemachineRotationComposer>(cam.gameObject);

        EditorUtility.SetDirty(cam);
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
        var tt = cam.Target; tt.TrackingTarget = null; cam.Target = tt;

        float3 p = road.EvaluatePosition(0.14f);
        float3 tan = road.EvaluateTangent(0.14f);
        Vector3 dir = new Vector3(tan.x, 0f, tan.z);
        cam.transform.position = new Vector3(p.x, p.y + 0.6f, p.z);
        if (dir.sqrMagnitude > 0.0001f) cam.transform.rotation = Quaternion.LookRotation(dir.normalized);
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
