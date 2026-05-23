using UnityEngine;
using UnityEngine.UI;
using TMPro;

[RequireComponent(typeof(CharacterController))]
public class PlayerController : MonoBehaviour
{
    [Header("Scene Mode")]
    public bool isCampMode = false;
    [HideInInspector] public bool isControlBlocked = false;
    private float actionLockEndTime = 0f; // ФІКС: Таймер блокування дій

    [Header("Weapon Spawning")]
    public GameObject[] weaponPrefabs;
    private GameObject currentWeapon;

    [Header("Debug")]
    public float noclipSpeed = 30f;
    private bool isNoclip = false;

    [Header("Movement Settings")]
    public float moveSpeed = 8f;
    public float rotationSpeed = 15f;

    [Header("MegaBoom Inertia")]
    public float normalAcceleration = 15f;
    public float dragAcceleration = 3f;
    private Vector3 currentVelocityMove;

    [Header("Jump & Gravity")]
    public bool canJump = true;
    public float jumpHeight = 2f;
    public float gravity = -25f;

    [Header("Player Stats")]
    public float maxHealth = 100f;
    public float currentHealth;
    public float healthRegenRate = 0f;
    public float pickupRadius = 4f;

    [Header("RPG Stats")]
    public int currentLevel = 1;
    public float currentXP = 0f;
    public float xpToNextLevel = 50f;
    public int crystalsCollected = 0;

    [Header("Melee Combat")]
    public float meleeDamage = 25f;
    public float meleeRadius = 2.5f;
    public Transform meleePoint;
    public float attackCooldown = 0.6f;
    private float lastAttackTime = -100f;

    [Header("Grenade")]
    public GameObject grenadePrefab;
    public Transform throwPoint;
    public LineRenderer trajectoryLine;
    public int linePoints = 30;
    public float timeBetweenPoints = 0.1f;
    public float minThrowForce = 5f;
    public float maxThrowForce = 30f;
    public float chargeRate = 15f;
    public float upwardAngle = 0.5f;

    private float currentThrowForce;
    private bool isAimingGrenade = false;
    private Vector3 savedThrowVelocity;

    [Header("Visual Effects")]
    public Image damageFlashImage;
    private TrailRenderer weaponTrail;

    [Header("HUD UI References")]
    public Image hpFill;
    public Image xpFill;
    public Image dashStaminaFill;
    public TextMeshProUGUI levelText;
    public TextMeshProUGUI crystalText;
    public TextMeshProUGUI hpText;

    [Header("Juicy UI & Effects")]
    public Image hpCatchupFill;
    public float uiLerpSpeed = 5f;
    private float visualXP = 0f;

    [Header("Dash Juice")]
    public ParticleSystem dashParticles;
    public float dashSpeed = 25f;
    public float dashDuration = 0.2f;
    public float dashCooldown = 1.5f;
    private bool isDashing = false;
    private float lastDashTime = -100f;

    [Header("Meta Upgrades")]
    [HideInInspector] public float globalDamageMultiplier = 1f;
    [HideInInspector] public float globalCritChance = 0.05f;
    [HideInInspector] public float damageReduction = 0f;

    [Header("MegaBoom Settings")]
    public float stackRadius = 7f;
    public TextMeshProUGUI stackText;
    public float criticalDamagePerSec = 5f;
    [HideInInspector] public int currentStack = 0;
    [HideInInspector] public int currentMultiplier = 1;

    private CameraFollow cameraFollow;
    private HealthVisuals healthVisuals;
    private CharacterController characterController;
    private Vector3 velocity;
    private Animator anim;
    private bool isDead = false;

    private void Awake()
    {
        gameObject.layer = 8;
        Physics.IgnoreLayerCollision(8, 9, true);

        characterController = GetComponent<CharacterController>();
        anim = GetComponentInChildren<Animator>();
        if (anim != null) anim.applyRootMotion = false;

        if (Camera.main != null) cameraFollow = Camera.main.GetComponent<CameraFollow>();
        healthVisuals = FindFirstObjectByType<HealthVisuals>();

        if (trajectoryLine != null) trajectoryLine.positionCount = 0;
    }

