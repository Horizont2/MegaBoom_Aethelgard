using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Playables;
using Unity.Cinemachine;

// Act II — the ride CONTINUES and the world cycles Summer -> Autumn -> Winter
// around the galloping horse (showcasing the game's dynamic seasons), then he
// rides on into the winter mist toward the threat.
//
//   Tools ▸ Lore Trailer ▸ Setup Act II Seasons Ride
//
// Run it AFTER "Setup Act I Road Ride" (it reuses that rig + horse). It:
//   * lengthens the ride so the journey feels like a passage of time,
//   * drives a full season change across the ride (vegetation tint, terrain
//     ground texture, sun + fog, falling leaves then snow) via TrailerSeasonRide,
//   * adds a long chase camera so the extended ride stays on screen after the
//     Act I shots finish.
public static class ActIISeasonsRideSetup
{
    private const string RigName = "LoreTrailer_Rig";
    private const string TerrainMat = "Assets/RPGPP_LT/Materials/rpgpp_lt_mat_a.mat";
    private const string TexSummer = "Assets/RPGPP_LT/Textures/rpgpp_lt_tex_a.tga";
    private const string TexAutumn = "Assets/RPGPP_LT/Textures/rpgpp_lt_tex_a.tga_3_Autumn.png";
    private const string TexWinter = "Assets/RPGPP_LT/Textures/rpgpp_lt_tex_a.tga_5_Winter.png";
    private const string LeavesPrefab = "Assets/VFX Brady Games/Particle Effect/Falling Leaves.prefab";
    private const string SnowPrefab = "Assets/VFX Brady Games/Particle Effect/Snowfall.prefab";
    private const float RideSeconds = 34f;

    [MenuItem("Tools/Lore Trailer/Setup Act II Seasons Ride")]
    public static void Setup()
    {
        var ride = Object.FindObjectsByType<TrailerHorseRide>(FindObjectsInactive.Include, FindObjectsSortMode.None).FirstOrDefault();
        if (ride == null)
        {
            EditorUtility.DisplayDialog("Act II Seasons Ride",
                "No TrailerHorseRide in the scene — run 'Setup Act I Road Ride' first, then this.", "OK");
            return;
        }
        var horse = ride.transform;

        Undo.SetCurrentGroupName("Setup Act II Seasons Ride");

        // 1) Lengthen the journey so the seasons have room to breathe.
        Undo.RecordObject(ride, "extend ride");
        ride.autoFitSeconds = RideSeconds;
        EditorUtility.SetDirty(ride);

        // 2) Season driver on the rig.
        var rig = FindRig();
        if (rig == null)
        {
            EditorUtility.DisplayDialog("Act II Seasons Ride",
                "No '" + RigName + "' found — run 'Setup Act I Road Ride' first.", "OK");
            return;
        }

        var season = rig.GetComponent<TrailerSeasonRide>();
        if (season == null) season = Undo.AddComponent<TrailerSeasonRide>(rig);
        Undo.RecordObject(season, "config seasons");
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

        // 3) Long chase camera so the extended ride stays framed after Act I ends.
        int chase = EnsureChaseCam(rig, horse) ? 1 : 0;

        MarkDirty();

        EditorUtility.DisplayDialog("Act II Seasons Ride",
            "Wired up:\n" +
            $"  • Ride lengthened to {RideSeconds:0}s (the long journey).\n" +
            $"  • Full season change across the ride: vegetation tint (_SeasonColor), terrain ground texture (summer/autumn/winter), sun + fog, falling leaves then snow.\n" +
            $"  • Terrain material: {(season.terrainMaterial != null ? "OK" : "NOT FOUND")}; sun light: {(season.sun != null ? "OK" : "NOT FOUND — assign TrailerSeasonRide.sun")}.\n" +
            $"  • Chase camera: {(chase == 1 ? "added (covers the extended ride)" : "already present")}.\n\n" +
            "PRESS PLAY: the horse gallops on while Summer→Autumn→Winter sweep across the world; leaves blow past, then snow, and he rides into the winter mist.\n" +
            "Tune season timing on TrailerSeasonRide; chase framing on CM_SeasonChase.", "OK");
    }

    private static GameObject FindRig()
    {
        return GameObject.Find(RigName) ??
               Object.FindObjectsByType<PlayableDirector>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                     .Select(d => d.gameObject).FirstOrDefault(g => g.name == RigName);
    }

    private static Light FindSun()
    {
        // Prefer the DayNightCycle's assigned sun; else the first directional light.
        var dnc = Object.FindObjectsByType<DayNightCycle>(FindObjectsInactive.Include, FindObjectsSortMode.None).FirstOrDefault();
        if (dnc != null && dnc.sunLight != null) return dnc.sunLight;
        return Object.FindObjectsByType<Light>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                     .FirstOrDefault(l => l.type == LightType.Directional);
    }

    private static bool EnsureChaseCam(GameObject rig, Transform horse)
    {
        var existing = rig.GetComponentsInChildren<CinemachineCamera>(true).FirstOrDefault(c => c.name == "CM_SeasonChase");
        if (existing != null) return false;

        var go = new GameObject("CM_SeasonChase");
        Undo.RegisterCreatedObjectUndo(go, "chase cam");
        go.transform.SetParent(rig.transform, false);

        var cam = go.AddComponent<CinemachineCamera>();
        cam.Lens.FieldOfView = 46f;
        var tgt = cam.Target; tgt.TrackingTarget = horse; cam.Target = tgt;
        // Win the brain once the Act I timeline shots finish.
        var pr = cam.Priority; pr.Value = 100; cam.Priority = pr;

        var follow = go.AddComponent<CinemachineFollow>();
        follow.FollowOffset = new Vector3(2.5f, 4.5f, -9f);   // behind + above, slightly to the side
        var ts = follow.TrackerSettings;
        ts.PositionDamping = new Vector3(1.0f, 1.0f, 1.4f);
        ts.RotationDamping = new Vector3(0.8f, 0.8f, 0.8f);
        follow.TrackerSettings = ts;

        var composer = go.AddComponent<CinemachineRotationComposer>();
        composer.Damping = new Vector2(0.6f, 0.6f);
        return true;
    }

    private static void MarkDirty()
    {
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(
            UnityEngine.SceneManagement.SceneManager.GetActiveScene());
    }
}
