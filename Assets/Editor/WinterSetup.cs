using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Wiring and diagnostics for the winter region.
//
// The winter work is nearly all runtime code, but two things cannot be done
// from a script at runtime: pointing the sky at a material that lives outside
// Resources, and finding out what is actually in the terrain's detail
// prototype list. Both are one menu click here.
public static class WinterSetup
{
    // The scene already uses the FS002 family for day, night and storm, so the
    // winter set stays in it — mixing panorama families gives two different
    // horizons and two different cloud styles depending on the weather.
    private const string SkyFolder = "Assets/Fantasy Skybox FREE/Panoramics/FS002";
    private const string WinterDay = SkyFolder + "/FS002_Snowy.mat";
    private const string WinterNight = SkyFolder + "/FS002_Night.mat";
    private const string WinterStorm = SkyFolder + "/FS002_Rainy.mat";

    [MenuItem("Tools/World/Wire Winter Sky")]
    public static void WireWinterSky()
    {
        var cycles = Object.FindObjectsByType<DayNightCycle>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        if (cycles == null || cycles.Length == 0)
        {
            Debug.LogError("[Winter] No DayNightCycle in the open scene. Open GameScene (or whichever scene the region runs in) and try again.");
            return;
        }

        var day = AssetDatabase.LoadAssetAtPath<Material>(WinterDay);
        var night = AssetDatabase.LoadAssetAtPath<Material>(WinterNight);
        var storm = AssetDatabase.LoadAssetAtPath<Material>(WinterStorm);

        if (day == null)
        {
            Debug.LogError($"[Winter] {WinterDay} not found. If Fantasy Skybox FREE moved, assign the three winter sky " +
                           "slots on DayNightCycle by hand — they fall back to the ordinary sky while empty.");
            return;
        }

        foreach (var c in cycles)
        {
            Undo.RecordObject(c, "Wire winter sky");
            c.winterDaySkybox = day;
            // A winter night is BRIGHTER than a summer one — snow reflects the
            // moon — so this deliberately takes the sky WITH a moon in it, not
            // the moonless one the ordinary night uses.
            if (night != null) c.winterNightSkybox = night;
            // Heavy overcast. With the snow VFX in front of it this reads as a
            // blizzard; there is no dedicated blizzard panorama in the pack.
            if (storm != null) c.winterStormSkybox = storm;
            EditorUtility.SetDirty(c);
        }

        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        Debug.Log($"[Winter] Winter sky wired on {cycles.Length} DayNightCycle(s). Save the scene to keep it.");
    }

    // ---------------------------------------------------------------------

    // Prints what the terrain's detail (grass) layers actually are.
    //
    // The generator hard-codes layer 0 = forest, 1 = desert, 2 = snow and skips
    // everything above 2 in a winter region, so winter gets exactly ONE grass
    // type while forest gets its own plus every extra layer. Whether that can be
    // fixed by three lines in the generator or needs new prototypes appended
    // depends entirely on what is in this list — and TerrainData is a binary
    // asset, so this is the only way to read it.
    [MenuItem("Tools/World/Report Terrain Detail Layers")]
    public static void ReportDetailLayers()
    {
        var terrains = Object.FindObjectsByType<Terrain>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        if (terrains == null || terrains.Length == 0)
        {
            Debug.LogError("[Winter] No Terrain in the open scene.");
            return;
        }

        var sb = new StringBuilder();
        foreach (var t in terrains)
        {
            var td = t.terrainData;
            if (td == null) continue;

            sb.AppendLine($"=== {t.name} ({AssetDatabase.GetAssetPath(td)})");
            sb.AppendLine($"    detailResolution {td.detailResolution}, scatter {td.detailScatterMode}");

            var protos = td.detailPrototypes;
            sb.AppendLine($"    detail layers: {protos.Length}");
            for (int i = 0; i < protos.Length; i++)
            {
                var p = protos[i];
                string what = p.usePrototypeMesh
                    ? $"mesh '{(p.prototype != null ? p.prototype.name : "NULL")}'"
                    : $"texture '{(p.prototypeTexture != null ? p.prototypeTexture.name : "NULL")}'";
                // The generator's own reading of each index, so the mismatch (if
                // there is one) is obvious at a glance rather than after a hunt.
                string role = i == 0 ? "forest" : i == 1 ? "desert" : i == 2 ? "snow" : "extra (forest only — skipped in winter)";
                sb.AppendLine($"      [{i}] {what}   -> generator treats as: {role}");
            }

            var layers = td.terrainLayers;
            sb.AppendLine($"    terrain (splat) layers: {layers.Length}");
            for (int i = 0; i < layers.Length; i++)
                sb.AppendLine($"      [{i}] {(layers[i] != null ? layers[i].name : "NULL")}");
        }

        Debug.Log("[Winter] Terrain report:\n" + sb);
    }
}
