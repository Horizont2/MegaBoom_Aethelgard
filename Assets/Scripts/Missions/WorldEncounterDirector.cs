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

    [Header("Clear Reward")]
    [Tooltip("Префаб, що випадає у центрі групи, коли всю групу вбили. Зазвичай XP кристал")]
    public GameObject clearedRewardPrefab;
    [Tooltip("Скільки одиниць винагороди впаде, коли патруль зачищений")]
    [Range(0, 8)] public int patrolClearReward = 3;
    [Tooltip("Скільки одиниць винагороди впаде, коли табір зачищений (більший куш)")]
    [Range(0, 12)] public int campClearReward = 5;

    [Header("Spawner Integration")]
    [Tooltip("Блокувати радіальний EnemySpawner, щоб не було безглуздих спавнів навколо гравця")]
    public bool blockRandomSpawnerOnStart = true;

    [Header("Conquered Regions")]
    [Tooltip("Чи спавнити патрулі у вже захопленому регіоні (для атмосфери/farming). За замовчуванням вимкнено — у захопленому регіоні мають спавнитися лише звичайні мобі радіальним EnemySpawner'ом.")]
    public bool spawnInConqueredRegion = false;
    [Tooltip("Множник кількості енкаунтерів у захопленому регіоні (0.3 = 30% від звичайного). Використовується тільки якщо spawnInConqueredRegion=true.")]
    [Range(0f, 1f)] public float conqueredCountMultiplier = 0.4f;
    [Tooltip("У захоплених регіонах вимикаємо табори (атмосфера 'зачистили' — лиш дрібні патрулі)")]
    public bool conqueredPatrolsOnly = true;

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

        if (!IsAnyRegionMode())
        {
            enabled = false;
            yield break;
        }

        bool conquered = IsConqueredRegion();
        if (conquered && !spawnInConqueredRegion)
        {
            // Хочемо звичайні радіальні спавни у захопленому регіоні —
            // повністю виходимо, і НЕ блокуємо EnemySpawner. Раніше цей
            // директор першим ділом вирубав радіальний спавнер (для
            // атмосфери "тільки патрулі"), і якщо ми пропускали патрулі
            // без розблокування — регіон лишався порожнім.
            EnemySpawner.IsSpawningBlocked = false;
            enabled = false;
            yield break;
        }

        // У non-conquered місіях блокуємо радіальний спавнер щоб не було
        // хаосу між патрулями та випадковими мобами (стара поведінка).
        if (!conquered && blockRandomSpawnerOnStart) EnemySpawner.IsSpawningBlocked = true;
        yield return StartCoroutine(RunDirectorRoutine(conquered));
    }

    /// <summary>True when in a region mission flow (active OR conquered).</summary>
    public static bool IsAnyRegionMode()
    {
        if (PlayerPrefs.GetInt("IsRegionMission", 0) != 1) return false;
        if (GameManager.Instance != null && GameManager.Instance.currentRegion != null) return true;
        if (MissionInitializer.PendingMissionRegion != null) return true;
        return false;
    }

    /// <summary>True when the region has already been purified.</summary>
    public static bool IsConqueredRegion()
    {
        RegionData r = (GameManager.Instance != null) ? GameManager.Instance.currentRegion : null;
        if (r == null) r = MissionInitializer.PendingMissionRegion;
        return r != null && r.currentState == RegionState.Conquered;
    }

    /// <summary>Legacy gate: active (non-conquered) region mission only.</summary>
    public static bool IsActiveRegionMission()
    {
        if (!IsAnyRegionMode()) return false;
        return !IsConqueredRegion();
    }

    private IEnumerator RunDirectorRoutine(bool conqueredMode)
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

        int targetCount = conqueredMode
            ? Mathf.Max(1, Mathf.RoundToInt(encounterCount * conqueredCountMultiplier))
            : encounterCount;

        int placed = 0;
        int attempts = 0;
        int maxAttempts = targetCount * 12;

        while (placed < targetCount && attempts < maxAttempts)
        {
            attempts++;
            Vector3 candidate = SampleCandidatePosition();
            if (!IsValidPlacement(candidate, totems)) continue;

            bool isCamp;
            if (conqueredMode && conqueredPatrolsOnly)
            {
                isCamp = false;
            }
            else
            {
                float campRatio = isWinter ? winterCampRatio : defaultCampRatio;
                isCamp = Random.value < campRatio && campfirePrefab != null;
            }

            SpawnEncounter(candidate, isCamp);
            placedPositions.Add(candidate);
            placed++;

            if (logPlacements)
                Debug.Log($"[WorldEncounter] Placed {(isCamp ? "camp" : "patrol")} at {candidate} ({placed}/{targetCount}) {(conqueredMode ? "[conquered]" : "")}");

            if ((placed % 3) == 0) yield return null;
        }

        if (placed < targetCount)
            Debug.LogWarning($"[WorldEncounter] Only placed {placed}/{targetCount} encounters (attempts={attempts}). Reduce minSeparation or encounterCount.");
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
        // Camps stay axis-aligned so the ring around the fire reads symmetric.
        // Patrols get a random facing because their group center moves anyway.
        groupObj.transform.position = position;
        groupObj.transform.rotation = isCamp ? Quaternion.identity : Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);
        groupObj.transform.SetParent(transform);

        EnemyEncounterGroup eeg = groupObj.AddComponent<EnemyEncounterGroup>();
        eeg.enemyPrefabs = enemyPrefabs;
        eeg.style = isCamp ? EnemyEncounterGroup.EncounterStyle.Camp : EnemyEncounterGroup.EncounterStyle.Patrol;
        eeg.enemyCount = isCamp ? campGroupSize : patrolGroupSize;
        eeg.roamRadius = roamRadius;
        eeg.aggroRange = aggroRange;
        eeg.campfirePrefab = isCamp ? campfirePrefab : null;
        eeg.clearedRewardPrefab = clearedRewardPrefab;
        eeg.clearedRewardCount = isCamp ? campClearReward : patrolClearReward;
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
