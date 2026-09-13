using UnityEngine;
using UnityEngine.UI;
using System.Collections;

public class EnemyAI : MonoBehaviour, IDamageable
{
    [Header("Archetype & Poise")]
    public bool isElite = false;

    [Tooltip("Suppress XP crystals and diamond drops on death. Set by the trailer's battle director: pickups spilling out of every kill read as gameplay, not cinema.")]
    public bool suppressDrops = false;

    [Tooltip("Region bosses run on this same AI, but had no way to say so — which is why they fought in silence: every sound they made was the generic skeleton set, and the shared vocal cooldown let the swarm around them win it every time. Tick this on a boss prefab for its own roar, slam, enrage and death audio, and for vocals that ignore the crowd's cooldown.")]
    public bool isBoss = false;
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
    [Tooltip("With this AND isCinematicFrozen set, the AI stops touching the animator entirely so a director can pose and move this enemy itself. Without it, the freeze forces isMoving true every frame for the victory flythrough.")]
    public bool cinematicDrivesAnimator = false;

    [Header("Spawn Settings")]
    public GameObject spawnVFXPrefab;
    public float spawnDuration = 1.5f;

    [Header("Combat Settings")]
    public float attackRange = 1.6f;
    [Tooltip("Seconds between one enemy's swings. Raised from 1.5: with several enemies each on their own timer, a short individual cooldown adds up to a wall of blows the player has no gap to act in.")]
    public float attackCooldown = 2.1f;
    [Tooltip("Wind-up before the blow lands. This is the player's whole window to read the attack and move, so it is a readability number, not a difficulty one.")]
    public float attackTelegraphTime = 0.65f;
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

    [Header("Skeleton Mage")]
    [Tooltip("Casts a flying magic orb instead of meleeing. Auto-enabled for enemies whose name contains 'mage'. Uses ranged behavior; the orb is built at runtime, no prefab needed.")]
    public bool magicCaster = false;
    [Tooltip("Color of the mage's magic orb + its glow/trail.")]
    public Color magicOrbColor = new Color(0.55f, 0.35f, 1f);
    [Tooltip("Diameter of the mage's orb, in metres. It was a hard-coded 0.5, which is large next to characters at this scale — the bolt read as a boulder.")]
    [Range(0.12f, 0.8f)] public float magicOrbSize = 0.34f;

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
    // When true this enemy never shows its floating world-space health bar (the
    // trailer boss uses the on-screen GlobalHUD boss bar instead).
    [HideInInspector] public bool suppressWorldHealthBar = false;
    public Image healthFill;        // Зображення смужки ХП (Image Type = Filled)
    private float targetHealthRatio = 1f;

    [HideInInspector] public float xpRewardMultiplier = 1f;

    public bool isInvincible = false;
    private bool isEnraged = false;
    private bool isSpawning = false;

    private float currentHealth;

    // Read-only accessors + a direct health setter for cinematics/trailer (the
    // boss-duel scales HP to a fixed number of player hits and drives the HP bar).
    public float CurrentHealth => currentHealth;
    public bool IsDead => isDead;
    public void SetHealthDirect(float hp)
    {
        maxHealth = Mathf.Max(1f, hp);
        currentHealth = maxHealth;
        targetHealthRatio = 1f;
    }

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

    // Global halt for victory cinematics: when true every enemy stops moving,
    // deciding, and attacking so it can't chip the player (or wander) while the
    // camera is off flying the reveal. Reset to false on world generation so an
    // interrupted cinematic can't leave the next region frozen.
    public static bool GlobalFreeze = false;
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

    // ==== READABLE WINDOWS ====
    //
    // Every enemy used to wind up for exactly the same 0.65 s, which is why a
    // crowd read as weather rather than as a set of problems. When the tell is
    // identical on all of them there is nothing to notice, so the only strategy
    // left is to back away from all of it at once — and being unable to answer a
    // tell is what makes a fight draining rather than hard.
    //
    // Weighting the wind-up by archetype turns the same crowd into a readable
    // one: the rogue's jab is over before you can react and barely hurts, the
    // warrior's is the one you time your dodge on, and the boss's is long enough
    // to walk around behind. The player starts CHOOSING who to hit first, which
    // is the decision the fight was missing.
    public bool IsPreparingAttack => isPreparingAttack;

    private float TelegraphScale
    {
        get
        {
            if (isBoss) return 1.75f;
            var a = personality != null ? personality.archetype : EnemyPersonality.Archetype.Minion;
            switch (a)
            {
                case EnemyPersonality.Archetype.Boss:        return 1.75f;
                case EnemyPersonality.Archetype.Warrior:     return 1.15f;
                case EnemyPersonality.Archetype.Necromancer: return 1.30f;
                case EnemyPersonality.Archetype.Mage:        return 1.30f;
                case EnemyPersonality.Archetype.Rogue:       return 0.62f;
                case EnemyPersonality.Archetype.Archer:      return 1.0f;
                default:                                     return 0.78f;   // Minion
            }
        }
    }

    private float EffectiveTelegraph => attackTelegraphTime * TelegraphScale;
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

