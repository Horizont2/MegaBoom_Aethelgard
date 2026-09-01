using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

// One-click generator for per-biome tree prefab ASSETS. Terrain trees must be
// real prefab assets (a runtime material-copy does NOT render), so we bake the
// biome look into actual prefab variants here, then auto-assign them to the
// WorldGenerator's Base Trees Autumn / Winter arrays.
//
// Run it from:  Tools ▸ Generate Biome Tree Prefabs
// It reads the WorldGenerator in the open scene: its Base Trees + the
// Base Tree Autumn/Winter materials, and writes variants to
// Assets/GeneratedBiomeTrees/.
public static class BiomeTreePrefabGenerator
{
    private const string OutFolder = "Assets/GeneratedBiomeTrees";

    // Renderers/materials whose name contains any of these are TRUNK/wood and are
    // left untouched — only the leaf/foliage materials get the biome swap.
    private static readonly string[] TrunkTerms = { "trunk", "wood", "bark", "stem", "branch", "log" };

    [MenuItem("Tools/Generate Biome Tree Prefabs")]
    public static void Generate()
    {
        var wg = Object.FindFirstObjectByType<WorldGenerator>();
        if (wg == null)
        {
            EditorUtility.DisplayDialog("Biome Vegetation Prefabs",
                "No WorldGenerator found in the open scene. Open the region generation scene (GameScene) and try again.", "OK");
            return;
        }

        // Trees and bushes are handled independently — you can generate biome
        // BUSH variants even if trees aren't set up for painting (and vice versa).
        bool canTrees = wg.baseTrees != null && wg.baseTrees.Length > 0
                        && (wg.baseTreeAutumnMaterial != null || wg.baseTreeWinterMaterial != null);
        bool canBushes = wg.baseBushes != null && wg.baseBushes.Length > 0
                         && (wg.bushAutumnMaterial != null || wg.bushWinterMaterial != null);

        if (!canTrees && !canBushes)
        {
            EditorUtility.DisplayDialog("Biome Vegetation Prefabs",
                "Nothing to generate. Assign, on the WorldGenerator:\n" +
                "  • Base Trees + Base Tree Autumn/Winter materials (for tree variants), and/or\n" +
                "  • Base Bushes + Bush Autumn/Winter materials (for bush variants).", "OK");
            return;
        }

        if (!AssetDatabase.IsValidFolder(OutFolder))
            AssetDatabase.CreateFolder("Assets", "GeneratedBiomeTrees");

        var tAutumn = new List<GameObject>();
        var tWinter = new List<GameObject>();
        var bAutumn = new List<GameObject>();
        var bWinter = new List<GameObject>();

        Undo.RecordObject(wg, "Assign Biome Vegetation Prefabs");

        if (canTrees)
        {
            tAutumn = BuildVariants(wg.baseTrees, wg.baseTreeAutumnMaterial, "_Autumn");
            tWinter = BuildVariants(wg.baseTrees, wg.baseTreeWinterMaterial, "_Winter");
            if (tAutumn.Count > 0) wg.baseTreesAutumn = tAutumn.ToArray();
            if (tWinter.Count > 0) wg.baseTreesWinter = tWinter.ToArray();
        }

        if (canBushes)
        {
            bAutumn = BuildVariants(wg.baseBushes, wg.bushAutumnMaterial, "_Autumn");
            bWinter = BuildVariants(wg.baseBushes, wg.bushWinterMaterial, "_Winter");
            if (bAutumn.Count > 0) wg.baseBushesAutumn = bAutumn.ToArray();
            if (bWinter.Count > 0) wg.baseBushesWinter = bWinter.ToArray();
        }

        EditorUtility.SetDirty(wg);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        EditorUtility.DisplayDialog("Biome Vegetation Prefabs",
            $"Done.\nTrees  — Autumn: {tAutumn.Count}, Winter: {tWinter.Count}\nBushes — Autumn: {bAutumn.Count}, Winter: {bWinter.Count}\n\nSaved to {OutFolder} and assigned to the WorldGenerator.\nSAVE THE SCENE to keep the assignment.", "OK");
    }

    private static List<GameObject> BuildVariants(GameObject[] sources, Material biomeMat, string suffix)
    {
        var list = new List<GameObject>();
        if (sources == null || biomeMat == null) return list;
        foreach (var src in sources)
        {
            if (src == null) continue;
            var v = MakeVariant(src, biomeMat, suffix);
            if (v != null) list.Add(v);
        }
        return list;
    }

    private static GameObject MakeVariant(GameObject src, Material foliageMat, string suffix)
    {
        var inst = (GameObject)PrefabUtility.InstantiatePrefab(src);
        if (inst == null) return null;
        PrefabUtility.UnpackPrefabInstance(inst, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);

        foreach (var rnd in inst.GetComponentsInChildren<Renderer>(true))
        {
            if (rnd == null || rnd is ParticleSystemRenderer) continue;
            if (NameIsTrunk(rnd.gameObject.name)) continue;

            var mats = rnd.sharedMaterials;
            bool changed = false;
            for (int i = 0; i < mats.Length; i++)
            {
                if (mats[i] == null) continue;
                if (NameIsTrunk(mats[i].name)) continue;   // keep bark/trunk slots
                mats[i] = foliageMat;
                changed = true;
            }
            if (changed) rnd.sharedMaterials = mats;
        }

        string path = AssetDatabase.GenerateUniqueAssetPath($"{OutFolder}/{src.name}{suffix}.prefab");
        GameObject asset = PrefabUtility.SaveAsPrefabAsset(inst, path);
        Object.DestroyImmediate(inst);
        return asset;
    }

    private static bool NameIsTrunk(string n)
    {
        if (string.IsNullOrEmpty(n)) return false;
        n = n.ToLowerInvariant();
        for (int i = 0; i < TrunkTerms.Length; i++)
            if (n.Contains(TrunkTerms[i])) return true;
        return false;
    }
}
