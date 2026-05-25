using UnityEngine;
using System.Collections;
using System.Collections.Generic;

[RequireComponent(typeof(Terrain))]
public class WorldGenerator : MonoBehaviour
{
    // Прапорець для синхронізації з екраном завантаження
    public static bool IsGenerationDone = false;

    [Header("Mountain & Arena Settings")]
    public float depth = 40f;
    public float scale = 2.5f;
    [Range(1, 6)] public int octaves = 4;
    public float persistence = 0.45f;
    public float lacunarity = 2.5f;
    [Range(1f, 5f)] public float peakSharpness = 2.5f;
    public int terraceCount = 24;
    public float edgeMountainMultiplier = 2f;

    private float offsetX;
    private float offsetZ;

    [Header("Environment & Sky")]
    public Material skyboxMaterial;

    [Header("Biome Textures (Terrain Layers)")]
    public TerrainLayer grassLayer;
    public TerrainLayer sandLayer;
    public TerrainLayer snowLayer;
    public TerrainLayer rockLayer;

    [Header("Biome Textures (ONLY for Trees)")]
    public Texture2D forestTreeTexture;
    public Texture2D desertTreeTexture;
    public Texture2D snowTreeTexture;

    [Header("Biome Colors (For Grass, Bushes, Flowers & Rocks)")]
    public Color forestFoliageColor = new Color(0.17f, 0.30f, 0.12f);
    public Color desertFoliageColor = new Color(0.65f, 0.55f, 0.26f);
    public Color snowFoliageColor = new Color(0.40f, 0.55f, 0.70f);

    public Color forestRockColor = new Color(0.55f, 0.55f, 0.55f);
    public Color desertRockColor = new Color(0.73f, 0.57f, 0.40f);
    public Color snowRockColor = new Color(0.65f, 0.72f, 0.79f);

    [Header("Biome & Cluster Settings")]
    public int spawnAttempts = 40000;
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
    public GameObject[] baseMushrooms; // НОВЕ: Масив для грибів!
    public GameObject[] logPrefabs;

    [Header("Map Border Mountains (NEW)")]
    public GameObject[] borderMountainPrefabs;
    [Tooltip("Відстань між горами по периметру")]
    public float borderSpacing = 40f;
    [Tooltip("Відступ від краю карти (щоб не красти ігровий простір)")]
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
    private Dictionary<string, Material> biomeMaterialsCache = new Dictionary<string, Material>();

    private void Start()
    {
        IsGenerationDone = false;
        terrain = GetComponent<Terrain>();

        if (skyboxMaterial != null)
        {
            RenderSettings.skybox = skyboxMaterial;
            DynamicGI.UpdateEnvironment();
        }

        if (PlayerPrefs.GetInt("IsContinuing", 0) == 1)
        {
            offsetX = PlayerPrefs.GetFloat("MapSeedX", 0f);
            offsetZ = PlayerPrefs.GetFloat("MapSeedZ", 0f);
        }
        else
        {
            offsetX = Random.Range(0f, 9999f);
            offsetZ = Random.Range(0f, 9999f);
            PlayerPrefs.SetFloat("MapSeedX", offsetX);
            PlayerPrefs.SetFloat("MapSeedZ", offsetZ);
            PlayerPrefs.Save();
        }

        AdjustSettingsForBiome();

        terrain.terrainData = GenerateHeights(terrain.terrainData);
        PaintTerrain(terrain.terrainData);

        StartCoroutine(GenerateWorldRoutine());
    }

    private IEnumerator GenerateWorldRoutine()
    {
        yield return StartCoroutine(PopulateBiomesRoutine());
        yield return StartCoroutine(SpawnPOIsRoutine());
        yield return StartCoroutine(SpawnExtractionCartsRoutine());
        yield return StartCoroutine(SpawnBorderMountainsRoutine());

        Physics.SyncTransforms();

        GameObject player = GameObject.FindGameObjectWithTag("Player");
        if (player != null)
        {
            float groundY = terrain.SampleHeight(player.transform.position) + terrain.transform.position.y;
            player.transform.position = new Vector3(player.transform.position.x, groundY + 1.5f, player.transform.position.z);
        }

        yield return new WaitForEndOfFrame();
        yield return new WaitForEndOfFrame();

        IsGenerationDone = true;
    }

