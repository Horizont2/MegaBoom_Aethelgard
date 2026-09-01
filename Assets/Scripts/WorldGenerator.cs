using Polyart; // Інструмент Dreamscape
using System.Collections;
using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Splines;

[RequireComponent(typeof(Terrain))]
public class WorldGenerator : MonoBehaviour
{
    public static bool IsGenerationDone = false;
    public static float CurrentProgress = 0f;

    [Header("Mountain & Arena Settings")]
    public float depth = 50f;
    public float scale = 2.5f;
    [Range(1, 6)] public int octaves = 5;
    public float persistence = 0.45f;
    public float lacunarity = 2.5f;
    [Range(1f, 5f)] public float peakSharpness = 3.0f;
    public int terraceCount = 0;
    public float edgeMountainMultiplier = 3f;

    private float offsetX;
    private float offsetZ;

    [Header("Environment & Sky")]
    public Material skyboxMaterial;

    [Header("Dark Fantasy: Water (BITGEM)")]
    public float waterLevel = 0.12f;
    public Material waterMaterial;

    [Header("Side Objectives (Dead End Altars)")]
    public GameObject[] altarPrefabs;
    public int altarsAmount = 4; // Скільки вівтарів гарантовано з'явиться на мапі

    [Header("Caged Ally Event (roadside captive + skeleton guards)")]
    [Tooltip("Skeleton prefabs that guard the cage. Reuse the region's enemy prefabs.")]
    public GameObject[] cagedAllyGuardPrefabs;
    [Tooltip("The captive humanoid freed on clearing the guards (e.g. Knight). AllyAI is added automatically.")]
    public GameObject cagedAllyPrefab;
    [Range(0f, 1f)]
    [Tooltip("Chance a road dead-end hosts a caged-ally event instead of an altar.")]
    public float cagedAllyChance = 0.85f;
    public int maxCagedAllies = 4;
    private int spawnedCagedAllies = 0;

    [Header("Ambient Crows (distant flock atmosphere)")]
    [Tooltip("Looping crow-flock effects, e.g. P_Crows_Random / P_Crows_Orbit. A few are placed circling high over the map for atmosphere. Leave empty to skip.")]
    public GameObject[] ambientCrowPrefabs;
    public int ambientCrowCount = 0;   // 0 = off (crows rendered as untextured planes; disabled)
    public float ambientCrowMinHeight = 14f;
    public float ambientCrowMaxHeight = 32f;

    [Header("Smart Road System")]
    public TerrainLayer roadLayer; // Текстура доріг (має бути 5-м шаром в масиві)
    public float roadWidth = 5f;
    public GameObject[] roadEdgeDecorations;
    public GameObject[] deadEndAssets;
    [Range(0f, 1f)] public float roadDecorSpawnChance = 0.3f;
    public int extraDeadEndRoads = 7;
    // Minimum XZ distance between two dead-end targets — stops A* from
    // building two parallel almost-touching stub roads.
    public float deadEndMinSeparation = 60f;
    // Maximum steepness (degrees) a dead-end candidate spot may sit on.
    // Was 10° — too strict, mountainous regions often produced zero
    // dead-ends and hence zero altars. 18° is walkable but still avoids
    // cliffs.
    public float deadEndMaxSteepness = 18f;
    // Extends the road spline `deadEndTipExtensionMeters` past the
    // dead-end target so the altar totem doesn't sit on the exact final
    // knot (which reads as "road just stops at the totem"). Positive
    // values pull the road tip further in the direction the road was
    // heading, giving a small "path leading off into wilderness" feel.
    public float deadEndTipExtensionMeters = 4f;

    // Системні змінні для доріг
    private List<Vector3> roadTargets = new List<Vector3>();
    private List<Vector3> deadEndTargets = new List<Vector3>();
    private List<SplineContainer> roadSplines = new List<SplineContainer>();
    private float[,] roadBlendMap;

    // ==========================================
    // СИСТЕМА СПЛАЙНОВИХ РІЧОК (AAA РІВЕНЬ)
    // ==========================================
    [Header("Dreamscape: Spline Rivers & Lakes")]
    public int riverCount = 2;
    [Tooltip("Ширина мешу води (рекомендую 8-10)")]
    public float riverWidth = 8f;
    [Tooltip("Множник ширини водного мешу щоб він перекривав береги траншеї (1.2-1.5)")]
    [Range(1.0f, 2.0f)] public float waterMeshWidthMultiplier = 1.3f;
    [Tooltip("Ширина траншеї навколо річки (рекомендую 15)")]
    public float riverBankWidth = 15f;
    [Tooltip("Глибина траншеї в метрах (рекомендую 0.8-1.5 для м'якого русла як у Dreamscape)")]
    public float riverDepthCarve = 1.2f;
    [Tooltip("Радіус фінального озера")]
    public float lakeRadius = 25f;

    [Header("Smart Terrain Adaptation")]
    [Tooltip("Згладжувати рельєф вздовж русла ПЕРЕД вирізанням — робить річку природнішою (як на ідеальному скріні)")]
    public bool smoothTerrainAlongRiver = true;
    [Tooltip("Ширина зони згладжування рельєфу навколо русла (м)")]
    public float terrainSmoothWidth = 20f;
    [Tooltip("Сила згладжування рельєфу (0=нічого, 1=повністю рівно)")]
    [Range(0f, 1f)] public float terrainSmoothStrength = 0.5f;
    [Tooltip("Дозволити річкам текти і по рівнинах, не тільки по схилах гір")]
    public bool allowFlatlandRivers = true;

    [Header("Dynamic Water Depth")]
    [Tooltip("Заглиблення поверхні води нижче краю траншеї (% від riverDepthCarve). 0.3 = вода трохи нижче берегу")]
    [Range(0.0f, 0.6f)] public float waterDepthRatio = 0.35f;
    [Tooltip("Озера глибші за річки в N разів")]
    [Range(1.0f, 3.0f)] public float lakeDepthMultiplier = 1.5f;

    [Header("Waterfall Settings")]
    [Tooltip("Шанс що річка матиме водоспад (0=ніколи, 1=завжди)")]
    [Range(0f, 1f)] public float waterfallChance = 0.55f;
    [Tooltip("Якщо true — система буде моделювати ідеальний обрив під водоспад прямо в heightmap")]
    public bool sculptWaterfallCliffs = true;
    [Tooltip("Мінімальна висота падіння водоспаду (м)")]
    public float minWaterfallDrop = 3f;
    [Tooltip("Максимальна висота падіння водоспаду (м)")]
    public float maxWaterfallDrop = 12f;
    [Tooltip("Корекція повороту префаба водоспаду (градуси по Y). Якщо VFX дивиться не туди — підбери 90/180/270")]
    public float waterfallYawOffset = 0f;
    [Tooltip("Додатковий нахил префаба водоспаду (градуси по X). Зазвичай 0")]
    public float waterfallPitchOffset = 0f;
    [Tooltip("Мінімальна висота краю водоспаду над рівнем води (м). Водоспад не з'явиться нижче цього")]
    public float minWaterfallEdgeHeight = 6f;

    [Header("River Spacing & Quality")]
    [Tooltip("Мінімальна відстань між початками різних річок (м). Запобігає скупченню")]
    public float minRiverSeparation = 80f;
    [Tooltip("Мінімальна відстань між точками різних річок під час трейсингу (м). Якщо < — річка зупиняється")]
    public float minPathSeparation = 25f;
    [Tooltip("Спавнити окремий плейн в кінцевому озері (зазвичай не треба бо меш річки сам формує озеро)")]
    public bool spawnLakePrefab = false;
    [Tooltip("Спавнити окремий плейн чаші під водоспадом (зазвичай не треба)")]
    public bool spawnWaterfallBasin = false;

    [Tooltip("МАТЕРІАЛ річки (M_Dreamscape_River)")]
    public Material splineRiverMaterial;
    [Tooltip("Префаб озера (Prefab_WaterLake)")]
    public GameObject riverLakePrefab;
    [Tooltip("Префаб водоспаду (Prefab_WaterfallMain)")]
    public GameObject[] riverWaterfallPrefabs;

    [Header("River Details (Foam & Rocks)")]
    public GameObject[] riverFoamEdgePrefabs;
    public GameObject[] riverFoamTopPrefabs;
    public GameObject[] riverFoamBottomPrefabs;
    public GameObject[] riverRockPrefabs;

    [Header("Epic Landscapes (Canyons)")]
    public GameObject[] cliffPrefabs;
    public GameObject[] waterfallPrefabs;
    [Range(30f, 60f)] public float cliffSteepnessThreshold = 40f;
    public int maxGrassDensity = 8;

    [Header("Dreamscape: New Ecosystem")]
    public GameObject[] waterPlantsPrefabs;
    public GameObject[] deadTreesPrefabs;
    public GameObject[] ambientVFXPrefabs;

    [Header("Dark Fantasy: Ecosystem Logic")]
    public float meadowScale = 3f;
    [Range(0f, 1f)] public float meadowThreshold = 0.65f;
    public float veinScale = 8f;
    [Range(0f, 1f)] public float veinThreshold = 0.75f;

    [Header("Biome Textures (Terrain Layers)")]
    public TerrainLayer grassLayer;
    public TerrainLayer sandLayer;
    public TerrainLayer snowLayer;
    public TerrainLayer rockLayer;

    [Header("Sand Under Water")]
    [Tooltip("Множник ширини піщаної облямівки навколо русла річки (× riverBankWidth)")]
    [Range(0.3f, 2.0f)] public float riverSandWidthMul = 0.7f;
    [Tooltip("Множник радіуса піску навколо озер (× lakeRadius)")]
    [Range(0.8f, 2.0f)] public float lakeSandRadiusMul = 1.2f;
    [Tooltip("Як різко пісок переходить у траву (0=різко, 1=дуже плавно)")]
    [Range(0.05f, 0.6f)] public float sandEdgeSoftness = 0.3f;

    [Header("Biome Textures (ONLY for Trees)")]
    public Texture2D forestTreeTexture;
    public Texture2D desertTreeTexture;
    public Texture2D snowTreeTexture;

    [Header("Biome Materials (Foliage Replacement)")]
    [Tooltip("Матеріали для листя звичайних дерев")]
    public Material baseTreeAutumnMaterial;
    public Material baseTreeWinterMaterial;
    [Tooltip("Матеріали для листя великих дерев")]
    public Material giantTreeAutumnMaterial;
    public Material giantTreeWinterMaterial;
    [Tooltip("Матеріали для кущів")]
    public Material bushAutumnMaterial;
    public Material bushWinterMaterial;

    [Header("Biome Colors (Base)")]
    public Color forestFoliageColor = new Color(0.17f, 0.30f, 0.12f);
    public Color desertFoliageColor = new Color(0.65f, 0.55f, 0.26f);
    // Snow tints were too saturated blue (0.40, 0.55, 0.70), which made
    // trees and bushes look like cartoon ice cubes instead of frosted
    // vegetation. Defaults are now near-white with a cool tint so the
    // mood is "snow-dusted forest" rather than "everything was repainted
    // blue." Inspector overrides still apply if explicitly set.
    public Color snowFoliageColor = new Color(0.86f, 0.90f, 0.94f);
    public Color forestRockColor = new Color(0.55f, 0.55f, 0.55f);
    public Color desertRockColor = new Color(0.73f, 0.57f, 0.40f);
    public Color snowRockColor = new Color(0.80f, 0.83f, 0.87f);

    [Header("GENERATION BUDGETS")]
    public int spawnAttempts = 60000;
    public int maxTrees = 3000;
    public int maxBushesAndMushroom = 2500;
    public int maxRocks = 1200;

    [Header("Biome & Cluster Settings")]
    public float clusterScale = 12f;
    [Range(0f, 1f)] public float forestThreshold = 0.48f;
    public float globalBiomeScale = 2.5f;

    [Header("Base Nature Prefabs")]
    public GameObject[] giantTrees;
    public GameObject[] baseTrees;

    [Header("Per-Biome Tree Prefabs (terrain-painted, real assets)")]
    [Tooltip("Autumn/desert tree prefabs — each with its OWN leaves material baked in. Painted onto the terrain in autumn/desert cells. Leave empty to reuse Base Trees. Generate with Tools ▸ Generate Biome Tree Prefabs.")]
    public GameObject[] baseTreesAutumn;
    [Tooltip("Winter/snow tree prefabs — each with its OWN snow leaves material baked in. Painted in snow cells. Leave empty to reuse Base Trees.")]
    public GameObject[] baseTreesWinter;
    [Range(0f, 1f)]
    [Tooltip("Fraction of trees spawned as REAL objects (farmable — keep ResourceNode/collider) instead of terrain-painted. Only used when Use Terrain Tree Painting is ON.")]
    public float treeFarmableFraction = 0.18f;
    [Tooltip("OFF (default): every tree/bush spawns as a real OBJECT — farmable, with a collider, exactly like the original generator. ON: paint most vegetation onto the terrain for FPS (loses per-tree farming/occlusion-fade). Left OFF because painting broke farming + combat.")]
    public bool useTerrainTreePainting = false;

    [Header("Cursed → Bloomed Trees (story regions)")]
    [Tooltip("Blighted/dead tree prefabs spawned in cursed STORY regions. Their trunks are recoloured to the biome like normal trees, and they transform into living trees during the victory flythrough.")]
    public GameObject[] cursedDeadTrees;
    [Tooltip("Living tree the husk becomes when the curse lifts — PAIRED BY INDEX with Cursed Dead Trees. If shorter/empty, a random Base Tree is used.")]
    public GameObject[] bloomedTreeVariants;
    [Tooltip("Optional shared burst VFX played when a cursed tree blooms (procedural puff used if empty).")]
    public GameObject cursedTreeBloomVFX;
    [Tooltip("Rotation correction for the dead-tree models if they import lying down (e.g. set X = -90 for Z-up FBX models). Applied on top of the random yaw.")]
    public Vector3 cursedTreeRotationOffset = Vector3.zero;
    [Tooltip("Spawn cursed dead trees in story regions.")]
    public bool useCursedTrees = true;
    [Range(0f, 1f)]
    [Tooltip("Fraction of tree slots in a story region that become cursed dead trees.")]
    public float cursedTreeChance = 0.55f;

    public GameObject[] baseRocks;
    public GameObject[] baseBushes;
    public GameObject[] baseMushrooms;
    public GameObject[] logPrefabs;

    [Header("Per-Biome Bush Prefabs (terrain-painted, real assets)")]
    [Tooltip("Autumn/desert bush prefabs — own material baked in. Painted in autumn/desert cells. Empty = reuse Base Bushes. Generate with Tools ▸ Generate Biome Tree Prefabs.")]
    public GameObject[] baseBushesAutumn;
    [Tooltip("Winter/snow bush prefabs — own material baked in. Painted in snow cells. Empty = reuse Base Bushes.")]
    public GameObject[] baseBushesWinter;

    [Header("Storytelling & Detail Prefabs")]
    public GameObject[] ruinPrefabs;
    public GameObject[] groundClutterPrefabs;

    [Header("Giant Tree VFX (By Biome)")]
    public GameObject giantTreeVFXForest; // Зелене листя
    public GameObject giantTreeVFXAutumn; // Жовте/червоне листя
    public GameObject giantTreeVFXWinter; // Падаючий сніг

    [Header("Map Border Mountains")]
    public GameObject[] borderMountainPrefabs;
    public float borderSpacing = 40f;
    public float borderOffset = 10f;
    public float borderMinScale = 3f;
    public float borderMaxScale = 6f;

    [Header("Points of Interest")]
    public GameObject[] poiPrefabs;
    public int maxPOIs = 15;
    public float maxPOISteepness = 12f;
    public float poiClearanceRadius = 4f;

    [Header("Extraction Settings")]
    public GameObject extractionCartPrefab;
    public int extractionCartsAmount = 3;
    public float cartClearanceRadius = 6f;

    private Terrain terrain;
    private MaterialPropertyBlock propBlock;
    private const float MAX_FRAME_TIME = 0.015f;

    private int currentTreeCount = 0;
    private int currentBushCount = 0;
    private int currentRockCount = 0;
    private int currentGiantTreeCount = 0;

    [Tooltip("Hard cap on giant trees for the whole map. They are the heaviest overdraw source (large alpha-tested canopies); too many packed together drops FPS to single digits.")]
    public int maxGiantTrees = 45;
    [Tooltip("Minimum spacing between two giant trees (metres). Prevents their canopies from stacking and multiplying overdraw.")]
    public float giantTreeMinSpacing = 28f;
    // Positions of placed giant trees, for the spacing check above.
    private readonly List<Vector3> giantTreePositions = new List<Vector3>();

    private Vector3 spawnedTotemPos = Vector3.zero;
    private System.Random prng;

    private bool isRegionMissionCached;
    private int regionBiomeTypeCached;

    private List<Vector3> forbiddenZones = new List<Vector3>();

    // Terrain-tree painting: normal (baseTrees/deadTrees) trees are batched onto
    // the Unity terrain as TreeInstances instead of GameObjects — a big FPS win in
    // dense forests. Giant trees, cursed husks and everything else stay as objects
    // (they need per-instance behaviour/VFX). NOTE: painted trees do NOT get the
    // per-tree semi-transparent occlusion fade — accepted tradeoff for the FPS gain.
    private readonly List<TreeInstance> pendingTreeInstances = new List<TreeInstance>(4096);
    // Every REAL tree prefab (forest + per-biome variants + dead) gets one terrain
    // prototype. Terrain trees must be real prefab ASSETS — a runtime scene copy
    // does NOT render as a tree, which is why the earlier per-biome-copy approach
    // made winter/autumn trees vanish. Biome look now comes from dedicated biome
    // prefabs (below) whose own material is baked into the asset.
    private readonly Dictionary<GameObject, int> treeProtoIndex = new Dictionary<GameObject, int>();
    private bool terrainTreesReady;

    private class WaterfallData
    {
        public Vector3 topPos;
        public Vector3 bottomPos;
        public Vector3 flowDir;
        public float dropHeight;
        public int pathIndex;
    }

    private class RiverSystem
    {
        public List<Vector3> path = new List<Vector3>();
        public List<WaterfallData> waterfalls = new List<WaterfallData>();
        public Vector3 lakePos;
        public float lakeSurfaceY;
    }
    private List<RiverSystem> generatedRivers = new List<RiverSystem>();

    private float GetRandomFloat() => (float)prng.NextDouble();
    private float GetRandomRange(float min, float max) => Mathf.Lerp(min, max, (float)prng.NextDouble());
    private int GetRandomRangeInt(int min, int max) => prng.Next(min, max);

    // Varied scale for decorations so props don't all spawn identical. Returns a
    // MULTIPLIER vector: a uniform base pick in [min,max] plus a small independent
    // height wobble (some trees taller/thinner, some short/squat) — much more
    // natural than one uniform size. Multiply componentwise onto the prefab's
    // own localScale (Vector3.Scale) so the prefab's authored proportions survive.
    private Vector3 RandomDecorScale(float min, float max, float heightWobble = 0.14f)
    {
        float s = GetRandomRange(min, max);
        float yv = 1f + GetRandomRange(-heightWobble, heightWobble);
        return new Vector3(s, s * yv, s);
    }

    // PERF: small props spawn in the thousands; each casting real-time shadows is
    // a large, barely-visible cost. Turn shadow CASTING off (they still receive).
    private void DisableShadowCasting(GameObject go)
    {
        if (go == null) return;
        foreach (var rnd in go.GetComponentsInChildren<Renderer>(true))
            if (rnd != null && !(rnd is ParticleSystemRenderer))
                rnd.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
    }

    // ---- Terrain-tree painting ------------------------------------------------
    // Register every REAL tree prefab (forest + per-biome + dead) as a terrain
    // prototype. Real prefab assets render correctly as terrain trees; the biome
    // LOOK comes from dedicated biome prefabs (each carries its own material).
    private void PrepareTerrainTreePrototypes()
    {
        pendingTreeInstances.Clear();
        treeProtoIndex.Clear();
        terrainTreesReady = false;

        // ALWAYS wipe any terrain trees baked into the terrainData — an earlier
        // painting run (in the editor) polluted the asset, leaving stale
        // NON-harvestable painted trees on the map. Clearing here guarantees only
        // the real object trees (which ARE harvestable) exist.
        if (terrain != null && terrain.terrainData != null)
        {
            try { terrain.terrainData.SetTreeInstances(new TreeInstance[0], true); }
            catch { /* terrain has no trees — fine */ }
        }

        if (!useTerrainTreePainting) return;   // OFF by default → all vegetation spawns as objects
        if (terrain == null || terrain.terrainData == null) return;

        var protos = new List<TreePrototype>();
        void AddProtos(GameObject[] arr)
        {
            if (arr == null) return;
            foreach (var p in arr)
            {
                if (p == null || treeProtoIndex.ContainsKey(p)) continue;
                treeProtoIndex[p] = protos.Count;
                protos.Add(new TreePrototype { prefab = p, bendFactor = 0f });
            }
        }
        AddProtos(baseTrees);
        AddProtos(baseTreesAutumn);
        AddProtos(baseTreesWinter);
        AddProtos(deadTreesPrefabs);
        // Bushes + mushrooms are painted too (all vegetation on the terrain for FPS).
        AddProtos(baseBushes);
        AddProtos(baseBushesAutumn);
        AddProtos(baseBushesWinter);
        AddProtos(baseMushrooms);
        if (protos.Count == 0) return;

        try
        {
            terrain.terrainData.treePrototypes = protos.ToArray();
            terrain.terrainData.RefreshPrototypes();
            terrain.terrainData.SetTreeInstances(new TreeInstance[0], true);   // wipe stale trees

            // Keep close trees FULL-MESH (nice LOD) and push billboards far so they
            // don't pop/"disappear". High full-LOD count keeps a dense forest solid.
            terrain.treeDistance = 5000f;
            terrain.treeBillboardDistance = 220f;
            terrain.treeCrossFadeLength = 40f;
            terrain.treeMaximumFullLODCount = Mathf.Max(terrain.treeMaximumFullLODCount, 1000);

            terrainTreesReady = true;
        }
        catch (System.Exception e)
        {
            Debug.LogWarning($"[Gen] Terrain tree prototypes failed ({e.Message}) — falling back to object trees.");
            terrainTreesReady = false;
        }
    }

    // Pick the biome-appropriate tree prefab: dedicated biome prefabs if assigned,
    // else the forest set. `biomeMat` tells us which biome the cell is.
    private GameObject PickTreePrefabForBiome(Material biomeMat, bool useDeadTree)
    {
        if (useDeadTree) return GetRandomPrefab(deadTreesPrefabs);
        if (biomeMat != null && biomeMat == baseTreeAutumnMaterial && baseTreesAutumn != null && baseTreesAutumn.Length > 0)
            return GetRandomPrefab(baseTreesAutumn);
        if (biomeMat != null && biomeMat == baseTreeWinterMaterial && baseTreesWinter != null && baseTreesWinter.Length > 0)
            return GetRandomPrefab(baseTreesWinter);
        return GetRandomPrefab(baseTrees);
    }

    // True when this biome has a dedicated tree prefab set (its material carries
    // the colour), so we skip the fallback vertex tint. Forest always counts
    // (baseTrees ARE its dedicated look).
    private bool TreeBiomeHasPrefab(Material biomeMat)
    {
        if (biomeMat == baseTreeAutumnMaterial) return baseTreesAutumn != null && baseTreesAutumn.Length > 0;
        if (biomeMat == baseTreeWinterMaterial) return baseTreesWinter != null && baseTreesWinter.Length > 0;
        return true;   // forest → baseTrees
    }

