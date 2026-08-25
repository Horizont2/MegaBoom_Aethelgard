using UnityEngine;
using UnityEngine.UI;
using System.Collections;

public class EnemyAI : MonoBehaviour, IDamageable
{
    [Header("Archetype & Poise")]
    public bool isElite = false;
    public float maxPoise = 100f;
    private float currentPoise;

    [Header("Base Enemy Stats (Level 1)")]
    public float maxHealth = 20f;
    public float moveSpeed = 4f;
    public float damage = 10f;

    [Header("Night Buff Settings")]
    public float nightMultiplier = 1.25f;

    [Header("Cinematic Settings")]
    public bool isCinematicFrozen = false;

    [Header("Spawn Settings")]
    public GameObject spawnVFXPrefab;
    public float spawnDuration = 1.5f;

    [Header("Combat Settings")]
    public float attackRange = 1.6f;
    public float attackCooldown = 1.5f;
    public float attackTelegraphTime = 0.5f;
    public GameObject weaponGlintVFX;

    [Header("Ranged (Archer)")]
    [Tooltip("Turns this enemy into a kiting archer: it holds distance and fires projectiles instead of meleeing. Uses the same animator params (Attack trigger fires the shot).")]
    public bool isRanged = false;
    [Tooltip("Distance the archer tries to hold from the player.")]
    public float preferredRange = 9f;
    [Tooltip("Arrow/projectile prefab — needs an EnemyProjectile component.")]
    public GameObject projectilePrefab;
    public float projectileSpeed = 18f;
    [Tooltip("Gravity applied to the arrow so it flies a real arc (higher = steeper lob).")]
    public float arcGravity = 22f;
    [Range(0f, 1f)]
    [Tooltip("How much the archer leads a moving player (0 = aim where they are, 1 = full prediction). ~0.7 reads as smart but is still dodgeable by dashing / turning.")]
    public float rangedLeadFactor = 0.7f;
    [Tooltip("HP multiplier for archers — they're glass cannons: threatening shots but they die fast, so kiting doesn't make them unkillable.")]
    public float rangedHealthFactor = 0.6f;
    [Tooltip("Optional bow/muzzle transform to fire from. Falls back to chest height + forward.")]
    public Transform projectileSpawnPoint;

    [Header("Summon Ability (optional — e.g. the Necromancer)")]
    [Tooltip("Enable to give this enemy the reusable minion-summon ability. Assign at least one minion prefab below.")]
    public bool canSummon = false;
    public GameObject[] summonMinionPrefabs;
    public int summonMinCount = 2;
    public int summonMaxCount = 4;
    [Tooltip("Cap on simultaneously-alive summons from this caster.")]
    public int summonMaxActive = 6;
    [Tooltip("When true this caster won't summon again until all its previous minions are dead (e.g. the Skeleton Mage).")]
    public bool summonRequireAllDead = false;
    public float summonCooldown = 12f;
    [Tooltip("Delay between the cast animation and the minions rising, so the summon reads.")]
    public float summonWindup = 0.8f;
    [Tooltip("Player distance under which the caster will summon.")]
    public float summonAggroRange = 28f;
    public float summonSpawnRadius = 3.5f;
    [Tooltip("Animator trigger for the cast. 'Attack' always exists; use a dedicated 'Cast' if the model has one.")]
    public string summonAnimTrigger = "Attack";
    public GameObject summonCastVFX;
    public GameObject summonMinionSpawnVFX;

    [Header("Drops & Economy")]
    public GameObject xpCrystalPrefab;
    public GameObject diamondPrefab;
    [Range(0f, 1f)] public float diamondDropChance = 0.1f;
    public GameObject damagePopupPrefab;

    [Header("Targeting & Swarm")]
    public Transform target;
    public float verticalOffset = 0.0f;
    public float repulsionRadius = 1.5f;
    public float repulsionForce = 4f;

    [Header("Juice VFX")]
    public GameObject deathVFXPrefab;
    public ParticleSystem dissolveAshVFX;

    [Header("UI Settings")]
    public GameObject healthCanvas; // Об'єкт Канвасу (має бути вимкнений за замовчуванням)
    public Image healthFill;        // Зображення смужки ХП (Image Type = Filled)
    private float targetHealthRatio = 1f;

    [HideInInspector] public float xpRewardMultiplier = 1f;

    public bool isInvincible = false;
    private bool isEnraged = false;
    private bool isSpawning = false;

    private float currentHealth;
    private float actualMoveSpeed;
    private float randomOffset;
    private float strafeDir;

    private float baseActualMoveSpeed;
    private float baseDamage;
    private bool isNightBuffActive = false;
    private DayNightCycle dayNightCycle;

    private MeshRenderer[] meshRenderers;
    private Color[] originalColors;
    private MaterialPropertyBlock s_mpb;
    private static readonly int s_baseColorID = Shader.PropertyToID("_BaseColor");
    private static readonly int s_colorID = Shader.PropertyToID("_Color");

    private int updateSkipCounter = 0;
    private PlayerController playerTarget;
    // When a friendly ally is nearer than the player, the enemy fights IT instead.
    private IDamageable currentTargetDamageable;   // who ExecuteAttackDamage actually hits
    private AllyAI allyTarget;
    private float nextAllyScanTime;
    private Animator animator;
    private bool isDead = false;
    // ONE tracked handle for the enemy's current combat vocal — the
    // aggro roar OR the attack-telegraph growl. Both are long-ish clips
    // that used to keep ringing off the corpse when the enemy was
    // killed mid-sound. Playing a new vocal stops the previous one, so
    // only a single growl is ever live per enemy; Die() + OnDisable
    // fade it out. -1 = nothing playing.
    private int vocalSfxHandle = -1;
    // Dedupe cooldown for Enemy_Attack — prevents the swing sound from
    // firing twice on the same frame when animator events and the
    // aggro coroutine both trigger it.
    private float lastAttackSfxTime = -999f;
    private const float ATTACK_SFX_COOLDOWN = 0.15f;

    private Vector3 knockbackVelocity = Vector3.zero;
    private float stunTimer = 0f;
    private float lastAttackTime;
    private bool isPreparingAttack = false;
    private Transform mainCamTransform;

    private static Transform s_player;
    private static PlayerController s_playerController;
    private static Terrain s_terrain;
    private static float s_terrainOriginY;
    private static readonly Collider[] s_overlapBuffer = new Collider[32];

    private CharacterController cc;
    private float verticalVelocity = 0f; // Для гравітації

    public static int ActiveEnemiesCount = 0;

