using UnityEngine;
using System.Collections;
using System.Collections.Generic;

public class WorldEncounterDirector : MonoBehaviour
{
    [Header("Enemy Pool")]
    [Tooltip("Префаби ворогів, які використовуються в усіх енкаунтерах")]
    public GameObject[] enemyPrefabs;

    [Header("Group Sizes")]
    [Tooltip("Скільки ворогів у патрульній групі")]
    [Range(1, 8)] public int patrolGroupSize = 3;
    [Tooltip("Скільки ворогів у таборі біля вогнища")]
    [Range(1, 10)] public int campGroupSize = 4;

    [Header("Distribution")]
    [Tooltip("Кількість енкаунтерів на старті місії")]
    public int encounterCount = 10;
    [Tooltip("Мінімальна відстань між енкаунтерами")]
    public float minSeparation = 28f;
    [Tooltip("Мінімальна відстань від гравця")]
    public float minDistanceFromPlayer = 35f;
    [Tooltip("Мінімальна відстань від тотемів")]
    public float minDistanceFromTotems = 20f;
    [Tooltip("Відступ від країв терейну (м)")]
    public float terrainEdgePadding = 15f;

    [Header("Biome Logic")]
    [Tooltip("Частка таборів навколо вогнищ у зимовому біомі")]
    [Range(0f, 1f)] public float winterCampRatio = 0.6f;
    [Tooltip("Частка таборів у інших біомах")]
    [Range(0f, 1f)] public float defaultCampRatio = 0.15f;
    [Tooltip("Префаб вогнища, що ставиться у центрі таборів")]
    public GameObject campfirePrefab;

    [Header("Patrol Routing")]
    [Tooltip("Скільки waypoints у згенерованому патрульному маршруті")]
    [Range(2, 6)] public int patrolWaypointCount = 3;
    [Tooltip("Радіус маршруту патруля від точки спавну")]
    public float patrolRouteRadius = 12f;

    [Header("Group Tuning")]
    public float aggroRange = 14f;
    public float roamRadius = 3.5f;

    [Header("Spawner Integration")]
    [Tooltip("Блокувати радіальний EnemySpawner, щоб не було безглуздих спавнів навколо гравця")]
    public bool blockRandomSpawnerOnStart = true;

    [Header("Diagnostics")]
    public bool logPlacements = false;

    private Transform player;
    private Vector3 terrainMin;
    private Vector3 terrainMax;
    private readonly List<Vector3> placedPositions = new List<Vector3>(32);

    private void Start()
    {
        StartCoroutine(GateAndRun());
    }

    private IEnumerator GateAndRun()
    {
        // Wait one frame so MissionInitializer.Start() runs first and gets
        // a chance to set the IsRegionMission flag (it can flip on through
        // testFallbackRegion even when PlayerPrefs was clean).
        yield return null;

        if (!IsActiveRegionMission())
        {
            enabled = false;
            yield break;
        }

        if (blockRandomSpawnerOnStart) EnemySpawner.IsSpawningBlocked = true;
        yield return StartCoroutine(RunDirectorRoutine());
    }

    public static bool IsActiveRegionMission()
    {
        if (PlayerPrefs.GetInt("IsRegionMission", 0) != 1) return false;

        if (GameManager.Instance != null && GameManager.Instance.currentRegion != null)
        {
            if (GameManager.Instance.currentRegion.currentState == RegionState.Conquered)
                return false;
        }
        else if (MissionInitializer.PendingMissionRegion == null)
        {
            // No region data wired up — refuse rather than spawn into a blank slate.
            return false;
        }

        return true;
    }

    private IEnumerator RunDirectorRoutine()
    {
        float deadline = Time.unscaledTime + 30f;
        while (!WorldGenerator.IsGenerationDone && Time.unscaledTime < deadline)
            yield return null;

        yield return new WaitForSeconds(2f);

        GameObject pObj = GameObject.FindGameObjectWithTag("Player");
        if (pObj != null) player = pObj.transform;

        if (Terrain.activeTerrain != null)
        {
            terrainMin = Terrain.activeTerrain.transform.position;
            terrainMax = terrainMin + Terrain.activeTerrain.terrainData.size;
        }
        else
        {
            terrainMin = new Vector3(-100f, 0f, -100f);
            terrainMax = new Vector3(100f, 0f, 100f);
        }

        bool isWinter = ResolveIsWinter();
        RegionTotem[] totems = FindObjectsByType<RegionTotem>(FindObjectsSortMode.None);

        int placed = 0;
        int attempts = 0;
        int maxAttempts = encounterCount * 12;

        while (placed < encounterCount && attempts < maxAttempts)
        {
            attempts++;
            Vector3 candidate = SampleCandidatePosition();
            if (!IsValidPlacement(candidate, totems)) continue;

            float campRatio = isWinter ? winterCampRatio : defaultCampRatio;
            bool isCamp = Random.value < campRatio && campfirePrefab != null;

            SpawnEncounter(candidate, isCamp);
            placedPositions.Add(candidate);
            placed++;

            if (logPlacements)
                Debug.Log($"[WorldEncounter] Placed {(isCamp ? "camp" : "patrol")} at {candidate} ({placed}/{encounterCount})");

            // Spread cost across frames to keep startup smooth
            if ((placed % 3) == 0) yield return null;
        }

        if (placed < encounterCount)
            Debug.LogWarning($"[WorldEncounter] Only placed {placed}/{encounterCount} encounters (attempts={attempts}). Reduce minSeparation or encounterCount.");
    }