    // Pick the biome-appropriate BUSH prefab (dedicated biome bushes if assigned).
    private GameObject PickBushPrefabForBiome(Material bushMat)
    {
        if (bushMat != null && bushMat == bushAutumnMaterial && baseBushesAutumn != null && baseBushesAutumn.Length > 0)
            return GetRandomPrefab(baseBushesAutumn);
        if (bushMat != null && bushMat == bushWinterMaterial && baseBushesWinter != null && baseBushesWinter.Length > 0)
            return GetRandomPrefab(baseBushesWinter);
        return GetRandomPrefab(baseBushes);
    }

    private bool BushBiomeHasPrefab(Material bushMat)
    {
        if (bushMat == bushAutumnMaterial) return baseBushesAutumn != null && baseBushesAutumn.Length > 0;
        if (bushMat == bushWinterMaterial) return baseBushesWinter != null && baseBushesWinter.Length > 0;
        return true;   // forest → baseBushes
    }

    // Paint a small CLUSTER of vegetation instances (bushes/mushrooms) onto the
    // terrain around a point. Returns how many were placed, or -1 if painting is
    // unavailable (caller then falls back to SpawnNatureCluster objects).
    private int PaintVegetationCluster(GameObject prefab, float worldX, float worldZ, int minCount, int maxCount, float radius, Color tint)
    {
        if (!terrainTreesReady || prefab == null || !treeProtoIndex.ContainsKey(prefab)) return -1;
        int count = GetRandomRangeInt(minCount, maxCount + 1);
        int placed = 0;
        for (int i = 0; i < count; i++)
        {
            float ox = GetRandomRange(-radius, radius);
            float oz = GetRandomRange(-radius, radius);
            Vector3 s = RandomDecorScale(0.7f, 1.3f);
            if (AddTerrainTree(prefab, worldX + ox, worldZ + oz, s.x, s.y, tint)) placed++;
        }
        return placed;
    }

    // Queue a terrain-tree instance. `tint` gives a mild per-biome hue when no
    // dedicated biome prefab exists. Returns false if painting is unavailable
    // (caller then falls back to a GameObject tree).
    private bool AddTerrainTree(GameObject prefab, float worldX, float worldZ, float widthScale, float heightScale, Color tint)
    {
        if (!terrainTreesReady || prefab == null) return false;
        if (!treeProtoIndex.TryGetValue(prefab, out int idx)) return false;

        Vector3 tp = terrain.transform.position;
        Vector3 size = terrain.terrainData.size;
        float nx = (worldX - tp.x) / Mathf.Max(0.001f, size.x);
        float nz = (worldZ - tp.z) / Mathf.Max(0.001f, size.z);
        if (nx < 0f || nx > 1f || nz < 0f || nz > 1f) return false;   // off-terrain → object path

        // Mild tint toward the biome colour (best-effort; the dedicated biome
        // prefab's material does the heavy lifting when one is assigned).
        Color c = Color.Lerp(Color.white, tint, 0.35f);

        pendingTreeInstances.Add(new TreeInstance
        {
            position = new Vector3(nx, 0f, nz),   // y snapped to heightmap on flush
            prototypeIndex = idx,
            widthScale = widthScale,
            heightScale = heightScale,
            rotation = GetRandomRange(0f, Mathf.PI * 2f),
            color = c,
            lightmapColor = Color.white
        });
        return true;
    }

    // Commit all queued trees to the terrain at once (snapped to the final heightmap).
    private void FlushTerrainTrees()
    {
        if (!terrainTreesReady || pendingTreeInstances.Count == 0) return;
        try
        {
            terrain.terrainData.SetTreeInstances(pendingTreeInstances.ToArray(), true);
            terrain.Flush();
            GameLog.Info($"[Gen] Painted {pendingTreeInstances.Count} terrain trees.");
        }
        catch (System.Exception e)
        {
            Debug.LogWarning($"[Gen] SetTreeInstances failed: {e.Message}");
        }
        pendingTreeInstances.Clear();
    }

    private void Awake()
    {
        Time.timeScale = 1f;
        Time.fixedDeltaTime = 0.02f;
    }

    private void Start()
    {
        IsGenerationDone = false;
        CurrentProgress = 0f;
        // Reset statics that survive a scene reload — otherwise a "Try again"
        // after dying MID-RAID inherits a stuck state: AnyActivatingRightNow
        // left true makes every totem's Update early-return (no anchors spawn →
        // capture impossible), a stuck timeScale=0 freezes the survival timer and
        // hangs the anchor spawn coroutine, and IsSpawningBlocked stops enemies.
        Time.timeScale = 1f;
        RegionTotem.AnyActivatingRightNow = false;
        EnemySpawner.IsSpawningBlocked = false;
        EnemyAI.GlobalFreeze = false;   // never inherit a stuck victory-freeze
        EnemyAI.SuppressCombatVocals = false;   // nor a stuck trailer mute
        RegionManager.CinematicActive = false;   // nor a stuck victory-cinematic latch
        CursedTree.ResetWave(); // fresh region: don't let a stale bloom wave insta-bloom
        forbiddenZones.Clear();
        generatedRivers.Clear();

        // Очищаємо списки доріг
        roadTargets.Clear();
        deadEndTargets.Clear();
        roadSplines.Clear();

        // 1. СПОЧАТКУ знаходимо Terrain
        terrain = GetComponent<Terrain>();

        // 2. ТІЛЬКИ ТЕПЕР створюємо маску (бо terrain більше не null!)
        roadBlendMap = new float[terrain.terrainData.alphamapWidth, terrain.terrainData.alphamapHeight];

        propBlock = new MaterialPropertyBlock();

        if (skyboxMaterial != null) RenderSettings.skybox = skyboxMaterial;

        RegionData curRegion = null;
        if (GameManager.Instance != null && GameManager.Instance.currentRegion != null) curRegion = GameManager.Instance.currentRegion;
        if (curRegion == null && MissionInitializer.PendingMissionRegion != null) curRegion = MissionInitializer.PendingMissionRegion;

        if (curRegion != null)
        {
            isRegionMissionCached = true;
            regionBiomeTypeCached = (int)curRegion.regionBiome;
        }
        else
        {
            isRegionMissionCached = PlayerPrefs.GetInt("IsRegionMission", 0) == 1;
            regionBiomeTypeCached = PlayerPrefs.GetInt("RegionBiomeType", 0);
        }

        GameObject player = GameObject.FindGameObjectWithTag("Player");
        if (player != null)
        {
            CharacterController cc = player.GetComponent<CharacterController>();
            if (cc != null) cc.enabled = false;
            Rigidbody rb = player.GetComponent<Rigidbody>();
            if (rb != null) { rb.isKinematic = true; rb.linearVelocity = Vector3.zero; }
            player.transform.position = new Vector3(transform.position.x + terrain.terrainData.size.x / 2f, 1000f, transform.position.z + terrain.terrainData.size.z / 2f);
        }

        int mapSeed = 0;
        if (isRegionMissionCached && curRegion != null)
        {
            if (curRegion.currentState == RegionState.Conquered) mapSeed = PlayerPrefs.GetInt("RegionSeed_" + curRegion.regionID, UnityEngine.Random.Range(0, 999999));
            else { mapSeed = UnityEngine.Random.Range(0, 999999); PlayerPrefs.SetInt("RegionSeed_" + curRegion.regionID, mapSeed); PlayerPrefs.Save(); }
        }
        else
        {
            if (PlayerPrefs.GetInt("IsContinuing", 0) == 1) mapSeed = PlayerPrefs.GetInt("MapSeed", UnityEngine.Random.Range(0, 999999));
            else { mapSeed = UnityEngine.Random.Range(0, 999999); PlayerPrefs.SetInt("MapSeed", mapSeed); PlayerPrefs.Save(); }
        }

        prng = new System.Random(mapSeed);
        offsetX = GetRandomRange(0f, 9999f);
        offsetZ = GetRandomRange(0f, 9999f);

        AdjustSettingsForBiome();
        StartCoroutine(GenerateWorldRoutine());
    }

    private IEnumerator GenerateWorldRoutine()
    {
        yield return StartCoroutine(GenerateHeightsRoutine(terrain.terrainData));
        CurrentProgress = 0.10f;

        yield return StartCoroutine(CalculateAndCarveRiversRoutine(terrain.terrainData));
        CurrentProgress = 0.20f;

        // СПАВН БАЗ ТА POI
        yield return StartCoroutine(SpawnRegionTotemRoutine());
        yield return StartCoroutine(SpawnPOIsRoutine());
        yield return StartCoroutine(SpawnExtractionCartsRoutine());
        Physics.SyncTransforms();
        CurrentProgress = 0.30f;

        // --- НОВЕ: ПРОКЛАДАННЯ ДОРІГ ---
        yield return StartCoroutine(GenerateRoadsRoutine());
        CurrentProgress = 0.40f;

        yield return StartCoroutine(PaintTerrainRoutine(terrain.terrainData));
        CurrentProgress = 0.50f;

        yield return StartCoroutine(GenerateDetailsRoutine());
        CurrentProgress = 0.60f;

        SpawnWaterPlane();
        yield return StartCoroutine(PopulateSplineRiversRoutine());
        CurrentProgress = 0.65f;

        yield return StartCoroutine(PopulateBiomesRoutine());
        CurrentProgress = 0.85f;

        // --- НОВЕ: ДЕКОРАЦІЇ ДОРІГ ---
        yield return StartCoroutine(SpawnRoadDecorationsRoutine());

        yield return StartCoroutine(SpawnBorderMountainsRoutine());
        Physics.SyncTransforms();
        CurrentProgress = 0.95f;

        GameObject player = GameObject.FindGameObjectWithTag("Player");
        if (player != null)
        {
            float absoluteWaterHeight = transform.position.y + (depth * waterLevel);
            Vector3 safePos = player.transform.position;
            bool foundSafeSpot = false;
            float mapWidth = terrain.terrainData.size.x;
            float mapLength = terrain.terrainData.size.z;
            float minSpawnDistance = Mathf.Min(mapWidth, mapLength) * 0.30f;

            for (int i = 0; i < 500; i++)
            {
                float px = GetRandomRange(mapWidth * 0.1f, mapWidth * 0.9f);
                float pz = GetRandomRange(mapLength * 0.1f, mapLength * 0.9f);
                float worldX = transform.position.x + px;
                float worldZ = transform.position.z + pz;
                float worldY = terrain.SampleHeight(new Vector3(worldX, 0, worldZ)) + transform.position.y;

                if (worldY > absoluteWaterHeight + 1.5f && terrain.terrainData.GetSteepness(px / mapWidth, pz / mapLength) < 20f)
                {
                    if (Vector3.Distance(new Vector3(worldX, worldY, worldZ), spawnedTotemPos) > minSpawnDistance)
                    {
                        safePos = new Vector3(worldX, worldY + 2f, worldZ);
                        foundSafeSpot = true;
                        break;
                    }
                }
            }

            if (!foundSafeSpot) safePos.y += 5f;
            // Do the synchronous realtime-GI refresh HERE — before control is
            // handed back — instead of after. Running it once the player could
            // already walk produced the hitch right as gameplay started.
            yield return new WaitForEndOfFrame();
            DynamicGI.UpdateEnvironment();
            CharacterController cc = player.GetComponent<CharacterController>();
            if (cc != null) { cc.enabled = false; player.transform.position = safePos; cc.enabled = true; }
            else { player.transform.position = safePos; Rigidbody rb = player.GetComponent<Rigidbody>(); if (rb != null) { rb.isKinematic = false; rb.linearVelocity = Vector3.zero; } }
            if (Camera.main != null)
            {
                CameraFollow camFollow = Camera.main.GetComponent<CameraFollow>();
                if (camFollow != null) camFollow.SnapToTarget();
            }
        }

        SpawnAmbientCrows();

        CurrentProgress = 1f;
        IsGenerationDone = true;
    }

    // Places a few looping crow-flock effects high over the land for ambient
    // "distant crows" atmosphere. Purely visual — no colliders, over land only.
    private void SpawnAmbientCrows()
    {
        // Disabled: the crow prefabs draw an untextured plane mesh (M_Crow has
        // no _MainTex), so they read as gray floating squares in the sky rather
        // than birds. Count 0 keeps them off until the material is fixed.
        if (ambientCrowCount <= 0) return;
        if (ambientCrowPrefabs == null || ambientCrowPrefabs.Length == 0 || terrain == null) return;

        Transform container = new GameObject("AmbientCrows").transform;
        container.SetParent(this.transform);

        float w = terrain.terrainData.size.x;
        float l = terrain.terrainData.size.z;
        float absWaterH = transform.position.y + (depth * waterLevel);

        int placed = 0, attempts = 0;
        int target = Mathf.Max(1, ambientCrowCount);
        while (placed < target && attempts++ < target * 40)
        {
            float px = transform.position.x + GetRandomRange(w * 0.15f, w * 0.85f);
            float pz = transform.position.z + GetRandomRange(l * 0.15f, l * 0.85f);
            float groundY = terrain.SampleHeight(new Vector3(px, 0, pz)) + transform.position.y;
            if (groundY <= absWaterH + 2f) continue;   // keep them over land

            GameObject prefab = ambientCrowPrefabs[GetRandomRangeInt(0, ambientCrowPrefabs.Length)];
            if (prefab == null) continue;

            float h = groundY + GetRandomRange(ambientCrowMinHeight, ambientCrowMaxHeight);
            Instantiate(prefab, new Vector3(px, h, pz),
                        Quaternion.Euler(0f, GetRandomRange(0f, 360f), 0f), container);
            placed++;
        }
    }

    private IEnumerator CalculateAndCarveRiversRoutine(TerrainData td)
    {
        if (riverCount <= 0) yield break;

        int res = td.heightmapResolution;
        float[,] heights = td.GetHeights(0, 0, res, res);
        float startTime = Time.realtimeSinceStartup;

        float absWaterH = transform.position.y + (depth * waterLevel);

        float pxPerMeter = (res - 1) / td.size.x;

        int riverPx = Mathf.Max(2, Mathf.RoundToInt(riverWidth * 0.55f * pxPerMeter));
        int bankPx = Mathf.Max(riverPx + 2, Mathf.RoundToInt(riverBankWidth * 0.5f * pxPerMeter));
        int lakePx = Mathf.Max(4, Mathf.RoundToInt(lakeRadius * pxPerMeter));

        float carveN = riverDepthCarve / depth;
        float lakeCarveN = (riverDepthCarve * lakeDepthMultiplier) / depth;
        float maxCarveN = carveN * 1.8f;
        int smoothPx = Mathf.Max(bankPx, Mathf.RoundToInt(terrainSmoothWidth * 0.5f * pxPerMeter));

        const float STEP = 2.2f;
        int successCount = 0;
        int attempts = 0;

        while (successCount < riverCount && attempts < 100)
        {
            attempts++;

            bool wantWaterfall = GetRandomFloat() < waterfallChance;

            Vector3 startPos = Vector3.zero;
            bool foundStart = false;
            float minStartH = wantWaterfall ? absWaterH + 14f : absWaterH + 6f;
            float maxStartH = wantWaterfall ? absWaterH + 60f : absWaterH + 45f;
            float minStartSteep = allowFlatlandRivers ? 0.3f : 2f;

            for (int t = 0; t < 600 && !foundStart; t++)
            {
                float px = GetRandomRange(td.size.x * 0.1f, td.size.x * 0.9f);
                float pz = GetRandomRange(td.size.z * 0.1f, td.size.z * 0.9f);
                float py = terrain.SampleHeight(new Vector3(transform.position.x + px, 0, transform.position.z + pz))
                           + transform.position.y;
                float steep = td.GetSteepness(px / td.size.x, pz / td.size.z);

                if (py > minStartH && py < maxStartH && steep < 28f && steep > minStartSteep)
                {
                    Vector3 candidate = new Vector3(transform.position.x + px, py, transform.position.z + pz);

                    bool tooClose = false;
                    float minSepSq = minRiverSeparation * minRiverSeparation;
                    foreach (RiverSystem existing in generatedRivers)
                    {
                        if (existing.path.Count == 0) continue;
                        Vector3 existStart = existing.path[0];
                        Vector3 existEnd = existing.path[existing.path.Count - 1];

                        Vector2 c2 = new Vector2(candidate.x, candidate.z);
                        Vector2 s2 = new Vector2(existStart.x, existStart.z);
                        Vector2 e2 = new Vector2(existEnd.x, existEnd.z);

                        if ((c2 - s2).sqrMagnitude < minSepSq || (c2 - e2).sqrMagnitude < minSepSq)
                        {
                            tooClose = true;
                            break;
                        }

                        float minPathSqDist = minPathSeparation * minPathSeparation;
                        for (int pi = 0; pi < existing.path.Count; pi += 4)
                        {
                            Vector2 p2 = new Vector2(existing.path[pi].x, existing.path[pi].z);
                            if ((c2 - p2).sqrMagnitude < minPathSqDist) { tooClose = true; break; }
                        }
                        if (tooClose) break;
                    }

                    if (!tooClose)
                    {
                        startPos = candidate;
                        foundStart = true;
                    }
                }
            }
            if (!foundStart) continue;

            float nx0 = (startPos.x - transform.position.x) / td.size.x;
            float nz0 = (startPos.z - transform.position.z) / td.size.z;
            Vector3 terrN = td.GetInterpolatedNormal(nx0, nz0);
            Vector3 dir = new Vector3(terrN.x, 0, terrN.z).normalized;
            if (dir.sqrMagnitude < 0.01f)
                dir = new Vector3(GetRandomRange(-1f, 1f), 0, GetRandomRange(-1f, 1f)).normalized;

            RiverSystem river = new RiverSystem();
            Vector3 cur = startPos;
            int safety = 1000;
            int stuck = 0;

            float minPathSepSq = minPathSeparation * minPathSeparation;
            bool collidedWithOtherRiver = false;

            while (safety > 0 && cur.y > absWaterH + 0.15f)
            {
                if (generatedRivers.Count > 0 && river.path.Count % 3 == 0)
                {
                    Vector2 c2 = new Vector2(cur.x, cur.z);
                    foreach (RiverSystem other in generatedRivers)
                    {
                        for (int pi = 0; pi < other.path.Count; pi += 3)
                        {
                            Vector2 p2 = new Vector2(other.path[pi].x, other.path[pi].z);
                            if ((c2 - p2).sqrMagnitude < minPathSepSq)
                            {
                                collidedWithOtherRiver = true;
                                break;
                            }
                        }
                        if (collidedWithOtherRiver) break;
                    }
                    if (collidedWithOtherRiver) break;
                }

                river.path.Add(cur);
                safety--;

                float step = STEP * (stuck > 3 ? 3f : 1f);
                Vector3 best = cur;
                float bestH = cur.y - 0.002f;
                bool found = false;

                for (int a = -85; a <= 85; a += 8)
                {
                    Vector3 d = Quaternion.Euler(0, a, 0) * dir;
                    Vector3 c = cur + d * step;
                    if (!InBounds(c, td)) continue;
                    float h = terrain.SampleHeight(c) + transform.position.y;
                    if (h < bestH) { bestH = h; best = new Vector3(c.x, h, c.z); found = true; }
                }

                if (!found)
                {
                    stuck++;
                    if (stuck <= 8)
                    {
                        float bigStep = STEP * 5f;
                        for (int a = 0; a < 360; a += 12)
                        {
                            Vector3 d = Quaternion.Euler(0, a, 0) * dir;
                            Vector3 c = cur + d * bigStep;
                            if (!InBounds(c, td)) continue;
                            float h = terrain.SampleHeight(c) + transform.position.y;
                            if (h < cur.y - 0.002f)
                            {
                                best = new Vector3(c.x, h, c.z);
                                found = true;
                                Vector3 nd = best - cur; nd.y = 0;
                                if (nd.sqrMagnitude > 0.001f) dir = nd.normalized;
                                break;
                            }
                        }
                    }
                    if (!found) break;
                }
                else { stuck = 0; }

                float drop = cur.y - (terrain.SampleHeight(best) + transform.position.y);
                float steepness = td.GetSteepness(
                    (cur.x - transform.position.x) / td.size.x,
                    (cur.z - transform.position.z) / td.size.z);

                bool canAddWaterfall = wantWaterfall
                    && river.waterfalls.Count < 2
                    && river.path.Count > 18
                    && (river.waterfalls.Count == 0 ||
                        river.path.Count - river.waterfalls[river.waterfalls.Count - 1].pathIndex > 25);

                if (canAddWaterfall && drop > 2f && steepness > 22f)
                {
                    Vector3 wfBottom = best;
                    for (int wfi = 0; wfi < 12; wfi++)
                    {
                        Vector3 nextWf = wfBottom + dir * (STEP * 1.5f);
                        if (!InBounds(nextWf, td)) break;
                        float wfH = terrain.SampleHeight(nextWf) + transform.position.y;
                        if (wfH >= wfBottom.y - 0.1f) break;
                        wfBottom = new Vector3(nextWf.x, wfH, nextWf.z);
                    }

                    float realDrop = cur.y - wfBottom.y;
                    float edgeAboveWater = cur.y - absWaterH;
                    bool nearOther = IsPositionNearOtherRiver(cur, river, minRiverSeparation * 0.7f)
                                  || IsPositionNearOtherRiver(wfBottom, river, minRiverSeparation * 0.7f);

                    if (realDrop >= minWaterfallDrop
                        && edgeAboveWater >= minWaterfallEdgeHeight
                        && !nearOther)
                    {
                        river.waterfalls.Add(new WaterfallData
                        {
                            topPos = cur,
                            bottomPos = wfBottom,
                            flowDir = dir,
                            dropHeight = Mathf.Min(realDrop, maxWaterfallDrop),
                            pathIndex = river.path.Count - 1
                        });
                        cur = wfBottom;
                        stuck = 0;
                        continue;
                    }
                }

                Vector3 mv = best - cur; mv.y = 0;
                if (mv.sqrMagnitude > 0.001f)
                    dir = Vector3.Lerp(dir, mv.normalized, 0.3f).normalized;

                cur = best;
            }

            if (river.path.Count < 20) continue;
            float traveled = Vector3.Distance(river.path[0], river.path[river.path.Count - 1]);
            if (traveled < 50f) continue;

            if (wantWaterfall && sculptWaterfallCliffs && river.waterfalls.Count == 0
                && river.path.Count > 40)
            {
                TrySculptWaterfall(river, heights, td, res, pxPerMeter,
                    riverPx, bankPx, absWaterH);
            }

            river.lakePos = cur;
            river.path.Add(cur);

            SmoothPathXZ(river.path, 4);

            generatedRivers.Add(river);
            successCount++;

            if (smoothTerrainAlongRiver && terrainSmoothStrength > 0.01f)
            {
                SmoothTerrainAlongPath(heights, res, td, river.path, smoothPx, terrainSmoothStrength);
            }

            int pc = river.path.Count;
            float[] bedHeights = new float[pc];
            float runningMin = float.MaxValue;
            for (int i = 0; i < pc; i++)
            {
                Vector3 pt = river.path[i];
                int cx = Mathf.Clamp(Mathf.RoundToInt(((pt.x - transform.position.x) / td.size.x) * (res - 1)), 0, res - 1);
                int cy = Mathf.Clamp(Mathf.RoundToInt(((pt.z - transform.position.z) / td.size.z) * (res - 1)), 0, res - 1);
                float surfH = heights[cy, cx];
                float desired = surfH - carveN;
                runningMin = Mathf.Min(runningMin, desired);
                bedHeights[i] = Mathf.Max(runningMin, (absWaterH - 1f - transform.position.y) / depth);
            }

            for (int i = 0; i < pc; i++)
            {
                Vector3 pt = river.path[i];
                int cx = Mathf.Clamp(Mathf.RoundToInt(((pt.x - transform.position.x) / td.size.x) * (res - 1)), 0, res - 1);
                int cy = Mathf.Clamp(Mathf.RoundToInt(((pt.z - transform.position.z) / td.size.z) * (res - 1)), 0, res - 1);
                CarveRiverChannelAt(heights, res, cx, cy, riverPx, bankPx, bedHeights[i], maxCarveN);
            }

            CarveCircle(heights, res, td, river.lakePos, lakePx,
                ((absWaterH - 1.5f * lakeDepthMultiplier) - transform.position.y) / depth,
                maxCarveN * lakeDepthMultiplier);

            foreach (WaterfallData wf in river.waterfalls)
            {
                int basinPx = Mathf.Max(3, Mathf.RoundToInt(riverBankWidth * 0.9f * pxPerMeter));
                float basinBedN = ((wf.bottomPos.y - 1.5f) - transform.position.y) / depth;
                CarveCircle(heights, res, td, wf.bottomPos, basinPx, basinBedN, maxCarveN * 1.3f);
            }

            if (Time.realtimeSinceStartup - startTime > MAX_FRAME_TIME)
            { yield return null; startTime = Time.realtimeSinceStartup; }
        }

        td.SetHeights(0, 0, heights);
    }

