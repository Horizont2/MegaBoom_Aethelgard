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
    [Tooltip("LATE-GAME cap. Early game uses a smaller cap that grows to this (see startCap / capRampMinutes).")]
    public int maxEnemiesOnMap = 35;
    public SpawnableEnemy[] enemyPool;
    public Transform player;
    public float baseSpawnInterval = 1.5f;

    [Header("Spawn Area")]
    public float minSpawnRadius = 10f;
    public float maxSpawnRadius = 20f;

    // ─────────────────────────────────────────────────────────────
    //  Pacing director (AAA-style intensity curve)
    // ─────────────────────────────────────────────────────────────
    // Instead of a constant swarm, the spawner runs an explicit intensity
    // cycle so the run breathes and the player gets real windows to farm:
    //   RELAX   — a calm farming/exploration window; spawns nearly stop and
    //             the on-map count DRAINS so the map clears out.
    //   BUILDUP — pressure ramps back up (music swells as a tell).
    //   PEAK    — the fight: dense spawns + directional pack rushes.
    // A grace period keeps the opening calm, and the on-map cap GROWS with
    // time (few enemies early → busy late) instead of sitting at max from
    // second one. Set useDirector = false for a flat spawn.
    [Header("Pacing Director")]
    public bool useDirector = true;
    [Tooltip("Calm opening (seconds) before the intensity cycle begins — time to learn / farm.")]
    public float gracePeriod = 45f;

    [Header("Phase length (seconds, random x..y)")]
    public Vector2 relaxDuration = new Vector2(16f, 24f);   // farming window
    public Vector2 buildupDuration = new Vector2(5f, 8f);
    public Vector2 peakDuration = new Vector2(12f, 18f);

    [Header("Phase density (spawn-interval multiplier; >1 = calmer)")]
    public float relaxIntervalMult = 5f;
    public float buildupIntervalMult = 1.4f;
    public float peakIntervalMult = 0.6f;
    [Range(0f, 1f)]
    [Tooltip("Fraction of the on-map cap allowed during RELAX so the field drains for farming.")]
    public float relaxCapFactor = 0.4f;

    [Header("On-map cap over time")]
    [Tooltip("Enemies allowed on the map at the very start of the run.")]
    public int startCap = 8;
    [Tooltip("Minutes for the cap to grow from startCap up to maxEnemiesOnMap.")]
    public float capRampMinutes = 8f;

    [Header("Peak flavour")]
    [Range(0f, 1f)]
    [Tooltip("Chance a PEAK tick arrives as a directional pack instead of a lone enemy.")]
    public float packChance = 0.3f;
    public Vector2Int packSize = new Vector2Int(3, 6);
    [Tooltip("Every Nth peak is a heavier horde. 0 = never.")]
    public int hordeEveryNPeaks = 4;

    [Header("Story-region ground ambush")]
    [Tooltip("On region missions, skeletons occasionally CLAW UP from the ground ahead of the running player — a scripted scare on top of the normal spawns.")]
    public bool enableGroundAmbush = true;
    [Tooltip("Seconds between ambush checks (random x..y).")]
    public Vector2 groundAmbushInterval = new Vector2(10f, 18f);
    [Range(0f, 1f)]
    [Tooltip("Chance an ambush actually fires on each check.")]
    public float groundAmbushChance = 0.55f;
    [Tooltip("How many skeletons burst up per ambush (random x..y).")]
    public Vector2Int groundAmbushCount = new Vector2Int(1, 3);
    [Tooltip("How far ahead of the player they erupt.")]
    public float groundAmbushMinDist = 6f;
    public float groundAmbushMaxDist = 12f;
    [Tooltip("Only ambush while the player is actually moving faster than this (m/s).")]
    public float groundAmbushMinPlayerSpeed = 2.5f;
    private float nextGroundAmbush = -1f;
    private Vector3 _prevPlayerPos;
    private Vector3 _playerMoveDir;
    private bool _isRegionMission;

    private enum Phase { Relax, Buildup, Peak }
    private Phase phase = Phase.Relax;
    private float phaseTimer = 0f;
    private float phaseDuration = 8f;
    private int peakIndex = 0;

    private float timer;
    private WorldGenerator worldGen;
    private readonly List<GameObject> availableEnemiesCache = new List<GameObject>(16);

    private void Start()
    {
        IsSpawningBlocked = false;
        worldGen = FindFirstObjectByType<WorldGenerator>();
        BeginPhase(Phase.Relax);

        // Ground ambush is a story-region flavour beat only.
        _isRegionMission = PlayerPrefs.GetInt("IsRegionMission", 0) == 1
            || (GameManager.Instance != null && GameManager.Instance.currentRegion != null)
            || MissionInitializer.PendingMissionRegion != null;

        // Regions felt empty — the guards who "defend" it were rarely seen. On a
        // region mission, push the density up at runtime (overrides the serialized
        // pacing so it works regardless of the prefab values). Max/Min keep any
        // deliberately-higher designer setting.
        if (_isRegionMission)
        {
            maxEnemiesOnMap = Mathf.Max(maxEnemiesOnMap, 55);
            startCap        = Mathf.Max(startCap, 18);
            capRampMinutes  = Mathf.Min(capRampMinutes, 5f);   // fills up faster
            gracePeriod     = Mathf.Min(gracePeriod, 18f);     // shorter empty opening
            relaxCapFactor  = Mathf.Max(relaxCapFactor, 0.6f); // RELAX still populated
            relaxIntervalMult = Mathf.Min(relaxIntervalMult, 3f); // relax not a ghost town
        }
    }

    // Recheck every 2 s whether the "blocked" state is still legitimate.
    // Self-healing: if IsSpawningBlocked is true but no totem is currently
    // mid-activation (all totems purified or none activating), the block is
    // assumed stale (a coroutine forgot to reset it) and released.
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
                if (!IsAnyTotemActivating()) IsSpawningBlocked = false;
            }
            if (IsSpawningBlocked) return;
        }
        else unblockCheckTimer = 0f;

        if (worldGen != null && !WorldGenerator.IsGenerationDone) return;

        if (player == null)
        {
            GameObject pObj = GameObject.FindGameObjectWithTag("Player");
            if (pObj != null) player = pObj.transform;
            else return;
        }

        if (enemyPool == null || enemyPool.Length == 0) return;

        // Track player movement direction/speed for the ground ambush.
        float dt = Time.deltaTime;
        if (dt > 0f)
        {
            Vector3 delta = player.position - _prevPlayerPos;
            delta.y = 0f;
            _playerMoveDir = delta.sqrMagnitude > 0.0001f ? delta.normalized : _playerMoveDir;
            float speed = delta.magnitude / dt;
            _prevPlayerPos = player.position;
            TickGroundAmbush(speed);
        }

        float minutes = GameManager.survivalTime / 60f;

        // Advance the intensity cycle. The opening grace period is held as one
        // long RELAX so the player can settle in and farm before real pressure.
        if (useDirector)
        {
            phaseTimer += Time.deltaTime;
            if (GameManager.survivalTime < gracePeriod)
            {
                if (phase != Phase.Relax) BeginPhase(Phase.Relax);
            }
            else if (phaseTimer >= phaseDuration)
            {
                AdvancePhase();
            }
        }

        int cap = CurrentCap();
        if (EnemyAI.ActiveEnemiesCount >= cap) return;

        // Steady interval still tightens slowly over the run (gentler ramp).
        float baseInterval = Mathf.Max(0.4f, baseSpawnInterval / (1f + minutes * 0.12f));
        float intervalMult = useDirector ? PhaseIntervalMult() : 1f;
        float currentSpawnInterval = baseInterval * intervalMult;

        timer += Time.deltaTime;
        if (timer >= currentSpawnInterval)
        {
            timer = 0f;

            bool peak = useDirector && phase == Phase.Peak;
            bool horde = peak && hordeEveryNPeaks > 0 && (peakIndex % hordeEveryNPeaks == 0);

            // Peaks send the occasional directional PACK rushing in from one
            // arc instead of scattered singles.
            if (peak && (horde || Random.value < packChance))
                SpawnCluster(minutes, horde, cap);
            else
                SpawnEnemy(minutes);
        }
    }

    private void BeginPhase(Phase p)
    {
        phase = p;
        phaseTimer = 0f;
        switch (p)
        {
            case Phase.Relax:   phaseDuration = Random.Range(relaxDuration.x, relaxDuration.y); break;
            case Phase.Buildup: phaseDuration = Random.Range(buildupDuration.x, buildupDuration.y); break;
            case Phase.Peak:    phaseDuration = Random.Range(peakDuration.x, peakDuration.y); peakIndex++; break;
        }

        // Drive the music's Intensity parameter with the phase so the score
        // swells into the peak and eases in the farming window (no-op if the
        // track has no such FMOD parameter).
        if (AudioManager.Instance != null)
            AudioManager.Instance.SetMusicIntensity(p == Phase.Peak ? 1f : (p == Phase.Buildup ? 0.55f : 0.2f));
    }

    private void AdvancePhase()
    {
        switch (phase)
        {
            case Phase.Relax:   BeginPhase(Phase.Buildup); break;
            case Phase.Buildup: BeginPhase(Phase.Peak);    break;
            default:            BeginPhase(Phase.Relax);   break;
        }
    }

    private float PhaseIntervalMult()
    {
        switch (phase)
        {
            case Phase.Buildup: return buildupIntervalMult;
            case Phase.Peak:    return peakIntervalMult;
            default:            return relaxIntervalMult;
        }
    }

    // On-map cap grows from startCap to maxEnemiesOnMap over capRampMinutes,
    // and is trimmed during RELAX so the field drains for a clean farm window.
    private int CurrentCap()
    {
        float minutes = GameManager.survivalTime / 60f;
        float t = capRampMinutes > 0f ? Mathf.Clamp01(minutes / capRampMinutes) : 1f;
        int cap = Mathf.RoundToInt(Mathf.Lerp(startCap, maxEnemiesOnMap, t));
        if (useDirector && phase == Phase.Relax) cap = Mathf.RoundToInt(cap * relaxCapFactor);
        return Mathf.Max(1, cap);
    }

    // Any totem in the scene that is activated but not yet purified?
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

    // A pack that rushes in from ONE direction — a deliberate wave, not
    // scattered singles. Respects the current cap.
    private void SpawnCluster(float minutesSurvived, bool horde, int cap)
    {
        int count = Random.Range(packSize.x, packSize.y + 1);
        if (horde) count = Mathf.RoundToInt(count * 1.6f);

        float baseAngle = Random.Range(0f, Mathf.PI * 2f);
        for (int i = 0; i < count; i++)
        {
            if (EnemyAI.ActiveEnemiesCount >= cap) break;
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

    private float GroundY(Vector3 p)
    {
        if (Terrain.activeTerrain != null)
            return Terrain.activeTerrain.SampleHeight(p) + Terrain.activeTerrain.transform.position.y;
        return p.y;
    }

    // Story-region flavour: skeletons claw up from the ground a short way AHEAD
    // of the running player. Uses each enemy's own emerge animation + dust VFX
    // (we don't cinematic-freeze them, unlike the ambient spawner), so it reads
    // as a scripted "they were waiting under the dirt" scare.
    private void TickGroundAmbush(float playerSpeed)
    {
        if (!enableGroundAmbush || !_isRegionMission) return;
        if (nextGroundAmbush < 0f) { nextGroundAmbush = Time.time + Random.Range(groundAmbushInterval.x, groundAmbushInterval.y); return; }
        if (Time.time < nextGroundAmbush) return;

        nextGroundAmbush = Time.time + Random.Range(groundAmbushInterval.x, groundAmbushInterval.y);

        // Only spring the trap when the player is actually on the move and the
        // field isn't already at cap.
        if (playerSpeed < groundAmbushMinPlayerSpeed) return;
        if (Random.value > groundAmbushChance) return;
        int cap = CurrentCap();
        if (EnemyAI.ActiveEnemiesCount >= cap) return;

        float minutes = GameManager.survivalTime / 60f;
        int n = Random.Range(groundAmbushCount.x, groundAmbushCount.y + 1);
        Vector3 fwd = _playerMoveDir.sqrMagnitude > 0.01f ? _playerMoveDir
                     : Quaternion.Euler(0f, Random.Range(0f, 360f), 0f) * Vector3.forward;

        Vector3 clusterCenter = player.position + fwd.normalized * Random.Range(groundAmbushMinDist, groundAmbushMaxDist);
        for (int i = 0; i < n; i++)
        {
            if (EnemyAI.ActiveEnemiesCount >= cap) break;
            Vector2 jitter = Random.insideUnitCircle * 2.5f;
            Vector3 pos = clusterCenter + new Vector3(jitter.x, 0f, jitter.y);
            pos.y = GroundY(pos);
            SpawnAmbushSkeleton(pos, minutes);
        }

        if (AudioManager.Instance != null) AudioManager.Instance.PlaySFX3D(AudioID.Enemy_Spawn, clusterCenter);
    }

    // Spawn one ambush skeleton bursting up from `spawnPos`: play the enemy's
    // dust/emerge VFX at the ground, then run the rise. Done explicitly (rather
    // than relying on EnemyAI's own emerge) because pooled reuse doesn't re-run
    // that path — this way the burst reads the same for fresh and pooled enemies.
    private void SpawnAmbushSkeleton(Vector3 spawnPos, float minutesSurvived)
    {
        availableEnemiesCache.Clear();
        for (int i = 0; i < enemyPool.Length; i++)
        {
            SpawnableEnemy se = enemyPool[i];
            if (minutesSurvived >= se.spawnAtMinute) availableEnemiesCache.Add(se.enemyPrefab);
        }
        if (availableEnemiesCache.Count == 0) return;

        GameObject prefab = availableEnemiesCache[Random.Range(0, availableEnemiesCache.Count)];
        GameObject e = ObjectPoolManager.Instance != null
            ? ObjectPoolManager.Instance.SpawnFromPool(prefab, spawnPos, Quaternion.identity)
            : Instantiate(prefab, spawnPos, Quaternion.identity);
        if (e == null) return;

        EnemyAI ai = e.GetComponent<EnemyAI>();
        if (ai != null)
        {
            ai.maxHealth *= (1f + minutesSurvived * 0.4f);
            ai.damage *= (1f + minutesSurvived * 0.15f);
            ai.moveSpeed *= Mathf.Min(1.5f, 1f + minutesSurvived * 0.05f);
            ai.xpRewardMultiplier = 1f + (minutesSurvived * 0.2f);

            // Dust burst at the emerge point.
            if (ai.spawnVFXPrefab != null)
            {
                GameObject vfx = ObjectPoolManager.Instance != null
                    ? ObjectPoolManager.Instance.SpawnFromPool(ai.spawnVFXPrefab, spawnPos, ai.spawnVFXPrefab.transform.rotation)
                    : null;
                if (vfx == null) Instantiate(ai.spawnVFXPrefab, spawnPos, ai.spawnVFXPrefab.transform.rotation);
            }
        }

        StartCoroutine(RiseFromGroundRoutine(e));
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

        // Rise-from-ground spawn cue, played 3D so it distance-attenuates and
        // distant spawns don't clutter the mix.
        if (AudioManager.Instance != null) AudioManager.Instance.PlaySFX3D(AudioID.Enemy_Spawn, spawnPos);

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