    private void Start()
    {
        isDead = false;
        ReconnectUI();
        SpawnEquippedWeapon();

        if (!isCampMode) ApplyMetaUpgrades();
        currentHealth = maxHealth;
        visualXP = currentXP;
        UpdateHUD();

        UIIconGlimmer glimmer = FindFirstObjectByType<UIIconGlimmer>();
        if (glimmer != null) glimmer.StartEffect();

        if (weaponTrail != null) weaponTrail.emitting = false;

        StartCoroutine(SpawnSafely());
    }

    private void SpawnEquippedWeapon()
    {
        Transform socket = FindDeepChild(transform, "WeaponSocket");
        if (socket == null) socket = FindDeepChild(transform, "handslot.r");
        if (socket == null) socket = FindDeepChild(transform, "hand_r");
        if (socket == null) socket = FindDeepChild(transform, "hand_R");
        if (socket == null) socket = FindDeepChild(transform, "RightHand");

        int selectedWeaponID = PlayerPrefs.GetInt("SelectedWeaponID", 0);

        if (socket != null && weaponPrefabs != null && weaponPrefabs.Length > selectedWeaponID && weaponPrefabs[selectedWeaponID] != null)
        {
            currentWeapon = Instantiate(weaponPrefabs[selectedWeaponID], socket);
            currentWeapon.transform.localPosition = Vector3.zero;
            currentWeapon.transform.localRotation = Quaternion.identity;

            weaponTrail = currentWeapon.GetComponentInChildren<TrailRenderer>();
        }
    }

    private void ReconnectUI()
    {
        if (hpFill != null) return;

        Image[] images = FindObjectsByType<Image>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        foreach (Image img in images)
        {
            string n = img.name.ToLower();
            string p = img.transform.parent != null ? img.transform.parent.name.ToLower() : "";

            bool isHP = n.Contains("hp") || p.Contains("hp");
            bool isXP = n.Contains("xp") || p.Contains("xp");
            bool isStamina = n.Contains("stamina") || n.Contains("dash") || p.Contains("stamina") || p.Contains("dash");

            if (isHP && n.Contains("fill") && !n.Contains("catchup")) hpFill = img;
            else if (isHP && n.Contains("catchup")) hpCatchupFill = img;
            else if (isXP && n.Contains("fill")) xpFill = img;
            else if (isStamina && n.Contains("fill")) dashStaminaFill = img;
        }

        TextMeshProUGUI[] texts = FindObjectsByType<TextMeshProUGUI>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        foreach (TextMeshProUGUI txt in texts)
        {
            string n = txt.name.ToLower();
            string p = txt.transform.parent != null ? txt.transform.parent.name.ToLower() : "";

            bool isHP = n.Contains("hp") || p.Contains("hp");

            if (isHP && (n.Contains("text") || n.Contains("val"))) hpText = txt;
            else if (n.Contains("lvl") || n.Contains("level")) levelText = txt;
            else if (n.Contains("crystal") || n.Contains("diamond")) crystalText = txt;
        }
    }

