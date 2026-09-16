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

    [Header("Density")]
    // The region read as empty: the player could walk it end to end and meet
    // almost nobody. This multiplies the number of ENCOUNTERS, not the size of
    // each -- more places where something is happening, rather than bigger mobs.
    // Live enemy count stays bounded because groups stream in (see below).
    [Tooltip("Multiplier on encounterCount. 2 = twice as many encounters on the map.")]
    [Range(0.5f, 4f)] public float densityMultiplier = 1.5f;
    [Tooltip("Distance at which a group actually spawns its enemies. Keeps 80 encounters from meaning 260 live Animators.")]
    public float encounterActivationDistance = 95f;

    [Header("Sentry Patrols")]
    [Tooltip("Fraction of patrols that carry a horn and can raise a region alarm.")]
    [Range(0f, 1f)] public float hornPatrolRatio = 0.3f;
    [Tooltip("Fraction of patrols that walk a LONG route between distant points instead of circling one spot.")]
    [Range(0f, 1f)] public float longPatrolRatio = 0.35f;
    [Tooltip("Route radius for a long patrol. They cover ground, so they turn up where the player did not expect anyone.")]
    public float longPatrolRouteRadius = 38f;

    [Header("Watchtowers")]
    // ==== A FINISHED FEATURE SWITCHED OFF BY ONE BOOL ====
    //
    // Behind this flag: WatchtowerAlarm, a complete implementation with a
    // sweeping searchlight, a readable pool on the ground, a 1.2s grace before
    // it locks on and a decay afterwards; and RegionAlertDirector, a complete
    // and budgeted response with one concurrent alert, a 30s expiry, a 25s
    // cooldown and reinforcements that spawn 45m out and walk in. The prefab is
    // assigned, the counts and separation are tuned, and EnsureAlertDirector
    // already runs unconditionally.
    //
    // With it off, the only thing in the region that can raise an alarm is a
    // horn patrol, which is a 30% roll on a patrol. That is the cheapest new
    // content in the project because none of it has to be written.
    //
    // Turned on now that placement rejects water and cliffs — a tower is a tall
    // thing on a foundation, and the first pass at this feature was judged on
    // towers that had landed in a lake.
    [Tooltip("Places watchtowers with a sweeping searchlight that raises a region alarm when it locks on to the player.")]
    public bool enableWatchtowers = true;
    [Tooltip("Log every rejected tower position and why. Use with enableWatchtowers when none appear.")]
    public bool logWatchtowerPlacement = false;
    [Tooltip("Tower mesh. Assets/Locations/fbx2/MESH_ScoutTower is the one that matches the region kit.")]
    public GameObject watchtowerPrefab;
    [Tooltip("How many watchtowers to raise across the region.")]
    [Range(0, 8)] public int watchtowerCount = 3;
    [Tooltip("Guards stationed at the foot of each tower.")]
    [Range(0, 6)] public int watchtowerGuards = 2;
    [Tooltip("Keeps towers away from each other so their sweeps don't overlap into one unavoidable wall of light.")]
    public float watchtowerSeparation = 90f;

    [Header("Clear Reward")]
    [Tooltip("Префаб, що випадає у центрі групи, коли всю групу вбили. Зазвичай XP кристал")]
    public GameObject clearedRewardPrefab;
    [Tooltip("Скільки одиниць винагороди впаде, коли патруль зачищений")]
    [Range(0, 8)] public int patrolClearReward = 3;
    [Tooltip("Скільки одиниць винагороди впаде, коли табір зачищений (більший куш)")]
    [Range(0, 12)] public int campClearReward = 5;

    [Header("Spawner Integration")]
    [Tooltip("Притишити радіальний EnemySpawner, щоб патрулі й табори не змагалися з випадковими мобами навколо гравця")]
    public bool blockRandomSpawnerOnStart = true;
    [Tooltip("Наскільки притишити радіальний спавнер, поки директор веде регіон. 1 = без змін, 0 = повністю вимкнути.")]
    [Range(0f, 1f)] public float ambientSpawnerThrottle = 0.6f;

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
            EnemySpawner.AmbientThrottle = 1f;
            enabled = false;
            yield break;
        }

        // У non-conquered місіях притишуємо радіальний спавнер, щоб не було
        // хаосу між патрулями та випадковими мобами.
        //
        // Turned DOWN, not off, and through the throttle rather than through
        // IsSpawningBlocked — that flag is cleared by the spawner's own
        // self-heal within two seconds, so the old "block" here lasted exactly
        // as long as it took the player to reach the first patrol.
        if (!conquered && blockRandomSpawnerOnStart)
        {
            float throttle = ambientSpawnerThrottle;
            // Halve it again on the player's very first region, and thin the
            // hand-placed encounters to match. Two systems each at "reasonable"
            // still add up to no quiet moment, and a first region with no quiet
            // moment in it teaches nothing.
            if (PlayerPrefs.GetInt("TotalConqueredRegions", 0) == 0)
            {
                throttle *= 0.5f;
                densityMultiplier = Mathf.Min(densityMultiplier, 0.8f);
            }
            EnemySpawner.AmbientThrottle = throttle;
        }
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
            : Mathf.RoundToInt(encounterCount * densityMultiplier);

        EnsureAlertDirector();

        int placed = 0;
        int attempts = 0;
        // Raised from 12: two more rejections sit in this loop now, and a
        // shortfall here is missing content rather than a hitch — a rejected
        // candidate costs one SampleHeight and one GetSteepness.
        int maxAttempts = targetCount * 22;

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
                GameLog.Info($"[WorldEncounter] Placed {(isCamp ? "camp" : "patrol")} at {candidate} ({placed}/{targetCount}) {(conqueredMode ? "[conquered]" : "")}");

            if ((placed % 3) == 0) yield return null;
        }

        if (placed < targetCount)
            Debug.LogWarning($"[WorldEncounter] Only placed {placed}/{targetCount} encounters (attempts={attempts}). Reduce minSeparation or encounterCount.");

        // Towers last: they want to sit apart from each other and away from the
        // encounters already down, and placing them after gives the separation
        // test the full picture.
        if (!conqueredMode) yield return StartCoroutine(PlaceWatchtowers(totems));
    }

    // The alert coordinator is a plain runtime object -- nothing to wire in the
    // scene, so a region that was authored before this system existed still gets
    // working alarms.
    private void EnsureAlertDirector()
    {
        if (RegionAlertDirector.Instance != null) return;
        var go = new GameObject("[RegionAlertDirector]");
        go.transform.SetParent(transform);
        var dir = go.AddComponent<RegionAlertDirector>();
        dir.reinforcementPrefabs = enemyPrefabs;
    }

    private IEnumerator PlaceWatchtowers(RegionTotem[] totems)
    {
        if (!enableWatchtowers) yield break;

        if (watchtowerPrefab == null || watchtowerCount <= 0)
        {
            if (logWatchtowerPlacement)
                Debug.LogWarning($"[Watchtower] Nothing to place: prefab={(watchtowerPrefab == null ? "NULL" : watchtowerPrefab.name)}, count={watchtowerCount}.");
            yield break;
        }

        var towerPositions = new List<Vector3>(watchtowerCount);
        int attempts = 0;
        int maxAttempts = watchtowerCount * 40;
        int rejPlayer = 0, rejTotem = 0, rejTower = 0, rejGround = 0;

        while (towerPositions.Count < watchtowerCount && attempts < maxAttempts)
        {
            attempts++;
            Vector3 candidate = SampleCandidatePosition();
            if (player != null && Vector3.Distance(candidate, player.position) < minDistanceFromPlayer) { rejPlayer++; continue; }
            // A tower is a tall thing on a foundation; it has even less business
            // in a lake than a campfire does.
            if (!IsBuildableGround(candidate)) { rejGround++; continue; }

            bool tooCloseToTotem = false;
            for (int i = 0; i < totems.Length; i++)
            {
                if (totems[i] == null) continue;
                if (Vector3.Distance(candidate, totems[i].transform.position) < minDistanceFromTotems) { tooCloseToTotem = true; break; }
            }
            if (tooCloseToTotem) { rejTotem++; continue; }

            bool tooCloseToTower = false;
            for (int i = 0; i < towerPositions.Count; i++)
            {
                if (Vector3.Distance(candidate, towerPositions[i]) < watchtowerSeparation) { tooCloseToTower = true; break; }
            }
            if (tooCloseToTower) { rejTower++; continue; }

            SpawnWatchtower(candidate);
            towerPositions.Add(candidate);
            yield return null;
        }

        // Always report a shortfall. A silent zero is what made the first pass
        // look like the feature had never been wired up at all.
        if (towerPositions.Count < watchtowerCount)
        {
            Debug.LogWarning($"[Watchtower] Raised only {towerPositions.Count}/{watchtowerCount} after {attempts} attempts " +
                             $"(rejected: {rejPlayer} too near player, {rejTotem} too near a totem, {rejTower} too near another tower, {rejGround} unbuildable ground). " +
                             $"Lower watchtowerSeparation ({watchtowerSeparation}) or minDistanceFromPlayer ({minDistanceFromPlayer}) if the terrain is small.");
        }
        else if (logWatchtowerPlacement)
        {
            GameLog.Info($"[Watchtower] Raised {towerPositions.Count}/{watchtowerCount} watchtowers in {attempts} attempts.");
        }
    }

    private void SpawnWatchtower(Vector3 position)
    {
        GameObject tower = Instantiate(watchtowerPrefab, position, Quaternion.Euler(0f, Random.Range(0f, 360f), 0f));
        tower.name = "Watchtower";
        tower.transform.SetParent(transform);

        var alarm = tower.GetComponent<WatchtowerAlarm>();
        if (alarm == null) alarm = tower.AddComponent<WatchtowerAlarm>();

        // A tower with nobody at its foot is a free objective. The guards make
        // putting it out a real decision rather than a detour.
        if (watchtowerGuards <= 0 || enemyPrefabs == null || enemyPrefabs.Length == 0) return;

        GameObject guardRoot = new GameObject("Watchtower_Guards");
        guardRoot.transform.position = position;
        guardRoot.transform.SetParent(tower.transform);

        var eeg = guardRoot.AddComponent<EnemyEncounterGroup>();
        eeg.enemyPrefabs = enemyPrefabs;
        eeg.style = EnemyEncounterGroup.EncounterStyle.Camp;   // stationed, not wandering
        eeg.enemyCount = watchtowerGuards;
        eeg.spawnSpread = 4f;
        eeg.aggroRange = aggroRange;
        eeg.campfirePrefab = null;
        eeg.campGuardsFaceFire = false;
        eeg.clearedRewardPrefab = clearedRewardPrefab;
        eeg.clearedRewardCount = patrolClearReward;
        eeg.activationDistance = encounterActivationDistance;
        eeg.autoStart = true;
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

    // ==== THE GROUND HAS TO BE SOMEWHERE PEOPLE COULD STAND ====
    //
    // Placement checked three distances and nothing else: not the water level,
    // not the slope. SampleCandidatePosition is a uniform Random.Range over the
    // terrain bounds with a single SampleHeight, so any point in a lake or on a
    // cliff face was a perfectly valid answer — and with eighty encounters per
    // region, some of them always were.
    //
    // A camp half-submerged in a lake is not a difficulty problem, it is the
    // player deciding the world is broken. Two cheap rejections, both reading
    // numbers this project already exposes, shared by camps and towers alike.
    private static WorldGenerator s_gen;

    [Tooltip("Steepest ground, in degrees, that a camp or a tower may stand on. A group of standing figures on a steeper face reads as sliding off it.")]
    public float maxPlacementSteepness = 22f;
    [Tooltip("Clearance above the water surface. Measured above rather than at it, so nothing ends up camped in the shallows.")]
    public float waterClearance = 1.5f;

    private bool IsBuildableGround(Vector3 candidate)
    {
        if (s_gen == null) s_gen = Object.FindFirstObjectByType<WorldGenerator>();
        if (s_gen != null && candidate.y < s_gen.AbsoluteWaterHeight + waterClearance) return false;

        Terrain t = Terrain.activeTerrain;
        if (t == null) return true;

        Vector3 local = candidate - t.transform.position;
        Vector3 size = t.terrainData.size;
        if (size.x <= 0.01f || size.z <= 0.01f) return true;

        float steep = t.terrainData.GetSteepness(Mathf.Clamp01(local.x / size.x),
                                                 Mathf.Clamp01(local.z / size.z));
        return steep <= maxPlacementSteepness;
    }

    private bool IsValidPlacement(Vector3 candidate, RegionTotem[] totems)
    {
        if (player != null && Vector3.Distance(candidate, player.position) < minDistanceFromPlayer)
            return false;

        if (!IsBuildableGround(candidate)) return false;

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
        eeg.activationDistance = encounterActivationDistance;

        if (!isCamp)
        {
            // A long patrol walks between far-apart points instead of circling
            // one spot. This is what stops the map reading as a set of static
            // pockets the player can memorise and route around -- a long patrol
            // turns up somewhere the player already cleared.
            bool isLong = Random.value < longPatrolRatio;
            float routeRadius = isLong ? longPatrolRouteRadius : patrolRouteRadius;
            if (isLong)
            {
                groupObj.name = "Encounter_LongPatrol";
                eeg.waypointPauseDuration = 2.5f;   // less loitering, more ground covered
                eeg.patrolMoveSpeed = 2.2f;
            }

            eeg.patrolPoints = GeneratePatrolWaypoints(groupObj.transform, position, routeRadius);

            // Horns go on patrols only. A camp that could call the region in
            // would make every camp a mandatory stealth problem; a patrol that
            // can is something the player can watch coming and choose to avoid.
            if (Random.value < hornPatrolRatio)
            {
                eeg.hasHorn = true;
                groupObj.name += "_Horn";
            }
        }
    }

    private Transform[] GeneratePatrolWaypoints(Transform groupRoot, Vector3 center)
        => GeneratePatrolWaypoints(groupRoot, center, patrolRouteRadius);

    private Transform[] GeneratePatrolWaypoints(Transform groupRoot, Vector3 center, float routeRadius)
    {
        Transform[] points = new Transform[patrolWaypointCount];
        float baseAngle = Random.Range(0f, Mathf.PI * 2f);

        for (int i = 0; i < patrolWaypointCount; i++)
        {
            float angle = baseAngle + (i * (Mathf.PI * 2f / patrolWaypointCount)) + Random.Range(-0.35f, 0.35f);
            float r = routeRadius * Random.Range(0.7f, 1.3f);
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
