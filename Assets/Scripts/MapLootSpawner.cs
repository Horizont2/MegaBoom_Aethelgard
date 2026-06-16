using UnityEngine;
using System.Collections;

public class MapLootSpawner : MonoBehaviour
{
    [Header("Loot Settings")]
    public GameObject xpCrystalPrefab;
    public int amountToSpawn = 200;
    public float scatterRadius = 300f;

    [Header("AAA Placement Settings")]
    [Tooltip("Шари перешкод (дерева, каміння), щоб лут не спавнився в них")]
    public LayerMask obstacleLayer;
    public float minClearanceRadius = 1.5f;
    [Tooltip("Мінімальна висота (абсолютна), нижче якої лут не спавниться (Вода)")]
    public float absoluteWaterHeight = -999f;

    [Header("Performance")]
    public int spawnsPerFrame = 10;

    private void Start()
    {
        StartCoroutine(SpawnLootAsync());
    }

    private IEnumerator SpawnLootAsync()
    {
        if (xpCrystalPrefab == null) yield break;

        // Чекаємо, поки WorldGenerator закінчить роботу, щоб знати рівень води
        while (!WorldGenerator.IsGenerationDone) yield return null;

        WorldGenerator wg = FindFirstObjectByType<WorldGenerator>();
        if (wg != null) absoluteWaterHeight = wg.transform.position.y + (wg.depth * wg.waterLevel);

        int successfullySpawned = 0;
        int currentFrameSpawns = 0;
        int maxAttemptsPerItem = 10;

        for (int i = 0; i < amountToSpawn; i++)
        {
            for (int attempt = 0; attempt < maxAttemptsPerItem; attempt++)
            {
                Vector2 randomPoint = Random.insideUnitCircle * scatterRadius;
                Vector3 worldPos = transform.position + new Vector3(randomPoint.x, 0, randomPoint.y);

                if (Terrain.activeTerrain != null)
                {
                    // Читаємо висоту землі НАПРЯМУ (без лагів від Raycast)
                    worldPos.y = Terrain.activeTerrain.SampleHeight(worldPos) + Terrain.activeTerrain.transform.position.y;

                    // 1. Захист від води (не спавнимо на дні озера)
                    if (worldPos.y <= absoluteWaterHeight + 0.5f) continue;

                    // 2. Захист від стрімких скель
                    float normX = (worldPos.x - Terrain.activeTerrain.transform.position.x) / Terrain.activeTerrain.terrainData.size.x;
                    float normZ = (worldPos.z - Terrain.activeTerrain.transform.position.z) / Terrain.activeTerrain.terrainData.size.z;
                    if (Terrain.activeTerrain.terrainData.GetSteepness(normX, normZ) > 40f) continue;

                    Vector3 spawnPos = worldPos + Vector3.up * 0.4f;

                    // 3. Захист від накладання на дерева/каміння
                    if (!Physics.CheckSphere(spawnPos, minClearanceRadius, obstacleLayer))
                    {
                        GameObject loot = Instantiate(xpCrystalPrefab, spawnPos, Quaternion.Euler(0f, Random.Range(0f, 360f), 0f));
                        loot.transform.SetParent(transform);
                        successfullySpawned++;
                        break;
                    }
                }
            }

            currentFrameSpawns++;
            if (currentFrameSpawns >= spawnsPerFrame)
            {
                currentFrameSpawns = 0;
                yield return null;
            }
        }
    }
}