    private static bool TryGetPlayer(out Transform t, out PlayerController pc)
    {
        if (s_player != null && s_playerController != null) { t = s_player; pc = s_playerController; return true; }
        GameObject p = GameObject.FindGameObjectWithTag("Player");
        if (p != null)
        {
            s_player = p.transform;
            s_playerController = p.GetComponent<PlayerController>();
            t = s_player; pc = s_playerController; return true;
        }
        t = null; pc = null; return false;
    }

    // Nearest living ally companion within `range`, or null. Uses AllyAI's static
    // registry so this is cheap even with many enemies.
    private AllyAI FindNearestAlly(float range)
    {
        var list = AllyAI.Active;
        if (list == null || list.Count == 0) return null;
        AllyAI best = null;
        float bestSqr = range * range;
        Vector3 p = transform.position;
        for (int i = 0; i < list.Count; i++)
        {
            AllyAI a = list[i];
            if (a == null) continue;
            float sq = (a.transform.position - p).sqrMagnitude;
            if (sq < bestSqr) { bestSqr = sq; best = a; }
        }
        return best;
    }

    private static Terrain CachedTerrain
    {
        get
        {
            if (s_terrain == null)
            {
                s_terrain = Terrain.activeTerrain;
                if (s_terrain != null) s_terrainOriginY = s_terrain.transform.position.y;
            }
            return s_terrain;
        }
    }

    // Reused hit buffer for the ground raycast — Physics.RaycastAll used
    // to allocate a fresh array on every call, and each enemy calls this
    // 1-3× per Update tick. With 50 aggro'd enemies that was 150 array
    // allocs/frame plus a per-hit string-ToLower alloc. NonAlloc + layer-
    // mask + direct comparison drops all of it.
    private static readonly RaycastHit[] s_terrainHitBuffer = new RaycastHit[16];
    private static int s_groundLayerMask = -1;