    private System.Collections.IEnumerator SpawnSafely()
    {
        if (characterController != null) characterController.enabled = false;
        yield return null;
        yield return null;

        if (isCampMode)
        {
            if (PlayerPrefs.GetInt("HasCampSave", 0) == 1)
            {
                float cx = PlayerPrefs.GetFloat("CampPosX");
                float cy = PlayerPrefs.GetFloat("CampPosY");
                float cz = PlayerPrefs.GetFloat("CampPosZ");
                transform.position = new Vector3(cx, cy, cz);
            }
            if (characterController != null) characterController.enabled = true;
            yield break;
        }

        if (PlayerPrefs.GetInt("IsContinuing", 0) == 1)
        {
            float savedX = PlayerPrefs.GetFloat("PlayerPosX", transform.position.x);
            float savedY = PlayerPrefs.GetFloat("PlayerPosY", transform.position.y);
            float savedZ = PlayerPrefs.GetFloat("PlayerPosZ", transform.position.z);
            transform.position = new Vector3(savedX, savedY, savedZ);
        }
        else
        {
            GameObject spawnPoint = GameObject.Find("PlayerSpawnPoint");
            if (spawnPoint != null)
            {
                transform.position = spawnPoint.transform.position;
                transform.rotation = spawnPoint.transform.rotation;
            }
            else
            {
                float spawnX = 0f;
                float spawnZ = 0f;
                float spawnY = 20f;

                Vector3 skyPos = new Vector3(spawnX, 1000f, spawnZ);

                if (Physics.Raycast(skyPos, Vector3.down, out RaycastHit hit, 2000f))
                {
                    spawnY = hit.point.y + 2f;
                }
                else if (Terrain.activeTerrain != null)
                {
                    spawnY = Terrain.activeTerrain.SampleHeight(new Vector3(spawnX, 0, spawnZ)) + Terrain.activeTerrain.transform.position.y + 2f;
                }

                transform.position = new Vector3(spawnX, spawnY, spawnZ);
            }
        }

        if (characterController != null) characterController.enabled = true;
    }

    private void ApplyMetaUpgrades()
    {
        int healthLvl = SaveManager.GetUpgradeLevel("MetaHealth");
        maxHealth += maxHealth * (healthLvl * 0.1f);
        int speedLvl = SaveManager.GetUpgradeLevel("MetaSpeed");
        moveSpeed += moveSpeed * (speedLvl * 0.05f);
        int magnetLvl = SaveManager.GetUpgradeLevel("MetaMagnet");
        pickupRadius += pickupRadius * (magnetLvl * 0.2f);
        int armorLvl = SaveManager.GetUpgradeLevel("MetaArmor");
        damageReduction = armorLvl * 0.05f;

        int dmgLvl = SaveManager.GetUpgradeLevel("MetaDamage");
        globalDamageMultiplier = 1f + (dmgLvl * 0.1f);

        float weaponDmgBonus = PlayerPrefs.GetFloat("EquippedWeaponDamage", 0f);
        meleeDamage += weaponDmgBonus;

        globalCritChance = PlayerPrefs.GetFloat("EquippedWeaponCrit", 0.05f);

        int forgeLevel = PlayerPrefs.GetInt("SaveBld_Forge", 0);
        float forgeDamageBonus = 0f;
        switch (forgeLevel)
        {
            case 1: forgeDamageBonus = 0.02f; break;
            case 2: forgeDamageBonus = 0.05f; break;
            case 3: forgeDamageBonus = 0.08f; break;
            case 4: forgeDamageBonus = 0.11f; break;
            case 5: forgeDamageBonus = 0.15f; break;
            default: forgeDamageBonus = 0f; break;
        }
        globalDamageMultiplier += forgeDamageBonus;
    }

    private void CheckStack()
    {
        if (isCampMode) return;

        Collider[] colliders = Physics.OverlapSphere(transform.position, stackRadius, 1 << 9);
        currentStack = 0;

        foreach (Collider col in colliders)
        {
            if (col.CompareTag("Enemy")) currentStack++;
        }

        if (currentStack >= 30) currentMultiplier = 5;
        else if (currentStack >= 20) currentMultiplier = 4;
        else if (currentStack >= 15) currentMultiplier = 2;
        else currentMultiplier = 1;

        if (stackText != null)
        {
            stackText.text = "STACK: " + currentStack + "  |  x" + currentMultiplier;
            if (currentStack >= 30) stackText.color = Color.red;
            else if (currentStack >= 15) stackText.color = Color.yellow;
            else stackText.color = Color.white;
        }
    }

