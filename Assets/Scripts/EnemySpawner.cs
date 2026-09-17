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

    // How hard the radial "ambient" spawner is allowed to push, 0..1. Owned by
    // WorldEncounterDirector; 1 is normal, 0 is off.
    //
    // This is a SEPARATE knob from IsSpawningBlocked, deliberately. That flag
    // SELF-HEALS: every two seconds the spawner asks whether a totem is still
    // mid-fight, and if not it assumes the flag is stale and clears it. That is
    // right for the totem's own block — a coroutine dying mid-wave must not
    // silence the map for the rest of the run — and completely wrong for the
    // encounter director's, which switched the radial spawner off for the whole
    // mission and had it switched back on two seconds later. The region then ran
    // the hand-placed patrols AND the radial horde at once, which is why it
    // stopped giving the player any rest.
    //
    // A throttle rather than an on/off switch because a region with the radial
    // spawner fully off reads as deserted in the gaps between patrols. Turning
    // it down keeps the world inhabited and gives the encounters room to land.
    public static float AmbientThrottle = 1f;

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

    // ==== A SURVIVAL CURVE WAS RUNNING A LIBERATION ====
    //
    // Every ambient enemy was scaled by +40% health and +15% damage PER MINUTE
    // of the run clock, with no ceiling and no reference to which region the
    // player was in. That curve belongs to the survival level, where the whole
    // point is that the tenth minute is not the first one.
    //
    // A region liberation is not a timed survival: it is explored. Twelve
    // minutes of walking to a totem is ordinary, and it produced skeletons with
    // FIVE TIMES the health hitting for two and a half times the damage - a
    // 17-damage trash mob landing 42. The region's own difficulty, meanwhile,
    // was not consulted at all, so the twenty-fourth region's ambient guards
    // were as weak as the first's at minute zero and as absurd as each other by
    // minute ten. The clock, not the content, was the difficulty.
    //
    // On a region mission the enemies now take their strength from the REGION
    // (its authored hp/damage multipliers and the player's power against its
    // recommended power) with only a small, capped drift for time. Survival
    // keeps the curve that was written for it.
    private float _regionHpMult = 1f;
    private float _regionDmgMult = 1f;

    // Capped so a long region never becomes a different game. Health is allowed
    // more room than damage: a tougher enemy is a longer fight, a harder-hitting
    // one is a shorter life.
    private const float REGION_TIME_HP_PER_MIN = 0.05f;
    private const float REGION_TIME_HP_CAP = 1.6f;
    private const float REGION_TIME_DMG_PER_MIN = 0.02f;
    private const float REGION_TIME_DMG_CAP = 1.24f;

    // True once a region was actually found, so a GameManager that comes up
    // after this spawner does not leave every enemy on the neutral multiplier
    // for the whole run.
    private bool _regionScalingResolved;

    private void ResolveRegionScaling()
    {
        _regionHpMult = 1f;
        _regionDmgMult = 1f;
        if (!_isRegionMission) { _regionScalingResolved = true; return; }

        RegionData region = GameManager.Instance != null ? GameManager.Instance.currentRegion : null;
        if (region == null) region = MissionInitializer.PendingMissionRegion;
        if (region == null) return;
        _regionScalingResolved = true;

        int playerPower = PowerSystemManager.Instance != null
                        ? PowerSystemManager.Instance.CalculatePlayerPower() : 100;
        float diff = PowerSystemManager.CalculateDifficultyMultiplier(playerPower, region.recommendedPower);

        _regionHpMult = region.enemyHpMultiplier * diff;
        _regionDmgMult = region.enemyDamageMultiplier * diff;
    }

    // The one place ambient enemy strength is decided, so the two spawn paths
    // cannot drift apart again.
    private void ApplyScaling(EnemyAI ai, float minutesSurvived)
    {
        if (ai == null) return;
        if (!_regionScalingResolved) ResolveRegionScaling();

        if (_isRegionMission)
        {
            float hpTime = Mathf.Min(1f + minutesSurvived * REGION_TIME_HP_PER_MIN, REGION_TIME_HP_CAP);
            float dmgTime = Mathf.Min(1f + minutesSurvived * REGION_TIME_DMG_PER_MIN, REGION_TIME_DMG_CAP);
            ai.maxHealth *= _regionHpMult * hpTime;
            ai.damage *= _regionDmgMult * dmgTime;
        }
        else
        {
            ai.maxHealth *= (1f + minutesSurvived * 0.4f);
            ai.damage *= (1f + minutesSurvived * 0.15f);
        }

        ai.moveSpeed *= Mathf.Min(1.5f, 1f + minutesSurvived * 0.05f);
        ai.xpRewardMultiplier = 1f + (minutesSurvived * 0.2f);
    }

    private void Start()
    {
        IsSpawningBlocked = false;
        // Statics outlive a scene load, so a throttle set for last run's region
        // would otherwise quietly follow the player into the next level. The
        // director only sets it after a yield, so it always wins over this.
        AmbientThrottle = 1f;
        worldGen = FindFirstObjectByType<WorldGenerator>();
        BeginPhase(Phase.Relax);

        // Ground ambush is a story-region flavour beat only.
        _isRegionMission = PlayerPrefs.GetInt("IsRegionMission", 0) == 1
            || (GameManager.Instance != null && GameManager.Instance.currentRegion != null)
            || MissionInitializer.PendingMissionRegion != null;

        ResolveRegionScaling();

        // Regions felt empty — the guards who "defend" it were rarely seen. On a
        // region mission, push the density up at runtime (overrides the serialized
        // pacing so it works regardless of the prefab values). Max/Min keep any
        // deliberately-higher designer setting.
        if (_isRegionMission)
        {
            // Defended, not relentless. These were pushed hard when regions read
            // as deserted, but that was before the encounter director started
            // placing patrols and camps as well. With both running the player
            // never got a quiet moment, and a fight with no gaps in it stops
            // registering as a fight at all. Note these are the numbers BEFORE
            // AmbientThrottle, which the director then scales down again.
            // Lowered from 38/12. These were set when the region's only enemies
            // were the ambient ones; the encounter director now places patrols,
            // camps and towers as well, and the two together left the player
            // with nothing but contact. The director's own count came down at
            // the same time — neither number means anything on its own.
            maxEnemiesOnMap = Mathf.Max(maxEnemiesOnMap, 28);
            startCap        = Mathf.Max(startCap, 9);
            capRampMinutes  = Mathf.Min(capRampMinutes, 7f);
            gracePeriod     = Mathf.Min(gracePeriod, 24f);
            relaxCapFactor  = Mathf.Max(relaxCapFactor, 0.45f); // RELAX is meant to BE a rest
            relaxIntervalMult = Mathf.Min(relaxIntervalMult, 4f);

            // THE FIRST REGION IS A LESSON, NOT A TEST.
            //
            // Keyed on having conquered nothing yet rather than on a region ID,
            // so it is true for whichever region the player actually opens with.
            // Someone who has never fought one of these needs room to find out
            // what the enemies do, where the totem is and what the anchors are
            // for — and none of that is learnable while being swarmed. It goes
            // away permanently the moment they win one.
            if (PlayerPrefs.GetInt("TotalConqueredRegions", 0) == 0)
            {
                maxEnemiesOnMap = Mathf.RoundToInt(maxEnemiesOnMap * 0.6f);
                startCap        = Mathf.RoundToInt(startCap * 0.55f);
                gracePeriod     = Mathf.Max(gracePeriod, 40f);
                relaxCapFactor  = Mathf.Min(relaxCapFactor, 0.35f);
                relaxDuration   = new Vector2(relaxDuration.x * 1.5f, relaxDuration.y * 1.5f);
                peakDuration    = new Vector2(peakDuration.x * 0.7f, peakDuration.y * 0.7f);
                enableGroundAmbush = false;   // no ambushes on a first outing
                Debug.Log("[Spawner] First region — density eased while the player learns the format.");
            }
        }
    }

    // Recheck every 2 s whether the "blocked" state is still legitimate.
    // Self-healing: if IsSpawningBlocked is true but no totem is currently
    // mid-activation (all totems purified or none activating), the block is
    // assumed stale (a coroutine forgot to reset it) and released.
    private float unblockCheckTimer = 0f;

    private void Update()
    {
        if (AmbientThrottle <= 0.001f)
        {
            timer = 0f;
            return;
        }

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
        float currentSpawnInterval = baseInterval * intervalMult / Mathf.Clamp(AmbientThrottle, 0.05f, 1f);

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
        // The throttle has to bite on the CAP, not only on the spawn interval.
        // Interval alone just slows down how fast the map fills; the map still
        // ends up equally full, which is what the player actually feels.
        cap = Mathf.RoundToInt(cap * Mathf.Clamp01(AmbientThrottle));
        return Mathf.Max(1, cap);
    }

    // Is a totem being captured right now? Both halves of a capture count — the
    // anchor pre-gate as well as the purify wave — which is why this defers to
    // the totem rather than reading `isActivated` here (that field is still
    // false during the pre-gate, so the old check said "no" through the whole
    // anchor fight and let the ambient horde pile onto it).
    private static bool IsAnyTotemActivating() => RegionTotem.AnyCaptureFightRunning;

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
        // Ambushes are ambient pressure too, so they stretch out with the same
        // throttle. Leaving them at full rate would just move the crowding from
        // the spawn ring to the ground under the player's feet.
        float ambushGap = 1f / Mathf.Clamp(AmbientThrottle, 0.05f, 1f);
        if (nextGroundAmbush < 0f) { nextGroundAmbush = Time.time + Random.Range(groundAmbushInterval.x, groundAmbushInterval.y) * ambushGap; return; }
        if (Time.time < nextGroundAmbush) return;

        nextGroundAmbush = Time.time + Random.Range(groundAmbushInterval.x, groundAmbushInterval.y) * ambushGap;

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
            ApplyScaling(ai, minutesSurvived);

            // Dust burst at the emerge point.
            if (ai.spawnVFXPrefab != null)
            {
                GameObject vfx = ObjectPoolManager.Instance != null
                    ? ObjectPoolManager.Instance.SpawnFromPool(ai.spawnVFXPrefab, spawnPos, ai.spawnVFXPrefab.transform.rotation)
                    : null;
                if (vfx == null) Instantiate(ai.spawnVFXPrefab, spawnPos, ai.spawnVFXPrefab.transform.rotation);
            }
        }

        StartRise(e);
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
        ApplyScaling(enemyScript, minutesSurvived);

        // Rise-from-ground spawn cue, played 3D so it distance-attenuates and
        // distant spawns don't clutter the mix.
        if (AudioManager.Instance != null) AudioManager.Instance.PlaySFX3D(AudioID.Enemy_Spawn, spawnPos);

        StartRise(newEnemy);
    }

    // ==== THE RISE BELONGS TO THE ENEMY, NOT TO THE SPAWNER ====
    //
    // This used to be a coroutine on the spawner that set the enemy's
    // isCinematicFrozen, lerped its transform for 1.5s, and cleared the flag
    // only if the object was still active at the end. A coroutine on the
    // spawner does not stop when the enemy is pooled, so that clear was skipped
    // whenever an enemy died or was culled mid-rise, and the flag rode back out
    // of the pool on the next spawn — where UpdateBehavior's first line returns
    // on it. A quick pool turnaround could also leave two of them alive for one
    // GameObject, each dragging the body toward a different target.
    //
    // EnemyAI.PlayRiseFromGround owns it now: it dies with the object, its
    // finally always lowers the flag, and it refuses to start twice. See the
    // note there.
    private void StartRise(GameObject enemy)
    {
        if (enemy == null) return;
        var ai = enemy.GetComponent<EnemyAI>();
        if (ai == null) return;

        // ==== COME IN THROUGH THE SAME DOOR THE GUARDS USE ====
        //
        // The one group of enemies that has never had an animation problem is
        // the reliquary guardians, and the thing that makes them different is
        // not their prefab — it is the same six prefabs — it is HOW they enter
        // the world. A guard is startPassive, so it runs UpdatePassiveBehavior,
        // spots the player, and goes through Aggro(). Aggro() is what sets
        // isAggroed, which is what SetGait reads, which is what decides the
        // clip in the run state.
        //
        // A spawner enemy had startPassive false on the prefab, so it fell
        // straight into the chase with isAggroed false and only turned it on
        // later, from somewhere else — and every one of those late flips is a
        // clip swap through the AnimatorOverrideController, which rebuilds the
        // controller mid-stride.
        //
        // These spawn within the player's aggro range anyway, so passive lasts
        // a frame or two at most. It is the ORDER that matters, not the delay:
        // the enemy is standing still while it rises, notices the player, and
        // starts running — one gait change, at the moment the animation is
        // already changing.
        ai.startPassive = true;
        ai.roamWhilePassive = false;   // it is rising out of the ground, not strolling
        ai.anchorPoint = enemy.transform.position;
        ai.anchorTransform = null;
        // Passive has to END, immediately. These spawn between minSpawnRadius
        // and maxSpawnRadius, and the default aggro range is shorter than the
        // far end of that — so without this an enemy that came up at twenty
        // metres would stand at its post until the player happened to walk two
        // metres closer, which is not a horde. The point of the passive step is
        // the ORDER it puts the gait change in, not any delay.
        ai.aggroRange = Mathf.Max(ai.aggroRange, maxSpawnRadius + 10f);
        ai.CapturePost();

        ai.PlayRiseFromGround();
    }
}
