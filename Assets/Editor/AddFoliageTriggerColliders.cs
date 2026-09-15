using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

// Puts a trigger MeshCollider on the mesh of every bush bound to terrain tree
// painting, so something can finally detect a player standing in one.
//
// ==== WHY THIS IS NEEDED AT ALL ====
//
// The bush prefabs have no colliders whatsoever. A spherecast can only hit a
// collider, so nothing in the game can tell that the player is inside a bush —
// not CameraOcclusion, not the occlusion silhouette. That is why the silhouette
// only ever triggers behind trees, which do have one.
//
// ==== TWO THINGS TO KNOW BEFORE RUNNING IT ====
//
// 1. A MeshCollider CANNOT be a trigger unless it is convex. Unity rejects a
//    non-convex trigger mesh outright. So this sets convex as well — the shape
//    becomes the hull of the bush rather than its leaves, which for a "is the
//    player inside this" test is what you want anyway, and is far cheaper.
//
// 2. These bushes are painted as TERRAIN TREES, not instantiated as
//    GameObjects. Unity builds tree colliders from the prototype prefab into
//    the terrain's own collision, and that path is not the same as a collider
//    on a live GameObject: it is static, and the trigger flag is not reliably
//    honoured there. If, after running this, the player collides with bushes
//    instead of walking through them, that is what happened — undo, and the
//    bushes need to be spawned as objects rather than painted for this to work.
//
//    Check it in thirty seconds: run the game, walk into a bush. Walk through
//    it and the probe should now see it. Bump into it and this approach is
//    dead and the spawning has to change instead.
public static class AddFoliageTriggerColliders
{
    [MenuItem("Tools/World/Add Trigger Colliders To Foliage")]
    private static void Run()
    {
        var prefabs = new HashSet<GameObject>();

        // Whatever the terrain in the open scene is actually painting.
        foreach (var terrain in Object.FindObjectsByType<Terrain>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (terrain == null || terrain.terrainData == null) continue;
            foreach (var proto in terrain.terrainData.treePrototypes)
                if (proto != null && proto.prefab != null && PrefabUtility.IsPartOfPrefabAsset(proto.prefab))
                    prefabs.Add(proto.prefab);
        }

        // And whatever the generator will register at runtime, which is the set
        // that actually matters in a generated region — the scene's authored
        // list is only what somebody painted by hand.
        var gen = Object.FindFirstObjectByType<WorldGenerator>(FindObjectsInactive.Include);
        if (gen != null)
            foreach (var field in new[] { "baseBushes", "baseBushesAutumn", "baseBushesWinter" })
            {
                var f = typeof(WorldGenerator).GetField(field);
                if (f?.GetValue(gen) is GameObject[] arr)
                    foreach (var g in arr)
                        if (g != null && PrefabUtility.IsPartOfPrefabAsset(g)) prefabs.Add(g);
            }

        if (prefabs.Count == 0)
        {
            EditorUtility.DisplayDialog("Add Trigger Colliders To Foliage",
                "Found no bush prefabs. Open the scene with the Terrain and the WorldGenerator, or select " +
                "prefabs in the Project window and use the selection menu item.", "OK");
            return;
        }

        Apply(prefabs.ToList(), $"{prefabs.Count} prefab(s) from terrain prototypes and the generator's bush arrays");
    }

    [MenuItem("Tools/World/Add Trigger Colliders To Foliage (selected prefabs)")]
    private static void RunSelection()
    {
        var prefabs = Selection.GetFiltered<GameObject>(SelectionMode.Assets)
                               .Where(PrefabUtility.IsPartOfPrefabAsset).ToList();
        if (prefabs.Count == 0)
        {
            EditorUtility.DisplayDialog("Add Trigger Colliders To Foliage",
                "Select one or more prefabs in the Project window first.", "OK");
            return;
        }
        Apply(prefabs, $"{prefabs.Count} selected prefab(s)");
    }

    private static void Apply(List<GameObject> prefabs, string source)
    {
        int natureLayer = LayerMask.NameToLayer("Nature");
        int changed = 0, added = 0, skipped = 0;
        var log = new StringBuilder();

        try
        {
            for (int i = 0; i < prefabs.Count; i++)
            {
                string path = AssetDatabase.GetAssetPath(prefabs[i]);
                if (string.IsNullOrEmpty(path)) continue;

                if (EditorUtility.DisplayCancelableProgressBar(
                        "Adding trigger colliders", path, (float)i / Mathf.Max(1, prefabs.Count)))
                    break;

                GameObject root = PrefabUtility.LoadPrefabContents(path);
                bool dirty = false;
                int here = 0;

                // Wherever the geometry is — on the root, or on a child group
                // under it. The mesh is the thing that defines the shape, so the
                // collider goes on whatever object holds it.
                foreach (var mf in root.GetComponentsInChildren<MeshFilter>(true))
                {
                    if (mf == null || mf.sharedMesh == null) continue;

                    if (mf.GetComponent<Collider>() != null) { skipped++; continue; }

                    var mc = mf.gameObject.AddComponent<MeshCollider>();
                    mc.sharedMesh = mf.sharedMesh;
                    // Convex FIRST: Unity refuses isTrigger on a concave mesh,
                    // and setting them the other way round logs an error and
                    // leaves the collider solid.
                    mc.convex = true;
                    mc.isTrigger = true;

                    // The probe that looks for foliage filters by layer, and a
                    // collider on Default would be invisible to it.
                    if (natureLayer >= 0) mf.gameObject.layer = natureLayer;

                    dirty = true; here++; added++;
                }

                if (dirty)
                {
                    PrefabUtility.SaveAsPrefabAsset(root, path);
                    changed++;
                    log.AppendLine($"  {path}  (+{here} trigger collider(s))");
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
            $"Prefabs changed: {changed}\n" +
            $"Trigger MeshColliders added: {added}  (convex, on the '{LayerMask.LayerToName(Mathf.Max(0, natureLayer))}' layer)\n" +
            $"Meshes skipped because they already had a collider: {skipped}\n\n" +
            "Now walk into a bush in play mode. If you pass through it, the probe can see it and this " +
            "worked. If you BUMP INTO it, terrain tree colliders have ignored the trigger flag — undo " +
            "this and the bushes need spawning as objects instead of painting.";

        Debug.Log("[Foliage Colliders]\n" + summary + (log.Length > 0 ? "\n\nChanged:\n" + log : "\n\nNothing needed changing."));
        EditorUtility.DisplayDialog("Add Trigger Colliders To Foliage", summary, "OK");
    }
}