    // ФІКС: Новий метод для жорсткої зупинки тіла
    private void LockAction(string trigger, float duration)
    {
        actionLockEndTime = Time.time + duration;
        currentVelocityMove = Vector3.zero; // Миттєва зупинка ковзання

        if (anim != null)
        {
            anim.SetFloat("Speed", 0f); // Примушуємо ноги перейти в Idle
            anim.SetTrigger(trigger);
        }
    }

    private void Update()
    {
        if (dashStaminaFill != null)
        {
            float targetDashProgress = Mathf.Clamp01((Time.time - lastDashTime) / dashCooldown);
            dashStaminaFill.fillAmount = Mathf.Lerp(dashStaminaFill.fillAmount, targetDashProgress, Time.deltaTime * 15f);
        }

        float targetHpFill = currentHealth / maxHealth;

        if (hpFill != null) hpFill.fillAmount = targetHpFill;
        if (hpCatchupFill != null && hpCatchupFill.fillAmount > targetHpFill)
        {
            hpCatchupFill.fillAmount = Mathf.Lerp(hpCatchupFill.fillAmount, targetHpFill, Time.deltaTime * uiLerpSpeed);
        }

        float targetXpFill = currentXP / xpToNextLevel;
        if (xpFill != null && visualXP < currentXP)
        {
            visualXP = Mathf.Lerp(visualXP, currentXP, Time.deltaTime * uiLerpSpeed);
            xpFill.fillAmount = visualXP / xpToNextLevel;
        }

        CheckStack();

        if (Input.GetKeyDown(KeyCode.F10))
        {
            isNoclip = !isNoclip;
            if (characterController != null) characterController.enabled = !isNoclip;
        }

        if (isNoclip)
        {
            float h = Input.GetAxisRaw("Horizontal");
            float v = Input.GetAxisRaw("Vertical");
            float up = 0f;
            if (Input.GetKey(KeyCode.Space)) up = 1f;
            if (Input.GetKey(KeyCode.LeftControl)) up = -1f;

            Vector3 ncForward = Camera.main.transform.forward;
            Vector3 ncRight = Camera.main.transform.right;

            Vector3 dir = (ncForward * v + ncRight * h + Vector3.up * up).normalized;
            transform.position += dir * noclipSpeed * Time.deltaTime;
            return;
        }

        // ФІКС: Перевірка, чи гравець зараз в процесі атаки/отримання урону
        bool isCurrentlyLocked = isControlBlocked || Time.time < actionLockEndTime;

        Vector3 movement = Vector3.zero;
        Vector3 inputDir = Vector3.zero;

        if (!isCurrentlyLocked)
        {
            float horizontal = Input.GetAxisRaw("Horizontal");
            float vertical = Input.GetAxisRaw("Vertical");
            inputDir = new Vector3(horizontal, 0f, vertical).normalized;

            if (!isCampMode && Input.GetKeyDown(KeyCode.LeftShift) && Time.time >= lastDashTime + dashCooldown)
            {
                StartCoroutine(DashRoutine(inputDir));
            }
        }
        else
        {
            inputDir = Vector3.zero; // Забороняємо рух
        }

        if (isDashing) return;
        if (Camera.main == null) return;

        Vector3 camForward = Camera.main.transform.forward;
        Vector3 camRight = Camera.main.transform.right;
        camForward.y = 0f; camRight.y = 0f;
        camForward.Normalize(); camRight.Normalize();

        Vector3 targetMoveDirection = Vector3.zero;
        if (inputDir.magnitude >= 0.1f)
        {
            targetMoveDirection = (camForward * inputDir.z + camRight * inputDir.x).normalized;

            Quaternion targetRotation = Quaternion.LookRotation(targetMoveDirection);
            transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, rotationSpeed * Time.deltaTime);
        }

        float currentAccel = normalAcceleration;

        if (!isCampMode)
        {
            if (currentStack >= 30)
            {
                currentAccel = dragAcceleration;
                currentHealth -= criticalDamagePerSec * Time.deltaTime;
                UpdateHUD();
                if (currentHealth <= 0) Die();
            }
            else if (currentStack >= 15)
            {
                currentAccel = dragAcceleration;
            }
        }