        // Fast path. This is called for every enemy every frame, and a 150m
        // physics raycast per enemy is one of the heaviest things a horde does.
        // Terrain.SampleHeight is a heightmap lookup with no physics at all, so
        // take it whenever the answer is plausible — i.e. the enemy is standing
        // near the terrain rather than on a bridge, a location's floor mesh or a
        // prop. The raycast still runs in those cases.
        Terrain fast = Terrain.activeTerrain;
        if (fast != null)
        {
            float th = fast.SampleHeight(worldPos) + fast.transform.position.y;
            if (Mathf.Abs(worldPos.y - th) < 2.5f) return th;
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
        // Give the attack slot back. A dead or pooled enemy still holding one
        // means the crowd is quietly allowed fewer attackers than it should be,
        // and after a few fights nobody can swing at all.
        if (CombatRing.Instance != null) CombatRing.Instance.ReportDisengaged(this);

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

    // ---- Perception: losing the player, and looking for them ------------------
    //
    // Chase used to be a one-way door. Once aggroed an enemy pursued forever, so
    // a region slowly drained into one ball of skeletons around the player and
    // breaking contact was impossible. These give the chase an end, in three
    // stages, so retreating is a real tactic rather than a slower death:
    //
    //   sight lost  -> keep chasing the LAST KNOWN position for a moment
    //   arrived     -> SEARCH the area for a while, still able to re-spot
    //   nothing     -> walk back to the post and resume duty
    //
    // EVERYONE opts in now, including the radial spawner's horde.
    //
    // "Relentless" was the intent and it turned out to mean a skeleton following
    // the player across the entire map for the rest of the run. That is not
    // pressure, it is a tail: the player has no move that answers it, so the only
    // thing running away buys is a longer conga line, and a fight you cannot
    // leave stops being a fight you are choosing to have.
    //
    // The horde is still the persistent one — it just gives up eventually, and it
    // gives up on LOSING SIGHT rather than on distance, so breaking line of sight
    // is the skill that works. Encounter enemies keep the tighter numbers set on
    // them by their group.
    [HideInInspector] public bool canDeAggro = true;
    // This enemy's own look and gait. Added in Start; see the note there.
    private EnemyPersonality personality;
    private Vector3 _lastGaitPos;
    private float _measuredSpeed;
    [HideInInspector] public float loseSightDuration = 6f;   // grace after sight breaks
    [HideInInspector] public float searchDuration = 9f;      // how long the area is searched
    [HideInInspector] public float searchRoamRadius = 7f;
    [HideInInspector] public float chaseGiveUpRange = 34f;   // too far to see, whatever the walls say
    [HideInInspector] public float leashRange = 55f;         // max distance from the post

    private bool isSearching = false;
    private float searchEndTime;
    private float sightLostTimer;
    private Vector3 lastKnownTargetPos;
    private float perceptionCheckTimer;

    // The post this enemy belongs to, captured the first time it is made
    // passive. Search and de-aggro restore these, so a patrol that chased the
    // player across the map walks back to its own route instead of adopting
    // whatever spot it happened to give up on.
    private bool homeCaptured = false;
    private Vector3 homeAnchorPoint;
    private Transform homeAnchorTransform;
    private float homeRoamRadius;
    private bool homeRoamWhilePassive;

    public bool IsSearching => isSearching;

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

            // Give this one an identity of its own — its own idle, its own death,
            // its own gait and build. Every enemy prefab in the game shares a
            // single controller, so without this a camp of four skeletons is one
            // skeleton drawn four times, animating on the same frame.
            //
            // GetComponent then an explicit null check, never `?? AddComponent`:
            // UnityEngine.Object overloads ==, so a destroyed-but-not-yet-collected
            // component is "null" to == and NOT null to ??, and the coalescing form
            // silently skips the Add.
            personality = GetComponent<EnemyPersonality>();
            if (personality == null) personality = gameObject.AddComponent<EnemyPersonality>();
            _lastGaitPos = transform.position;
        }

