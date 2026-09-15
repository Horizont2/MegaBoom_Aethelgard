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

    [Header("Caster pacing")]
    [Tooltip("Shots fired back-to-back before the caster pauses. A ranged enemy with one flat cooldown is a turret; a burst-and-rest gives the player a window to close in.")]
    public int castsPerBurst = 3;
    [Tooltip("Seconds the caster holds fire after a burst.")]
    public float castRestSeconds = 2.6f;

    private int _castsInBurst;
    private float _castRestUntil;

    [Header("Skeleton Mage")]
    [Tooltip("Casts a flying magic orb instead of meleeing. Auto-enabled for enemies whose name contains 'mage'. Uses ranged behavior; the orb is built at runtime, no prefab needed.")]
    public bool magicCaster = false;
    [Tooltip("Color of the mage's magic orb + its glow/trail.")]
    public Color magicOrbColor = new Color(0.55f, 0.35f, 1f);
    [Tooltip("Diameter of the mage's orb, in metres. It was a hard-coded 0.5, which is large next to characters at this scale — the bolt read as a boulder.")]
    // Was 0.34, which at the range these are fired from is a beach ball with a
    // point light in it — it covers the enemy that threw it and reads as an
    // explosion rather than a projectile you are supposed to dodge.
    [Range(0.08f, 0.8f)] public float magicOrbSize = 0.2f;

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

    // ==== A WIND-UP SHORTER THAN A REACTION IS NOT A TELL ====
    //
    // The archetype scales produced a Rogue wind-up of 0.65 * 0.62 = 0.40s and a
    // Minion's of 0.51s. Human reaction to a visual cue is around 0.25s, and the
    // parry window sits at the END of the wind-up — so against a Rogue the
    // window opened at 0.22s, BEFORE the player could have reacted to the tell
    // at all. Parrying the fast archetypes was not difficult, it was impossible
    // by reaction and could only be guessed.
    //
    // An attack the player cannot answer is not a mechanic, it is just damage
    // arriving. The floor is what makes every enemy in the game readable; the
    // scales above still decide who is quick and who is ponderous, they just do
    // it above the line where a human can participate.
    [Tooltip("Shortest wind-up any enemy may have, in seconds. Reaction to a visual cue is about 0.25s and the parry window sits at the end of the wind-up, so anything below roughly 0.5 cannot be answered on reaction — only guessed.")]
    public float minTelegraphTime = 0.55f;

    public float EffectiveTelegraph => Mathf.Max(minTelegraphTime, attackTelegraphTime * TelegraphScale);
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
        // A pooled enemy comes back believing whatever it believed when it went
        // away; if that was "engaged", it never re-registers with the ring and
        // is invisible to the slot count for the rest of its life.
        _reportedEngaged = false;
        _ringHeading = Vector3.zero;
        _chaseHeading = Vector3.zero;
        UnityEngine.SceneManagement.SceneManager.activeSceneChanged += OnActiveSceneChanged;
    }

    private void OnDisable()
    {
        // Give the attack slot back. A dead or pooled enemy still holding one
        // means the crowd is quietly allowed fewer attackers than it should be,
        // and after a few fights nobody can swing at all.
        if (CombatRing.Instance != null) CombatRing.Instance.ReportDisengaged(this);
        ParryCue.Cancel(this);
        _reportedEngaged = false;

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
    // Fourteen metres was a short leash for a top-down camera: the player can
    // see an enemy long before it notices them, walk up and take the first
    // swing, every time. Eighteen means being SEEN is part of approaching.
    // Sight is still required — see CanSeeTarget — so this widens notice, not
    // omniscience.
    [HideInInspector] public float aggroRange = 18f;
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
        SetMovingAnim(true);

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

        SetMovingAnim(false);
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
            //
            // ==== BUT NOT FOR AN ENEMY A HUNDRED METRES AWAY ====
            //
            // This block sits ABOVE the distance throttle below, and the canvas
            // is switched on at first damage and stays on until death — so once
            // a fight starts, every damaged enemy in the region billboards its
            // own world-space Canvas every single frame regardless of range.
            // The throttle exists precisely to make crowds affordable and this
            // walked straight past it, and each write dirties a separate
            // world-space canvas, which do not batch with each other.
            //
            // A bar you cannot read is not worth a draw call: past the cutoff it
            // is switched off entirely and comes back when the player returns.
            if (mainCamTransform == null) mainCamTransform = CameraCache.MainTransform;
            if (mainCamTransform != null)
            {
                float sqrToCam = (mainCamTransform.position - transform.position).sqrMagnitude;
                if (sqrToCam > healthBarCullDistance * healthBarCullDistance)
                {
                    healthCanvas.SetActive(false);
                }
                else
                {
                    healthCanvas.transform.rotation = mainCamTransform.rotation;
                }
            }
        }
        else if (healthCanvas != null && !healthCanvas.activeSelf && _healthBarWanted && mainCamTransform != null)
        {
            // Came back into range — restore the bar the cull switched off.
            float sqr = (mainCamTransform.position - transform.position).sqrMagnitude;
            if (sqr <= healthBarCullDistance * healthBarCullDistance) healthCanvas.SetActive(true);
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
            SetMovingAnim(false);
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
            if (isCinematicFrozen && !cinematicDrivesAnimator)
                SetMovingAnim(true);
            return;
        }

        if (stunTimer > 0 && !isEnraged)
        {
            stunTimer -= Time.deltaTime;
            SetMovingAnim(false);
            return;
        }

        if (isPreparingAttack) return;

        // ==== ONE SYSTEM OWNS THE TRANSFORM AT A TIME ====
        //
        // THIS is what was left of the zigzag, and it was never in the steering.
        //
        // Three coroutines move this enemy every frame of their own accord:
        // FeintRoutine steps toward the player, RecoilRoutine is knocked away,
        // BackOffRoutine walks away after a shot. None of them stopped the
        // movement code below from running in the same frame — so two systems
        // wrote the position each tick, in opposite directions, through two
        // CharacterController.Move calls. The body lands somewhere between them,
        // and the place between them changes every frame. That is a vibration,
        // and no amount of easing in either system could damp it, because they
        // were not disagreeing about a heading — they were fighting for the
        // transform itself.
        //
        // The feint had a flag for exactly this. It was set, it was cleared, and
        // it was never read anywhere. The other two had no flag at all.
        //
        // Whichever system is driving, drives alone.
        if (_scriptedMove) return;

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
            if (neighbor == null || neighbor.isTrigger) continue;

            // ==== AN ENEMY WAS PUSHING ITSELF AROUND ====
            //
            // This compared gameObject only, but a collider usually sits on a
            // CHILD of the enemy root — and on these rigs some of them hang off
            // animated bones. So an enemy counted its own hitboxes as
            // neighbours, and because those bones move with the run cycle, the
            // push it computed swung left and right in time with its own
            // footsteps. Every frame it steered away from where its own arm had
            // just been.
            //
            // A whole-hierarchy test costs one extra comparison and removes a
            // wobble that was locked to the animation, which is exactly the kind
            // that looks deliberate and unnatural at the same time.
            Transform nt = neighbor.transform;
            if (nt == transform || nt.IsChildOf(transform)) continue;

            Vector3 pushDir = currentPos - nt.position;
            pushDir.y = 0f;
            float distance = pushDir.magnitude;
            if (distance < repulsionRadius && distance > 0.001f)
                repulsion += pushDir / distance * (repulsionRadius - distance);
        }

        float distanceToPlayer = Vector3.Distance(transform.position, target.position);

        // ==== THE RING IS ASKED BEFORE THE DISTANCE CLOSES, NOT AFTER ====
        //
        // All of this used to live INSIDE the `distanceToPlayer <= attackRange *
        // 1.5f` branch below, which meant the crowd control only had an opinion
        // once an enemy was already standing on top of the player. Everything
        // further out — which is where the ring's posts actually are, at three to
        // six metres — fell straight through to the plain chase at the bottom of
        // this method and beelined in.
        //
        // So every enemy arrived in the huddle FIRST and was only then told to
        // wait its turn, by which point it was already inside the player's guard
        // with five others. The ring was built, posted and never occupied, and
        // from the player's seat the fight was still a scrum with a shield
        // bouncing off it. That is the "вороги стоять впритул" report, and it was
        // never a tuning problem — the waiters were never given the chance to
        // wait anywhere but in your face.
        //
        // Deciding here, while there is still ground between them and the player,
        // is the whole fix.
        var ring = CombatRing.Instance;
        float ringOuter = ring != null ? ring.outerRadius : 0f;

        // Two bands, deliberately different sizes. The fight is joined generously
        // so the slot count reflects the real size of the crowd; the ring itself
        // is tighter, so an enemy still crossing open ground just runs.
        bool inFight = ring != null && distanceToPlayer <= ringOuter + 6f;
        bool inRingBand = ring != null && distanceToPlayer <= ringOuter + 2f;

        // Only on the edges, not every frame — ReportDisengaged also hands back
        // any slot, and calling it continuously for every enemy still crossing
        // the map is churn for nothing.
        if (ring != null && inFight != _reportedEngaged)
        {
            _reportedEngaged = inFight;
            if (inFight) ring.ReportEngaged(this);
            else ring.ReportDisengaged(this);
        }

        // A boss is never a member of a crowd — see CombatRing.RequestAttack.
        bool hasSlot = ring == null || isBoss || ring.IsAttacking(this);

        // Only ASK once close enough to use the answer. A token claimed from
        // twelve metres out would expire on the walk in, and would be held —
        // and therefore denied to someone in range — the whole way.
        //
        // RequestSlot, not RequestAttack: this decides the ROLE, and the role
        // must not be revoked by the crowd's swing rhythm. It used to be, and
        // the consequence was severe — every time any enemy anywhere started a
        // swing, everyone else was told they were no longer an attacker,
        // turned around and walked back to their ring post, then turned again
        // 0.85s later. See the note on CombatRing.MayStrikeNow.
        if (!hasSlot && inRingBand && Time.time >= lastAttackTime + attackCooldown)
            hasSlot = ring.RequestSlot(this);

        // Anyone in the band without a token holds the ring instead of closing.
        bool holdRing = ring != null && inRingBand && !hasSlot;

        if (holdRing)
        {
            HoldRingPost(currentPos, repulsion, ring);
            return;
        }

        if (distanceToPlayer <= attackRange * 1.5f)
        {
            if (directionToPlayer != Vector3.zero)
            {
                Vector3 lookDir = directionToPlayer; lookDir.y = 0;
                transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(lookDir), 15f * Time.deltaTime);
            }

            bool isAttackReady = Time.time >= lastAttackTime + attackCooldown;

            // The token was already resolved above, before the approach — the
            // enemies without one never reach this branch at all, they are
            // circling out at their posts.

            // Commit only once genuinely in range.
            //
            // This used to fire at 1.15x, on the theory that the wind-up lunge
            // would cover the last of the gap. In practice the two compounded:
            // the enemy started its swing well short AND then chased the player
            // through the whole telegraph, so backing off did nothing and hits
            // seemed to land from outside the range the player could see. Wind up
            // on arrival, not on approach.
            // The rhythm is asked HERE, at the swing, and nowhere else. Failing
            // it means "wait a beat", not "stop being an attacker" — so the
            // enemy holds its ground in the footwork branch below instead of
            // abandoning its approach.
            bool rhythmOk = CombatRing.Instance == null || CombatRing.Instance.MayStrikeNow();

            if (isAttackReady && distanceToPlayer <= attackRange && hasSlot && rhythmOk)
            {
                _attackCo = StartCoroutine(AttackRoutine());
            }
            else if (isAttackReady && distanceToPlayer > attackRange && hasSlot)
            {
                SetMovingAnim(true);

                // Closing the last metre: same rule as the charge. Straight at
                // the player, separation applied afterwards as a slide.
                Vector3 closeWanted = SteerAroundObstacles(currentPos, directionToPlayer);

                _chaseHeading = _chaseHeading == Vector3.zero
                    ? closeWanted
                    : Vector3.Slerp(_chaseHeading, closeWanted, 12f * Time.deltaTime);

                Vector3 moveDir = _chaseHeading.sqrMagnitude > 0.0001f ? _chaseHeading.normalized : closeWanted;
                Vector3 nextPos = currentPos + moveDir * actualMoveSpeed * Time.deltaTime
                                + SeparationStep(repulsion);

                nextPos.y = SampleTerrainHeight(nextPos) + verticalOffset;
                SetPositionSafe(nextPos);
            }
            else
            {
                // ==== THIS IS THE "RUNNING LEFT AND RIGHT" ====
                //
                // Reported five times, and it was never the ring: it is here.
                // This branch runs when an enemy is inside melee range, HOLDS an
                // attack token, and is waiting out its cooldown — which is about
                // two seconds of every 2.1, so it is where a front-line attacker
                // spends most of its life.
                //
                // And it moved PURELY SIDEWAYS: Cross(up, directionToPlayer) is
                // a tangent, with only a small in/out correction added to it. At
                // 70% of run speed, facing the player the whole time, with no
                // strafe animation in the set — a forward-run clip playing while
                // the body slides crabwise, one metre from the player's face.
                // The unclamped repulsion on top of it meant two attackers
                // standing close flipped each other's direction as they jostled,
                // which is the left-right oscillation specifically.
                //
                // An attacker waiting for its cooldown should HOLD ITS GROUND.
                // The only movement it needs is closing or backing off to keep
                // its reach, and that is forward-and-back — the one axis the
                // animation set can actually show.
                float ideal = attackRange * 0.75f;
                float off = distanceToPlayer - ideal;

                // Deadzone: at the right distance it simply stands there. A
                // creature adjusting its footing every frame reads as jitter.
                // Crowding no longer drags it out of the deadzone — the slide
                // below handles that without pretending it is footwork.
                //
                // Hysteresis, because a single threshold is not a deadzone when
                // the PLAYER is the thing moving: strafing round an enemy walks
                // the distance back and forth across 0.35m several times a
                // second, and the legs started and stopped with it. It takes a
                // real gap to set the feet going, and a smaller one to settle.
                float startAt = _footworkActive ? 0.22f : 0.6f;
                bool needsFootwork = Mathf.Abs(off) >= startAt;
                _footworkActive = needsFootwork;

                SetMovingAnim(needsFootwork, actualMoveSpeed * 0.45f);

                Vector3 footwork = needsFootwork
                    ? SteerAroundObstacles(currentPos, directionToPlayer * Mathf.Sign(off)) * (actualMoveSpeed * 0.45f) * Time.deltaTime
                    : Vector3.zero;

                Vector3 slide = SeparationStep(repulsion);
                if (footwork != Vector3.zero || slide != Vector3.zero)
                {
                    Vector3 nextPos = currentPos + footwork + slide;
                    nextPos.y = SampleTerrainHeight(nextPos) + verticalOffset;
                    SetPositionSafe(nextPos);
                }
            }
        }
        else
        {
            // ==== STRAIGHT AT THE PLAYER. THAT IS THE WHOLE HEADING. ====
            //
            // What used to be here: the direction to the player, plus a capped
            // crowd push, plus a Perlin wander, all summed and normalised, then
            // eased. Six rounds of tuning went into those two extra terms and
            // the charge still did not read as a charge, because both of them
            // move the heading — and the body faces the heading. An enemy that
            // is running at you does not need a reason to look like it is not.
            //
            // Separation happens after the move now (see SeparationStep), and
            // the wander is gone. A pack advancing on the same line is fine:
            // they arrive at different times, they have different speeds, and
            // the ones without a slot stop short. That is variety the player can
            // read, rather than noise that reads as a bug.
            Vector3 wanted = SteerAroundObstacles(currentPos, directionToPlayer);
            wanted.y = 0f;

            // Still eased, but only to soften the turn when an obstacle forces
            // one. With nothing in the way this settles onto the line to the
            // player and stays there.
            _chaseHeading = _chaseHeading == Vector3.zero
                ? wanted
                : Vector3.Slerp(_chaseHeading, wanted, 9f * Time.deltaTime);

            Vector3 finalDirection = _chaseHeading.sqrMagnitude > 0.0001f ? _chaseHeading.normalized : wanted;

            Vector3 nextPos = currentPos + finalDirection * actualMoveSpeed * Time.deltaTime
                            + SeparationStep(repulsion);
            nextPos.y = SampleTerrainHeight(nextPos) + verticalOffset;
            SetPositionSafe(nextPos);

            if (finalDirection != Vector3.zero)
            {
                transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(finalDirection), 10f * Time.deltaTime);
                SetMovingAnim(true);
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
        // Resolved once. NameToLayer is a string lookup and this runs four times
        // a second PER ENEMY; SampleTerrainHeight in this same file already
        // caches its mask exactly this way.
        if (s_losBlockers == int.MinValue)
        {
            int m = 0;
            int def = LayerMask.NameToLayer("Default");   if (def >= 0) m |= 1 << def;
            int obs = LayerMask.NameToLayer("Obstacles"); if (obs >= 0) m |= 1 << obs;
            int nat = LayerMask.NameToLayer("Nature");    if (nat >= 0) m |= 1 << nat;
            s_losBlockers = m;
        }
        int blockers = s_losBlockers;
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

    private float _animSpeed;

    private void SetMovingAnim(bool moving, float speed)
    {
        if (animator == null || !animator.enabled) return;
        animator.SetBoolSafe("isMoving", moving);

        // ==== EASE THE BLEND VALUE, DO NOT SNAP IT ====
        //
        // The branches move at different paces — a full charge, then footwork at
        // 45%, then a standstill — and writing those numbers straight into Speed
        // makes the locomotion tree JUMP between clips at every branch change.
        // That is a pop, and it is what reads as the animation switching mid-run
        // even when the enemy is doing something perfectly sensible.
        //
        // A blend tree exists to cross-fade; it just needs a value that travels.
        float target = moving ? speed : 0f;
        _animSpeed = Mathf.MoveTowards(_animSpeed, target,
                                       Mathf.Max(2f, actualMoveSpeed * 3.5f) * Time.deltaTime);
        animator.SetFloatSafe("Speed", _animSpeed);
    }

    // ==== isMoving IS ONLY HALF OF THE ANIMATOR ====
    //
    // The locomotion tree blends on Speed; isMoving only decides whether to be
    // in it at all. Every combat branch used to write the bool directly and
    // leave Speed at whatever the last PATROL tick put there — passiveSpeed,
    // which is 40% of a walk. So an enemy charging at full speed played a
    // stroll, and the pose changed as it crossed between branches even though
    // its travel speed had not changed at all. That is the "switches to some
    // strange animation while running" report, and no amount of steering work
    // was ever going to fix it.
    //
    // Everything now goes through here, and here always sets both.
    private void SetMovingAnim(bool moving) => SetMovingAnim(moving, moving ? actualMoveSpeed : 0f);

    // ==== SEPARATION IS A NUDGE, NOT A DIRECTION ====
    //
    // Every previous attempt at the zigzag capped the crowd push, eased it, or
    // reduced it — and it kept coming back, because all of them left it inside
    // the HEADING. The heading is what the body rotates to face, so any amount
    // of it makes the enemy turn toward wherever its neighbours are not, and
    // neighbours shuffle constantly. Capping it only decided how far the enemy
    // turned, never whether it turned at all.
    //
    // So it is out of the heading entirely. The enemy decides where it is going
    // — the player, or its holding distance — and goes there in a straight
    // line. Bodies still stop overlapping, because afterwards they are slid
    // apart as a position correction. A correction cannot rotate anything, and
    // it cannot bend the path: it can only stop two enemies occupying one spot.
    //
    // This is the standard separation-after-steering split, and it is what
    // should have been done the first time.
    private Vector3 SeparationStep(Vector3 repulsion)
    {
        if (repulsion.sqrMagnitude < 0.0001f) return Vector3.zero;
        return Vector3.ClampMagnitude(repulsion, 1f) * (separationSpeed * Time.deltaTime);
    }

    [Tooltip("Metres per second an enemy slides sideways to stop overlapping another. This never steers — it only un-stacks bodies after the move.")]
    public float separationSpeed = 1.8f;

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
        // ==== NOT EVERY ENEMY ANNOUNCES ITSELF ====
        //
        // The shared vocal gate is 0.22s, which stops a crowd barking on ONE
        // frame and does nothing about the rate: twenty enemies spotting the
        // player still produced four and a half barks a second for four
        // straight seconds. And BeginSearch clears isAggroed, so an enemy that
        // loses sight behind a tree and finds the player again barks afresh
        // every time — a player weaving through a forest is followed by a
        // continuous chorus.
        //
        // A bark exists to tell the player "you have been seen". One is
        // information; twenty is noise that also drowns the attack telegraphs,
        // which are the sounds that actually matter now that blocking is timed
        // off them. So it gets its own long gate, and only the first enemy in a
        // group through that gate says anything.
        if (isBoss || Time.time - s_lastAggroBark >= AGGRO_BARK_INTERVAL)
        {
            if (!isBoss) s_lastAggroBark = Time.time;
            PlayVocal(isBoss ? AudioID.Boss_Roar : AudioID.Enemy_Agro);
        }
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

    // Separate, much longer gate for the "I have seen you" bark specifically.
    // The 0.22s gate above is about not stacking sounds on one frame; this is
    // about how OFTEN the crowd is allowed to say the same thing. See Aggro().
    [Tooltip("Metres past which a damaged enemy's health bar is switched off entirely. A bar you cannot read is not worth a world-space canvas and a draw call.")]
    public float healthBarCullDistance = 35f;
    // Set when the bar is first shown, so the cull knows whether an off canvas
    // is off because it was culled or because the enemy was never hurt.
    private bool _healthBarWanted;

    private static bool s_showDamagePopups = true;
    private static float s_popupPrefRefresh = -1f;

    // int.MinValue = not resolved yet; 0 is a legitimate answer (no such layers).
    private static int s_losBlockers = int.MinValue;

    private static float s_lastAggroBark = -10f;
    private const float AGGRO_BARK_INTERVAL = 3.5f;

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

        // ==== CASTERS ARE PART OF THE FIGHT, NOT A SEPARATE METRONOME ====
        //
        // This whole branch bypassed CombatRing: no token, no global rhythm, no
        // hesitation after a block. A mage therefore fired on nothing but its
        // own cooldown, forever, through parries and staggers and everything
        // else the crowd was doing — which is the "безкінечно атакує" report,
        // and it also quietly cancelled the reward for parrying, since a caster
        // kept the pressure up while the melee ring was flinching.
        //
        // It does NOT take a melee token: a caster holding one would starve the
        // enemies actually standing in front of the player. It respects the
        // RHYTHM instead — the shared gap between any two attacks starting, and
        // the pause the whole crowd takes after a successful block.
        var ring = CombatRing.Instance;
        bool rhythmAllows = ring == null || (!ring.Hesitating && ring.SwingWindowOpen);

        // And it rests. A caster with one flat cooldown reads as a turret; one
        // that fires a short burst and then pauses gives the player a window to
        // close the distance in, which is the only counterplay a ranged enemy
        // can offer.
        bool resting = Time.time < _castRestUntil;

        bool ready = Time.time >= lastAttackTime + attackCooldown;
        if (ready && rhythmAllows && !resting && dist <= preferredRange * 1.35f)
        {
            SetMovingAnim(false);
            if (ring != null) ring.NoteSwingStarted();

            if (++_castsInBurst >= castsPerBurst)
            {
                _castsInBurst = 0;
                _castRestUntil = Time.time + castRestSeconds;
            }

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
            SetMovingAnim(true);
        }
        else SetMovingAnim(false);
    }

    private IEnumerator RangedAttackRoutine()
    {
        isPreparingAttack = true;
        SetMovingAnim(false);
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
        // Released WITH a wait. Handing the token back clean let this enemy
        // immediately re-request it, or the next one take it the same frame,
        // which is how three attackers cycled through inside two seconds.
        if (CombatRing.Instance != null) CombatRing.Instance.Release(this, penalise: true, extraWait: 0.5f);
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

    // Animator.parameters allocates a fresh array on every call. This runs once
    // per ranged enemy per frame (the "Aim" pose), so a group of archers was
    // feeding gen-0 continuously. Resolved once per parameter and remembered.
    private readonly System.Collections.Generic.Dictionary<string, bool> _boolParamCache
        = new System.Collections.Generic.Dictionary<string, bool>(8);

    private void SetAnimBoolSafe(string param, bool value)
    {
        if (animator == null) return;

        if (!_boolParamCache.TryGetValue(param, out bool present))
        {
            present = false;
            foreach (var p in animator.parameters)
                if (p.type == AnimatorControllerParameterType.Bool && p.name == param) { present = true; break; }
            _boolParamCache[param] = present;
        }
        if (present) animator.SetBool(param, value);
        return;
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

    private static readonly System.Collections.Generic.Dictionary<Color, Material> s_orbMats
        = new System.Collections.Generic.Dictionary<Color, Material>(4);
    private static Shader s_orbShader;

    private static Material OrbMaterial(Color c)
    {
        if (s_orbMats.TryGetValue(c, out var cached) && cached != null) return cached;

        if (s_orbShader == null)
            s_orbShader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");

        var mat = new Material(s_orbShader) { hideFlags = HideFlags.HideAndDontSave };
        if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", c); else mat.color = c;
        mat.EnableKeyword("_EMISSION");
        if (mat.HasProperty("_EmissionColor")) mat.SetColor("_EmissionColor", c * 3.5f);
        s_orbMats[c] = mat;
        return mat;
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

        // ==== ONE MATERIAL FOR EVERY ORB EVER FIRED ====
        //
        // This built a Material and ran Shader.Find on EVERY SHOT, and
        // EnemyProjectile only ever Destroys the GameObject — so each cast
        // leaked a material, and each cast paid for a string lookup over the
        // whole shader table. A caster firing through a whole raid adds up to
        // hundreds of both.
        //
        // Nothing about the orb varies per shot except its colour, which is a
        // per-archetype constant, so the material is cached by colour and
        // shared. sharedMaterial, not material — the latter would instantiate a
        // per-renderer copy of the very thing being cached.
        var rend = orb.GetComponent<Renderer>();
        rend.sharedMaterial = OrbMaterial(magicOrbColor);
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
        // The trail shares the orb's cached material rather than getting its own
        // — sharedMaterial, so TrailRenderer does not instantiate a copy of it.
        trail.sharedMaterial = OrbMaterial(magicOrbColor);
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
        _scriptedMove = true;

        // In a finally. _scriptedMove switches the movement update off, so a
        // feint that is interrupted — the enemy dies mid-step, the object is
        // pooled, StopAllCoroutines is called — would otherwise leave this
        // enemy unable to move for the rest of its life, standing still in the
        // middle of a fight with nothing in the log to say why.
        try
        {
            if (animator != null) { animator.ResetTrigger("Attack"); animator.SetTrigger("Attack"); }
            PlayVocal(AudioID.Enemy_Telegraph);

            // A short step toward the player and back. Short on purpose — a
            // feint that closes real distance is just an attack that forgot to
            // hit.
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
        }
        finally
        {
            // The swing is cancelled before it could ever land damage, so the
            // animator is put back rather than left mid-attack.
            if (animator != null) animator.ResetTrigger("Attack");
            _feinting = false;
            _scriptedMove = false;
        }
    }

    private bool _feinting;

    // Set by any coroutine that is driving this enemy's transform itself, and
    // read at the top of the movement update. See the note there: without it,
    // two systems wrote the position in the same frame and the body vibrated
    // between them.
    private bool _scriptedMove;

    // The live swing, so a parry can actually stop it. See the note in the
    // parry handler for why the name-based overload could not.
    private Coroutine _attackCo;
    private bool _thisSwingUnblockable;
    // Smoothed circling direction — see the note where it is used.
    private Vector3 _ringHeading;
    // Same, for the straight chase.
    private Vector3 _chaseHeading;
    // Latches that stop a threshold being crossed back and forth by the
    // PLAYER's movement rather than the enemy's own decisions.
    private bool _holdSettled;
    private bool _footworkActive;
    private bool _reportedEngaged;

    // NO SLOT: CLOSE, THEN HOLD.
    //
    // Called from the movement update BEFORE the approach logic, so a waiter
    // holds its distance instead of closing to melee and only then being told
    // to wait there. That ordering is the whole point — see the note at the
    // call.
    private void HoldRingPost(Vector3 currentPos, Vector3 repulsion, CombatRing ring)
    {
        if (ring == null || target == null) return;

        // ==== A WAITER RUNS AT YOU AND STOPS. IT DOES NOT CIRCLE. ====
        //
        // This used to seek a POST — a point on a ring around the player that
        // jumps 20 to 50 degrees round every few seconds. At those radii each
        // jump is a four-metre sideways walk, every enemy on its own timer, and
        // the radius re-rolled on each step too.
        //
        // It was written to stop the ring reading as a queue of statues, and it
        // is the real answer to seven reports of enemies moving strangely. None
        // of the steering fixes could reach it, because it was not noise in the
        // steering — the AI was deliberately walking sideways, and doing it
        // correctly.
        //
        // What the ring is FOR is the slot limit: two or three press at once and
        // the rest wait. That part stays. The choreography goes. A waiter closes
        // in a straight line, stops at its own distance, and watches. Enemies
        // still look different from one another because their distances differ,
        // they arrive at different times, slots rotate between them, and they
        // still feint.
        float hold = ring.HoldDistanceFor(this);

        Vector3 fromPlayer = currentPos - target.position; fromPlayer.y = 0f;
        float distNow = fromPlayer.magnitude;

        // ==== LOSING A SLOT MUST NOT MEAN WALKING AWAY ====
        //
        // THIS is the advancing-and-retreating, and it was never a steering
        // problem. The ring hands slots around every few seconds so the same two
        // enemies do not do all the fighting, which is right. What was wrong is
        // what happened to the one that lost a slot: its hold distance is 3.6 to
        // 6.2 metres and it was standing at about two, so it turned round,
        // walked several metres out, waited out the cooldown, and ran straight
        // back in. Every enemy, every few seconds, for the whole fight.
        //
        // The AI was not jittering. It was correctly executing a round trip
        // nobody wanted, and every leg of it flipped the animation as well.
        //
        // So the hold distance is a target for enemies still ARRIVING. Anything
        // already inside it holds its ground; only standing literally inside the
        // player's swing is worth a step back.
        float standFloor = Mathf.Max(attackRange * 1.15f, ring.innerRadius * 0.5f);

        // Hysteresis on the outer edge too, so a settled waiter does not step
        // forward every time the player drifts half a metre away.
        float advanceAt = _holdSettled ? hold + 1.6f : hold + 0.45f;

        Vector3 desired = Vector3.zero;
        float pace = 1f;

        if (distNow > advanceAt)
        {
            desired = -fromPlayer / Mathf.Max(distNow, 0.001f);   // straight in
            _holdSettled = false;
        }
        else if (distNow < standFloor && distNow > 0.05f)
        {
            // Genuinely inside the player's reach — knocked back, or spawned on
            // top of them. Step out, directly, because an arc would leave it in
            // the player's face for another second or two.
            desired = fromPlayer / distNow;
            pace = 0.75f;
            _holdSettled = false;
        }
        else
        {
            _holdSettled = true;
        }

        Vector3 slide = SeparationStep(repulsion);

        if (desired == Vector3.zero)
        {
            // At its distance: stand and watch. Bodies may still un-stack, but
            // that is a slide, not a walk, so the legs stay still.
            SetMovingAnim(false);
            _ringHeading = Vector3.zero;

            if (slide != Vector3.zero)
            {
                Vector3 settle = currentPos + slide;
                settle.y = SampleTerrainHeight(settle) + verticalOffset;
                SetPositionSafe(settle);
            }

            Vector3 idleToPlayer = target.position - currentPos; idleToPlayer.y = 0f;
            if (idleToPlayer.sqrMagnitude > 0.0001f)
                transform.rotation = Quaternion.Slerp(transform.rotation,
                                                      Quaternion.LookRotation(idleToPlayer.normalized),
                                                      8f * Time.deltaTime);
        }
        else
        {
            Vector3 wanted = SteerAroundObstacles(currentPos, desired);
            _ringHeading = _ringHeading == Vector3.zero
                ? wanted
                : Vector3.Slerp(_ringHeading, wanted, 9f * Time.deltaTime);

            Vector3 heading = _ringHeading.sqrMagnitude > 0.0001f ? _ringHeading.normalized : wanted;
            float speed = actualMoveSpeed * pace;

            Vector3 nextPos = currentPos + heading * speed * Time.deltaTime + slide;
            nextPos.y = SampleTerrainHeight(nextPos) + verticalOffset;
            SetPositionSafe(nextPos);

            SetMovingAnim(true, speed);

            // ALWAYS face the way it is travelling. Keeping its eyes on the
            // player while stepping backwards meant a forward run animation
            // playing while the body moved backwards — there is no backward
            // walk in this animation set, and pretending otherwise is half of
            // what "the animations switch to something strange" was.
            //
            // So a retreating enemy turns, walks out, and turns back when it
            // stops. That is what the animation set can actually show.
            transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(heading), 9f * Time.deltaTime);
        }

        // A waiter still THREATENS. Every few seconds it lunges a step and
        // raises its weapon without swinging — enough that the crowd reads as a
        // pack looking for an opening rather than an audience.
        // ==== ONLY FEINT WHEN STANDING STILL ====
        //
        // A feint triggers the ATTACK animation and steps forward. Fired while
        // the enemy is still walking to its post, that cuts a run animation to
        // an attack animation mid-stride for no reason the player can see —
        // which is most of "they switch to the attack animation while running".
        //
        // A feint is a threat made from a standstill. _holdSettled says the
        // enemy has arrived and is watching, which is exactly when one reads as
        // menace rather than as a glitch.
        if (_holdSettled && ring.ShouldFeint(this, Time.time) && !isPreparingAttack)
            StartCoroutine(FeintRoutine());
    }

    // ==== BEING BLOCKED HURTS ====
    //
    // Recoiling off a shield is what turns a block from a delay into an
    // opening. Without it the player absorbs hits forever and never gets a
    // turn, which is a defensive stance rather than a defensive MECHANIC.
    //
    // A parry additionally marks this enemy vulnerable — extra damage taken
    // for a moment — so the counter-attack the stagger allows is also worth
    // more than an ordinary swing. That is the payoff that makes the read
    // worth learning.
    public void ApplyBlockRecoil(float stagger, float damageMultiplier, float vulnerableFor)
    {
        if (isDead) return;

        stunTimer = Mathf.Max(stunTimer, stagger);
        // The mark goes with the swing. A countdown still running for an attack
        // that was just parried out of existence teaches a timing that is not
        // there any more.
        ParryCue.Cancel(this);
        // ==== THIS WAS CANCELLING NOTHING ====
        //
        // Cancel the swing outright: an enemy that finishes its animation and
        // connects anyway makes the block look like it did nothing.
        //
        // That was the intent, and the line did not do it. StopCoroutine(string)
        // only stops a coroutine that was STARTED by name, and this one is
        // started as StartCoroutine(AttackRoutine()) — so the call matched
        // nothing, the routine ran on, and a parried swing still landed its
        // damage a few frames later. The block system's own contract, silently
        // broken by an overload that fails quietly instead of complaining.
        //
        // Stopping the handle works regardless of how it was started.
        if (_attackCo != null) { StopCoroutine(_attackCo); _attackCo = null; }
        isPreparingAttack = false;
        lastAttackTime = Time.time;

        if (animator != null)
        {
            animator.ResetTrigger("Attack");
            animator.SetTrigger("Hit");
        }
        SetColor(Color.white);

        if (damageMultiplier > 1f && vulnerableFor > 0f)
        {
            _vulnerableMult = damageMultiplier;
            _vulnerableUntil = Time.time + vulnerableFor;
            // MARK THE TARGET, NOT JUST THE SCREEN.
            //
            // The player is told they parried by a flash, a word and a freeze —
            // but none of those say WHO is now open, and in a crowd that is the
            // only part they have to act on. A parried enemy pulses gold for as
            // long as it is vulnerable, so the answer to "where do I swing" is
            // on the enemy itself.
            StartCoroutine(VulnerableGlowRoutine());
        }

        // Shoved back off the shield, so the recoil is visible and not just a
        // pause in the animation.
        if (target != null)
        {
            Vector3 away = transform.position - target.position; away.y = 0f;
            if (away.sqrMagnitude > 0.01f) StartCoroutine(RecoilRoutine(away.normalized, stagger));
        }
    }

    private float _vulnerableMult = 1f;
    private float _vulnerableUntil = -1f;

    public bool IsVulnerable => Time.time < _vulnerableUntil;

    private IEnumerator VulnerableGlowRoutine()
    {
        var gold = new Color(1f, 0.85f, 0.35f);
        while (!isDead && IsVulnerable)
        {
            // Pulsing rather than a steady tint: a static colour on one enemy in
            // a crowd is easy to miss, and it also reads as a status the enemy
            // always had rather than one the player just created.
            float k = 0.45f + 0.55f * Mathf.Abs(Mathf.Sin(Time.unscaledTime * 9f));
            SetColor(Color.Lerp(Color.white, gold, k));
            yield return null;
        }
        if (!isDead) SetColor(Color.white);
    }

    private IEnumerator RecoilRoutine(Vector3 dir, float seconds)
    {
        float t0 = 0f;
        float dur = Mathf.Min(0.25f, seconds);
        // A finally, because a knockback that is interrupted — the enemy dies
        // mid-recoil, the object is disabled — must not leave the movement code
        // switched off for the rest of this enemy's life.
        _scriptedMove = true;
        try
        {
            while (t0 < dur && !isDead)
            {
                t0 += Time.deltaTime;
                Vector3 next = transform.position + dir * (5.5f * (1f - t0 / dur) * Time.deltaTime);
                next.y = SampleTerrainHeight(next) + verticalOffset;
                SetPositionSafe(next);
                yield return null;
            }
        }
        finally { _scriptedMove = false; }
    }

    // Gives ground after a swing, so the player has somewhere to answer into.
    private IEnumerator BackOffRoutine(float seconds)
    {
        float t0 = 0f;
        float pace = moveSpeed * 0.55f;
        _scriptedMove = true;
        try
        {
            while (t0 < seconds && !isDead && target != null && stunTimer <= 0f && !isPreparingAttack)
            {
                t0 += Time.deltaTime;
                Vector3 away = transform.position - target.position; away.y = 0f;
                if (away.sqrMagnitude > 0.01f)
                {
                    Vector3 dir = away.normalized;
                    Vector3 next = transform.position + dir * (pace * Time.deltaTime);
                    next.y = SampleTerrainHeight(next) + verticalOffset;
                    SetPositionSafe(next);

                    // FACE THE WAY IT WALKS. This used to keep its eyes on the
                    // player while travelling backwards, on the theory that
                    // turning away reads as fleeing. It does — but there is no
                    // backward walk in this animation set, so what actually
                    // played was a forward run on a body moving in reverse,
                    // which reads as broken rather than as anything.
                    transform.rotation = Quaternion.Slerp(transform.rotation,
                        Quaternion.LookRotation(dir), 10f * Time.deltaTime);

                    // And the legs move, because the enemy does. Nothing drove
                    // the animator here at all, so a backing-off enemy slid with
                    // whatever pose the previous branch happened to leave.
                    SetMovingAnim(true, pace);
                }
                yield return null;
            }
        }
        finally
        {
            _scriptedMove = false;
            SetMovingAnim(false);
        }
    }

    private IEnumerator AttackRoutine()
    {
        isPreparingAttack = true;
        // Starts the shared rhythm clock, so nobody else may begin a swing for
        // the next fraction of a second. See CombatRing.globalAttackGap.
        if (CombatRing.Instance != null) CombatRing.Instance.NoteSwingStarted();

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

        // ONE SWING IN THREE FROM A HEAVY ENEMY CANNOT BE BLOCKED.
        //
        // Decided at the START of the wind-up, never at the moment of impact,
        // because the whole point is that the player can SEE it coming. An
        // unblockable attack chosen when it lands would be indistinguishable
        // from the shield failing at random, which is the difference between a
        // mechanic and a gotcha.
        _thisSwingUnblockable = (isBoss || isElite) && UnityEngine.Random.value < 0.34f;

        float telegraph = EffectiveTelegraph;

        // ==== ONLY WARN THE PLAYER ABOUT SWINGS AIMED AT THE PLAYER ====
        //
        // An enemy fighting a rescued ally was raising the threat indicator, the
        // parry countdown and the perfect-dodge window on the player's screen,
        // for a blow that was never going to reach them. Across a fight with a
        // companion in it that is a constant stream of marks demanding a
        // reaction to nothing, and it makes the real ones worthless.
        bool aimedAtPlayer = currentTargetDamageable == null
                             || (playerTarget != null && ReferenceEquals(currentTargetDamageable, playerTarget));

        if (aimedAtPlayer)
        {
            if (ThreatUI.Instance != null) ThreatUI.Instance.ShowThreat(transform, telegraph + 0.2f);

            // The parry clock. The colour pulse says an attack is COMING; only
            // this says when to answer it — and it is the one piece of feedback
            // the whole block mechanic was missing. Also the only thing that
            // makes an attack from behind fair, because its mark pins to the
            // screen edge and tells the player which way to turn.
            ParryCue.Show(this, telegraph, PlayerBlock.ParryWindowSecondsFor(this), _thisSwingUnblockable);

            // The swing no shield stops gets its own voice. The colour already
            // says it, but colour is something the player has to be LOOKING at,
            // and this one has to land even when they are watching a different
            // enemy. Falls back to the ordinary telegraph until authored, so
            // nothing is lost in the meantime.
            if (_thisSwingUnblockable) PlayVocal(AudioID.Enemy_Unblockable);

            // ==== NOTHING IN THE GAME EXPLAINED THE SHIELD ====
            //
            // There are 28 authored hints and not one of them mentions block,
            // parry or the guard key. Behind that key sit PlayerBlock, the
            // combat ring, the parry cue and banner, four purchasable shields
            // and a whole stamina economy — and the key itself, Q, appeared in
            // no prompt anywhere in the project.
            //
            // The first swing aimed at the player is the moment it matters:
            // the ring is on screen, closing, and the answer to it is one key.
            if (TutorialHints.Instance != null)
                TutorialHints.Instance.ShowIfNew("Block",
                    "Hold <b>Q</b> to raise your shield. Press it just as the ring closes to PARRY — that " +
                    "staggers the attacker. The guard only covers the FRONT, and pink swings go straight " +
                    "through it.", 8f);

            // MOVED INSIDE aimedAtPlayer. An enemy swinging at a rescued
            // captive was freezing the game for seven seconds to teach the
            // player about a blow that was never coming at them.
            if (TutorialHints.Instance != null)
                TutorialHints.Instance.ShowIfNew("CombatTelegraph",
                    "TIP: red flash on an enemy = incoming attack. DASH (SHIFT) through it to dodge.", 5f);
        }

        if (isElite && playerTarget != null && aimedAtPlayer)
        {
            playerTarget.OpenPerfectDodgeWindow(transform, telegraph + 0.6f);

            if (weaponGlintVFX != null && ObjectPoolManager.Instance != null)
                ObjectPoolManager.Instance.SpawnFromPool(weaponGlintVFX, transform.position + Vector3.up * 1.5f, Quaternion.identity);
        }

        // An unblockable swing announces itself in a colour nothing else uses.
        // The player has to be able to read "shield will not save you" from
        // across the fight, before the swing commits.
        Color baseTele = _thisSwingUnblockable
            ? new Color(0.85f, 0.05f, 0.45f)
            : isEnraged ? Color.black : (isElite ? new Color(1f, 0.5f, 0f) : new Color(1f, 0.15f, 0.05f));
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
                        // motionless feet during the approach-attack — at the
                        // lunge's own pace, not a full charge.
                        SetMovingAnim(true, actualMoveSpeed * 0.3f);
                    }
                    else SetMovingAnim(false);
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
            SetMovingAnim(false);

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
            tgt.TakeDamage(new DamageInfo
            {
                Amount = damage,
                PushDirection = transform.forward,
                SourceName = src,
                // Where the blow comes FROM, so a shield can tell whether it was
                // covering that side. Without it the guard has to guess, and in
                // a crowd it guesses wrong — which is exactly the fight blocking
                // is meant to be for.
                HitPoint = transform.position + Vector3.up * 1.2f,
                Attacker = this,
                // Elites and bosses throw one attack in three that a shield
                // cannot answer, so a raised guard is never the whole answer.
                // The telegraph already differs by archetype (see
                // TelegraphScale); this is the half that has teeth.
                Unblockable = _thisSwingUnblockable,
            });
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

        // A parried enemy takes more for a moment — the reward for the read.
        if (IsVulnerable) info.Amount *= _vulnerableMult;

        currentHealth -= info.Amount;
        if (currentHealth < 0) currentHealth = 0;

        // --- Показуємо ХП та передаємо нове значення для анімації ---
        if (!suppressWorldHealthBar && healthCanvas != null)
        {
            // Remembered so the distance cull can tell a bar it switched off
            // from one that was never meant to be up.
            _healthBarWanted = true;
            if (!healthCanvas.activeSelf) healthCanvas.SetActive(true);
        }
        targetHealthRatio = currentHealth / maxHealth;
        // -------------------------------------------------------------

        if (AudioManager.Instance != null) AudioManager.Instance.PlaySFX3DAttached(AudioID.Enemy_Hurt, transform);
        StartCoroutine(HitFlashRoutine());

        // Cached across enemies. This ran on EVERY hit on EVERY enemy — an AoE
        // swing into a crowd was one marshalled PlayerPrefs lookup per body — to
        // answer a question that changes only when the player opens the settings
        // panel. Refreshed on a timer so it still responds to that.
        if (Time.unscaledTime >= s_popupPrefRefresh)
        {
            s_popupPrefRefresh = Time.unscaledTime + 1f;
            s_showDamagePopups = PlayerPrefs.GetInt("Settings_DamagePopups", 1) == 1;
        }
        bool showPopups = s_showDamagePopups;
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
                    currentPoise = maxPoise;

                    // ==== A STAGGER HAS TO ACTUALLY STOP THE SWING ====
                    //
                    // Clearing isPreparingAttack does not cancel anything. It
                    // only re-opens the early-out at the top of Update, while
                    // AttackRoutine keeps running and lands its damage a few
                    // frames later — so the one read this fight is built to
                    // teach, punish the wind-up, paid out in a damage number
                    // the player cannot see and then hit them anyway. Exactly
                    // the failure the block path already had a note about.
                    //
                    // Worse, re-opening Update while the old routine is alive
                    // lets a SECOND AttackRoutine start over the top of it.
                    //
                    // Only on a real stagger. A chip hit that happens to land
                    // during a wind-up must not cancel it, or every swing in a
                    // crowd would be interrupted by somebody's stray arrow.
                    if (info.StunDuration > 0f)
                    {
                        if (_attackCo != null) { StopCoroutine(_attackCo); _attackCo = null; }
                        ParryCue.Cancel(this);
                        isPreparingAttack = false;
                        if (animator != null) animator.ResetTrigger("Attack");
                    }

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
    private Quaternion _avoidTarget = Quaternion.identity;

    [Tooltip("How many enemies may re-solve their obstacle steering in a single frame, across the whole horde. Anyone over the limit keeps the heading they already had and tries next frame.")]
    public int maxAvoidSolvesPerFrame = 6;
    private static int s_solveFrame = -1;
    private static int s_solvesThisFrame;
    // Which way round the current obstacle this enemy committed to: -1 left,
    // +1 right, 0 nothing in the way. See SolveDeflection.
    private int _avoidSide;

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

        // ==== ONE HORDE, ONE SOLVE BUDGET ====
        //
        // SolveDeflection can fire up to nine spherecasts when a path is blocked,
        // at ten solves a second, PER ENEMY. Forty aggro'd skeletons is a few
        // thousand casts a second, and nothing stopped them all landing on the
        // same frame — which is a spike rather than a cost.
        //
        // A shared per-frame budget spreads them: an enemy that misses its turn
        // keeps steering on the answer it already has, which is exactly what the
        // cached deflection is for, and tries again next frame.
        if (Time.time >= _nextAvoidSolve)
        {
            if (s_solveFrame != Time.frameCount) { s_solveFrame = Time.frameCount; s_solvesThisFrame = 0; }
            if (s_solvesThisFrame < maxAvoidSolvesPerFrame)
            {
                s_solvesThisFrame++;
                _nextAvoidSolve = Time.time + 1f / Mathf.Max(1f, avoidSolvesPerSecond);
                _avoidTarget = SolveDeflection(pos, dir);
            }
        }

        // ==== THE DEFLECTION EASES IN, IT DOES NOT SNAP ====
        //
        // The solve runs a few times a second, and the answer used to be applied
        // the instant it changed — so an enemy rounding a rock jumped its
        // heading by twenty-five degrees at a time, several times a second. Even
        // with a committed side that reads as a stutter rather than a turn.
        // Rotating toward the answer costs nothing and turns the same decisions
        // into a curve.
        _avoidDeflection = Quaternion.RotateTowards(_avoidDeflection, _avoidTarget, 220f * Time.deltaTime);
        return _avoidDeflection * dir;
    }

    // Returns the rotation to apply to the desired heading to get a clear one.
    // Storing a ROTATION rather than a direction means the cached answer stays
    // correct as the enemy turns between solves.
    private Quaternion SolveDeflection(Vector3 pos, Vector3 dir)
    {
        Vector3 origin = pos + Vector3.up * avoidProbeHeight;
        // Path is clear: forget the side we were committed to, so the next
        // obstacle is judged fresh rather than inheriting an old preference.
        if (!ProbeBlocked(origin, dir)) { _avoidSide = 0; return Quaternion.identity; }

        // ==== COMMIT TO A SIDE ====
        //
        // This tried left first every time. Which side is clear changes as the
        // enemy moves, so on any awkward obstacle the answer flipped from left
        // to right and back at the solve rate — and the enemy walked a zigzag
        // instead of walking round the thing. That is the "рухаються дуже дивно
        // зігзагами" report, and it is a decision problem, not a steering one:
        // the choice was correct each time and simply kept being re-made.
        //
        // Whichever way it went last time is tried first now, and it only
        // changes sides when its committed side is blocked at every angle. A
        // human going round a rock does the same thing — picks a side and stays
        // with it — and the movement instantly reads as intent rather than
        // indecision.
        int first = _avoidSide != 0 ? _avoidSide : 1;

        for (int pass = 0; pass < 2; pass++)
        {
            int side = pass == 0 ? first : -first;
            for (int step = 1; step <= 4; step++)
            {
                var q = Quaternion.Euler(0f, side * step * 25f, 0f);
                if (!ProbeBlocked(origin, q * dir)) { _avoidSide = side; return q; }
            }
        }

        // Boxed in — slide sideways rather than grinding into the wall, and
        // keep sliding the same way for the same reason as above.
        _avoidSide = first;
        return Quaternion.Euler(0f, first * 90f, 0f);
    }

    // ==== A MUSHROOM IS NOT A WALL ====
    //
    // This is the zigzag, reported four times, and none of the previous fixes
    // could have cured it because they were all about how the enemy REACTS to an
    // obstacle. The real fault is what counts as one.
    //
    // With no explicit mask the probe sweeps every layer but enemies, and this
    // loop then returned true for ANY collider it touched. The generator fills a
    // region with up to three thousand trees, two and a half thousand bushes and
    // mushrooms, twelve hundred rocks, plus dropped pickups and ground clutter —
    // all of them with colliders, because they are harvestable. So an enemy
    // running at the player across ordinary forest floor had something inside its
    // 2.4-metre probe almost every frame, and dutifully swerved around a bush.
    //
    // Size is the test. Something you would walk THROUGH or step OVER is not an
    // obstacle however solid its collider is; a wall, a building or a boulder is.
    [Tooltip("Narrower than this (metres, horizontal) and the enemy walks through it rather than around it. Bushes, mushrooms, dropped loot and saplings are scenery, not walls.")]
    public float minObstacleWidth = 0.9f;
    [Tooltip("Shorter than this (metres) and the enemy steps over it instead of steering. Logs, rubble and low clutter.")]
    public float minObstacleHeight = 0.8f;

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
            // (No GetComponentInParent<EnemyAI> here: the default obstacle mask
            // already excludes layer 9, so walking the parent chain per hit was
            // dead weight on a query that runs up to nine times per solve.)
            if (c.GetComponentInParent<AllyAI>() != null) continue;      // walk past a companion
            if (c.GetComponentInParent<ResourceDrop>() != null) continue; // loot on the floor

            // Big enough to be worth going round?
            Bounds b = c.bounds;
            float width = Mathf.Max(b.size.x, b.size.z);
            if (width < minObstacleWidth) continue;
            if (b.size.y < minObstacleHeight) continue;

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