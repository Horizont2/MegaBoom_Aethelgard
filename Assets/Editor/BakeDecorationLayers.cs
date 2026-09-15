using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

// Bakes the Nature layer and shadow-casting settings INTO the vegetation
// prefabs, instead of applying them to every instance at runtime.
//
// ==== WHY THIS EXISTS ====
//
// WorldGenerator.TagAsNature and DisableShadowCasting do this to every spawned
// decoration, every run. Two problems with that.
//
// It is invisible. Open a bush prefab and it says Default, so anybody reasoning
// about layers — a raycast mask, the camera's per-layer cull distance, the
// silhouette's foliage probe — reads the wrong answer from the inspector and
// has no way to know the truth without reading the generator.
//
// And it is work repeated thousands of times per run, at the worst possible
// moment: generation, when the player is already staring at a loading screen.
// GetComponentsInChildren allocates an array per prop.
//
// Baked into the asset, the inspector tells the truth and the generator's pass
// becomes a no-op safety net for anything this missed.
//
// ==== THE RULE IS COPIED EXACTLY, NOT IMPROVED ====
//
// Only renderers WITHOUT a collider are moved to Nature — the same condition
// the runtime tagger uses. A GameObject with no collider has no physics
// behaviour and cannot be hit by a raycast, so its layer affects nothing but
// rendering and culling. Touching a collider's layer would quietly change the
// collision matrix, and that is not a change to make as a side effect of
// tidying up.
public static class BakeDecorationLayers
{
    private const string NatureLayerName = "Nature";

    [MenuItem("Tools/World/Bake Decoration Layers (generator vegetation)")]
    private static void BakeFromGenerator()
    {
        var gen = Object.FindFirstObjectByType<WorldGenerator>(FindObjectsInactive.Include);
        if (gen == null)
        {
            EditorUtility.DisplayDialog("Bake Decoration Layers",
                "No WorldGenerator in the open scene. Open the scene that has one (GameScene), " +
                "or select prefabs in the Project window and use the 'selected prefabs' menu item instead.",
                "OK");
            return;
        }

        var prefabs = new HashSet<GameObject>();
        foreach (var field in VegetationFields)
            Collect(gen, field, prefabs);

        Run(prefabs.ToList(), $"WorldGenerator vegetation arrays ({VegetationFields.Length} fields)");
    }

    [MenuItem("Tools/World/Bake Decoration Layers (selected prefabs)")]
    private static void BakeFromSelection()
    {
        var prefabs = Selection.GetFiltered<GameObject>(SelectionMode.Assets)
                               .Where(g => PrefabUtility.IsPartOfPrefabAsset(g))
                               .ToList();
        if (prefabs.Count == 0)
        {
            EditorUtility.DisplayDialog("Bake Decoration Layers",
                "Select one or more prefabs in the Project window first.", "OK");
            return;
        }
        Run(prefabs, $"{prefabs.Count} selected prefab(s)");
    }

    // Every array on WorldGenerator that holds scenery the runtime tagger would
    // have caught. Deliberately explicit rather than "every GameObject[] field":
    // altars, POIs and enemy prefabs also go through DisableShadowCasting, and
    // those carry colliders and behaviour that nobody should be relayering in
    // bulk from a menu item.
    private static readonly string[] VegetationFields =
    {
        "baseBushes", "baseBushesAutumn", "baseBushesWinter",
        "baseTrees",  "baseTreesAutumn",  "baseTreesWinter",
        "giantTrees", "cursedDeadTrees",  "bloomedTreeVariants",
        "deadTreesPrefabs", "waterPlantsPrefabs", "groundClutterPrefabs",
        "logPrefabs", "baseMushrooms", "riverRockPrefabs",
    };

    private static void Collect(WorldGenerator gen, string fieldName, HashSet<GameObject> into)
    {
        var f = typeof(WorldGenerator).GetField(fieldName);
        if (f == null) return;
        if (f.GetValue(gen) is GameObject[] arr)
            foreach (var g in arr)
                if (g != null && PrefabUtility.IsPartOfPrefabAsset(g)) into.Add(g);
    }

    private static void Run(List<GameObject> prefabs, string source)
    {
        int nature = LayerMask.NameToLayer(NatureLayerName);
        if (nature < 0)
        {
            EditorUtility.DisplayDialog("Bake Decoration Layers",
                $"There is no layer called '{NatureLayerName}'. Add it in Project Settings > Tags and Layers first.",
                "OK");
            return;
        }

        int changedPrefabs = 0, changedObjects = 0, changedShadows = 0, skippedPhysical = 0;
        var log = new StringBuilder();

        try
        {
            for (int i = 0; i < prefabs.Count; i++)
            {
                GameObject asset = prefabs[i];
                string path = AssetDatabase.GetAssetPath(asset);
                if (string.IsNullOrEmpty(path)) continue;

                if (EditorUtility.DisplayCancelableProgressBar(
                        "Baking decoration layers", path, (float)i / Mathf.Max(1, prefabs.Count)))
                    break;

                // Edit through the prefab contents API so variants write proper
                // modifications against their base rather than being flattened.
                GameObject root = PrefabUtility.LoadPrefabContents(path);
                bool dirty = false;
                int objs = 0, shadows = 0;

                foreach (var rnd in root.GetComponentsInChildren<Renderer>(true))
                {
                    if (rnd == null || rnd is ParticleSystemRenderer) continue;

                    if (rnd.GetComponent<Collider>() != null) { skippedPhysical++; continue; }

                    if (rnd.gameObject.layer != nature)
                    {
                        rnd.gameObject.layer = nature;
                        dirty = true; objs++;
                    }

                    // Small props spawn in the thousands and each casting a
                    // real-time shadow is a large, barely visible cost. They
                    // still RECEIVE shadows.
                    if (rnd.shadowCastingMode != UnityEngine.Rendering.ShadowCastingMode.Off)
                    {
                        rnd.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                        dirty = true; shadows++;
                    }
                }

                if (dirty)
                {
                    PrefabUtility.SaveAsPrefabAsset(root, path);
                    changedPrefabs++;
                    changedObjects += objs;
                    changedShadows += shadows;
                    log.AppendLine($"  {path}  (+{objs} to Nature, {shadows} shadow casters off)");
                }

                PrefabUtility.UnloadPrefabContents(root);
            }
        }
        finally
        {
            EditorUtility.ClearProgressBar();
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }

        string summary =
            $"Source: {source}\n" +
            $"Prefabs examined: {prefabs.Count}\n" +
            $"Prefabs changed:  {changedPrefabs}\n" +
            $"Objects moved to '{NatureLayerName}': {changedObjects}\n" +
            $"Shadow casters switched off: {changedShadows}\n" +
            $"Renderers left alone because they carry a collider: {skippedPhysical}";

        Debug.Log("[Bake Decoration Layers]\n" + summary +
                  (log.Length > 0 ? "\n\nChanged:\n" + log : "\n\nNothing needed changing."));
        EditorUtility.DisplayDialog("Bake Decoration Layers", summary, "OK");
    }
}