    private static float SampleTerrainHeight(Vector3 worldPos)
    {
        // Resolve the ground layer mask once. Falls back to "everything"
        // if none of the expected layers exist in the project.
        if (s_groundLayerMask == -1)
        {
            int m = 0;
            int terrainLayer = LayerMask.NameToLayer("Terrain");
            int groundLayer = LayerMask.NameToLayer("Ground");
            int defaultLayer = LayerMask.NameToLayer("Default");
            if (terrainLayer >= 0) m |= 1 << terrainLayer;
            if (groundLayer >= 0) m |= 1 << groundLayer;
            if (defaultLayer >= 0) m |= 1 << defaultLayer;
            s_groundLayerMask = m == 0 ? ~0 : m;
        }

        Vector3 origin = new Vector3(worldPos.x, worldPos.y + 50f, worldPos.z);
        int count = Physics.RaycastNonAlloc(origin, Vector3.down, s_terrainHitBuffer, 150f,
                                            s_groundLayerMask, QueryTriggerInteraction.Ignore);
        float bestY = -9999f;
        bool found = false;
        for (int i = 0; i < count; i++)
        {
            var hit = s_terrainHitBuffer[i];
            if (hit.collider.isTrigger) continue;
            // If the ray hit an actual Terrain we take it immediately —
            // the Terrain layer is the authoritative ground surface, no
            // need to compare names.
            if (hit.collider is TerrainCollider)
            {
                return hit.point.y;
            }
            if (hit.point.y > bestY) { bestY = hit.point.y; found = true; }
        }
        if (found) return bestY;
        // Raycast missed every ground collider (spawn over a gap, collider
        // momentarily disabled during generation, or ground over 150m below the
        // origin). Fall back to the terrain's authoritative sampled height rather
        // than worldPos.y — returning the enemy's own (possibly airborne) Y was
        // what made skeletons + their emerge VFX appear floating in the air.
        Terrain terr = Terrain.activeTerrain;
        if (terr != null)
            return terr.SampleHeight(worldPos) + terr.transform.position.y;
        return worldPos.y;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStaticsOnDomainReload()
    {
        s_player = null;
        s_playerController = null;
        s_terrain = null;
        s_terrainOriginY = 0f;
    }

    private void OnEnable()
    {
        ActiveEnemiesCount++;
        UnityEngine.SceneManagement.SceneManager.activeSceneChanged += OnActiveSceneChanged;
    }

    private void OnDisable()
    {
        ActiveEnemiesCount--;
        UnityEngine.SceneManagement.SceneManager.activeSceneChanged -= OnActiveSceneChanged;
        // Kill any live combat vocal when the enemy is pooled / despawned
        // outside of Die() — same principle as the corpse fadeout.
        StopVocal(0.1f);
    }

    private static void OnActiveSceneChanged(UnityEngine.SceneManagement.Scene a, UnityEngine.SceneManagement.Scene b)
    {
        s_player = null;
        s_playerController = null;
        s_terrain = null;
        s_terrainOriginY = 0f;
    }

    [HideInInspector] public bool startPassive = false;
    [HideInInspector] public Vector3 anchorPoint;
    [HideInInspector] public Transform anchorTransform;
    [HideInInspector] public float roamRadius = 3.5f;
    [HideInInspector] public float aggroRange = 14f;
    [HideInInspector] public EnemyEncounterGroup parentGroup;
    [HideInInspector] public bool roamWhilePassive = true;
    [HideInInspector] public bool faceAnchorWhenIdle = false;
    private bool isAggroed = false;
    private Vector3 currentRoamTarget;
    private float nextRoamPickTime;
    private float passiveAggroCheckTimer;

    // Footstep bookkeeping — track distance covered along XZ so we can
    // trigger step SFX at a believable stride, gated by hearing range.
    private Vector3 footstepLastPos;
    private float footstepDistanceAccum;
    private const float FOOTSTEP_STRIDE = 1.6f;
    private const float FOOTSTEP_HEARING_RANGE_SQR = 30f * 30f;

    public bool IsAggroed => isAggroed;

    private void Awake()
    {
        cc = GetComponent<CharacterController>();

        gameObject.layer = 9;
        int minimapLayer = LayerMask.NameToLayer("MinimapOnly");

        meshRenderers = GetComponentsInChildren<MeshRenderer>();
        originalColors = new Color[meshRenderers.Length];
        s_mpb = new MaterialPropertyBlock();
        for (int i = 0; i < meshRenderers.Length; i++)
        {
            if (meshRenderers[i].gameObject.layer != minimapLayer) meshRenderers[i].gameObject.layer = 9;
            originalColors[i] = meshRenderers[i].sharedMaterial != null
                ? meshRenderers[i].sharedMaterial.color
                : Color.white;
            meshRenderers[i].shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }
        SkinnedMeshRenderer[] skinned = GetComponentsInChildren<SkinnedMeshRenderer>();
        for (int i = 0; i < skinned.Length; i++)
        {
            if (skinned[i] != null) skinned[i].shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        Rigidbody rb = GetComponent<Rigidbody>();
        if (rb == null) rb = gameObject.AddComponent<Rigidbody>();
        rb.isKinematic = true;
        rb.useGravity = false;

        animator = GetComponentInChildren<Animator>();
        if (animator != null)
        {
            animator.applyRootMotion = false;
            animator.cullingMode = AnimatorCullingMode.CullCompletely;
            // Tell the SHARED enemy Animator whether this instance is an archer,
            // so the aiming state's transitions (gated on IsRanged) only fire for
            // ranged enemies. Guarded so a controller without the param stays quiet.
            SetAnimBoolSafe("IsRanged", isRanged);
        }

        randomOffset = Random.Range(0f, 100f);
        strafeDir = Random.value > 0.5f ? 1f : -1f;
    }

    private void Start()
    {
        mainCamTransform = CameraCache.MainTransform;
        dayNightCycle = FindFirstObjectByType<DayNightCycle>();
        actualMoveSpeed = moveSpeed * Random.Range(0.8f, 1.2f);

        float timeMultiplier = PowerSystemManager.CalculateTimeMultiplier(Time.timeSinceLevelLoad);

        if (GameManager.Instance != null && GameManager.Instance.currentRegion != null)
        {
            RegionData region = GameManager.Instance.currentRegion;
            int playerPower = PowerSystemManager.Instance != null
                ? PowerSystemManager.Instance.CalculatePlayerPower()
                : PlayerPrefs.GetInt("PlayerTotalPower", 50);

            float dynamicMultiplier = PowerSystemManager.CalculateDifficultyMultiplier(playerPower, region.recommendedPower);

            maxHealth *= region.enemyHpMultiplier * dynamicMultiplier * timeMultiplier;
            damage *= region.enemyDamageMultiplier * dynamicMultiplier * timeMultiplier;

            // Armor HP + damage reduction are now actually applied to the player
            // (they used to be dead), so the player is markedly tankier — most so
            // in late regions where a full high-tier set is worn. Scale enemy
            // damage up by region depth (enemyHpMultiplier is the depth proxy) so
            // the added durability doesn't trivialise progression. R1 ≈ ×1.0,
            // R24 ≈ ×1.7. Tune ENEMY_DMG_GEAR_COMP if it feels off.
            const float ENEMY_DMG_GEAR_COMP = 0.10f;
            damage *= 1f + Mathf.Max(0f, region.enemyHpMultiplier - 1f) * ENEMY_DMG_GEAR_COMP;

            if (dynamicMultiplier > 1.4f) actualMoveSpeed *= 1.15f;

            // XP scales with REGION difficulty and survival time, floored at
            // 1× so early regions still pay the base crystal (the old
            // ×0.5 quietly halved early-game XP). Deliberately NOT tied to the
            // player-power dynamic multiplier — out-gearing a region shouldn't
            // cut its XP. Preserve any higher value the spawner already set
            // (its steeper per-minute ramp for endless/survival waves).
            float regionXp = Mathf.Max(1f, region.enemyHpMultiplier * 0.6f) * timeMultiplier;
            xpRewardMultiplier = Mathf.Max(xpRewardMultiplier, regionXp);
        }
        else
        {
            maxHealth *= timeMultiplier;
            damage *= timeMultiplier;
        }

        // Archers are glass cannons — cut their HP so kiting can't make them
        // unkillable damage sponges.
        if (isRanged) maxHealth *= Mathf.Clamp(rangedHealthFactor, 0.1f, 1f);

        baseActualMoveSpeed = actualMoveSpeed;
        baseDamage = damage;
        currentHealth = maxHealth;
        currentPoise = maxPoise;

        // Ініціалізація UI ХП ворога при спавні
        if (healthCanvas != null) healthCanvas.SetActive(false);
        if (healthFill != null) healthFill.fillAmount = 1f;
        targetHealthRatio = 1f;

        if (TryGetPlayer(out Transform startT, out PlayerController startPC))
        {
            target = startT;
            playerTarget = startPC;
        }

        lastAttackTime = Time.time - Random.Range(0f, attackCooldown);

        // Give summoner enemies (the Necromancer) their minion-call ability.
        // Reuses one component; guarded so pooled reuse doesn't stack copies.
        if (canSummon && summonMinionPrefabs != null && summonMinionPrefabs.Length > 0)
        {
            var summon = GetComponent<MinionSummonAbility>();
            if (summon == null) summon = gameObject.AddComponent<MinionSummonAbility>();
            summon.Configure(summonMinionPrefabs, summonMinCount, summonMaxCount, summonMaxActive,
                             summonCooldown, summonWindup, summonAggroRange, summonSpawnRadius,
                             summonAnimTrigger, summonCastVFX, summonMinionSpawnVFX,
                             summonRequireAllDead);
        }

        StartCoroutine(SpawnRoutine());
    }

    private IEnumerator SpawnRoutine()
    {
        isSpawning = true;
        if (animator != null) animator.SetBool("isMoving", true);

        Vector3 finalPos = transform.position;
        finalPos.y = SampleTerrainHeight(finalPos) + verticalOffset;

        if (!isCinematicFrozen)
        {
            Vector3 startPos = finalPos - Vector3.up * 2.5f;
            transform.position = startPos;

            if (spawnVFXPrefab != null)
            {
                GameObject vfx = null;
                if (ObjectPoolManager.Instance != null)
                    vfx = ObjectPoolManager.Instance.SpawnFromPool(spawnVFXPrefab, finalPos, spawnVFXPrefab.transform.rotation);

                if (vfx == null)
                    Instantiate(spawnVFXPrefab, finalPos, spawnVFXPrefab.transform.rotation);
            }

            float elapsed = 0f;
            while (elapsed < spawnDuration)
            {
                elapsed += Time.unscaledDeltaTime;
                float t = Mathf.Sin((elapsed / spawnDuration) * Mathf.PI * 0.5f);
                transform.position = Vector3.Lerp(startPos, finalPos, t);
                yield return null;
            }
            transform.position = finalPos;
        }
        else
        {
            yield return new WaitForSecondsRealtime(spawnDuration);
        }

        isSpawning = false;

        if (animator != null) animator.SetBool("isMoving", false);
    }

    private void Update()
    {
        // --- Плавна анімація UI ХП (Lerp) ---
        if (healthCanvas != null && healthCanvas.activeInHierarchy)
        {
            // 1. Фікс зависання смужки ХП ворога
            if (healthFill != null)
            {
                healthFill.fillAmount = Mathf.Lerp(healthFill.fillAmount, targetHealthRatio, Time.deltaTime * 8f);
                if (Mathf.Abs(healthFill.fillAmount - targetHealthRatio) < 0.005f)
                {
                    healthFill.fillAmount = targetHealthRatio;
                }
            }

            // 2. Фікс повороту: Канвас ворога завжди дивиться прямо в камеру.
            // CameraCache re-resolves lazily on scene load, so we don't pay
            // Camera.main's scene-walk each frame.
            if (mainCamTransform == null) mainCamTransform = CameraCache.MainTransform;
            if (mainCamTransform != null)
                healthCanvas.transform.rotation = mainCamTransform.rotation;
        }

        if (target == null && !isDead)
        {
            if (TryGetPlayer(out Transform pt, out PlayerController pc))
            {
                target = pt;
                playerTarget = pc;
            }
            if (target == null) return;
        }

        if (isDead) return;

        float sqrDistToPlayer = (target.position - transform.position).sqrMagnitude;
        int updateInterval;
        if (sqrDistToPlayer > 2500f) updateInterval = 8;
        else if (sqrDistToPlayer > 900f) updateInterval = 4;
        else if (sqrDistToPlayer > 400f) updateInterval = 2;
        else updateInterval = 1;

        if (updateInterval > 1)
        {
            updateSkipCounter++;
            if (updateSkipCounter % updateInterval != 0) return;
        }

        if (playerTarget != null && playerTarget.currentHealth <= 0)
        {
            if (animator != null) animator.SetBool("isMoving", false);
            return;
        }

        // Retarget: fight the player by default, but if a living ally companion is
        // CLOSER, switch to it — so freed captives/mercenaries actually draw and
        // trade blows with enemies instead of being ignored.
        if (Time.time >= nextAllyScanTime)
        {
            nextAllyScanTime = Time.time + 0.3f;
            allyTarget = FindNearestAlly(18f);
        }
        if (allyTarget != null)
        {
            float dPlayer = playerTarget != null ? (playerTarget.transform.position - transform.position).sqrMagnitude : float.MaxValue;
            float dAlly = (allyTarget.transform.position - transform.position).sqrMagnitude;
            if (dAlly < dPlayer) { target = allyTarget.transform; currentTargetDamageable = allyTarget; }
            else { if (playerTarget != null) target = playerTarget.transform; currentTargetDamageable = playerTarget; }
        }
        else
        {
            if (playerTarget != null) target = playerTarget.transform;
            currentTargetDamageable = playerTarget;
        }

        CheckNightBuff();

        if (currentPoise < maxPoise && stunTimer <= 0) currentPoise += Time.deltaTime * 15f;

        if (knockbackVelocity.magnitude > 0.1f)
        {
            transform.position += knockbackVelocity * Time.deltaTime;
            knockbackVelocity = Vector3.Lerp(knockbackVelocity, Vector3.zero, Time.deltaTime * 10f);
        }

        if (isCinematicFrozen || isSpawning)
        {
            if (animator != null && isCinematicFrozen) animator.SetBool("isMoving", true);
            return;
        }

        if (stunTimer > 0 && !isEnraged)
        {
            stunTimer -= Time.deltaTime;
            if (animator != null) animator.SetBool("isMoving", false);
            return;
        }

        if (isPreparingAttack) return;

        if (startPassive && !isAggroed)
        {
            UpdatePassiveBehavior();
            return;
        }

        if (isRanged)
        {
            UpdateRangedBehavior();
            return;
        }

        Vector3 currentPos = transform.position;
        Vector3 directionToPlayer = (target.position - currentPos).normalized;
        Vector3 repulsion = Vector3.zero;

        int neighborCount = Physics.OverlapSphereNonAlloc(currentPos, repulsionRadius, s_overlapBuffer, 1 << 9);
        for (int i = 0; i < neighborCount; i++)
        {
            Collider neighbor = s_overlapBuffer[i];
            if (neighbor.gameObject != gameObject && !neighbor.isTrigger)
            {
                Vector3 pushDir = currentPos - neighbor.transform.position;
                float distance = pushDir.magnitude;
                if (distance < repulsionRadius && distance > 0)
                {
                    repulsion += pushDir.normalized * (repulsionRadius - distance);
                }
            }
        }

        float distanceToPlayer = Vector3.Distance(transform.position, target.position);

        if (distanceToPlayer <= attackRange * 1.5f)
        {
            if (directionToPlayer != Vector3.zero)
            {
                Vector3 lookDir = directionToPlayer; lookDir.y = 0;
                transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(lookDir), 15f * Time.deltaTime);
            }

            bool isAttackReady = Time.time >= lastAttackTime + attackCooldown;

            if (isAttackReady && distanceToPlayer <= attackRange)
            {
                if (animator != null) animator.SetBool("isMoving", false);
                StartCoroutine(AttackRoutine());
            }
            else if (isAttackReady && distanceToPlayer > attackRange)
            {
                if (animator != null) animator.SetBool("isMoving", true);

                Vector3 moveDir = (directionToPlayer + repulsion).normalized;
                Vector3 nextPos = currentPos + moveDir * actualMoveSpeed * Time.deltaTime;

                nextPos.y = SampleTerrainHeight(nextPos) + verticalOffset;
                SetPositionSafe(nextPos);
            }
            else
            {
                if (animator != null) animator.SetBool("isMoving", true);

                Vector3 flankDir = Vector3.Cross(Vector3.up, directionToPlayer) * strafeDir;

                if (distanceToPlayer > attackRange * 0.9f) flankDir += directionToPlayer * 0.4f;
                else if (distanceToPlayer < attackRange * 0.6f) flankDir -= directionToPlayer * 0.5f;

                Vector3 moveDir = (flankDir + repulsion).normalized;
                Vector3 nextPos = currentPos + moveDir * (actualMoveSpeed * 0.7f) * Time.deltaTime;

                nextPos.y = SampleTerrainHeight(nextPos) + verticalOffset;
                SetPositionSafe(nextPos);
            }
        }
        else
        {
            float sway = Mathf.PerlinNoise(Time.time * 0.5f, randomOffset) * 2f - 1f;
            Vector3 rightDir = Vector3.Cross(Vector3.up, directionToPlayer).normalized;
            Vector3 finalDirection = (directionToPlayer + repulsion * repulsionForce + (rightDir * sway * 0.5f)).normalized;
            finalDirection.y = 0f;

            Vector3 nextPos = currentPos + finalDirection * actualMoveSpeed * Time.deltaTime;
            nextPos.y = SampleTerrainHeight(nextPos) + verticalOffset;
            SetPositionSafe(nextPos);

            if (finalDirection != Vector3.zero)
            {
                transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(finalDirection), 10f * Time.deltaTime);
                if (animator != null) animator.SetBool("isMoving", true);
            }
        }

        UpdateFootstepAudio();
    }