    private void SmoothTerrainAlongPath(float[,] h, int res, TerrainData td,
        List<Vector3> path, int smoothPx, float strength)
    {
        foreach (Vector3 pt in path)
        {
            int cx = Mathf.Clamp(Mathf.RoundToInt(((pt.x - transform.position.x) / td.size.x) * (res - 1)), 0, res - 1);
            int cy = Mathf.Clamp(Mathf.RoundToInt(((pt.z - transform.position.z) / td.size.z) * (res - 1)), 0, res - 1);

            float sum = 0f; int cnt = 0;
            for (int dy = -smoothPx; dy <= smoothPx; dy += 2)
                for (int dx = -smoothPx; dx <= smoothPx; dx += 2)
                {
                    int hx = cx + dx, hy = cy + dy;
                    if (hx < 0 || hx >= res || hy < 0 || hy >= res) continue;
                    if (dx * dx + dy * dy > smoothPx * smoothPx) continue;
                    sum += h[hy, hx]; cnt++;
                }
            if (cnt == 0) continue;
            float avg = sum / cnt;

            for (int dy = -smoothPx; dy <= smoothPx; dy++)
                for (int dx = -smoothPx; dx <= smoothPx; dx++)
                {
                    int hx = cx + dx, hy = cy + dy;
                    if (hx < 0 || hx >= res || hy < 0 || hy >= res) continue;
                    float dist = Mathf.Sqrt(dx * dx + dy * dy);
                    if (dist > smoothPx) continue;

                    float edgeFade = 1f - Mathf.SmoothStep(0f, 1f, dist / smoothPx);
                    float k = strength * edgeFade;
                    h[hy, hx] = Mathf.Lerp(h[hy, hx], avg, k);
                }
        }
    }

    private void CarveRiverChannelAt(float[,] h, int res, int cx, int cy,
        int riverPx, int bankPx, float targetH, float maxCarveN)
    {
        for (int dy = -bankPx; dy <= bankPx; dy++)
        {
            for (int dx = -bankPx; dx <= bankPx; dx++)
            {
                int hx = cx + dx, hy = cy + dy;
                if (hx < 0 || hx >= res || hy < 0 || hy >= res) continue;
                float dist = Mathf.Sqrt(dx * dx + dy * dy);
                if (dist > bankPx) continue;

                float orig = h[hy, hx];

                if (dist <= riverPx)
                {
                    float newH = Mathf.Max(targetH, orig - maxCarveN);
                    if (newH < orig) h[hy, hx] = newH;
                }
                else
                {
                    float t = Mathf.SmoothStep(0f, 1f, (dist - riverPx) / (float)(bankPx - riverPx));
                    float desired = Mathf.Lerp(targetH, orig, t);
                    float maxed = Mathf.Max(desired, orig - maxCarveN);
                    if (maxed < orig) h[hy, hx] = maxed;
                }
            }
        }
    }

    private void TrySculptWaterfall(RiverSystem river, float[,] heights, TerrainData td,
        int res, float pxPerMeter, int riverPx, int bankPx, float absWaterH)
    {
        if (river.path.Count < 40) return;

        int candidateIdx = -1;
        float bestScore = float.MinValue;

        int searchStart = Mathf.Max(15, river.path.Count * 3 / 10);
        int searchEnd = Mathf.Min(river.path.Count - 15, river.path.Count * 7 / 10);

        for (int i = searchStart; i < searchEnd; i++)
        {
            Vector3 p = river.path[i];
            float heightAboveWater = p.y - absWaterH;
            if (heightAboveWater < Mathf.Max(minWaterfallDrop + 3f, minWaterfallEdgeHeight)) continue;

            if (IsPositionNearOtherRiver(p, river, minRiverSeparation * 0.8f)) continue;

            int backIdx = Mathf.Max(0, i - 6);
            float behindDrop = river.path[backIdx].y - p.y;
            float platformBefore = 1f / (Mathf.Abs(behindDrop) + 0.5f);

            int aheadIdx = Mathf.Min(river.path.Count - 1, i + 4);
            Vector3 dirAhead = river.path[aheadIdx] - p; dirAhead.y = 0;
            Vector3 dirBehind = p - river.path[backIdx]; dirBehind.y = 0;
            float dirStability = 0f;
            if (dirAhead.sqrMagnitude > 0.01f && dirBehind.sqrMagnitude > 0.01f)
                dirStability = Vector3.Dot(dirAhead.normalized, dirBehind.normalized);
            if (dirStability < 0.5f) continue;

            float score = heightAboveWater * 0.5f
                        + platformBefore * 5f
                        + dirStability * 3f;

            if (score > bestScore) { bestScore = score; candidateIdx = i; }
        }

        if (candidateIdx < 0) return;

        Vector3 topPt = river.path[candidateIdx];
        float maxAllowedDrop = topPt.y - absWaterH - 1.5f;
        float wantedDrop = GetRandomRange(minWaterfallDrop, maxWaterfallDrop);
        wantedDrop = Mathf.Min(wantedDrop, maxAllowedDrop);
        if (wantedDrop < minWaterfallDrop) return;

        Vector3 fwd = (river.path[Mathf.Min(candidateIdx + 1, river.path.Count - 1)] - topPt);
        fwd.y = 0;
        if (fwd.sqrMagnitude < 0.01f) return;
        fwd.Normalize();

        Vector3 bottomPt = topPt + fwd * (riverWidth * 1.5f);
        bottomPt.y = topPt.y - wantedDrop;

        int topCx = Mathf.Clamp(Mathf.RoundToInt(((topPt.x - transform.position.x) / td.size.x) * (res - 1)), 0, res - 1);
        int topCy = Mathf.Clamp(Mathf.RoundToInt(((topPt.z - transform.position.z) / td.size.z) * (res - 1)), 0, res - 1);

        float cliffStartDist = 1f;
        float cliffEndDist = riverWidth * 2f + 5f;
        int sweepSteps = 14;
        int cliffWidthPx = Mathf.Max(bankPx, Mathf.RoundToInt(riverBankWidth * 1.2f * pxPerMeter));

        float targetBottomN = (bottomPt.y - 0.5f - transform.position.y) / depth;

        for (int step = 0; step < sweepSteps; step++)
        {
            float distAhead = Mathf.Lerp(cliffStartDist, cliffEndDist, step / (float)(sweepSteps - 1));
            Vector3 sweepPt = topPt + fwd * distAhead;
            int sx = Mathf.Clamp(Mathf.RoundToInt(((sweepPt.x - transform.position.x) / td.size.x) * (res - 1)), 0, res - 1);
            int sy = Mathf.Clamp(Mathf.RoundToInt(((sweepPt.z - transform.position.z) / td.size.z) * (res - 1)), 0, res - 1);

            for (int dy = -cliffWidthPx; dy <= cliffWidthPx; dy++)
            {
                for (int dx = -cliffWidthPx; dx <= cliffWidthPx; dx++)
                {
                    int hx = sx + dx, hy = sy + dy;
                    if (hx < 0 || hx >= res || hy < 0 || hy >= res) continue;
                    float distC = Mathf.Sqrt(dx * dx + dy * dy);
                    if (distC > cliffWidthPx) continue;

                    float t = Mathf.SmoothStep(0f, 1f, distC / cliffWidthPx);
                    float orig = heights[hy, hx];
                    float desired = Mathf.Lerp(targetBottomN, orig, t);
                    if (desired < orig) heights[hy, hx] = desired;
                }
            }
        }

        float realBotH = SampleHeightFromHeightmap(heights, res, td, bottomPt) + transform.position.y;
        bottomPt.y = realBotH;

        river.waterfalls.Add(new WaterfallData
        {
            topPos = topPt,
            bottomPos = bottomPt,
            flowDir = fwd,
            dropHeight = wantedDrop,
            pathIndex = candidateIdx
        });

        if (candidateIdx + 1 < river.path.Count)
        {
            river.path[candidateIdx + 1] = bottomPt;

            float postStepDist = 3f;
            for (int n = 2; n <= 4 && candidateIdx + n < river.path.Count; n++)
            {
                Vector3 nextPos = bottomPt + fwd * (postStepDist * (n - 1));
                float h = SampleHeightFromHeightmap(heights, res, td, nextPos) + transform.position.y;
                river.path[candidateIdx + n] = new Vector3(nextPos.x, h, nextPos.z);
            }
        }
    }

    private float SampleHeightFromHeightmap(float[,] h, int res, TerrainData td, Vector3 worldPos)
    {
        int hx = Mathf.Clamp(Mathf.RoundToInt(((worldPos.x - transform.position.x) / td.size.x) * (res - 1)), 0, res - 1);
        int hy = Mathf.Clamp(Mathf.RoundToInt(((worldPos.z - transform.position.z) / td.size.z) * (res - 1)), 0, res - 1);
        return h[hy, hx] * depth;
    }

    private void CarveCircle(float[,] h, int res, TerrainData td, Vector3 worldPos,
        int radiusPx, float targetNorm, float maxCarveNorm)
    {
        int cx = Mathf.Clamp(Mathf.RoundToInt(((worldPos.x - transform.position.x) / td.size.x) * (res - 1)), 0, res - 1);
        int cy = Mathf.Clamp(Mathf.RoundToInt(((worldPos.z - transform.position.z) / td.size.z) * (res - 1)), 0, res - 1);

        for (int dy = -radiusPx; dy <= radiusPx; dy++)
        {
            for (int dx = -radiusPx; dx <= radiusPx; dx++)
            {
                int hx = cx + dx, hy = cy + dy;
                if (hx < 0 || hx >= res || hy < 0 || hy >= res) continue;
                float dist = Mathf.Sqrt(dx * dx + dy * dy);
                if (dist > radiusPx) continue;

                float t = Mathf.SmoothStep(0f, 1f, dist / radiusPx);
                float orig = h[hy, hx];
                float desired = Mathf.Lerp(targetNorm, orig, t);
                float maxed = Mathf.Max(desired, orig - maxCarveNorm);
                if (maxed < orig) h[hy, hx] = maxed;
            }
        }
    }

    private bool InBounds(Vector3 pos, TerrainData td)
    {
        return pos.x > transform.position.x + 3f &&
               pos.x < transform.position.x + td.size.x - 3f &&
               pos.z > transform.position.z + 3f &&
               pos.z < transform.position.z + td.size.z - 3f;
    }

    private bool IsPositionNearOtherRiver(Vector3 pos, RiverSystem currentRiver, float minDist)
    {
        float minDistSq = minDist * minDist;
        Vector2 p2 = new Vector2(pos.x, pos.z);
        foreach (RiverSystem other in generatedRivers)
        {
            if (other == currentRiver) continue;
            for (int i = 0; i < other.path.Count; i += 3)
            {
                Vector2 op = new Vector2(other.path[i].x, other.path[i].z);
                if ((p2 - op).sqrMagnitude < minDistSq) return true;
            }
            foreach (WaterfallData owf in other.waterfalls)
            {
                Vector2 wfp = new Vector2(owf.bottomPos.x, owf.bottomPos.z);
                if ((p2 - wfp).sqrMagnitude < minDistSq) return true;
            }
        }
        return false;
    }

    private void SmoothPathXZ(List<Vector3> path, int passes)
    {
        if (path.Count < 3) return;
        for (int k = 0; k < passes; k++)
        {
            for (int i = 1; i < path.Count - 1; i++)
            {
                float sx = (path[i - 1].x + path[i].x * 2f + path[i + 1].x) * 0.25f;
                float sz = (path[i - 1].z + path[i].z * 2f + path[i + 1].z) * 0.25f;
                path[i] = new Vector3(sx, path[i].y, sz);
            }
        }
    }

    private void SmoothRiverPath(List<Vector3> path) => SmoothPathXZ(path, 3);
    private void SmoothRiverPathXZ(List<Vector3> path, int passes) => SmoothPathXZ(path, passes);

    private IEnumerator PopulateSplineRiversRoutine()
    {
        Transform riverContainer = new GameObject("RiversContainer").transform;
        riverContainer.SetParent(this.transform);
        float startTime = Time.realtimeSinceStartup;
        float absWaterH = transform.position.y + (depth * waterLevel);
        int splineIdx = 0;

        float dynamicWaterDepth = riverDepthCarve * waterDepthRatio;

        foreach (RiverSystem river in generatedRivers)
        {
            if (river.path.Count < 3) continue;

            List<(int from, int to)> segments = BuildSegments(river);

            Vector3 sourceWorld = river.path[0];
            sourceWorld.y = terrain.SampleHeight(sourceWorld) + transform.position.y;
            Vector3 sourceDir = GetPathDir(river.path, 0, Mathf.Min(2, river.path.Count - 1));

            bool waterfallNearSource = false;
            foreach (WaterfallData wf in river.waterfalls)
            {
                if (wf.pathIndex < river.path.Count * 0.25f) { waterfallNearSource = true; break; }
                if (Vector3.Distance(wf.topPos, river.path[0]) < lakeRadius + riverWidth * 2f)
                { waterfallNearSource = true; break; }
            }

            if (!waterfallNearSource)
                SpawnSourceRocks(sourceWorld, sourceDir, riverContainer);

            foreach ((int from, int to) seg in segments)
            {
                BuildSplineSegment(river.path, seg.from, seg.to,
                    riverContainer, ref splineIdx, absWaterH, dynamicWaterDepth);
            }

            foreach (WaterfallData wf in river.waterfalls)
            {
                SpawnWaterfallFeature(wf, absWaterH, dynamicWaterDepth, riverContainer);
            }

            float lakeTerrY = terrain.SampleHeight(river.lakePos) + transform.position.y;
            float lakeSurfY = Mathf.Max(absWaterH + 0.05f, lakeTerrY + dynamicWaterDepth);
            river.lakeSurfaceY = lakeSurfY;

            if (spawnLakePrefab && riverLakePrefab != null)
            {
                float lakeScale = Mathf.Max(1f, lakeRadius / 5f);
                GameObject lake = Instantiate(riverLakePrefab,
                    new Vector3(river.lakePos.x, lakeSurfY, river.lakePos.z),
                    Quaternion.Euler(0, GetRandomRange(0, 360f), 0), riverContainer);
                lake.transform.localScale = new Vector3(lakeScale, 1f, lakeScale);
            }
            forbiddenZones.Add(new Vector3(river.lakePos.x, lakeSurfY, river.lakePos.z));

            if (Time.realtimeSinceStartup - startTime > MAX_FRAME_TIME)
            { yield return null; startTime = Time.realtimeSinceStartup; }
        }
    }

    private List<(int from, int to)> BuildSegments(RiverSystem river)
    {
        var segs = new List<(int, int)>();
        int cur = 0;
        int last = river.path.Count - 1;

        foreach (WaterfallData wf in river.waterfalls)
        {
            if (wf.pathIndex > cur + 2)
                segs.Add((cur, wf.pathIndex));
            cur = wf.pathIndex + 3;
            if (cur > last) cur = last;
        }
        if (cur < last - 1)
            segs.Add((cur, last));
        return segs;
    }

    private Vector3 GetPathDir(List<Vector3> path, int from, int to)
    {
        to = Mathf.Min(to, path.Count - 1);
        Vector3 d = path[to] - path[from]; d.y = 0;
        return d.sqrMagnitude > 0.001f ? d.normalized : Vector3.forward;
    }

    private void SpawnSourceRocks(Vector3 pos, Vector3 forward, Transform parent)
    {
        if (cliffPrefabs == null || cliffPrefabs.Length == 0) return;
        Vector3 right = Vector3.Cross(Vector3.up, forward).normalized;
        Quaternion rot = Quaternion.LookRotation(forward);

        void PlaceRock(Vector3 offset, float scaleMul)
        {
            Vector3 p = pos + offset;
            p.y = terrain.SampleHeight(p) + transform.position.y - 0.3f;
            GameObject r = Instantiate(GetRandomPrefab(cliffPrefabs), p,
                rot * Quaternion.Euler(0, GetRandomRange(-30f, 30f), 0), parent);
            r.transform.localScale *= scaleMul;
        }

        PlaceRock(-forward * 2.5f + Vector3.up * 0.3f, GetRandomRange(0.9f, 1.3f));
        PlaceRock(right * (riverWidth * 0.6f), GetRandomRange(0.7f, 1.0f));
        PlaceRock(-right * (riverWidth * 0.6f), GetRandomRange(0.7f, 1.0f));
    }

    private void BuildSplineSegment(List<Vector3> fullPath, int fromIdx, int toIdx,
        Transform container, ref int index, float absWaterH, float dynamicWaterDepth)
    {
        int count = toIdx - fromIdx + 1;
        if (count < 3 || splineRiverMaterial == null) return;

        var wpts = new List<Vector3>(count);
        for (int i = fromIdx; i <= toIdx; i++)
        {
            Vector3 pt = fullPath[i];
            float groundY = terrain.SampleHeight(pt) + transform.position.y;
            float waterY = Mathf.Max(absWaterH + 0.04f, groundY + dynamicWaterDepth);
            wpts.Add(new Vector3(pt.x, waterY, pt.z));
        }

        SmoothSurfaceY(wpts, absWaterH);
        if (wpts.Count < 2) return;

        GameObject obj = new GameObject($"RiverMesh_{index++}");
        obj.transform.SetParent(container);
        obj.transform.position = wpts[0];
        obj.AddComponent<MeshFilter>();
        obj.AddComponent<MeshRenderer>();

        SplineContainer sc = obj.AddComponent<SplineContainer>();
        Spline spline = sc.Spline;

        for (int i = 0; i < wpts.Count; i++)
        {
            Vector3 pt = wpts[i];
            Vector3 fwd = i < wpts.Count - 1 ? wpts[i + 1] - pt : pt - wpts[i - 1];
            fwd.y = 0f;
            if (fwd.sqrMagnitude < 0.0001f) fwd = Vector3.forward;
            fwd.Normalize();

            Quaternion rot = Quaternion.LookRotation(fwd, Vector3.up);
            Vector3 local = obj.transform.InverseTransformPoint(pt);
            quaternion mathRot = new quaternion(rot.x, rot.y, rot.z, rot.w);

            spline.Add(new BezierKnot(new float3(local.x, local.y, local.z), default, default, mathRot));
            spline.SetTangentMode(i, TangentMode.AutoSmooth);
        }

        SplineRiverGenerator gen = obj.AddComponent<SplineRiverGenerator>();
        gen.splineContainer = sc;
        gen.material = splineRiverMaterial;
        gen.width = riverWidth * waterMeshWidthMultiplier;
        gen.traceForTerrain = false;
        gen.tileLength = 3f;
        gen.GenerateMesh(1f);

        if (riverRockPrefabs != null && riverRockPrefabs.Length > 0)
        {
            for (int i = 1; i < wpts.Count - 1; i += 5)
            {
                if (GetRandomFloat() > 0.6f) continue;
                Vector3 fwdR = wpts[i + 1] - wpts[i - 1]; fwdR.y = 0; fwdR.Normalize();
                Vector3 right = Vector3.Cross(Vector3.up, fwdR);
                float side = GetRandomFloat() > 0.5f ? 1f : -1f;
                float bankOfs = riverWidth * 0.55f + GetRandomRange(0.4f, 2f);
                Vector3 rPos = wpts[i] + right * side * bankOfs;
                rPos.y = terrain.SampleHeight(rPos) + transform.position.y - 0.1f;

                GameObject rock = Instantiate(GetRandomPrefab(riverRockPrefabs),
                    rPos,
                    Quaternion.Euler(GetRandomRange(-15f, 15f), GetRandomRange(0, 360), GetRandomRange(-10f, 10f)),
                    container);
                rock.transform.localScale *= GetRandomRange(0.4f, 1.2f);
                Color rc = GetTemperature(rPos.x / terrain.terrainData.size.x,
                    rPos.z / terrain.terrainData.size.z) >= 0.65f ? desertRockColor : forestRockColor;
                ApplyBiomeColor(rock, rc, true);
            }
        }

        forbiddenZones.Add(wpts[wpts.Count / 2]);
    }

    private void SpawnWaterfallFeature(WaterfallData wf, float absWaterH,
        float dynamicWaterDepth, Transform parent)
    {
        float topGroundY = terrain.SampleHeight(wf.topPos) + transform.position.y;
        float topEdgeY = topGroundY + dynamicWaterDepth;

        float bottomGroundY = terrain.SampleHeight(wf.bottomPos) + transform.position.y;
        float bottomPoolY = Mathf.Max(absWaterH + 0.05f, bottomGroundY + dynamicWaterDepth);

        float drop = Mathf.Max(1f, topEdgeY - bottomPoolY);

        Quaternion baseRot = Quaternion.LookRotation(wf.flowDir, Vector3.up);
        Quaternion rot = baseRot * Quaternion.Euler(waterfallPitchOffset, waterfallYawOffset, 0f);
        Vector3 right = Vector3.Cross(Vector3.up, wf.flowDir).normalized;

        Vector3 topEdge = new Vector3(wf.topPos.x, topEdgeY, wf.topPos.z);
        Vector3 bottomPool = new Vector3(wf.bottomPos.x, bottomPoolY, wf.bottomPos.z);

        if (cliffPrefabs != null && cliffPrefabs.Length > 0)
        {
            Vector3 wallCenter = topEdge + wf.flowDir * 0.5f - Vector3.up * (drop * 0.5f);
            GameObject backWall = Instantiate(GetRandomPrefab(cliffPrefabs), wallCenter, baseRot, parent);
            float wallH = Mathf.Clamp(drop * 0.6f, 2f, 8f);
            backWall.transform.localScale = new Vector3(
                Mathf.Max(2f, riverWidth * 0.5f), wallH, Mathf.Max(2f, riverWidth * 0.4f));
            Color wc = GetTemperature(wallCenter.x / terrain.terrainData.size.x,
                wallCenter.z / terrain.terrainData.size.z) >= 0.65f ? desertRockColor : forestRockColor;
            ApplyBiomeColor(backWall, wc, true);

            float sideOfs = Mathf.Max(riverWidth * 0.7f, 3.5f);
            for (int side = -1; side <= 1; side += 2)
            {
                Vector3 sidePos = topEdge + right * side * sideOfs - Vector3.up * (drop * 0.45f);
                float groundAtSide = terrain.SampleHeight(sidePos) + transform.position.y;
                sidePos.y = Mathf.Max(sidePos.y, groundAtSide - 1f);
                GameObject sideCliff = Instantiate(GetRandomPrefab(cliffPrefabs), sidePos, baseRot, parent);
                float cs = Mathf.Clamp(drop * 0.5f, 1.5f, 6f);
                sideCliff.transform.localScale = new Vector3(cs * 0.7f, cs, cs * 0.7f);
                Color rc = GetTemperature(sidePos.x / terrain.terrainData.size.x,
                    sidePos.z / terrain.terrainData.size.z) >= 0.65f ? desertRockColor : forestRockColor;
                ApplyBiomeColor(sideCliff, rc, true);
            }
        }

        if (riverWaterfallPrefabs != null && riverWaterfallPrefabs.Length > 0)
        {
            Vector3 vfxPos = topEdge + wf.flowDir * 0.2f;
            GameObject wfGO = Instantiate(GetRandomPrefab(riverWaterfallPrefabs), vfxPos, rot, parent);
            float wScale = Mathf.Clamp(riverWidth / 6f, 0.5f, 2.5f);
            float hScale = Mathf.Clamp(drop / 4f, 0.5f, 6f);
            wfGO.transform.localScale = new Vector3(wScale, hScale, wScale);
        }

        if (riverFoamTopPrefabs != null && riverFoamTopPrefabs.Length > 0)
            Instantiate(GetRandomPrefab(riverFoamTopPrefabs),
                topEdge + wf.flowDir * 0.1f, rot, parent);

        if (riverFoamBottomPrefabs != null && riverFoamBottomPrefabs.Length > 0)
            Instantiate(GetRandomPrefab(riverFoamBottomPrefabs),
                bottomPool + Vector3.up * 0.05f,
                Quaternion.Euler(0, rot.eulerAngles.y, 0), parent);

        if (spawnWaterfallBasin && riverLakePrefab != null)
        {
            float basinScale = Mathf.Max(0.6f, riverBankWidth / 10f);
            GameObject basin = Instantiate(riverLakePrefab, bottomPool,
                Quaternion.Euler(0, GetRandomRange(0, 360f), 0), parent);
            basin.transform.localScale = new Vector3(basinScale, 1f, basinScale);
        }
    }