    private bool ResolveIsWinter()
    {
        if (GameManager.Instance != null && GameManager.Instance.currentRegion != null)
            return GameManager.Instance.currentRegion.regionBiome == RegionBiome.Winter;
        return PlayerPrefs.GetInt("RegionBiomeType", 0) == (int)RegionBiome.Winter;
    }

    private Vector3 SampleCandidatePosition()
    {
        float x = Random.Range(terrainMin.x + terrainEdgePadding, terrainMax.x - terrainEdgePadding);
        float z = Random.Range(terrainMin.z + terrainEdgePadding, terrainMax.z - terrainEdgePadding);
        Vector3 pos = new Vector3(x, 0f, z);
        if (Terrain.activeTerrain != null)
            pos.y = Terrain.activeTerrain.SampleHeight(pos) + Terrain.activeTerrain.transform.position.y;
        return pos;
    }

    private bool IsValidPlacement(Vector3 candidate, RegionTotem[] totems)
    {
        if (player != null && Vector3.Distance(candidate, player.position) < minDistanceFromPlayer)
            return false;

        for (int i = 0; i < totems.Length; i++)
        {
            if (totems[i] == null) continue;
            if (Vector3.Distance(candidate, totems[i].transform.position) < minDistanceFromTotems)
                return false;
        }

        for (int i = 0; i < placedPositions.Count; i++)
        {
            if (Vector3.Distance(candidate, placedPositions[i]) < minSeparation)
                return false;
        }

        return true;
    }

    private void SpawnEncounter(Vector3 position, bool isCamp)
    {
        GameObject groupObj = new GameObject(isCamp ? "Encounter_Camp" : "Encounter_Patrol");
        groupObj.transform.position = position;
        groupObj.transform.rotation = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);
        groupObj.transform.SetParent(transform);

        EnemyEncounterGroup eeg = groupObj.AddComponent<EnemyEncounterGroup>();
        eeg.enemyPrefabs = enemyPrefabs;
        eeg.style = isCamp ? EnemyEncounterGroup.EncounterStyle.Camp : EnemyEncounterGroup.EncounterStyle.Patrol;
        eeg.enemyCount = isCamp ? campGroupSize : patrolGroupSize;
        eeg.roamRadius = roamRadius;
        eeg.aggroRange = aggroRange;
        eeg.campfirePrefab = isCamp ? campfirePrefab : null;
        eeg.autoStart = true;

        if (!isCamp)
        {
            eeg.patrolPoints = GeneratePatrolWaypoints(groupObj.transform, position);
        }
    }

    private Transform[] GeneratePatrolWaypoints(Transform groupRoot, Vector3 center)
    {
        Transform[] points = new Transform[patrolWaypointCount];
        float baseAngle = Random.Range(0f, Mathf.PI * 2f);

        for (int i = 0; i < patrolWaypointCount; i++)
        {
            float angle = baseAngle + (i * (Mathf.PI * 2f / patrolWaypointCount)) + Random.Range(-0.35f, 0.35f);
            float r = patrolRouteRadius * Random.Range(0.7f, 1.3f);
            Vector3 pos = center + new Vector3(Mathf.Cos(angle) * r, 0f, Mathf.Sin(angle) * r);

            // Clamp to terrain bounds
            pos.x = Mathf.Clamp(pos.x, terrainMin.x + terrainEdgePadding, terrainMax.x - terrainEdgePadding);
            pos.z = Mathf.Clamp(pos.z, terrainMin.z + terrainEdgePadding, terrainMax.z - terrainEdgePadding);

            if (Terrain.activeTerrain != null)
                pos.y = Terrain.activeTerrain.SampleHeight(pos) + Terrain.activeTerrain.transform.position.y;

            GameObject wp = new GameObject($"PatrolWP_{i}");
            wp.transform.position = pos;
            wp.transform.SetParent(groupRoot);
            points[i] = wp.transform;
        }

        return points;
    }
}
