using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Splines;
using Unity.Mathematics;

// Dresses the ride route into a living, cursed road for the trailer: torches
// lighting the way, the undead (Bone Tide) lining/rising along it, and villages
// burning in the distance. Everything is placed along the horse's Spline,
// grounded to the terrain, with variation, under one "Trailer_RoadDressing" root
// so it's trivial to toggle or delete.
//
//   Tools ▸ Lore Trailer ▸ Dress Roadside (torches / undead / fires)
//
// Spawned undead are stripped of AI/colliders/health so they're pure cinematic
// scenery (the horse rides through the horde), keeping only their model + idle
// animator.
public static class TrailerRoadsideDresser
{
    private const string Root = "Trailer_RoadDressing";

    private const string TorchPath = "Assets/Scenes/Low_Poly_Survival/Prefabs/Torch.prefab";
    private static readonly string[] UndeadPaths =
    {
        "Assets/Prefabs/Skeleton_Warrior.prefab",
        "Assets/Prefabs/Skeleton_Minion.prefab",
        "Assets/Prefabs/Skeleton_Rogue.prefab",
        "Assets/Prefabs/Skeleton_Mage.prefab",
    };
    // Tunables
    private const float TorchSpacing = 13f;   // metres between torches (alternating sides)
    private const float TorchSide = 3.2f;     // offset from road centre
    private const float UndeadSpacing = 16f;  // metres between undead clusters
    private const float UndeadSideMin = 3.5f, UndeadSideMax = 8f;

    [MenuItem("Tools/Lore Trailer/Dress Roadside (torches / undead / fires)")]
    public static void Dress()
    {
        var road = FindRoad();
        if (road == null)
        {
            EditorUtility.DisplayDialog("Roadside Dressing",
                "No route Spline found. Set up the ride (Act I/II) or draw a road Spline first.", "OK");
            return;
        }

        // Fresh root each run (delete the previous dressing so it doesn't stack).
        var old = GameObject.Find(Root);
        if (old != null) Undo.DestroyObjectImmediate(old);
        var root = new GameObject(Root);
        Undo.RegisterCreatedObjectUndo(root, "road dressing");

        var torch = AssetDatabase.LoadAssetAtPath<GameObject>(TorchPath);
        var undead = UndeadPaths.Select(p => AssetDatabase.LoadAssetAtPath<GameObject>(p)).Where(g => g != null).ToArray();

        float len = road.CalculateLength();
        if (len < 1f) { EditorUtility.DisplayDialog("Roadside Dressing", "Route spline is too short.", "OK"); return; }

        UnityEngine.Random.InitState(20260902);
        int torches = 0, mobs = 0;

        // Torches — alternating sides, evenly along the whole road. Kept UPRIGHT
        // (preserve the prefab's standing pose; only add yaw) — LookRotation laid
        // them on their side.
        if (torch != null)
        {
            int n = Mathf.Max(2, Mathf.FloorToInt(len / TorchSpacing));
            for (int i = 0; i <= n; i++)
            {
                float t = (float)i / n;
                float side = (i % 2 == 0) ? TorchSide : -TorchSide;
                if (PlaceAlong(road, t, side, out Vector3 p, out Vector3 fwd))
                {
                    // This torch prefab's mesh is authored lying — it needs X=-90 to
                    // stand (verified from a correctly-placed instance in the project).
                    PlaceUpright(torch, root.transform, p, UnityEngine.Random.Range(0f, 360f), 1f, -90f);
                    torches++;
                }
            }
        }

        // Undead — clusters lining the road, facing it, AI stripped.
        if (undead.Length > 0)
        {
            int n = Mathf.Max(2, Mathf.FloorToInt(len / UndeadSpacing));
            for (int i = 0; i <= n; i++)
            {
                float t = (float)i / n;
                int cluster = UnityEngine.Random.Range(1, 4);
                for (int c = 0; c < cluster; c++)
                {
                    float sideMag = UnityEngine.Random.Range(UndeadSideMin, UndeadSideMax);
                    float side = (UnityEngine.Random.value > 0.5f ? 1f : -1f) * sideMag;
                    float tt = Mathf.Clamp01(t + UnityEngine.Random.Range(-0.01f, 0.01f));
                    if (PlaceAlong(road, tt, side, out Vector3 p, out Vector3 fwd))
                    {
                        // Face the road (perpendicular toward centre).
                        Vector3 toRoad = (side > 0 ? -1f : 1f) * Vector3.Cross(Vector3.up, fwd).normalized;
                        var rot = Quaternion.LookRotation(new Vector3(toRoad.x, 0, toRoad.z)) * Quaternion.Euler(0, UnityEngine.Random.Range(-35f, 35f), 0);
                        var go = Place(undead[UnityEngine.Random.Range(0, undead.Length)], root.transform, p, rot, UnityEngine.Random.Range(0.9f, 1.1f), 0f);
                        MakeScenery(go);
                        mobs++;
                    }
                }
            }
        }

        // (Houses / burning villages left OUT — the user places those manually.)

        EditorSceneMarkDirty();
        EditorUtility.DisplayDialog("Roadside Dressing",
            $"Dressed '{road.name}':\n" +
            $"  • {torches} torches lighting the road (upright)\n" +
            $"  • {mobs} undead lining the route (AI + HP bars + colliders stripped — pure scenery)\n\n" +
            "All under '" + Root + "' — delete that object to clear, or re-run to regenerate.\n" +
            (torch == null ? "⚠ Torch prefab missing.\n" : "") +
            (undead.Length == 0 ? "⚠ No undead prefabs found.\n" : ""), "OK");
    }

