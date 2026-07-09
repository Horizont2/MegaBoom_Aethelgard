using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using UnityEngine.Splines;
using Unity.Mathematics;
using Polyart; // Інструмент Dreamscape

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
    public GameObject[] baseRocks;
    public GameObject[] baseBushes;
    public GameObject[] baseMushrooms;
    public GameObject[] logPrefabs;

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

    private Vector3 spawnedTotemPos = Vector3.zero;
    private System.Random prng;

    private bool isRegionMissionCached;
    private int regionBiomeTypeCached;

    private List<Vector3> forbiddenZones = new List<Vector3>();

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

    private void Awake()
    {
        Time.timeScale = 1f;
        Time.fixedDeltaTime = 0.02f;
    }

    private void Start()
    {
        IsGenerationDone = false;
        CurrentProgress = 0f;
        forbiddenZones.Clear();
        generatedRivers.Clear();
        terrain = GetComponent<Terrain>();
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
        CurrentProgress = 0.15f;

        yield return StartCoroutine(CalculateAndCarveRiversRoutine(terrain.terrainData));
        CurrentProgress = 0.25f;

        yield return StartCoroutine(PaintTerrainRoutine(terrain.terrainData));
        CurrentProgress = 0.35f;

        yield return StartCoroutine(GenerateDetailsRoutine());
        CurrentProgress = 0.45f;

        SpawnWaterPlane();

        yield return StartCoroutine(PopulateSplineRiversRoutine());
        CurrentProgress = 0.50f;

        yield return StartCoroutine(SpawnRegionTotemRoutine());
        Physics.SyncTransforms();
        CurrentProgress = 0.55f;

        yield return StartCoroutine(SpawnPOIsRoutine());
        Physics.SyncTransforms();
        CurrentProgress = 0.70f;

        yield return StartCoroutine(PopulateBiomesRoutine());
        CurrentProgress = 0.90f;

        yield return StartCoroutine(SpawnExtractionCartsRoutine());
        yield return StartCoroutine(SpawnBorderMountainsRoutine());

        CurrentProgress = 0.95f;
        Physics.SyncTransforms();

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
            CharacterController cc = player.GetComponent<CharacterController>();
            if (cc != null) { cc.enabled = false; player.transform.position = safePos; cc.enabled = true; }
            else { player.transform.position = safePos; Rigidbody rb = player.GetComponent<Rigidbody>(); if (rb != null) { rb.isKinematic = false; rb.linearVelocity = Vector3.zero; } }
            if (Camera.main != null)
            {
                CameraFollow camFollow = Camera.main.GetComponent<CameraFollow>();
                if (camFollow != null) camFollow.SnapToTarget();
            }
        }

        yield return new WaitForEndOfFrame();
        DynamicGI.UpdateEnvironment();
        CurrentProgress = 1f;
        IsGenerationDone = true;
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

    private IEnumerator GenerateDetailsRoutine()
    {
        TerrainData td = terrain.terrainData;
        int dRes = td.detailResolution;
        int layers = td.detailPrototypes.Length;

        if (layers == 0) yield break;

        float startProgress = 0.35f;
        float endProgress = 0.45f;

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
                        // Each flower layer needs its own independent perlin
                        // sample, otherwise every flower type chases the same
                        // meadow patches (since the old `+ layer*0.15` only
                        // shifted the same noise field by a constant) and the
                        // first flower in the list always wins. The two big
                        // primes shove each layer into a distinct frequency
                        // and offset of the noise basis.
                        float layerSeedX = offsetX + (layer * 137.55f);
                        float layerSeedZ = offsetZ + (layer * 211.31f);
                        float perLayerMeadow = Mathf.PerlinNoise(
                            normX * meadowScale + layerSeedX,
                            normZ * meadowScale + layerSeedZ);
                        float perLayerDensity = Mathf.PerlinNoise(
                            normX * clusterScale * 5f + layerSeedX,
                            normZ * clusterScale * 5f + layerSeedZ);

                        // Slightly lower threshold so the rarer-noise tail of
                        // each layer still produces visible patches.
                        if (perLayerMeadow > 0.55f)
                            density = Mathf.RoundToInt(Mathf.Lerp(40f, 180f, perLayerDensity));
                    }
                    else
                    {
                        // Grass layers stay on the shared cluster noise so the
                        // visual texture of the meadow reads as one biome
                        // (only the flowers needed full independence).
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
        if (GameManager.Instance != null && GameManager.Instance.currentRegion != null) activeRegionData = GameManager.Instance.currentRegion;
        else if (MissionInitializer.PendingMissionRegion != null) activeRegionData = MissionInitializer.PendingMissionRegion;
        if (activeRegionData != null) { totemPrefab = activeRegionData.regionTotemPrefab; locationYOffset = activeRegionData.locationYOffset; }

        if (totemPrefab == null) yield break;
        float w = terrain.terrainData.size.x; float l = terrain.terrainData.size.z;
        float absoluteWaterHeight = transform.position.y + (depth * waterLevel);
        Vector3 bestPos = Vector3.zero; bool found = false;

        // Three-pass search:
        //   pass 0 — summer (grass) cells that are also clear of rivers,
        //   pass 1 — any flat above-water cell clear of rivers,
        //   pass 2 — last-resort flat above-water cell (rivers allowed).
        // River avoidance matters because flattening the arena pad on top of a
        // carved river bed raises the terrain back up and leaves the water
        // plane hanging in the air.
        for (int pass = 0; pass < 3 && !found; pass++)
        {
            bool summerOnly = pass == 0;
            bool avoidRivers = pass < 2;
            for (int i = 0; i < 500; i++)
            {
                float px = GetRandomRange(w * 0.2f, w * 0.8f); float pz = GetRandomRange(l * 0.2f, l * 0.8f);
                float normX = px / w; float normZ = pz / l;
                if (terrain.terrainData.GetSteepness(normX, normZ) >= 20f) continue;

                float worldX = transform.position.x + px; float worldZ = transform.position.z + pz;
                float worldY = terrain.SampleHeight(new Vector3(worldX, 0, worldZ)) + transform.position.y;
                if (worldY <= absoluteWaterHeight + 5f) continue;

                if (summerOnly && !IsSummerZone(normX, normZ, worldY)) continue;
                if (avoidRivers && IsNearRiver(new Vector3(worldX, worldY, worldZ), 30f)) continue;

                bestPos = new Vector3(worldX, worldY, worldZ); found = true; break;
            }
        }
        if (!found) { bestPos = new Vector3(transform.position.x + w / 2, 0, transform.position.z + l / 2); bestPos.y = terrain.SampleHeight(bestPos) + transform.position.y; }

        GameObject camp = Instantiate(totemPrefab, bestPos, Quaternion.Euler(0, GetRandomRange(0f, 360f), 0));
        camp.transform.SetParent(this.transform);

        // Footprint radius for the flatten pad. Prefer the root collider, fall
        // back to a child collider, then a fixed default. Capped so a huge
        // trigger volume can't flatten half the map (which is what raised the
        // terrain under the rivers last time).
        Collider campCol = camp.GetComponent<Collider>();
        if (campCol == null) campCol = camp.GetComponentInChildren<Collider>();

        float dynamicRadius = 20f;
        if (campCol != null)
            dynamicRadius = Mathf.Clamp(Mathf.Max(campCol.bounds.size.x, campCol.bounds.size.z) / 2f + 3f, 8f, 35f);

        // Clamp XZ so the collider's footprint fits inside the terrain. Purely
        // horizontal — does not touch Y or rivers. Fixes region 4 spawning
        // past the edge of the map.
        if (campCol != null)
        {
            float halfW = campCol.bounds.extents.x;
            float halfL = campCol.bounds.extents.z;
            float minX = transform.position.x + halfW + 2f;
            float maxX = transform.position.x + w - halfW - 2f;
            float minZ = transform.position.z + halfL + 2f;
            float maxZ = transform.position.z + l - halfL - 2f;
            if (maxX > minX && maxZ > minZ)
            {
                Vector3 clamped = new Vector3(
                    Mathf.Clamp(bestPos.x, minX, maxX), bestPos.y,
                    Mathf.Clamp(bestPos.z, minZ, maxZ));
                Vector3 shift = clamped - bestPos;
                if (shift.sqrMagnitude > 0.001f)
                {
                    camp.transform.position += new Vector3(shift.x, 0, shift.z);
                    bestPos = new Vector3(clamped.x, bestPos.y, clamped.z);
                }
            }
        }

        FlattenTerrainAt(bestPos, dynamicRadius, 15f);

        // Anchor the prefab's PIVOT to the flattened ground, then apply the
        // per-region locationYOffset from RegionData. No collider/renderer
        // guessing — that repeatedly buried or floated arenas because every
        // prefab has its pivot in a different spot. The offset is authored
        // once per region in the inspector and is 100% predictable.
        float groundY = terrain.SampleHeight(new Vector3(bestPos.x, 0, bestPos.z)) + transform.position.y;
        camp.transform.position = new Vector3(bestPos.x, groundY + locationYOffset, bestPos.z);

        spawnedTotemPos = new Vector3(bestPos.x, groundY, bestPos.z);
        forbiddenZones.Add(spawnedTotemPos);
        yield return null;
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

    private IEnumerator PaintTerrainRoutine(TerrainData terrainData)
    {
        if (grassLayer == null || sandLayer == null || snowLayer == null || rockLayer == null) yield break;
        terrainData.terrainLayers = new TerrainLayer[] { grassLayer, sandLayer, snowLayer, rockLayer };
        int aWidth = terrainData.alphamapWidth; int aHeight = terrainData.alphamapHeight; float[,,] splatmapData = new float[aWidth, aHeight, 4];
        float startTime = Time.realtimeSinceStartup;

        for (int y = 0; y < aHeight; y++)
        {
            for (int x = 0; x < aWidth; x++)
            {
                float temp = GetTemperature((float)x / aWidth, (float)y / aHeight);
                float steepness = terrainData.GetSteepness((float)x / aWidth, (float)y / aHeight);
                float normalizedHeight = terrainData.GetHeight(y, x) / depth;
                float[] weights = new float[4];

                if (normalizedHeight > 0.65f) weights[2] = 1f;
                else if (normalizedHeight <= waterLevel + 0.02f) weights[1] = 1f;
                else { if (temp >= 0.65f) weights[1] = 1f; else if (temp <= 0.35f) weights[2] = 1f; else weights[0] = 1f; }

                weights[3] = Mathf.Clamp01(Mathf.InverseLerp(30f, 45f, steepness));
                float remain = 1f - weights[3];
                weights[0] *= remain; weights[1] *= remain; weights[2] *= remain;

                splatmapData[y, x, 0] = weights[0]; splatmapData[y, x, 1] = weights[1]; splatmapData[y, x, 2] = weights[2]; splatmapData[y, x, 3] = weights[3];
            }
            if (Time.realtimeSinceStartup - startTime > MAX_FRAME_TIME) { yield return null; startTime = Time.realtimeSinceStartup; }
        }

        float sandStartTime = Time.realtimeSinceStartup;
        foreach (var river in generatedRivers)
        {
            float riverSandRadius = riverBankWidth * riverSandWidthMul;
            foreach (var pt in river.path)
                PaintSandCircle(splatmapData, terrainData, aWidth, aHeight, pt, riverSandRadius);

            if (river.lakePos != Vector3.zero)
                PaintSandCircle(splatmapData, terrainData, aWidth, aHeight,
                    river.lakePos, lakeRadius * lakeSandRadiusMul);

            foreach (var wf in river.waterfalls)
                PaintSandCircle(splatmapData, terrainData, aWidth, aHeight,
                    wf.bottomPos, riverBankWidth * lakeSandRadiusMul);

            if (Time.realtimeSinceStartup - sandStartTime > MAX_FRAME_TIME)
            { yield return null; sandStartTime = Time.realtimeSinceStartup; }
        }

        terrainData.SetAlphamaps(0, 0, splatmapData);
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

                splat[sy, sx, 1] = Mathf.Max(splat[sy, sx, 1], blend);

                float remain = 1f - splat[sy, sx, 1];
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
                foreach (Vector3 fz in forbiddenZones)
                {
                    if (Vector3.Distance(currentPos, fz) < 18f) { inForbiddenZone = true; break; }
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
                        obj.transform.localScale *= GetRandomRange(0.8f, 1.4f);
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

                if (ambientVFXPrefabs != null && ambientVFXPrefabs.Length > 0 && randomSpawn > 0.985f)
                {
                    GameObject vfxPrefab = GetRandomPrefab(ambientVFXPrefabs);
                    Instantiate(vfxPrefab, new Vector3(worldX, worldY + 1.5f, worldZ), Quaternion.identity, treeContainer);
                }

                if (density > forestThreshold && steepness <= 25f)
                {
                    if (currentTreeCount < maxTrees && density > forestThreshold + 0.2f && randomSpawn > 0.85f && giantTrees != null && giantTrees.Length > 0)
                    {
                        GameObject giantTreePrefab = GetRandomPrefab(giantTrees);
                        GameObject obj = Instantiate(giantTreePrefab, new Vector3(worldX, worldY, worldZ), Quaternion.Euler(0, GetRandomRange(0f, 360f), 0), treeContainer);
                        obj.transform.localScale *= GetRandomRange(1.0f, 1.4f);

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
                        bool useDeadTree = (isSnow || isDesert) && GetRandomFloat() > 0.5f && deadTreesPrefabs != null && deadTreesPrefabs.Length > 0;
                        GameObject treePrefab = useDeadTree ? GetRandomPrefab(deadTreesPrefabs) : GetRandomPrefab(baseTrees);

                        GameObject obj = Instantiate(treePrefab, new Vector3(worldX, worldY, worldZ), Quaternion.Euler(0, GetRandomRange(0f, 360f), 0), treeContainer);
                        obj.transform.localScale *= GetRandomRange(0.8f, 1.1f);

                        if (!useDeadTree)
                        {
                            if (currentBaseTreeMat != null) ApplyBiomeSpecificMaterial(obj, currentBaseTreeMat);
                            else { ApplyBiomeTexture(obj, currentTreeTexture); ApplyBiomeColor(obj, currentFoliageColor, true); }
                        }
                        else { ApplyBiomeColor(obj, currentRockColor, true); }

                        currentTreeCount++;
                    }
                    else if (currentBushCount < maxBushesAndMushroom && randomSpawn > 0.10f)
                    {
                        GameObject naturePrefab = (isDesert) ?
                            (GetRandomFloat() > 0.5f ? GetRandomPrefab(baseMushrooms) : GetRandomPrefab(baseBushes)) :
                            GetRandomPrefab(baseBushes);

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
                        if (!isRuin) ApplyBiomeColor(obj, currentRockColor, true);
                        currentRockCount++;
                    }
                }
                else
                {
                    if (currentTreeCount < maxTrees && randomSpawn > 0.95f)
                    {
                        GameObject log = GetRandomPrefab(logPrefabs);
                        if (log != null) { Instantiate(log, new Vector3(worldX, worldY, worldZ), Quaternion.Euler(0, GetRandomRange(0f, 360f), 0), logContainer); currentTreeCount++; }
                    }
                }
            }
            catch (System.Exception e) { Debug.LogError($"[Помилка генерації префабу]: {e.Message}"); }
        }
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

    private IEnumerator SpawnPOIsRoutine()
    {
        if (poiPrefabs == null || poiPrefabs.Length == 0) yield break;
        Transform poiContainer = new GameObject("POIContainer").transform; poiContainer.SetParent(this.transform);
        float w = terrain.terrainData.size.x; float l = terrain.terrainData.size.z; int spawnedCount = 0;
        float absoluteWaterHeight = transform.position.y + (depth * waterLevel);

        float startTime = Time.realtimeSinceStartup;

        for (int i = 0; i < 3000; i++)
        {
            if (Time.realtimeSinceStartup - startTime > MAX_FRAME_TIME) { yield return null; startTime = Time.realtimeSinceStartup; }

            if (spawnedCount >= maxPOIs) break;
            try
            {
                float px = GetRandomRange(20f, w - 20f); float pz = GetRandomRange(20f, l - 20f);
                if (terrain.terrainData.GetSteepness(px / w, pz / l) > maxPOISteepness) continue;
                float worldX = transform.position.x + px; float worldZ = transform.position.z + pz;
                float worldY = terrain.SampleHeight(new Vector3(worldX, 0, worldZ)) + transform.position.y;
                if (worldY <= absoluteWaterHeight + 1.5f) continue;
                Vector3 spawnPos = new Vector3(worldX, worldY, worldZ);

                if (IsPositionClear(spawnPos, poiClearanceRadius))
                {
                    GameObject prefab = GetRandomPrefab(poiPrefabs);
                    GameObject poi = Instantiate(prefab, spawnPos, Quaternion.Euler(0, GetRandomRange(0f, 360f), 0), poiContainer);
                    Collider col = poi.GetComponent<Collider>();
                    float dynamicRadius = col != null ? (Mathf.Max(col.bounds.size.x, col.bounds.size.z) / 2f) + 2f : 8f;
                    FlattenTerrainAt(spawnPos, dynamicRadius, 6f);
                    spawnPos.y = terrain.SampleHeight(spawnPos) + transform.position.y; poi.transform.position = spawnPos;

                    forbiddenZones.Add(spawnPos);
                    spawnedCount++;
                }
            }
            catch (System.Exception e) { Debug.LogWarning($"POI Spawn Skip: {e.Message}"); }
        }
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
            mnt.transform.localScale *= GetRandomRange(borderMinScale, borderMaxScale);

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
}