        float actualSpeed = isAimingGrenade ? moveSpeed * 0.5f : moveSpeed;

        if (inputDir.magnitude >= 0.1f)
        {
            Vector3 targetMove = targetMoveDirection * actualSpeed;
            currentVelocityMove = Vector3.Lerp(currentVelocityMove, targetMove, currentAccel * Time.deltaTime);
        }
        else
        {
            currentVelocityMove = Vector3.Lerp(currentVelocityMove, Vector3.zero, currentAccel * Time.deltaTime);
        }

        movement = currentVelocityMove;
        float safeDeltaTime = Mathf.Min(Time.deltaTime, 0.05f);

        if (characterController.isGrounded && velocity.y < 0) velocity.y = -2f;
        if (!isCurrentlyLocked && canJump && Input.GetButtonDown("Jump") && characterController.isGrounded)
        {
            velocity.y = Mathf.Sqrt(jumpHeight * -2f * gravity);
        }
        velocity.y += gravity * safeDeltaTime;

        Vector3 finalMove = movement + velocity;

        if (characterController.enabled)
        {
            characterController.Move(finalMove * safeDeltaTime);
        }

        if (anim != null)
        {
            // Оновлюємо анімацію швидкості (яка тепер миттєво падає до 0 при атаці)
            anim.SetFloat("Speed", currentVelocityMove.magnitude);
            anim.SetBool("IsGrounded", characterController.isGrounded);

            Vector3 localVelocity = transform.InverseTransformDirection(currentVelocityMove);
            anim.SetFloat("MoveX", Mathf.Clamp(localVelocity.x / moveSpeed, -1f, 1f));
            anim.SetFloat("MoveZ", Mathf.Clamp(localVelocity.z / moveSpeed, -1f, 1f));

            if (characterController.isGrounded && !isCurrentlyLocked)
            {
                if (!isCampMode)
                {
                    if (Input.GetMouseButtonDown(0))
                    {
                        if (!isAimingGrenade && Time.time >= lastAttackTime + attackCooldown)
                        {
                            lastAttackTime = Time.time;
                            if (camForward.sqrMagnitude > 0.01f) transform.rotation = Quaternion.LookRotation(camForward);
                            LockAction("Attack", 0.4f); // Блокуємо рух на 0.4 секунди
                        }
                        else if (isAimingGrenade)
                        {
                            isAimingGrenade = false;
                            if (trajectoryLine != null) trajectoryLine.positionCount = 0;
                        }
                    }

                    if (Input.GetMouseButtonDown(1))
                    {
                        isAimingGrenade = true;
                        currentThrowForce = minThrowForce;
                        if (trajectoryLine != null) trajectoryLine.positionCount = 0;
                    }
                }
            }

            if (!isCampMode && !isCurrentlyLocked && Input.GetMouseButton(1) && isAimingGrenade)
            {
                currentThrowForce += chargeRate * Time.deltaTime;
                if (currentThrowForce > maxThrowForce) currentThrowForce = maxThrowForce;
                DrawTrajectory();
            }

            if (!isCampMode && (!isCurrentlyLocked && Input.GetMouseButtonUp(1) || (isCurrentlyLocked && isAimingGrenade)))
            {
                if (isAimingGrenade)
                {
                    isAimingGrenade = false;
                    savedThrowVelocity = GetThrowVelocity();
                    if (trajectoryLine != null) trajectoryLine.positionCount = 0;

                    if (characterController.isGrounded) LockAction("Throw", 0.4f);
                    else ExecuteThrow();
                }
            }
        }

