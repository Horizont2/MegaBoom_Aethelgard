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
    [Tooltip("Висота (від 0 до 1), нижче якої генерується вода")]
    public float waterLevel = 0.12f;

    // ФІКС: Тепер ми беремо напряму матеріал і меш із папки Bitgem
    [Tooltip("Матеріал з папки Bitgem/StylisedWater/URP/Materials")]
    public Material waterMaterial;
    [Tooltip("Меш з папки Bitgem/StylisedWater/URP/Meshes (або залиш пустим)")]
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

    private void Start()
    {
        IsGenerationDone = false;
        CurrentProgress = 0f;
        terrain = GetComponent<Terrain>();
        propBlock = new MaterialPropertyBlock();

        if (skyboxMaterial != null) RenderSettings.skybox = skyboxMaterial;

        if (PlayerPrefs.GetInt("IsContinuing", 0) == 1)
        {
            offsetX = PlayerPrefs.GetFloat("MapSeedX", 0f); offsetZ = PlayerPrefs.GetFloat("MapSeedZ", 0f);
        }
        else
        {
            offsetX = Random.Range(0f, 9999f); offsetZ = Random.Range(0f, 9999f);
            PlayerPrefs.SetFloat("MapSeedX", offsetX); PlayerPrefs.SetFloat("MapSeedZ", offsetZ); PlayerPrefs.Save();
        }

        AdjustSettingsForBiome();
        StartCoroutine(GenerateWorldRoutine());
    }

    private IEnumerator GenerateWorldRoutine()
    {
        yield return StartCoroutine(GenerateHeightsRoutine(terrain.terrainData));
        CurrentProgress = 0.25f;

        yield return StartCoroutine(PaintTerrainRoutine(terrain.terrainData));
        CurrentProgress = 0.50f;

        SpawnWaterPlane();

        yield return StartCoroutine(PopulateBiomesRoutine());
        CurrentProgress = 0.85f;

        yield return StartCoroutine(SpawnPOIsRoutine());
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

            // Шукаємо випадкову безпечну точку на карті (суху)
            for (int i = 0; i < 200; i++)
            {
                float px = Random.Range(terrain.terrainData.size.x * 0.2f, terrain.terrainData.size.x * 0.8f);
                float pz = Random.Range(terrain.terrainData.size.z * 0.2f, terrain.terrainData.size.z * 0.8f);
                float worldX = transform.position.x + px;
                float worldZ = transform.position.z + pz;
                float worldY = terrain.SampleHeight(new Vector3(worldX, 0, worldZ)) + transform.position.y;

                if (worldY > absoluteWaterHeight + 1.5f && terrain.terrainData.GetSteepness(px / terrain.terrainData.size.x, pz / terrain.terrainData.size.z) < 20f)
                {
                    safePos = new Vector3(worldX, worldY + 2f, worldZ);
                    foundSafeSpot = true;
                    break;
                }
            }

            if (!foundSafeSpot) safePos.y += 5f; // Запобіжник

            CharacterController cc = player.GetComponent<CharacterController>();
            if (cc != null) { cc.enabled = false; player.transform.position = safePos; cc.enabled = true; }
            else { player.transform.position = safePos; Rigidbody rb = player.GetComponent<Rigidbody>(); if (rb != null) rb.linearVelocity = Vector3.zero; }
        }

        yield return new WaitForEndOfFrame();
        DynamicGI.UpdateEnvironment();
        CurrentProgress = 1f;
        IsGenerationDone = true;
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

    // --- ОНОВЛЕНО ДЛЯ BITGEM WATER ---
    private void SpawnWaterPlane()
    {
        if (waterMaterial == null) return;

        float w = terrain.terrainData.size.x;
        float l = terrain.terrainData.size.z;
        float absoluteWaterHeight = transform.position.y + (depth * waterLevel);

        GameObject waterObj = new GameObject("Bitgem_WaterPlane");
        waterObj.transform.SetParent(this.transform);
        waterObj.transform.position = new Vector3(transform.position.x + w / 2, absoluteWaterHeight, transform.position.z + l / 2);

        MeshFilter mf = waterObj.AddComponent<MeshFilter>();
        MeshRenderer mr = waterObj.AddComponent<MeshRenderer>();

        // Вода не повинна відкидати тіні
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        mr.material = waterMaterial;

        // Якщо в Bitgem є спеціальний меш (для хвиль), використовуємо його
        if (waterMesh != null)
        {
            mf.mesh = waterMesh;
        }
        else
        {
            // Якщо ні - створюємо стандартний
            GameObject primitive = GameObject.CreatePrimitive(PrimitiveType.Plane);
            mf.mesh = primitive.GetComponent<MeshFilter>().sharedMesh;
            Destroy(primitive);
        }

        // Розтягуємо воду на весь світ
        waterObj.transform.localScale = new Vector3(w / 5f, 1f, l / 5f);
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

                if (finalHeight < waterLevel)
                {
                    finalHeight = Mathf.Lerp(finalHeight, waterLevel * 0.8f, 0.5f);
                }

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
                float px = Random.Range(10f, w - 10f); float pz = Random.Range(10f, l - 10f);
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

                // ФІКС ОПТИМІЗАЦІЇ: Повністю забороняємо генерацію об'єктів під водою
                if (normalizedHeight <= waterLevel + 0.03f) continue;

                if (steepness > 40f)
                {
                    if (currentRockCount < maxRocks && Random.value > 0.95f)
                    {
                        GameObject rockPrefab = GetRandomPrefab(baseRocks);
                        GameObject obj = Instantiate(rockPrefab, new Vector3(worldX, worldY, worldZ), slopeRotation * Quaternion.Euler(0, Random.Range(0, 360f), 0), rockContainer);
                        obj.transform.localScale *= Random.Range(1.5f, 3f);
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

                float randomSpawn = Random.value;

                if (density > forestThreshold && steepness <= 25f)
                {
                    if (currentTreeCount < maxTrees && density > forestThreshold + 0.2f && randomSpawn > 0.85f && giantTrees != null && giantTrees.Length > 0)
                    {
                        GameObject giantTreePrefab = GetRandomPrefab(giantTrees);
                        GameObject obj = Instantiate(giantTreePrefab, new Vector3(worldX, worldY, worldZ), Quaternion.Euler(0, Random.Range(0, 360f), 0), treeContainer);
                        obj.transform.localScale *= Random.Range(1.0f, 1.4f);
                        ApplyBiomeTexture(obj, currentTreeTexture);
                        ApplyBiomeColor(obj, currentFoliageColor, true);
                        currentTreeCount++;

                        if (giantTreeVFXPrefab != null) Instantiate(giantTreeVFXPrefab, obj.transform.position + Vector3.up * 5f, Quaternion.identity, obj.transform);
                        if (groundClutterPrefabs != null && groundClutterPrefabs.Length > 0) SpawnNatureCluster(GetRandomPrefab(groundClutterPrefabs), obj.transform.position, bushContainer, 3, 6, 3f, true, slopeRotation, currentRockColor);
                    }
                    else if (currentTreeCount < maxTrees && randomSpawn > 0.65f)
                    {
                        GameObject treePrefab = GetRandomPrefab(baseTrees);
                        GameObject obj = Instantiate(treePrefab, new Vector3(worldX, worldY, worldZ), Quaternion.Euler(0, Random.Range(0, 360f), 0), treeContainer);
                        obj.transform.localScale *= Random.Range(0.8f, 1.1f);
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
                        float subRoll = Random.value;
                        GameObject naturePrefab = subRoll > 0.5f ? GetRandomPrefab(baseBushes) : (subRoll > 0.2f ? GetRandomPrefab(baseFlowers) : ((localTemp > 0.35f && localTemp < 0.65f) ? GetRandomPrefab(baseMushrooms) : GetRandomPrefab(baseBushes)));
                        currentBushCount += SpawnNatureCluster(naturePrefab, new Vector3(worldX, worldY, worldZ), bushContainer, 2, 6, 4f, true, slopeRotation, currentFoliageColor);
                    }
                }
                else if (density < 0.3f || isMeadow)
                {
                    if (isVein && currentRockCount < maxRocks && randomSpawn > 0.7f)
                    {
                        GameObject rockBase = GetRandomPrefab(baseRocks);
                        int clusterSize = Random.Range(3, 6);
                        for (int c = 0; c < clusterSize; c++)
                        {
                            float ox = Random.Range(-4f, 4f); float oz = Random.Range(-4f, 4f);
                            float cy = terrain.SampleHeight(new Vector3(worldX + ox, 0, worldZ + oz)) + transform.position.y;
                            GameObject obj = Instantiate(rockBase, new Vector3(worldX + ox, cy, worldZ + oz), slopeRotation * Quaternion.Euler(0, Random.Range(0f, 360f), 0), rockContainer);
                            obj.transform.localScale *= Random.Range(0.5f, 1.2f);
                            ApplyBiomeColor(obj, currentRockColor, true);
                            currentRockCount++;
                        }
                    }
                    else if (currentRockCount < maxRocks && randomSpawn > 0.95f)
                    {
                        bool isRuin = ruinPrefabs != null && ruinPrefabs.Length > 0 && Random.value > 0.8f;
                        GameObject targetPrefab = isRuin ? GetRandomPrefab(ruinPrefabs) : GetRandomPrefab(baseRocks);

                        GameObject obj = Instantiate(targetPrefab, new Vector3(worldX, worldY, worldZ), slopeRotation * Quaternion.Euler(0, Random.Range(0f, 360f), 0), rockContainer);
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
                        if (log != null) { Instantiate(log, new Vector3(worldX, worldY, worldZ), Quaternion.Euler(0, Random.Range(0f, 360f), 0), logContainer); currentTreeCount++; }
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
        Renderer[] renderers = obj.GetComponentsInChildren<Renderer>();
        Color finalColor = baseColor;

        if (randomize)
        {
            Color.RGBToHSV(baseColor, out float h, out float s, out float v);
            h = Mathf.Repeat(h + Random.Range(-0.04f, 0.04f), 1f);
            s = Mathf.Clamp01(s * Random.Range(0.8f, 1.1f));
            v = Mathf.Clamp01(v * Random.Range(0.6f, 1.1f));
            finalColor = Color.HSVToRGB(h, s, v);
        }

        foreach (Renderer rend in renderers)
        {
            if (rend is ParticleSystemRenderer) continue;
            string objName = rend.gameObject.name.ToLower();
            if (objName.Contains("vfx") || objName.Contains("smoke") || objName.Contains("effect")) continue;

            rend.GetPropertyBlock(propBlock);
            propBlock.SetColor("_Color", finalColor); propBlock.SetColor("_BaseColor", finalColor);
            propBlock.SetColor("_Primary_Color", finalColor); propBlock.SetColor("_Secondary_Color", finalColor);
            propBlock.SetColor("_TopColor", finalColor);
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
        int count = Random.Range(minCount, maxCount + 1);
        int spawned = 0;

        for (int i = 0; i < count; i++)
        {
            float ox = Random.Range(-radius, radius); float oz = Random.Range(-radius, radius);
            float cy = terrain.SampleHeight(new Vector3(centerPos.x + ox, 0, centerPos.z + oz)) + transform.position.y;
            Quaternion randomYRot = Quaternion.Euler(0, Random.Range(0f, 360f), 0);
            Quaternion finalRot = alignToSlope ? (slopeRotation * randomYRot * prefab.transform.rotation) : (randomYRot * prefab.transform.rotation);

            GameObject obj = Instantiate(prefab, new Vector3(centerPos.x + ox, cy, centerPos.z + oz), finalRot, container);
            obj.transform.localScale *= Random.Range(0.7f, 1.3f);
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

        for (int i = 0; i < 3000; i++)
        {
            if (Time.realtimeSinceStartup - startTime > MAX_FRAME_TIME) { yield return null; startTime = Time.realtimeSinceStartup; }
            if (spawnedCount >= maxPOIs) break;

            try
            {
                float px = Random.Range(20f, w - 20f); float pz = Random.Range(20f, l - 20f);
                if (terrain.terrainData.GetSteepness(px / w, pz / l) > maxPOISteepness) continue;

                float normHeight = terrain.terrainData.GetHeight((int)(pz / l * terrain.terrainData.heightmapResolution), (int)(px / w * terrain.terrainData.heightmapResolution)) / depth;
                if (normHeight < waterLevel + 0.05f) continue;

                float worldX = transform.position.x + px; float worldZ = transform.position.z + pz;
                float worldY = terrain.SampleHeight(new Vector3(worldX, 0, worldZ)) + transform.position.y;
                Vector3 spawnPos = new Vector3(worldX, worldY, worldZ);

                if (IsPositionClear(spawnPos, poiClearanceRadius))
                {
                    Instantiate(GetRandomPrefab(poiPrefabs), spawnPos, Quaternion.Euler(0, Random.Range(0f, 360f), 0), poiContainer);
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

        for (int i = 0; i < 5000; i++)
        {
            if (Time.realtimeSinceStartup - startTime > MAX_FRAME_TIME) { yield return null; startTime = Time.realtimeSinceStartup; }
            if (spawnedCarts >= extractionCartsAmount) break;

            try
            {
                float px = Random.Range(30f, w - 30f); float pz = Random.Range(30f, l - 30f);
                if (terrain.terrainData.GetSteepness(px / w, pz / l) < 8f)
                {
                    float normHeight = terrain.terrainData.GetHeight((int)(pz / l * terrain.terrainData.heightmapResolution), (int)(px / w * terrain.terrainData.heightmapResolution)) / depth;
                    if (normHeight < waterLevel + 0.02f) continue;

                    float worldX = transform.position.x + px; float worldZ = transform.position.z + pz;
                    float worldY = terrain.SampleHeight(new Vector3(worldX, 0, worldZ)) + transform.position.y;
                    Vector3 spawnPos = new Vector3(worldX, worldY, worldZ);

                    if (IsPositionClear(spawnPos, cartClearanceRadius))
                    {
                        Instantiate(extractionCartPrefab, spawnPos, Quaternion.Euler(0, Random.Range(0, 360f), 0));
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

            GameObject mnt = Instantiate(prefab, new Vector3(worldX, y - 5f, worldZ), Quaternion.Euler(0, Random.Range(0f, 360f), 0), container);
            mnt.transform.localScale *= Random.Range(borderMinScale, borderMaxScale);

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
            if (col.GetComponent<TerrainCollider>() != null || col.GetComponent<Terrain>() != null || col.isTrigger) continue;
            return false;
        }
        return true;
    }

    private GameObject GetRandomPrefab(GameObject[] array) => (array == null || array.Length == 0) ? null : array[Random.Range(0, array.Length)];
}