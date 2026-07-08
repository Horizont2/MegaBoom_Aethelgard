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
    private Animator animator;
    private bool isDead = false;

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

    private static float SampleTerrainHeight(Vector3 worldPos)
    {
        Terrain t = CachedTerrain;
        if (t == null) return worldPos.y;
        return t.SampleHeight(worldPos) + s_terrainOriginY;
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
            if (dynamicMultiplier > 1.4f) actualMoveSpeed *= 1.15f;

            xpRewardMultiplier = region.enemyHpMultiplier * dynamicMultiplier * timeMultiplier * 0.5f;
        }
        else
        {
            maxHealth *= timeMultiplier;
            damage *= timeMultiplier;
        }

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

            // 2. Фікс повороту: Канвас ворога завжди дивиться прямо в камеру
            if (mainCamTransform != null)
            {
                healthCanvas.transform.rotation = mainCamTransform.rotation;
            }
            else if (Camera.main != null)
            {
                healthCanvas.transform.rotation = Camera.main.transform.rotation;
            }
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
                transform.position = nextPos;
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
                transform.position = nextPos;
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
            transform.position = nextPos;

            if (finalDirection != Vector3.zero)
            {
                transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(finalDirection), 10f * Time.deltaTime);
                if (animator != null) animator.SetBool("isMoving", true);
            }
        }

        UpdateFootstepAudio();
    }

    // Track XZ distance travelled between Update ticks and fire a
    // spatial footstep clip once we cross the stride threshold. Gated
    // by squared distance to the player so a crowd of far-away skeletons
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
        AudioManager.Instance.PlaySFX3D(AudioID.Enemy_Footstep, pos);
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
            transform.position = nextPos;
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
        // doesn't chorus at once. 3D at enemy position for spatial cue.
        if (AudioManager.Instance != null)
            AudioManager.Instance.PlaySFX3D(AudioID.Enemy_Agro, transform.position);
    }

    private void CheckNightBuff()
    {
        if (dayNightCycle == null || isEnraged) return;
        bool isNight = dayNightCycle.timeOfDay < 5f || dayNightCycle.timeOfDay > 19f;

        if (isNight && !isNightBuffActive) { isNightBuffActive = true; actualMoveSpeed = baseActualMoveSpeed * nightMultiplier; damage = baseDamage * nightMultiplier; }
        else if (!isNight && isNightBuffActive) { isNightBuffActive = false; actualMoveSpeed = baseActualMoveSpeed; damage = baseDamage; }
    }

    private IEnumerator AttackRoutine()
    {
        isPreparingAttack = true;
        if (animator != null) animator.SetBool("isMoving", false);
        if (AudioManager.Instance != null) AudioManager.Instance.PlaySFX3D(AudioID.Enemy_Telegraph, transform.position);

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
            if (AudioManager.Instance != null)
                AudioManager.Instance.PlaySFX3D(AudioID.Enemy_Attack, transform.position);
        }
        yield return new WaitForSeconds(0.2f);
        isPreparingAttack = false;
    }

    public void ExecuteAttackDamage()
    {
        if (isDead || playerTarget == null || playerTarget.currentHealth <= 0) return;
        if (Vector3.Distance(transform.position, target.position) <= attackRange + 1f)
        {
            playerTarget.TakeDamage(new DamageInfo { Amount = damage, PushDirection = transform.forward });
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

            // Якщо смерть все одно іноді не програється (через складні transitions),
            // заміни рядок нижче на: animator.Play("Die", 0, 0f);
            animator.SetTrigger("Die");
        }

        if (AudioManager.Instance != null) AudioManager.Instance.PlaySFX3D(AudioID.Enemy_Die, transform.position);

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
            Instantiate(xpCrystalPrefab, transform.position + Vector3.up * 1f, Quaternion.identity);
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
}