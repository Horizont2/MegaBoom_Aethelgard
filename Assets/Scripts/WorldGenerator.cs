using UnityEngine;
using System.Collections;
using System.Collections.Generic;

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
    public Mesh waterMesh;

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

    [Header("Biome Textures (ONLY for Trees)")]
    public Texture2D forestTreeTexture;
    public Texture2D desertTreeTexture;
    public Texture2D snowTreeTexture;

    [Header("Biome Colors (Base)")]
    public Color forestFoliageColor = new Color(0.17f, 0.30f, 0.12f);
    public Color desertFoliageColor = new Color(0.65f, 0.55f, 0.26f);
    public Color snowFoliageColor = new Color(0.40f, 0.55f, 0.70f);

    public Color forestRockColor = new Color(0.55f, 0.55f, 0.55f);
    public Color desertRockColor = new Color(0.73f, 0.57f, 0.40f);
    public Color snowRockColor = new Color(0.65f, 0.72f, 0.79f);

    [Header("GENERATION BUDGETS (AAA Limits)")]
    public int spawnAttempts = 60000;
    public int maxTrees = 3000;
    public int maxGrassObjects = 15000;
    public int maxBushesAndMushroom = 2500;
    public int maxRocks = 1200;

    [Header("Biome & Cluster Settings")]
    public float clusterScale = 12f;
    [Range(0f, 1f)] public float forestThreshold = 0.48f;
    public float globalBiomeScale = 2.5f;

    [Header("Base Nature Prefabs")]
    public GameObject[] giantTrees;
    public GameObject[] baseTrees;
    public GameObject[] baseGrass;
    public GameObject[] baseRocks;
    public GameObject[] baseBushes;
    public GameObject[] baseFlowers;
    public GameObject[] baseMushrooms;
    public GameObject[] logPrefabs;

    [Header("Storytelling & Detail Prefabs")]
    public GameObject[] ruinPrefabs;
    public GameObject[] groundClutterPrefabs;
    public GameObject giantTreeVFXPrefab;

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
    private int currentGrassCount = 0;
    private int currentBushCount = 0;
    private int currentRockCount = 0;

    // --- ЗМІННА ДЛЯ ЗБЕРЕЖЕННЯ ПОЗИЦІЇ ГОЛОВНОГО ТОТЕМУ ---
    private Vector3 spawnedTotemPos = Vector3.zero;

    private System.Random prng;

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
        terrain = GetComponent<Terrain>();
        propBlock = new MaterialPropertyBlock();

        if (skyboxMaterial != null) RenderSettings.skybox = skyboxMaterial;

        GameObject player = GameObject.FindGameObjectWithTag("Player");
        if (player != null)
        {
            CharacterController cc = player.GetComponent<CharacterController>();
            if (cc != null) cc.enabled = false;

            Rigidbody rb = player.GetComponent<Rigidbody>();
            if (rb != null) { rb.isKinematic = true; rb.linearVelocity = Vector3.zero; }

            player.transform.position = new Vector3(transform.position.x + terrain.terrainData.size.x / 2f, 1000f, transform.position.z + terrain.terrainData.size.z / 2f);
        }

        RegionData curRegion = null;
        if (GameManager.Instance != null && GameManager.Instance.currentRegion != null) curRegion = GameManager.Instance.currentRegion;
        if (curRegion == null && MissionInitializer.PendingMissionRegion != null) curRegion = MissionInitializer.PendingMissionRegion;

        bool isRegionMission = PlayerPrefs.GetInt("IsRegionMission", 0) == 1;
        int mapSeed = 0;

        if (isRegionMission && curRegion != null)
        {
            if (curRegion.currentState == RegionState.Conquered)
            {
                mapSeed = PlayerPrefs.GetInt("RegionSeed_" + curRegion.regionID, UnityEngine.Random.Range(0, 999999));
            }
            else
            {
                mapSeed = UnityEngine.Random.Range(0, 999999);
                PlayerPrefs.SetInt("RegionSeed_" + curRegion.regionID, mapSeed);
                PlayerPrefs.Save();
            }
        }
        else
        {
            if (PlayerPrefs.GetInt("IsContinuing", 0) == 1) mapSeed = PlayerPrefs.GetInt("MapSeed", UnityEngine.Random.Range(0, 999999));
            else
            {
                mapSeed = UnityEngine.Random.Range(0, 999999);
                PlayerPrefs.SetInt("MapSeed", mapSeed);
                PlayerPrefs.Save();
            }
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
        CurrentProgress = 0.25f;

        yield return StartCoroutine(PaintTerrainRoutine(terrain.terrainData));
        CurrentProgress = 0.40f;

        SpawnWaterPlane();

        // 1. Спочатку Тотем (створює рівну зону)
        yield return StartCoroutine(SpawnRegionTotemRoutine());
        Physics.SyncTransforms(); // Оновлюємо фізику після спавну
        CurrentProgress = 0.50f;

        // 2. ПОТІМ спавнимо POI (Намети) - вони теж роблять рівну зону під собою
        yield return StartCoroutine(SpawnPOIsRoutine());
        Physics.SyncTransforms();
        CurrentProgress = 0.65f;

        // 3. І ТІЛЬКИ ПОТІМ ліс і каміння (вони не будуть рости на вирівняних місцях)
        yield return StartCoroutine(PopulateBiomesRoutine());
        CurrentProgress = 0.85f;

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

            // ФІКС 1: Мінімальна дистанція від Тотему (30% від ширини мапи)
            float minSpawnDistance = Mathf.Min(mapWidth, mapLength) * 0.30f;

            for (int i = 0; i < 500; i++)
            {
                // Шукаємо по всій мапі (від 10% до 90% країв)
                float px = GetRandomRange(mapWidth * 0.1f, mapWidth * 0.9f);
                float pz = GetRandomRange(mapLength * 0.1f, mapLength * 0.9f);
                float worldX = transform.position.x + px;
                float worldZ = transform.position.z + pz;
                float worldY = terrain.SampleHeight(new Vector3(worldX, 0, worldZ)) + transform.position.y;

                if (worldY > absoluteWaterHeight + 1.5f && terrain.terrainData.GetSteepness(px / mapWidth, pz / mapLength) < 20f)
                {
                    // Перевіряємо, чи ми достатньо далеко від головного Тотему!
                    if (Vector3.Distance(new Vector3(worldX, worldY, worldZ), spawnedTotemPos) > minSpawnDistance)
                    {
                        safePos = new Vector3(worldX, worldY + 2f, worldZ);
                        foundSafeSpot = true;
                        break;
                    }
                }
            }

            // Запасний план, якщо мапа надто маленька
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

    private IEnumerator SpawnRegionTotemRoutine()
    {
        GameObject totemPrefab = null;

        if (GameManager.Instance != null && GameManager.Instance.currentRegion != null)
            totemPrefab = GameManager.Instance.currentRegion.regionTotemPrefab;
        else if (MissionInitializer.PendingMissionRegion != null)
            totemPrefab = MissionInitializer.PendingMissionRegion.regionTotemPrefab;

        if (totemPrefab == null) yield break;

        float w = terrain.terrainData.size.x;
        float l = terrain.terrainData.size.z;
        float absoluteWaterHeight = transform.position.y + (depth * waterLevel);

        Vector3 bestPos = Vector3.zero;
        bool found = false;

        for (int i = 0; i < 500; i++)
        {
            float px = GetRandomRange(w * 0.2f, w * 0.8f);
            float pz = GetRandomRange(l * 0.2f, l * 0.8f);

            if (terrain.terrainData.GetSteepness(px / w, pz / l) < 20f)
            {
                float worldX = transform.position.x + px;
                float worldZ = transform.position.z + pz;
                float worldY = terrain.SampleHeight(new Vector3(worldX, 0, worldZ)) + transform.position.y;

                if (worldY > absoluteWaterHeight + 5f)
                {
                    bestPos = new Vector3(worldX, worldY, worldZ);
                    found = true;
                    break;
                }
            }
        }

        if (!found)
        {
            bestPos = new Vector3(transform.position.x + w / 2, 0, transform.position.z + l / 2);
            bestPos.y = terrain.SampleHeight(bestPos) + transform.position.y;
        }

        GameObject camp = Instantiate(totemPrefab, bestPos, Quaternion.Euler(0, GetRandomRange(0f, 360f), 0));
        camp.transform.SetParent(this.transform);

        Collider campCol = camp.GetComponent<Collider>();
        float dynamicRadius = 20f;

        if (campCol != null)
        {
            float maxSize = Mathf.Max(campCol.bounds.size.x, campCol.bounds.size.z);
            dynamicRadius = (maxSize / 2f) + 3f;
        }

        FlattenTerrainAt(bestPos, dynamicRadius, 15f);

        bestPos.y = terrain.SampleHeight(bestPos) + transform.position.y;
        camp.transform.position = bestPos;

        // Запам'ятовуємо позицію для віддаленого спавну гравця
        spawnedTotemPos = bestPos;

        yield return null;
    }

    private void FlattenTerrainAt(Vector3 worldPos, float flatRadius, float blendRadius)
    {
        TerrainData td = terrain.terrainData;
        int hRes = td.heightmapResolution;

        float normX = (worldPos.x - transform.position.x) / td.size.x;
        float normZ = (worldPos.z - transform.position.z) / td.size.z;

        int centerX = Mathf.RoundToInt(normX * (hRes - 1));
        int centerZ = Mathf.RoundToInt(normZ * (hRes - 1));

        int flatRadiusSamples = Mathf.RoundToInt((flatRadius / td.size.x) * hRes);
        int blendRadiusSamples = Mathf.RoundToInt((blendRadius / td.size.x) * hRes);
        int totalRadiusSamples = flatRadiusSamples + blendRadiusSamples;

        int startX = Mathf.Clamp(centerX - totalRadiusSamples, 0, hRes - 1);
        int endX = Mathf.Clamp(centerX + totalRadiusSamples, 0, hRes - 1);
        int startZ = Mathf.Clamp(centerZ - totalRadiusSamples, 0, hRes - 1);
        int endZ = Mathf.Clamp(centerZ + totalRadiusSamples, 0, hRes - 1);

        int width = endX - startX + 1;
        int length = endZ - startZ + 1;

        float[,] heights = td.GetHeights(startX, startZ, width, length);
        float targetHeightNorm = (worldPos.y - transform.position.y) / td.size.y;

        for (int z = 0; z < length; z++)
        {
            for (int x = 0; x < width; x++)
            {
                int mapX = startX + x;
                int mapZ = startZ + z;

                float dist = Vector2.Distance(new Vector2(mapX, mapZ), new Vector2(centerX, centerZ));

                if (dist <= flatRadiusSamples) heights[z, x] = targetHeightNorm;
                else if (dist <= totalRadiusSamples)
                {
                    float t = (dist - flatRadiusSamples) / blendRadiusSamples;
                    float smoothT = t * t * (3f - 2f * t);
                    heights[z, x] = Mathf.Lerp(targetHeightNorm, heights[z, x], smoothT);
                }
            }
        }
        td.SetHeights(startX, startZ, heights);
    }

    private void AdjustSettingsForBiome()
    {
        bool isRegionMission = PlayerPrefs.GetInt("IsRegionMission", 0) == 1;
        if (isRegionMission)
        {
            int biomeType = PlayerPrefs.GetInt("RegionBiomeType", 0);
            terraceCount = 0;
            if (biomeType == 1) { peakSharpness = 2.2f; edgeMountainMultiplier = 3.0f; }
            else if (biomeType == 2) { peakSharpness = 3.5f; edgeMountainMultiplier = 3.5f; }
            else { peakSharpness = 3.0f; edgeMountainMultiplier = 3.0f; }
        }
    }

    private void SpawnWaterPlane()
    {
        if (waterMaterial == null) return;

        float w = terrain.terrainData.size.x;
        float l = terrain.terrainData.size.z;
        float absoluteWaterHeight = transform.position.y + (depth * waterLevel);

        GameObject waterObj = GameObject.CreatePrimitive(PrimitiveType.Plane);
        waterObj.name = "Bitgem_WaterPlane";
        waterObj.transform.SetParent(this.transform);

        waterObj.transform.position = new Vector3(transform.position.x + w / 2, absoluteWaterHeight, transform.position.z + l / 2);
        waterObj.transform.localRotation = Quaternion.identity;
        waterObj.transform.localScale = new Vector3(w / 10f, 1f, l / 10f);

        MeshRenderer mr = waterObj.GetComponent<MeshRenderer>();
        mr.material = waterMaterial;
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

        Collider col = waterObj.GetComponent<Collider>();
        if (col != null) Destroy(col);
    }

    private IEnumerator GenerateHeightsRoutine(TerrainData terrainData)
    {
        int width = terrainData.heightmapResolution; int height = terrainData.heightmapResolution;
        float[,] heights = new float[width, height];
        float centerX = width / 2f; float centerY = height / 2f;
        float startTime = Time.realtimeSinceStartup;

        for (int x = 0; x < width; x++)
        {
            for (int y = 0; y < height; y++)
            {
                float amplitude = 1f; float frequency = 1f; float noiseHeight = 0f; float maxAmplitude = 0f;

                for (int i = 0; i < octaves; i++)
                {
                    float xCoord = (float)x / width * scale * frequency + offsetX;
                    float yCoord = (float)y / height * scale * frequency + offsetZ;

                    float perlinValue = Mathf.PerlinNoise(xCoord, yCoord);
                    perlinValue = 1f - Mathf.Abs(perlinValue * 2f - 1f);
                    perlinValue *= perlinValue;

                    noiseHeight += perlinValue * amplitude;
                    maxAmplitude += amplitude;
                    amplitude *= persistence; frequency *= lacunarity;
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

        terrainData.SetHeights(0, 0, heights);
        terrainData.size = new Vector3(terrainData.size.x, depth, terrainData.size.z);
    }

    private IEnumerator PaintTerrainRoutine(TerrainData terrainData)
    {
        if (grassLayer == null || sandLayer == null || snowLayer == null || rockLayer == null) yield break;

        terrainData.terrainLayers = new TerrainLayer[] { grassLayer, sandLayer, snowLayer, rockLayer };
        int aWidth = terrainData.alphamapWidth; int aHeight = terrainData.alphamapHeight;
        float[,,] splatmapData = new float[aWidth, aHeight, 4];
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
                else
                {
                    if (temp >= 0.65f) weights[1] = 1f;
                    else if (temp <= 0.35f) weights[2] = 1f;
                    else weights[0] = 1f;
                }

                weights[3] = Mathf.Clamp01(Mathf.InverseLerp(30f, 45f, steepness));
                float remain = 1f - weights[3];

                weights[0] *= remain; weights[1] *= remain; weights[2] *= remain;

                splatmapData[y, x, 0] = weights[0]; splatmapData[y, x, 1] = weights[1];
                splatmapData[y, x, 2] = weights[2]; splatmapData[y, x, 3] = weights[3];
            }

            if (Time.realtimeSinceStartup - startTime > MAX_FRAME_TIME) { yield return null; startTime = Time.realtimeSinceStartup; }
        }
        terrainData.SetAlphamaps(0, 0, splatmapData);
    }

    private IEnumerator PopulateBiomesRoutine()
    {
        float w = terrain.terrainData.size.x; float l = terrain.terrainData.size.z;
        Transform treeContainer = new GameObject("TreesContainer").transform; treeContainer.SetParent(this.transform);
        Transform rockContainer = new GameObject("RocksContainer").transform; rockContainer.SetParent(this.transform);
        Transform grassContainer = new GameObject("GrassContainer").transform; grassContainer.SetParent(this.transform);
        Transform bushContainer = new GameObject("BushContainer").transform; bushContainer.SetParent(this.transform);
        Transform logContainer = new GameObject("LogsContainer").transform; logContainer.SetParent(this.transform);

        float startTime = Time.realtimeSinceStartup;

        for (int i = 0; i < spawnAttempts; i++)
        {
            if (currentTreeCount >= maxTrees && currentGrassCount >= maxGrassObjects && currentRockCount >= maxRocks && currentBushCount >= maxBushesAndMushroom)
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

                Vector3 terrainNormal = terrain.terrainData.GetInterpolatedNormal(normalizedX, normalizedZ);
                Quaternion slopeRotation = Quaternion.FromToRotation(Vector3.up, terrainNormal);

                Texture2D currentTreeTexture = forestTreeTexture; Color currentFoliageColor = forestFoliageColor; Color currentRockColor = forestRockColor;
                if (localTemp >= 0.65f) { currentTreeTexture = desertTreeTexture; currentFoliageColor = desertFoliageColor; currentRockColor = desertRockColor; }
                else if (localTemp <= 0.35f) { currentTreeTexture = snowTreeTexture; currentFoliageColor = snowFoliageColor; currentRockColor = snowRockColor; }

                if (normalizedHeight <= waterLevel + 0.03f) continue;
                if (!IsPositionClear(new Vector3(worldX, worldY, worldZ), 1.5f)) continue;

                if (steepness > 40f)
                {
                    if (currentRockCount < maxRocks && GetRandomFloat() > 0.95f)
                    {
                        GameObject rockPrefab = GetRandomPrefab(baseRocks);
                        GameObject obj = Instantiate(rockPrefab, new Vector3(worldX, worldY, worldZ), slopeRotation * Quaternion.Euler(0, GetRandomRange(0f, 360f), 0), rockContainer);
                        obj.transform.localScale *= GetRandomRange(1.5f, 3f);
                        ApplyBiomeColor(obj, currentRockColor, true);
                        currentRockCount++;
                    }
                    continue;
                }

                float density = Mathf.PerlinNoise(normalizedX * clusterScale + offsetX, normalizedZ * clusterScale + offsetZ);
                float meadowNoise = Mathf.PerlinNoise(normalizedX * meadowScale + offsetX + 1000f, normalizedZ * meadowScale + offsetZ + 1000f);
                float veinNoise = Mathf.PerlinNoise(normalizedX * veinScale + offsetX + 2000f, normalizedZ * veinScale + offsetZ + 2000f);

                bool isMeadow = meadowNoise > meadowThreshold;
                bool isVein = veinNoise > veinThreshold;

                if (isMeadow) density = 0f;

                float randomSpawn = GetRandomFloat();

                if (density > forestThreshold && steepness <= 25f)
                {
                    if (currentTreeCount < maxTrees && density > forestThreshold + 0.2f && randomSpawn > 0.85f && giantTrees != null && giantTrees.Length > 0)
                    {
                        GameObject giantTreePrefab = GetRandomPrefab(giantTrees);
                        GameObject obj = Instantiate(giantTreePrefab, new Vector3(worldX, worldY, worldZ), Quaternion.Euler(0, GetRandomRange(0f, 360f), 0), treeContainer);
                        obj.transform.localScale *= GetRandomRange(1.0f, 1.4f);
                        ApplyBiomeTexture(obj, currentTreeTexture);
                        ApplyBiomeColor(obj, currentFoliageColor, true);
                        currentTreeCount++;

                        if (giantTreeVFXPrefab != null) Instantiate(giantTreeVFXPrefab, obj.transform.position + Vector3.up * 5f, Quaternion.identity, obj.transform);
                        if (groundClutterPrefabs != null && groundClutterPrefabs.Length > 0) SpawnNatureCluster(GetRandomPrefab(groundClutterPrefabs), obj.transform.position, bushContainer, 3, 6, 3f, true, slopeRotation, currentRockColor);
                    }
                    else if (currentTreeCount < maxTrees && randomSpawn > 0.65f)
                    {
                        GameObject treePrefab = GetRandomPrefab(baseTrees);
                        GameObject obj = Instantiate(treePrefab, new Vector3(worldX, worldY, worldZ), Quaternion.Euler(0, GetRandomRange(0f, 360f), 0), treeContainer);
                        obj.transform.localScale *= GetRandomRange(0.8f, 1.1f);
                        ApplyBiomeTexture(obj, currentTreeTexture);
                        ApplyBiomeColor(obj, currentFoliageColor, true);
                        currentTreeCount++;
                    }
                    else if (currentGrassCount < maxGrassObjects && randomSpawn > 0.15f)
                    {
                        currentGrassCount += SpawnNatureCluster(GetRandomPrefab(baseGrass), new Vector3(worldX, worldY, worldZ), grassContainer, 8, 16, 8f, true, slopeRotation, currentFoliageColor);
                    }
                    else if (currentBushCount < maxBushesAndMushroom && randomSpawn > 0.10f)
                    {
                        float subRoll = GetRandomFloat();
                        GameObject naturePrefab = subRoll > 0.5f ? GetRandomPrefab(baseBushes) : (subRoll > 0.2f ? GetRandomPrefab(baseFlowers) : ((localTemp > 0.35f && localTemp < 0.65f) ? GetRandomPrefab(baseMushrooms) : GetRandomPrefab(baseBushes)));
                        currentBushCount += SpawnNatureCluster(naturePrefab, new Vector3(worldX, worldY, worldZ), bushContainer, 2, 6, 4f, true, slopeRotation, currentFoliageColor);
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
                            obj.transform.localScale *= GetRandomRange(0.5f, 1.2f);
                            ApplyBiomeColor(obj, currentRockColor, true);
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

                    if (isMeadow && currentGrassCount < maxGrassObjects && randomSpawn > 0.3f)
                    {
                        currentGrassCount += SpawnNatureCluster(GetRandomPrefab(baseGrass), new Vector3(worldX, worldY, worldZ), grassContainer, 10, 20, 6f, true, slopeRotation, currentFoliageColor);
                    }
                }
                else
                {
                    if (currentTreeCount < maxTrees && randomSpawn > 0.95f)
                    {
                        GameObject log = GetRandomPrefab(logPrefabs);
                        if (log != null) { Instantiate(log, new Vector3(worldX, worldY, worldZ), Quaternion.Euler(0, GetRandomRange(0f, 360f), 0), logContainer); currentTreeCount++; }
                    }
                    else if (currentGrassCount < maxGrassObjects && randomSpawn > 0.50f)
                    {
                        currentGrassCount += SpawnNatureCluster(GetRandomPrefab(baseGrass), new Vector3(worldX, worldY, worldZ), grassContainer, 5, 10, 6f, true, slopeRotation, currentFoliageColor);
                    }
                }
            }
            catch (System.Exception e) { Debug.LogError($"[Помилка генерації префабу]: {e.Message}"); }
        }
    }

    private void ApplyBiomeColor(GameObject obj, Color baseColor, bool randomize = false)
    {
        // 🛑 МАГІЧНИЙ ФІКС: 'true' змушує Unity шукати рендерери навіть у вимкнених LOD'ах!
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
            string objName = rend.gameObject.name.ToLower();
            if (objName.Contains("vfx") || objName.Contains("smoke") || objName.Contains("effect")) continue;

            propBlock.Clear();
            rend.GetPropertyBlock(propBlock);

            propBlock.SetColor("_Color", finalColor);
            propBlock.SetColor("Color", finalColor);
            propBlock.SetColor("_BaseColor", finalColor);
            propBlock.SetColor("_Base_Color", finalColor);
            propBlock.SetColor("_PrimaryColor", finalColor);
            propBlock.SetColor("_Primary_Color", finalColor);
            propBlock.SetColor("PrimaryColor", finalColor);
            propBlock.SetColor("_Secondary_Color", finalColor);
            propBlock.SetColor("_TopColor", finalColor);
            propBlock.SetColor("_BottomColor", finalColor);
            propBlock.SetColor("_Tint", finalColor);
            propBlock.SetColor("_TintColor", finalColor);

            propBlock.SetColor("_FoliageColor", finalColor);
            propBlock.SetColor("_LeafColor", finalColor);
            propBlock.SetColor("_Color1", finalColor);
            propBlock.SetColor("_Color2", finalColor);

            rend.SetPropertyBlock(propBlock);
        }
    }

    private void ApplyBiomeTexture(GameObject obj, Texture2D biomeTexture)
    {
        if (biomeTexture == null) return;
        Renderer[] renderers = obj.GetComponentsInChildren<Renderer>();

        foreach (Renderer rend in renderers)
        {
            if (rend is ParticleSystemRenderer) continue;
            string objName = rend.gameObject.name.ToLower();
            if (objName.Contains("vfx")) continue;

            rend.GetPropertyBlock(propBlock);
            propBlock.SetTexture("_BaseMap", biomeTexture); propBlock.SetTexture("_MainTex", biomeTexture);
            rend.SetPropertyBlock(propBlock);
        }
    }

    private int SpawnNatureCluster(GameObject prefab, Vector3 centerPos, Transform container, int minCount, int maxCount, float radius, bool alignToSlope, Quaternion slopeRotation, Color tintColor)
    {
        if (prefab == null) return 0;
        int count = GetRandomRangeInt(minCount, maxCount + 1);
        int spawned = 0;

        for (int i = 0; i < count; i++)
        {
            float ox = GetRandomRange(-radius, radius); float oz = GetRandomRange(-radius, radius);
            float cy = terrain.SampleHeight(new Vector3(centerPos.x + ox, 0, centerPos.z + oz)) + transform.position.y;
            Quaternion randomYRot = Quaternion.Euler(0, GetRandomRange(0f, 360f), 0);
            Quaternion finalRot = alignToSlope ? (slopeRotation * randomYRot * prefab.transform.rotation) : (randomYRot * prefab.transform.rotation);

            GameObject obj = Instantiate(prefab, new Vector3(centerPos.x + ox, cy, centerPos.z + oz), finalRot, container);
            obj.transform.localScale *= GetRandomRange(0.7f, 1.3f);
            ApplyBiomeColor(obj, tintColor, true);
            spawned++;
        }
        return spawned;
    }

    private IEnumerator SpawnPOIsRoutine()
    {
        if (poiPrefabs == null || poiPrefabs.Length == 0) yield break;
        Transform poiContainer = new GameObject("POIContainer").transform; poiContainer.SetParent(this.transform);
        float w = terrain.terrainData.size.x; float l = terrain.terrainData.size.z; int spawnedCount = 0;
        float startTime = Time.realtimeSinceStartup;

        float absoluteWaterHeight = transform.position.y + (depth * waterLevel);

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

                    // ФІКС 2: Вирівнюємо землю під наметами і таборами
                    Collider col = poi.GetComponent<Collider>();
                    float dynamicRadius = 8f;
                    if (col != null)
                    {
                        float maxSize = Mathf.Max(col.bounds.size.x, col.bounds.size.z);
                        dynamicRadius = (maxSize / 2f) + 2f;
                    }

                    // Згладжуємо землю, щоб скрині і вогнища не висіли
                    FlattenTerrainAt(spawnPos, dynamicRadius, 6f);

                    // Після згладжування землі, перераховуємо висоту для самого намету
                    spawnPos.y = terrain.SampleHeight(spawnPos) + transform.position.y;
                    poi.transform.position = spawnPos;

                    // Оновлюємо фізику одразу, щоб дерева не росли крізь намет
                    Physics.SyncTransforms();
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
        float startTime = Time.realtimeSinceStartup;

        float absoluteWaterHeight = transform.position.y + (depth * waterLevel);

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
                        spawnedCarts++;
                    }
                }
            }
            catch (System.Exception e) { Debug.LogWarning($"Cart Spawn Skip: {e.Message}"); }
        }
    }

    private IEnumerator SpawnBorderMountainsRoutine()
    {
        if (borderMountainPrefabs == null || borderMountainPrefabs.Length == 0) yield break;
        Transform borderContainer = new GameObject("BorderMountainsContainer").transform; borderContainer.SetParent(this.transform);
        float w = terrain.terrainData.size.x; float l = terrain.terrainData.size.z;
        float startTime = Time.realtimeSinceStartup;

        for (float x = -borderOffset; x <= w + borderOffset; x += borderSpacing)
        {
            SpawnSingleBorderMountain(new Vector3(x, 0, -borderOffset), borderContainer, w, l);
            SpawnSingleBorderMountain(new Vector3(x, 0, l + borderOffset), borderContainer, w, l);
            if (Time.realtimeSinceStartup - startTime > MAX_FRAME_TIME) { yield return null; startTime = Time.realtimeSinceStartup; }
        }

        for (float z = -borderOffset; z <= l + borderOffset; z += borderSpacing)
        {
            SpawnSingleBorderMountain(new Vector3(-borderOffset, 0, z), borderContainer, w, l);
            SpawnSingleBorderMountain(new Vector3(w + borderOffset, 0, z), borderContainer, w, l);
            if (Time.realtimeSinceStartup - startTime > MAX_FRAME_TIME) { yield return null; startTime = Time.realtimeSinceStartup; }
        }
    }

    private float GetTemperature(float normX, float normZ)
    {
        if (PlayerPrefs.GetInt("IsRegionMission", 0) == 1)
        {
            int biomeType = PlayerPrefs.GetInt("RegionBiomeType", 0);
            if (biomeType == 1) return 0.8f;
            if (biomeType == 2) return 0.2f;
            return 0.5f;
        }
        return Mathf.PerlinNoise(normX * globalBiomeScale + offsetX + 500f, normZ * globalBiomeScale + offsetZ + 500f);
    }

    private void SpawnSingleBorderMountain(Vector3 localPos, Transform container, float w, float l)
    {
        GameObject prefab = GetRandomPrefab(borderMountainPrefabs);
        if (prefab == null) return;
        try
        {
            float clampedX = Mathf.Clamp(localPos.x, 0, w); float clampedZ = Mathf.Clamp(localPos.z, 0, l);
            float worldX = transform.position.x + localPos.x; float worldZ = transform.position.z + localPos.z;
            float y = terrain.SampleHeight(new Vector3(transform.position.x + clampedX, 0, transform.position.z + clampedZ)) + transform.position.y;

            GameObject mnt = Instantiate(prefab, new Vector3(worldX, y - 5f, worldZ), Quaternion.Euler(0, GetRandomRange(0f, 360f), 0), container);
            mnt.transform.localScale *= GetRandomRange(borderMinScale, borderMaxScale);

            float temp = GetTemperature(clampedX / w, clampedZ / l);
            Color rockColor = temp >= 0.65f ? desertRockColor : (temp <= 0.35f ? snowRockColor : forestRockColor);
            ApplyBiomeColor(mnt, rockColor, true);
        }
        catch (System.Exception) { }
    }

    private bool IsPositionClear(Vector3 position, float radius)
    {
        Collider[] colliders = Physics.OverlapSphere(position + Vector3.up * 1.5f, radius);
        foreach (Collider col in colliders)
        {
            if (col.GetComponent<TerrainCollider>() != null || col.GetComponent<Terrain>() != null) continue;

            if (col.isTrigger)
            {
                if (col.GetComponentInParent<RegionManager>() != null) return false;
                continue;
            }

            return false;
        }
        return true;
    }

    private GameObject GetRandomPrefab(GameObject[] array) => (array == null || array.Length == 0) ? null : array[GetRandomRangeInt(0, array.Length)];
}