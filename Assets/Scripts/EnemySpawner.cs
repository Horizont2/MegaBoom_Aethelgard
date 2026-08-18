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
    public int maxEnemiesOnMap = 35;
    public SpawnableEnemy[] enemyPool;
    public Transform player;
    public float baseSpawnInterval = 1.5f;

    [Header("Spawn Area")]
    public float minSpawnRadius = 10f;
    public float maxSpawnRadius = 20f;

    // ─────────────────────────────────────────────────────────────
    //  Rhythm / pacing
    // ─────────────────────────────────────────────────────────────
    // The old spawner was a flat trickle — one enemy every N seconds,
    // N shrinking slowly with time — so the map just sat pinned at the cap
    // and every minute felt the same. This layers WAVES of intensity on top:
    // calm "lull" breathers where the count drops and the player can push
    // out, then "surge" phases that spawn faster and sometimes send a whole
    // PACK rushing in from one direction. Every Nth surge escalates into a
    // heavier horde. All knobs are exposed so the feel can be tuned without
    // touching code; set useRhythm = false to restore the old flat behaviour.
    [Header("Rhythm / Pacing")]
    public bool useRhythm = true;
    [Tooltip("Calm breather length (seconds), random in [x, y]. Count drops here.")]
    public Vector2 lullDuration = new Vector2(7f, 11f);
    [Tooltip("Surge length (seconds), random in [x, y]. Denser + pack rushes.")]
    public Vector2 surgeDuration = new Vector2(11f, 18f);
    [Tooltip("Spawn-interval multiplier per phase. >1 = slower/calmer, <1 = faster/denser.")]
    public float lullIntervalMult = 2.0f;
    public float surgeIntervalMult = 0.55f;
    [Range(0f, 1f)]
    [Tooltip("Chance a surge tick arrives as a directional pack instead of a lone enemy.")]
    public float packChance = 0.3f;
    [Tooltip("Pack size, random in [x, y].")]
    public Vector2Int packSize = new Vector2Int(3, 6);
    [Tooltip("Every Nth surge is a heavier horde (bigger, guaranteed pack). 0 = never.")]
    public int hordeEveryNSurges = 3;

    private enum SpawnPhase { Lull, Surge }
    private SpawnPhase phase = SpawnPhase.Lull;
    private float phaseTimer = 0f;
    private float phaseDuration = 8f;
    private int surgeIndex = 0;

    private float timer;
    private WorldGenerator worldGen;
    private readonly List<GameObject> availableEnemiesCache = new List<GameObject>(16);

    private void Start()
    {
        IsSpawningBlocked = false;
        worldGen = FindFirstObjectByType<WorldGenerator>();
        BeginPhase(SpawnPhase.Lull);
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

        if (player == null)
        {
            GameObject pObj = GameObject.FindGameObjectWithTag("Player");
            if (pObj != null) player = pObj.transform;
            else return;
        }

        if (enemyPool == null || enemyPool.Length == 0) return;

        float minutes = GameManager.survivalTime / 60f;

        // Long-term ramp (unchanged): the steady interval still tightens as
        // the run goes on.
        float baseInterval = Mathf.Max(0.3f, baseSpawnInterval / (1f + minutes * 0.2f));

        float intervalMult = 1f;
        if (useRhythm)
        {
            // Advance the calm/surge cycle so intensity ebbs and flows.
            phaseTimer += Time.deltaTime;
            if (phaseTimer >= phaseDuration)
                BeginPhase(phase == SpawnPhase.Lull ? SpawnPhase.Surge : SpawnPhase.Lull);

            intervalMult = (phase == SpawnPhase.Lull) ? lullIntervalMult : surgeIntervalMult;
        }

        float currentSpawnInterval = baseInterval * intervalMult;

        timer += Time.deltaTime;
        if (timer >= currentSpawnInterval)
        {
            timer = 0f;

            bool horde = useRhythm && phase == SpawnPhase.Surge
                         && hordeEveryNSurges > 0 && (surgeIndex % hordeEveryNSurges == 0);

            // During surges, some ticks arrive as a directional pack — a wave
            // that visibly rushes in from one side — instead of a lone enemy
            // drifting in from a random angle.
            if (useRhythm && phase == SpawnPhase.Surge && (horde || Random.value < packChance))
                SpawnCluster(minutes, horde);
            else
                SpawnEnemy(minutes);
        }
    }

    private void BeginPhase(SpawnPhase p)
    {
        phase = p;
        phaseTimer = 0f;
        if (p == SpawnPhase.Lull)
        {
            phaseDuration = Random.Range(lullDuration.x, lullDuration.y);
        }
        else
        {
            phaseDuration = Random.Range(surgeDuration.x, surgeDuration.y);
            surgeIndex++;
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

    // A pack that rushes in from ONE direction — reads as a deliberate wave
    // instead of scattered singles. Respects the on-map cap.
    private void SpawnCluster(float minutesSurvived, bool horde)
    {
        int count = Random.Range(packSize.x, packSize.y + 1);
        if (horde) count = Mathf.RoundToInt(count * 1.6f);

        float baseAngle = Random.Range(0f, Mathf.PI * 2f); // one arc for the whole pack
        for (int i = 0; i < count; i++)
        {
            if (EnemyAI.ActiveEnemiesCount >= maxEnemiesOnMap) break;
            float angle = baseAngle + Random.Range(-0.5f, 0.5f); // ~±29° spread
            float dist = Random.Range(minSpawnRadius, maxSpawnRadius);
            SpawnOneAt(PositionFromPlayer(angle, dist), minutesSurvived);
        }
    }

    private void SpawnEnemy(float minutesSurvived)
    {
        float dist = Random.Range(minSpawnRadius, maxSpawnRadius);
        float angle = Random.Range(0f, Mathf.PI * 2f);
        SpawnOneAt(PositionFromPlayer(angle, dist), minutesSurvived);
    }

    private Vector3 PositionFromPlayer(float angleRad, float dist)
    {
        float x = player.position.x + Mathf.Cos(angleRad) * dist;
        float z = player.position.z + Mathf.Sin(angleRad) * dist;
        float y = 0.5f;
        if (Terrain.activeTerrain != null)
            y = Terrain.activeTerrain.SampleHeight(new Vector3(x, 0f, z)) + Terrain.activeTerrain.transform.position.y;
        return new Vector3(x, y, z);
    }

    private void SpawnOneAt(Vector3 spawnPos, float minutesSurvived)
    {
        availableEnemiesCache.Clear();
        for (int i = 0; i < enemyPool.Length; i++)
        {
            SpawnableEnemy se = enemyPool[i];
            if (minutesSurvived >= se.spawnAtMinute)
                availableEnemiesCache.Add(se.enemyPrefab);
        }
        if (availableEnemiesCache.Count == 0) return;

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