    // Turn a full enemy prefab into inert cinematic scenery.
    private static void MakeScenery(GameObject go)
    {
        string[] killTypes = { "EnemyAI", "EnemyHealth", "Health", "OptimizedObject", "DistanceOptimizer", "EnemySpawnerAgent" };
        foreach (var mb in go.GetComponentsInChildren<MonoBehaviour>(true))
        {
            if (mb == null) continue;
            if (killTypes.Contains(mb.GetType().Name)) Undo.DestroyObjectImmediate(mb);
        }
        foreach (var agent in go.GetComponentsInChildren<UnityEngine.AI.NavMeshAgent>(true)) Undo.DestroyObjectImmediate(agent);
        foreach (var rb in go.GetComponentsInChildren<Rigidbody>(true)) { rb.isKinematic = true; rb.useGravity = false; }
        foreach (var col in go.GetComponentsInChildren<Collider>(true)) col.enabled = false;   // horse rides through

        // Strip world-space HP bars / any floating UI so the undead read as
        // scenery, not gameplay mobs.
        var kill = new List<GameObject>();
        foreach (var c in go.GetComponentsInChildren<Canvas>(true)) if (c != null) kill.Add(c.gameObject);
        foreach (var t in go.GetComponentsInChildren<Transform>(true))
        {
            if (t == null || t == go.transform) continue;
            string n = t.name.ToLowerInvariant();
            if (n.Contains("health") || n.Contains("hpbar") || n.Contains("hp_bar") ||
                n.Contains("healthbar") || n.Contains("hpcanvas") || n.Contains("hp bar"))
                kill.Add(t.gameObject);
        }
        foreach (var k in kill) if (k != null) Undo.DestroyObjectImmediate(k);
    }

    // --- placement helpers ---

    private static GameObject Place(GameObject prefab, Transform parent, Vector3 pos, Quaternion rot, float scale, float _)
    {
        var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
        if (go == null) go = Object.Instantiate(prefab);
        Undo.RegisterCreatedObjectUndo(go, "place " + prefab.name);
        go.transform.SetParent(parent, true);
        go.transform.SetPositionAndRotation(pos, rot);
        if (!Mathf.Approximately(scale, 1f)) go.transform.localScale *= scale;
        return go;
    }

    // Stand the prop upright with an explicit pitch (some low-poly meshes are
    // authored lying and need a -90 X to stand) plus a random yaw.
    private static GameObject PlaceUpright(GameObject prefab, Transform parent, Vector3 pos, float yaw, float scale, float pitch)
    {
        var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
        if (go == null) go = Object.Instantiate(prefab);
        Undo.RegisterCreatedObjectUndo(go, "place " + prefab.name);
        go.transform.SetParent(parent, true);
        go.transform.position = pos;
        go.transform.rotation = Quaternion.Euler(pitch, yaw, 0f);
        if (!Mathf.Approximately(scale, 1f)) go.transform.localScale *= scale;
        return go;
    }

    // Sample the spline at t, offset sideways, ground to terrain.
    private static bool PlaceAlong(SplineContainer road, float t, float side, out Vector3 pos, out Vector3 fwd)
    {
        float3 p = road.EvaluatePosition(t);
        float3 tan = road.EvaluateTangent(t);
        fwd = new Vector3(tan.x, 0f, tan.z);
        if (fwd.sqrMagnitude < 0.0001f) fwd = Vector3.forward;
        fwd.Normalize();
        Vector3 perp = Vector3.Cross(Vector3.up, fwd).normalized;
        pos = new Vector3(p.x, p.y, p.z) + perp * side;
        if (TryGround(pos, out float y)) pos.y = y;
        return true;
    }

    private static readonly string[] GroundNames = { "terrain", "ground", "floor", "road", "path", "plane" };
    private static bool TryGround(Vector3 pos, out float y)
    {
        y = pos.y;
        var hits = Physics.RaycastAll(pos + Vector3.up * 30f, Vector3.down, 120f, ~0, QueryTriggerInteraction.Ignore);
        float best = float.NegativeInfinity; bool found = false;
        foreach (var h in hits)
        {
            var col = h.collider; if (col == null) continue;
            bool isGround = col.GetComponentInParent<Terrain>() != null;
            if (!isGround) { string n = col.name.ToLowerInvariant(); foreach (var g in GroundNames) if (n.Contains(g)) { isGround = true; break; } }
            if (!isGround) continue;
            if (h.point.y > best) { best = h.point.y; found = true; }
        }
        if (found) { y = best; return true; }
        return false;
    }

    private static SplineContainer FindRoad()
    {
        var ride = Object.FindObjectsByType<TrailerHorseRide>(FindObjectsInactive.Include, FindObjectsSortMode.None).FirstOrDefault();
        if (ride != null && ride.path != null) return ride.path;
        var splines = Object.FindObjectsByType<SplineContainer>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        if (splines == null || splines.Length == 0) return null;
        return splines.FirstOrDefault(s => s.name.ToLowerInvariant().Contains("road")) ?? splines[0];
    }

    private static void EditorSceneMarkDirty()
    {
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(
            UnityEngine.SceneManagement.SceneManager.GetActiveScene());
    }
}