    // Track XZ distance travelled between Update ticks and fire a
    // footstep clip once we cross the stride threshold. Gated by
    // squared distance to the player so a crowd of far-away skeletons
    // doesn't burn PlayOneShots the listener can't hear.
    private void UpdateFootstepAudio()
    {
        if (AudioManager.Instance == null) return;
        Vector3 pos = transform.position;
        Vector3 delta = pos - footstepLastPos;
        delta.y = 0f;
        footstepLastPos = pos;
        float dist = delta.magnitude;
        if (dist < 0.001f) return;
        footstepDistanceAccum += dist;
        if (footstepDistanceAccum < FOOTSTEP_STRIDE) return;
        footstepDistanceAccum = 0f;
        if (target != null && (target.position - pos).sqrMagnitude > FOOTSTEP_HEARING_RANGE_SQR) return;
        AudioManager.Instance.PlaySFX3D(AudioID.Enemy_Footstep, transform.position);
    }

    private void UpdatePassiveBehavior()
    {
        Vector3 anchor = anchorTransform != null ? anchorTransform.position : anchorPoint;

        passiveAggroCheckTimer -= Time.deltaTime;
        if (passiveAggroCheckTimer <= 0f && target != null)
        {
            passiveAggroCheckTimer = 0.2f;
            float distSqr = (target.position - transform.position).sqrMagnitude;
            if (distSqr <= aggroRange * aggroRange)
            {
                Aggro();
                if (parentGroup != null) parentGroup.AlertAll();
                return;
            }
        }

        if (roamWhilePassive)
        {
            if (Time.time >= nextRoamPickTime || Vector3.SqrMagnitude(transform.position - currentRoamTarget) < 0.6f)
            {
                Vector2 r = Random.insideUnitCircle * roamRadius;
                currentRoamTarget = anchor + new Vector3(r.x, 0f, r.y);
                currentRoamTarget.y = SampleTerrainHeight(currentRoamTarget) + verticalOffset;
                nextRoamPickTime = Time.time + Random.Range(2.5f, 5f);
            }
        }
        else
        {
            currentRoamTarget = anchor;
        }

        Vector3 toTarget = currentRoamTarget - transform.position;
        toTarget.y = 0f;
        float distXZ = toTarget.magnitude;
        float standThreshold = roamWhilePassive ? 0.2f : 0.6f;

        if (distXZ > standThreshold)
        {
            Vector3 moveDir = toTarget / distXZ;
            float passiveSpeed = actualMoveSpeed * 0.4f;
            Vector3 nextPos = transform.position + moveDir * passiveSpeed * Time.deltaTime;
            nextPos.y = SampleTerrainHeight(nextPos) + verticalOffset;
            SetPositionSafe(nextPos);
            transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(moveDir), 5f * Time.deltaTime);
            SetMovingAnim(true, passiveSpeed);
            UpdateFootstepAudio();
        }
        else
        {
            if (faceAnchorWhenIdle)
            {
                Vector3 lookAt = anchor - transform.position;
                lookAt.y = 0f;
                if (lookAt.sqrMagnitude > 0.01f)
                {
                    Quaternion target = Quaternion.LookRotation(lookAt.normalized);
                    transform.rotation = Quaternion.Slerp(transform.rotation, target, 2f * Time.deltaTime);
                }
            }
            SetMovingAnim(false, 0f);
        }
    }

    private void SetMovingAnim(bool moving, float speed)
    {
        if (animator == null || !animator.enabled) return;
        animator.SetBool("isMoving", moving);
        animator.SetFloat("Speed", moving ? speed : 0f);
    }

    public void Aggro()
    {
        if (isAggroed) return;
        isAggroed = true;

        if (target != null)
        {
            Vector3 look = target.position - transform.position;
            look.y = 0f;
            if (look.sqrMagnitude > 0.01f)
                transform.rotation = Quaternion.LookRotation(look.normalized);
        }

        // Aggro bark — only for the first-agro moment so a horde
        // doesn't chorus at once. 3D so the growl sits at the enemy in
        // space. If the FMOD event has no spatializer authored, add a
        // "3D Panner" preset in FMOD Studio; the position we pass will
        // then attenuate distance and pan by direction.
        PlayVocal(AudioID.Enemy_Agro);
    }

    // Plays a tracked 3D vocal on the enemy, stopping whatever vocal was
    // already playing first — guarantees at most one growl per enemy and
    // gives Die()/OnDisable a single handle to cut. Falls back to a
    // fire-and-forget one-shot if the looping variant is unavailable
    // (missing FMOD event) so the sound still plays; that fallback can't
    // be stopped, but it's the degraded path, not the norm.
    private void PlayVocal(string audioId)
    {
        if (AudioManager.Instance == null) return;
        StopVocal(0.05f);
        vocalSfxHandle = AudioManager.Instance.PlayLoopingSFX3D(audioId, transform);
        if (vocalSfxHandle == -1)
            AudioManager.Instance.PlaySFX3D(audioId, transform.position);
    }

    private void StopVocal(float fadeSeconds)
    {
        if (vocalSfxHandle != -1 && AudioManager.Instance != null)
        {
            AudioManager.Instance.StopLoopingSFX(vocalSfxHandle, fadeSeconds);
            vocalSfxHandle = -1;
        }
    }

    private void CheckNightBuff()
    {
        if (dayNightCycle == null || isEnraged) return;
        bool isNight = dayNightCycle.timeOfDay < 5f || dayNightCycle.timeOfDay > 19f;

        if (isNight && !isNightBuffActive) { isNightBuffActive = true; actualMoveSpeed = baseActualMoveSpeed * nightMultiplier; damage = baseDamage * nightMultiplier; }
        else if (!isNight && isNightBuffActive) { isNightBuffActive = false; actualMoveSpeed = baseActualMoveSpeed; damage = baseDamage; }
    }

    // ─────────────────────────────────────────────────────────────
    //  Ranged / archer behaviour
    // ─────────────────────────────────────────────────────────────
    private void UpdateRangedBehavior()
    {
        Vector3 pos = transform.position;
        Vector3 toPlayer = target.position - pos; toPlayer.y = 0f;
        float dist = toPlayer.magnitude;
        Vector3 dir = dist > 0.001f ? toPlayer / dist : transform.forward;

        // Archers keep aim on the player.
        if (dir != Vector3.zero)
            transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(dir), 12f * Time.deltaTime);

        // Hold the drawn-bow (aim) pose the whole time it's engaged, so the bow
        // stays nocked between shots instead of dropping to idle.
        bool engaged = dist <= preferredRange * 1.5f;
        SetAnimBoolSafe("Aim", engaged);

        // Can fire from point-blank up to max range (no dead min-range), so a
        // cornered archer keeps shooting instead of standing there doing nothing.
        bool ready = Time.time >= lastAttackTime + attackCooldown;
        if (ready && dist <= preferredRange * 1.35f)
        {
            if (animator != null) animator.SetBool("isMoving", false);
            StartCoroutine(RangedAttackRoutine());
            return;
        }

        // Light separation from crowding neighbours (layer 9, same as melee).
        Vector3 repulsion = Vector3.zero;
        int neighborCount = Physics.OverlapSphereNonAlloc(pos, repulsionRadius, s_overlapBuffer, 1 << 9);
        for (int i = 0; i < neighborCount; i++)
        {
            Collider n = s_overlapBuffer[i];
            if (n.gameObject != gameObject && !n.isTrigger)
            {
                Vector3 push = pos - n.transform.position; push.y = 0f;
                float d = push.magnitude;
                if (d < repulsionRadius && d > 0f) repulsion += push.normalized * (repulsionRadius - d);
            }
        }

        // Catchable kiter: when the player gets close it BACKPEDALS (and keeps
        // shooting) instead of standing AFK — but slowly enough that a running or
        // dashing player runs it down, so it never becomes an unkillable fleer.
        // Out of range it closes the gap; in the sweet spot it holds and shoots.
        float backoffDist = 3.6f;
        float moveSpeedMult;
        Vector3 move;
        if (dist < backoffDist)
        {
            move = -dir;            // kite back at a catchable pace, still firing
            moveSpeedMult = 0.5f;
        }
        else if (dist > preferredRange * 1.15f)
        {
            move = dir;             // close the gap
            moveSpeedMult = 0.85f;
        }
        else
        {
            move = Vector3.zero;    // in the band → hold ground and shoot
            moveSpeedMult = 0f;
        }
        move = (move + repulsion * 0.6f);
        if (move.sqrMagnitude > 0.0001f) move.Normalize();

        if (move != Vector3.zero)
        {
            Vector3 next = pos + move * (actualMoveSpeed * moveSpeedMult) * Time.deltaTime;
            next.y = SampleTerrainHeight(next) + verticalOffset;
            SetPositionSafe(next);
            if (animator != null) animator.SetBool("isMoving", true);
        }
        else if (animator != null) animator.SetBool("isMoving", false);
    }

    private IEnumerator RangedAttackRoutine()
    {
        isPreparingAttack = true;
        if (animator != null) animator.SetBool("isMoving", false);
        SetAnimBoolSafe("Aim", true);   // archer draws the bow (aim state) during the wind-up
        PlayVocal(AudioID.Enemy_Telegraph);
        if (ThreatUI.Instance != null) ThreatUI.Instance.ShowThreat(transform, attackTelegraphTime + 0.2f);

        Color baseTele = isElite ? new Color(1f, 0.5f, 0f) : new Color(1f, 0.15f, 0.05f);
        float elapsed = 0f;
        while (elapsed < attackTelegraphTime)
        {
            elapsed += Time.deltaTime;
            // keep aiming through the wind-up so the shot tracks the player
            if (target != null)
            {
                Vector3 aim = target.position - transform.position; aim.y = 0f;
                if (aim != Vector3.zero)
                    transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(aim), 10f * Time.deltaTime);
            }
            float pulse = Mathf.PingPong(elapsed * 8f, 1f);
            SetColor(Color.Lerp(baseTele, Color.white, pulse));
            yield return null;
        }
        ResetColor();

        if (!isDead && target != null)
        {
            lastAttackTime = Time.time;
            if (animator != null) animator.SetTrigger("Attack");
            FireProjectile();
            if (AudioManager.Instance != null && Time.time - lastAttackSfxTime > ATTACK_SFX_COOLDOWN)
            {
                lastAttackSfxTime = Time.time;
                AudioManager.Instance.PlaySFX3D(AudioID.Arrow_Fire, transform.position); // bow release
            }
        }
        // Keep Aim ON — UpdateRangedBehavior lowers it only when disengaged, so
        // the archer holds the bow nocked between shots instead of dropping it.
        yield return new WaitForSeconds(0.2f);
        isPreparingAttack = false;
    }

    // Sets an Animator bool only if the controller actually has that parameter,
    // so a shared controller without the archer-specific params logs no warnings.
    private void SetAnimBoolSafe(string param, bool value)
    {
        if (animator == null) return;
        foreach (var p in animator.parameters)
            if (p.type == AnimatorControllerParameterType.Bool && p.name == param)
            {
                animator.SetBool(param, value);
                return;
            }
    }

    private void FireProjectile()
    {
        if (projectilePrefab == null || target == null) return;

        Vector3 spawn = projectileSpawnPoint != null
            ? projectileSpawnPoint.position
            : transform.position + Vector3.up * 1.2f + transform.forward * 0.5f;
        Vector3 aimPoint = target.position + Vector3.up * 0.9f; // aim at the torso

        // LEAD the shot: predict where the player will be after the arrow's flight
        // time and aim there, so a running player can't just walk out of every
        // arrow. leadFactor < 1 keeps it beatable (dashing/turning still dodges).
        float roughDist = Vector3.Distance(spawn, aimPoint);
        float tPredict = Mathf.Clamp(roughDist / Mathf.Max(1f, projectileSpeed), 0.35f, 2.2f);
        if (playerTarget != null)
            aimPoint += playerTarget.HorizontalVelocity * (tPredict * rangedLeadFactor);

        GameObject proj = Instantiate(projectilePrefab, spawn, Quaternion.identity);
        EnemyProjectile ep = proj.GetComponent<EnemyProjectile>();
        if (ep == null) ep = proj.AddComponent<EnemyProjectile>(); // survive a prefab that forgot the component

        // Ballistic solve: choose a flight time from the distance, then the launch
        // velocity that reaches the aim point under gravity — the arrow lobs in a
        // real arc and drops onto the player instead of flying dead straight.
        Vector3 toTarget = aimPoint - spawn;
        Vector3 flat = new Vector3(toTarget.x, 0f, toTarget.z);
        float d = flat.magnitude;
        float g = Mathf.Max(0f, arcGravity);
        float tFlight = Mathf.Clamp(d / Mathf.Max(1f, projectileSpeed), 0.35f, 2.2f);
        Vector3 vel = flat / tFlight;
        vel.y = toTarget.y / tFlight + 0.5f * g * tFlight;
        ep.LaunchBallistic(vel, g, damage, gameObject);
    }

    private IEnumerator AttackRoutine()
    {
        isPreparingAttack = true;
        if (animator != null) animator.SetBool("isMoving", false);
        // Telegraph growl is the pre-attack roar the player kept hearing
        // continue off a corpse — route it through the tracked vocal so
        // Die()/OnDisable can cut it. Stops any lingering aggro roar too,
        // so the two never overlap into a "double" sound.
        PlayVocal(AudioID.Enemy_Telegraph);

        if (ThreatUI.Instance != null) ThreatUI.Instance.ShowThreat(transform, attackTelegraphTime + 0.2f);

        if (isElite && playerTarget != null)
        {
            playerTarget.OpenPerfectDodgeWindow(transform, attackTelegraphTime + 0.6f);

            if (weaponGlintVFX != null && ObjectPoolManager.Instance != null)
                ObjectPoolManager.Instance.SpawnFromPool(weaponGlintVFX, transform.position + Vector3.up * 1.5f, Quaternion.identity);
        }

        if (TutorialHints.Instance != null)
            TutorialHints.Instance.ShowIfNew("CombatTelegraph",
                "TIP: red flash on an enemy = incoming attack. DASH (Space) through it to dodge.", 5f);

        Color baseTele = isEnraged ? Color.black : (isElite ? new Color(1f, 0.5f, 0f) : new Color(1f, 0.15f, 0.05f));
        Color flashTele = Color.white;
        float elapsed = 0f;
        while (elapsed < attackTelegraphTime)
        {
            elapsed += Time.deltaTime;
            float pulse = Mathf.PingPong(elapsed * 8f, 1f);
            SetColor(Color.Lerp(baseTele, flashTele, pulse));
            yield return null;
        }
        ResetColor();

        if (!isDead && Vector3.Distance(transform.position, target.position) <= attackRange + 0.5f)
        {
            lastAttackTime = Time.time;
            if (animator != null) animator.SetTrigger("Attack");
            // Swing / lunge SFX at the moment the animator commits — the
            // telegraph beeps as the wind-up, this reads as the strike.
            // Dedupe: if the animation clip ALSO has an Animation Event
            // that plays Enemy_Attack, our code path plus the event path
            // would fire twice on the same frame. 150ms cooldown keeps
            // legitimate follow-up attacks working.
            if (AudioManager.Instance != null && Time.time - lastAttackSfxTime > ATTACK_SFX_COOLDOWN)
            {
                lastAttackSfxTime = Time.time;
                AudioManager.Instance.PlaySFX3D(AudioID.Enemy_Attack, transform.position);
            }
        }
        yield return new WaitForSeconds(0.2f);
        isPreparingAttack = false;
    }

    public void ExecuteAttackDamage()
    {
        if (isDead || target == null) return;
        // Resolve who we're actually swinging at (ally or player). Fall back to
        // the player if the cached damageable was cleared.
        IDamageable tgt = currentTargetDamageable;
        if (tgt == null) tgt = playerTarget;
        if (tgt == null) return;
        // Don't hit a dead player.
        if (tgt == (IDamageable)playerTarget && playerTarget != null && playerTarget.currentHealth <= 0) return;

        if (Vector3.Distance(transform.position, target.position) <= attackRange + 1f)
        {
            // Name feeds the death recap "Slain by …" line. Uses the
            // enemy GO name minus any "(Clone)" suffix so the recap
            // reads clean (e.g. "Slain by Skeleton Sentry").
            string src = gameObject.name;
            int cloneIdx = src.IndexOf("(Clone)");
            if (cloneIdx > 0) src = src.Substring(0, cloneIdx).TrimEnd();
            tgt.TakeDamage(new DamageInfo { Amount = damage, PushDirection = transform.forward, SourceName = src });
            // Landed-hit impact SFX. Enemy_Hit is the meaty thud; the
            // player's own Hurt SFX plays inside TakeDamage.
            if (AudioManager.Instance != null)
                AudioManager.Instance.PlaySFX3D(AudioID.Enemy_Hit, transform.position);
        }
    }

    public void TakeDamage(DamageInfo info)
    {
        if (isDead || isInvincible) return;

        if (startPassive && !isAggroed)
        {
            Aggro();
            if (parentGroup != null) parentGroup.AlertAll();
        }

        currentHealth -= info.Amount;
        if (currentHealth < 0) currentHealth = 0;

        // --- Показуємо ХП та передаємо нове значення для анімації ---
        if (healthCanvas != null && !healthCanvas.activeSelf)
        {
            healthCanvas.SetActive(true);
        }
        targetHealthRatio = currentHealth / maxHealth;
        // -------------------------------------------------------------

        if (AudioManager.Instance != null) AudioManager.Instance.PlaySFX3D(AudioID.Enemy_Hurt, transform.position);
        StartCoroutine(HitFlashRoutine());

        bool showPopups = PlayerPrefs.GetInt("Settings_DamagePopups", 1) == 1;
        if (damagePopupPrefab != null && showPopups && ObjectPoolManager.Instance != null)
        {
            GameObject popup = ObjectPoolManager.Instance.SpawnFromPool(damagePopupPrefab, transform.position + Vector3.up, Quaternion.identity);
            popup.GetComponent<DamagePopup>()?.Setup(info.Amount, info.IsCritical);
        }

        if (currentHealth <= 0)
        {
            Die();
        }
        else
        {
            currentPoise -= info.KnockbackForce * 10f;

            if (!isEnraged && !isSpawning)
            {
                if (!isElite || currentPoise <= 0 || info.IsCritical)
                {
                    knockbackVelocity = info.PushDirection * info.KnockbackForce;
                    stunTimer = info.StunDuration;
                    isPreparingAttack = false;
                    currentPoise = maxPoise;
                    ResetColor();
                    if (animator != null) animator.SetTrigger("Hit");
                }
            }
        }
    }

    public void MakeInvincibleAndFurious()
    {
        isInvincible = true; isEnraged = true; actualMoveSpeed = moveSpeed * 1.8f;
        for (int i = 0; i < originalColors.Length; i++) originalColors[i] = new Color(0.2f, 0f, 0f);
        ResetColor();
    }

    private IEnumerator HitFlashRoutine()
    {
        SetColor(Color.white);
        yield return new WaitForSeconds(0.1f);
        if (!isPreparingAttack) ResetColor();
    }

    private void SetColor(Color c)
    {
        if (meshRenderers == null || s_mpb == null) return;
        foreach (var r in meshRenderers)
        {
            if (r == null) continue;
            r.GetPropertyBlock(s_mpb);
            s_mpb.SetColor(s_baseColorID, c);
            s_mpb.SetColor(s_colorID, c);
            r.SetPropertyBlock(s_mpb);
        }
    }

    private void ResetColor()
    {
        if (meshRenderers == null || originalColors == null || s_mpb == null) return;
        for (int i = 0; i < meshRenderers.Length; i++)
        {
            Renderer r = meshRenderers[i];
            if (r == null) continue;
            r.GetPropertyBlock(s_mpb);
            s_mpb.SetColor(s_baseColorID, originalColors[i]);
            s_mpb.SetColor(s_colorID, originalColors[i]);
            r.SetPropertyBlock(s_mpb);
        }
    }

    public void ForceStop()
    {
        isCinematicFrozen = false;
        isSpawning = false;
        if (animator != null)
        {
            animator.SetBool("isMoving", false);
            animator.Play("Idle", 0, 0f);
        }
    }

    private void Die()
    {
        if (isDead) return;
        isDead = true;

        // Миттєво ховаємо канвас після смерті
        if (healthCanvas != null)
        {
            healthCanvas.SetActive(false);
        }

        PlayerController.OnEnemyKilled?.Invoke();

        // ==========================================
        // ФІКС АНІМАЦІЇ: Жорстке скидання всіх станів
        // ==========================================
        if (animator != null)
        {
            animator.SetBool("isMoving", false); // Змушуємо забути про біг
            animator.SetFloat("Speed", 0f);

            animator.ResetTrigger("Hit");        // Видаляємо всі "застряглі" тригери
            animator.ResetTrigger("Attack");

            // Archers were stuck in Bow_Attack: the AnyState→Bow_Attack transition
            // is gated on IsRanged && Aim, so with Aim still true it kept
            // overriding the Die trigger. Clear both so Die actually plays.
            SetAnimBoolSafe("Aim", false);
            SetAnimBoolSafe("IsRanged", false);

            // Якщо смерть все одно іноді не програється (через складні transitions),
            // заміни рядок нижче на: animator.Play("Die", 0, 0f);
            animator.SetTrigger("Die");
        }

        // Remove the red minimap marker INSTANTLY on death — it lived on a
        // MinimapOnly-layer renderer that otherwise stayed visible for the whole
        // corpse fade, so dead enemies kept showing as blips.
        int deadMinimapLayer = LayerMask.NameToLayer("MinimapOnly");
        if (deadMinimapLayer >= 0)
        {
            Renderer[] allR = GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < allR.Length; i++)
                if (allR[i] != null && allR[i].gameObject.layer == deadMinimapLayer) allR[i].enabled = false;
        }

        if (AudioManager.Instance != null)
        {
            // Cut whatever combat vocal (aggro roar OR attack telegraph
            // growl) was live so it doesn't keep ringing off the corpse.
            // 0.25s tail sells "the beast choked mid-roar" over a hard
            // snap. Covers BOTH sounds now, not just aggro.
            StopVocal(0.25f);
            AudioManager.Instance.PlaySFX3D(AudioID.Enemy_Die, transform.position);
        }

        if (deathVFXPrefab != null)
        {
            GameObject vfx = null;
            if (ObjectPoolManager.Instance != null) vfx = ObjectPoolManager.Instance.SpawnFromPool(deathVFXPrefab, transform.position + Vector3.up * 1f, Quaternion.identity);
            if (vfx == null) Instantiate(deathVFXPrefab, transform.position + Vector3.up * 1f, Quaternion.identity);
        }

        // Вимикаємо всі колізії, включно з CharacterController.
        // Ворог не провалиться крізь карту, бо isDead зупиняє Update(),
        // відповідно гравітація (MoveEnemy) більше не застосовується.
        foreach (Collider c in GetComponentsInChildren<Collider>()) c.enabled = false;
        ResetColor();

        if (xpCrystalPrefab != null)
        {
            GameObject xc = Instantiate(xpCrystalPrefab, transform.position + Vector3.up * 1f, Quaternion.identity);
            // Scale the XP payout by this enemy's reward multiplier (region
            // difficulty + survival time). This was COMPUTED in Start()/the
            // spawner but never actually applied — every enemy dropped a flat
            // crystal, so hard regions and long runs gave no extra XP.
            XpCrystal xcData = xc.GetComponent<XpCrystal>();
            if (xcData != null && xpRewardMultiplier > 0f)
                xcData.xpAmount *= xpRewardMultiplier;
        }

        if (diamondPrefab != null && Random.value <= diamondDropChance)
        {
            int dropCount = isElite ? Random.Range(2, 4) : 1;
            for (int d = 0; d < dropCount; d++)
            {
                Vector2 spread = Random.insideUnitCircle * 0.6f;
                Vector3 pos = transform.position + new Vector3(spread.x, 1f, spread.y);
                Instantiate(diamondPrefab, pos, Quaternion.identity);
            }
        }

        if (MissionManager.Instance != null) MissionManager.Instance.AddProgress(MissionType.KillEnemies, 1);
        if (Level1_QuestManager.Instance != null) Level1_QuestManager.Instance.EnemyDefeated();

        // Feed the per-run scoreboard (drives the death recap panel).
        // isElite reads directly off the SO; boss-tier kills are handled
        // separately by boss AIs which increment RunSession.AddKill(...,
        // isBoss:true).
        RunSession.AddKill(isElite: isElite, isBoss: false);

        StartCoroutine(DeathDissolveRoutine());
    }

    private IEnumerator DeathDissolveRoutine()
    {
        yield return new WaitForSeconds(1.5f);

        if (dissolveAshVFX != null) dissolveAshVFX.Play();

        float dissolveDuration = 1.5f;
        float elapsed = 0f;
        Vector3 startScale = transform.localScale;
        Vector3 targetScale = new Vector3(startScale.x, 0.05f, startScale.z);
        Vector3 startPos = transform.position;
        Vector3 targetPos = startPos - new Vector3(0, 0.5f, 0);

        while (elapsed < dissolveDuration)
        {
            elapsed += Time.deltaTime;
            float t = elapsed / dissolveDuration;

            transform.localScale = Vector3.Lerp(startScale, targetScale, t);
            transform.position = Vector3.Lerp(startPos, targetPos, t);
            SetColor(Color.Lerp(originalColors[0], Color.black, t));

            yield return null;
        }

        Destroy(gameObject);
    }

    private void SetPositionSafe(Vector3 newPos)
    {
        if (cc != null && cc.enabled)
        {
            // Більше жодного вимикання колайдерів! 
            // Move() автоматично застосовує дельту переміщення і коректно взаємодіє з землею.
            Vector3 movementDelta = newPos - transform.position;
            cc.Move(movementDelta);
        }
        else
        {
            transform.position = newPos;
        }
    }
}