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
        foreach (var oldRig in TrailerFind.AllByName("LoreTrailer_ActII_Rig"))
            if (oldRig != null) Undo.DestroyObjectImmediate(oldRig);

        // Earlier runs couldn't see DISABLED rigs, so every run made another one.
        // Collapse any duplicates down to the first.
        foreach (var dupName in new[] { RigName, "LoreTrailer_Part2_Rig" })
        {
            var all = TrailerFind.AllByName(dupName);
            for (int i = all.Count - 1; i >= 1; i--) Undo.DestroyObjectImmediate(all[i]);
            if (all.Count > 1) Debug.Log($"[Trailer] Removed {all.Count - 1} duplicate '{dupName}' object(s).");
        }

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
        season.startProgress = 0.6f;   // time-lapse begins as the end-crane rises
        season.terrainMaterial = AssetDatabase.LoadAssetAtPath<Material>(TerrainMat);
        season.summerTexture = AssetDatabase.LoadAssetAtPath<Texture>(TexSummer);
        season.autumnTexture = AssetDatabase.LoadAssetAtPath<Texture>(TexAutumn);
        season.winterTexture = AssetDatabase.LoadAssetAtPath<Texture>(TexWinter);
        season.leavesPrefab = null;   // user asked to remove the green falling leaves
        season.snowPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(SnowPrefab);
        season.sun = FindSun();
        season.cam = Camera.main;

        // FOLIAGE: the trees here are ordinary scene GameObjects (the terrains
        // report 0 tree prototypes), so build a material table — every summer
        // foliage material in the scene paired with its _Autumn / _Snow variant.
        int foliageMatched = BuildFoliageTable(season);
        EditorUtility.SetDirty(season);

        // Terrain + painted grass recolour on EVERY terrain (Part 1 + Part 2),
        // safe: works on runtime clones.
        var terrains = Object.FindObjectsByType<Terrain>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                             .Where(t => t != null && t.terrainData != null).ToArray();
        bool terrainOk = terrains.Length > 0;
        if (terrainOk)
        {
            var ts = rig.GetComponent<TrailerTerrainSeasons>();
            if (ts == null) ts = Undo.AddComponent<TrailerTerrainSeasons>(rig);
            Undo.RecordObject(ts, "config terrain seasons");
            ts.driveByRideProgress = true;
            ts.ride = ride;
            ts.terrains = terrains;
            ts.terrain = null;
            ts.startProgress = 0.6f;
            ts.swapGroundTexture = false;      // don't repaint the terrain with wrong textures
            ts.forceTintableDetails = true;    // instanced details ignore the tint — turn instancing off on the clone

            // Season prefab variants, keyed by the ORIGINAL prototype prefab so one
            // table covers every terrain.
            var variants = AssetDatabase.FindAssets("t:GameObject", new[] { "Assets/GeneratedBiomeTrees" })
                .Select(g => AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(g)))
                .Where(g => g != null).ToArray();

            var vBase = new System.Collections.Generic.List<GameObject>();
            var vAut = new System.Collections.Generic.List<GameObject>();
            var vWin = new System.Collections.Generic.List<GameObject>();
            int trees = 0, dets = 0, matched = 0;
            var names = new System.Collections.Generic.List<string>();

            foreach (var t in terrains)
            {
                foreach (var p in t.terrainData.treePrototypes) { trees++; Register(p.prefab); }
                foreach (var d in t.terrainData.detailPrototypes)
                {
                    dets++;
                    names.Add($"{(d.prototype != null ? d.prototype.name : "<texture:" + (d.prototypeTexture != null ? d.prototypeTexture.name : "none") + ">")}(instanced={d.useInstancing})");
                    Register(d.prototype);
                }
            }

            void Register(GameObject prefab)
            {
                if (prefab == null || vBase.Contains(prefab)) return;
                string bn = prefab.name;
                var a = FindVariant(variants, bn, "autumn");
                var w = FindVariant(variants, bn, "winter") ?? FindVariant(variants, bn, "snow");
                if (a == null && w == null) return;
                vBase.Add(prefab); vAut.Add(a); vWin.Add(w); matched++;
            }

            ts.variantBase = vBase.ToArray();
            ts.variantAutumn = vAut.ToArray();
            ts.variantWinter = vWin.ToArray();

            Debug.Log($"[Trailer] Terrains {terrains.Length} | tree prototypes {trees}, detail/grass prototypes {dets} (prefab variants matched {matched}). Details: {string.Join(", ", names)}");
            EditorUtility.SetDirty(ts);
        }

        Debug.Log($"[Trailer] Foliage material table: {foliageMatched} scene materials paired with season variants.");
        MarkDirty();

        EditorUtility.DisplayDialog("Act II Seasons",
            "Added to the Act I ride (Act I cameras untouched):\n" +
            "  • Seasons Summer → Autumn → Winter follow the ROUTE progress.\n" +
            "  • DAY/NIGHT: the sun races on its orbit as he rides (driveDayNight).\n" +
            $"  • TERRAIN + painted grass recolour per season: {(terrainOk ? "ON (runtime clone — asset safe)" : "NO active Terrain found")}.\n" +
            "  • Falling leaves (autumn) then snow (winter); sun + fog shift.\n" +
            "  • The Act I end-crane (CM_04) rises over the changed world while the horse is STILL galloping (overrun) — no standing still.\n\n" +
            $"  • Sun {(season.sun != null ? "OK" : "NOT FOUND")}.\n" +
            "Everything is driven by route progress, so it all stays in sync with the ride.", "OK");
    }

    private static GameObject FindVariant(GameObject[] pool, string baseName, string season)
    {
        return pool.FirstOrDefault(v => v.name.StartsWith(baseName) && v.name.ToLowerInvariant().Contains(season));
    }

    // Scene trees/bushes are GameObjects, so the recolour is a MATERIAL swap.
    // Pair every foliage material used in the scene with its _Autumn / _Snow
    // (or _Winter) sibling on disk.
    private static int BuildFoliageTable(TrailerSeasonRide season)
    {
        var allMats = AssetDatabase.FindAssets("t:Material")
            .Select(g => AssetDatabase.GUIDToAssetPath(g))
            .Where(p => p.StartsWith("Assets/"))
            .Select(p => AssetDatabase.LoadAssetAtPath<Material>(p))
            .Where(m => m != null)
            .ToArray();

        string[] suffixes = { "_Autumn", "_autumn" };
        string[] winterSuffixes = { "_Snow", "_snow", "_Winter", "_winter" };

        var bases = new System.Collections.Generic.List<Material>();
        var auts = new System.Collections.Generic.List<Material>();
        var wins = new System.Collections.Generic.List<Material>();
        var seen = new System.Collections.Generic.HashSet<Material>();

        foreach (var r in Object.FindObjectsByType<Renderer>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (r == null || r is ParticleSystemRenderer) continue;
            foreach (var m in r.sharedMaterials)
            {
                if (m == null || !seen.Add(m)) continue;
                string n = m.name;
                // Skip materials that ARE season variants already.
                string ln = n.ToLowerInvariant();
                if (ln.EndsWith("_autumn") || ln.EndsWith("_snow") || ln.EndsWith("_winter")) continue;

                // Two naming conventions in this project:
                //   M_TreeBirch_Leaves -> M_TreeBirch_Leaves_Autumn / _Snow
                //   M_TreeLarge_Leaves -> M_TreeLarge_Autumn        / _Snow  (no _Leaves)
                var stems = new System.Collections.Generic.List<string> { n };
                foreach (var drop in new[] { "_Leaves", "_leaves", "_Leaf" })
                    if (n.EndsWith(drop)) stems.Add(n.Substring(0, n.Length - drop.Length));

                Material a = null, w = null;
                foreach (var stem in stems)
                {
                    if (a == null) foreach (var s in suffixes) { a = allMats.FirstOrDefault(x => x.name == stem + s); if (a != null) break; }
                    if (w == null) foreach (var s in winterSuffixes) { w = allMats.FirstOrDefault(x => x.name == stem + s); if (w != null) break; }
                }
                if (a == null && w == null) continue;
                Debug.Log($"[Trailer] Foliage pair: {n} -> autumn={(a != null ? a.name : "-")}, winter={(w != null ? w.name : "-")}");
                bases.Add(m); auts.Add(a); wins.Add(w);
            }
        }

        season.foliageBase = bases.ToArray();
        season.foliageAutumn = auts.ToArray();
        season.foliageWinter = wins.ToArray();
        return bases.Count;
    }

    private static GameObject FindRig()
    {
        return TrailerFind.ByName(RigName);
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
