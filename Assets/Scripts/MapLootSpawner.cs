using UnityEngine;
using System.Collections;

public class MapLootSpawner : MonoBehaviour
{
    [Header("Loot Settings")]
    public GameObject xpCrystalPrefab;
    public int amountToSpawn = 200;

    private void Start()
    {
        StartCoroutine(SpawnLootAsync());
    }

    private IEnumerator SpawnLootAsync()
    {
        if (xpCrystalPrefab == null) yield break;

        // ФІКС 1: Надійне очікування поки гори повністю побудуються
        while (!WorldGenerator.IsGenerationDone)
        {
            yield return null;
        }

        if (Terrain.activeTerrain == null) yield break;

        TerrainData tData = Terrain.activeTerrain.terrainData;
        Vector3 tPos = Terrain.activeTerrain.transform.position;
        WorldGenerator wg = FindFirstObjectByType<WorldGenerator>();
        float absoluteWaterHeight = wg != null ? tPos.y + (wg.depth * wg.waterLevel) : -999f;

        int successfullySpawned = 0;
        int maxAttempts = amountToSpawn * 5;
        int attempts = 0;

        while (successfullySpawned < amountToSpawn && attempts < maxAttempts)
        {
            attempts++;

            float randX = Random.Range(20f, tData.size.x - 20f);
            float randZ = Random.Range(20f, tData.size.z - 20f);

            Vector3 spawnPos = new Vector3(tPos.x + randX, 0, tPos.z + randZ);
            spawnPos.y = Terrain.activeTerrain.SampleHeight(spawnPos) + tPos.y;

            // Захист від води
            if (spawnPos.y <= absoluteWaterHeight + 0.5f) continue;

            // Захист від крутих гір
            float steepness = tData.GetSteepness(randX / tData.size.x, randZ / tData.size.z);
            if (steepness > 35f) continue;

            // ФІКС 2: Простий і надійний спавн без блокування коллайдерами дерев
            GameObject loot = Instantiate(xpCrystalPrefab, spawnPos + Vector3.up * 0.5f, Quaternion.Euler(0f, Random.Range(0f, 360f), 0f));
            loot.transform.SetParent(transform);
            successfullySpawned++;

            if (attempts % 15 == 0) yield return null;
        }

        GameLog.Info($"[MapLootSpawner] Успішно розкидано {successfullySpawned} кристалів по мапі.");
    }
}