    private void SmoothSurfaceY(List<Vector3> pts, float minY)
    {
        if (pts.Count < 3) return;
        for (int pass = 0; pass < 3; pass++)
            for (int i = 1; i < pts.Count - 1; i++)
            {
                float avg = (pts[i - 1].y + pts[i].y + pts[i + 1].y) / 3f;
                pts[i] = new Vector3(pts[i].x, Mathf.Max(minY, avg), pts[i].z);
            }

        for (int i = 1; i < pts.Count; i++)
            if (pts[i].y > pts[i - 1].y + 0.06f)
                pts[i] = new Vector3(pts[i].x, pts[i - 1].y, pts[i].z);
    }

    private void SmoothWaterSurfaceY(List<Vector3> pts) => SmoothSurfaceY(pts, 0f);

    private void BuildSplineSegment(List<Vector3> points, Transform container,
        ref int index, float absWaterHeight, bool isAfterWaterfall)
    { }

    private IEnumerator GenerateHeightsRoutine(TerrainData terrainData)
    {
        int width = terrainData.heightmapResolution; int height = terrainData.heightmapResolution;
        float[,] heights = new float[width, height]; float centerX = width / 2f; float centerY = height / 2f; float startTime = Time.realtimeSinceStartup;

        for (int x = 0; x < width; x++)
        {
            for (int y = 0; y < height; y++)
            {
                float amplitude = 1f; float frequency = 1f; float noiseHeight = 0f; float maxAmplitude = 0f;
                for (int i = 0; i < octaves; i++)
                {
                    float xCoord = (float)x / width * scale * frequency + offsetX; float yCoord = (float)y / height * scale * frequency + offsetZ;
                    float perlinValue = 1f - Mathf.Abs(Mathf.PerlinNoise(xCoord, yCoord) * 2f - 1f);
                    noiseHeight += (perlinValue * perlinValue) * amplitude; maxAmplitude += amplitude; amplitude *= persistence; frequency *= lacunarity;
                }
                float normalizedHeight = noiseHeight / maxAmplitude;
                if (terraceCount > 0) normalizedHeight = Mathf.Round(normalizedHeight * terraceCount) / terraceCount;
                float sharpenedNoise = Mathf.Pow(normalizedHeight, peakSharpness);
                float distFromCenter = Vector2.Distance(new Vector2(x, y), new Vector2(centerX, centerY));
                float edgeWall = Mathf.Pow(distFromCenter / centerX, 4f) * edgeMountainMultiplier;
                float finalHeight = Mathf.Clamp01(sharpenedNoise + edgeWall);
                if (finalHeight < waterLevel) finalHeight = Mathf.Lerp(finalHeight, waterLevel * 0.8f, 0.5f);
                heights[x, y] = finalHeight;
            }
            if (Time.realtimeSinceStartup - startTime > MAX_FRAME_TIME) { yield return null; startTime = Time.realtimeSinceStartup; }
        }
        terrainData.SetHeights(0, 0, heights); terrainData.size = new Vector3(terrainData.size.x, depth, terrainData.size.z);
    }

    private IEnumerator PaintTerrainRoutine(TerrainData terrainData)
    {
        if (grassLayer == null || sandLayer == null || snowLayer == null || rockLayer == null || roadLayer == null) yield break;

        terrainData.terrainLayers = new TerrainLayer[] { grassLayer, sandLayer, snowLayer, rockLayer, roadLayer };
        int aWidth = terrainData.alphamapWidth; int aHeight = terrainData.alphamapHeight;

        // ФІКС: Правильний порядок масиву для Unity Terrain (Висота, Ширина, Шари)
        float[,,] splatmapData = new float[aHeight, aWidth, 5];

        float startTime = Time.realtimeSinceStartup;

        // One reusable weights buffer for the whole paint pass — the old code
        // allocated a new float[5] for every alphamap cell (~262k allocations on
        // a 512² map), which churned the GC during generation.
        float[] weights = new float[5];
        for (int y = 0; y < aHeight; y++)
        {
            for (int x = 0; x < aWidth; x++)
            {
                float temp = GetTemperature((float)x / aWidth, (float)y / aHeight);
                float steepness = terrainData.GetSteepness((float)x / aWidth, (float)y / aHeight);
                float normalizedHeight = terrainData.GetHeight(y, x) / depth;
                weights[0] = weights[1] = weights[2] = weights[3] = weights[4] = 0f;

                if (normalizedHeight > 0.65f) weights[2] = 1f;
                else if (normalizedHeight <= waterLevel + 0.02f) weights[1] = 1f;
                else { if (temp >= 0.65f) weights[1] = 1f; else if (temp <= 0.35f) weights[2] = 1f; else weights[0] = 1f; }

                weights[3] = Mathf.Clamp01(Mathf.InverseLerp(30f, 45f, steepness));
                float remainAfterRock = 1f - weights[3];
                weights[0] *= remainAfterRock; weights[1] *= remainAfterRock; weights[2] *= remainAfterRock;

                float roadBlend = roadBlendMap != null ? roadBlendMap[y, x] : 0f;
                if (roadBlend > 0.01f)
                {
                    weights[4] = roadBlend;
                    float remainAfterRoad = 1f - roadBlend;
                    if (remainAfterRoad < 0) remainAfterRoad = 0;
                    weights[0] *= remainAfterRoad; weights[1] *= remainAfterRoad; weights[2] *= remainAfterRoad; weights[3] *= remainAfterRoad;
                }

                splatmapData[y, x, 0] = weights[0]; splatmapData[y, x, 1] = weights[1]; splatmapData[y, x, 2] = weights[2]; splatmapData[y, x, 3] = weights[3]; splatmapData[y, x, 4] = weights[4];
            }
            if (Time.realtimeSinceStartup - startTime > MAX_FRAME_TIME) { yield return null; startTime = Time.realtimeSinceStartup; }
        }

        foreach (var river in generatedRivers)
        {
            float riverSandRadius = riverBankWidth * riverSandWidthMul;
            foreach (var pt in river.path) PaintSandCircle(splatmapData, terrainData, aWidth, aHeight, pt, riverSandRadius);
            if (river.lakePos != Vector3.zero) PaintSandCircle(splatmapData, terrainData, aWidth, aHeight, river.lakePos, lakeRadius * lakeSandRadiusMul);
            foreach (var wf in river.waterfalls) PaintSandCircle(splatmapData, terrainData, aWidth, aHeight, wf.bottomPos, riverBankWidth * lakeSandRadiusMul);
        }

        terrainData.SetAlphamaps(0, 0, splatmapData);
    }

    private IEnumerator GenerateDetailsRoutine()
    {
        TerrainData td = terrain.terrainData;
        int dRes = td.detailResolution;
        int layers = td.detailPrototypes.Length;

        if (layers == 0) yield break;

        // ФІКС 1: Встановлюємо правильні значення прогресу (генерація трави йде від 50% до 60%)
        float startProgress = 0.50f;
        float endProgress = 0.60f;

        List<int[,]> detailMaps = new List<int[,]>();
        for (int i = 0; i < layers; i++) detailMaps.Add(new int[dRes, dRes]);

        float startTime = Time.realtimeSinceStartup;

        for (int y = 0; y < dRes; y++)
        {
            CurrentProgress = startProgress + (endProgress - startProgress) * ((float)y / dRes);

            for (int x = 0; x < dRes; x++)
            {
                float normX = (float)x / dRes;
                float normZ = (float)y / dRes;

                float steepness = td.GetSteepness(normX, normZ);
                float normHeight = td.GetInterpolatedHeight(normX, normZ) / depth;

                if (steepness > 45f || normHeight <= waterLevel + 0.02f) continue;

                // --- ФІКС: Забороняємо траві рости на дорогах! ---
                int ax = Mathf.Clamp(Mathf.RoundToInt(normX * td.alphamapWidth), 0, td.alphamapWidth - 1);
                int ay = Mathf.Clamp(Mathf.RoundToInt(normZ * td.alphamapHeight), 0, td.alphamapHeight - 1);
                if (roadBlendMap != null && roadBlendMap[ay, ax] > 0.1f) continue;
                // -------------------------------------------------

                float temp = GetTemperature(normX, normZ);
                bool isSnowBiome = false;
                bool isDesertBiome = false;
                bool isForestBiome = false;

                if (normHeight > 0.65f) isSnowBiome = true;
                else if (normHeight <= waterLevel + 0.02f) isDesertBiome = true;
                else { if (temp >= 0.65f) isDesertBiome = true; else if (temp <= 0.35f) isSnowBiome = true; else isForestBiome = true; }

                float baseMeadowNoise = Mathf.PerlinNoise(normX * meadowScale + offsetX, normZ * meadowScale + offsetZ);
                float densityNoise = Mathf.PerlinNoise(normX * clusterScale * 5f + offsetX, normZ * clusterScale * 5f + offsetZ);

                for (int layer = 0; layer < layers; layer++)
                {
                    if (layer == 0 && !isForestBiome) continue;
                    if (layer == 1 && !isDesertBiome) continue;
                    if (layer == 2 && !isSnowBiome) continue;
                    if (layer > 2 && isSnowBiome) continue;

                    int density = 0;

                    if (layer > 2)
                    {
                        float layerSeedX = offsetX + (layer * 137.55f);
                        float layerSeedZ = offsetZ + (layer * 211.31f);
                        float perLayerMeadow = Mathf.PerlinNoise(normX * meadowScale + layerSeedX, normZ * meadowScale + layerSeedZ);
                        float perLayerDensity = Mathf.PerlinNoise(normX * clusterScale * 5f + layerSeedX, normZ * clusterScale * 5f + layerSeedZ);

                        if (perLayerMeadow > 0.55f)
                            density = Mathf.RoundToInt(Mathf.Lerp(40f, 180f, perLayerDensity));
                    }
                    else
                    {
                        float layerMeadowNoise = (baseMeadowNoise + (layer * 0.15f)) % 1f;
                        density = Mathf.RoundToInt(Mathf.Lerp(150f, 255f, densityNoise));
                        if (layerMeadowNoise > 0.3f) density = 255;
                    }

                    detailMaps[layer][y, x] = density;
                }
            }
            if (Time.realtimeSinceStartup - startTime > MAX_FRAME_TIME) { yield return null; startTime = Time.realtimeSinceStartup; }
        }

        for (int layer = 0; layer < layers; layer++) { td.SetDetailLayer(0, 0, layer, detailMaps[layer]); yield return null; }
    }

    private IEnumerator SpawnRegionTotemRoutine()
    {
        GameObject totemPrefab = null;
        float locationYOffset = 0f;
        RegionData activeRegionData = null;

        if (GameManager.Instance != null && GameManager.Instance.currentRegion != null)
            activeRegionData = GameManager.Instance.currentRegion;
        else if (MissionInitializer.PendingMissionRegion != null)
            activeRegionData = MissionInitializer.PendingMissionRegion;

        if (activeRegionData != null)
        {
            totemPrefab = activeRegionData.regionTotemPrefab;
            locationYOffset = activeRegionData.locationYOffset;

            // FALLBACK: some newer region assets (R21-R24) ship with no
            // regionTotemPrefab wired. Silent yield-break was hiding
            // the misconfiguration — the level generated with no totem,
            // no conquest goal, and no clear-condition. Borrow the
            // prefab from any earlier region in the same
            // MapProgressionManager pool so the run at least completes.
            if (totemPrefab == null && MapProgressionManager.Instance != null
                && MapProgressionManager.Instance.allRegionsInGame != null)
            {
                foreach (var r in MapProgressionManager.Instance.allRegionsInGame)
                {
                    if (r != null && r.regionTotemPrefab != null)
                    {
                        Debug.LogWarning($"[WorldGenerator] Region '{activeRegionData.regionName}' (ID {activeRegionData.regionID}) has NO regionTotemPrefab wired — falling back to '{r.regionName}' totem. Wire the field on the RegionData asset to remove this warning.");
                        totemPrefab = r.regionTotemPrefab;
                        break;
                    }
                }
            }
        }

        float w = terrain.terrainData.size.x;
        float l = terrain.terrainData.size.z;
        float absoluteWaterHeight = transform.position.y + (depth * waterLevel);

        // ФІКС 1: Якщо Тотему немає, шукаємо БЕЗПЕЧНУ точку для центру доріг
        if (totemPrefab == null)
        {
            spawnedTotemPos = new Vector3(transform.position.x + w / 2, 0, transform.position.z + l / 2);
            for (int i = 0; i < 500; i++)
            {
                float px = GetRandomRange(w * 0.2f, w * 0.8f);
                float pz = GetRandomRange(l * 0.2f, l * 0.8f);
                float py = terrain.SampleHeight(new Vector3(transform.position.x + px, 0, transform.position.z + pz)) + transform.position.y;
                if (py > absoluteWaterHeight + 3f && terrain.terrainData.GetSteepness(px / w, pz / l) < 15f && !IsNearRiver(new Vector3(transform.position.x + px, py, transform.position.z + pz), 15f))
                {
                    spawnedTotemPos = new Vector3(transform.position.x + px, py, transform.position.z + pz);
                    break;
                }
            }
            roadTargets.Add(spawnedTotemPos);
            yield break;
        }

        BoxCollider rootBox = totemPrefab.GetComponent<BoxCollider>();
        float flatRadius = 40f;
        float bottomOfCollider = 0f;

        if (rootBox != null)
        {
            float sx = rootBox.size.x * totemPrefab.transform.localScale.x;
            float sz = rootBox.size.z * totemPrefab.transform.localScale.z;
            flatRadius = Mathf.Max(20f, Mathf.Sqrt(sx * sx + sz * sz) * 0.5f + 5f);
            float sy = rootBox.size.y * totemPrefab.transform.localScale.y;
            float cy = rootBox.center.y * totemPrefab.transform.localScale.y;
            bottomOfCollider = cy - (sy / 2f);
        }

        // The root box is often a small trigger (an activation zone), so the box
        // math above gave a tiny flatten radius — the terrain was leveled only in
        // a ~20m circle while a whole TOWN location extends much further, leaving
        // it perched on ungraded bumps. Measure the prefab's real mesh footprint
        // and flatten at least that far so the entire location sits on level
        // ground. Bottom of the mesh footprint also gives a better base Y than a
        // mis-sized trigger box.
        if (MeasurePrefabFootprint(totemPrefab, out float meshRadius, out float meshBottomY))
        {
            if (meshRadius + 8f > flatRadius) flatRadius = meshRadius + 8f;
            bottomOfCollider = meshBottomY;
        }

        Vector2 bestSpot = Vector2.zero;
        bool foundSpot = false;
        List<Vector2> validSpots = new List<Vector2>();
        float scanStep = 30f;
        // Keep the totem well clear of the map edge. Border mountains
        // spawn just outside the terrain and are scaled 3–6×, so they
        // reach tens of metres inward (collider-stripped, pure visual) —
        // a totem placed at the old ~50m margin got visually swallowed by
        // one ("totem at map edge inside a rock"). 120m of clearance from
        // every edge keeps it in open ground.
        float edgeMargin = flatRadius + 120f;
        // On a small map, flatRadius+120 can exceed half the map and the
        // scan loop below never runs → validSpots empty → fallback to the
        // map centre (which is always safe). Clamp so we still get a scan
        // band on normal-sized maps.
        edgeMargin = Mathf.Min(edgeMargin, Mathf.Min(w, l) * 0.4f);

        for (float x = edgeMargin; x < w - edgeMargin; x += scanStep)
        {
            for (float z = edgeMargin; z < l - edgeMargin; z += scanStep)
            {
                float pX = transform.position.x + x;
                float pZ = transform.position.z + z;
                float h = terrain.SampleHeight(new Vector3(pX, 0, pZ)) + transform.position.y;
                if (h <= absoluteWaterHeight + 4f) continue;
                float normX = x / w; float normZ = z / l;
                if (terrain.terrainData.GetSteepness(normX, normZ) > 15f) continue;

                float minH = float.MaxValue;
                float maxH = float.MinValue;
                bool touchesWater = false;
                // Dense footprint sampling: centre + two rings (0.5x and 1.0x
                // flatRadius) at 8 compass directions = 17 probes. The old 5
                // cardinal points missed a lake sitting UNDER a large location
                // (between or diagonal to them), so big locations could spawn
                // inside a carved lake. This covers the whole footprint disc.
                for (int ring = 0; ring <= 2; ring++)
                {
                    float r = ring == 0 ? 0f : (ring == 1 ? flatRadius * 0.5f : flatRadius);
                    int dirs = ring == 0 ? 1 : 8;
                    for (int d = 0; d < dirs; d++)
                    {
                        float ang = d * Mathf.PI * 2f / 8f;
                        Vector3 pt = new Vector3(pX + Mathf.Cos(ang) * r, 0f, pZ + Mathf.Sin(ang) * r);
                        float ch = terrain.SampleHeight(pt) + transform.position.y;
                        if (ch <= absoluteWaterHeight + 1.5f) touchesWater = true;
                        if (ch > maxH) maxH = ch;
                        if (ch < minH) minH = ch;
                    }
                }

                if (!touchesWater && (maxH - minH) <= 12f)
                {
                    if (IsSummerZone(normX, normZ, minH) && !IsNearRiver(new Vector3(pX, minH, pZ), flatRadius + 15f))
                        validSpots.Add(new Vector2(pX, pZ));
                }
            }
        }

        if (validSpots.Count > 0)
        {
            bestSpot = validSpots[UnityEngine.Random.Range(0, validSpots.Count)];
            foundSpot = true;
        }
        else bestSpot = new Vector2(transform.position.x + w / 2, transform.position.z + l / 2);

        Vector3 centerPos = new Vector3(bestSpot.x, 0, bestSpot.y);
        float exactGroundY = terrain.SampleHeight(centerPos) + transform.position.y;
        centerPos.y = exactGroundY;

        FlattenTerrainRobust(centerPos, flatRadius, 18f, exactGroundY);
        terrain.Flush();
        TerrainCollider tc = terrain.GetComponent<TerrainCollider>();
        if (tc != null) { tc.enabled = false; tc.enabled = true; }

        yield return new WaitForFixedUpdate();
        yield return new WaitForFixedUpdate();

        // Re-sample the terrain height AT the totem's XZ AFTER the flatten
        // + Flush + collider rebuild. Using the pre-flatten `exactGroundY`
        // was the intermittent air-spawn cause: FlattenTerrainRobust can
        // settle the pad a fraction of a metre off its target (heightmap
        // quantisation + neighbour blending), so the pre-flatten sample no
        // longer matches the real ground the totem sits on. Post-flatten
        // sampling grounds it every time.
        float groundYAfterFlatten = terrain.SampleHeight(centerPos) + transform.position.y;
        float finalY = groundYAfterFlatten - bottomOfCollider + locationYOffset;
        Vector3 finalSpawnPos = new Vector3(centerPos.x, finalY, centerPos.z);
        Quaternion randomRot = Quaternion.Euler(0, GetRandomRange(0f, 360f), 0);
        Quaternion finalRot = randomRot * totemPrefab.transform.rotation;

        GameObject camp = Instantiate(totemPrefab, finalSpawnPos, finalRot);
        camp.transform.SetParent(this.transform);

        // Belt-and-braces: re-snap the INSTANTIATED totem by its real
        // combined renderer bounds. The bottomOfCollider math only knows
        // about a root BoxCollider; a totem whose collider/mesh is nested
        // under a child still floated. Measuring the live instance's
        // bounds grounds it regardless of hierarchy.
        SnapInstanceToGround(camp, groundYAfterFlatten + locationYOffset);

        spawnedTotemPos = camp.transform.position;
        forbiddenZones.Add(spawnedTotemPos);
        roadTargets.Add(spawnedTotemPos);

        // Optional extra capture LOCATIONS: additional totems at spread-out
        // clearings so a region has several points to capture (bonus side
        // objectives). Default 0 → nothing extra spawns, behaviour unchanged.
        int extra = activeRegionData != null ? activeRegionData.extraCaptureLocations : 0;
        if (extra > 0 && validSpots.Count > 1)
            yield return StartCoroutine(SpawnExtraCaptureTotems(totemPrefab, validSpots, bestSpot, extra, flatRadius, bottomOfCollider, locationYOffset));
    }

    // Places `count` additional capture totems at valid clearings that are well
    // separated from the main totem and from each other, grounding each the same
    // way as the main one and flagging them standalone (bonus objectives).
    private IEnumerator SpawnExtraCaptureTotems(GameObject totemPrefab, List<Vector2> validSpots, Vector2 mainSpot, int count, float flatRadius, float bottomOfCollider, float locationYOffset)
    {
        List<Vector2> placed = new List<Vector2> { mainSpot };
        float minSep = flatRadius * 2f + 90f;
        int spawned = 0;

        for (int attempt = 0; attempt < validSpots.Count * 2 && spawned < count; attempt++)
        {
            Vector2 cand = validSpots[UnityEngine.Random.Range(0, validSpots.Count)];
            bool ok = true;
            foreach (var p in placed) if (Vector2.Distance(cand, p) < minSep) { ok = false; break; }
            if (!ok) continue;
            placed.Add(cand);

            Vector3 centerPos = new Vector3(cand.x, 0, cand.y);
            float gY = terrain.SampleHeight(centerPos) + transform.position.y;
            centerPos.y = gY;

            FlattenTerrainRobust(centerPos, flatRadius, 18f, gY);
            terrain.Flush();
            TerrainCollider tc = terrain.GetComponent<TerrainCollider>();
            if (tc != null) { tc.enabled = false; tc.enabled = true; }
            yield return new WaitForFixedUpdate();

            float gY2 = terrain.SampleHeight(centerPos) + transform.position.y;
            float finalY = gY2 - bottomOfCollider + locationYOffset;
            Vector3 finalPos = new Vector3(centerPos.x, finalY, centerPos.z);
            Quaternion rot = Quaternion.Euler(0, GetRandomRange(0f, 360f), 0) * totemPrefab.transform.rotation;

            GameObject extraTotem = Instantiate(totemPrefab, finalPos, rot);
            extraTotem.transform.SetParent(this.transform);
            SnapInstanceToGround(extraTotem, gY2 + locationYOffset);

            RegionTotem rt = extraTotem.GetComponent<RegionTotem>();
            if (rt != null) rt.isStandalone = true; // bonus capture point, not a region gate

            forbiddenZones.Add(extraTotem.transform.position);
            roadTargets.Add(extraTotem.transform.position);
            spawned++;
        }
    }

