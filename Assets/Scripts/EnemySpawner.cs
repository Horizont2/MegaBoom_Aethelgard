using UnityEngine;
using System.Collections;
using System.Collections.Generic;

[System.Serializable]
public class SpawnableEnemy
{
    public GameObject enemyPrefab;
    public float spawnAtMinute = 0f;
}

public class EnemySpawner : MonoBehaviour
{
    public static bool IsSpawningBlocked = false;

    [Header("Spawner Settings")]
    public int maxEnemiesOnMap = 35; // ����� ���
    public SpawnableEnemy[] enemyPool;
    public Transform player;
    public float baseSpawnInterval = 1.5f;

    [Header("Spawn Area")]
    public float minSpawnRadius = 10f;
    public float maxSpawnRadius = 20f;

    private float timer;
    private WorldGenerator worldGen;
    private readonly List<GameObject> availableEnemiesCache = new List<GameObject>(16);

    private void Start()
    {
        IsSpawningBlocked = false;
        worldGen = FindFirstObjectByType<WorldGenerator>();
    }

    // Recheck every 2 s whether the "blocked" state is still legitimate.
    // Self-healing: if IsSpawningBlocked is true but no totem is
    // currently mid-activation (all totems purified or none activating),
    // we assume the block is stale (a coroutine forgot to reset it) and
    // release it so the radial spawner works again.
    private float unblockCheckTimer = 0f;

    private void Update()
    {
        if (IsSpawningBlocked)
        {
            timer = 0f;
            unblockCheckTimer += Time.deltaTime;
            if (unblockCheckTimer >= 2f)
            {
                unblockCheckTimer = 0f;
                if (!IsAnyTotemActivating())
                {
                    IsSpawningBlocked = false;
                }
            }
            if (IsSpawningBlocked) return;
        }
        else unblockCheckTimer = 0f;

        if (worldGen != null && !WorldGenerator.IsGenerationDone) return;

        if (EnemyAI.ActiveEnemiesCount >= maxEnemiesOnMap) return;

        // ��������� ������ ���������
        if (player == null)
        {
            GameObject pObj = GameObject.FindGameObjectWithTag("Player");
            if (pObj != null) player = pObj.transform;
            else return;
        }

        if (enemyPool == null || enemyPool.Length == 0) return;

        timer += Time.deltaTime;

        float minutes = GameManager.survivalTime / 60f;
        float currentSpawnInterval = Mathf.Max(0.3f, baseSpawnInterval / (1f + minutes * 0.2f));

        if (timer >= currentSpawnInterval)
        {
            SpawnEnemy(minutes);
            timer = 0f;
        }
    }

    // Any totem in the scene that is activated but not yet purified?
    // If yes, the block is legitimate. If no, the block is stale.
    private static bool IsAnyTotemActivating()
    {
        RegionTotem[] all = Object.FindObjectsByType<RegionTotem>(FindObjectsSortMode.None);
        for (int i = 0; i < all.Length; i++)
        {
            var t = all[i];
            if (t == null) continue;
            if (t.isActivated && !t.isPurified) return true;
        }
        return false;
    }

    private void SpawnEnemy(float minutesSurvived)
    {
        availableEnemiesCache.Clear();
        for (int i = 0; i < enemyPool.Length; i++)
        {
            SpawnableEnemy se = enemyPool[i];
            if (minutesSurvived >= se.spawnAtMinute)
            {
                availableEnemiesCache.Add(se.enemyPrefab);
            }
        }

        if (availableEnemiesCache.Count == 0) return;

        float randomDist = Random.Range(minSpawnRadius, maxSpawnRadius);
        Vector2 randomCircle = Random.insideUnitCircle.normalized * randomDist;

        float spawnX = player.position.x + randomCircle.x;
        float spawnZ = player.position.z + randomCircle.y;
        float spawnY = 0.5f;

        if (Terrain.activeTerrain != null)
        {
            Vector3 worldPos = new Vector3(spawnX, 0, spawnZ);
            spawnY = Terrain.activeTerrain.SampleHeight(worldPos) + Terrain.activeTerrain.transform.position.y;
        }

        Vector3 spawnPos = new Vector3(spawnX, spawnY, spawnZ);

        int randomIndex = Random.Range(0, availableEnemiesCache.Count);
        GameObject selectedPrefab = availableEnemiesCache[randomIndex];

        GameObject newEnemy;
        if (ObjectPoolManager.Instance != null)
            newEnemy = ObjectPoolManager.Instance.SpawnFromPool(selectedPrefab, spawnPos, Quaternion.identity);
        else
            newEnemy = Instantiate(selectedPrefab, spawnPos, Quaternion.identity);

        EnemyAI enemyScript = newEnemy.GetComponent<EnemyAI>();
        if (enemyScript != null)
        {
            enemyScript.maxHealth *= (1f + minutesSurvived * 0.4f);
            enemyScript.damage *= (1f + minutesSurvived * 0.15f);
            enemyScript.moveSpeed *= Mathf.Min(1.5f, 1f + minutesSurvived * 0.05f);
            enemyScript.xpRewardMultiplier = 1f + (minutesSurvived * 0.2f);
        }

        StartCoroutine(RiseFromGroundRoutine(newEnemy));
    }

    private IEnumerator RiseFromGroundRoutine(GameObject enemy)
    {
        EnemyAI ai = enemy.GetComponent<EnemyAI>();
        if (ai != null) ai.isCinematicFrozen = true;

        Vector3 finalPos = enemy.transform.position;
        enemy.transform.position = finalPos - new Vector3(0, 2.5f, 0);

        float duration = 1.5f;
        float elapsed = 0f;

        while (elapsed < duration && enemy != null && enemy.activeInHierarchy)
        {
            elapsed += Time.deltaTime;
            float t = elapsed / duration;
            t = t * t * (3f - 2f * t);

            enemy.transform.position = Vector3.Lerp(finalPos - new Vector3(0, 2.5f, 0), finalPos, t);
            yield return null;
        }

        if (enemy != null && enemy.activeInHierarchy)
        {
            enemy.transform.position = finalPos;
            if (ai != null) ai.isCinematicFrozen = false;
        }
    }
}