        if (!isCampMode && currentHealth < maxHealth && healthRegenRate > 0)
        {
            currentHealth += healthRegenRate * Time.deltaTime;
            currentHealth = Mathf.Min(currentHealth, maxHealth);
            UpdateHUD();
        }
    }

    private Vector3 GetThrowVelocity()
    {
        Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);
        Plane groundPlane = new Plane(Vector3.up, transform.position);
        Vector3 aimDir = transform.forward;

        if (groundPlane.Raycast(ray, out float enter))
        {
            Vector3 hitPoint = ray.GetPoint(enter);
            aimDir = (hitPoint - throwPoint.position).normalized;
            aimDir.y = 0;
            aimDir.Normalize();
        }

        if (aimDir.sqrMagnitude > 0.01f)
        {
            Quaternion targetRot = Quaternion.LookRotation(aimDir);
            transform.rotation = Quaternion.Slerp(transform.rotation, targetRot, rotationSpeed * Time.deltaTime * 2f);
        }

        Vector3 throwDir = (aimDir + Vector3.up * upwardAngle).normalized;
        return throwDir * currentThrowForce;
    }

    private void DrawTrajectory()
    {
        if (trajectoryLine == null || throwPoint == null) return;
        trajectoryLine.positionCount = linePoints;
        Vector3 startPosition = throwPoint.position;
        Vector3 startVelocity = GetThrowVelocity();

        for (int i = 0; i < linePoints; i++)
        {
            float t = i * timeBetweenPoints;
            Vector3 point = startPosition + startVelocity * t + Physics.gravity * 0.5f * t * t;

            if (point.y < transform.position.y && i > 3)
            {
                trajectoryLine.positionCount = i + 1;
                trajectoryLine.SetPosition(i, new Vector3(point.x, transform.position.y + 0.1f, point.z));
                break;
            }
            trajectoryLine.SetPosition(i, point);
        }
    }

    public void ExecuteAttack()
    {
        if (meleePoint == null || isCampMode) return;

        if (AudioManager.Instance != null) AudioManager.Instance.PlaySFX(AudioID.Player_Swing);

        Collider[] hitObjects = Physics.OverlapSphere(meleePoint.position, meleeRadius);
        bool hitEnemy = false;
        bool hitResource = false;
        bool isCriticalHit = Random.value <= globalCritChance;

        foreach (Collider col in hitObjects)
        {
            if (col.CompareTag("Enemy"))
            {
                EnemyAI enemy = col.GetComponent<EnemyAI>();
                if (enemy != null)
                {
                    float finalDmg = meleeDamage * globalDamageMultiplier;
                    if (isCriticalHit) finalDmg *= 2.5f;

                    enemy.TakeDamage(finalDmg, isCriticalHit);

                    Vector3 pushDir = (enemy.transform.position - transform.position).normalized;
                    pushDir.y = 0;
                    enemy.ApplyKnockback(pushDir, isCriticalHit ? 12f : 8f, isCriticalHit ? 0.8f : 0.4f);
                    hitEnemy = true;
                }
            }
            else
            {
                ResourceNode resource = col.GetComponent<ResourceNode>();
                if (resource == null) resource = col.GetComponentInParent<ResourceNode>();

                if (resource != null)
                {
                    resource.TakeDamage(meleeDamage * globalDamageMultiplier);
                    hitResource = true;
                }
            }
        }

        if (hitEnemy)
        {
            if (AudioManager.Instance != null) AudioManager.Instance.PlaySFX(AudioID.Player_HitEnemy);

            Vector3 recoilDir = -transform.forward;

            if (isCriticalHit)
            {
                if (cameraFollow != null) cameraFollow.TriggerDirectionalShake(recoilDir, 1.2f, 0.25f, 0.2f);
                StartCoroutine(HitStopRoutine(0.12f));
            }
            else
            {
                if (cameraFollow != null) cameraFollow.TriggerDirectionalShake(recoilDir, 0.5f, 0.1f, 0.05f);
                StartCoroutine(HitStopRoutine(0.04f));
            }
        }
        else if (hitResource)
        {
            if (AudioManager.Instance != null) AudioManager.Instance.PlaySFX(AudioID.Player_HitResource);
            Vector3 recoilDir = -transform.forward;
            if (cameraFollow != null) cameraFollow.TriggerDirectionalShake(recoilDir, 0.3f, 0.1f, 0.05f);
        }
    }

    private System.Collections.IEnumerator HitStopRoutine(float duration)
    {
        Time.timeScale = 0.05f;
        yield return new WaitForSecondsRealtime(duration);
        Time.timeScale = 1f;
    }

    public void ExecuteThrow()
    {
        if (grenadePrefab != null && throwPoint != null && !isCampMode)
        {
            if (AudioManager.Instance != null) AudioManager.Instance.PlaySFX(AudioID.Player_Throw);

            GameObject grenade = Instantiate(grenadePrefab, throwPoint.position, throwPoint.rotation);
            Rigidbody rb = grenade.GetComponent<Rigidbody>();

            if (rb != null)
            {
                rb.linearVelocity = savedThrowVelocity;
                rb.AddTorque(Random.insideUnitSphere * 50f, ForceMode.Impulse);
            }
        }
    }

    public void TakeDamage(float damageAmount)
    {
        if (isCampMode) return;

        float finalDamage = damageAmount * (1f - damageReduction);
        currentHealth -= finalDamage;

        if (AudioManager.Instance != null) AudioManager.Instance.PlaySFX(AudioID.Player_Hurt);

        if (cameraFollow != null)
        {
            Vector3 hitPushDir = transform.forward;
            cameraFollow.TriggerDirectionalShake(hitPushDir, 1.5f, 0.3f, 0.3f);
        }

        if (healthVisuals != null) healthVisuals.TriggerHitFlash();

        if (damageFlashImage != null)
        {
            StopAllCoroutines();
            StartCoroutine(FlashRoutine());
        }

        UpdateHUD();

        if (currentHealth <= 0)
        {
            if (hpCatchupFill != null) hpCatchupFill.fillAmount = 0;
            Die();
        }
        else
        {
            LockAction("Hit", 0.35f); // Блокуємо рух при отриманні удару
        }
    }

    private System.Collections.IEnumerator FlashRoutine()
    {
        float t = 0.3f;
        Color c = damageFlashImage.color;
        c.a = 0.6f;
        damageFlashImage.color = c;
        while (c.a > 0)
        {
            c.a -= Time.deltaTime / t;
            damageFlashImage.color = c;
            yield return null;
        }
    }

    private void Die()
    {
        if (isDead) return;
        isDead = true;

        SaveManager.AddCrystals(crystalsCollected);

        WeaponOrbit weapon = FindFirstObjectByType<WeaponOrbit>();
        if (weapon != null) weapon.gameObject.SetActive(false);

        string currentSceneName = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
        GameManager gm = FindFirstObjectByType<GameManager>();

        if (Level1_QuestManager.Instance != null)
        {
            Level1_QuestManager.Instance.TriggerGameOver();
        }
        else if (gm != null)
        {
            gm.TriggerGameOver();
        }
        else if (GlobalHUD.Instance != null)
        {
            if (currentSceneName == "Lvl_1") GlobalHUD.Instance.FadeAndLoadScene(currentSceneName);
            else GlobalHUD.Instance.FadeAndLoadScene("CampScene");
        }

        gameObject.SetActive(false);
    }

    public void GainXP(float amount)
    {
        if (isCampMode) return;
        currentXP += amount;
        if (currentXP >= xpToNextLevel) LevelUp();
    }

    public void GainDiamond(int amount = 1)
    {
        crystalsCollected += amount;

        if (AudioManager.Instance != null) AudioManager.Instance.PlaySFX(AudioID.Camp_CollectGem);
        UpdateHUD();

        if (MissionManager.Instance != null)
        {
            MissionManager.Instance.AddProgress(MissionType.CollectCrystals, amount);
        }
    }

    private void LevelUp()
    {
        currentLevel++;
        currentXP -= xpToNextLevel;
        xpToNextLevel *= 1.5f;
        visualXP = 0f;

        if (AudioManager.Instance != null) AudioManager.Instance.PlayUI(AudioID.UI_LevelUp);

        LevelUpManager lum = FindFirstObjectByType<LevelUpManager>();
        if (lum != null) lum.ShowMenu();
        UpdateHUD();
    }

    public void UpdateHUD()
    {
        float hpRatio = currentHealth / maxHealth;

        if (hpFill != null) hpFill.fillAmount = hpRatio;

        if (hpCatchupFill != null && hpCatchupFill.fillAmount < hpRatio)
        {
            hpCatchupFill.fillAmount = hpRatio;
        }

        if (hpText != null) hpText.text = Mathf.CeilToInt(currentHealth) + " / " + Mathf.CeilToInt(maxHealth);

        if (xpFill != null) xpFill.fillAmount = currentXP / xpToNextLevel;

        if (levelText != null) levelText.text = "LVL: " + currentLevel;
        if (crystalText != null) crystalText.text = crystalsCollected.ToString();
    }

    private System.Collections.IEnumerator DashRoutine(Vector3 direction)
    {
        isDashing = true;
        lastDashTime = Time.time;
        float startTime = Time.time;

        if (AudioManager.Instance != null) AudioManager.Instance.PlaySFX(AudioID.Player_Dash);

        float originalFOV = Camera.main.fieldOfView;
        float targetFOV = originalFOV + 12f;

        if (dashParticles != null) dashParticles.Play();
        if (cameraFollow != null) cameraFollow.TriggerShake(0.15f, 0.2f);

        if (direction == Vector3.zero) direction = transform.forward;
        else
        {
            Vector3 camForward = Camera.main.transform.forward;
            Vector3 camRight = Camera.main.transform.right;
            camForward.y = 0f; camRight.y = 0f;
            direction = (camForward * direction.z + camRight * direction.x).normalized;
        }

        while (Time.time < startTime + dashDuration)
        {
            float normalizedTime = (Time.time - startTime) / dashDuration;
            float curve = Mathf.Sin(normalizedTime * Mathf.PI);

            characterController.Move(direction * dashSpeed * curve * Time.deltaTime);
            Camera.main.fieldOfView = Mathf.Lerp(Camera.main.fieldOfView, targetFOV, normalizedTime);

            yield return null;
        }

        isDashing = false;

        float elapsed = 0f;
        float returnTime = 0.3f;
        while (elapsed < returnTime)
        {
            elapsed += Time.deltaTime;
            Camera.main.fieldOfView = Mathf.Lerp(targetFOV, originalFOV, elapsed / returnTime);
            yield return null;
        }

        Camera.main.fieldOfView = originalFOV;
    }

    public void Heal(float amount)
    {
        currentHealth += amount;
        if (currentHealth > maxHealth) currentHealth = maxHealth;
        UpdateHUD();
    }

    private Transform FindDeepChild(Transform parent, string name)
    {
        foreach (Transform child in parent)
        {
            string childName = child.name.ToLower();
            if (childName == name.ToLower()) return child;
            Transform result = FindDeepChild(child, name);
            if (result != null) return result;
        }
        return null;
    }

    private void OnDrawGizmosSelected()
    {
        if (meleePoint != null)
        {
            Gizmos.color = Color.red;
            Gizmos.DrawWireSphere(meleePoint.position, meleeRadius);
        }
    }

    public void StartSwing()
    {
        if (weaponTrail != null) weaponTrail.emitting = true;
    }

    public void EndSwing()
    {
        if (weaponTrail != null) weaponTrail.emitting = false;
    }

    private void OnDestroy()
    {
        if (isCampMode)
        {
            PlayerPrefs.SetFloat("CampPosX", transform.position.x);
            PlayerPrefs.SetFloat("CampPosY", transform.position.y);
            PlayerPrefs.SetFloat("CampPosZ", transform.position.z);
            PlayerPrefs.SetInt("HasCampSave", 1);
            PlayerPrefs.Save();
        }
    }
}