    // Snap an instantiated object so its FLOOR sits at `targetGroundY`.
    //
    // Grounding priority:
    //   1. A root BoxCollider — the designer-defined footprint/floor. This
    //      is the correct reference for a LOCATION prefab: decorative trees
    //      and props inside it often hang well below the floor, and using
    //      the absolute-lowest mesh point would ground on THAT and lift the
    //      whole location into the air (the reported "location spawns
    //      slightly in the air / crooked alignment" bug). The collider
    //      ignores stray decoration.
    //   2. Fall back to the lowest ACTIVE, enabled MeshRenderer /
    //      SkinnedMeshRenderer only when there's no root BoxCollider.
    //      (Inactive renderers carry stale/origin bounds; particle
    //      renderers glow below the base — both are excluded.)
    // Measures a prefab's combined MESH footprint (not its collider box):
    //   radius  = half the larger XZ extent, in world units,
    //   bottomY = the lowest mesh point relative to the root pivot, world units.
    // Used to flatten a large enough pad and to place a location by its real
    // base when the root collider is a small/mis-sized trigger.
    private bool MeasurePrefabFootprint(GameObject prefab, out float radius, out float bottomY)
    {
        radius = 0f; bottomY = 0f;
        if (prefab == null) return false;

        Matrix4x4 w2l = prefab.transform.worldToLocalMatrix;
        Bounds b = new Bounds();
        bool has = false;

        void Accumulate(Mesh mesh, Transform t)
        {
            if (mesh == null || t == null) return;
            Matrix4x4 m = w2l * t.localToWorldMatrix;   // mesh-local → prefab-root-local
            Vector3 c = mesh.bounds.center, e = mesh.bounds.extents;
            for (int i = 0; i < 8; i++)
            {
                Vector3 corner = c + new Vector3((i & 1) == 0 ? -e.x : e.x,
                                                 (i & 2) == 0 ? -e.y : e.y,
                                                 (i & 4) == 0 ? -e.z : e.z);
                Vector3 p = m.MultiplyPoint3x4(corner);
                if (!has) { b = new Bounds(p, Vector3.zero); has = true; } else b.Encapsulate(p);
            }
        }

        foreach (var mf in prefab.GetComponentsInChildren<MeshFilter>(true))
            if (mf != null) Accumulate(mf.sharedMesh, mf.transform);
        foreach (var smr in prefab.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            if (smr != null) Accumulate(smr.sharedMesh, smr.transform);

        if (!has) return false;

        Vector3 s = prefab.transform.localScale;
        radius = Mathf.Max(b.size.x * Mathf.Abs(s.x), b.size.z * Mathf.Abs(s.z)) * 0.5f;
        bottomY = b.min.y * s.y;
        return true;
    }

    private void SnapInstanceToGround(GameObject go, float targetGroundY)
    {
        if (go == null) return;

        // Ground so the model's PHYSICAL FOOTPRINT sits on the terrain. We take
        // the lowest point across BOTH the colliders (what the player stands on)
        // and the visible meshes, then snap that to the ground. Using colliders
        // alone floated prefabs whose interaction box dips below the base;
        // using renderers alone sank prefabs that have a mesh below their base.
        // The min of the two lands the true bottom on the ground for both.
        // Per-region fine-tuning is still available via RegionData.locationYOffset.
        // Track the STRUCTURE's base separately from everything, so geometry
        // deliberately sunk below grade — water-mill wheels dipping into a river,
        // trees/foliage planted deep in the soil — doesn't drag the snap point
        // down and lift the whole location into the air (the region-24 castle bug).
        float structuralLow = float.MaxValue;
        float allLow = float.MaxValue;
        bool any = false;

        foreach (var col in go.GetComponentsInChildren<Collider>(false))
        {
            if (col == null || col.isTrigger) continue;      // triggers aren't footing
            float b = col.bounds.min.y;
            allLow = Mathf.Min(allLow, b);
            if (!IsBelowGradeDecor(col.transform, go.transform)) structuralLow = Mathf.Min(structuralLow, b);
            any = true;
        }
        foreach (var mr in go.GetComponentsInChildren<MeshRenderer>(false))
        {
            if (mr == null || !mr.enabled) continue;
            float b = mr.bounds.min.y;
            allLow = Mathf.Min(allLow, b);
            if (!IsBelowGradeDecor(mr.transform, go.transform)) structuralLow = Mathf.Min(structuralLow, b);
            any = true;
        }
        foreach (var smr in go.GetComponentsInChildren<SkinnedMeshRenderer>(false))
        {
            if (smr == null || !smr.enabled) continue;
            float b = smr.bounds.min.y;
            allLow = Mathf.Min(allLow, b);
            if (!IsBelowGradeDecor(smr.transform, go.transform)) structuralLow = Mathf.Min(structuralLow, b);
            any = true;
        }

        // Prefer the structural base; fall back to the true lowest if a prefab is
        // entirely decor-named (so we never leave it un-snapped).
        float lowestY = structuralLow < float.MaxValue ? structuralLow : allLow;

        // Last-resort fallback to a root box (even a trigger one).
        if (!any)
        {
            BoxCollider rootBox = go.GetComponent<BoxCollider>();
            if (rootBox == null) return;
            lowestY = rootBox.bounds.min.y;
        }

        // Small downward embed so the base sits FLUSH with (very slightly into)
        // the terrain rather than perched a hair above it. On uneven ground the
        // exact bottom sample almost never matches every point under a wide base,
        // which read as "floating slightly in the air"; sinking the footprint a
        // touch hides that gap and looks natural (props are normally bedded in).
        const float groundEmbed = 0.35f;
        float delta = targetGroundY - lowestY - groundEmbed;
        go.transform.position += new Vector3(0f, delta, 0f);
    }

    // Returns how far the prefab's pivot sits above the lowest point of its
    // SOLID mesh geometry (MeshRenderer + SkinnedMeshRenderer only). Particle
    // renderers are ignored so decorative waterfalls/mist can't skew the base.
    private bool TryGetSolidBaseOffset(GameObject root, out float pivotToBase)
    {
        pivotToBase = 0f;
        float lowest = float.MaxValue;
        bool any = false;

        MeshRenderer[] meshes = root.GetComponentsInChildren<MeshRenderer>(false);
        for (int i = 0; i < meshes.Length; i++)
        {
            if (meshes[i] == null) continue;
            lowest = Mathf.Min(lowest, meshes[i].bounds.min.y);
            any = true;
        }
        SkinnedMeshRenderer[] skinned = root.GetComponentsInChildren<SkinnedMeshRenderer>(false);
        for (int i = 0; i < skinned.Length; i++)
        {
            if (skinned[i] == null) continue;
            lowest = Mathf.Min(lowest, skinned[i].bounds.min.y);
            any = true;
        }

        if (!any) return false;
        pivotToBase = root.transform.position.y - lowest;
        return true;
    }

    // True if worldPos is within radiusMeters of any carved river path point
    // or waterfall pool. Used to keep the region arena off rivers so the
    // flatten pad doesn't raise terrain under an already-placed water plane.
    private bool IsNearRiver(Vector3 worldPos, float radiusMeters)
    {
        if (generatedRivers == null || generatedRivers.Count == 0) return false;
        float rSqr = radiusMeters * radiusMeters;
        Vector2 p = new Vector2(worldPos.x, worldPos.z);
        foreach (RiverSystem river in generatedRivers)
        {
            for (int i = 0; i < river.path.Count; i++)
            {
                Vector2 rp = new Vector2(river.path[i].x, river.path[i].z);
                if ((p - rp).sqrMagnitude <= rSqr) return true;
            }
            Vector2 lp = new Vector2(river.lakePos.x, river.lakePos.z);
            if ((p - lp).sqrMagnitude <= rSqr) return true;
        }
        return false;
    }

    // Mirrors PaintTerrainRoutine's biome classifier so the region location
    // only lands on cells that get painted as grass ("summer" green). Purely a
    // filter — does not modify terrain or prefab position.
    private bool IsSummerZone(float normX, float normZ, float worldY)
    {
        float normalizedHeight = (worldY - transform.position.y) / depth;
        if (normalizedHeight > 0.65f) return false;                   // snow / mountaintop
        if (normalizedHeight <= waterLevel + 0.02f) return false;     // sandy shoreline

        float temp = GetTemperature(normX, normZ);
        if (temp >= 0.65f) return false;                              // desert
        if (temp <= 0.35f) return false;                              // snow biome

        float steepness = terrain.terrainData.GetSteepness(normX, normZ);
        if (steepness > 25f) return false;                            // painted rock

        return true;
    }

    private void FlattenTerrainAt(Vector3 worldPos, float flatRadius, float blendRadius)
    {
        TerrainData td = terrain.terrainData; int hRes = td.heightmapResolution;
        float normX = (worldPos.x - transform.position.x) / td.size.x; float normZ = (worldPos.z - transform.position.z) / td.size.z;
        int centerX = Mathf.RoundToInt(normX * (hRes - 1)); int centerZ = Mathf.RoundToInt(normZ * (hRes - 1));
        int flatRadiusSamples = Mathf.RoundToInt((flatRadius / td.size.x) * hRes); int blendRadiusSamples = Mathf.RoundToInt((blendRadius / td.size.x) * hRes);
        int totalRadiusSamples = flatRadiusSamples + blendRadiusSamples;

        int startX = Mathf.Clamp(centerX - totalRadiusSamples, 0, hRes - 1); int endX = Mathf.Clamp(centerX + totalRadiusSamples, 0, hRes - 1);
        int startZ = Mathf.Clamp(centerZ - totalRadiusSamples, 0, hRes - 1); int endZ = Mathf.Clamp(centerZ + totalRadiusSamples, 0, hRes - 1);

        int width = endX - startX + 1; int length = endZ - startZ + 1;
        float[,] heights = td.GetHeights(startX, startZ, width, length);
        float targetHeightNorm = (worldPos.y - transform.position.y) / td.size.y;

        for (int z = 0; z < length; z++)
        {
            for (int x = 0; x < width; x++)
            {
                float dist = Vector2.Distance(new Vector2(startX + x, startZ + z), new Vector2(centerX, centerZ));
                if (dist <= flatRadiusSamples) heights[z, x] = targetHeightNorm;
                else if (dist <= totalRadiusSamples)
                {
                    float t = (dist - flatRadiusSamples) / blendRadiusSamples; float smoothT = t * t * (3f - 2f * t);
                    heights[z, x] = Mathf.Lerp(targetHeightNorm, heights[z, x], smoothT);
                }
            }
        }
        td.SetHeights(startX, startZ, heights);
    }

    private void AdjustSettingsForBiome()
    {
        if (isRegionMissionCached)
        {
            terraceCount = 0;
            if (regionBiomeTypeCached == 1) { peakSharpness = 2.2f; edgeMountainMultiplier = 3.0f; }
            else if (regionBiomeTypeCached == 2) { peakSharpness = 3.5f; edgeMountainMultiplier = 3.5f; }
            else { peakSharpness = 3.0f; edgeMountainMultiplier = 3.0f; }
        }
    }

    private void SpawnWaterPlane()
    {
        if (waterMaterial == null) return;
        float w = terrain.terrainData.size.x; float l = terrain.terrainData.size.z; float absoluteWaterHeight = transform.position.y + (depth * waterLevel);
        GameObject waterObj = GameObject.CreatePrimitive(PrimitiveType.Plane);
        waterObj.name = "Bitgem_WaterPlane"; waterObj.transform.SetParent(this.transform);
        waterObj.transform.position = new Vector3(transform.position.x + w / 2, absoluteWaterHeight, transform.position.z + l / 2);
        waterObj.transform.localScale = new Vector3(w / 10f, 1f, l / 10f);
        MeshRenderer mr = waterObj.GetComponent<MeshRenderer>(); mr.material = waterMaterial; mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        Destroy(waterObj.GetComponent<Collider>());
    }

    private IEnumerator GenerateRoadsRoutine()
    {
        if (roadLayer == null)
        {
            Debug.LogError("[Smart Roads] ПОМИЛКА: Road Layer не призначено в Інспекторі! Дороги скасовано.");
            yield break;
        }
        if (spawnedTotemPos == Vector3.zero)
        {
            Debug.LogWarning("[Smart Roads] Центр магістралі не знайдено, скасовуємо дороги.");
            yield break;
        }

        Transform roadContainer = new GameObject("RoadsContainer").transform;
        roadContainer.SetParent(this.transform);

        float mapW = terrain.terrainData.size.x; float mapL = terrain.terrainData.size.z;
        float absWaterH = transform.position.y + (depth * waterLevel);

        float deadEndMinSepSq = deadEndMinSeparation * deadEndMinSeparation;
        for (int i = 0; i < extraDeadEndRoads; i++)
        {
            bool placed = false;
            for (int attempt = 0; attempt < 300; attempt++)
            {
                float px = GetRandomRange(50f, mapW - 50f); float pz = GetRandomRange(50f, mapL - 50f);
                float wX = transform.position.x + px; float wZ = transform.position.z + pz;
                float wY = terrain.SampleHeight(new Vector3(wX, 0, wZ)) + transform.position.y;
                Vector3 candidate = new Vector3(wX, wY, wZ);

                // Тупик має бути далеко від бази (> 70м), не крутий, над водою,
                // і достатньо далеко від інших тупиків, щоб не спавнилися пари.
                if (wY <= absWaterH + 5f) continue;
                if (terrain.terrainData.GetSteepness(px / mapW, pz / mapL) >= deadEndMaxSteepness) continue;
                if (Vector3.Distance(candidate, spawnedTotemPos) <= 70f) continue;

                bool tooClose = false;
                for (int k = 0; k < deadEndTargets.Count; k++)
                {
                    Vector3 diff = candidate - deadEndTargets[k]; diff.y = 0f;
                    if (diff.sqrMagnitude < deadEndMinSepSq) { tooClose = true; break; }
                }
                if (tooClose) continue;

                deadEndTargets.Add(candidate);
                placed = true;
                break;
            }
            if (!placed) Debug.LogWarning($"[Smart Roads] Не знайшов місце для тупика #{i + 1} за 300 спроб (steep<{deadEndMaxSteepness}°, водоймá, {deadEndMinSeparation}м від сусідів). Мапа задуже пересічена?");
        }

        List<Vector3> allTargets = new List<Vector3>(roadTargets);
        allTargets.AddRange(deadEndTargets);
        GameLog.Info($"[Smart Roads] Починаємо генерацію. Цільових точок: {allTargets.Count}");

        float cellSize = 8f;
        int gridW = Mathf.CeilToInt(mapW / cellSize);
        int gridL = Mathf.CeilToInt(mapL / cellSize);

        bool[,] roadNetwork = new bool[gridW, gridL];
        Vector2Int totemGrid = new Vector2Int(
            Mathf.Clamp(Mathf.RoundToInt((spawnedTotemPos.x - transform.position.x) / cellSize), 0, gridW - 1),
            Mathf.Clamp(Mathf.RoundToInt((spawnedTotemPos.z - transform.position.z) / cellSize), 0, gridL - 1)
        );

        for (int x = -2; x <= 2; x++)
        {
            for (int z = -2; z <= 2; z++)
            {
                if (totemGrid.x + x >= 0 && totemGrid.x + x < gridW && totemGrid.y + z >= 0 && totemGrid.y + z < gridL)
                    roadNetwork[totemGrid.x + x, totemGrid.y + z] = true;
            }
        }

        float startTime = Time.realtimeSinceStartup;
        int roadsBuilt = 0;

        // Read the heightmap ONCE; every road carves into this shared array and
        // it's written back a single time after the loop (see fix below).
        TerrainData roadTd = terrain.terrainData;
        int roadRes = roadTd.heightmapResolution;
        float[,] roadHeights = roadTd.GetHeights(0, 0, roadRes, roadRes);

        foreach (Vector3 targetPos in allTargets)
        {
            Vector2Int startGrid = new Vector2Int(
                Mathf.Clamp(Mathf.RoundToInt((targetPos.x - transform.position.x) / cellSize), 0, gridW - 1),
                Mathf.Clamp(Mathf.RoundToInt((targetPos.z - transform.position.z) / cellSize), 0, gridL - 1)
            );

            List<Vector3> path = FindAStarPathToNetwork(startGrid, totemGrid, roadNetwork, gridW, gridL, cellSize, absWaterH);

            if (path != null && path.Count > 4)
            {
                // Extend the road tip a bit past the dead-end target so the
                // altar has a visible run of path leading to it — otherwise
                // the road just stops at the flat spot and the altar looks
                // like it's floating in a clearing. Only extend for dead-end
                // targets (POI targets already have the POI itself as the
                // visual terminus).
                if (deadEndTipExtensionMeters > 0.1f && deadEndTargets.Contains(targetPos) && path.Count >= 2)
                {
                    Vector3 tipDir = path[0] - path[1];
                    tipDir.y = 0f;
                    if (tipDir.sqrMagnitude > 0.001f)
                    {
                        Vector3 extendedTip = path[0] + tipDir.normalized * deadEndTipExtensionMeters;
                        // Snap to terrain height so the extension doesn't
                        // hang above/below the ground surface.
                        extendedTip.y = terrain.SampleHeight(extendedTip) + transform.position.y;
                        path.Insert(0, extendedTip);
                    }
                }

                SmoothPathXZ(path, 4);
                GameObject roadObj = new GameObject($"RoadSpline_{roadsBuilt}");
                roadObj.transform.SetParent(roadContainer);
                roadObj.transform.position = path[0];
                SplineContainer sc = roadObj.AddComponent<SplineContainer>();
                Spline spline = sc.Spline;
                spline.Clear();

                for (int i = 0; i < path.Count; i += 2)
                {
                    Vector3 pt = path[i];
                    pt.y = terrain.SampleHeight(pt) + transform.position.y + 0.1f;
                    Vector3 fwd = i < path.Count - 2 ? path[i + 2] - pt : pt - path[i - 2];
                    fwd.y = 0; if (fwd.sqrMagnitude < 0.001f) fwd = Vector3.forward;
                    Quaternion rot = Quaternion.LookRotation(fwd.normalized, Vector3.up);
                    Vector3 local = roadObj.transform.InverseTransformPoint(pt);
                    spline.Add(new BezierKnot(new float3(local.x, local.y, local.z), default, default, new quaternion(rot.x, rot.y, rot.z, rot.w)));
                    spline.SetTangentMode(spline.Count - 1, TangentMode.AutoSmooth);

                    int pX = Mathf.Clamp(Mathf.RoundToInt((pt.x - transform.position.x) / cellSize), 0, gridW - 1);
                    int pZ = Mathf.Clamp(Mathf.RoundToInt((pt.z - transform.position.z) / cellSize), 0, gridL - 1);
                    roadNetwork[pX, pZ] = true;
                }
                roadSplines.Add(sc);
                CarveAndRasterizeRoad(sc, roadHeights);
                roadsBuilt++;
            }
            else
            {
                Debug.LogWarning($"[Smart Roads] Шлях заблоковано ландшафтом для цілі: {targetPos}");
            }
            if (Time.realtimeSinceStartup - startTime > MAX_FRAME_TIME) { yield return null; startTime = Time.realtimeSinceStartup; }
        }

        // Write the carved heightmap back ONCE, then rebuild the TerrainCollider
        // ONCE. Previously each road did its own SetHeights + collider cycle —
        // ~20 full-terrain writes and physics-mesh bakes back-to-back, the single
        // biggest load-time stall. The end state is identical.
        if (roadsBuilt > 0)
        {
            roadTd.SetHeights(0, 0, roadHeights);
            terrain.Flush();
            TerrainCollider tcRoad = terrain.GetComponent<TerrainCollider>();
            if (tcRoad != null) { tcRoad.enabled = false; tcRoad.enabled = true; }
        }
        GameLog.Info($"[Smart Roads] Готово! Побудовано {roadsBuilt} доріг.");
    }

    private void PaintSandCircle(float[,,] splat, TerrainData td,
        int aWidth, int aHeight, Vector3 worldPos, float radiusMeters)
    {
        int ax = Mathf.RoundToInt(((worldPos.x - transform.position.x) / td.size.x) * aWidth);
        int ay = Mathf.RoundToInt(((worldPos.z - transform.position.z) / td.size.z) * aHeight);
        int rad = Mathf.Max(1, Mathf.RoundToInt((radiusMeters / td.size.x) * aWidth));

        float fullCore = 1f - sandEdgeSoftness;

        for (int y = -rad; y <= rad; y++)
        {
            for (int x = -rad; x <= rad; x++)
            {
                int sx = ax + x, sy = ay + y;
                if (sx < 0 || sx >= aWidth || sy < 0 || sy >= aHeight) continue;

                float dist = Mathf.Sqrt(x * x + y * y);
                if (dist > rad) continue;

                float norm = dist / rad;
                float blend = (norm <= fullCore) ? 1f : Mathf.SmoothStep(1f, 0f, (norm - fullCore) / (1f - fullCore));

                if (blend <= 0.001f) continue;

                // --- ФІКС: Забороняємо піску малюватись поверх дороги ---
                if (splat[sy, sx, 4] > 0.5f) continue;

                splat[sy, sx, 1] = Mathf.Max(splat[sy, sx, 1], blend);

                float remain = 1f - splat[sy, sx, 1] - splat[sy, sx, 4];
                if (remain < 0) remain = 0;

                float sumOthers = splat[sy, sx, 0] + splat[sy, sx, 2] + splat[sy, sx, 3];

                if (sumOthers > 0.001f)
                {
                    splat[sy, sx, 0] = (splat[sy, sx, 0] / sumOthers) * remain;
                    splat[sy, sx, 2] = (splat[sy, sx, 2] / sumOthers) * remain;
                    splat[sy, sx, 3] = (splat[sy, sx, 3] / sumOthers) * remain;
                }
                else
                {
                    splat[sy, sx, 0] = remain;
                }
            }
        }
    }

    private IEnumerator PopulateBiomesRoutine()
    {
        float w = terrain.terrainData.size.x; float l = terrain.terrainData.size.z;
        Transform treeContainer = new GameObject("TreesContainer").transform; treeContainer.SetParent(this.transform);
        Transform rockContainer = new GameObject("RocksContainer").transform; rockContainer.SetParent(this.transform);
        Transform bushContainer = new GameObject("BushContainer").transform; bushContainer.SetParent(this.transform);
        Transform logContainer = new GameObject("LogsContainer").transform; logContainer.SetParent(this.transform);

        // Register tree prototypes so normal trees get PAINTED onto the terrain
        // (batched) instead of instantiated as GameObjects.
        PrepareTerrainTreePrototypes();

        float startTime = Time.realtimeSinceStartup;

        for (int i = 0; i < spawnAttempts; i++)
        {
            if (currentTreeCount >= maxTrees && currentRockCount >= maxRocks && currentBushCount >= maxBushesAndMushroom)
                break;

            if (Time.realtimeSinceStartup - startTime > MAX_FRAME_TIME) { yield return null; startTime = Time.realtimeSinceStartup; }

            try
            {
                float px = GetRandomRange(10f, w - 10f); float pz = GetRandomRange(10f, l - 10f);
                float worldX = transform.position.x + px; float worldZ = transform.position.z + pz;
                float worldY = terrain.SampleHeight(new Vector3(worldX, 0, worldZ)) + transform.position.y;

                float normalizedX = px / w; float normalizedZ = pz / l;
                float normalizedHeight = (worldY - transform.position.y) / depth;
                float steepness = terrain.terrainData.GetSteepness(normalizedX, normalizedZ);
                float localTemp = GetTemperature(normalizedX, normalizedZ);

                bool inForbiddenZone = false;
                Vector3 currentPos = new Vector3(worldX, worldY, worldZ);
                // sqrMagnitude comparison — Vector3.Distance does a
                // Sqrt per call, which at ~60k spawn attempts × growing
                // forbiddenZones list adds real seconds to load time.
                const float FORBIDDEN_SQR = 18f * 18f;
                for (int fzi = 0; fzi < forbiddenZones.Count; fzi++)
                {
                    Vector3 d = currentPos - forbiddenZones[fzi];
                    if (d.x * d.x + d.y * d.y + d.z * d.z < FORBIDDEN_SQR) { inForbiddenZone = true; break; }
                }
                if (inForbiddenZone) continue;

                Vector3 terrainNormal = terrain.terrainData.GetInterpolatedNormal(normalizedX, normalizedZ);
                Quaternion slopeRotation = Quaternion.FromToRotation(Vector3.up, terrainNormal);

                bool isSnow = false;
                bool isDesert = false;

                if (normalizedHeight > 0.65f) isSnow = true;
                else if (normalizedHeight <= waterLevel + 0.02f) isDesert = true;
                else
                {
                    if (localTemp >= 0.65f) isDesert = true;
                    else if (localTemp <= 0.35f) isSnow = true;
                }

                Texture2D currentTreeTexture = forestTreeTexture;
                Material currentBaseTreeMat = null;
                Material currentGiantTreeMat = null;
                Material currentBushMat = null;
                Color currentFoliageColor = forestFoliageColor;
                Color currentRockColor = forestRockColor;
                GameObject vfxToSpawn = giantTreeVFXForest;

                if (isDesert)
                {
                    currentTreeTexture = desertTreeTexture;
                    currentBaseTreeMat = baseTreeAutumnMaterial;
                    currentGiantTreeMat = giantTreeAutumnMaterial;
                    currentBushMat = bushAutumnMaterial;
                    currentFoliageColor = desertFoliageColor;
                    currentRockColor = desertRockColor;
                    vfxToSpawn = giantTreeVFXAutumn;
                }
                else if (isSnow)
                {
                    currentTreeTexture = snowTreeTexture;
                    currentBaseTreeMat = baseTreeWinterMaterial;
                    currentGiantTreeMat = giantTreeWinterMaterial;
                    currentBushMat = bushWinterMaterial;
                    currentFoliageColor = snowFoliageColor;
                    currentRockColor = snowRockColor;
                    vfxToSpawn = giantTreeVFXWinter;
                }

                float absWaterHeight = transform.position.y + (depth * waterLevel);

                if (normalizedHeight <= waterLevel) continue;

                if (normalizedHeight <= waterLevel + 0.03f)
                {
                    if (waterPlantsPrefabs != null && waterPlantsPrefabs.Length > 0 && GetRandomFloat() > 0.4f)
                    {
                        GameObject wpPrefab = GetRandomPrefab(waterPlantsPrefabs);
                        GameObject obj = Instantiate(wpPrefab, new Vector3(worldX, absWaterHeight, worldZ), Quaternion.Euler(0, GetRandomRange(0f, 360f), 0), bushContainer);
                        obj.transform.localScale = Vector3.Scale(obj.transform.localScale, RandomDecorScale(0.75f, 1.45f));
                        DisableShadowCasting(obj);
                    }
                    continue;
                }

                if (steepness > cliffSteepnessThreshold) continue;

                float density = Mathf.PerlinNoise(normalizedX * clusterScale + offsetX, normalizedZ * clusterScale + offsetZ);
                float meadowNoise = Mathf.PerlinNoise(normalizedX * meadowScale + offsetX + 1000f, normalizedZ * meadowScale + offsetZ + 1000f);
                float veinNoise = Mathf.PerlinNoise(normalizedX * veinScale + offsetX + 2000f, normalizedZ * veinScale + offsetZ + 2000f);

                bool isMeadow = meadowNoise > meadowThreshold;
                bool isVein = veinNoise > veinThreshold;
                if (isMeadow) density = 0f;

                float randomSpawn = GetRandomFloat();

                // Note: any ambient VFX we spawn here goes through the
                // no-collider guard below so authored trigger volumes on
                // VFX prefabs can't leak into runtime as invisible walls.
                if (ambientVFXPrefabs != null && ambientVFXPrefabs.Length > 0 && randomSpawn > 0.985f)
                {
                    GameObject vfxPrefab = GetRandomPrefab(ambientVFXPrefabs);
                    GameObject vfxInst = Instantiate(vfxPrefab, new Vector3(worldX, worldY + 1.5f, worldZ), Quaternion.identity, treeContainer);
                    // Strip non-trigger colliders — ambient VFX shouldn't
                    // block movement even if the prefab was authored with
                    // an interaction volume.
                    if (vfxInst != null)
                    {
                        foreach (var col in vfxInst.GetComponentsInChildren<Collider>(true))
                            if (col != null && !col.isTrigger) Destroy(col);
                    }
                }

                if (density > forestThreshold && steepness <= 25f)
                {
                    // Giant trees are gated by a DEDICATED cap and a spacing rule
                    // (on top of the density/roll test) so their big alpha-tested
                    // canopies can't stack and multiply overdraw — that stacking
                    // was the real cause of the 1-5 FPS drops next to giant trees.
                    bool giantTreeAllowed = currentGiantTreeCount < maxGiantTrees && giantTrees != null && giantTrees.Length > 0;
                    if (giantTreeAllowed)
                    {
                        float giantSpacingSqr = giantTreeMinSpacing * giantTreeMinSpacing;
                        for (int gi = 0; gi < giantTreePositions.Count; gi++)
                        {
                            Vector3 gd = currentPos - giantTreePositions[gi];
                            if (gd.x * gd.x + gd.z * gd.z < giantSpacingSqr) { giantTreeAllowed = false; break; }
                        }
                    }

                    if (currentTreeCount < maxTrees && giantTreeAllowed && density > forestThreshold + 0.2f && randomSpawn > 0.85f)
                    {
                        GameObject giantTreePrefab = GetRandomPrefab(giantTrees);
                        GameObject obj = Instantiate(giantTreePrefab, new Vector3(worldX, worldY, worldZ), Quaternion.Euler(0, GetRandomRange(0f, 360f), 0), treeContainer);
                        obj.transform.localScale = Vector3.Scale(obj.transform.localScale, RandomDecorScale(0.95f, 1.6f, 0.1f));

                        // Bias the prefab's LODGroup so it drops to the cheaper LODs
                        // sooner — the full-detail canopy (LOD0) is the overdraw hog
                        // at close range. Shrinking the group's reference size pulls
                        // every LOD switch nearer without touching the prefab.
                        LODGroup giantLod = obj.GetComponent<LODGroup>();
                        if (giantLod != null) giantLod.size *= 0.6f;

                        if (currentGiantTreeMat != null) ApplyBiomeSpecificMaterial(obj, currentGiantTreeMat);
                        else { ApplyBiomeTexture(obj, currentTreeTexture); ApplyBiomeColor(obj, currentFoliageColor, true); }

                        // Giant trees are the worst-case shadow caster on
                        // the map: tall geometry, every leaf billboard
                        // sliced into the depth pass, multiplied by N per
                        // chunk. Each tree was driving its own shadow pass
                        // when the camera looked at the canopy and FPS
                        // tanked to ~20 next to one. Forcing the trunk +
                        // canopy renderers to ShadowsOnly = Off cuts the
                        // worst spike without changing what the player
                        // sees in the foreground.
                        Renderer[] giantRenderers = obj.GetComponentsInChildren<Renderer>(true);
                        foreach (Renderer r in giantRenderers)
                            if (r != null) r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

                        currentTreeCount++;
                        currentGiantTreeCount++;
                        giantTreePositions.Add(currentPos);

                        if (vfxToSpawn != null)
                        {
                            GameObject vfxGO = Instantiate(vfxToSpawn, obj.transform.position + Vector3.up * 5f, Quaternion.identity, obj.transform);
                            // The leaf-fall / snow particle systems run
                            // unconditionally; multiplied across every giant
                            // tree in a region they crushed the GPU budget the
                            // moment the player walked up to one. The LOD
                            // component stops emission past 55m and scales it
                            // smoothly inside that radius, so distant trees
                            // contribute nothing while the close-up effect
                            // still looks right.
                            if (vfxGO.GetComponent<GiantTreeVFXLOD>() == null)
                                vfxGO.AddComponent<GiantTreeVFXLOD>();
                        }

                        if (groundClutterPrefabs != null && groundClutterPrefabs.Length > 0) SpawnNatureCluster(GetRandomPrefab(groundClutterPrefabs), obj.transform.position, bushContainer, 3, 6, 3f, true, slopeRotation, currentFoliageColor, currentTreeTexture, currentBushMat);
                    }
                    else if (currentTreeCount < maxTrees && randomSpawn > 0.65f)
                    {
                        // In a cursed STORY region, a share of the trees are
                        // blighted husks that transform into living trees during
                        // the victory flythrough. Their trunks are still recoloured
                        // to the biome so they read as "this biome, but withered".
                        bool useCursed = useCursedTrees && isRegionMissionCached
                                         && cursedDeadTrees != null && cursedDeadTrees.Length > 0
                                         && GetRandomFloat() < cursedTreeChance;

                        if (useCursed)
                        {
                            int ci = GetRandomRangeInt(0, cursedDeadTrees.Length);
                            GameObject deadPrefab = cursedDeadTrees[ci];
                            if (deadPrefab != null)
                            {
                                Quaternion huskRot = Quaternion.Euler(0, GetRandomRange(0f, 360f), 0) * Quaternion.Euler(cursedTreeRotationOffset);
                                GameObject husk = Instantiate(deadPrefab, new Vector3(worldX, worldY, worldZ), huskRot, treeContainer);
                                husk.transform.localScale = Vector3.Scale(husk.transform.localScale, RandomDecorScale(0.7f, 1.4f));

                                // Recolour the withered trunk to the biome.
                                bool appliedMat = false;
                                if (currentBaseTreeMat != null) { ApplyBiomeSpecificMaterial(husk, currentBaseTreeMat); appliedMat = true; }
                                else { ApplyBiomeTexture(husk, currentTreeTexture); ApplyBiomeColor(husk, currentRockColor, true); }

                                // Pick the living version it becomes (paired by
                                // index, else a random base tree).
                                GameObject bloomed = (bloomedTreeVariants != null && ci < bloomedTreeVariants.Length && bloomedTreeVariants[ci] != null)
                                                   ? bloomedTreeVariants[ci] : GetRandomPrefab(baseTrees);

                                CursedTree ct = husk.AddComponent<CursedTree>();
                                ct.Configure(bloomed, currentBaseTreeMat, currentFoliageColor, appliedMat, !appliedMat, cursedTreeBloomVFX);

                                currentTreeCount++;
                            }
                        }
                        else
                        {
                            bool useDeadTree = (isSnow || isDesert) && GetRandomFloat() > 0.5f && deadTreesPrefabs != null && deadTreesPrefabs.Length > 0;
                            // Pick the biome-appropriate REAL prefab (dedicated biome trees
                            // if assigned, else the forest set).
                            GameObject treePrefab = PickTreePrefabForBiome(currentBaseTreeMat, useDeadTree);

                            // Wider, non-uniform size spread so trees aren't all clones.
                            Vector3 tScale = RandomDecorScale(0.7f, 1.45f);

                            // A fraction of trees spawn as REAL objects so wood stays
                            // FARMABLE — terrain-painted trees are batched for FPS but
                            // can't carry the ResourceNode/collider needed to harvest.
                            // Seed-deterministic (prng), so it's stable per region.
                            bool asFarmable = GetRandomFloat() < treeFarmableFraction;

                            // Prefer PAINTING onto the terrain (batched, big FPS win). The
                            // biome PREFAB's own material gives the colour now, so only tint
                            // as a fallback when that biome has no dedicated prefab assigned.
                            Color treeTint = TreeBiomeHasPrefab(currentBaseTreeMat) ? Color.white : currentFoliageColor;
                            if (!useDeadTree && !asFarmable && AddTerrainTree(treePrefab, worldX, worldZ, tScale.x, tScale.y, treeTint))
                            {
                                currentTreeCount++;
                            }
                            else
                            {
                                // Object tree: farmable (keeps ResourceNode + collider), or
                                // the fallback when painting is unavailable / off-terrain.
                                GameObject obj = Instantiate(treePrefab, new Vector3(worldX, worldY, worldZ), Quaternion.Euler(0, GetRandomRange(0f, 360f), 0), treeContainer);
                                obj.transform.localScale = Vector3.Scale(obj.transform.localScale, tScale);

                                if (!useDeadTree)
                                {
                                    if (currentBaseTreeMat != null) ApplyBiomeSpecificMaterial(obj, currentBaseTreeMat);
                                    else { ApplyBiomeTexture(obj, currentTreeTexture); ApplyBiomeColor(obj, currentFoliageColor, true); }
                                }
                                else { ApplyBiomeColor(obj, currentRockColor, true); }

                                currentTreeCount++;
                            }
                        }
                    }
                    else if (currentBushCount < maxBushesAndMushroom && randomSpawn > 0.10f)
                    {
                        // Desert sometimes uses mushrooms; otherwise biome bushes.
                        bool asMushroom = isDesert && GetRandomFloat() > 0.5f && baseMushrooms != null && baseMushrooms.Length > 0;
                        GameObject naturePrefab = asMushroom ? GetRandomPrefab(baseMushrooms) : PickBushPrefabForBiome(currentBushMat);

                        // PAINT the cluster onto the terrain (batched) — the biome
                        // prefab's material colours it; tint only as a fallback.
                        Color bushTint = (asMushroom || BushBiomeHasPrefab(currentBushMat)) ? Color.white : currentFoliageColor;
                        int painted = PaintVegetationCluster(naturePrefab, worldX, worldZ, 2, 6, 4f, bushTint);
                        if (painted >= 0)
                            currentBushCount += painted;
                        else
                            currentBushCount += SpawnNatureCluster(naturePrefab, new Vector3(worldX, worldY, worldZ), bushContainer, 2, 6, 4f, true, slopeRotation, currentFoliageColor, currentTreeTexture, currentBushMat);
                    }
                }
                else if (density < 0.3f || isMeadow)
                {
                    if (isVein && currentRockCount < maxRocks && randomSpawn > 0.7f)
                    {
                        GameObject rockBase = GetRandomPrefab(baseRocks);
                        int clusterSize = GetRandomRangeInt(3, 6);
                        for (int c = 0; c < clusterSize; c++)
                        {
                            float ox = GetRandomRange(-4f, 4f); float oz = GetRandomRange(-4f, 4f);
                            float cy = terrain.SampleHeight(new Vector3(worldX + ox, 0, worldZ + oz)) + transform.position.y;
                            GameObject obj = Instantiate(rockBase, new Vector3(worldX + ox, cy, worldZ + oz), slopeRotation * Quaternion.Euler(0, GetRandomRange(0f, 360f), 0), rockContainer);
                            obj.transform.localScale *= GetRandomRange(0.5f, 1.2f); ApplyBiomeColor(obj, currentRockColor, true);
                            currentRockCount++;
                        }
                    }
                    else if (currentRockCount < maxRocks && randomSpawn > 0.95f)
                    {
                        bool isRuin = ruinPrefabs != null && ruinPrefabs.Length > 0 && GetRandomFloat() > 0.8f;
                        GameObject targetPrefab = isRuin ? GetRandomPrefab(ruinPrefabs) : GetRandomPrefab(baseRocks);
                        GameObject obj = Instantiate(targetPrefab, new Vector3(worldX, worldY, worldZ), slopeRotation * Quaternion.Euler(0, GetRandomRange(0f, 360f), 0), rockContainer);
                        // Ruins/rocks were spawning at a fixed size — vary them.
                        obj.transform.localScale = Vector3.Scale(obj.transform.localScale,
                            isRuin ? RandomDecorScale(0.85f, 1.25f, 0.08f) : RandomDecorScale(0.5f, 1.3f));
                        if (!isRuin) ApplyBiomeColor(obj, currentRockColor, true);
                        currentRockCount++;
                    }
                }
                else
                {
                    if (currentTreeCount < maxTrees && randomSpawn > 0.95f)
                    {
                        GameObject log = GetRandomPrefab(logPrefabs);
                        if (log != null)
                        {
                            GameObject lg = Instantiate(log, new Vector3(worldX, worldY, worldZ), Quaternion.Euler(0, GetRandomRange(0f, 360f), 0), logContainer);
                            lg.transform.localScale = Vector3.Scale(lg.transform.localScale, RandomDecorScale(0.7f, 1.3f));
                            DisableShadowCasting(lg);
                            currentTreeCount++;
                        }
                    }
                }
            }
            catch (System.Exception e) { Debug.LogError($"[Помилка генерації префабу]: {e.Message}"); }

            // Post-iteration budget check: a single giant-tree iteration does a
            // lot of uninterrupted work (Instantiate a 4-LOD prefab + VFX + a
            // clutter cluster + several GetComponentsInChildren). The top-of-loop
            // check alone let one such iteration overshoot the frame; re-check
            // here so a heavy iteration yields before the next one starts.
            if (Time.realtimeSinceStartup - startTime > MAX_FRAME_TIME) { yield return null; startTime = Time.realtimeSinceStartup; }
        }

        // Commit all painted trees to the terrain in one batch (snapped to the
        // now-final heightmap).
        FlushTerrainTrees();
    }

    private void ApplyBiomeColor(GameObject obj, Color baseColor, bool randomize = false)
    {
        Renderer[] renderers = obj.GetComponentsInChildren<Renderer>(true);
        Color finalColor = baseColor;

        if (randomize)
        {
            Color.RGBToHSV(baseColor, out float h, out float s, out float v);
            h = Mathf.Repeat(h + GetRandomRange(-0.04f, 0.04f), 1f);
            s = Mathf.Clamp01(s * GetRandomRange(0.8f, 1.1f));
            v = Mathf.Clamp01(v * GetRandomRange(0.6f, 1.1f));
            finalColor = Color.HSVToRGB(h, s, v);
        }

        foreach (Renderer rend in renderers)
        {
            if (rend is ParticleSystemRenderer) continue;
            if (IsVFX(rend.gameObject.name)) continue;

            for (int i = 0; i < rend.sharedMaterials.Length; i++)
            {
                Material mat = rend.sharedMaterials[i];
                if (mat == null) continue;

                // GPU INSTANCING: colors here are pushed per-renderer via a
                // MaterialPropertyBlock (below), which is instancing-friendly, so
                // enabling instancing on the SHARED material lets Unity batch all
                // trees/bushes of the same mesh+material into a handful of draws
                // instead of thousands — a large FPS win in dense forests.
                if (!mat.enableInstancing) mat.enableInstancing = true;

                // Більше ніяких .ToLower(), це економить МЕГАБАЙТИ оперативної пам'яті
                if (IsWoodOrTrunk(rend.gameObject.name) || IsWoodOrTrunk(mat.name))
                {
                    continue;
                }

                propBlock.Clear();
                rend.GetPropertyBlock(propBlock, i);

                propBlock.SetColor("_Color", finalColor);
                propBlock.SetColor("Color", finalColor);
                propBlock.SetColor("_BaseColor", finalColor);
                propBlock.SetColor("_Base_Color", finalColor);
                propBlock.SetColor("_PrimaryColor", finalColor);
                propBlock.SetColor("_TopColor", finalColor);
                propBlock.SetColor("_BottomColor", finalColor);
                propBlock.SetColor("_Tint", finalColor);
                propBlock.SetColor("_TintColor", finalColor);
                propBlock.SetColor("_FoliageColor", finalColor);
                propBlock.SetColor("_LeafColor", finalColor);

                rend.SetPropertyBlock(propBlock, i);
            }
        }
    }

    private void ApplyBiomeTexture(GameObject obj, Texture2D biomeTexture)
    {
        if (biomeTexture == null) return;
        Renderer[] renderers = obj.GetComponentsInChildren<Renderer>(true);
        foreach (Renderer rend in renderers)
        {
            if (rend is ParticleSystemRenderer) continue;
            if (IsVFX(rend.gameObject.name)) continue;

            for (int i = 0; i < rend.sharedMaterials.Length; i++)
            {
                Material mat = rend.sharedMaterials[i];
                if (mat == null) continue;

                if (IsWoodOrTrunk(rend.gameObject.name) || IsWoodOrTrunk(mat.name)) continue;

                propBlock.Clear();
                rend.GetPropertyBlock(propBlock, i);

                propBlock.SetTexture("_BaseMap", biomeTexture);
                propBlock.SetTexture("_MainTex", biomeTexture);
                propBlock.SetTexture("_Albedo", biomeTexture);

                rend.SetPropertyBlock(propBlock, i);
            }
        }
    }

    private void ApplyBiomeSpecificMaterial(GameObject obj, Material foliageMat)
    {
        if (foliageMat == null) return;
        // Batch every tree/bush sharing this biome material via GPU instancing.
        if (!foliageMat.enableInstancing) foliageMat.enableInstancing = true;

        Renderer[] renderers = obj.GetComponentsInChildren<Renderer>(true);
        foreach (Renderer rend in renderers)
        {
            if (rend is ParticleSystemRenderer) continue;
            if (IsVFX(rend.gameObject.name)) continue;

            Material[] mats = rend.sharedMaterials;
            bool changed = false;

            for (int i = 0; i < mats.Length; i++)
            {
                if (mats[i] == null) continue;
                if (IsWoodOrTrunk(rend.gameObject.name) || IsWoodOrTrunk(mats[i].name)) continue;

                mats[i] = foliageMat;
                changed = true;
            }
            if (changed) rend.sharedMaterials = mats;
        }
    }

    private int SpawnNatureCluster(GameObject prefab, Vector3 centerPos, Transform container, int minCount, int maxCount, float radius, bool alignToSlope, Quaternion slopeRotation, Color tintColor, Texture2D biomeTexture = null, Material biomeMaterial = null)
    {
        if (prefab == null) return 0;
        int count = GetRandomRangeInt(minCount, maxCount + 1); int spawned = 0;
        for (int i = 0; i < count; i++)
        {
            float ox = GetRandomRange(-radius, radius); float oz = GetRandomRange(-radius, radius);
            float cy = terrain.SampleHeight(new Vector3(centerPos.x + ox, 0, centerPos.z + oz)) + transform.position.y;
            Quaternion randomYRot = Quaternion.Euler(0, GetRandomRange(0f, 360f), 0);
            Quaternion finalRot = alignToSlope ? (slopeRotation * randomYRot * prefab.transform.rotation) : (randomYRot * prefab.transform.rotation);

            GameObject obj = Instantiate(prefab, new Vector3(centerPos.x + ox, cy, centerPos.z + oz), finalRot, container);
            obj.transform.localScale *= GetRandomRange(0.7f, 1.3f);

            DisableShadowCasting(obj);   // small foliage: no shadow casting (perf)

            // Strip solid colliders from bushes/mushrooms/ground clutter —
            // these are visual props, not obstacles. Rocks & trees keep
            // their colliders because they spawn via a different path.
            // Triggers are preserved (some props have interaction volumes).
            foreach (var col in obj.GetComponentsInChildren<Collider>(true))
                if (col != null && !col.isTrigger) Destroy(col);

            if (biomeMaterial != null)
            {
                ApplyBiomeSpecificMaterial(obj, biomeMaterial);
            }
            else
            {
                if (biomeTexture != null) ApplyBiomeTexture(obj, biomeTexture);
                ApplyBiomeColor(obj, tintColor, true);
            }

            spawned++;
        }
        return spawned;
    }

    private void FlattenTerrainRobust(Vector3 center, float radius, float falloff, float targetWorldY)
    {
        TerrainData td = terrain.terrainData;
        int resolution = td.heightmapResolution;
        float w = td.size.x;
        float l = td.size.z;

        float localX = center.x - transform.position.x;
        float localZ = center.z - transform.position.z;

        int tX = Mathf.RoundToInt((localX / w) * resolution);
        int tZ = Mathf.RoundToInt((localZ / l) * resolution);

        int r = Mathf.CeilToInt((radius + falloff) / w * resolution);
        int startX = Mathf.Clamp(tX - r, 0, resolution - 1);
        int startZ = Mathf.Clamp(tZ - r, 0, resolution - 1);
        int endX = Mathf.Clamp(tX + r, 0, resolution - 1);
        int endZ = Mathf.Clamp(tZ + r, 0, resolution - 1);

        int width = endX - startX;
        int height = endZ - startZ;
        if (width <= 0 || height <= 0) return;

        float[,] heights = td.GetHeights(startX, startZ, width, height);
        float normalizedTarget = (targetWorldY - transform.position.y) / td.size.y;

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                float curWorldX = (startX + x) * w / resolution;
                float curWorldZ = (startZ + y) * l / resolution;
                float dist = Vector2.Distance(new Vector2(curWorldX, curWorldZ), new Vector2(localX, localZ));

                if (dist <= radius)
                {
                    heights[y, x] = normalizedTarget;
                }
                else if (dist <= radius + falloff)
                {
                    // Плавний спуск (Smoothstep) від рівнини до гір
                    float t = (dist - radius) / falloff;
                    t = t * t * (3f - 2f * t);
                    heights[y, x] = Mathf.Lerp(normalizedTarget, heights[y, x], t);
                }
            }
        }
        td.SetHeights(startX, startZ, heights);
    }

    private IEnumerator SpawnPOIsRoutine()
    {
        if (poiPrefabs == null || poiPrefabs.Length == 0) yield break;
        Transform poiContainer = new GameObject("POIContainer").transform;
        poiContainer.SetParent(this.transform);

        float w = terrain.terrainData.size.x;
        float l = terrain.terrainData.size.z;
        float absoluteWaterHeight = transform.position.y + (depth * waterLevel);

        List<POIVirtualData> plannedPOIs = new List<POIVirtualData>();

        // ==========================================
        // ФАЗА 1: VIRTUAL GRID (Створюємо список потенційних точок)
        // ==========================================
        List<Vector2> masterGrid = new List<Vector2>();
        float scanStep = 40f;
        float edgeMargin = 120f; // Ближче ніж 120м до краю карти нічого не будуємо

        for (float x = edgeMargin; x < w - edgeMargin; x += scanStep)
        {
            for (float z = edgeMargin; z < l - edgeMargin; z += scanStep)
            {
                float pX = transform.position.x + x;
                float pZ = transform.position.z + z;
                float h = terrain.SampleHeight(new Vector3(pX, 0, pZ)) + transform.position.y;

                // Відсіюємо воду і відверті гори
                if (h > absoluteWaterHeight + 3f && terrain.terrainData.GetSteepness(x / w, z / l) < maxPOISteepness)
                {
                    masterGrid.Add(new Vector2(pX, pZ));
                }
            }
        }

        // Перемішуємо точки для рандомності
        for (int i = 0; i < masterGrid.Count; i++)
        {
            Vector2 temp = masterGrid[i];
            int rnd = UnityEngine.Random.Range(i, masterGrid.Count);
            masterGrid[i] = masterGrid[rnd];
            masterGrid[rnd] = temp;
        }

        // ==========================================
        // ФАЗА 2: ВІРТУАЛЬНИЙ РОЗПОДІЛ (Без спавну об'єктів)
        // ==========================================
        int prefabsAssigned = 0;

        foreach (Vector2 gridPoint in masterGrid)
        {
            if (prefabsAssigned >= maxPOIs) break;

            GameObject prefab = GetRandomPrefab(poiPrefabs);
            POISettings settings = prefab.GetComponent<POISettings>();

            if (settings == null)
            {
                Debug.LogError($"[AAA Gen] Префаб {prefab.name} не має скрипта POISettings! Пропускаємо.");
                continue;
            }

            Vector3 centerPos = new Vector3(gridPoint.x, 0, gridPoint.y);

            // Перевіряємо, чи не накладається на інші заплановані локації
            bool isOverlap = false;
            foreach (var planned in plannedPOIs)
            {
                if (Vector3.Distance(centerPos, planned.worldPosition) < (settings.flattenRadius + planned.settings.flattenRadius + 20f))
                {
                    isOverlap = true; break;
                }
            }
            if (isOverlap) continue;

            // AREA VARIANCE SCAN: Перевіряємо перепад висот під радіусом конкретного префабу.
            // DENSE FOOTPRINT SAMPLING: the old 5-probe cross (center + 4 cardinals
            // at the rim) left big gaps — a large location straddling a lake in a
            // corner or between the arms passed the test and spawned in the water.
            // Sample two concentric rings of 8 (rim + mid) plus the center = 17
            // probes so no lake pocket under the footprint goes unnoticed.
            float minH = float.MaxValue;
            float maxH = float.MinValue;
            bool touchesWater = false;

            float r = settings.flattenRadius;
            List<Vector3> scanPts = new List<Vector3>(17) { centerPos };
            for (int a = 0; a < 8; a++)
            {
                float ang = a * (Mathf.PI * 2f / 8f);
                Vector3 dir = new Vector3(Mathf.Cos(ang), 0, Mathf.Sin(ang));
                scanPts.Add(centerPos + dir * r);          // outer rim
                scanPts.Add(centerPos + dir * (r * 0.6f)); // mid ring
            }

            foreach (var pt in scanPts)
            {
                float ch = terrain.SampleHeight(pt) + transform.position.y;
                if (ch <= absoluteWaterHeight + 1.5f) touchesWater = true;
                if (ch > maxH) maxH = ch;
                if (ch < minH) minH = ch;
            }

            // Якщо води немає і перепад висот в межах норми паспорта - ЗАТВЕРДЖУЄМО ТОЧКУ
            if (!touchesWater && (maxH - minH) <= settings.maxAllowedSlope)
            {
                centerPos.y = minH; // Беремо найнижчу точку як базову для вирівнювання

                plannedPOIs.Add(new POIVirtualData
                {
                    worldPosition = centerPos,
                    prefab = prefab,
                    settings = settings
                });
                prefabsAssigned++;
            }
        }

        // ==========================================
        // ФАЗА 3: ТЕРАФОРМУВАННЯ
        // ==========================================
        foreach (var poi in plannedPOIs)
        {
            roadTargets.Add(new Vector3(poi.worldPosition.x, poi.worldPosition.y, poi.worldPosition.z));
            FlattenTerrainRobust(poi.worldPosition, poi.settings.flattenRadius, 15f, poi.worldPosition.y);
            forbiddenZones.Add(poi.worldPosition);
        }

        // ЖОРСТКИЙ СИНХРОН ФІЗИКИ: 
        // Миттєво перебудовуємо TerrainCollider, щоб старі гори зникли і фізично!
        terrain.Flush();
        TerrainCollider tc = terrain.GetComponent<TerrainCollider>();
        if (tc != null)
        {
            tc.enabled = false;
            tc.enabled = true; // Цей трюк змушує Unity миттєво перерахувати колізію землі
        }

        yield return new WaitForFixedUpdate();
        yield return new WaitForFixedUpdate();

        // ==========================================
        // ФАЗА 4: ФІЗИЧНИЙ СПАВН ОБ'ЄКТІВ
        // ==========================================
        foreach (var poi in plannedPOIs)
        {
            // 1. Беремо висоту рівної землі ПІСЛЯ тераформінгу
            float exactGroundY = terrain.SampleHeight(poi.worldPosition) + transform.position.y;
            Vector3 groundPos = new Vector3(poi.worldPosition.x, exactGroundY, poi.worldPosition.z);

            // 2. Обертання та Спавн
            Quaternion randomRot = Quaternion.Euler(0, GetRandomRange(0f, 360f), 0);
            Quaternion finalRot = randomRot * poi.prefab.transform.rotation;
            GameObject instance = Instantiate(poi.prefab, groundPos, finalRot, poiContainer);

            // 3. РОЗУМНЕ ПРИТИСКАННЯ ДО ЗЕМЛІ (Глобальний фікс Pivot'ів)
            BoxCollider rootBox = instance.GetComponent<BoxCollider>();
            if (rootBox != null)
            {
                // Метод А: Якщо на руті префабу є BoxCollider (як на Тотемах)
                float sy = rootBox.size.y * instance.transform.localScale.y;
                float cy = rootBox.center.y * instance.transform.localScale.y;
                float bottomOffset = cy - (sy / 2f);
                instance.transform.position -= new Vector3(0, bottomOffset, 0);
            }
            else
            {
                // Метод Б: Якщо колайдера немає — скануємо 3D-сітки (Meshes).
                // We snap the STRUCTURE's base to the ground — but decorative
                // geometry that is DELIBERATELY sunk below grade (water-mill
                // wheels dipping into a river, foliage/trees planted deep in the
                // soil) must be EXCLUDED from that calc. Including them made the
                // snap lift the whole location so those parts sat on top of the
                // ground, floating the castle/building in the air.
                List<float> structuralBottoms = new List<float>(16);
                List<float> allBottoms = new List<float>(16);
                foreach (var rend in instance.GetComponentsInChildren<MeshRenderer>(false))
                {
                    if (rend == null || !rend.enabled) continue;
                    float b = rend.bounds.min.y;
                    allBottoms.Add(b);
                    if (!IsBelowGradeDecor(rend.transform, instance.transform)) structuralBottoms.Add(b);
                }
                foreach (var rend in instance.GetComponentsInChildren<SkinnedMeshRenderer>(false))
                {
                    if (rend == null || !rend.enabled) continue;
                    float b = rend.bounds.min.y;
                    allBottoms.Add(b);
                    if (!IsBelowGradeDecor(rend.transform, instance.transform)) structuralBottoms.Add(b);
                }

                // Prefer the structural set; fall back to everything if a prefab
                // is entirely made of "decor"-named parts (so we never skip snap).
                List<float> bottoms = structuralBottoms.Count > 0 ? structuralBottoms : allBottoms;

                if (bottoms.Count > 0)
                {
                    bottoms.Sort();
                    float lowestY = bottoms[0];

                    // Secondary safety: even among structural parts, reject a lone
                    // deep outlier (a foundation pile) so it can't rocket the loc up.
                    if (bottoms.Count >= 3)
                    {
                        float span = bottoms[bottoms.Count - 1] - bottoms[0];
                        float gap = bottoms[1] - bottoms[0];
                        if (span > 0.01f && gap > span * 0.5f && gap > 1.5f)
                            lowestY = bottoms[1];
                    }

                    // Рахуємо різницю між базою моделі та землею і притискаємо об'єкт!
                    float delta = exactGroundY - lowestY;
                    instance.transform.position += new Vector3(0f, delta, 0f);
                }
            }

            // 4. Застосовуємо ручний відступ з твого скрипта POISettings 
            // (можеш ставити yOffset = -0.5f в Інспекторі, щоб додатково "втопити" локацію в траву)
            instance.transform.position += (Vector3.up * poi.settings.yOffset);
        }

        GameLog.Info($"[AAA Gen] Успішно згенеровано {plannedPOIs.Count} POI.");
    }

    // Names (on the renderer or any ancestor up to the loc root) that mark
    // geometry meant to sit BELOW the ground line — water-mill wheels dipping
    // into a river, or trees/foliage planted deep in the soil. These must be
    // ignored when snapping the loc's base to the terrain, or the snap lifts the
    // whole location so they rest on top of the ground and the building floats.
    private static readonly string[] s_belowGradeDecorTerms =
        { "wheel", "mill", "paddle", "waterwheel", "tree", "trunk", "leaf", "leav",
          "foliage", "branch", "bush", "plant", "grass", "reed", "shrub" };

    private bool IsBelowGradeDecor(Transform t, Transform root)
    {
        int guard = 0;
        while (t != null && guard++ < 24)
        {
            string n = t.name.ToLowerInvariant();
            for (int i = 0; i < s_belowGradeDecorTerms.Length; i++)
                if (n.Contains(s_belowGradeDecorTerms[i])) return true;
            if (t == root) break;
            t = t.parent;
        }
        return false;
    }

    private float GetPOIClearanceRadius(GameObject prefab)
    {
        // Шукаємо BoxCollider на кореневому об'єкті
        BoxCollider rootBox = prefab.GetComponent<BoxCollider>();
        if (rootBox != null)
        {
            float sx = rootBox.size.x * prefab.transform.localScale.x;
            float sz = rootBox.size.z * prefab.transform.localScale.z;
            // Теорема Піфагора: половина діагоналі (радіус описаного кола)
            return (Mathf.Sqrt(sx * sx + sz * sz) * 0.5f);
        }
        // Запасний варіант, якщо колайдера немає
        return 30f;
    }

    private IEnumerator SpawnExtractionCartsRoutine()
    {
        if (extractionCartPrefab == null) yield break;
        float w = terrain.terrainData.size.x; float l = terrain.terrainData.size.z; int spawnedCarts = 0;
        float absoluteWaterHeight = transform.position.y + (depth * waterLevel);

        float startTime = Time.realtimeSinceStartup;

        for (int i = 0; i < 5000; i++)
        {
            if (Time.realtimeSinceStartup - startTime > MAX_FRAME_TIME) { yield return null; startTime = Time.realtimeSinceStartup; }

            if (spawnedCarts >= extractionCartsAmount) break;
            try
            {
                float px = GetRandomRange(30f, w - 30f); float pz = GetRandomRange(30f, l - 30f);
                if (terrain.terrainData.GetSteepness(px / w, pz / l) < 8f)
                {
                    float worldX = transform.position.x + px; float worldZ = transform.position.z + pz;
                    float worldY = terrain.SampleHeight(new Vector3(worldX, 0, worldZ)) + transform.position.y;
                    if (worldY <= absoluteWaterHeight + 2f) continue;
                    Vector3 spawnPos = new Vector3(worldX, worldY, worldZ);
                    if (IsPositionClear(spawnPos, cartClearanceRadius))
                    {
                        roadTargets.Add(spawnPos);
                        Instantiate(extractionCartPrefab, spawnPos, Quaternion.Euler(0, GetRandomRange(0f, 360f), 0));
                        forbiddenZones.Add(spawnPos);
                        spawnedCarts++;
                    }
                }
            }
            catch (System.Exception) { }
        }
    }

    private IEnumerator SpawnBorderMountainsRoutine()
    {
        if (borderMountainPrefabs == null || borderMountainPrefabs.Length == 0) yield break;
        Transform borderContainer = new GameObject("BorderMountainsContainer").transform; borderContainer.SetParent(this.transform);
        float w = terrain.terrainData.size.x; float l = terrain.terrainData.size.z; float startTime = Time.realtimeSinceStartup;

        for (float x = -borderOffset; x <= w + borderOffset; x += borderSpacing)
        {
            SpawnSingleBorderMountain(new Vector3(x, 0, -borderOffset), borderContainer, w, l); SpawnSingleBorderMountain(new Vector3(x, 0, l + borderOffset), borderContainer, w, l);
            if (Time.realtimeSinceStartup - startTime > MAX_FRAME_TIME) { yield return null; startTime = Time.realtimeSinceStartup; }
        }
        for (float z = -borderOffset; z <= l + borderOffset; z += borderSpacing)
        {
            SpawnSingleBorderMountain(new Vector3(-borderOffset, 0, z), borderContainer, w, l); SpawnSingleBorderMountain(new Vector3(w + borderOffset, 0, z), borderContainer, w, l);
            if (Time.realtimeSinceStartup - startTime > MAX_FRAME_TIME) { yield return null; startTime = Time.realtimeSinceStartup; }
        }
    }

    private float GetTemperature(float normX, float normZ)
    {
        if (isRegionMissionCached)
        {
            if (regionBiomeTypeCached == 1) return 0.8f;
            if (regionBiomeTypeCached == 2) return 0.2f;
            return 0.5f;
        }
        return Mathf.PerlinNoise(normX * globalBiomeScale + offsetX + 500f, normZ * globalBiomeScale + offsetZ + 500f);
    }

    private void SpawnSingleBorderMountain(Vector3 localPos, Transform container, float w, float l)
    {
        GameObject prefab = GetRandomPrefab(borderMountainPrefabs); if (prefab == null) return;
        try
        {
            float clampedX = Mathf.Clamp(localPos.x, 0, w); float clampedZ = Mathf.Clamp(localPos.z, 0, l);
            float worldX = transform.position.x + localPos.x; float worldZ = transform.position.z + localPos.z;
            float y = terrain.SampleHeight(new Vector3(transform.position.x + clampedX, 0, transform.position.z + clampedZ)) + transform.position.y;
            GameObject mnt = Instantiate(prefab, new Vector3(worldX, y - 5f, worldZ), Quaternion.Euler(0, GetRandomRange(0f, 360f), 0), container);
            float scaleMul = GetRandomRange(borderMinScale, borderMaxScale);
            mnt.transform.localScale *= scaleMul;

            // Strip colliders on border-mountain instances — the mountains
            // are pure background decoration and the map edge is already
            // walled off by the terrain. A 3–6× scaled mesh with its
            // collider stretched to match was invisibly encroaching
            // several metres into the playable area.
            foreach (var col in mnt.GetComponentsInChildren<Collider>()) Destroy(col);

            bool isSnow = false;
            bool isDesert = false;
            float normHeight = (y - transform.position.y) / depth;
            float temp = GetTemperature(clampedX / w, clampedZ / l);

            if (normHeight > 0.65f) isSnow = true;
            else if (normHeight <= waterLevel + 0.02f) isDesert = true;
            else { if (temp >= 0.65f) isDesert = true; else if (temp <= 0.35f) isSnow = true; }

            Color rockColor = isDesert ? desertRockColor : (isSnow ? snowRockColor : forestRockColor);
            ApplyBiomeColor(mnt, rockColor, true);
        }
        catch (System.Exception) { }
    }

    private static Collider[] overlapResults = new Collider[20];

    private bool IsPositionClear(Vector3 position, float radius)
    {
        int count = Physics.OverlapSphereNonAlloc(position + Vector3.up * 1.5f, radius, overlapResults);
        for (int i = 0; i < count; i++)
        {
            Collider col = overlapResults[i];
            if (col.GetComponent<TerrainCollider>() != null || col.GetComponent<Terrain>() != null) continue;
            if (col.isTrigger) { if (col.GetComponentInParent<RegionManager>() != null) return false; continue; }
            return false;
        }
        return true;
    }

    private bool IsWoodOrTrunk(string name)
    {
        return name.IndexOf("trunk", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
               name.IndexOf("wood", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
               name.IndexOf("bark", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
               name.IndexOf("branch", System.StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private bool IsVFX(string name)
    {
        return name.IndexOf("vfx", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
               name.IndexOf("smoke", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
               name.IndexOf("effect", System.StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private GameObject GetRandomPrefab(GameObject[] array) => (array == null || array.Length == 0) ? null : array[GetRandomRangeInt(0, array.Length)];

    private class POIVirtualData
    {
        public Vector3 worldPosition;
        public GameObject prefab;
        public POISettings settings;
    }

    // ==========================================
    // СИСТЕМА ДОРІГ (НОВІ МЕТОДИ)
    // ==========================================
    private class PathNode
    {
        public int x, z;
        public float g, h;
        public PathNode parent;
        public float f => g + h;
        public PathNode(int _x, int _z) { x = _x; z = _z; }
    }

    private List<Vector3> FindAStarPathToNetwork(Vector2Int start, Vector2Int totemNode, bool[,] roadNetwork, int gridW, int gridL, float cellSize, float absWaterH)
    {
        if (roadNetwork[start.x, start.y]) return null;

        List<PathNode> openSet = new List<PathNode>();
        HashSet<Vector2Int> closedSet = new HashSet<Vector2Int>();
        Dictionary<Vector2Int, PathNode> nodeMap = new Dictionary<Vector2Int, PathNode>();

        PathNode startNode = new PathNode(start.x, start.y) { g = 0, h = Vector2Int.Distance(start, totemNode) };
        openSet.Add(startNode); nodeMap[start] = startNode;

        int[] dx = { -1, 1, 0, 0, -1, 1, -1, 1 }; int[] dz = { 0, 0, -1, 1, -1, -1, 1, 1 };
        int iterations = 0;

        // ФІКС: Жорсткий безпечний ліміт. 15 000 ітерацій — це максимум ~0.05 сек завантаження.
        // Якщо за цей час шлях не знайдено (гора-лабіринт), дорога просто скасовується, а Unity НЕ висне.
        while (openSet.Count > 0 && iterations < 15000)
        {
            iterations++;
            int minIndex = 0;
            for (int i = 1; i < openSet.Count; i++) if (openSet[i].f < openSet[minIndex].f) minIndex = i;

            PathNode current = openSet[minIndex]; openSet.RemoveAt(minIndex); closedSet.Add(new Vector2Int(current.x, current.z));

            if (roadNetwork[current.x, current.z])
            {
                List<Vector3> path = new List<Vector3>();
                while (current != null)
                {
                    float wX = transform.position.x + (current.x * cellSize); float wZ = transform.position.z + (current.z * cellSize);
                    path.Add(new Vector3(wX, 0, wZ)); current = current.parent;
                }
                path.Reverse(); return path;
            }

            for (int i = 0; i < 8; i++)
            {
                int nx = current.x + dx[i]; int nz = current.z + dz[i]; Vector2Int nPos = new Vector2Int(nx, nz);
                if (nx < 0 || nx >= gridW || nz < 0 || nz >= gridL || closedSet.Contains(nPos)) continue;

                float wX = transform.position.x + (nx * cellSize); float wZ = transform.position.z + (nz * cellSize);
                float h = terrain.SampleHeight(new Vector3(wX, 0, wZ)) + transform.position.y;

                // Блокуємо тільки глибоку воду
                if (h < absWaterH + 0.5f) continue;

                float steepness = terrain.terrainData.GetSteepness((float)nx / gridW, (float)nz / gridL);
                // Відкидаємо тільки відверті 90-градусні скелі, щоб не витрачати туди ітерації пошуку
                if (steepness > 40f) continue;

                float moveCost = (i < 4) ? 1f : 1.414f;
                float steepPenalty = steepness * 2f;
                float newG = current.g + moveCost + steepPenalty;

                if (!nodeMap.ContainsKey(nPos) || newG < nodeMap[nPos].g)
                {
                    PathNode neighbor = new PathNode(nx, nz) { g = newG, h = Vector2Int.Distance(nPos, totemNode), parent = current };
                    openSet.Add(neighbor); nodeMap[nPos] = neighbor;
                }
            }
        }
        return null; // Шлях занадто складний або заблокований, скасовуємо цю дорогу
    }

    // Carves the road into the SHARED heights array (caller owns the single
    // GetHeights/SetHeights). Previously each road did its own full-heightmap
    // GetHeights + SetHeights — a multi-MB read/write and terrain recompute per
    // road, ~20× per map. Now the caller reads once, passes the array through
    // every road, and writes once.
    private void CarveAndRasterizeRoad(SplineContainer sc, float[,] heights)
    {
        TerrainData td = terrain.terrainData; int res = td.heightmapResolution; int aWidth = td.alphamapWidth; int aHeight = td.alphamapHeight;
        float splLen = sc.CalculateLength(); float step = 2f;
        int stepIndex = 0;

        for (float d = 0; d < splLen; d += step, stepIndex++)
        {
            sc.Evaluate(d / splLen, out float3 p, out float3 tan, out float3 up);
            Vector3 worldPos = sc.transform.TransformPoint(new Vector3(p.x, p.y, p.z));
            // Register a forbidden point only every ~8th step. The exclusion
            // radius is 18 m and steps are 2 m apart, so one sphere every 16 m
            // still overlaps continuously along the road — but the biome loop
            // then scans a FRACTION of the points (it ran up to 60k× over the
            // full list, which roads had been flooding with a point every 2 m).
            if ((stepIndex & 7) == 0) forbiddenZones.Add(worldPos);

            int ax = Mathf.Clamp(Mathf.RoundToInt(((worldPos.x - transform.position.x) / td.size.x) * aWidth), 0, aWidth - 1);
            int ay = Mathf.Clamp(Mathf.RoundToInt(((worldPos.z - transform.position.z) / td.size.z) * aHeight), 0, aHeight - 1);
            int radA = Mathf.Max(1, Mathf.RoundToInt((roadWidth / td.size.x) * aWidth));

            for (int y = -radA; y <= radA; y++) for (int x = -radA; x <= radA; x++)
                {
                    int sx = ax + x, sy = ay + y; if (sx < 0 || sx >= aWidth || sy < 0 || sy >= aHeight) continue;
                    float dist = Mathf.Sqrt(x * x + y * y); if (dist > radA) continue;
                    float strength = 1f - Mathf.SmoothStep(0.2f, 1f, dist / radA);
                    if (strength > roadBlendMap[sy, sx]) roadBlendMap[sy, sx] = strength;
                }

            int cx = Mathf.Clamp(Mathf.RoundToInt(((worldPos.x - transform.position.x) / td.size.x) * (res - 1)), 0, res - 1);
            int cy = Mathf.Clamp(Mathf.RoundToInt(((worldPos.z - transform.position.z) / td.size.z) * (res - 1)), 0, res - 1);
            int radH = Mathf.Max(1, Mathf.RoundToInt((roadWidth * 0.8f / td.size.x) * res));

            float centerH = heights[cy, cx];
            for (int y = -radH; y <= radH; y++) for (int x = -radH; x <= radH; x++)
                {
                    int sx = cx + x, sy = cy + y; if (sx < 0 || sx >= res || sy < 0 || sy >= res) continue;
                    float dist = Mathf.Sqrt(x * x + y * y); if (dist > radH) continue;
                    float t = Mathf.SmoothStep(0f, 1f, dist / radH);
                    heights[sy, sx] = Mathf.Lerp(centerH, heights[sy, sx], t);
                }
        }
        // NOTE: no SetHeights here — the caller writes the shared array once.
    }

    // Adjust the intended spawn position so the visual BOTTOM of the
    // prefab sits on the terrain surface, rather than the pivot. Same
    // trick SpawnRegionTotemRoutine uses for the main totem — the road-
    // side altar prefab has its pivot at the visual centre, which was
    // making the altar float ~1.5 m above ground.
    // Falls back to the raw spawn position if the prefab has no
    // collider/renderer we can measure.
    private Vector3 GroundPrefabToTerrain(GameObject prefab, Vector3 desiredWorldPos)
    {
        if (prefab == null) return desiredWorldPos;

        float bottomOffset = 0f;
        // Prefer a Collider — that's the physics reality players hit.
        BoxCollider box = prefab.GetComponent<BoxCollider>();
        if (box != null)
        {
            float sy = box.size.y * prefab.transform.localScale.y;
            float cy = box.center.y * prefab.transform.localScale.y;
            bottomOffset = cy - (sy * 0.5f); // signed: negative when the collider dips below pivot
        }
        else
        {
            // Fall back to renderer bounds. Not perfect (relies on the mesh
            // in its neutral pose) but better than "float wherever the pivot
            // lands".
            Renderer r = prefab.GetComponentInChildren<Renderer>();
            if (r != null)
            {
                // r.bounds is world-space at authoring time, not necessarily
                // representative for a prefab asset. Use localBounds via
                // sharedMesh instead when available.
                MeshFilter mf = r.GetComponent<MeshFilter>();
                if (mf != null && mf.sharedMesh != null)
                {
                    Bounds b = mf.sharedMesh.bounds;
                    bottomOffset = b.min.y * prefab.transform.localScale.y;
                }
            }
        }

        Vector3 result = desiredWorldPos;
        result.y -= bottomOffset; // pushing pivot up by -bottomOffset places the bottom at ground
        return result;
    }

    // Ground an ALREADY-INSTANTIATED decoration/altar by the lowest point of
    // its real, combined mesh geometry — measured live on the instance.
    //
    // Why not GroundPrefabToTerrain for altars: the roadside altar prefab's
    // root BoxCollider is a small interaction box near the pivot (size.y≈0.1,
    // center≈pivot), NOT the visual footprint. Grounding by that collider
    // bottom leaves the altar's tall base mesh buried — the reported "totem
    // spawns half (or more) underground" bug. The prefab also has no
    // renderers in its own file (they live in nested prefabs), so the
    // renderer fallback never fired. Measuring the instantiated object's
    // MeshRenderer/SkinnedMeshRenderer bounds sees the actual geometry
    // regardless of hierarchy and grounds it every time.
    //
    // `embed` sinks the base a few cm into the terrain so a wide base doesn't
    // read as floating on gently uneven ground (altars are already filtered
    // to sub-18° slopes, so this stays invisible).
    private void SnapAltarInstanceToGround(GameObject go, float targetGroundY, float embed = 0.2f)
    {
        if (go == null) return;

        float lowestY = float.MaxValue;
        bool any = false;
        foreach (var mr in go.GetComponentsInChildren<MeshRenderer>(false))
        {
            if (mr == null || !mr.enabled) continue;
            lowestY = Mathf.Min(lowestY, mr.bounds.min.y);
            any = true;
        }
        foreach (var smr in go.GetComponentsInChildren<SkinnedMeshRenderer>(false))
        {
            if (smr == null || !smr.enabled) continue;
            lowestY = Mathf.Min(lowestY, smr.bounds.min.y);
            any = true;
        }
        // No solid renderers found (all nested under inactive roots or the
        // prefab is collider-only) — leave the caller's grounded Y as-is.
        if (!any) return;

        float delta = (targetGroundY - embed) - lowestY;
        go.transform.position += new Vector3(0f, delta, 0f);
    }

    private IEnumerator SpawnRoadDecorationsRoutine()
    {
        // The altar-at-dead-end spawn is intentionally NOT gated on
        // roadEdgeDecorations — an unassigned decoration array used to
        // early-out here, silently killing the altar totem spawn too.
        bool hasDecor = roadEdgeDecorations != null && roadEdgeDecorations.Length > 0;
        bool hasAltars = altarPrefabs != null && altarPrefabs.Length > 0;
        bool hasDeadEndFallback = deadEndAssets != null && deadEndAssets.Length > 0;

        if (!hasDecor && !hasAltars && !hasDeadEndFallback) yield break;
        if (roadSplines == null || roadSplines.Count == 0) yield break;

        Transform decorContainer = new GameObject("RoadDecorations").transform; decorContainer.SetParent(this.transform);

        float mapW = terrain.terrainData.size.x; float mapL = terrain.terrainData.size.z;
        float absWaterH = transform.position.y + (depth * waterLevel);
        float startTime = Time.realtimeSinceStartup;

        int spawnedAltars = 0;

        // Only HALF of each event type is allowed to claim a road dead-end
        // (dead-ends sit at the map border, so an edge-only quota made every
        // altar / caged-ally cluster around the rim — the player never saw one
        // mid-map). The remaining share is filled by the interior random-spline
        // pass below, spreading the encounters across the whole terrain.
        // Dead-ends sit at the MAP BORDER, so anything placed here reads as an
        // "edge-only" event the player rarely reaches. Cap the edge pass at ONE
        // of each type and let the interior fallback place the rest along
        // mid-map road stretches — that's where the player actually travels, and
        // it's what makes caged-ally rescues (and their minimap markers) show up
        // in play. (Higher quotas keep the overall count up; this only shifts
        // WHERE they go.)
        int deadEndAltarCap = 1;
        int deadEndCagedCap = 1;

        // ДІАГНОСТИКА: Перевіряємо чи не порожній масив в Інспекторі
        if (!hasAltars)
        {
            Debug.LogWarning("⚠️ [Smart Roads] Масив 'Altar Prefabs' ПОРОЖНІЙ! Вівтарі не з'являться. Закинь префаб в Інспекторі!");
        }
        GameLog.Info($"[Smart Roads] Decor start — hasDecor={hasDecor} hasAltars={hasAltars} hasDeadEndFallback={hasDeadEndFallback} roadSplines={roadSplines.Count} deadEndTargets={deadEndTargets.Count}");

        foreach (SplineContainer sc in roadSplines)
        {
            if (hasDecor)
            {
                float len = sc.CalculateLength();
                float startOffset = Mathf.Min(10f, len * 0.15f);
                float endOffset = Mathf.Min(10f, len * 0.15f);

                for (float d = startOffset; d < len - endOffset; d += 35f)
                {
                    if (GetRandomFloat() > roadDecorSpawnChance) continue;
                    sc.Evaluate(d / len, out float3 p, out float3 tan, out float3 up);
                    Vector3 wPos = sc.transform.TransformPoint(new Vector3(p.x, p.y, p.z));
                    Vector3 wTan = sc.transform.TransformDirection(new Vector3(tan.x, tan.y, tan.z)).normalized;
                    Vector3 right = Vector3.Cross(Vector3.up, wTan).normalized;
                    float side = GetRandomFloat() > 0.5f ? 1f : -1f;

                    Vector3 spawnPos = wPos + right * side * (roadWidth * 0.85f);

                    if (spawnPos.x < transform.position.x + 25f || spawnPos.x > transform.position.x + mapW - 25f ||
                        spawnPos.z < transform.position.z + 25f || spawnPos.z > transform.position.z + mapL - 25f)
                        continue;

                    spawnPos.y = terrain.SampleHeight(spawnPos) + transform.position.y;
                    float spawnSteep = terrain.terrainData.GetSteepness((spawnPos.x - transform.position.x) / mapW, (spawnPos.z - transform.position.z) / mapL);

                    if (spawnPos.y > absWaterH + 2f && spawnSteep < 15f && IsPositionClear(spawnPos, 1.5f))
                    {
                        Instantiate(GetRandomPrefab(roadEdgeDecorations), spawnPos, Quaternion.LookRotation(right * -side), decorContainer);
                        forbiddenZones.Add(spawnPos);
                    }
                }
            }

            bool isDeadEnd = false;
            Vector3 tipPos = Vector3.zero;
            Vector3 tipTan = Vector3.forward;

            BezierKnot firstKnot = sc.Spline[0];
            Vector3 firstP = sc.transform.TransformPoint(new Vector3(firstKnot.Position.x, firstKnot.Position.y, firstKnot.Position.z));

            BezierKnot lastKnot = sc.Spline[sc.Spline.Count - 1];
            Vector3 lastP = sc.transform.TransformPoint(new Vector3(lastKnot.Position.x, lastKnot.Position.y, lastKnot.Position.z));

            // Tolerance was 15m but a road spline's start knot lands on
            // the A*-grid-snapped cell (up to cellSize=8m off the true
            // target) PLUS the optional deadEndTipExtensionMeters push.
            // 25m covers that comfortably without allowing a non-dead-end
            // road to be tagged incorrectly (POI roads terminate at the
            // POI itself, which is > 25m from any dead-end target thanks
            // to deadEndMinSeparation).
            const float DEAD_END_MATCH_TOLERANCE = 25f;
            foreach (var de in deadEndTargets)
            {
                if (Vector3.Distance(firstP, de) < DEAD_END_MATCH_TOLERANCE)
                {
                    isDeadEnd = true;
                    sc.Evaluate(0f, out float3 p0, out float3 t0, out float3 u0);
                    tipPos = sc.transform.TransformPoint(new Vector3(p0.x, p0.y, p0.z));
                    tipTan = -sc.transform.TransformDirection(new Vector3(t0.x, t0.y, t0.z)).normalized;
                    break;
                }
                else if (Vector3.Distance(lastP, de) < DEAD_END_MATCH_TOLERANCE)
                {
                    isDeadEnd = true;
                    sc.Evaluate(1f, out float3 p1, out float3 t1, out float3 u1);
                    tipPos = sc.transform.TransformPoint(new Vector3(p1.x, p1.y, p1.z));
                    tipTan = sc.transform.TransformDirection(new Vector3(t1.x, t1.y, t1.z)).normalized;
                    break;
                }
            }

            if (isDeadEnd)
            {
                Vector3 endSpawn = tipPos + tipTan * 6f; // Відступаємо трохи далі від кінця дороги

                bool insideMap = endSpawn.x > transform.position.x + 20f && endSpawn.x < transform.position.x + mapW - 20f &&
                                 endSpawn.z > transform.position.z + 20f && endSpawn.z < transform.position.z + mapL - 20f;

                if (insideMap)
                {
                    endSpawn.y = terrain.SampleHeight(endSpawn) + transform.position.y;

                    // Caged-ally event: a captive guarded by a skeleton pack. Takes
                    // priority on some dead-ends (chance-gated + capped) — clearing
                    // the guards frees an ally that fights for the player.
                    // Clearance guard: the old code force-placed the event at the
                    // dead-end tip with NO overlap test, so altars / cages ended
                    // up buried inside cliff rocks at the terrain edge. Skip a
                    // blocked or steep tip — the interior pass will make up the
                    // count on clear ground instead.
                    float endNx = (endSpawn.x - transform.position.x) / mapW;
                    float endNz = (endSpawn.z - transform.position.z) / mapL;
                    bool tipUsable = endSpawn.y > absWaterH + 2f
                                     && terrain.terrainData.GetSteepness(endNx, endNz) <= deadEndMaxSteepness
                                     && IsPositionClear(endSpawn, 3f);

                    bool spawnedCaged = false;
                    if (tipUsable
                        && spawnedCagedAllies < deadEndCagedCap
                        && cagedAllyGuardPrefabs != null && cagedAllyGuardPrefabs.Length > 0
                        && cagedAllyPrefab != null
                        && GetRandomFloat() < cagedAllyChance)
                    {
                        GameObject go = new GameObject("CagedAllyEvent");
                        go.transform.SetParent(decorContainer);
                        go.transform.position = endSpawn;
                        CagedAllyEvent ev = go.AddComponent<CagedAllyEvent>();
                        ev.guardPrefabs = cagedAllyGuardPrefabs;
                        ev.allyPrefab = cagedAllyPrefab;
                        spawnedCagedAllies++;
                        forbiddenZones.Add(endSpawn);
                        spawnedCaged = true;
                        GameLog.Info($"[Smart Roads] Caged-ally event spawned at {endSpawn} ({spawnedCagedAllies}/{maxCagedAllies}).");
                    }

                    if (spawnedCaged) { /* dead-end taken by the caged-ally event */ }
                    else if (tipUsable && spawnedAltars < deadEndAltarCap && altarPrefabs != null && altarPrefabs.Length > 0)
                    {
                        GameObject prefab = GetRandomPrefab(altarPrefabs);
                        Vector3 grounded = GroundPrefabToTerrain(prefab, endSpawn);
                        GameObject inst = Instantiate(prefab, grounded, Quaternion.LookRotation(-tipTan), decorContainer);
                        // Re-ground by the live mesh — the prefab collider is a
                        // small interaction box, so pivot-math alone buried the altar.
                        SnapAltarInstanceToGround(inst, endSpawn.y);
                        spawnedAltars++;
                        forbiddenZones.Add(inst.transform.position);
                        GameLog.Info($"🎯 [Smart Roads] Вівтар успішно заспавнено! Координати: {inst.transform.position} ({spawnedAltars}/{altarsAmount})");
                    }
                    else if (deadEndAssets != null && deadEndAssets.Length > 0)
                    {
                        GameObject prefab = GetRandomPrefab(deadEndAssets);
                        Vector3 grounded = GroundPrefabToTerrain(prefab, endSpawn);
                        GameObject inst = Instantiate(prefab, grounded, Quaternion.LookRotation(-tipTan), decorContainer);
                        SnapAltarInstanceToGround(inst, endSpawn.y);
                        forbiddenZones.Add(inst.transform.position);
                    }
                }
                else
                {
                    Debug.LogWarning($"[Smart Roads] Тупик знайдений, але місце спавну ({endSpawn}) впало за межі мапи. Пропускаю.");
                }
            }
            if (Time.realtimeSinceStartup - startTime > MAX_FRAME_TIME) { yield return null; startTime = Time.realtimeSinceStartup; }
        }
        GameLog.Info($"[Smart Roads] Decor done — altars {spawnedAltars}/{altarsAmount} spawned. deadEndTargets={deadEndTargets.Count} roadSplines={roadSplines.Count}.");

        // Safety net — the dead-end matcher misses on maps where:
        //   * every dead-end target was rejected (steep terrain, water,
        //     too close to base) and deadEndTargets stayed empty, OR
        //   * A* couldn't path to any dead-end target, OR
        //   * the road splines' knot positions drifted > 25m from the
        //     tolerance window.
        // Without altars the mid-map mini-boss loop is missing — the
        // player never gets bonus diamonds/xp from roadside encounters.
        // Fallback: pick random points along random road splines
        // (comfortably off-road, off-water, off-flat-terrain) so at
        // least `altarsAmount` show up per map.
        bool cagedReady = cagedAllyPrefab != null && cagedAllyGuardPrefabs != null && cagedAllyGuardPrefabs.Length > 0;
        bool needInterior = (hasAltars && spawnedAltars < altarsAmount)
                          || (cagedReady && spawnedCagedAllies < maxCagedAllies);
        if (needInterior && roadSplines.Count > 0)
        {
            Debug.LogWarning($"[Smart Roads] Interior pass: altars {spawnedAltars}/{altarsAmount}, caged {spawnedCagedAllies}/{maxCagedAllies}. Placing the remainder along mid-map road stretches.");
            // Wrap the whole fallback in try/catch — an exception here
            // (null spline, GroundPrefabToTerrain edge case) must NOT
            // kill the parent GenerateWorld coroutine, or IsGenerationDone
            // never gets set and the survival timer freezes at 00:00.
            try
            {
                int fallbackAttempts = 0;
                int remaining = Mathf.Max(0, altarsAmount - spawnedAltars) + Mathf.Max(0, maxCagedAllies - spawnedCagedAllies);
                int fallbackMax = Mathf.Max(30, remaining * 30);
                while (((hasAltars && spawnedAltars < altarsAmount) || (cagedReady && spawnedCagedAllies < maxCagedAllies))
                       && fallbackAttempts++ < fallbackMax)
                {
                    // Fully-qualify UnityEngine.Random — the file also
                    // imports Unity.Mathematics, whose Random type
                    // collides with UnityEngine.Random on unqualified use.
                    SplineContainer sc = roadSplines[UnityEngine.Random.Range(0, roadSplines.Count)];
                    if (sc == null || sc.Spline == null) continue;
                    float len = sc.CalculateLength();
                    if (len < 20f) continue;
                    // Sample somewhere along the middle of the spline, then
                    // step OFF the road by ~roadWidth.
                    float sample = UnityEngine.Random.Range(0.25f, 0.75f);
                    sc.Evaluate(sample, out float3 p, out float3 tan, out float3 up);
                    Vector3 wPos = sc.transform.TransformPoint(new Vector3(p.x, p.y, p.z));
                    Vector3 wTan = sc.transform.TransformDirection(new Vector3(tan.x, tan.y, tan.z)).normalized;
                    Vector3 right = Vector3.Cross(Vector3.up, wTan).normalized;
                    float side = UnityEngine.Random.value > 0.5f ? 1f : -1f;
                    Vector3 spawnPos = wPos + right * side * (roadWidth * 1.4f);

                    // Basic map-edge + water + slope + forbidden-zone guards
                    if (spawnPos.x < transform.position.x + 30f || spawnPos.x > transform.position.x + mapW - 30f) continue;
                    if (spawnPos.z < transform.position.z + 30f || spawnPos.z > transform.position.z + mapL - 30f) continue;
                    spawnPos.y = terrain.SampleHeight(spawnPos) + transform.position.y;
                    if (spawnPos.y <= absWaterH + 2f) continue;
                    // Reject spots on steep terrain — an altar on a slope
                    // reads as floating / half-buried even after grounding.
                    float nx = (spawnPos.x - transform.position.x) / mapW;
                    float nz = (spawnPos.z - transform.position.z) / mapL;
                    if (terrain.terrainData.GetSteepness(nx, nz) > deadEndMaxSteepness) continue;
                    // Keep altars the SAME minimum distance apart as the
                    // dead-end ones (was 25m — far too clustered). Uses
                    // deadEndMinSeparation so all altars honour one rule.
                    // Interior spots use a LOOSER separation than the edge
                    // dead-ends (60m rejected almost every interior candidate on
                    // smaller maps, so the fallback pass placed nothing). 0.6x
                    // still keeps events comfortably apart.
                    float interiorSeparation = deadEndMinSeparation * 0.6f;
                    bool nearForbidden = false;
                    for (int k = 0; k < forbiddenZones.Count; k++)
                    {
                        if (Vector3.Distance(spawnPos, forbiddenZones[k]) < interiorSeparation) { nearForbidden = true; break; }
                    }
                    if (nearForbidden) continue;
                    // Same rock/obstacle guard as the dead-end tips — no more
                    // events buried inside cliffs or props.
                    if (!IsPositionClear(spawnPos, 3f)) continue;

                    // Alternate between caged-ally events and altars so both
                    // types reach the map interior. Prefer whichever is further
                    // from its quota; coin-flip when both still need one.
                    bool wantCaged = cagedReady && spawnedCagedAllies < maxCagedAllies
                                     && (!hasAltars || spawnedAltars >= altarsAmount || UnityEngine.Random.value < 0.5f);

                    if (wantCaged)
                    {
                        GameObject cageGo = new GameObject("CagedAllyEvent");
                        cageGo.transform.SetParent(decorContainer);
                        cageGo.transform.position = spawnPos;
                        CagedAllyEvent ev = cageGo.AddComponent<CagedAllyEvent>();
                        ev.guardPrefabs = cagedAllyGuardPrefabs;
                        ev.allyPrefab = cagedAllyPrefab;
                        spawnedCagedAllies++;
                        forbiddenZones.Add(spawnPos);
                        GameLog.Info($"[Smart Roads] Interior caged-ally spawned at {spawnPos} ({spawnedCagedAllies}/{maxCagedAllies}).");
                    }
                    else if (hasAltars && spawnedAltars < altarsAmount)
                    {
                        GameObject prefab = GetRandomPrefab(altarPrefabs);
                        if (prefab == null) continue;
                        Vector3 grounded = GroundPrefabToTerrain(prefab, spawnPos);
                        GameObject inst = Instantiate(prefab, grounded, Quaternion.LookRotation(-wTan), decorContainer);
                        SnapAltarInstanceToGround(inst, spawnPos.y);
                        spawnedAltars++;
                        forbiddenZones.Add(inst.transform.position);
                        GameLog.Info($"[Smart Roads] Interior altar spawned at {inst.transform.position} ({spawnedAltars}/{altarsAmount}).");
                    }
                }
                if (spawnedAltars < altarsAmount || spawnedCagedAllies < maxCagedAllies)
                    Debug.LogWarning($"[Smart Roads] Interior pass exhausted — altars {spawnedAltars}/{altarsAmount}, caged {spawnedCagedAllies}/{maxCagedAllies}. Map likely too constrained (water/edges/slopes).");
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[Smart Roads] Altar fallback threw — swallowed to keep generation alive: {e.Message}");
            }
        }
    }
}