        randomOffset = Random.Range(0f, 100f);
        // Spread the first avoidance solve so a whole wave doesn't probe on the
        // same frame — that spike is what a crowd would otherwise cost.
        _nextAvoidSolve = Time.time + Random.value / Mathf.Max(1f, avoidSolvesPerSecond);
        strafeDir = Random.value > 0.5f ? 1f : -1f;
    }

    private void Start()
    {
        mainCamTransform = CameraCache.MainTransform;
        dayNightCycle = FindFirstObjectByType<DayNightCycle>();
        actualMoveSpeed = moveSpeed * Random.Range(0.8f, 1.2f);

        // Skeleton Mage: casts a flying magic orb instead of meleeing like a
        // grunt. Detected by name so no per-prefab wiring is needed. Force ranged
        // behavior so it kites and uses the cast loop; FireProjectile builds the
        // orb at runtime (no projectile prefab required).
        if (!magicCaster && name.ToLowerInvariant().Contains("mage"))
            magicCaster = true;
        if (magicCaster)
            isRanged = true;

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
            // The summoner "remains" prop was rendering magenta (its material's
            // shader is missing from the build) — repair it so it reads as bone.
            ShaderRepair.Fix(gameObject);
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
        // Walk or run, decided from intent rather than from speed.
        //
        // Driven from ONE place on purpose. `isMoving` is written from eighteen
        // sites in this file, and threading a gait argument through all of them
        // would be eighteen chances to get it backwards. Chasing runs; roaming,
        // searching and walking back to a post do not — which is why the game's
        // patrols have always looked like they were sprinting to nowhere.
        if (personality != null)
        {
            personality.SetGait(isAggroed && !isSearching);
            // Measured travel, not the intended speed: night buffs, the enrage
            // multiplier, obstacle steering and the strafe all change how far the
            // enemy actually gets, and it is the actual distance the feet have to
            // match or they slide.
            float dt = Time.deltaTime;
            if (dt > 0f)
            {
                Vector3 moved = transform.position - _lastGaitPos;
                moved.y = 0f;
                _measuredSpeed = Mathf.Lerp(_measuredSpeed, moved.magnitude / dt, 1f - Mathf.Exp(-10f * dt));
                _lastGaitPos = transform.position;
                personality.MatchLocomotion(_measuredSpeed, isPreparingAttack);
            }
        }

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

        // Victory cinematic: freeze in place — no movement, no attack decisions.
        // (HP-bar billboarding above still runs; it's harmless and cheap.)
        if (GlobalFreeze && !isDead) return;

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
            // isMoving is forced TRUE for the victory flythrough, where the
            // enemies are meant to keep running on the spot. A choreographed
            // scene drives the animator itself, and having this overwrite it
            // every frame is what left skeletons running in place while standing
            // still — so it now respects cinematicDrivesAnimator.
            if (animator != null && isCinematicFrozen && !cinematicDrivesAnimator)
                animator.SetBool("isMoving", true);
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

        // Chasing: decide whether the chase is still justified. Only encounter
        // enemies can give up; the radial horde stays relentless.
        if (canDeAggro)
        {
            UpdateChasePerception();
            if (!isAggroed) { UpdatePassiveBehavior(); return; }
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

            // ==== ASK BEFORE SWINGING ====
            //
            // Two enemies press at a time; the rest circle. See CombatRing for
            // why — eight independent attackers produce a wall of damage with no
            // gaps, which is not difficulty, it is arithmetic, and it makes both
            // blocking and counter-attacking impossible before they are written.
            var ring = CombatRing.Instance;
            if (ring != null) ring.ReportEngaged(this);
            bool hasSlot = ring == null || ring.IsAttacking(this);

            // Commit only once genuinely in range.
            //
            // This used to fire at 1.15x, on the theory that the wind-up lunge
            // would cover the last of the gap. In practice the two compounded:
            // the enemy started its swing well short AND then chased the player
            // through the whole telegraph, so backing off did nothing and hits
            // seemed to land from outside the range the player could see. Wind up
            // on arrival, not on approach.
            if (isAttackReady && distanceToPlayer <= attackRange
                && (hasSlot || ring.RequestAttack(this)))
            {
                StartCoroutine(AttackRoutine());
            }
            else if (isAttackReady && distanceToPlayer > attackRange && (hasSlot || ring.RequestAttack(this)))
            {
                if (animator != null) animator.SetBool("isMoving", true);

                Vector3 moveDir = SteerAroundObstacles(currentPos, (directionToPlayer + repulsion).normalized);
                Vector3 nextPos = currentPos + moveDir * actualMoveSpeed * Time.deltaTime;

                nextPos.y = SampleTerrainHeight(nextPos) + verticalOffset;
                SetPositionSafe(nextPos);
            }
            else if (ring != null && !hasSlot)
            {
                // NO SLOT: CIRCLE, DO NOT QUEUE.
                //
                // The whole risk of a token system is that the ones waiting look
                // like they are waiting. They must not stand, and they must not
                // all orbit alike — CombatRing.PostFor gives each of them its own
                // radius, direction, pace and drift, so the shape around the
                // player stays ragged and alive rather than reading as a circle
                // somebody spawned.
                if (animator != null) animator.SetBool("isMoving", true);

                Vector3 post = ring.PostFor(this, target.position, Time.time);
                Vector3 toPost = post - currentPos; toPost.y = 0f;

                // Close fast when far from the post, ease when near it, so a
                // waiter settles into its orbit instead of jittering on the spot.
                float urgency = Mathf.Clamp01(toPost.magnitude / 3f);
                Vector3 desired = toPost.sqrMagnitude > 0.04f ? toPost.normalized : Vector3.zero;

                Vector3 moveDir = SteerAroundObstacles(currentPos, (desired + repulsion * 0.6f).normalized);
                Vector3 nextPos = currentPos + moveDir * (actualMoveSpeed * Mathf.Lerp(0.35f, 0.95f, urgency)) * Time.deltaTime;
                nextPos.y = SampleTerrainHeight(nextPos) + verticalOffset;
                SetPositionSafe(nextPos);

                // A waiter still THREATENS. Every few seconds it lunges a step
                // and raises its weapon without swinging — enough that the crowd
                // reads as a pack looking for an opening rather than an audience.
                if (ring.ShouldFeint(this, Time.time) && !isPreparingAttack)
                    StartCoroutine(FeintRoutine());
            }
            else
            {
                if (animator != null) animator.SetBool("isMoving", true);

                Vector3 flankDir = Vector3.Cross(Vector3.up, directionToPlayer) * strafeDir;

                if (distanceToPlayer > attackRange * 0.9f) flankDir += directionToPlayer * 0.4f;
                else if (distanceToPlayer < attackRange * 0.6f) flankDir -= directionToPlayer * 0.5f;

                Vector3 moveDir = SteerAroundObstacles(currentPos, (flankDir + repulsion).normalized);
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
            finalDirection = SteerAroundObstacles(currentPos, finalDirection);

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

    // Remember where this enemy is stationed, so search and de-aggro have
    // somewhere to send it back to. Captured once, on the first passive setup.
    public void CapturePost()
    {
        if (homeCaptured) return;
        homeCaptured = true;
        homeAnchorPoint = anchorTransform != null ? anchorTransform.position : anchorPoint;
        homeAnchorTransform = anchorTransform;
        homeRoamRadius = roamRadius;
        homeRoamWhilePassive = roamWhilePassive;
    }

    // Can this enemy actually see the target right now? Distance alone is not
    // sight -- without the line check, ducking behind a building did nothing and
    // the chase continued through solid walls.
    private bool CanSeeTarget()
    {
        if (target == null) return false;

        Vector3 eye = transform.position + Vector3.up * 1.4f;
        Vector3 at = target.position + Vector3.up * 1.0f;
        Vector3 to = at - eye;
        float dist = to.magnitude;
        if (dist > chaseGiveUpRange) return false;
        if (dist < 0.05f) return true;

        // Only static world geometry blocks sight. Other enemies must not, or a
        // crowd would blind itself and the whole pack would give up at once.
        int blockers = 0;
        int def = LayerMask.NameToLayer("Default");      if (def >= 0) blockers |= 1 << def;
        int obs = LayerMask.NameToLayer("Obstacles");    if (obs >= 0) blockers |= 1 << obs;
        int nat = LayerMask.NameToLayer("Nature");       if (nat >= 0) blockers |= 1 << nat;
        if (blockers == 0) return true;

        return !Physics.Raycast(eye, to / dist, dist - 0.5f, blockers, QueryTriggerInteraction.Ignore);
    }

    // Runs while aggroed. Decides when the chase is over.
    private void UpdateChasePerception()
    {
        perceptionCheckTimer -= Time.deltaTime;
        if (perceptionCheckTimer > 0f) return;
        perceptionCheckTimer = 0.25f;

        // Wandered too far from the post? Give up regardless of sight, or the
        // region hollows out as every group migrates toward the player.
        if (homeCaptured)
        {
            Vector3 post = homeAnchorTransform != null ? homeAnchorTransform.position : homeAnchorPoint;
            if ((transform.position - post).sqrMagnitude > leashRange * leashRange)
            {
                BeginSearch(transform.position);
                return;
            }
        }

        if (CanSeeTarget())
        {
            sightLostTimer = 0f;
            lastKnownTargetPos = target.position;
            return;
        }

        sightLostTimer += 0.25f;
        if (sightLostTimer >= loseSightDuration)
            BeginSearch(lastKnownTargetPos);
    }

    // Stop chasing and comb the area around `center`. Still fully able to
    // re-spot the player, so hiding right next to a searcher does not work.
    public void BeginSearch(Vector3 center)
    {
        if (isDead) return;
        CapturePost();

        isAggroed = false;
        isSearching = true;
        searchEndTime = Time.time + searchDuration;
        sightLostTimer = 0f;

        startPassive = true;
        anchorTransform = null;
        anchorPoint = center;
        roamRadius = searchRoamRadius;
        roamWhilePassive = true;
        nextRoamPickTime = 0f;   // pick a search point immediately
    }

    // Nothing found. Back to the route.
    private void ReturnToPost()
    {
        isSearching = false;
        if (!homeCaptured) return;
        anchorTransform = homeAnchorTransform;
        anchorPoint = homeAnchorPoint;
        roamRadius = homeRoamRadius;
        roamWhilePassive = homeRoamWhilePassive;
        nextRoamPickTime = 0f;
    }

    // Called by a horn, a watchtower, or a neighbouring group: something happened
    // over there, go and look. Deliberately NOT an instant aggro -- the enemy
    // converges on the noise and only engages if it actually finds the player,
    // so a raised alarm reads as a search party rather than as everyone gaining
    // perfect knowledge of the player's position.
    public void AlertTo(Vector3 position)
    {
        if (isDead || isAggroed) return;
        CapturePost();

        isSearching = true;
        searchEndTime = Time.time + searchDuration;

        startPassive = true;
        anchorTransform = null;
        anchorPoint = position;
        roamRadius = searchRoamRadius;
        roamWhilePassive = true;
        nextRoamPickTime = 0f;
    }

    private void UpdatePassiveBehavior()
    {
        // Search runs on the passive mover, just aimed somewhere else and on a
        // timer. When it expires the enemy goes back to its own duty.
        if (isSearching && Time.time >= searchEndTime) ReturnToPost();

        Vector3 anchor = anchorTransform != null ? anchorTransform.position : anchorPoint;

        passiveAggroCheckTimer -= Time.deltaTime;
        if (passiveAggroCheckTimer <= 0f && target != null)
        {
            passiveAggroCheckTimer = 0.2f;
            float distSqr = (target.position - transform.position).sqrMagnitude;
            // A searching enemy is alert: it notices further out, and it has to
            // actually see the player rather than sense them through a wall.
            float range = isSearching ? aggroRange * 1.6f : aggroRange;
            if (distSqr <= range * range && (!canDeAggro || CanSeeTarget()))
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
            // A search party moves with purpose; an idle patrol strolls.
            float passiveSpeed = actualMoveSpeed * (isSearching ? 0.8f : 0.4f);
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
        animator.SetBoolSafe("isMoving", moving);
        animator.SetFloatSafe("Speed", moving ? speed : 0f);
    }

    public void Aggro()
    {
        if (isAggroed) return;
        isAggroed = true;
        // Engaging ends any search and resets the give-up clock, so an enemy that
        // re-spots the player gets the full grace period again rather than
        // dropping the chase a moment later on a stale timer.
        isSearching = false;
        sightLostTimer = 0f;
        if (target != null) lastKnownTargetPos = target.position;
        CapturePost();

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
        PlayVocal(isBoss ? AudioID.Boss_Roar : AudioID.Enemy_Agro);
        if (isBoss)
        {
            SetAnimTriggerSafe("Roar");
            if (AudioManager.Instance != null) AudioManager.Instance.NotifyCombat(20f);
        }
    }

    // Plays a tracked 3D vocal on the enemy, stopping whatever vocal was
    // already playing first — guarantees at most one growl per enemy and
    // gives Die()/OnDisable a single handle to cut. Falls back to a
    // fire-and-forget one-shot if the looping variant is unavailable
    // (missing FMOD event) so the sound still plays; that fallback can't
    // be stopped, but it's the degraded path, not the norm.
    // Fully mute enemy roars/growls (set by the trailer director so a crowd
    // doesn't scream over the capture).
    public static bool SuppressCombatVocals = false;
    // Global throttle so a big crowd doesn't all roar/telegraph on the same
    // frame — that stacked into an ear-splitting wall of vocals. One new vocal
    // per this interval across ALL enemies; the rest silently skip theirs.
    private static float s_lastVocalTime = -10f;
    private const float GLOBAL_VOCAL_INTERVAL = 0.22f;

    // Bosses get their own line. The 0.22s gate is STATIC — shared by every
    // enemy alive — so in a boss fight the swarm of adds claimed it almost every
    // frame and the boss's own roars and telegraphs were dropped. A boss has its
    // own, much shorter cooldown and never competes with the crowd.
    private static float s_lastBossVocalTime = -10f;
    private const float BOSS_VOCAL_INTERVAL = 0.05f;

    private void PlayVocal(string audioId)
    {
        if (AudioManager.Instance == null || SuppressCombatVocals) return;
        if (isBoss)
        {
            if (Time.time - s_lastBossVocalTime < BOSS_VOCAL_INTERVAL) return;
            s_lastBossVocalTime = Time.time;
            StopVocal(0.05f);
            vocalSfxHandle = AudioManager.Instance.PlayLoopingSFX3D(audioId, transform);
            if (vocalSfxHandle == -1) AudioManager.Instance.PlaySFX3D(audioId, transform.position);
            return;
        }
        if (Time.time - s_lastVocalTime < GLOBAL_VOCAL_INTERVAL) return;
        s_lastVocalTime = Time.time;
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
        if (move.sqrMagnitude > 0.0001f) { move.Normalize(); move = SteerAroundObstacles(pos, move); }

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
            // Vary the swing when the rig offers alternates, so a long boss
            // fight isn't the same animation on loop.
            SetAnimIntSafe("AttackIndex", UnityEngine.Random.Range(0, 3));
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

        // HAND THE SLOT BACK, AND STEP OFF.
        //
        // Holding it until the cooldown expires would mean the same two enemies
        // monopolise the fight while everyone else circles — the version of this
        // system that looks staged. Releasing here lets somebody from the ring
        // lunge in next, so the crowd keeps rotating who presses.
        //
        // The brief retreat is what gives the player their turn. An enemy that
        // finishes a swing and stands in your face leaves no moment to lower a
        // guard and answer; one that gives ground for a beat creates the opening
        // the whole block-and-counter loop is built around.
        if (CombatRing.Instance != null) CombatRing.Instance.Release(this);
        if (!isDead) StartCoroutine(BackOffRoutine(0.55f));
    }

    // Sets an Animator bool only if the controller actually has that parameter,
    // so a shared controller without the archer-specific params logs no warnings.
    // Fire an animator trigger only if the controller actually declares it, so a
    // boss can be given Roar / Enrage / Stagger / attack-variation states without
    // any code change, and rigs that lack them are unaffected.
    private bool SetAnimTriggerSafe(string param)
    {
        if (animator == null) return false;
        foreach (var p in animator.parameters)
            if (p.type == AnimatorControllerParameterType.Trigger && p.name == param)
            {
                animator.ResetTrigger(param);
                animator.SetTrigger(param);
                return true;
            }
        return false;
    }

    private bool SetAnimIntSafe(string param, int value)
    {
        if (animator == null) return false;
        foreach (var p in animator.parameters)
            if (p.type == AnimatorControllerParameterType.Int && p.name == param)
            {
                animator.SetInteger(param, value);
                return true;
            }
        return false;
    }

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
        if (GlobalFreeze || isDead) return;   // no shots during the victory freeze
        if (target == null) return;
        if (magicCaster) { FireMagicOrb(); return; }
        if (projectilePrefab == null) return;

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

    // Skeleton Mage cast: a glowing magic orb that flies STRAIGHT at the player
    // (grenade-like sphere, distinct color) and bursts on impact. Built entirely
    // in code so no projectile prefab has to be wired onto the mage.
    private void FireMagicOrb()
    {
        Vector3 spawn = projectileSpawnPoint != null
            ? projectileSpawnPoint.position
            : transform.position + Vector3.up * 1.4f + transform.forward * 0.6f;
        Vector3 aimPoint = target.position + Vector3.up * 0.9f;

        float roughDist = Vector3.Distance(spawn, aimPoint);
        float tPredict = Mathf.Clamp(roughDist / Mathf.Max(1f, projectileSpeed), 0.3f, 2f);
        if (playerTarget != null)
            aimPoint += playerTarget.HorizontalVelocity * (tPredict * rangedLeadFactor);

        GameObject orb = BuildMagicOrb(spawn);
        var ep = orb.GetComponent<EnemyProjectile>();
        ep.stickOnHit = false;   // burst, don't embed like an arrow
        Vector3 dir = (aimPoint - spawn).normalized;
        if (dir.sqrMagnitude < 0.0001f) dir = transform.forward;
        ep.Launch(dir, Mathf.Max(9f, projectileSpeed), damage, gameObject);

        if (AudioManager.Instance != null) AudioManager.Instance.PlaySFX3D(AudioID.Totem_Activate, spawn);
    }

    private GameObject BuildMagicOrb(Vector3 pos)
    {
        var orb = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        orb.name = "MageOrb";
        orb.transform.position = pos;
        orb.transform.localScale = Vector3.one * magicOrbSize;
        // EnemyProjectile raycasts its own path — a collider would just cause
        // self-hits and physics noise, so strip the primitive's collider.
        var pc = orb.GetComponent<Collider>();
        if (pc != null) Destroy(pc);

        Shader sh = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
        var mat = new Material(sh);
        if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", magicOrbColor); else mat.color = magicOrbColor;
        mat.EnableKeyword("_EMISSION");
        if (mat.HasProperty("_EmissionColor")) mat.SetColor("_EmissionColor", magicOrbColor * 3.5f);
        var rend = orb.GetComponent<Renderer>();
        rend.material = mat;
        rend.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

        var light = orb.AddComponent<Light>();
        light.type = LightType.Point;
        light.color = magicOrbColor;
        light.range = 6f * (magicOrbSize / 0.5f);
        light.intensity = 3.2f;

        var trail = orb.AddComponent<TrailRenderer>();
        trail.time = 0.32f;
        // Sized off the orb, or shrinking the ball just leaves it rattling
        // around inside a streak that is still the old width.
        trail.startWidth = 0.42f * (magicOrbSize / 0.5f);
        trail.endWidth = 0f;
        trail.numCapVertices = 4;
        trail.material = mat;
        trail.startColor = magicOrbColor;
        trail.endColor = new Color(magicOrbColor.r, magicOrbColor.g, magicOrbColor.b, 0f);

        var ep = orb.AddComponent<EnemyProjectile>();
        ep.lifetime = 5f;
        ep.playHitSfx = true;
        return orb;
    }

    // REMOVED: the global crowd swing gate.
    //
    // The idea was that enemies should take turns rather than all raising a
    // weapon on the same frame. The implementation was wrong in a way that broke
    // the fight: when the gate refused, the enemy fell through the attack branch
    // into the STRAFE branch, so a crowd in attack range spent almost all of its
    // time side-stepping back and forth instead of swinging. That is the zigzag,
    // and it is also why enemies stopped doing damage — they were barely ever
    // reaching AttackRoutine at all.
    //
    // Desynchronising a crowd does not need a runtime gate. Each enemy now takes
    // a random slice off its FIRST cooldown at spawn (see Start), so they arrive
    // at their first swing at different moments and stay out of phase from then
    // on, with nothing to go wrong mid-fight.

    // A threat with no swing behind it.
    //
    // This is the single cheapest thing that stops a waiting crowd looking like
    // a queue: a step in, the weapon up, a growl, and back out. It costs the
    // player nothing and reads as an enemy hunting for an opening, which is what
    // the ones without a slot are supposed to be doing.
    private IEnumerator FeintRoutine()
    {
        if (isDead || target == null) yield break;
        _feinting = true;

        if (animator != null) { animator.ResetTrigger("Attack"); animator.SetTrigger("Attack"); }
        PlayVocal(AudioID.Enemy_Telegraph);

        // A short step toward the player and back. Short on purpose — a feint
        // that closes real distance is just an attack that forgot to hit.
        float t0 = 0f;
        while (t0 < 0.28f && !isDead && target != null && stunTimer <= 0f)
        {
            t0 += Time.deltaTime;
            Vector3 to = target.position - transform.position; to.y = 0f;
            if (to.sqrMagnitude > 0.01f)
            {
                Vector3 step = to.normalized * (moveSpeed * 0.5f * Time.deltaTime);
                Vector3 next = transform.position + step;
                next.y = SampleTerrainHeight(next) + verticalOffset;
                SetPositionSafe(next);
                transform.rotation = Quaternion.Slerp(transform.rotation,
                    Quaternion.LookRotation(to.normalized), 12f * Time.deltaTime);
            }
            yield return null;
        }

        // The swing is cancelled before it could ever land damage, so the
        // animator is put back rather than left mid-attack.
        if (animator != null) animator.ResetTrigger("Attack");
        _feinting = false;
    }

    private bool _feinting;

    // Gives ground after a swing, so the player has somewhere to answer into.
    private IEnumerator BackOffRoutine(float seconds)
    {
        float t0 = 0f;
        while (t0 < seconds && !isDead && target != null && stunTimer <= 0f && !isPreparingAttack)
        {
            t0 += Time.deltaTime;
            Vector3 away = transform.position - target.position; away.y = 0f;
            if (away.sqrMagnitude > 0.01f)
            {
                Vector3 next = transform.position + away.normalized * (moveSpeed * 0.55f * Time.deltaTime);
                next.y = SampleTerrainHeight(next) + verticalOffset;
                SetPositionSafe(next);
                // Still facing the player while backing off. An enemy that turns
                // its back to reposition reads as fleeing, not as circling.
                transform.rotation = Quaternion.Slerp(transform.rotation,
                    Quaternion.LookRotation(-away.normalized), 10f * Time.deltaTime);
            }
            yield return null;
        }
    }

    private IEnumerator AttackRoutine()
    {
        isPreparingAttack = true;

        // START THE SWING NOW, NOT AT THE END OF THE WIND-UP.
        //
        // The telegraph used to be a colour pulse and nothing else: the enemy ran
        // in, flashed red for half a second, and only then did the Attack trigger
        // fire — so the raise, the commitment and the strike all happened in one
        // instant after it had already stopped. That is why they read as standing
        // next to you and then hitting, rather than as swinging at you.
        //
        // Firing here means the weapon comes up WHILE the enemy is still closing,
        // and the lunge below carries the body into the blow. The damage still
        // lands at the end of the telegraph, which is roughly where these clips
        // put their contact frame, so the hit and the visual agree.
        //
        // The legs deliberately keep running: the old SetBool(false) here stopped
        // them for a frame before the lunge switched them back on, which is a
        // visible hitch at exactly the moment the player is reading the attack.
        // A different swing from this archetype's own pool each time, so a long
        // fight is not one animation on loop and two of the same enemy side by
        // side stop mirroring each other. Swapped before the trigger, never
        // during — mid-swing it would restart the clip on the contact frame.
        if (personality != null) personality.RerollAttack();
        SetAnimIntSafe("AttackIndex", UnityEngine.Random.Range(0, 3));
        if (animator != null) { animator.ResetTrigger("Attack"); animator.SetTrigger("Attack"); }
        // Telegraph growl is the pre-attack roar the player kept hearing
        // continue off a corpse — route it through the tracked vocal so
        // Die()/OnDisable can cut it. Stops any lingering aggro roar too,
        // so the two never overlap into a "double" sound.
        PlayVocal(isBoss ? AudioID.Boss_Roar : AudioID.Enemy_Telegraph);
        // A boss wind-up gets a second, non-vocal layer so it reads through the
        // crowd — the roar alone was being lost under the adds.
        if (isBoss && AudioManager.Instance != null)
            AudioManager.Instance.PlaySFX3D(AudioID.Enemy_Telegraph, transform.position);

        float telegraph = EffectiveTelegraph;
        if (ThreatUI.Instance != null) ThreatUI.Instance.ShowThreat(transform, telegraph + 0.2f);

        if (isElite && playerTarget != null)
        {
            playerTarget.OpenPerfectDodgeWindow(transform, telegraph + 0.6f);

            if (weaponGlintVFX != null && ObjectPoolManager.Instance != null)
                ObjectPoolManager.Instance.SpawnFromPool(weaponGlintVFX, transform.position + Vector3.up * 1.5f, Quaternion.identity);
        }

        if (TutorialHints.Instance != null)
            TutorialHints.Instance.ShowIfNew("CombatTelegraph",
                "TIP: red flash on an enemy = incoming attack. DASH (Space) through it to dodge.", 5f);

        Color baseTele = isEnraged ? Color.black : (isElite ? new Color(1f, 0.5f, 0f) : new Color(1f, 0.15f, 0.05f));
        Color flashTele = Color.white;
        float elapsed = 0f;
        // A heavy swing pulses SLOWER than a light one. The flash rate is the
        // first thing the eye picks up in a crowd, long before the animation
        // reads, so it has to carry the same information the length does.
        float pulseRate = Mathf.Lerp(13f, 5f, Mathf.InverseLerp(0.6f, 1.8f, TelegraphScale));
        Transform body = animator != null ? animator.transform : null;
        Vector3 bodyRest = body != null ? body.localScale : Vector3.one;
        while (elapsed < telegraph)
        {
            elapsed += Time.deltaTime;
            float pulse = Mathf.PingPong(elapsed * pulseRate, 1f);
            SetColor(Color.Lerp(baseTele, flashTele, pulse));

            // The body winds up as well as the colour. A heavy attacker visibly
            // gathers itself — colour alone is a HUD effect painted on a model,
            // and players read silhouettes far faster than they read tints.
            if (body != null && TelegraphScale > 0.9f)
            {
                float k = Mathf.Clamp01(elapsed / Mathf.Max(0.01f, telegraph));
                float swell = 1f + Mathf.Sin(k * Mathf.PI) * 0.06f * (TelegraphScale - 0.9f) * 2f;
                body.localScale = bodyRest * swell;
            }

            // Press the attack IN MOTION instead of freezing: keep facing and
            // lunging toward the player through the wind-up, so the strike lands
            // even if the player edges away and enemies read as aggressive
            // rather than stopping dead to swing.
            if (!isDead && target != null && stunTimer <= 0f)
            {
                Vector3 to = target.position - transform.position; to.y = 0f;
                if (to.sqrMagnitude > 0.0001f)
                {
                    Vector3 dir = to.normalized;
                    transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(dir), 12f * Time.deltaTime);
                    // A step into the blow, NOT a chase.
                    //
                    // At 0.55x speed for the whole telegraph the enemy could
                    // follow a retreating player right through its own wind-up,
                    // which meant backing away from a telegraphed attack bought
                    // nothing — and being unable to answer a tell is what made
                    // fights feel unfair rather than hard. It now closes only the
                    // last of the gap, and stops sooner.
                    if (to.magnitude > attackRange * 0.9f)
                    {
                        Vector3 nextPos = transform.position + dir * (actualMoveSpeed * 0.3f) * Time.deltaTime;
                        nextPos.y = SampleTerrainHeight(nextPos) + verticalOffset;
                        SetPositionSafe(nextPos);
                        // Run the LEGS while lunging so the enemy doesn't slide with
                        // motionless feet during the approach-attack.
                        if (animator != null) animator.SetBool("isMoving", true);
                    }
                    else if (animator != null) animator.SetBool("isMoving", false);
                }
            }
            yield return null;
        }
        ResetColor();

        if (!isDead && Vector3.Distance(transform.position, target.position) <= attackRange + 0.8f)
        {
            lastAttackTime = Time.time;
            // Plant the feet for the blow. The swing itself started back at the
            // top of the wind-up and is mid-clip by now; re-triggering it here
            // would restart the animation on the frame it is supposed to connect.
            if (animator != null) animator.SetBool("isMoving", false);

            // LAND THE BLOW FROM CODE.
            //
            // ExecuteAttackDamage used to be called ONLY by an Animation Event
            // baked into Melee_1H_Attack_Stab. That is a hidden dependency on one
            // specific clip, and the moment anything swapped the attack animation
            // — which the personality layer now does on every swing — the event
            // went with it and enemies stopped dealing any damage at all. Nothing
            // errored; they simply became harmless.
            //
            // The timing is already correct here: this is the end of the
            // telegraph, which is where the damage was always meant to land. The
            // 0.2s guard inside ExecuteAttackDamage means a clip that DOES still
            // carry the event cannot double-hit, so both paths coexist safely.
            ExecuteAttackDamage();
            // Swing / lunge SFX at the moment the animator commits — the
            // telegraph beeps as the wind-up, this reads as the strike.
            // Dedupe: if the animation clip ALSO has an Animation Event
            // that plays Enemy_Attack, our code path plus the event path
            // would fire twice on the same frame. 150ms cooldown keeps
            // legitimate follow-up attacks working.
            if (AudioManager.Instance != null && Time.time - lastAttackSfxTime > ATTACK_SFX_COOLDOWN)
            {
                lastAttackSfxTime = Time.time;
                AudioManager.Instance.PlaySFX3DAttached(AudioID.Enemy_Attack, transform);
                // The weight of a boss swing comes from the slam layered under
                // the generic attack, not from the attack sound alone.
                if (isBoss) AudioManager.Instance.PlaySFX3D(AudioID.Boss_Slam, transform.position);
            }
        }
        yield return new WaitForSeconds(0.2f);
        isPreparingAttack = false;
    }

    // Guards against a single swing landing twice. The animation event that
    // calls this fired twice per attack on some rigs (a duplicate Animator on
    // the model / a state re-entry), so the player took double damage and got
    // two blood splats + two impact "sword" SFX. Real attacks are gated by
    // attackCooldown (>=~0.5s), so ignoring a second call within 0.2s can never
    // drop a legitimate follow-up hit.
    private float lastExecuteDamageTime = -10f;

    public void ExecuteAttackDamage()
    {
        if (Time.time - lastExecuteDamageTime < 0.2f) return;
        lastExecuteDamageTime = Time.time;

        // A swing already in flight when the victory freeze engages must not
        // land on the (control-blocked) player during the reveal cinematic.
        if (GlobalFreeze) return;

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
                AudioManager.Instance.PlaySFX3DAttached(AudioID.Enemy_Hit, transform);
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
        if (!suppressWorldHealthBar && healthCanvas != null && !healthCanvas.activeSelf)
        {
            healthCanvas.SetActive(true);
        }
        targetHealthRatio = currentHealth / maxHealth;
        // -------------------------------------------------------------

        if (AudioManager.Instance != null) AudioManager.Instance.PlaySFX3DAttached(AudioID.Enemy_Hurt, transform);
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
                    // A broken-poise boss reads as a real beat, not a flinch.
                    if (isBoss)
                    {
                        if (AudioManager.Instance != null)
                            AudioManager.Instance.PlaySFX3D(AudioID.Boss_Stagger, transform.position);
                        if (!SetAnimTriggerSafe("Stagger") && animator != null) animator.SetTrigger("Hit");
                        CameraShakeUtil.TryShake(0.25f, 0.12f);
                    }
                    else if (animator != null) animator.SetTrigger("Hit");
                }
            }
        }
    }

    public void MakeInvincibleAndFurious()
    {
        isInvincible = true; isEnraged = true; actualMoveSpeed = moveSpeed * 1.8f;
        if (AudioManager.Instance != null)
        {
            AudioManager.Instance.PlaySFX3D(AudioID.Boss_Enrage, transform.position);
            AudioManager.Instance.NotifyCombat(20f);
        }
        SetAnimTriggerSafe("Enrage");
        CameraShakeUtil.TryShake(0.35f, 0.18f);
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

        // ABSOLUTE BACKSTOP: no matter what any death effect (shatter / dissolve /
        // VFX) does or throws below, the enemy is guaranteed to be removed. This
        // fixes "enemy stays at 0 HP forever" when a death effect errored out.
        Destroy(gameObject, 5f);

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
            animator.SetBoolSafe("isMoving", false); // Змушуємо забути про біг
            animator.SetFloatSafe("Speed", 0f);

            animator.ResetTriggerSafe("Hit");        // Видаляємо всі "застряглі" тригери
            animator.ResetTriggerSafe("Attack");

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
            if (isBoss)
            {
                // A boss death needs to land: the execute hit, then the victory
                // stinger over it.
                AudioManager.Instance.PlaySFX3D(AudioID.Boss_Execute, transform.position);
                AudioManager.Instance.PlaySFX(AudioID.Region_VictoryStinger);
                CameraShakeUtil.TryShake(0.5f, 0.25f);
            }
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

        // Cinematic kills drop nothing: XP crystals and diamonds arcing out of
        // every skeleton read as a gameplay HUD moment in the middle of a
        // trailer shot.
        if (xpCrystalPrefab != null && !suppressDrops)
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

        if (diamondPrefab != null && !suppressDrops && Random.value <= diamondDropChance)
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
        RunSession.AddKill(isElite: isElite, isBoss: isBoss);

        // Physically SHATTER the skeleton into flying chunks the instant it dies
        // — its posed mesh is split by bone and each piece is launched outward.
        // A dust puff accompanies the break. Falls back to the fade for any
        // non-skinned enemy.
        // Guard the shatter: if it ever throws, the enemy must STILL die instead
        // of being stranded at 0 HP (that was the "enemy won't die" bug).
        bool shattered = false;
        try { shattered = SkeletonShatter.Shatter(gameObject, transform.position + Vector3.up * 0.6f, 2.2f); }
        catch (System.Exception e) { Debug.LogWarning($"[EnemyAI] Shatter failed on {name}: {e.Message}"); shattered = false; }

        if (shattered)
        {
            DeathAshEffect.Spawn(transform);
            Destroy(gameObject, 0.15f);   // the model is now independent chunks
            return;
        }

        StartCoroutine(DeathDissolveRoutine());
    }

    private IEnumerator DeathDissolveRoutine()
    {
        yield return new WaitForSeconds(1.5f);

        if (dissolveAshVFX != null) dissolveAshVFX.Play();
        // Procedural "crumble to ash" burst over the body's silhouette — grey
        // flakes + glowing embers that rise and scatter on the wind as the
        // skeleton dissolves. Captured now, before the body shrinks away.
        DeathAshEffect.Spawn(transform);

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
            // Guard: skinned-mesh skeletons have no MeshRenderers, so originalColors
            // is empty — indexing [0] threw and stranded the corpse. Fall back safely.
            Color baseCol = (originalColors != null && originalColors.Length > 0) ? originalColors[0] : Color.white;
            SetColor(Color.Lerp(baseCol, Color.black, t));

            yield return null;
        }

        Destroy(gameObject);
    }

    // ── Obstacle avoidance ───────────────────────────────────────────────
    // Enemies steer purely toward the player, so on region locations they walked
    // straight into houses and clipped through them. Before moving we probe ahead
    // and, when something solid is in the way, deflect the heading until it's
    // clear — so they path AROUND buildings, walls and props.
    [Header("Obstacle avoidance")]
    [Tooltip("Steer around buildings/props instead of walking into them.")]
    public bool avoidObstacles = true;
    [Tooltip("How far ahead to probe (metres).")]
    public float avoidLookAhead = 2.4f;
    [Tooltip("Radius of the probe — roughly the body width.")]
    public float avoidProbeRadius = 0.45f;
    [Tooltip("Height above the feet at which to probe, so the ground itself isn't read as a wall.")]
    public float avoidProbeHeight = 0.9f;
    [Tooltip("Layers treated as solid. Leave as Nothing to probe everything except enemies and the player (terrain is skipped automatically).")]
    public LayerMask obstacleMask;

    private static readonly RaycastHit[] s_avoidBuffer = new RaycastHit[8];

    // Probing costs up to nine spherecasts, and with a horde on screen that is
    // tens of thousands of casts a second if done every frame for every enemy.
    // The deflection is re-solved a few times a second instead and reused in
    // between; the phase is seeded per enemy so a crowd never all solves on the
    // same frame. Movement stays smooth because the deflection is a heading, not
    // a position.
    [Tooltip("How many times a second the obstacle heading is re-solved. 8-12 is indistinguishable from every frame and costs a fraction as much.")]
    public float avoidSolvesPerSecond = 10f;
    private float _nextAvoidSolve;
    private Quaternion _avoidDeflection = Quaternion.identity;

    private int ResolvedObstacleMask()
    {
        if (obstacleMask.value != 0) return obstacleMask.value;
        int m = ~0;
        m &= ~(1 << 2);   // Ignore Raycast
        m &= ~(1 << 9);   // other enemies — the repulsion pass already handles crowding
        return m;
    }

    private Vector3 SteerAroundObstacles(Vector3 pos, Vector3 dir)
    {
        if (!avoidObstacles) return dir;
        dir.y = 0f;
        if (dir.sqrMagnitude < 0.0001f) return dir;
        dir.Normalize();

        if (Time.time >= _nextAvoidSolve)
        {
            _nextAvoidSolve = Time.time + 1f / Mathf.Max(1f, avoidSolvesPerSecond);
            _avoidDeflection = SolveDeflection(pos, dir);
        }
        return _avoidDeflection * dir;
    }

    // Returns the rotation to apply to the desired heading to get a clear one.
    // Storing a ROTATION rather than a direction means the cached answer stays
    // correct as the enemy turns between solves.
    private Quaternion SolveDeflection(Vector3 pos, Vector3 dir)
    {
        Vector3 origin = pos + Vector3.up * avoidProbeHeight;
        if (!ProbeBlocked(origin, dir)) return Quaternion.identity;

        // Fan out to either side until a clear heading is found. Alternating
        // left/right keeps the deflection minimal, so they hug the wall and
        // slip past a corner instead of turning around.
        for (int step = 1; step <= 4; step++)
        {
            float a = step * 25f;
            var lq = Quaternion.Euler(0f, -a, 0f);
            if (!ProbeBlocked(origin, lq * dir)) return lq;
            var rq = Quaternion.Euler(0f, a, 0f);
            if (!ProbeBlocked(origin, rq * dir)) return rq;
        }
        // Boxed in — slide sideways rather than grinding into the wall.
        return Quaternion.Euler(0f, 90f, 0f);
    }

    private bool ProbeBlocked(Vector3 origin, Vector3 dir)
    {
        int n = Physics.SphereCastNonAlloc(origin, avoidProbeRadius, dir.normalized, s_avoidBuffer,
                                           avoidLookAhead, ResolvedObstacleMask(), QueryTriggerInteraction.Ignore);
        for (int i = 0; i < n; i++)
        {
            Collider c = s_avoidBuffer[i].collider;
            if (c == null) continue;
            if (c is TerrainCollider) continue;                  // the ground is not a wall
            if (c.transform == transform || c.transform.IsChildOf(transform)) continue;
            if (c.CompareTag("Player")) continue;                // never dodge the target
            if (c.GetComponentInParent<EnemyAI>() != null) continue;
            return true;
        }
        return false;
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