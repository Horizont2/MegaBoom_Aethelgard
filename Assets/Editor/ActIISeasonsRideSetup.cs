using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Splines;
using Unity.Cinemachine;

// Act II — the ride CONTINUES and the world cycles Summer -> Autumn -> Winter
// around the galloping horse, then he rides into the winter mist.
//
//   Tools ▸ Lore Trailer ▸ Setup Act II Seasons Ride
//
// This is SELF-CONTAINED: it assigns the horse's route, sets up follow cameras
// that track the rider through the whole journey with cinematic cuts, and drives
// the season change. (Run Setup Act I first only if you also want its opening
// gallop-past shots; Act II no longer depends on it.)
public static class ActIISeasonsRideSetup
{
    private const string RigName = "LoreTrailer_ActII_Rig";   // Act II's OWN rig (leaves Act I intact)
    private const string ActIRigName = "LoreTrailer_Rig";
    private const string TerrainMat = "Assets/RPGPP_LT/Materials/rpgpp_lt_mat_a.mat";
    private const string TexSummer = "Assets/RPGPP_LT/Textures/rpgpp_lt_tex_a.tga";
    private const string TexAutumn = "Assets/RPGPP_LT/Textures/rpgpp_lt_tex_a.tga_3_Autumn.png";
    private const string TexWinter = "Assets/RPGPP_LT/Textures/rpgpp_lt_tex_a.tga_5_Winter.png";
    private const string LeavesPrefab = "Assets/VFX Brady Games/Particle Effect/Falling Leaves.prefab";
    private const string SnowPrefab = "Assets/VFX Brady Games/Particle Effect/Snowfall.prefab";
    private const float RideSeconds = 34f;

    // Follow offsets for the three journey angles (relative to the horse heading).
    private static readonly Vector3 CamFrontOffset = new Vector3(2.2f, 1.6f, 6.5f);  // ahead, looking back at the approaching rider
    private static readonly Vector3 CamSideOffset = new Vector3(6f, 2.4f, 0f);        // tracking alongside
    private static readonly Vector3 CamCraneOffset = new Vector3(0f, 6.5f, -12f);     // behind + high

    [MenuItem("Tools/Lore Trailer/Setup Act II Seasons Ride")]
    public static void Setup()
    {
        var ride = Object.FindObjectsByType<TrailerHorseRide>(FindObjectsInactive.Include, FindObjectsSortMode.None).FirstOrDefault();
        if (ride == null)
        {
            EditorUtility.DisplayDialog("Act II Seasons Ride",
                "No TrailerHorseRide on the horse.\nRun 'Setup Act I Road Ride' once (it puts the ride + rider on the horse), then run this.", "OK");
            return;
        }
        var horse = ride.transform;

        Undo.SetCurrentGroupName("Setup Act II Seasons Ride");

        // 1) ROUTE — make sure the horse has a spline to ride, and configure the ride.
        SplineContainer road = ride.path != null ? ride.path : FindRoadSpline();
        if (road == null)
        {
            EditorUtility.DisplayDialog("Act II Seasons Ride",
                "No route Spline found. Draw a Spline along the road (GameObject ▸ Spline, name it with 'road') and run again.", "OK");
            return;
        }
        Undo.RecordObject(ride, "config ride");
        ride.path = road;
        // Constant, sensible gallop speed so the horse rides the WHOLE route you
        // draw at a good pace (no more "too slow", no straight-line overrun).
        ride.autoFitSeconds = 0f;
        ride.speed = 14f;
        ride.playOnStart = true;
        ride.driveFromTimeline = false;
        ride.faceAlongPath = true;
        ride.loop = false;
        ride.groundSnapOverrun = true;
        EditorUtility.SetDirty(ride);

        // 2) RIG — Act II gets its OWN rig so it never breaks Act I. Disable the
        //    Act I rig while Act II is set up (re-run Setup Act I to switch back).
        var actI = GameObject.Find(ActIRigName);
        if (actI != null && actI.activeSelf) { Undo.RecordObject(actI, "disable Act I rig"); actI.SetActive(false); }

        var rig = FindRig();
        if (rig == null)
        {
            rig = new GameObject(RigName);
            Undo.RegisterCreatedObjectUndo(rig, "create rig");
        }
        if (!rig.activeSelf) { Undo.RecordObject(rig, "enable rig"); rig.SetActive(true); }

        // 3) FOLLOW CAMERAS — three angles that track the rider.
        var camFront = MakeFollowCam(rig, "CM_ActII_Front", horse, CamFrontOffset, 40f);
        var camSide = MakeFollowCam(rig, "CM_ActII_Side", horse, CamSideOffset, 42f);
        var camCrane = MakeFollowCam(rig, "CM_ActII_Crane", horse, CamCraneOffset, 46f);

        // 4) CUTS across the journey.
        var cutter = rig.GetComponent<TrailerCameraCutter>();
        if (cutter == null) cutter = Undo.AddComponent<TrailerCameraCutter>(rig);
        Undo.RecordObject(cutter, "config cuts");
        cutter.cameras = new[] { camSide, camFront, camCrane };
        cutter.useProgress = true;
        cutter.ride = ride;
        cutter.cutProgress = new[] { 0f, 0.4f, 0.72f };   // cut along the route
        EditorUtility.SetDirty(cutter);

        // 5) SEASON change across the ride.
        var season = rig.GetComponent<TrailerSeasonRide>();
        if (season == null) season = Undo.AddComponent<TrailerSeasonRide>(rig);
        Undo.RecordObject(season, "config seasons");
        season.driveByRideProgress = true;   // seasons follow the route you drew
        season.ride = ride;
        season.seasonDuration = RideSeconds;
        season.terrainMaterial = AssetDatabase.LoadAssetAtPath<Material>(TerrainMat);
        season.summerTexture = AssetDatabase.LoadAssetAtPath<Texture>(TexSummer);
        season.autumnTexture = AssetDatabase.LoadAssetAtPath<Texture>(TexAutumn);
        season.winterTexture = AssetDatabase.LoadAssetAtPath<Texture>(TexWinter);
        season.leavesPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(LeavesPrefab);
        season.snowPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(SnowPrefab);
        season.sun = FindSun();
        season.cam = Camera.main;
        EditorUtility.SetDirty(season);

        MarkDirty();

        EditorUtility.DisplayDialog("Act II Seasons Ride",
            "Wired up (self-contained, Act I left intact on its own rig):\n" +
            $"  • Route: horse rides the WHOLE spline '{road.name}' at a steady gallop (draw a longer spline for a longer journey).\n" +
            "  • 3 follow cameras track the rider (side → front → crane), cutting along the ROUTE progress.\n" +
            "  • Seasons follow the ROUTE: summer at the start of the spline → winter at the end; leaves then snow.\n" +
            $"  • Terrain material {(season.terrainMaterial != null ? "OK" : "MISSING")}; sun {(season.sun != null ? "OK" : "NOT FOUND")}.\n\n" +
            "NOTE: draw/extend the road Spline to shape the journey. To switch back to Act I, run 'Setup Act I Road Ride'.\n" +
            "⚠ Unity-Terrain ground + painted grass won't recolour yet (no shader reads the season tint) — that needs a dedicated terrain-season pass; see chat.", "OK");
    }

