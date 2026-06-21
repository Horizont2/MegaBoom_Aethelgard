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

    [Header("UI (Health Bar)")]
    public Canvas hpCanvas;
    public Image hpFill;

    [Header("Targeting & Swarm")]
    public Transform target;
    public float verticalOffset = 0.0f;
    public float repulsionRadius = 1.5f;
    public float repulsionForce = 4f;

    [Header("Juice VFX")]
    public GameObject deathVFXPrefab;
    public ParticleSystem dissolveAshVFX;

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
    private PlayerController playerTarget;
    private Animator animator;
    private bool isDead = false;

    private Vector3 knockbackVelocity = Vector3.zero;
    private float stunTimer = 0f;
    private float lastAttackTime;
    private bool isPreparingAttack = false;
    private Transform mainCamTransform;

    // ---- Scene-wide caches (shared by every EnemyAI in the scene) ----
    private static Transform s_player;
    private static PlayerController s_playerController;
    private static Terrain s_terrain;
    private static float s_terrainOriginY;
    private static readonly Collider[] s_overlapBuffer = new Collider[32];

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
        UnityEngine.SceneManagement.SceneManager.activeSceneChanged += OnActiveSceneChanged;
    }

    private void OnDisable()
    {
        UnityEngine.SceneManagement.SceneManager.activeSceneChanged -= OnActiveSceneChanged;
    }

    private static void OnActiveSceneChanged(UnityEngine.SceneManagement.Scene a, UnityEngine.SceneManagement.Scene b)
    {
        // Reset shared caches so the next scene doesn't read stale references.
        s_player = null;
        s_playerController = null;
        s_terrain = null;
        s_terrainOriginY = 0f;
    }

    // --- Passive / Encounter Group Mode (configured by EnemyEncounterGroup) ---
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

    public bool IsAggroed => isAggroed;

    private void Awake()
    {
        gameObject.layer = 9;
        int minimapLayer = LayerMask.NameToLayer("MinimapOnly");

        meshRenderers = GetComponentsInChildren<MeshRenderer>();
        originalColors = new Color[meshRenderers.Length];
        for (int i = 0; i < meshRenderers.Length; i++)
        {
            if (meshRenderers[i].gameObject.layer != minimapLayer) meshRenderers[i].gameObject.layer = 9;
            originalColors[i] = meshRenderers[i].material.color;
        }

        Rigidbody rb = GetComponent<Rigidbody>();
        if (rb == null) rb = gameObject.AddComponent<Rigidbody>();
        rb.isKinematic = true;
        rb.useGravity = false;

        animator = GetComponentInChildren<Animator>();
        if (animator != null) animator.applyRootMotion = false;

        randomOffset = Random.Range(0f, 100f);
        strafeDir = Random.value > 0.5f ? 1f : -1f;
    }

    private void Start()
    {
        if (Camera.main != null) mainCamTransform = Camera.main.transform;
        dayNightCycle = FindFirstObjectByType<DayNightCycle>();
        actualMoveSpeed = moveSpeed * Random.Range(0.8f, 1.2f);

        if (hpCanvas != null) hpCanvas.gameObject.SetActive(false);

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
        UpdateHealthUI();

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

    private void UpdateHealthUI()
    {
        if (hpFill != null) hpFill.fillAmount = currentHealth / maxHealth;
    }

    private void Update()
    {
        if (target == null && !isDead)
        {
            // Use the shared static cache so 30 enemies don't each fire a
            // FindGameObjectWithTag every frame they have no target.
            if (TryGetPlayer(out Transform pt, out PlayerController pc))
            {
                target = pt;
                playerTarget = pc;
            }
            if (target == null) return;
        }

        if (isDead) return;

        if (playerTarget != null && playerTarget.currentHealth <= 0)
        {
            if (animator != null) animator.SetBool("isMoving", false);
            return;
        }

        CheckNightBuff();

        if (currentPoise < maxPoise && stunTimer <= 0) currentPoise += Time.deltaTime * 15f;

        if (hpCanvas != null && mainCamTransform != null && hpCanvas.gameObject.activeSelf)
        {
            hpCanvas.transform.rotation = Quaternion.LookRotation(hpCanvas.transform.position - mainCamTransform.position);
        }

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
    }

    private void UpdatePassiveBehavior()
    {
        Vector3 anchor = anchorTransform != null ? anchorTransform.position : anchorPoint;

        // Aggro check (every 200ms)
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

        // Pick wander target. When roamWhilePassive is false (camp guards) we
        // stick to our personal anchor so nobody walks circles around the fire.
        if (roamWhilePassive)
        {
            if (Time.time >= nextRoamPickTime || Vector3.SqrMagnitude(transform.position - currentRoamTarget) < 0.6f)
            {
                Vector2 r = Random.insideUnitCircle * roamRadius;
                currentRoamTarget = anchor + new Vector3(r.x, 0f, r.y);
                currentRoamTarget.y = SampleTerrainHeight(currentRoamTarget) + verticalOffset;nextRoamPickTime = Time.time + Random.Range(2.5f, 5f);
            }
        }
        else
        {
            currentRoamTarget = anchor;
        }

        Vector3 toTarget = currentRoamTarget - transform.position;
        toTarget.y = 0f;
        float distXZ = toTarget.magnitude;

        // Stationary deadband for camp guards is larger so they don't twitch
        // every frame to "correct" their position by a few cm.
        float standThreshold = roamWhilePassive ? 0.2f : 0.6f;

        if (distXZ > standThreshold)
        {
            Vector3 moveDir = toTarget / distXZ;
            float passiveSpeed = actualMoveSpeed * 0.4f;
            Vector3 nextPos = transform.position + moveDir * passiveSpeed * Time.deltaTime;
            nextPos.y = SampleTerrainHeight(nextPos) + verticalOffset;transform.position = nextPos;
            transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(moveDir), 5f * Time.deltaTime);
            SetMovingAnim(true, passiveSpeed);
        }
        else
        {
            // Standing — optionally turn to face the anchor (campfire) for
            // a natural "huddling around the fire" look.
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
        // Defensive: some skeleton controllers drive blend trees off a Speed float.
        // SetFloat silently no-ops when the parameter doesn't exist.
        animator.SetFloat("Speed", moving ? speed : 0f);
    }

    public void Aggro()
    {
        if (isAggroed) return;
        isAggroed = true;

        // Snap to face the player so the reaction reads instantly.
        if (target != null)
        {
            Vector3 look = target.position - transform.position;
            look.y = 0f;
            if (look.sqrMagnitude > 0.01f)
                transform.rotation = Quaternion.LookRotation(look.normalized);
        }
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
        if (AudioManager.Instance != null) AudioManager.Instance.PlaySFX(AudioID.Enemy_Telegraph);

        // Threat indicator: trigger for every attacker, not just elites.
        // It's the single best readability cue the player has for off-screen
        // hits, so we always raise it during the windup.
        if (ThreatUI.Instance != null) ThreatUI.Instance.ShowThreat(transform, attackTelegraphTime + 0.2f);

        if (isElite && playerTarget != null)
        {
            playerTarget.OpenPerfectDodgeWindow(transform, attackTelegraphTime + 0.6f);

            if (weaponGlintVFX != null && ObjectPoolManager.Instance != null)
                ObjectPoolManager.Instance.SpawnFromPool(weaponGlintVFX, transform.position + Vector3.up * 1.5f, Quaternion.identity);
        }

        // First-time combat tutorial — fires once the very first attack any
        // enemy ever telegraphs against the player.
        if (TutorialHints.Instance != null)
            TutorialHints.Instance.ShowIfNew("CombatTelegraph",
                "TIP: red flash on an enemy = incoming attack. DASH (Space) through it to dodge.", 5f);

        // Pulse the color across the windup instead of holding a static
        // red — the flicker makes the windup readable even mid-melee chaos.
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

        if (hpCanvas != null && !hpCanvas.gameObject.activeSelf) hpCanvas.gameObject.SetActive(true);

        currentHealth -= info.Amount;
        if (currentHealth < 0) currentHealth = 0;
        UpdateHealthUI();

        if (AudioManager.Instance != null) AudioManager.Instance.PlaySFX(AudioID.Enemy_Hurt);
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
        if (meshRenderers == null) return;
        foreach (var r in meshRenderers) if (r != null && r.material != null) r.material.color = c;
    }

    private void ResetColor()
    {
        if (meshRenderers == null || originalColors == null) return;
        for (int i = 0; i < meshRenderers.Length; i++)
            if (meshRenderers[i] != null && meshRenderers[i].material != null) meshRenderers[i].material.color = originalColors[i];
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

        if (hpCanvas != null) hpCanvas.gameObject.SetActive(false);
        if (animator != null) animator.SetTrigger("Die");
        if (AudioManager.Instance != null) AudioManager.Instance.PlaySFX(AudioID.Enemy_Die);

        if (deathVFXPrefab != null)
        {
            GameObject vfx = null;
            if (ObjectPoolManager.Instance != null) vfx = ObjectPoolManager.Instance.SpawnFromPool(deathVFXPrefab, transform.position + Vector3.up * 1f, Quaternion.identity);
            if (vfx == null) Instantiate(deathVFXPrefab, transform.position + Vector3.up * 1f, Quaternion.identity);
        }

        foreach (Collider c in GetComponentsInChildren<Collider>()) c.enabled = false;
        ResetColor();

        // --- ����������� Բ�� ������ ���� (�������� Pool) ---
        if (xpCrystalPrefab != null)
        {
            Instantiate(xpCrystalPrefab, transform.position + Vector3.up * 1f, Quaternion.identity);
        }

        if (diamondPrefab != null && Random.value <= diamondDropChance)
        {
            // Elite kills drop a small cluster (2-3 diamonds) so they read as
            // worthwhile fights, not "same loot as a minion."
            int dropCount = isElite ? Random.Range(2, 4) : 1;
            for (int d = 0; d < dropCount; d++)
            {
                Vector2 spread = Random.insideUnitCircle * 0.6f;
                Vector3 pos = transform.position + new Vector3(spread.x, 1f, spread.y);
                Instantiate(diamondPrefab, pos, Quaternion.identity);
            }
        }
        // ------------------------------------

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