    private void AdjustSettingsForBiome()
    {
        bool isRegionMission = PlayerPrefs.GetInt("IsRegionMission", 0) == 1;
        if (isRegionMission)
        {
            int biomeType = PlayerPrefs.GetInt("RegionBiomeType", 0);

            if (biomeType == 1) { peakSharpness = 1.8f; edgeMountainMultiplier = 1.5f; terraceCount = 0; }
            else if (biomeType == 2) { peakSharpness = 3.5f; edgeMountainMultiplier = 3.5f; terraceCount = 15; }
            else { peakSharpness = 2.5f; edgeMountainMultiplier = 2f; terraceCount = 24; }
        }
    }

    private float GetTemperature(float normX, float normZ)
    {
        bool isRegionMission = PlayerPrefs.GetInt("IsRegionMission", 0) == 1;
        if (isRegionMission)
        {
            int biomeType = PlayerPrefs.GetInt("RegionBiomeType", 0);
            if (biomeType == 1) return 0.8f;
            if (biomeType == 2) return 0.2f;
            return 0.5f;
        }

        return Mathf.PerlinNoise(normX * globalBiomeScale + offsetX + 500f, normZ * globalBiomeScale + offsetZ + 500f);
    }

    private TerrainData GenerateHeights(TerrainData terrainData)
    {
        int width = terrainData.heightmapResolution;
        int height = terrainData.heightmapResolution;
        float[,] heights = new float[width, height];
        float centerX = width / 2f; float centerY = height / 2f;

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
                heights[x, y] = finalHeight;
            }
        }

        terrainData.SetHeights(0, 0, heights);
        terrainData.size = new Vector3(terrainData.size.x, depth, terrainData.size.z);
        return terrainData;
    }

    private void PaintTerrain(TerrainData terrainData)
    {
        if (grassLayer == null || sandLayer == null || snowLayer == null || rockLayer == null) return;

        terrainData.terrainLayers = new TerrainLayer[] { grassLayer, sandLayer, snowLayer, rockLayer };
        int aWidth = terrainData.alphamapWidth; int aHeight = terrainData.alphamapHeight;
        float[,,] splatmapData = new float[aWidth, aHeight, 4];

        for (int y = 0; y < aHeight; y++)
        {
            for (int x = 0; x < aWidth; x++)
            {
                float temp = GetTemperature((float)x / aWidth, (float)y / aHeight);
                float steepness = terrainData.GetSteepness((float)x / aWidth, (float)y / aHeight);
                float[] weights = new float[4];

                if (temp >= 0.65f) weights[1] = 1f;
                else if (temp <= 0.35f) weights[2] = 1f;
                else weights[0] = 1f;

                weights[3] = Mathf.Clamp01(Mathf.InverseLerp(35f, 45f, steepness));
                float remain = 1f - weights[3];
                weights[0] *= remain; weights[1] *= remain; weights[2] *= remain;

                splatmapData[y, x, 0] = weights[0]; splatmapData[y, x, 1] = weights[1];
                splatmapData[y, x, 2] = weights[2]; splatmapData[y, x, 3] = weights[3];
            }
        }
        terrainData.SetAlphamaps(0, 0, splatmapData);
    }

    private void ApplyBiomeTexture(GameObject obj, Texture2D biomeTexture)
    {
        if (biomeTexture == null) return;
        Renderer[] renderers = obj.GetComponentsInChildren<Renderer>();

        foreach (Renderer rend in renderers)
        {
            if (rend is ParticleSystemRenderer) continue;
            string objName = rend.gameObject.name.ToLower();
            if (objName.Contains("vfx") || objName.Contains("smoke") || objName.Contains("effect")) continue;

            Material[] currentMaterials = rend.sharedMaterials;
            bool hasChanged = false;

            for (int i = 0; i < currentMaterials.Length; i++)
            {
                Material mat = currentMaterials[i];
                if (mat == null) continue;

                string cacheKey = mat.name + "_" + biomeTexture.name;

                if (!biomeMaterialsCache.ContainsKey(cacheKey))
                {
                    Material newMat = new Material(mat);
                    if (newMat.HasProperty("_BaseMap")) newMat.SetTexture("_BaseMap", biomeTexture);
                    if (newMat.HasProperty("_MainTex")) newMat.SetTexture("_MainTex", biomeTexture);
                    biomeMaterialsCache[cacheKey] = newMat;
                }

                currentMaterials[i] = biomeMaterialsCache[cacheKey];
                hasChanged = true;
            }

            if (hasChanged) rend.sharedMaterials = currentMaterials;
        }
    }

    private void ApplyBiomeColor(GameObject obj, Color biomeColor)
    {
        Renderer[] renderers = obj.GetComponentsInChildren<Renderer>();

        foreach (Renderer rend in renderers)
        {
            if (rend is ParticleSystemRenderer) continue;
            string objName = rend.gameObject.name.ToLower();
            if (objName.Contains("vfx") || objName.Contains("smoke") || objName.Contains("effect")) continue;

            Material[] currentMaterials = rend.sharedMaterials;
            bool hasChanged = false;

            for (int i = 0; i < currentMaterials.Length; i++)
            {
                Material mat = currentMaterials[i];
                if (mat == null) continue;

                string cacheKey = mat.name + "_" + biomeColor.ToString();

                if (!biomeMaterialsCache.ContainsKey(cacheKey))
                {
                    Material newMat = new Material(mat);

                    if (newMat.HasColor("_Color")) newMat.SetColor("_Color", biomeColor);
                    if (newMat.HasColor("_BaseColor")) newMat.SetColor("_BaseColor", biomeColor);
                    if (newMat.HasColor("_Primary_Color")) newMat.SetColor("_Primary_Color", biomeColor);
                    if (newMat.HasColor("_Secondary_Color")) newMat.SetColor("_Secondary_Color", biomeColor);
                    if (newMat.HasColor("_Tertiary_Color")) newMat.SetColor("_Tertiary_Color", biomeColor);
                    if (newMat.HasColor("_TintColor")) newMat.SetColor("_TintColor", biomeColor);
                    if (newMat.HasColor("_TopColor")) newMat.SetColor("_TopColor", biomeColor);

                    biomeMaterialsCache[cacheKey] = newMat;
                }

                currentMaterials[i] = biomeMaterialsCache[cacheKey];
                hasChanged = true;
            }

            if (hasChanged) rend.sharedMaterials = currentMaterials;
        }
    }

    private void SpawnNatureCluster(GameObject prefab, Vector3 centerPos, Transform container, int minCount, int maxCount, float radius, bool alignToSlope, Quaternion slopeRotation, Color tintColor)
    {
        if (prefab == null) return;
        int count = Random.Range(minCount, maxCount + 1);
        for (int i = 0; i < count; i++)
        {
            float ox = Random.Range(-radius, radius);
            float oz = Random.Range(-radius, radius);

            float cy = terrain.SampleHeight(new Vector3(centerPos.x + ox, 0, centerPos.z + oz)) + transform.position.y;

            Quaternion randomYRot = Quaternion.Euler(0, Random.Range(0f, 360f), 0);
            Quaternion baseRot = prefab.transform.rotation;
            Quaternion finalRot = alignToSlope ? (slopeRotation * randomYRot * baseRot) : (randomYRot * baseRot);

            GameObject obj = Instantiate(prefab, new Vector3(centerPos.x + ox, cy, centerPos.z + oz), finalRot, container);
            obj.transform.localScale *= Random.Range(0.7f, 1.3f);

            ApplyBiomeColor(obj, tintColor);
        }
    }

    private IEnumerator PopulateBiomesRoutine()
    {
        float w = terrain.terrainData.size.x;
        float l = terrain.terrainData.size.z;

        Transform treeContainer = new GameObject("TreesContainer").transform; treeContainer.SetParent(this.transform);
        Transform rockContainer = new GameObject("RocksContainer").transform; rockContainer.SetParent(this.transform);
        Transform grassContainer = new GameObject("GrassContainer").transform; grassContainer.SetParent(this.transform);
        Transform bushContainer = new GameObject("BushContainer").transform; bushContainer.SetParent(this.transform);
        Transform logContainer = new GameObject("LogsContainer").transform; logContainer.SetParent(this.transform);

        for (int i = 0; i < spawnAttempts; i++)
        {
            if (i % 250 == 0) yield return null;

            float px = Random.Range(10f, w - 10f); float pz = Random.Range(10f, l - 10f);
            float worldX = transform.position.x + px; float worldZ = transform.position.z + pz;
            float worldY = terrain.SampleHeight(new Vector3(worldX, 0, worldZ)) + transform.position.y;

            float normalizedX = px / w; float normalizedZ = pz / l;
            float steepness = terrain.terrainData.GetSteepness(normalizedX, normalizedZ);
            float localTemp = GetTemperature(normalizedX, normalizedZ);

            Vector3 terrainNormal = terrain.terrainData.GetInterpolatedNormal(normalizedX, normalizedZ);
            Quaternion slopeRotation = Quaternion.FromToRotation(Vector3.up, terrainNormal);

            Texture2D currentTreeTexture = forestTreeTexture;
            Color currentFoliageColor = forestFoliageColor;
            Color currentRockColor = forestRockColor;

            if (localTemp >= 0.65f)
            {
                currentTreeTexture = desertTreeTexture; currentFoliageColor = desertFoliageColor; currentRockColor = desertRockColor;
            }
            else if (localTemp <= 0.35f)
            {
                currentTreeTexture = snowTreeTexture; currentFoliageColor = snowFoliageColor; currentRockColor = snowRockColor;
            }

            if (steepness > 45f)
            {
                if (Random.value > 0.98f)
                {
                    GameObject rockPrefab = GetRandomPrefab(baseRocks);
                    if (rockPrefab != null)
                    {
                        Quaternion randomY = Quaternion.Euler(0, Random.Range(0, 360f), 0);
                        GameObject obj = Instantiate(rockPrefab, new Vector3(worldX, worldY, worldZ), slopeRotation * randomY * rockPrefab.transform.rotation, rockContainer);
                        obj.transform.localScale *= Random.Range(1.5f, 3f);
                        ApplyBiomeColor(obj, currentRockColor);
                    }
                }
                continue;
            }

            float density = Mathf.PerlinNoise(normalizedX * clusterScale + offsetX, normalizedZ * clusterScale + offsetZ);

            if (density > forestThreshold && steepness <= 25f)
            {
                float randomSpawn = Random.value;

                if (density > forestThreshold + 0.2f && randomSpawn > 0.85f && giantTrees != null && giantTrees.Length > 0)
                {
                    GameObject giantTreePrefab = GetRandomPrefab(giantTrees);
                    GameObject obj = Instantiate(giantTreePrefab, new Vector3(worldX, worldY, worldZ), Quaternion.Euler(0, Random.Range(0, 360f), 0) * giantTreePrefab.transform.rotation, treeContainer);
                    obj.transform.localScale *= Random.Range(1.0f, 1.4f);
                    ApplyBiomeTexture(obj, currentTreeTexture);
                }
                else if (randomSpawn > 0.65f)
                {
                    GameObject treePrefab = GetRandomPrefab(baseTrees);
                    if (treePrefab != null)
                    {
                        GameObject obj = Instantiate(treePrefab, new Vector3(worldX, worldY, worldZ), Quaternion.Euler(0, Random.Range(0, 360f), 0) * treePrefab.transform.rotation, treeContainer);
                        obj.transform.localScale *= Random.Range(0.8f, 1.1f);
                        ApplyBiomeTexture(obj, currentTreeTexture);
                    }
                }
                // --- ЗМІНЕНО: Більше трави (збільшено шанси та кількість) ---
                else if (randomSpawn > 0.30f)
                {
                    SpawnNatureCluster(GetRandomPrefab(baseGrass), new Vector3(worldX, worldY, worldZ), grassContainer, 40, 100, 8f, true, slopeRotation, currentFoliageColor);
                }
                // --- НОВЕ: Більше квітів, кущів та додано ГРИБИ! ---
                // --- НОВЕ: Більше квітів, кущів та додано ГРИБИ! ---
                else if (randomSpawn > 0.10f)
                {
                    float subRoll = Random.value;
                    GameObject naturePrefab;

                    if (subRoll > 0.5f) naturePrefab = GetRandomPrefab(baseBushes);
                    else if (subRoll > 0.2f) naturePrefab = GetRandomPrefab(baseFlowers);
                    else
                    {
                        // Спавнимо гриби ТІЛЬКИ якщо це ліс (не пустеля і не сніг)
                        if (localTemp > 0.35f && localTemp < 0.65f)
                        {
                            naturePrefab = GetRandomPrefab(baseMushrooms);
                        }
                        else
                        {
                            naturePrefab = GetRandomPrefab(baseBushes); // В інших біомах замінюємо на кущ
                        }
                    }

                    if (naturePrefab != null)
                    {
                        SpawnNatureCluster(naturePrefab, new Vector3(worldX, worldY, worldZ), bushContainer, 5, 15, 4.5f, true, slopeRotation, currentFoliageColor);
                    }
                }
            }
            else if (density < 0.3f)
            {
                if (Random.value > 0.93f)
                {
                    GameObject rockBase = GetRandomPrefab(baseRocks);
                    if (rockBase != null)
                    {
                        int clusterSize = Random.Range(1, 4);
                        for (int c = 0; c < clusterSize; c++)
                        {
                            float ox = Random.Range(-3f, 3f); float oz = Random.Range(-3f, 3f);
                            float cy = terrain.SampleHeight(new Vector3(worldX + ox, 0, worldZ + oz)) + transform.position.y;
                            GameObject obj = Instantiate(rockBase, new Vector3(worldX + ox, cy, worldZ + oz), slopeRotation * Quaternion.Euler(0, Random.Range(0f, 360f), 0) * rockBase.transform.rotation, rockContainer);
                            obj.transform.localScale *= Random.Range(0.5f, 1.5f);
                            ApplyBiomeColor(obj, currentRockColor);
                        }
                    }
                }
            }
            else
            {
                float rand = Random.value;
                if (rand > 0.95f)
                {
                    GameObject log = GetRandomPrefab(logPrefabs);
                    if (log != null) Instantiate(log, new Vector3(worldX, worldY, worldZ), Quaternion.Euler(0, Random.Range(0f, 360f), 0), logContainer);
                }
                else if (rand > 0.70f)
                {
                    // Рандомна трава поза густими лісами
                    SpawnNatureCluster(GetRandomPrefab(baseGrass), new Vector3(worldX, worldY, worldZ), grassContainer, 10, 25, 4f, true, slopeRotation, currentFoliageColor);
                }
            }
        }
    }

    private IEnumerator SpawnPOIsRoutine()
    {
        if (poiPrefabs == null || poiPrefabs.Length == 0) yield break;
        Transform poiContainer = new GameObject("POIContainer").transform; poiContainer.SetParent(this.transform);
        float w = terrain.terrainData.size.x; float l = terrain.terrainData.size.z; int spawnedCount = 0;

        for (int i = 0; i < 3000; i++)
        {
            if (i % 250 == 0) yield return null;

            if (spawnedCount >= maxPOIs) break;
            float px = Random.Range(20f, w - 20f); float pz = Random.Range(20f, l - 20f);
            float worldX = transform.position.x + px; float worldZ = transform.position.z + pz;
            float steepness = terrain.terrainData.GetSteepness(px / w, pz / l);
            if (steepness > maxPOISteepness) continue;
            float worldY = terrain.SampleHeight(new Vector3(worldX, 0, worldZ)) + transform.position.y;
            Vector3 spawnPos = new Vector3(worldX, worldY, worldZ);
            if (IsPositionClear(spawnPos, poiClearanceRadius))
            {
                Instantiate(GetRandomPrefab(poiPrefabs), spawnPos, Quaternion.Euler(0, Random.Range(0f, 360f), 0), poiContainer);
                spawnedCount++;
            }
        }
    }

    private IEnumerator SpawnExtractionCartsRoutine()
    {
        if (extractionCartPrefab == null) yield break;
        float w = terrain.terrainData.size.x; float l = terrain.terrainData.size.z; int spawnedCarts = 0;

        for (int i = 0; i < 5000; i++)
        {
            if (i % 250 == 0) yield return null;

            if (spawnedCarts >= extractionCartsAmount) break;
            float px = Random.Range(30f, w - 30f); float pz = Random.Range(30f, l - 30f);
            float worldX = transform.position.x + px; float worldZ = transform.position.z + pz;
            if (terrain.terrainData.GetSteepness(px / w, pz / l) < 8f)
            {
                float worldY = terrain.SampleHeight(new Vector3(worldX, 0, worldZ)) + transform.position.y;
                Vector3 spawnPos = new Vector3(worldX, worldY, worldZ);
                if (IsPositionClear(spawnPos, cartClearanceRadius))
                {
                    Instantiate(extractionCartPrefab, spawnPos, Quaternion.Euler(0, Random.Range(0, 360f), 0));
                    spawnedCarts++;
                }
            }
        }
    }

    private IEnumerator SpawnBorderMountainsRoutine()
    {
        if (borderMountainPrefabs == null || borderMountainPrefabs.Length == 0) yield break;

        Transform borderContainer = new GameObject("BorderMountainsContainer").transform;
        borderContainer.SetParent(this.transform);

        float w = terrain.terrainData.size.x;
        float l = terrain.terrainData.size.z;

        for (float x = -borderOffset; x <= w + borderOffset; x += borderSpacing)
        {
            SpawnSingleBorderMountain(new Vector3(x, 0, -borderOffset), borderContainer, w, l);
            SpawnSingleBorderMountain(new Vector3(x, 0, l + borderOffset), borderContainer, w, l);
            yield return null;
        }

        for (float z = -borderOffset; z <= l + borderOffset; z += borderSpacing)
        {
            SpawnSingleBorderMountain(new Vector3(-borderOffset, 0, z), borderContainer, w, l);
            SpawnSingleBorderMountain(new Vector3(w + borderOffset, 0, z), borderContainer, w, l);
            yield return null;
        }
    }

    private void SpawnSingleBorderMountain(Vector3 localPos, Transform container, float w, float l)
    {
        GameObject prefab = GetRandomPrefab(borderMountainPrefabs);
        if (prefab == null) return;

        float clampedX = Mathf.Clamp(localPos.x, 0, w);
        float clampedZ = Mathf.Clamp(localPos.z, 0, l);

        float worldX = transform.position.x + localPos.x;
        float worldZ = transform.position.z + localPos.z;

        float y = terrain.SampleHeight(new Vector3(transform.position.x + clampedX, 0, transform.position.z + clampedZ)) + transform.position.y;
        Vector3 spawnPos = new Vector3(worldX, y - 5f, worldZ);

        GameObject mnt = Instantiate(prefab, spawnPos, Quaternion.Euler(0, Random.Range(0f, 360f), 0), container);
        mnt.transform.localScale *= Random.Range(borderMinScale, borderMaxScale);

        float temp = GetTemperature(clampedX / w, clampedZ / l);
        Color rockColor = temp >= 0.65f ? desertRockColor : (temp <= 0.35f ? snowRockColor : forestRockColor);
        ApplyBiomeColor(mnt, rockColor);
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

    private GameObject GetRandomPrefab(GameObject[] array)
    {
        return (array == null || array.Length == 0) ? null : array[Random.Range(0, array.Length)];
    }
}