    private static CinemachineCamera MakeFollowCam(GameObject rig, string name, Transform horse, Vector3 offset, float fov)
    {
        var existing = rig.GetComponentsInChildren<CinemachineCamera>(true).FirstOrDefault(c => c.name == name);
        GameObject go;
        if (existing != null) go = existing.gameObject;
        else
        {
            go = new GameObject(name);
            Undo.RegisterCreatedObjectUndo(go, "cam");
            go.transform.SetParent(rig.transform, false);
        }

        var cam = go.GetComponent<CinemachineCamera>();
        if (cam == null) cam = go.AddComponent<CinemachineCamera>();
        cam.Lens.FieldOfView = fov;
        var tgt = cam.Target; tgt.TrackingTarget = horse; cam.Target = tgt;

        var follow = go.GetComponent<CinemachineFollow>();
        if (follow == null) follow = go.AddComponent<CinemachineFollow>();
        follow.FollowOffset = offset;
        var ts = follow.TrackerSettings;
        ts.PositionDamping = new Vector3(0.9f, 0.9f, 1.2f);
        ts.RotationDamping = new Vector3(0.7f, 0.7f, 0.7f);
        follow.TrackerSettings = ts;

        var composer = go.GetComponent<CinemachineRotationComposer>();
        if (composer == null) composer = go.AddComponent<CinemachineRotationComposer>();
        composer.Damping = new Vector2(0.55f, 0.55f);

        return cam;
    }

    private static GameObject FindRig()
    {
        return GameObject.Find(RigName) ??
               Object.FindObjectsByType<PlayableDirector>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                     .Select(d => d.gameObject).FirstOrDefault(g => g.name == RigName);
    }

    private static SplineContainer FindRoadSpline()
    {
        var splines = Object.FindObjectsByType<SplineContainer>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        if (splines == null || splines.Length == 0) return null;
        var road = splines.FirstOrDefault(s => s.name.ToLowerInvariant().Contains("road"));
        if (road != null) return road;
        return splines.FirstOrDefault(s =>
        {
            string x = s.name.ToLowerInvariant();
            return !x.Contains("cam") && !x.Contains("paralel") && !x.Contains("parallel");
        }) ?? splines[0];
    }

    private static Light FindSun()
    {
        var dnc = Object.FindObjectsByType<DayNightCycle>(FindObjectsInactive.Include, FindObjectsSortMode.None).FirstOrDefault();
        if (dnc != null && dnc.sunLight != null) return dnc.sunLight;
        return Object.FindObjectsByType<Light>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                     .FirstOrDefault(l => l.type == LightType.Directional);
    }

    private static void MarkDirty()
    {
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(
            UnityEngine.SceneManagement.SceneManager.GetActiveScene());
    }
}
