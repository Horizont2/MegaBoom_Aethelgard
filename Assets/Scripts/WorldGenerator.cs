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
    // Fired once the world (terrain + rivers + locations + roads) is fully
    // built. Systems that must position themselves against the FINAL terrain —
    // e.g. hand-authored location props that need to snap to the ground after
    // it's been carved/flattened — subscribe to this instead of guessing a delay.
    public static event System.Action OnWorldGenerationComplete;

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

    // The world Y that water sits at. Computed in half a dozen places from
    // `transform.position.y + depth * waterLevel`; anything placing objects needs
    // it too, and a second hand-rolled copy is a second thing to get wrong.
    public float AbsoluteWaterHeight => transform.position.y + (depth * waterLevel);
    public Material waterMaterial;
    [Tooltip("Surface used instead of waterMaterial in a winter region. Leave empty and the water stays liquid — there is no ice material in the project yet, so this is opt-in rather than a silent change.")]
    public Material winterIceMaterial;

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
    public float cagedAllyChance = 0.95f;
    [Tooltip("Freeing prisoners is the one thing on the map that hands the player a fighting companion, so a region with only a couple of them barely reads as a mechanic. Raised from 4.")]
    public int maxCagedAllies = 7;
    private int spawnedCagedAllies = 0;

    [Header("Ambient Birds")]
    [Tooltip("Flock prefabs — the Zacxophone bird prefabs work directly. Handed to AmbientBirdLife, which flies them around the player and startles them out of cover. Leave empty to skip birds entirely.")]
    public GameObject[] ambientCrowPrefabs;
    [Tooltip("How many flocks are ALIVE AT ONCE. They are recycled around the player rather than scattered over the map, so this is the entire budget however far the player walks — 3-5 is plenty.")]
    public int ambientCrowCount = 4;
    [Tooltip("Cruise height above the ground beneath them. Low enough to sit in the visible band of sky for a third-person camera; they are deliberately kept out of the zenith, which is off-screen.")]
    public float ambientCrowMinHeight = 20f;
    public float ambientCrowMaxHeight = 38f;

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

    [Tooltip("ON: paint BUSHES + MUSHROOMS onto the terrain (batched, GPU-instanced) instead of spawning up to Max Bushes And Mushroom (~2500) GameObjects — big load-time + FPS win. Safe because bushes/mushrooms are decorative (non-farmable). Trees are unaffected (still object trees / their own tree-painting flag). Needs biome bush/mushroom prefabs assigned; if none are, it falls back to objects automatically. Strip colliders from the bush/mushroom prefabs so painted decor doesn't block movement.")]
    public bool useTerrainVegetationPainting = true;

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

    // ==== WHAT ACTUALLY MAKES A WINTER REGION LOOK LIKE WINTER ====
    //
    // Not the colour. The winter region was already tinted, lit with a low cold
    // sun and blown over with ground drift, and it still read as "the summer map
    // painted white" — because GetTemperature returns a CONSTANT in a region
    // mission, so every single alphamap cell resolved to `temp <= 0.35` and got
    // weights[2] = 1. One texture, whole map, zero variation. A forest region at
    // least gets Perlin variation between grass, sand and snow; winter got a
    // white bedsheet.
    //
    // Real snow is not a coat of paint, it is a MATERIAL THAT MOVES. It slides
    // off anything steep, it is scoured off ridges and off every face that looks
    // into the wind, and all of it ends up piled in the hollows and on the lee
    // side of things. That is why a snowy landscape has enormous contrast — bare
    // dark rock a metre from a waist-deep drift — and why a uniform white one
    // looks fake even when every individual colour is correct.
    //
    // So the cover is computed from the terrain itself: slope, wind exposure,
    // how high the ground stands above its own neighbourhood, and noise for
    // patchiness. Where it survives you get snow; where it is stripped you get
    // the rock and dead grass underneath.
    [Header("Winter Snow Cover")]
    [Tooltip("Lay snow by slope, wind and shelter instead of covering the region uniformly. Off = the old flat white sheet.")]
    public bool winterSnowDrifts = true;
    [Tooltip("How much ground loses its snow. 0 = everything stays buried (the old look), 0.5 = a hard, scoured landscape with a lot of bare rock showing.")]
    [Range(0f, 0.7f)] public float winterBareGround = 0.28f;
    [Tooltip("Size of the drift patches. Lower = broad snowfields, higher = a restless, blotchy surface.")]
    [Range(1f, 20f)] public float winterDriftScale = 6f;
    [Tooltip("Which way the wind blows, in degrees. Faces looking into it are stripped; their lee sides collect. Matches WinterGroundDrift.windDegrees by default, so the snow blowing across the ground agrees with the way the snow on it is lying.")]
    public float winterWindDegrees = 30f;
    [Tooltip("How much the wind matters against slope and shelter. 0 = snow depth depends only on terrain shape.")]
    [Range(0f, 1f)] public float winterWindStrength = 0.65f;

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
    // Work budget per frame while generating. This used to be a fixed 15ms,
    // which on a static loading screen throws away half the machine: nothing is
    // being rendered that needs the other 85% of a frame. Raising it roughly
    // halves the number of frames generation takes.
    [Tooltip("Milliseconds of generation work per frame. Higher = faster loading, at the cost of the loading screen's own frame rate. 33ms still leaves the progress bar animating at ~30fps.")]
    [Range(8f, 100f)] public float generationBudgetMs = 33f;
    private float MAX_FRAME_TIME => generationBudgetMs * 0.001f;

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
    // Radius-aware no-vegetation discs around whole LOCATION footprints (xyz =
    // centre, w = radius). forbiddenZones only clears a fixed 18m around a point,
    // which left trees growing inside big castle/village footprints — these
    // cover the entire location so nothing spawns inside it.
    private readonly List<Vector4> locationExclusions = new List<Vector4>();

    // Where an EVENT already stands — altars, caged allies. Separate from
    // forbiddenZones on purpose.
    //
    // forbiddenZones is "do not put scenery here", and roads flood it with a
    // point every sixteen metres along every road. Roadside events are placed
    // seven metres from a road centreline BY DESIGN, so measuring their spacing
    // against that list asks every candidate whether it is too close to the very
    // road it is supposed to be standing beside. The answer was always yes, and
    // the interior pass placed nothing, ever — see the note where this is used.
    private readonly List<Vector3> eventSpots = new List<Vector3>();

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
        TagAsNature(go);
    }

    [Tooltip("Put spawned decoration on the Nature layer so CameraCulling's per-layer distance actually applies to it. Nothing was ever assigned that layer, so the culling distance was doing nothing and every rock, bush and prop rendered out to the camera's far plane.")]
    public bool tagDecorationAsNature = true;

    private static int s_natureLayer = -2;

    // Only GameObjects WITHOUT a collider are moved. A GameObject with no
    // collider has no physics behaviour and cannot be hit by a raycast, so its
    // layer affects nothing but rendering — which keeps this free of any
    // collision-matrix or raycast-mask surprises.
    private void TagAsNature(GameObject go)
    {
        if (!tagDecorationAsNature || go == null) return;
        if (s_natureLayer == -2) s_natureLayer = LayerMask.NameToLayer("Nature");
        if (s_natureLayer < 0) return;

        foreach (var rnd in go.GetComponentsInChildren<Renderer>(true))
        {
            if (rnd == null || rnd is ParticleSystemRenderer) continue;
            if (rnd.GetComponent<Collider>() != null) continue;   // leave anything physical alone
            rnd.gameObject.layer = s_natureLayer;
        }
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

        // Proceed if EITHER painting mode is on. Trees and bushes/mushrooms are
        // registered independently so bushes can be painted (cheap decor) while
        // trees stay as farmable objects.
        if (!useTerrainTreePainting && !useTerrainVegetationPainting) return;
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
        // Trees only when tree-painting is explicitly on (they need to stay
        // farmable objects by default, so their prototypes aren't registered →
        // AddTerrainTree falls back to object trees).
        if (useTerrainTreePainting)
        {
            AddProtos(baseTrees);
            AddProtos(baseTreesAutumn);
            AddProtos(baseTreesWinter);
            AddProtos(deadTreesPrefabs);
        }
        // Bushes + mushrooms — decorative, safe to paint (batched → huge win).
        if (useTerrainVegetationPainting)
        {
            AddProtos(baseBushes);
            AddProtos(baseBushesAutumn);
            AddProtos(baseBushesWinter);
            AddProtos(baseMushrooms);
        }
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

        // Denser world: more bushes/mushrooms and more roads than the serialized
        // defaults. Vegetation painting makes the extra bushes/mushrooms cheap,
        // and extra dead-end roads give the map more to explore. Max() respects a
        // higher value set in the inspector.
        maxBushesAndMushroom = Mathf.Max(maxBushesAndMushroom, 4500);
        extraDeadEndRoads = Mathf.Max(extraDeadEndRoads, 12);
        // Reset statics that survive a scene reload — otherwise a "Try again"
        // after dying MID-RAID inherits a stuck state: AnyActivatingRightNow
        // left true makes every totem's Update early-return (no anchors spawn →
        // capture impossible), a stuck timeScale=0 freezes the survival timer and
        // hangs the anchor spawn coroutine, and IsSpawningBlocked stops enemies.
        Time.timeScale = 1f;
        RegionTotem.AnyActivatingRightNow = false;
        EnemySpawner.IsSpawningBlocked = false;
        EnemySpawner.AmbientThrottle = 1f;   // nor a throttle left over from last run's region
        EnemyAI.GlobalFreeze = false;   // never inherit a stuck victory-freeze
        EnemyAI.SuppressCombatVocals = false;   // nor a stuck trailer mute
        RegionManager.CinematicActive = false;   // nor a stuck victory-cinematic latch
        CursedTree.ResetWave(); // fresh region: don't let a stale bloom wave insta-bloom
        forbiddenZones.Clear();
        locationExclusions.Clear();
        eventSpots.Clear();
        generatedRivers.Clear();

        // Очищаємо списки доріг
        roadTargets.Clear();
        deadEndTargets.Clear();
        roadSplines.Clear();

        // 1. СПОЧАТКУ знаходимо Terrain
        terrain = GetComponent<Terrain>();

        // Clear any terrain holes punched for a self-contained location in a
        // previous generation (holes live on the shared TerrainData asset).
        ResetTerrainHoles();

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

    private int _vsyncBeforeGen;
    private int _targetFpsBeforeGen;
    private float _genStartTime;

    // VSync makes every `yield return null` in these routines wait for the
    // display's refresh, so a 15ms slice of work still costs a full 16.7ms
    // frame — generation spent about half its time idle. Nothing on a loading
    // screen needs to be synced to the display, so unlock it for the duration
    // and restore whatever the player had afterwards.
    private void BeginGenerationPerfMode()
    {
        _vsyncBeforeGen = QualitySettings.vSyncCount;
        _targetFpsBeforeGen = Application.targetFrameRate;
        QualitySettings.vSyncCount = 0;
        Application.targetFrameRate = -1;
        _genStartTime = Time.realtimeSinceStartup;
    }

    private void EndGenerationPerfMode()
    {
        QualitySettings.vSyncCount = _vsyncBeforeGen;
        Application.targetFrameRate = _targetFpsBeforeGen;

        // A BREAKDOWN, NOT A TOTAL.
        //
        // "Generation finished in 11.4s" tells you the loading screen is too
        // long and nothing about which of the fourteen phases to attack. Every
        // optimisation attempt then starts with a guess, and guessing wrong
        // costs an afternoon that buys 200ms. The table below names the worst
        // offender outright, which is the whole job of a profiler and takes
        // twenty lines to have permanently.
        float total = Time.realtimeSinceStartup - _genStartTime;
        var sb = new System.Text.StringBuilder();
        sb.Append($"[WorldGenerator] Generation finished in {total:0.00}s.");
        if (_phaseTimes.Count > 0)
        {
            string worst = "";
            float worstMs = 0f;
            foreach (var kv in _phaseTimes) if (kv.Value > worstMs) { worstMs = kv.Value; worst = kv.Key; }
            sb.Append($"  Slowest phase: {worst} at {worstMs:0}ms ({worstMs / Mathf.Max(1f, total * 1000f):P0} of the load).\n");
            foreach (var kv in _phaseTimes)
                sb.Append($"    {kv.Key,-28} {kv.Value,7:0} ms\n");
        }
        Debug.Log(sb.ToString());
        _phaseTimes.Clear();
    }

    private readonly System.Collections.Generic.Dictionary<string, float> _phaseTimes =
        new System.Collections.Generic.Dictionary<string, float>(16);

    // Runs one generation phase and records what it cost. Wrapping rather than
    // sprinkling timers keeps the sequence readable — the phase list below still
    // reads as a list of phases.
    // ==== ONE BAD PHASE MUST NOT COST THE WHOLE WORLD ====
    //
    // This was `yield return StartCoroutine(routine)`, which means an exception
    // anywhere inside any phase stops GenerateWorldRoutine dead. Every later
    // phase silently never happens AND — worse — execution never reaches
    // `IsGenerationDone = true`, so the survival timer's watchdog waits out its
    // full twelve-second fallback before starting. From the player's seat: a
    // half-dressed map and a clock stuck at 00:00, with nothing saying why.
    //
    // Stepping the inner routine by hand is the only way to put a try/catch
    // around a coroutine's execution — C# forbids `yield` inside a try that has
    // a catch clause, so wrapping the yield is not an option. A phase that
    // throws is now abandoned, named loudly in the console, and the rest of the
    // world still gets built.
    private IEnumerator Phase(string name, IEnumerator routine)
    {
        float t0 = Time.realtimeSinceStartup;

        while (true)
        {
            object current;
            try
            {
                if (!routine.MoveNext()) break;
                current = routine.Current;
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[WorldGenerator] Phase '{name}' threw and was abandoned. Everything it had " +
                               $"still to do is missing from this map; the remaining phases continue.\n{e}");
                break;
            }
            yield return current;
        }

        _phaseTimes[name] = (Time.realtimeSinceStartup - t0) * 1000f;
    }

    // Restore even if the scene is torn down mid-generation, or the player would
    // be left with vsync off for the rest of the session.
    private void OnDisable()
    {
        if (!IsGenerationDone && _genStartTime > 0f)
        {
            QualitySettings.vSyncCount = _vsyncBeforeGen;
            Application.targetFrameRate = _targetFpsBeforeGen;
        }
    }

    private IEnumerator GenerateWorldRoutine()
    {
        // try/FINALLY, not try/catch — C# allows a finally around yields and
        // forbids a catch. Whatever happens above, the world reports itself
        // finished exactly once: the survival timer, the loading screen and the
        // spawn logic all wait on that flag, and leaving it false because a
        // phase misbehaved strands the player on a loading screen or a clock
        // frozen at 00:00.
        try
        {
        BeginGenerationPerfMode();
        yield return Phase("Heights", GenerateHeightsRoutine(terrain.terrainData));
        CurrentProgress = 0.10f;

        yield return Phase("Rivers", CalculateAndCarveRiversRoutine(terrain.terrainData));
        CurrentProgress = 0.20f;

        // СПАВН БАЗ ТА POI
        yield return Phase("Totem", SpawnRegionTotemRoutine());
        yield return Phase("POI locations", SpawnPOIsRoutine());
        yield return Phase("Extraction carts", SpawnExtractionCartsRoutine());
        Physics.SyncTransforms();
        CurrentProgress = 0.30f;

        // --- НОВЕ: ПРОКЛАДАННЯ ДОРІГ ---
        yield return Phase("Roads", GenerateRoadsRoutine());
        CurrentProgress = 0.40f;

        yield return Phase("Terrain paint", PaintTerrainRoutine(terrain.terrainData));
        CurrentProgress = 0.50f;

        yield return Phase("Grass details", GenerateDetailsRoutine());
        CurrentProgress = 0.60f;

        SpawnWaterPlane();
        yield return Phase("River dressing", PopulateSplineRiversRoutine());
        CurrentProgress = 0.65f;

        yield return Phase("Biome scatter", PopulateBiomesRoutine());
        CurrentProgress = 0.85f;

        // --- НОВЕ: ДЕКОРАЦІЇ ДОРІГ ---
        yield return Phase("Road decor", SpawnRoadDecorationsRoutine());

        yield return Phase("Border mountains", SpawnBorderMountainsRoutine());
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

        // Exploration sites are placed from here, the same way the birds are,
        // and for the same reason: this is a per-scene call that runs after the
        // world exists.
        //
        // They used to install themselves from RuntimeInitializeOnLoadMethod,
        // which fires ONCE per play session in whatever scene starts first — the
        // menu. The director was created there, found no region, and was never
        // created again when the game scene loaded. Not a single reliquary ever
        // existed and there was no log to say so, because the code that would
        // have logged it was never running.
        ReliquaryDirector.Install();

        // Painted trees become real, choppable objects near the player. Only
        // does anything when tree painting is on and something was actually
        // painted — see VegetationHydrator.
        VegetationHydrator.Install(terrain);

        CurrentProgress = 1f;
        }
        finally
        {
            IsGenerationDone = true;
            EndGenerationPerfMode();
            try { OnWorldGenerationComplete?.Invoke(); }
            catch (System.Exception e) { Debug.LogError("[WorldGenerator] OnWorldGenerationComplete handler threw: " + e); }
        }
    }

    // Hands the bird prefabs to AmbientBirdLife, which keeps a small number of
    // flocks alive AROUND THE PLAYER and flies them.
    //
    // This used to scatter the prefabs itself: a handful of them dropped at fixed
    // points across the whole terrain, 14-32 m up, and left standing there. Not
    // one of those ever got seen, for three reasons that compound. They never
    // moved, and travel is what peripheral vision actually reacts to — a bird
    // flapping on the spot is a rock. Seven of them across a whole region is
    // nothing; the player had to wander within a few dozen metres AND look up.
    // And looking up is the part that never happens: this is a third-person
    // camera a few metres behind the player, so a band of sky 30 m overhead is
    // simply not on screen.
    //
    // The settings below still belong to the generator — the birds are part of
    // the world's character, so the knobs stay where the rest of the world's
    // knobs are — but the behaviour lives in the component.
    private void SpawnAmbientCrows()
    {
        if (ambientCrowCount <= 0) return;
        if (ambientCrowPrefabs == null || ambientCrowPrefabs.Length == 0) return;

        var life = AmbientBirdLife.Install(ambientCrowPrefabs, ambientCrowCount,
                                           ambientCrowMinHeight, ambientCrowMaxHeight);
        if (life != null) life.transform.SetParent(this.transform, true);
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

    // True only in a winter REGION MISSION. Ordinary maps get snow from the
    // temperature noise like any other biome and must not be touched by any of
    // the winter-specific work below.
    private bool IsWinterRegion => isRegionMissionCached && regionBiomeTypeCached == 2;

    // Snow depth per alphamap cell, 0 = scoured bare, 1 = buried. Built by the
    // paint pass and kept because the grass pass, which runs after it, needs the
    // same field: vegetation should be absent from both the bare wind-scoured
    // ground AND the deep drifts, and present in the band between them.
    private float[,] winterSnowCover;

    private IEnumerator PaintTerrainRoutine(TerrainData terrainData)
    {
        if (grassLayer == null || sandLayer == null || snowLayer == null || rockLayer == null || roadLayer == null) yield break;

        terrainData.terrainLayers = TintLayersForSeason(
            new TerrainLayer[] { grassLayer, sandLayer, snowLayer, rockLayer, roadLayer });
        int aWidth = terrainData.alphamapWidth; int aHeight = terrainData.alphamapHeight;

        winterSnowCover = null;
        if (IsWinterRegion && winterSnowDrifts)
        {
            winterSnowCover = BuildWinterSnowCover(terrainData, aWidth, aHeight);
            yield return null;
        }

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

                // Rock that shows because the snow was stripped off it, as
                // opposed to rock that shows because the slope is a cliff. Zero
                // outside a winter region, so nothing else changes.
                float scouredRock = 0f;

                bool shoreline = normalizedHeight <= waterLevel + 0.02f;
                if (winterSnowCover != null && !shoreline)
                {
                    float cover = winterSnowCover[y, x];
                    float bare = 1f - cover;

                    // WHAT IS UNDER THE SNOW depends on where you are. High and
                    // steep, it is rock; low and sheltered, it is the dead grass
                    // of whatever grew there before the cold — which is the tone
                    // that stops a bare patch reading as a texture error.
                    float rockShare = Mathf.Clamp01(
                        Mathf.InverseLerp(0.42f, 0.68f, normalizedHeight) +
                        Mathf.InverseLerp(18f, 34f, steepness));

                    scouredRock = bare * rockShare;

                    // The three ground layers must still sum to 1 on their own:
                    // the rock share is applied once, below, and everything else
                    // is scaled into what it leaves. Folding rock in twice is
                    // what would make the alphamap stop summing to one and the
                    // terrain go translucent in patches.
                    float rest = 1f - scouredRock;
                    if (rest > 0.0001f)
                    {
                        weights[2] = cover / rest;
                        weights[0] = (bare - scouredRock) / rest;
                    }
                }
                else if (normalizedHeight > 0.65f) weights[2] = 1f;
                else if (shoreline) weights[1] = 1f;
                else { if (temp >= 0.65f) weights[1] = 1f; else if (temp <= 0.35f) weights[2] = 1f; else weights[0] = 1f; }

                // Max, not assignment: a cliff is rock whatever the snow is
                // doing, and scoured ground is rock even where it is flat.
                weights[3] = Mathf.Max(scouredRock, Mathf.Clamp01(Mathf.InverseLerp(30f, 45f, steepness)));
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

        // THE SAME MISTAKE THE ROADS MADE, ON A BIGGER GRID.
        //
        // This loop asked the TerrainData for GetSteepness and
        // GetInterpolatedHeight per detail cell. Neither is a lookup — each one
        // samples the heightmap and, for steepness, derives a surface normal. At
        // a 512 detail resolution that is a quarter of a million cells and half
        // a million derived queries, which measured at 2.4 seconds of a twelve
        // second load.
        //
        // The heightmap is read ONCE here and both values come out of plain
        // arrays. Steepness is computed from the height gradient, which is what
        // GetSteepness does internally anyway, and this loop only ever compares
        // it against a threshold.
        int hRes = td.heightmapResolution;
        float[,] rawH = td.GetHeights(0, 0, hRes, hRes);
        float[,] cellHeight01 = new float[dRes, dRes];
        float[,] cellSteep = new float[dRes, dRes];
        float metresPerHeightSample = td.size.x / Mathf.Max(1, hRes - 1);

        for (int y = 0; y < dRes; y++)
        {
            int hy = Mathf.Clamp(Mathf.RoundToInt((float)y / dRes * (hRes - 1)), 0, hRes - 1);
            for (int x = 0; x < dRes; x++)
            {
                int hx = Mathf.Clamp(Mathf.RoundToInt((float)x / dRes * (hRes - 1)), 0, hRes - 1);
                cellHeight01[y, x] = rawH[hy, hx];

                int xm = Mathf.Max(hx - 1, 0), xp = Mathf.Min(hx + 1, hRes - 1);
                int ym = Mathf.Max(hy - 1, 0), yp = Mathf.Min(hy + 1, hRes - 1);
                // Central difference, in metres of rise over metres of run.
                float ddx = (rawH[hy, xp] - rawH[hy, xm]) * td.size.y / ((xp - xm) * metresPerHeightSample);
                float ddz = (rawH[yp, hx] - rawH[ym, hx]) * td.size.y / ((yp - ym) * metresPerHeightSample);
                cellSteep[y, x] = Mathf.Atan(Mathf.Sqrt(ddx * ddx + ddz * ddz)) * Mathf.Rad2Deg;
            }
        }
        rawH = null;

        // ==== NO GRASS INSIDE A LOCATION ====
        //
        // Roads were already excluded below via roadBlendMap; locations were
        // not, and the result was a hand-built POI standing in waist-high grass
        // that grew through its own floor.
        //
        // STAMPED, NOT TESTED. Testing every detail cell against every location
        // is a quarter of a million cells times a dozen discs on the load path
        // this phase was only just optimised out of. Each disc instead paints
        // its own bounding box, which touches a few thousand cells in total.
        //
        // And it fades rather than cutting: bare across the inner three
        // quarters, thinning to full grass at the rim. A hard-edged bald circle
        // in a meadow reads as a bug; a fade reads as ground that gets walked
        // on.
        float[,] locationKeep = null;
        if (locationExclusions.Count > 0)
        {
            locationKeep = new float[dRes, dRes];
            for (int y = 0; y < dRes; y++)
                for (int x = 0; x < dRes; x++) locationKeep[y, x] = 1f;

            Vector3 tPos = terrain.transform.position;
            float cellsPerMetreX = dRes / Mathf.Max(0.01f, td.size.x);
            float cellsPerMetreZ = dRes / Mathf.Max(0.01f, td.size.z);

            for (int li = 0; li < locationExclusions.Count; li++)
            {
                Vector4 e = locationExclusions[li];
                float cx = (e.x - tPos.x) * cellsPerMetreX;
                float cz = (e.z - tPos.z) * cellsPerMetreZ;
                float rx = Mathf.Max(0.001f, e.w * cellsPerMetreX);
                float rz = Mathf.Max(0.001f, e.w * cellsPerMetreZ);

                int x0 = Mathf.Max(0, Mathf.FloorToInt(cx - rx));
                int x1 = Mathf.Min(dRes - 1, Mathf.CeilToInt(cx + rx));
                int z0 = Mathf.Max(0, Mathf.FloorToInt(cz - rz));
                int z1 = Mathf.Min(dRes - 1, Mathf.CeilToInt(cz + rz));

                for (int zz = z0; zz <= z1; zz++)
                {
                    for (int xx = x0; xx <= x1; xx++)
                    {
                        float dx = (xx - cx) / rx;
                        float dz = (zz - cz) / rz;
                        float d = Mathf.Sqrt(dx * dx + dz * dz);
                        if (d >= 1f) continue;
                        float keep = Mathf.InverseLerp(0.75f, 1f, d);
                        if (keep < locationKeep[zz, xx]) locationKeep[zz, xx] = keep;
                    }
                }
            }
        }

        for (int y = 0; y < dRes; y++)
        {
            CurrentProgress = startProgress + (endProgress - startProgress) * ((float)y / dRes);

            for (int x = 0; x < dRes; x++)
            {
                float normX = (float)x / dRes;
                float normZ = (float)y / dRes;

                float steepness = cellSteep[y, x];
                float normHeight = cellHeight01[y, x] * td.size.y / depth;

                if (steepness > 45f || normHeight <= waterLevel + 0.02f) continue;

                // --- ФІКС: Забороняємо траві рости на дорогах! ---
                int ax = Mathf.Clamp(Mathf.RoundToInt(normX * td.alphamapWidth), 0, td.alphamapWidth - 1);
                int ay = Mathf.Clamp(Mathf.RoundToInt(normZ * td.alphamapHeight), 0, td.alphamapHeight - 1);
                if (roadBlendMap != null && roadBlendMap[ay, ax] > 0.1f) continue;
                // -------------------------------------------------

                // Same rule for the hand-built locations. See the stamp above.
                float locKeep = locationKeep != null ? locationKeep[y, x] : 1f;
                if (locKeep <= 0f) continue;

                // ==== VEGETATION LIVES IN THE BAND BETWEEN BARE AND BURIED ====
                //
                // The paint pass has already worked out how deep the snow lies
                // here; the grass has to agree with it or the two passes describe
                // different winters. Nothing grows on ground the wind has scraped
                // down to rock, and nothing shows through a drift — so the tufts
                // appear only in the middle, which is also the only place a
                // player would expect to find them. It is the cheapest way to
                // make the ground look composed rather than sprinkled.
                if (winterSnowCover != null)
                {
                    float cov = winterSnowCover[ay, ax];
                    float band = Mathf.Min(Mathf.InverseLerp(0.05f, 0.32f, cov),
                                           Mathf.InverseLerp(0.95f, 0.62f, cov));
                    locKeep *= band;
                    if (locKeep <= 0.02f) continue;
                }

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

                    detailMaps[layer][y, x] = locKeep >= 1f ? density : Mathf.RoundToInt(density * locKeep);
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

        // Self-contained locations (own ground + water) are grounded by their
        // MANUAL root BoxCollider only — the designer sets the box so its bottom
        // is exactly the location's floor. We must NOT measure the mesh footprint
        // for these: it would include the location's own terrain/props and lift
        // the whole thing into the air (the "spawns in the air with its terrain"
        // bug). It also skips the later mesh-based SnapInstanceToGround.
        SelfContainedLocation selfContainedDef = totemPrefab.GetComponent<SelfContainedLocation>();
        bool isSelfContained = selfContainedDef != null;

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

        if (isSelfContained)
        {
            // Radius comes from the component override (or the manual box above);
            // never from the mesh. Fall back to the box-derived radius.
            if (selfContainedDef.footprintRadius > 0.1f)
                flatRadius = selfContainedDef.footprintRadius;
            if (rootBox == null)
                Debug.LogWarning($"[WorldGenerator] SelfContainedLocation '{totemPrefab.name}' has NO root BoxCollider — add one and size it to the location's floor, or it can't be grounded correctly.");
        }
        // The root box is often a small trigger (an activation zone), so the box
        // math above gave a tiny flatten radius — the terrain was leveled only in
        // a ~20m circle while a whole TOWN location extends much further, leaving
        // it perched on ungraded bumps. Measure the prefab's real mesh footprint
        // and flatten at least that far so the entire location sits on level
        // ground. Bottom of the mesh footprint also gives a better base Y than a
        // mis-sized trigger box. (Skipped for self-contained — see above.)
        else if (MeasurePrefabFootprint(totemPrefab, out float meshRadius, out float meshBottomY))
        {
            if (meshRadius + 8f > flatRadius) flatRadius = meshRadius + 8f;
            bottomOfCollider = meshBottomY;
        }

        Vector2 bestSpot = Vector2.zero;
        bool foundSpot = false;
        List<Vector2> validSpots = new List<Vector2>();
        // Best DRY (non-water, reasonably flat) spot found even if it failed the
        // stricter biome/near-river filters — used as the fallback so a big
        // location that finds no "perfect" spot still lands on dry land instead
        // of the blind map centre (which could be the carved lake). Tracks the
        // highest such spot.
        Vector2 driestFallback = Vector2.zero;
        float driestFallbackH = float.MinValue;
        bool haveDryFallback = false;
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
                    // Remember the highest dry+flat spot regardless of biome /
                    // near-river — the last-resort fallback if nothing "ideal"
                    // qualifies (keeps the location out of the lake).
                    if (minH > driestFallbackH)
                    {
                        driestFallbackH = minH;
                        driestFallback = new Vector2(pX, pZ);
                        haveDryFallback = true;
                    }

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
        else if (haveDryFallback)
        {
            // No ideal spot — use the highest dry spot we saw rather than the
            // blind map centre, which was landing the final castle in the lake.
            bestSpot = driestFallback;
            foundSpot = true;
        }
        else bestSpot = new Vector2(transform.position.x + w / 2, transform.position.z + l / 2);

        Vector3 centerPos = new Vector3(bestSpot.x, 0, bestSpot.y);
        float exactGroundY = terrain.SampleHeight(centerPos) + transform.position.y;
        // Hard safety net: never flatten the location pad below the water line.
        // If we had to fall back to a low spot, raise the pad above the lake
        // surface so the location can't end up submerged.
        float padY = Mathf.Max(exactGroundY, absoluteWaterHeight + 4f);

        // PLATEAU: for a location authored to sit on high ground (the castle),
        // raise the land into a hill with a flat top instead of dropping it on
        // the flat. The pad is the hill top and the falloff IS the slope, so the
        // generated roads — which path over the terrain — climb it to the gate.
        float padFalloff = 18f;
        if (selfContainedDef != null && selfContainedDef.raiseHill)
        {
            // Never push the top past the terrain's height range — SetHeights
            // would clamp it and the "hill" would come out as a flat mesa at max
            // altitude with vertical sides.
            float ceilingY = transform.position.y + terrain.terrainData.size.y * 0.92f;
            padY = Mathf.Min(padY + selfContainedDef.hillHeight, ceilingY);
            // Keep the slope long relative to the rise, or the sides become
            // cliffs nothing can walk up and the road dead-ends at the base.
            padFalloff = Mathf.Max(selfContainedDef.hillSlopeLength, selfContainedDef.hillHeight * 2.5f);
            Debug.Log($"[WorldGenerator] Plateau for '{totemPrefab.name}': +{selfContainedDef.hillHeight}m over a {padFalloff}m slope.");
        }
        centerPos.y = padY;

        FlattenTerrainRobust(centerPos, flatRadius, padFalloff, padY);
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
        // EXCEPT self-contained locations: those are grounded purely by their
        // manual root BoxCollider (finalY above), so measuring the live mesh
        // bounds would drag their own terrain/props into the calc and lift the
        // whole thing off the ground.
        if (!isSelfContained)
            SnapInstanceToGround(camp, groundYAfterFlatten + locationYOffset);

        // Water alignment: shift the whole location so its own water sits on the
        // world water plane, merging the two into one continuous level (used when
        // the location brings its own water). Supersedes the box-collider grounding.
        var scInstance = camp.GetComponent<SelfContainedLocation>();
        // A location's own lakes are water too — register them so wading in the
        // castle's moat behaves like wading in the world sea.
        if (scInstance != null && scInstance.waterReference != null)
            WaterBody.Attach(scInstance.waterReference.gameObject);
        if (isSelfContained && scInstance != null && scInstance.alignWaterToWorld && scInstance.waterReference != null
            && !scInstance.raiseHill)
        {
            // This DROPS the whole location so its own water meets the world
            // water plane. The pad, meanwhile, was flattened at least 4m ABOVE
            // the water line — so the location ended up metres below the ground
            // around it, which is the trench the world water was filling.
            // Re-level the pad to wherever the location actually landed.
            float worldWaterY = absoluteWaterHeight;
            float locWaterY = scInstance.waterReference.position.y;
            float shift = worldWaterY - locWaterY;
            camp.transform.position += new Vector3(0f, shift, 0f);

            float alignedGroundY = padY + shift;
            FlattenTerrainRobust(centerPos, flatRadius, padFalloff, alignedGroundY);
            terrain.Flush();
            if (tc != null) { tc.enabled = false; tc.enabled = true; }
            Debug.Log($"[WorldGenerator] '{totemPrefab.name}' water-aligned by {shift:0.0}m; pad re-levelled to {alignedGroundY:0.0} so the terrain still meets it.");
        }
        else if (isSelfContained && scInstance != null && scInstance.alignWaterToWorld && scInstance.raiseHill)
        {
            // A perched location can't merge its lakes with the world sea. Its
            // own water rides up with it and keeps its authored level relative
            // to its own ground, which is what the look depends on.
            Debug.Log($"[WorldGenerator] '{totemPrefab.name}' sits on a plateau, so its water keeps its own level instead of merging with the world water.");
        }

        spawnedTotemPos = camp.transform.position;
        forbiddenZones.Add(spawnedTotemPos);
        roadTargets.Add(spawnedTotemPos);

        // Keep vegetation out of the WHOLE location footprint (not just the 18m
        // point around the centre) — no trees/rocks/bushes inside a village/castle.
        locationExclusions.Add(new Vector4(spawnedTotemPos.x, spawnedTotemPos.y, spawnedTotemPos.z, flatRadius + 6f));

        // Self-contained locations (own terrain + water, e.g. the medieval
        // market village) drop a hole in the procedural terrain under the
        // footprint so the generated ground/collider don't poke through or
        // fight the location's own ground+water. The prefab must carry a
        // SelfContainedLocation component AND its own ground collider.
        var selfContained = camp.GetComponent<SelfContainedLocation>();
        if (selfContained != null && selfContained.cutTerrainHole)
            CutHoleForLocation(camp, selfContained, rootBox, flatRadius);

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
            locationExclusions.Add(new Vector4(extraTotem.transform.position.x, extraTotem.transform.position.y, extraTotem.transform.position.z, flatRadius + 6f));

            var esc = extraTotem.GetComponent<SelfContainedLocation>();
            if (esc != null && esc.cutTerrainHole)
                CutHoleForLocation(extraTotem, esc, totemPrefab.GetComponent<BoxCollider>(), flatRadius);
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

    // Clear every terrain hole so holes punched for a self-contained location in
    // a previous region don't persist into the next generated world (terrain
    // holes live on the shared TerrainData asset).
    private void ResetTerrainHoles()
    {
        if (terrain == null || terrain.terrainData == null) return;
        TerrainData td = terrain.terrainData;
        int res = td.holesResolution;
        if (res <= 0) return;
        bool[,] all = new bool[res, res];
        for (int y = 0; y < res; y++)
            for (int x = 0; x < res; x++)
                all[y, x] = true;   // true = solid, false = hole
        td.SetHoles(0, 0, all);
    }

    // Punch a circular hole in the procedural terrain (mesh + collider) so a
    // self-contained location can drop in its OWN ground + water without the
    // generated terrain poking through or fighting it.
    // Cut the terrain out from under a self-contained location, matching the
    // shape of its own ground rather than a circle around it.
    private void CutHoleForLocation(GameObject instance, SelfContainedLocation sc, BoxCollider rootBox, float flatRadius)
    {
        float inset = Mathf.Max(0f, sc.holeInset);

        // Preferred: the location's OWN ground mesh. Measured in the location's
        // own axes so a rotated instance still gets a correctly oriented cut.
        Transform ground = sc.groundReference != null ? sc.groundReference : FindGroundChild(instance.transform);
        if (ground != null && MeasureLocalXZ(instance.transform, ground, out Vector3 gCentre, out float gHalfX, out float gHalfZ))
        {
            // A water plane wider than the ground would spill over the procedural
            // terrain (or over nothing) whatever we cut — worth saying out loud.
            if (sc.waterReference != null &&
                MeasureLocalXZ(instance.transform, sc.waterReference, out _, out float wHalfX, out float wHalfZ) &&
                (wHalfX > gHalfX + 1f || wHalfZ > gHalfZ + 1f))
            {
                Debug.LogWarning($"[WorldGenerator] '{instance.name}': its water plane ({wHalfX * 2f:0}x{wHalfZ * 2f:0}m) is bigger than its own ground ({gHalfX * 2f:0}x{gHalfZ * 2f:0}m), so it overhangs the location. Scale the water down to the ground, or it renders as a sheet lying on the surrounding terrain.");
            }

            float hx = gHalfX - inset, hz = gHalfZ - inset;
            if (hx > 1f && hz > 1f)
            {
                PunchTerrainHoleRect(gCentre, hx, hz, instance.transform.eulerAngles.y);
                Debug.Log($"[WorldGenerator] Hole for '{instance.name}': cut to its own ground, {hx * 2f:0}x{hz * 2f:0}m (the old circle was ~{(flatRadius + sc.margin) * 2f:0}m across).");
                return;
            }
        }

        if (rootBox != null)
        {
            Vector3 ls = instance.transform.lossyScale;
            float halfX = Mathf.Abs(rootBox.size.x * ls.x) * 0.5f - inset;
            float halfZ = Mathf.Abs(rootBox.size.z * ls.z) * 0.5f - inset;
            if (halfX > 1f && halfZ > 1f)
            {
                // The box's centre is offset from the pivot on this prefab, so
                // cut around the box, not around the transform.
                Vector3 boxCentre = instance.transform.TransformPoint(rootBox.center);
                PunchTerrainHoleRect(boxCentre, halfX, halfZ, instance.transform.eulerAngles.y);
                Debug.Log($"[WorldGenerator] Hole for '{instance.name}': rect {halfX * 2f:0}x{halfZ * 2f:0}m (was a circle of ~{(flatRadius + sc.margin) * 2f:0}m across).");
                return;
            }
        }

        // No usable box: fall back to a circle, but use the INSET radius so it
        // still can't be cut wider than the location's own ground.
        float holeR = sc.footprintRadius > 0.1f ? sc.footprintRadius : flatRadius;
        PunchTerrainHole(instance.transform.position, Mathf.Max(holeR * 0.35f, holeR - inset));
    }

    private static readonly string[] GroundChildNames = { "terrain", "ground", "landscape" };

    private static Transform FindGroundChild(Transform root)
    {
        foreach (Transform c in root)
        {
            string n = c.name.ToLowerInvariant();
            foreach (var g in GroundChildNames) if (n.Contains(g)) return c;
        }
        return null;
    }

    // XZ extents of `part` expressed in `root`'s OWN axes, so a rotated location
    // still yields the true rectangle rather than an inflated world AABB.
    // Returns the world-space centre of that rectangle.
    private static bool MeasureLocalXZ(Transform root, Transform part, out Vector3 worldCentre, out float halfX, out float halfZ)
    {
        worldCentre = Vector3.zero; halfX = halfZ = 0f;
        if (root == null || part == null) return false;

        float minX = float.MaxValue, maxX = float.MinValue;
        float minZ = float.MaxValue, maxZ = float.MinValue;
        bool any = false;

        foreach (var r in part.GetComponentsInChildren<Renderer>(true))
        {
            if (r == null || r is ParticleSystemRenderer) continue;
            var mf = r.GetComponent<MeshFilter>();
            Mesh mesh = mf != null ? mf.sharedMesh : null;
            Bounds lb = mesh != null ? mesh.bounds : new Bounds(Vector3.zero, Vector3.zero);
            if (mesh == null && r is SkinnedMeshRenderer smr && smr.sharedMesh != null) lb = smr.sharedMesh.bounds;
            if (lb.size == Vector3.zero) continue;

            Vector3 c = lb.center, e = lb.extents;
            for (int i = 0; i < 8; i++)
            {
                Vector3 corner = c + new Vector3(
                    (i & 1) == 0 ? -e.x : e.x,
                    (i & 2) == 0 ? -e.y : e.y,
                    (i & 4) == 0 ? -e.z : e.z);
                Vector3 local = root.InverseTransformPoint(r.transform.TransformPoint(corner));
                if (local.x < minX) minX = local.x; if (local.x > maxX) maxX = local.x;
                if (local.z < minZ) minZ = local.z; if (local.z > maxZ) maxZ = local.z;
                any = true;
            }
        }
        if (!any) return false;

        halfX = (maxX - minX) * 0.5f;
        halfZ = (maxZ - minZ) * 0.5f;
        worldCentre = root.TransformPoint(new Vector3((minX + maxX) * 0.5f, 0f, (minZ + maxZ) * 0.5f));
        return halfX > 0.5f && halfZ > 0.5f;
    }

    // RECTANGULAR hole, rotated to match the location.
    //
    // A location's own ground is a rectangle; a circular hole sized off its
    // half-DIAGONAL removes a huge crescent of terrain along the short axis that
    // nothing covers, and the world water plane shows through it as a pit. The
    // castle is 361 x 456 m, so the old circle (radius ~300 m) was deleting some
    // 120 m of terrain past each long edge. Cutting the actual rectangle, inset
    // slightly so the location's floor overlaps the seam, is the fix.
    private void PunchTerrainHoleRect(Vector3 worldCenter, float halfX, float halfZ, float yawDeg)
    {
        if (terrain == null || terrain.terrainData == null || halfX <= 0f || halfZ <= 0f) return;
        TerrainData td = terrain.terrainData;
        int res = td.holesResolution;
        if (res <= 0) return;

        float cosY = Mathf.Cos(-yawDeg * Mathf.Deg2Rad);
        float sinY = Mathf.Sin(-yawDeg * Mathf.Deg2Rad);

        // Axis-aligned bounds of the rotated rectangle, in cells.
        float reachX = Mathf.Abs(halfX * cosY) + Mathf.Abs(halfZ * sinY);
        float reachZ = Mathf.Abs(halfX * sinY) + Mathf.Abs(halfZ * cosY);

        float relX = (worldCenter.x - transform.position.x) / td.size.x;
        float relZ = (worldCenter.z - transform.position.z) / td.size.z;
        int cx = Mathf.RoundToInt(relX * res);
        int cz = Mathf.RoundToInt(relZ * res);

        int spanX = Mathf.CeilToInt(reachX / td.size.x * res);
        int spanZ = Mathf.CeilToInt(reachZ / td.size.z * res);

        int minX = Mathf.Clamp(cx - spanX, 0, res - 1);
        int maxX = Mathf.Clamp(cx + spanX, 0, res - 1);
        int minZ = Mathf.Clamp(cz - spanZ, 0, res - 1);
        int maxZ = Mathf.Clamp(cz + spanZ, 0, res - 1);
        int wCells = maxX - minX + 1;
        int hCells = maxZ - minZ + 1;
        if (wCells <= 0 || hCells <= 0) return;

        float mPerCellX = td.size.x / res;
        float mPerCellZ = td.size.z / res;

        bool[,] holes = td.GetHoles(minX, minZ, wCells, hCells);
        for (int z = 0; z < hCells; z++)
        {
            for (int x = 0; x < wCells; x++)
            {
                // Cell offset from the centre, in metres, rotated into the
                // location's own axes.
                float dx = (minX + x - cx) * mPerCellX;
                float dz = (minZ + z - cz) * mPerCellZ;
                float lx = dx * cosY - dz * sinY;
                float lz = dx * sinY + dz * cosY;
                if (Mathf.Abs(lx) <= halfX && Mathf.Abs(lz) <= halfZ) holes[z, x] = false;
            }
        }
        td.SetHoles(minX, minZ, holes);
        terrain.Flush();
    }

    private void PunchTerrainHole(Vector3 worldCenter, float radius)
    {
        if (terrain == null || terrain.terrainData == null || radius <= 0f) return;
        TerrainData td = terrain.terrainData;
        int res = td.holesResolution;
        if (res <= 0) return;

        float relX = (worldCenter.x - transform.position.x) / td.size.x;
        float relZ = (worldCenter.z - transform.position.z) / td.size.z;
        float radXcells = radius / td.size.x * res;
        float radZcells = radius / td.size.z * res;
        int cx = Mathf.RoundToInt(relX * res);
        int cz = Mathf.RoundToInt(relZ * res);

        int minX = Mathf.Clamp(cx - Mathf.CeilToInt(radXcells), 0, res - 1);
        int maxX = Mathf.Clamp(cx + Mathf.CeilToInt(radXcells), 0, res - 1);
        int minZ = Mathf.Clamp(cz - Mathf.CeilToInt(radZcells), 0, res - 1);
        int maxZ = Mathf.Clamp(cz + Mathf.CeilToInt(radZcells), 0, res - 1);
        int wCells = maxX - minX + 1;
        int hCells = maxZ - minZ + 1;
        if (wCells <= 0 || hCells <= 0) return;

        bool[,] holes = td.GetHoles(minX, minZ, wCells, hCells);
        for (int z = 0; z < hCells; z++)
        {
            for (int x = 0; x < wCells; x++)
            {
                float nx = (minX + x - cx) / Mathf.Max(0.001f, radXcells);
                float nz = (minZ + z - cz) / Mathf.Max(0.001f, radZcells);
                if (nx * nx + nz * nz <= 1f) holes[z, x] = false;   // carve hole
            }
        }
        td.SetHoles(minX, minZ, holes);
        terrain.Flush();
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

    [Header("Season tint (RegionData.regionBiome)")]
    [Tooltip("Tint the painted GROUND to the region's season. The splat layers are the same set in every region — grass stays summer-green in an autumn or winter region unless the layers themselves are recoloured, which is what makes every region look like the same place.")]
    public bool tintGroundBySeason = true;
    public Color autumnGroundTint = new Color(0.72f, 0.55f, 0.30f);
    [Range(0f, 1f)] public float autumnGroundBlend = 0.62f;
    public Color winterGroundTint = new Color(0.93f, 0.96f, 1f);
    [Range(0f, 1f)] public float winterGroundBlend = 0.85f;

    // Clone the shared TerrainLayer assets and tint their diffuse remap, so the
    // ground reads as the region's season. Cloned because these layers are
    // project assets shared by every scene — writing to them directly would
    // repaint the whole game to whatever region was generated last.
    // Works out how deep the snow lies on every alphamap cell.
    //
    // Four things decide it, and they are the four things that decide it in the
    // real world:
    //
    //   SLOPE     — snow will not stay on anything steep. This alone breaks the
    //               white sheet, because it means every cliff and gully wall in
    //               the region turns to bare rock.
    //   WIND      — a face that looks into the wind is scoured; the lee side of
    //               the same ridge is where all of that snow ends up. This is
    //               what gives a snowfield direction, so the whole region reads
    //               as having weather rather than a climate.
    //   SHELTER   — ground standing above its own neighbourhood is stripped,
    //               ground sunk below it fills in. Ridges bare, hollows buried.
    //   PATCHINESS— noise, so none of the above resolves into a clean band.
    //
    // Cheap: one GetHeights, one separable blur, no per-cell TerrainData calls.
    // The paint pass it feeds already costs less than roads or grass did.
    private float[,] BuildWinterSnowCover(TerrainData td, int aW, int aH)
    {
        int hRes = td.heightmapResolution;
        float[,] raw = td.GetHeights(0, 0, hRes, hRes);
        float metresPerSample = td.size.x / Mathf.Max(1, hRes - 1);

        float[,] height = new float[aH, aW];
        float[,] steepDeg = new float[aH, aW];
        // How much this face looks INTO the wind: +1 straight into it, -1 in
        // its lee, 0 side-on or flat.
        float[,] exposure = new float[aH, aW];

        float windRad = winterWindDegrees * Mathf.Deg2Rad;
        Vector2 wind = new Vector2(Mathf.Sin(windRad), Mathf.Cos(windRad));

        for (int y = 0; y < aH; y++)
        {
            int hy = Mathf.Clamp(Mathf.RoundToInt((float)y / aH * (hRes - 1)), 0, hRes - 1);
            int ym = Mathf.Max(hy - 1, 0), yp = Mathf.Min(hy + 1, hRes - 1);

            for (int x = 0; x < aW; x++)
            {
                int hx = Mathf.Clamp(Mathf.RoundToInt((float)x / aW * (hRes - 1)), 0, hRes - 1);
                int xm = Mathf.Max(hx - 1, 0), xp = Mathf.Min(hx + 1, hRes - 1);

                height[y, x] = raw[hy, hx];

                float ddx = (raw[hy, xp] - raw[hy, xm]) * td.size.y / ((xp - xm) * metresPerSample);
                float ddz = (raw[yp, hx] - raw[ym, hx]) * td.size.y / ((yp - ym) * metresPerSample);
                steepDeg[y, x] = Mathf.Atan(Mathf.Sqrt(ddx * ddx + ddz * ddz)) * Mathf.Rad2Deg;

                // The slope descends along -(ddx, ddz), so that is the direction
                // the face looks. Looking against the wind means exposed.
                Vector2 face = new Vector2(-ddx, -ddz);
                float m = face.magnitude;
                exposure[y, x] = m > 0.0015f ? -Vector2.Dot(face / m, wind) : 0f;
            }
        }
        raw = null;

        // Blur the height to get "the ground around here", so a cell can be
        // compared against its own neighbourhood. Separable, so the radius is
        // nearly free.
        float[,] smooth = BoxBlur(height, aW, aH, 9);

        var cover = new float[aH, aW];
        float noiseScale = winterDriftScale;

        for (int y = 0; y < aH; y++)
        {
            float nz = (float)y / aH;
            for (int x = 0; x < aW; x++)
            {
                float nx = (float)x / aW;

                // Metres this cell stands above (or below) its surroundings.
                float relief = (height[y, x] - smooth[y, x]) * td.size.y;

                // ==== WHY THIS DOES NOT START AT FULLY COVERED ====
                //
                // The obvious base is 1 — everything buried, and the terrain
                // takes snow away. That reproduces the original bug on every
                // flat part of the map: with nothing to subtract, open level
                // ground lands at 1, saturates the threshold, and comes out as
                // the same uniform white sheet this whole pass exists to break.
                // And most of a region IS flat-ish, so most of it would not
                // change at all.
                //
                // Starting mid-range instead means flat open ground is decided
                // by the noise and therefore VARIES, while the terrain features
                // push decisively either side of it.
                float c = 0.55f;

                // Snow slides off. By 55 degrees there is essentially none left.
                c -= Mathf.InverseLerp(30f, 55f, steepDeg[y, x]) * 0.95f;

                // Ridges scoured, hollows filled.
                c -= Mathf.Clamp01(relief / 9f) * 0.50f;
                c += Mathf.Clamp01(-relief / 7f) * 0.30f;

                // Windward stripped, lee loaded.
                float e = exposure[y, x];
                c -= Mathf.Max(0f, e) * 0.45f * winterWindStrength;
                c += Mathf.Max(0f, -e) * 0.28f * winterWindStrength;

                // Two octaves of patchiness so nothing resolves into a band.
                float n = Mathf.PerlinNoise(nx * noiseScale + offsetX + 700f,
                                            nz * noiseScale + offsetZ + 700f) * 0.7f
                        + Mathf.PerlinNoise(nx * noiseScale * 3.7f + offsetX + 1300f,
                                            nz * noiseScale * 3.7f + offsetZ + 1300f) * 0.3f;
                c += (n - 0.5f) * 0.55f;

                // One dial for the whole region, with a soft band around it so
                // the transition from snow to bare ground is a gradient rather
                // than a cut line.
                cover[y, x] = Mathf.SmoothStep(0f, 1f,
                    Mathf.InverseLerp(winterBareGround, winterBareGround + 0.42f, Mathf.Clamp01(c)));
            }
        }

        Debug.Log("[WorldGenerator] Winter snow cover built — drifts, scoured ridges and lee loading.");
        return cover;
    }

    // Separable box blur. Two passes of a running sum, so the cost does not
    // depend on the radius.
    private static float[,] BoxBlur(float[,] src, int w, int h, int radius)
    {
        var tmp = new float[h, w];
        var dst = new float[h, w];
        int span = radius * 2 + 1;

        for (int y = 0; y < h; y++)
        {
            float sum = 0f;
            for (int i = -radius; i <= radius; i++) sum += src[y, Mathf.Clamp(i, 0, w - 1)];
            for (int x = 0; x < w; x++)
            {
                tmp[y, x] = sum / span;
                sum -= src[y, Mathf.Clamp(x - radius, 0, w - 1)];
                sum += src[y, Mathf.Clamp(x + radius + 1, 0, w - 1)];
            }
        }

        for (int x = 0; x < w; x++)
        {
            float sum = 0f;
            for (int i = -radius; i <= radius; i++) sum += tmp[Mathf.Clamp(i, 0, h - 1), x];
            for (int y = 0; y < h; y++)
            {
                dst[y, x] = sum / span;
                sum -= tmp[Mathf.Clamp(y - radius, 0, h - 1), x];
                sum += tmp[Mathf.Clamp(y + radius + 1, 0, h - 1), x];
            }
        }
        return dst;
    }

    private TerrainLayer[] TintLayersForSeason(TerrainLayer[] src)
    {
        if (!tintGroundBySeason || src == null) return src;

        // 0 = forest/summer (leave it alone), 1 = desert/autumn, 2 = winter.
        int biome = regionBiomeTypeCached;
        if (biome == 0) return src;

        Color tint = biome == 2 ? winterGroundTint : autumnGroundTint;
        float blend = biome == 2 ? winterGroundBlend : autumnGroundBlend;

        var outLayers = new TerrainLayer[src.Length];
        for (int i = 0; i < src.Length; i++)
        {
            if (src[i] == null) { outLayers[i] = null; continue; }

            // The ROAD keeps its own colour — a snow-white or autumn-brown road
            // stops reading as a road at all.
            if (src[i] == roadLayer) { outLayers[i] = src[i]; continue; }

            // AND SO DOES THE ROCK, IN WINTER. The tint was pushing every layer
            // toward white, rock included, which took away the last thing on the
            // ground with any tonal weight — so a snowfield with bare crags in it
            // came out as pale grey on pale white and read as one flat surface.
            // Dark rock against snow is the entire contrast budget of a winter
            // landscape; spending it on consistency is what made the region look
            // washed out rather than cold.
            if (biome == 2 && src[i] == rockLayer) { outLayers[i] = src[i]; continue; }

            var clone = Instantiate(src[i]);
            clone.name = src[i].name + (biome == 2 ? " (Winter)" : " (Autumn)");
            clone.hideFlags = HideFlags.HideAndDontSave;

            Color b = clone.diffuseRemapMax;
            Color c = Color.Lerp(b, tint, blend);
            c.a = b.a;
            clone.diffuseRemapMax = c;
            outLayers[i] = clone;
        }
        Debug.Log($"[WorldGenerator] Ground tinted for biome {biome} ({(biome == 2 ? "winter" : "autumn")}).");
        return outLayers;
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
        // Open, rippling water in a region where snow is lying is the single
        // loudest wrong note left on the map — it says the temperature is above
        // freezing everywhere the player looks. Optional: with no ice material
        // assigned the water stays as it is rather than disappearing.
        Material surface = (IsWinterRegion && winterIceMaterial != null) ? winterIceMaterial : waterMaterial;
        float w = terrain.terrainData.size.x; float l = terrain.terrainData.size.z; float absoluteWaterHeight = transform.position.y + (depth * waterLevel);
        GameObject waterObj = GameObject.CreatePrimitive(PrimitiveType.Plane);
        waterObj.name = "Bitgem_WaterPlane"; waterObj.transform.SetParent(this.transform);
        waterObj.transform.position = new Vector3(transform.position.x + w / 2, absoluteWaterHeight, transform.position.z + l / 2);
        waterObj.transform.localScale = new Vector3(w / 10f, 1f, l / 10f);
        MeshRenderer mr = waterObj.GetComponent<MeshRenderer>(); mr.material = surface; mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        Destroy(waterObj.GetComponent<Collider>());
        // No collider means nothing can detect this by physics, so register the
        // surface for gameplay (PlayerWaterState) to query.
        WaterBody.Attach(waterObj);
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

        // Force a fresh nav grid: the heightmap this samples was rewritten by
        // the heights and rivers phases, and a cached grid from a previous
        // generation would route roads over terrain that no longer exists.
        _navHeight = null;

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
        int roadsRelaxed = 0;
        int roadsAlreadyConnected = 0;
        int roadsFailed = 0;

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

            // ONE BAD ROAD MUST NOT COST THE REST OF THEM.
            //
            // This routine is a coroutine, so a throw anywhere inside it stops
            // the whole loop — every remaining road, and with them the altars
            // and prisoner events that are placed on road dead-ends. That is a
            // very quiet failure: the map simply comes out sparse, and nothing
            // says a road was ever meant to be there.
            List<Vector3> path = null;
            try
            {
                path = FindAStarPathToNetwork(startGrid, totemGrid, roadNetwork, gridW, gridL, cellSize, absWaterH);

                // ASK AGAIN, LESS FUSSILY, BEFORE GIVING UP.
                //
                // The first pass refuses anything over 40 degrees and pays a
                // heavy toll for every degree of slope, which is what keeps
                // roads in the valleys where they belong. On broken ground that
                // can leave a target with no route at all — and the result is
                // not "a road that takes the ugly way round", it is NO ROAD, and
                // with it no altar, no caged ally and no reason to walk there.
                //
                // A steep road is worse than a gentle one and enormously better
                // than nothing, so a failure is retried once with the slope limit
                // raised and the toll cut. Only the roads that would otherwise
                // not exist take this path, so it cannot make the normal ones
                // uglier.
                if (path == null || path.Count <= 4)
                {
                    var relaxed = FindAStarPathToNetwork(startGrid, totemGrid, roadNetwork, gridW, gridL,
                                                         cellSize, absWaterH, maxSteep: 55f, steepPenalty: 0.6f);
                    if (relaxed != null && relaxed.Count > 4) { path = relaxed; roadsRelaxed++; }
                }
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[Smart Roads] Pathfinding threw for target {targetPos}; skipping this road and " +
                               $"continuing with the rest.\n{e}");
            }

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
                // Not necessarily a failure. A target that already sits on the
                // network needs no road of its own, and counting that as
                // "blocked by terrain" is what made the log unreadable — the
                // warning fired dozens of times on a map whose roads were fine.
                Vector2Int sg = startGrid;
                if (roadNetwork[sg.x, sg.y]) roadsAlreadyConnected++;
                else { roadsFailed++; Debug.LogWarning($"[Smart Roads] Шлях заблоковано ландшафтом для цілі: {targetPos}"); }
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
        GameLog.Info($"[Smart Roads] Готово! Побудовано {roadsBuilt} доріг з {allTargets.Count} цілей " +
                     $"({roadsRelaxed} через складний рельєф, {roadsAlreadyConnected} вже були на мережі, {roadsFailed} не вдалося).");
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
                // Whole-location footprints — no trees/rocks/bushes inside a
                // castle/village. XZ distance vs the disc radius.
                if (!inForbiddenZone)
                {
                    for (int li = 0; li < locationExclusions.Count; li++)
                    {
                        Vector4 e = locationExclusions[li];
                        float dx = worldX - e.x; float dz = worldZ - e.z;
                        if (dx * dx + dz * dz < e.w * e.w) { inForbiddenZone = true; break; }
                    }
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
                    // Lush green reeds standing in a frozen lake undo the whole
                    // biome in one glance, and they sit exactly where the player
                    // walks to reach water.
                    if (IsWinterRegion) continue;
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

    // Public so systems that place their own structures after generation — the
    // reliquaries, for one — can level the ground under them with the same
    // routine the generator's own locations use, instead of a second
    // implementation that behaves subtly differently.
    public void FlattenTerrainRobust(Vector3 center, float radius, float falloff, float targetWorldY)
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

        // ==== THE DRAW IS WEIGHTED, AND SOME LOCATIONS ARE NOT ELIGIBLE AT ALL ====
        //
        // This used to be a flat GetRandomPrefab over the whole list. That is
        // correct while every entry is ordinary furniture and badly wrong once
        // one of them is an event: with five prefabs and eighty placements, a
        // reliquary landed about sixteen times per region, so the thing meant to
        // be a rare find became the most common sight on the map.
        //
        // Eligibility is rolled ONCE per region, before anything is placed, so a
        // rare location is either in this region or it is not — rather than
        // getting a fresh chance at every one of eighty grid points, which is the
        // same thing as always appearing. See POISettings.
        var pool = new List<POICandidate>(poiPrefabs.Length);
        var poolReport = new System.Text.StringBuilder();
        foreach (var candidatePrefab in poiPrefabs)
        {
            if (candidatePrefab == null) continue;
            var s = candidatePrefab.GetComponent<POISettings>();
            if (s == null)
            {
                Debug.LogError($"[AAA Gen] Префаб {candidatePrefab.name} не має скрипта POISettings! Пропускаємо.");
                continue;
            }
            if (s.spawnWeight <= 0f)
            {
                poolReport.Append($"\n    {candidatePrefab.name}: weight 0 — never drawn");
                continue;
            }
            if (s.regionAppearChance < 1f && GetRandomFloat() > s.regionAppearChance)
            {
                poolReport.Append($"\n    {candidatePrefab.name}: NOT in this region (chance {s.regionAppearChance:P0})");
                continue;
            }
            pool.Add(new POICandidate { prefab = candidatePrefab, settings = s, placed = 0 });
            poolReport.Append($"\n    {candidatePrefab.name}: weight {s.spawnWeight:0.##}" +
                              (s.maxPerRegion > 0 ? $", max {s.maxPerRegion}" : ", uncapped"));
        }
        GameLog.Info("[AAA Gen] POI pool for this region:" + poolReport);
        if (pool.Count == 0)
        {
            Debug.LogWarning("[AAA Gen] Every POI prefab rolled out of this region — no locations will be placed.");
            yield break;
        }

        foreach (Vector2 gridPoint in masterGrid)
        {
            if (prefabsAssigned >= maxPOIs) break;
            if (pool.Count == 0) break;   // everything left has hit its cap

            POICandidate pick = PickWeighted(pool);
            if (pick == null) break;
            GameObject prefab = pick.prefab;
            POISettings settings = pick.settings;

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

                // A capped location leaves the pool the moment it is satisfied,
                // so the remaining weight redistributes to whatever is left
                // instead of the draw quietly re-rolling something it can no
                // longer place.
                pick.placed++;
                if (settings.maxPerRegion > 0 && pick.placed >= settings.maxPerRegion) pool.Remove(pick);
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
            // THE LOCATION OWNS ITS GROUND.
            //
            // forbiddenZones on its own is a single 18m point sample, which is
            // narrower than most locations and — more importantly — is only ever
            // consulted by the scatter pass. The grass pass never saw it, so a
            // chest site dropped into a meadow ended up knee-deep in grass
            // growing straight out of its floor.
            //
            // Registering the flattened disc here puts locations on the same
            // footing as the region totem: one footprint, honoured by both the
            // scatter and the detail layers.
            locationExclusions.Add(new Vector4(poi.worldPosition.x, poi.worldPosition.y, poi.worldPosition.z,
                                               poi.settings.flattenRadius));
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
            //
            // THE VISIBLE GEOMETRY DECIDES, ALWAYS. The root BoxCollider is only
            // a fallback for a prefab with nothing to render.
            //
            // This used to be the other way round: a BoxCollider on the root won
            // outright and the meshes were never looked at. That is how a whole
            // hand-built fort ended up hanging in the sky. The branch also never
            // referenced the ground height at all — it just shifted the instance
            // by the collider's own bottom offset, so a collider that does not
            // tightly wrap the build (a default 1×1×1 box someone added for a
            // trigger, a wide trigger volume, a collider sized around the walls
            // but not the foundations) put the location wherever its pivot
            // happened to be. The player does not stand on a collider. They look
            // at the mesh, and the mesh is what has to touch the ground.
            bool snapped = TrySnapByMeshes(instance, exactGroundY);
            if (!snapped)
            {
                BoxCollider rootBox = instance.GetComponent<BoxCollider>();
                if (rootBox != null)
                {
                    float sy = rootBox.size.y * instance.transform.localScale.y;
                    float cy = rootBox.center.y * instance.transform.localScale.y;
                    float bottomOffset = cy - (sy / 2f);
                    instance.transform.position -= new Vector3(0, bottomOffset, 0);
                }
            }

            // 4. Застосовуємо ручний відступ з твого скрипта POISettings 
            // (можеш ставити yOffset = -0.5f в Інспекторі, щоб додатково "втопити" локацію в траву)
            instance.transform.position += (Vector3.up * poi.settings.yOffset);
        }

        GameLog.Info($"[AAA Gen] Успішно згенеровано {plannedPOIs.Count} POI.");
    }

    // Sits a location's visible base on the ground, and says so when it cannot.
    //
    // Returns false only when the prefab has nothing to render, which is the one
    // case where the root collider is a better answer than nothing.
    private bool TrySnapByMeshes(GameObject instance, float exactGroundY)
    {
        // We snap the STRUCTURE's base to the ground — but decorative geometry
        // that is DELIBERATELY sunk below grade (water-mill wheels dipping into a
        // river, foliage/trees planted deep in the soil) must be EXCLUDED from
        // that calc. Including them made the snap lift the whole location so
        // those parts sat on top of the ground, floating the building in the air.
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

        // Prefer the structural set; fall back to everything if a prefab is
        // entirely made of "decor"-named parts, so we never skip the snap.
        List<float> bottoms = structuralBottoms.Count > 0 ? structuralBottoms : allBottoms;
        if (bottoms.Count == 0) return false;

        bottoms.Sort();
        float lowestY = bottoms[0];

        // Even among structural parts, reject a lone deep outlier (a foundation
        // pile, a buried anchor) so it cannot rocket the whole location upward.
        if (bottoms.Count >= 3)
        {
            float span = bottoms[bottoms.Count - 1] - bottoms[0];
            float gap = bottoms[1] - bottoms[0];
            if (span > 0.01f && gap > span * 0.5f && gap > 1.5f)
                lowestY = bottoms[1];
        }

        float delta = exactGroundY - lowestY;

        // A correction this large is not a pivot offset, it is a broken prefab —
        // a mis-scaled child, a renderer left at the world origin, a collider
        // someone parented in from another scene. Applying it would fling the
        // location into the sky, which is exactly the bug the player reported.
        // Clamping and NAMING it means the next one is a two-second fix instead
        // of another round of "why is the fort floating".
        const float sane = 40f;
        if (Mathf.Abs(delta) > sane)
        {
            Debug.LogWarning($"[AAA Gen] '{instance.name}' wanted a {delta:F1} m vertical correction to sit on the " +
                             $"ground — that is not a pivot offset, something in the prefab is far from the rest of " +
                             $"it. Clamped to {Mathf.Sign(delta) * sane:F0} m. Check for a stray renderer or a " +
                             "mis-scaled child.", instance);
            delta = Mathf.Sign(delta) * sane;
        }

        instance.transform.position += new Vector3(0f, delta, 0f);
        return true;
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

    // One entry in this region's eligible POI pool. `placed` is what enforces
    // POISettings.maxPerRegion — with a high maxPOIs the cap, not the weight, is
    // the thing that actually keeps a rare location rare.
    private class POICandidate
    {
        public GameObject prefab;
        public POISettings settings;
        public int placed;
    }

    private POICandidate PickWeighted(List<POICandidate> pool)
    {
        float total = 0f;
        foreach (var c in pool) total += Mathf.Max(0f, c.settings.spawnWeight);
        if (total <= 0f) return null;

        float roll = GetRandomFloat() * total;
        foreach (var c in pool)
        {
            roll -= Mathf.Max(0f, c.settings.spawnWeight);
            if (roll <= 0f) return c;
        }
        return pool[pool.Count - 1];   // floating-point slack
    }

    // ==========================================
    // СИСТЕМА ДОРІГ (НОВІ МЕТОДИ)
    // ==========================================
    // PathNode is gone: the road A* no longer allocates a node object per cell.
    // See FindAStarPathToNetwork — the search runs on flat reusable arrays, so
    // twenty roads allocate nothing after the first.

    // ==== ROAD PATHFINDING ====
    //
    // This was 64% of a twelve-second load — eight seconds on its own — and the
    // reason was three separate costs multiplying together, none of them the
    // pathfinding itself.
    //
    //   THE TERRAIN WAS ASKED THE SAME QUESTIONS MILLIONS OF TIMES. Every
    //   neighbour of every expanded node called SampleHeight AND GetSteepness.
    //   GetSteepness is not a lookup — it samples the heightmap and derives a
    //   normal. At 15 000 iterations x 8 neighbours x ~20 roads that is well
    //   over two million terrain queries per load, to answer 15 625 distinct
    //   questions. The grid is now computed ONCE and shared by every road.
    //
    //   THE OPEN SET WAS A LIST WITH A LINEAR SCAN. Finding the cheapest node
    //   walked the whole set every iteration, so the cost grew with the square
    //   of the search. A binary heap makes it logarithmic.
    //
    //   AND IT RE-ADDED NODES INSTEAD OF UPDATING THEM, so the set it was
    //   linearly scanning was full of stale duplicates, which made the second
    //   problem worse in proportion to the first.
    //
    // The working arrays are flat and reused between roads, stamped by run
    // number rather than cleared, so twenty roads allocate nothing after the
    // first. The path this returns is identical to the one the old code found.
    private float[] _navHeight;
    private float[] _navSteep;
    private int _navW, _navL;

    private float[] _aG, _aF;
    private int[] _aParent, _aStamp;
    private int[] _aHeap;
    private int _aHeapCount, _aRun;

    // One pass over the map instead of one pass per neighbour per node.
    private void BuildRoadNavGrid(int gridW, int gridL, float cellSize)
    {
        _navW = gridW; _navL = gridL;
        int n = gridW * gridL;
        if (_navHeight == null || _navHeight.Length != n)
        {
            _navHeight = new float[n];
            _navSteep = new float[n];
            _aG = new float[n]; _aF = new float[n];
            _aParent = new int[n]; _aStamp = new int[n];
            _aHeap = new int[Mathf.Max(1024, n / 4)];
        }

        for (int x = 0; x < gridW; x++)
        {
            float wX = transform.position.x + (x * cellSize);
            for (int z = 0; z < gridL; z++)
            {
                float wZ = transform.position.z + (z * cellSize);
                int i = x * gridL + z;
                _navHeight[i] = terrain.SampleHeight(new Vector3(wX, 0f, wZ)) + transform.position.y;
                _navSteep[i] = terrain.terrainData.GetSteepness((float)x / gridW, (float)z / gridL);
            }
        }
    }

    private void HeapPush(int node)
    {
        // GROW, DO NOT OVERFLOW.
        //
        // The heap holds PUSHES, not nodes, and a node is pushed again every
        // time a cheaper route to it is found — so the count can comfortably
        // exceed the number of cells. Sizing it to the cell count threw an
        // IndexOutOfRange partway through the road loop, which killed the
        // coroutine: the first few roads were built and every road after that
        // silently never happened, taking the altars and prisoner events that
        // sit on road dead-ends with them.
        if (_aHeapCount + 1 >= _aHeap.Length)
            System.Array.Resize(ref _aHeap, _aHeap.Length * 2);

        int i = ++_aHeapCount;
        _aHeap[i] = node;
        while (i > 1)
        {
            int parent = i >> 1;
            if (_aF[_aHeap[parent]] <= _aF[_aHeap[i]]) break;
            (_aHeap[parent], _aHeap[i]) = (_aHeap[i], _aHeap[parent]);
            i = parent;
        }
    }

    private int HeapPop()
    {
        int top = _aHeap[1];
        _aHeap[1] = _aHeap[_aHeapCount--];
        int i = 1;
        while (true)
        {
            int l = i << 1, r = l + 1, best = i;
            if (l <= _aHeapCount && _aF[_aHeap[l]] < _aF[_aHeap[best]]) best = l;
            if (r <= _aHeapCount && _aF[_aHeap[r]] < _aF[_aHeap[best]]) best = r;
            if (best == i) break;
            (_aHeap[best], _aHeap[i]) = (_aHeap[i], _aHeap[best]);
            i = best;
        }
        return top;
    }

    private static readonly int[] s_navDX = { -1, 1, 0, 0, -1, 1, -1, 1 };
    private static readonly int[] s_navDZ = { 0, 0, -1, 1, -1, -1, 1, 1 };

    // ==== THE HEURISTIC HAS TO BE ON THE SAME SCALE AS THE COST ====
    //
    // A step costs `1 + steepness * 2`, and steepness is in DEGREES — so
    // ordinary walkable ground at ten degrees costs twenty-one per cell, and
    // rougher ground sixty. The heuristic was raw cell distance: one per cell.
    //
    // An A* whose heuristic is twenty to sixty times smaller than the real
    // remaining cost is not an A*. It is Dijkstra: it flood-fills every flat
    // basin in reach before it will consider a single uphill cell, spends its
    // whole iteration budget doing it, and returns null. Which is reported as
    // "Шлях заблоковано ландшафтом" — a road that was perfectly reachable and
    // was simply never searched for in the right direction.
    //
    // Deliberately weighted rather than admissible. An admissible heuristic here
    // would have to assume every remaining cell is dead flat, which is exactly
    // the assumption that produced the problem. Trading a guaranteed-shortest
    // road for a good road that is actually FOUND is the right trade in a world
    // generator, and it is the standard one in game pathfinding.
    private const float RoadHeuristicWeight = 6f;

    private List<Vector3> FindAStarPathToNetwork(Vector2Int start, Vector2Int totemNode, bool[,] roadNetwork, int gridW, int gridL, float cellSize, float absWaterH, float maxSteep = 40f, float steepPenalty = 2f)
    {
        if (roadNetwork[start.x, start.y]) return null;
        if (_navHeight == null || _navW != gridW || _navL != gridL) BuildRoadNavGrid(gridW, gridL, cellSize);

        // A run stamp instead of clearing four arrays: a node whose stamp is not
        // this run has simply never been visited.
        _aRun++;
        _aHeapCount = 0;

        int startIdx = start.x * gridL + start.y;
        _aG[startIdx] = 0f;
        _aF[startIdx] = Vector2Int.Distance(start, totemNode) * RoadHeuristicWeight;
        _aParent[startIdx] = -1;
        _aStamp[startIdx] = _aRun;
        HeapPush(startIdx);

        // CLOSED is folded into the stamp: a negative stamp means expanded.
        //
        // The cap was 15 000 because each iteration used to cost eight terrain
        // queries and a linear scan of the open set — it existed to stop the
        // editor hanging, and roads across broken ground gave up against it. An
        // iteration is now array lookups and a heap sift, so the same wall-clock
        // budget buys far more search, and roads that used to be abandoned as
        // "blocked by terrain" now find their way.
        int iterations = 0;
        while (_aHeapCount > 0 && iterations < 120000)
        {
            iterations++;
            int cur = HeapPop();
            if (_aStamp[cur] == -_aRun) continue;   // a stale duplicate
            _aStamp[cur] = -_aRun;

            int cx = cur / gridL, cz = cur % gridL;

            if (roadNetwork[cx, cz])
            {
                var path = new List<Vector3>();
                int walk = cur;
                while (walk >= 0)
                {
                    int wx = walk / gridL, wz = walk % gridL;
                    path.Add(new Vector3(transform.position.x + (wx * cellSize), 0f,
                                         transform.position.z + (wz * cellSize)));
                    walk = _aParent[walk];
                }
                path.Reverse();
                return path;
            }

            for (int i = 0; i < 8; i++)
            {
                int nx = cx + s_navDX[i], nz = cz + s_navDZ[i];
                if (nx < 0 || nx >= gridW || nz < 0 || nz >= gridL) continue;

                int nIdx = nx * gridL + nz;
                if (_aStamp[nIdx] == -_aRun) continue;                  // already expanded
                if (_navHeight[nIdx] < absWaterH + 0.5f) continue;      // deep water
                float steepness = _navSteep[nIdx];
                if (steepness > maxSteep) continue;                     // sheer cliff

                float newG = _aG[cur] + ((i < 4) ? 1f : 1.414f) + steepness * steepPenalty;
                if (_aStamp[nIdx] == _aRun && newG >= _aG[nIdx]) continue;

                _aG[nIdx] = newG;
                _aF[nIdx] = newG + Vector2Int.Distance(new Vector2Int(nx, nz), totemNode) * RoadHeuristicWeight;
                _aParent[nIdx] = cur;
                _aStamp[nIdx] = _aRun;
                HeapPush(nIdx);
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

        // Re-sample the terrain DIRECTLY under the instance's own XZ. The caller
        // passes a Y sampled at the road tip (before the +6m extension / any XZ
        // shift), so where the road drops sharply that Y is the higher road-top
        // level and the altar floated over the lower ground. Grounding to the
        // terrain actually beneath the altar fixes the "totem in the air" bug.
        if (terrain != null)
        {
            Vector3 p = go.transform.position;
            float gCenter = terrain.SampleHeight(p) + transform.position.y;
            // Also probe a small ring so a base straddling a sharp edge rests on
            // the LOWER ground (never left hanging over the drop).
            float gLow = gCenter;
            for (int a = 0; a < 4; a++)
            {
                float ang = a * Mathf.PI * 0.5f;
                Vector3 probe = p + new Vector3(Mathf.Cos(ang), 0f, Mathf.Sin(ang)) * 2.5f;
                float g = terrain.SampleHeight(probe) + transform.position.y;
                if (g < gLow) gLow = g;
            }
            targetGroundY = gLow;
        }

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

    // Puts a thing on the map.
    //
    // Reliquaries and caged allies register themselves from their own scripts,
    // but an altar is a bare prefab instantiated here with no behaviour of its
    // own — so nothing ever created a marker for one. The icon set has had an
    // Altar entry with a sprite and a 300m radius assigned in it the whole time,
    // pointing at a kind that no object in the game had ever registered.
    private static void TagMapEvent(GameObject go, MapEventIcons.Kind kind)
    {
        if (go == null) return;
        var marker = go.GetComponent<MapEventMarker>();
        if (marker == null) marker = go.AddComponent<MapEventMarker>();
        marker.kind = kind;
        // An altar is not consumed the way a chest is: it stays worth walking
        // back to, so the map keeps pointing at it.
        marker.hideWhenDone = false;
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
                        eventSpots.Add(endSpawn);
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
                        eventSpots.Add(inst.transform.position);
                        TagMapEvent(inst, MapEventIcons.Kind.Altar);
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

                    // ==== MEASURED AGAINST OTHER EVENTS, NOT AGAINST THE ROAD ====
                    //
                    // This tested forbiddenZones, and forbiddenZones contains a
                    // point every SIXTEEN METRES ALONG EVERY ROAD. The candidate
                    // is placed roadWidth * 1.4 — seven metres — off the road
                    // centreline on purpose, so the nearest road point is never
                    // more than about eleven metres away, against a required
                    // separation of thirty-six.
                    //
                    // Every candidate therefore failed, on every map, without
                    // exception: this pass has been placing exactly zero altars
                    // and zero caged allies since the check was written. With the
                    // dead-end pass capped at one of each, that is the whole of
                    // "вівтарів дуже рідко, часто їх 0" — the safety net that was
                    // supposed to make up the count could never fire.
                    //
                    // Spacing is a rule about how far apart EVENTS should be. The
                    // road is what the event is standing next to.
                    bool tooNearEvent = false;
                    for (int k = 0; k < eventSpots.Count; k++)
                    {
                        if (Vector3.Distance(spawnPos, eventSpots[k]) < interiorSeparation) { tooNearEvent = true; break; }
                    }
                    // Locations keep their footprint: an altar inside a castle
                    // courtyard is a different kind of wrong.
                    if (!tooNearEvent)
                    {
                        for (int k = 0; k < locationExclusions.Count; k++)
                        {
                            Vector4 e = locationExclusions[k];
                            float dx = spawnPos.x - e.x, dz = spawnPos.z - e.z;
                            float r = e.w + 8f;
                            if (dx * dx + dz * dz < r * r) { tooNearEvent = true; break; }
                        }
                    }
                    if (tooNearEvent) continue;
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
                        eventSpots.Add(spawnPos);
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
                        eventSpots.Add(inst.transform.position);
                        TagMapEvent(inst, MapEventIcons.Kind.Altar);
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