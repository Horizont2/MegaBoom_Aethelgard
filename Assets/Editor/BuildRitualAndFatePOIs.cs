using UnityEditor;
using UnityEngine;
using System.IO;

// Assembles the two new points of interest as real prefabs in the project.
//
// ==== WHY THIS IS A TOOL AND NOT A HAND-WRITTEN PREFAB ====
//
// Normally the right move is to edit the asset directly. Not here. Both of these
// are model prefabs nested inside other model prefabs — a monolith FBX, four
// Polyart campfires, particle prefabs — and a prefab that contains other prefabs
// is serialised as PrefabInstance blocks whose fileIDs Unity derives rather than
// stores. Writing that YAML by hand is the one kind of asset edit that cannot be
// checked without opening the editor, and a broken prefab arrives looking like a
// bug in the feature rather than a bug in the file.
//
// Run it once and the prefabs exist, correctly linked, with every field filled
// in. After that they are ordinary prefabs: open them, move things, retune the
// numbers. Running it again rebuilds from scratch, so do not edit and re-run.
//
// ==== THE ART, AND WHY EACH PIECE ====
//
// Everything here is already in the project AND already proven to render under
// URP in this game, which rules out most of the pack content: several kits
// import against built-in shaders and come out magenta.
//
//   Totem2.fbx        shares texture.mat with MESH_ScoutTower, which is live in
//                     GameScene as the watchtower. Unused by anything else.
//   Polyart campfires the lit one is already on Camp_POI. Unlit and lit are the
//                     same object cold and burning, which is exactly a brazier.
//   Hovl auras        the Player prefab already uses four of them.
public static class BuildRitualAndFatePOIs
{
    private const string OutDir = "Assets/Prefabs/POI";

    private const string MonolithFbx   = "Assets/Locations/fbx2/Totem2.fbx";
    private const string CampfireUnlit = "Assets/Polyart/PolyartStudio/DreamscapeMeadows/Prefabs/Props/Prefab_Campfire_Unlit.prefab";
    private const string CampfireLit   = "Assets/Polyart/PolyartStudio/DreamscapeMeadows/Prefabs/Props/Prefab_Campfire_Lit.prefab";
    private const string CurseAura     = "Assets/Hovl Studio/Magic effects pack/Prefabs/Character auras/Lightning aura.prefab";
    private const string CleanseAura   = "Assets/Hovl Studio/Magic effects pack/Prefabs/Character auras/Star aura.prefab";
    private const string TensionAura   = "Assets/Hovl Studio/Magic effects pack/Prefabs/Character auras/Buff.prefab";
    private const string AltarArt      = "Assets/Prefabs/RegionLocations/Prefab_SideAltar_Boss.prefab";

    [MenuItem("Tools/Exploration/Build Ritual + Altar of Fate POIs")]
    public static void Build()
    {
        Directory.CreateDirectory(OutDir);
        BuildRitual();
        BuildAltar();
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("[POI] Built POI_RitualMonolith and POI_AltarOfFate in " + OutDir +
                  ". Add them to WorldGenerator.poiPrefabs in GameScene to place them.");
    }

    // ---------------------------------------------------------------- ritual ---

    private static void BuildRitual()
    {
        var root = new GameObject("POI_RitualMonolith");

        // ---- the stone ----
        var stoneSrc = AssetDatabase.LoadAssetAtPath<GameObject>(MonolithFbx);
        Transform stone = null;
        if (stoneSrc != null)
        {
            var go = (GameObject)PrefabUtility.InstantiatePrefab(stoneSrc, root.transform);
            go.name = "Monolith";
            go.transform.localPosition = Vector3.zero;
            go.transform.localScale = Vector3.one * 1.6f;
            EnsureCollider(go);
            stone = go.transform;
        }
        else Warn(MonolithFbx);

        // ---- the curse on it ----
        GameObject curse = Spawn(CurseAura, root.transform, "CurseVFX", new Vector3(0f, 2.2f, 0f), 2.2f);
        GameObject cleanse = Spawn(CleanseAura, root.transform, "CleanseVFX", new Vector3(0f, 1.6f, 0f), 3.0f);
        if (cleanse != null) cleanse.SetActive(false);

        // ---- four braziers, evenly around it ----
        const int COUNT = 4;
        const float RING = 6.5f;
        var braziers = new RitualBrazier[COUNT];
        for (int i = 0; i < COUNT; i++)
        {
            float a = (i / (float)COUNT) * Mathf.PI * 2f + Mathf.PI * 0.25f;
            var holder = new GameObject($"Brazier_{i + 1}");
            holder.transform.SetParent(root.transform, false);
            holder.transform.localPosition = new Vector3(Mathf.Cos(a) * RING, 0f, Mathf.Sin(a) * RING);
            // Face the stone, so the whole site reads as arranged rather than scattered.
            holder.transform.localRotation = Quaternion.LookRotation(-holder.transform.localPosition.normalized, Vector3.up);

            GameObject cold = Spawn(CampfireUnlit, holder.transform, "Unlit", Vector3.zero, 1f);
            GameObject hot  = Spawn(CampfireLit,   holder.transform, "Lit",   Vector3.zero, 1f);
            if (hot != null) hot.SetActive(false);

            var lightGO = new GameObject("Flame");
            lightGO.transform.SetParent(holder.transform, false);
            lightGO.transform.localPosition = new Vector3(0f, 1.1f, 0f);
            var lt = lightGO.AddComponent<Light>();
            lt.type = LightType.Point;
            lt.color = new Color(1f, 0.64f, 0.28f);
            lt.range = 11f;
            lt.intensity = 0f;
            lt.enabled = false;
            // Real-time point lights are the cheapest thing to get wrong on a
            // map with a hundred POIs. Four per site, no shadows, short range.
            lt.shadows = LightShadows.None;

            var rb = holder.AddComponent<RitualBrazier>();
            rb.unlitVisual = cold;
            rb.litVisual = hot;
            rb.flame = lt;
            braziers[i] = rb;
        }

        // ---- the director ----
        var ritual = root.AddComponent<RitualMonolith>();
        ritual.braziers = braziers;
        ritual.monolith = stone;
        ritual.curseVFX = curse;
        ritual.cleanseVFX = cleanse;
        ritual.enemyPrefabs = LoadAll(
            "Assets/Prefabs/Skeleton_Minion.prefab",
            "Assets/Prefabs/Skeleton_Rogue.prefab",
            "Assets/Prefabs/Archer.prefab");
        ritual.elitePrefabs = LoadAll(
            "Assets/Prefabs/Skeleton_Warrior.prefab",
            "Assets/Prefabs/Skeleton_Mage.prefab");
        ritual.spawnRing = RING + 5.5f;

        // ---- how the generator places it ----
        var poi = root.AddComponent<POISettings>();
        poi.flattenRadius = RING + 3f;      // the whole ring stands on level ground
        poi.maxAllowedSlope = 8f;
        poi.spawnWeight = 0.5f;
        poi.regionAppearChance = 0.6f;      // not in every region, so meeting one is an event
        poi.maxPerRegion = 1;

        Save(root, $"{OutDir}/POI_RitualMonolith.prefab");
    }

