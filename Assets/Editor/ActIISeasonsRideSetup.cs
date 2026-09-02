using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Splines;

// Act II = the SAME ride as Act I, but the world changes season as the horse
// travels it. This does NOT create its own cameras or rig — it ADDS the season
// driver onto the existing Act I rig so Act I's shots are untouched (the opening
// gallop-past stays, and the end crane that rises IS the reveal of the changed
// world). Seasons follow the ROUTE progress: summer at the first knot, winter at
// the last.
//
//   Tools ▸ Lore Trailer ▸ Setup Act II Seasons (adds to Act I)
public static class ActIISeasonsRideSetup
{
    private const string RigName = "LoreTrailer_Rig";
    private const string TerrainMat = "Assets/RPGPP_LT/Materials/rpgpp_lt_mat_a.mat";
    private const string TexSummer = "Assets/RPGPP_LT/Textures/rpgpp_lt_tex_a.tga";
    private const string TexAutumn = "Assets/RPGPP_LT/Textures/rpgpp_lt_tex_a.tga_3_Autumn.png";
    private const string TexWinter = "Assets/RPGPP_LT/Textures/rpgpp_lt_tex_a.tga_5_Winter.png";
    private const string LeavesPrefab = "Assets/VFX Brady Games/Particle Effect/Falling Leaves.prefab";
    private const string SnowPrefab = "Assets/VFX Brady Games/Particle Effect/Snowfall.prefab";

    [MenuItem("Tools/Lore Trailer/Setup Act II Seasons (adds to Act I)")]
    public static void Setup()
    {
        var ride = Object.FindObjectsByType<TrailerHorseRide>(FindObjectsInactive.Include, FindObjectsSortMode.None).FirstOrDefault();
        if (ride == null)
        {
            EditorUtility.DisplayDialog("Act II Seasons",
                "No TrailerHorseRide found. Run 'Setup Act I Road Ride' first, then this.", "OK");
            return;
        }

        Undo.SetCurrentGroupName("Setup Act II Seasons");

        // Clean up any rig/cameras a PREVIOUS (broken) Act II version created.
        var oldRig = GameObject.Find("LoreTrailer_ActII_Rig");
        if (oldRig != null) Undo.DestroyObjectImmediate(oldRig);

        // Keep Act I's ride speed (don't fight it) — just make sure it isn't the
        // leftover fast/slow value from the old Act II. 24s = the Act I default.
        Undo.RecordObject(ride, "ride speed");
        if (ride.autoFitSeconds <= 0.01f) ride.autoFitSeconds = 24f;
        EditorUtility.SetDirty(ride);

        var rig = FindRig();
        if (rig == null)
        {
            EditorUtility.DisplayDialog("Act II Seasons",
                "No '" + RigName + "' found. Run 'Setup Act I Road Ride' first.", "OK");
            return;
        }

        // Season driver on the Act I rig — driven by ROUTE progress.
        var season = rig.GetComponent<TrailerSeasonRide>();
        if (season == null) season = Undo.AddComponent<TrailerSeasonRide>(rig);
        Undo.RecordObject(season, "config seasons");
        season.driveByRideProgress = true;
        season.ride = ride;
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

        EditorUtility.DisplayDialog("Act II Seasons",
            "Added seasons to the Act I ride (Act I cameras untouched):\n" +
            "  • Summer → Autumn → Winter follow the ROUTE (summer at the start of the spline, winter at the end).\n" +
            "  • Falling leaves (autumn) then snow (winter), sun + fog shift; terrain/veg tint where a shader supports it.\n" +
            "  • The Act I end-crane (CM_04) rises over the now-changed world — that's the reveal.\n\n" +
            $"  • Terrain material {(season.terrainMaterial != null ? "OK" : "MISSING")}, sun {(season.sun != null ? "OK" : "NOT FOUND")}.\n" +
            "⚠ Unity-Terrain ground + painted grass need the dedicated terrain-season pass to recolour (coming next).", "OK");
    }

    private static GameObject FindRig()
    {
        return GameObject.Find(RigName) ??
               Object.FindObjectsByType<PlayableDirector>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                     .Select(d => d.gameObject).FirstOrDefault(g => g.name == RigName);
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