    // ----------------------------------------------------------------- altar ---

    private static void BuildAltar()
    {
        var root = new GameObject("POI_AltarOfFate");

        var artSrc = AssetDatabase.LoadAssetAtPath<GameObject>(AltarArt);
        if (artSrc != null)
        {
            var go = (GameObject)PrefabUtility.InstantiatePrefab(artSrc, root.transform);
            go.name = "AltarArt";
            go.transform.localPosition = Vector3.zero;
            // The roadside altar prefab brings its own boss logic. This one is a
            // gamble, not a fight — strip anything that would start a duel.
            foreach (var extra in go.GetComponentsInChildren<RoadsideAltar>(true)) Object.DestroyImmediate(extra, true);
            foreach (var mk in go.GetComponentsInChildren<MapEventMarker>(true)) Object.DestroyImmediate(mk, true);
        }
        else Warn(AltarArt);

        GameObject tension = Spawn(TensionAura, root.transform, "TensionVFX", new Vector3(0f, 1.2f, 0f), 1.6f);
        if (tension != null) tension.SetActive(false);

        var lightGO = new GameObject("AltarLight");
        lightGO.transform.SetParent(root.transform, false);
        lightGO.transform.localPosition = new Vector3(0f, 1.4f, 0f);
        var lt = lightGO.AddComponent<Light>();
        lt.type = LightType.Point;
        lt.color = new Color(0.8f, 0.4f, 1f);
        lt.range = 14f;
        lt.intensity = 0.6f;
        lt.shadows = LightShadows.None;

        var altar = root.AddComponent<AltarOfFate>();
        altar.altarLight = lt;
        altar.tensionVFX = tension;
        altar.ambushPrefabs = LoadAll(
            "Assets/Prefabs/Skeleton_Warrior.prefab",
            "Assets/Prefabs/Skeleton_Necromancer.prefab");
        // Junk for the mockery: bones, which is the joke. Both verified on the
        // URP Lit shader, so they will not arrive magenta.
        altar.junkPrefabs = LoadAll(
            "Assets/BTM_Assets/BTM_Items_Gems/Prefabs/Skull.prefab",
            "Assets/BTM_Assets/BTM_Items_Gems/Prefabs/SkullBones.prefab");

        var poi = root.AddComponent<POISettings>();
        poi.flattenRadius = 4f;
        poi.maxAllowedSlope = 10f;
        poi.spawnWeight = 0.4f;
        poi.regionAppearChance = 0.5f;
        poi.maxPerRegion = 1;

        Save(root, $"{OutDir}/POI_AltarOfFate.prefab");
    }

    // ------------------------------------------------------------------ util ---

    private static GameObject Spawn(string path, Transform parent, string name, Vector3 localPos, float scale)
    {
        var src = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        if (src == null) { Warn(path); return null; }
        var go = (GameObject)PrefabUtility.InstantiatePrefab(src, parent);
        go.name = name;
        go.transform.localPosition = localPos;
        go.transform.localScale = Vector3.one * scale;
        return go;
    }

    private static GameObject[] LoadAll(params string[] paths)
    {
        var list = new System.Collections.Generic.List<GameObject>(paths.Length);
        foreach (var p in paths)
        {
            var go = AssetDatabase.LoadAssetAtPath<GameObject>(p);
            if (go == null) { Warn(p); continue; }
            list.Add(go);
        }
        return list.ToArray();
    }

    // The monolith needs something solid so the player cannot walk through it and
    // so the braziers read as being AROUND something.
    private static void EnsureCollider(GameObject go)
    {
        if (go.GetComponentInChildren<Collider>(true) != null) return;
        var cap = go.AddComponent<CapsuleCollider>();
        cap.height = 4.5f;
        cap.radius = 0.9f;
        cap.center = new Vector3(0f, 2.25f, 0f);
    }

    private static void Save(GameObject root, string path)
    {
        PrefabUtility.SaveAsPrefabAsset(root, path);
        Object.DestroyImmediate(root);
        Debug.Log("[POI] Saved " + path);
    }

    private static void Warn(string path) =>
        Debug.LogWarning($"[POI] Missing asset, that part is left empty: {path}");
}
