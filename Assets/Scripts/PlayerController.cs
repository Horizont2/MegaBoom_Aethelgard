using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections;

[RequireComponent(typeof(CharacterController))]
public class PlayerController : MonoBehaviour, IDamageable
{
    [Header("Scene Mode")]
    public bool isCampMode = false;
    [HideInInspector] public bool isControlBlocked = false;
    private float actionLockEndTime = 0f;

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
    private int lastAttackIndex = -1;

    [Header("Grenade Settings")]
    public GameObject grenadePrefab;
    public Transform throwPoint;
    public LineRenderer trajectoryLine;
    public int linePoints = 30;
    public float maxThrowDistance = 18f;
    public float grenadeExplosionRadius = 6f;
    public float grenadeThrowSpeed = 20f;
    public float grenadeCooldown = 5f;
    [HideInInspector] public float lastGrenadeTime = -100f;
    private bool isAimingGrenade = false;
    private Vector3 currentGrenadeTarget;
    private LineRenderer aoeMarkerLine;
    private LineRenderer innerMarkerLine;

    [Header("Grenade AAA Feel")]
    public float aimSlowMotion = 0.25f;
    public float aimAssistRadius = 3.5f;

    [Header("Dark Fantasy VFX")]
    public ParticleSystem runDustParticles;
    public ParticleSystem hardLandingVFX;
    public float hardLandingVelocityThreshold = -12f;
    public GameObject hitSparkVFXPrefab;
    public Image damageFlashImage;
    private TrailRenderer weaponTrail;

    [Header("HUD UI References")]
    public Image hpFill;
    public Image xpFill;
    public Image dashStaminaFill;
    public TextMeshProUGUI levelText;
    public TextMeshProUGUI crystalText;
    public TextMeshProUGUI hpText;
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

    [Header("Perfect Dodge Settings (AAA Feel)")]
    public float perfectDodgeSlowMoScale = 0.4f;
    public float perfectDodgeDuration = 1.5f;
    public GameObject perfectDodgeVFX;

    private float dodgeWindowTimer = 0f;
    [HideInInspector] public bool isNextAttackGuaranteedCrit = false;
    private bool isBulletTime = false;

    private CameraFollow cameraFollow;
    private HealthVisuals healthVisuals;
    private CharacterController characterController;
    private Vector3 velocity;
    private Animator anim;
    private bool isDead = false;

    private Transform visualModel;
    private bool wasGroundedLastFrame = true;

    // ‘≤ —» Œœ“»Ã≤«¿÷≤Ø
    private float stackCheckTimer = 0f;
    private float ikCheckTimer = 0f;
    private float focusCheckTimer = 0f;
    private Transform ikTargetItem = null;
    private Transform currentFocusEnemy = null;

    private void Awake()
    {
        gameObject.layer = 8;
        Physics.IgnoreLayerCollision(8, 9, true);

        characterController = GetComponent<CharacterController>();
        anim = GetComponentInChildren<Animator>();
        if (anim != null)
        {
            anim.applyRootMotion = false;
            visualModel = anim.transform;
        }

        if (Camera.main != null) cameraFollow = Camera.main.GetComponent<CameraFollow>();
        healthVisuals = FindFirstObjectByType<HealthVisuals>();

        if (trajectoryLine != null) trajectoryLine.positionCount = 0;

        InitAoEMarker();
    }

    private void Start()
    {
        if (runDustParticles != null)
        {
            var em = runDustParticles.emission;
            em.rateOverTime = 0f;
            em.rateOverDistance = 0f;
            runDustParticles.Stop();
        }
        if (hardLandingVFX != null) hardLandingVFX.Stop();

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

    public void TriggerFootstepDust()
    {
        if (characterController == null || runDustParticles == null) return;
        Vector3 horizontalVel = new Vector3(characterController.velocity.x, 0, characterController.velocity.z);
        if (characterController.isGrounded && horizontalVel.sqrMagnitude > 0.1f) runDustParticles.Emit(1);
    }

    private void InitAoEMarker()
    {
        GameObject markerObj = new GameObject("GrenadeAoEMarker");
        aoeMarkerLine = markerObj.AddComponent<LineRenderer>();
        aoeMarkerLine.material = new Material(Shader.Find("Sprites/Default"));
        aoeMarkerLine.startColor = new Color(0f, 0.8f, 1f, 0.8f);
        aoeMarkerLine.endColor = new Color(0f, 0.8f, 1f, 0.8f);
        aoeMarkerLine.startWidth = 0.25f;
        aoeMarkerLine.endWidth = 0.25f;
        aoeMarkerLine.useWorldSpace = true;
        aoeMarkerLine.loop = true;
        aoeMarkerLine.positionCount = 40;
        aoeMarkerLine.enabled = false;

        GameObject innerObj = new GameObject("GrenadeInnerMarker");
        innerMarkerLine = innerObj.AddComponent<LineRenderer>();
        innerMarkerLine.material = new Material(Shader.Find("Sprites/Default"));
        innerMarkerLine.startColor = new Color(0f, 0.8f, 1f, 0.8f);
        innerMarkerLine.endColor = new Color(0f, 0.8f, 1f, 0.8f);
        innerMarkerLine.startWidth = 0.15f;
        innerMarkerLine.endWidth = 0.15f;
        innerMarkerLine.useWorldSpace = true;
        innerMarkerLine.loop = true;
        innerMarkerLine.positionCount = 20;
        innerMarkerLine.enabled = false;
    }

    private void SpawnEquippedWeapon()
    {
        Transform socket = FindDeepChild(transform, "WeaponSocket") ?? FindDeepChild(transform, "handslot.r") ?? FindDeepChild(transform, "hand_r") ?? FindDeepChild(transform, "hand_R") ?? FindDeepChild(transform, "RightHand");
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

    private IEnumerator SpawnSafely()
    {
        if (characterController != null) characterController.enabled = false;
        yield return null;
        yield return null;

        GameObject spawnPoint = GameObject.Find("PlayerSpawnPoint");

        if (isCampMode)
        {
            bool loadedSave = false;
            if (PlayerPrefs.GetInt("HasCampSave", 0) == 1)
            {
                float cx = PlayerPrefs.GetFloat("CampPosX");
                float cy = PlayerPrefs.GetFloat("CampPosY");
                float cz = PlayerPrefs.GetFloat("CampPosZ");
                if (cy > -10f) { transform.position = new Vector3(cx, cy, cz); loadedSave = true; }
            }

            if (!loadedSave && spawnPoint != null) { transform.position = spawnPoint.transform.position; transform.rotation = spawnPoint.transform.rotation; }
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
            if (spawnPoint != null) { transform.position = spawnPoint.transform.position; transform.rotation = spawnPoint.transform.rotation; }
            else
            {
                float spawnX = 0f; float spawnZ = 0f; float spawnY = 20f;
                Vector3 skyPos = new Vector3(spawnX, 1000f, spawnZ);
                if (Physics.Raycast(skyPos, Vector3.down, out RaycastHit hit, 2000f)) spawnY = hit.point.y + 2f;
                else if (Terrain.activeTerrain != null) spawnY = Terrain.activeTerrain.SampleHeight(new Vector3(spawnX, 0, spawnZ)) + Terrain.activeTerrain.transform.position.y + 2f;
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
        }
        globalDamageMultiplier += forgeDamageBonus;
    }

    // ‘≤ — Œœ“»Ã≤«¿÷≤Ø: “‡ÈÏÂ ‰Îˇ ÔÂÂ‚≥ÍË Ì‡ÚÓ‚ÔÛ
    private void CheckStack()
    {
        if (isCampMode) return;

        stackCheckTimer -= Time.deltaTime;
        if (stackCheckTimer > 0f) return;
        stackCheckTimer = 0.25f;

        Collider[] colliders = Physics.OverlapSphere(transform.position, stackRadius, 1 << 9);
        currentStack = 0;
        foreach (Collider col in colliders) { if (col.CompareTag("Enemy")) currentStack++; }

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

    private void LockAction(string trigger, float duration)
    {
        actionLockEndTime = Time.unscaledTime + duration;
        currentVelocityMove = Vector3.zero;

        if (anim != null)
        {
            anim.SetFloat("Speed", 0f);
            anim.ResetTrigger(trigger);
            anim.SetTrigger(trigger);
        }
    }

    private void CancelGrenadeAim()
    {
        isAimingGrenade = false;

        if (!isBulletTime)
        {
            Time.timeScale = 1f;
            Time.fixedDeltaTime = 0.02f;
        }

        if (trajectoryLine != null) trajectoryLine.positionCount = 0;
        if (aoeMarkerLine != null) aoeMarkerLine.enabled = false;
        if (innerMarkerLine != null) innerMarkerLine.enabled = false;
    }

    private Transform GetClosestEnemyForFocus(float maxDist, float maxAngle)
    {
        Collider[] hits = Physics.OverlapSphere(transform.position, maxDist);
        Transform bestTarget = null;
        float minDist = float.MaxValue;

        Vector3 playerForward = transform.forward;
        playerForward.y = 0;

        foreach (var hit in hits)
        {
            if (hit.CompareTag("Enemy"))
            {
                Vector3 dir = hit.transform.position - transform.position;
                dir.y = 0;
                float dist = dir.magnitude;
                float angle = Vector3.Angle(playerForward, dir.normalized);

                if (dist < maxDist && angle < maxAngle)
                {
                    if (dist < minDist) { minDist = dist; bestTarget = hit.transform; }
                }
            }
        }
        return bestTarget;
    }

    private void Update()
    {
        if (isDead) return;

        if (dashStaminaFill != null)
            dashStaminaFill.fillAmount = Mathf.Lerp(dashStaminaFill.fillAmount, Mathf.Clamp01((Time.unscaledTime - lastDashTime) / dashCooldown), Time.unscaledDeltaTime * 15f);

        float targetHpFill = currentHealth / maxHealth;
        if (hpFill != null) hpFill.fillAmount = targetHpFill;
        if (hpCatchupFill != null && hpCatchupFill.fillAmount > targetHpFill)
            hpCatchupFill.fillAmount = Mathf.Lerp(hpCatchupFill.fillAmount, targetHpFill, Time.unscaledDeltaTime * uiLerpSpeed);

        float targetXpFill = currentXP / xpToNextLevel;
        if (xpFill != null && visualXP < currentXP)
        {
            visualXP = Mathf.Lerp(visualXP, currentXP, Time.unscaledDeltaTime * uiLerpSpeed);
            xpFill.fillAmount = visualXP / xpToNextLevel;
        }

        CheckStack();

        if (dodgeWindowTimer > 0) dodgeWindowTimer -= Time.unscaledDeltaTime;

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
            transform.position += dir * noclipSpeed * Time.unscaledDeltaTime;
            return;
        }

        bool isCurrentlyLocked = isControlBlocked || Time.unscaledTime < actionLockEndTime;
        Vector3 inputDir = Vector3.zero;

        if (!isCurrentlyLocked)
        {
            inputDir = new Vector3(Input.GetAxisRaw("Horizontal"), 0f, Input.GetAxisRaw("Vertical")).normalized;

            if (!isCampMode && Input.GetKeyDown(KeyCode.LeftShift) && Time.unscaledTime >= lastDashTime + dashCooldown)
            {
                if (!isAimingGrenade)
                {
                    if (dodgeWindowTimer > 0f) StartCoroutine(PerfectDodgeSequence(inputDir));
                    else StartCoroutine(DashRoutine(inputDir, false));
                }
            }
        }

        if (isDashing || Camera.main == null) return;

        Vector3 camForward = Camera.main.transform.forward;
        Vector3 camRight = Camera.main.transform.right;
        camForward.y = 0f; camRight.y = 0f;
        camForward.Normalize(); camRight.Normalize();

        Vector3 targetMoveDirection = Vector3.zero;

        if (inputDir.magnitude >= 0.1f)
        {
            targetMoveDirection = (camForward * inputDir.z + camRight * inputDir.x).normalized;
            Quaternion targetRotation = Quaternion.LookRotation(targetMoveDirection);
            transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, rotationSpeed * Time.unscaledDeltaTime);
        }
        else if (!isCurrentlyLocked && !isCampMode)
        {
            // ‘≤ — Œœ“»Ã≤«¿÷≤Ø: ÿÛÍ‡∫ÏÓ ‚ÓÓ„‡ ‰Îˇ ÙÓÍÛÒÛ ÌÂ ÍÓÊÂÌ Í‡‰
            focusCheckTimer -= Time.unscaledDeltaTime;
            if (focusCheckTimer <= 0f)
            {
                focusCheckTimer = 0.2f;
                currentFocusEnemy = GetClosestEnemyForFocus(4f, 60f);
            }

            if (currentFocusEnemy != null)
            {
                Vector3 dirToEnemy = (currentFocusEnemy.position - transform.position).normalized;
                dirToEnemy.y = 0;
                if (dirToEnemy.sqrMagnitude > 0.01f)
                {
                    Quaternion targetRot = Quaternion.LookRotation(dirToEnemy);
                    transform.rotation = Quaternion.Slerp(transform.rotation, targetRot, (rotationSpeed / 2f) * Time.unscaledDeltaTime);
                }
            }
        }

        float currentAccel = (!isCampMode && currentStack >= 30) ? dragAcceleration : normalAcceleration;
        float actualSpeed = isAimingGrenade ? moveSpeed * 0.4f : moveSpeed;
        float dt = isBulletTime || isAimingGrenade ? Time.unscaledDeltaTime : Time.deltaTime;

        if (inputDir.magnitude >= 0.1f) currentVelocityMove = Vector3.Lerp(currentVelocityMove, targetMoveDirection * actualSpeed, currentAccel * dt);
        else currentVelocityMove = Vector3.Lerp(currentVelocityMove, Vector3.zero, currentAccel * dt);

        float safeDeltaTime = Mathf.Min(dt, 0.05f);

        if (!isCurrentlyLocked && canJump && Input.GetButtonDown("Jump") && characterController.isGrounded)
        {
            velocity.y = Mathf.Sqrt(jumpHeight * -2f * gravity);
        }

        float yVelocityBeforeMove = velocity.y;
        velocity.y += gravity * safeDeltaTime;

        if (characterController.enabled)
        {
            characterController.Move((currentVelocityMove + velocity) * safeDeltaTime);
        }

        bool isGroundedNow = characterController.isGrounded;

        if (!wasGroundedLastFrame && isGroundedNow)
        {
            if (yVelocityBeforeMove <= hardLandingVelocityThreshold)
            {
                if (hardLandingVFX != null) hardLandingVFX.Play();
                if (cameraFollow != null) cameraFollow.TriggerShake(0.2f, 0.25f);
            }
            else if (yVelocityBeforeMove < -5f)
            {
                if (runDustParticles != null) runDustParticles.Emit(2);
            }
        }

        if (isGroundedNow && velocity.y < 0) velocity.y = -2f;
        wasGroundedLastFrame = isGroundedNow;

        if (visualModel != null && visualModel != transform && !isCampMode)
        {
            Vector3 localVel = transform.InverseTransformDirection(currentVelocityMove);
            float leanX = (localVel.z / moveSpeed) * 8f;
            float leanZ = -(localVel.x / moveSpeed) * 10f;

            Quaternion targetLean = Quaternion.Euler(leanX, 0, leanZ);
            visualModel.localRotation = Quaternion.Slerp(visualModel.localRotation, targetLean, Time.deltaTime * 18f);
        }

        bool isVisuallyGrounded = isGroundedNow;
        if (!isVisuallyGrounded && velocity.y <= 0f)
        {
            if (Physics.Raycast(transform.position + Vector3.up * 0.2f, Vector3.down, 0.4f, LayerMask.GetMask("Default", "Terrain", "Ground")))
                isVisuallyGrounded = true;
        }

        if (anim != null)
        {
            anim.SetFloat("Speed", currentVelocityMove.magnitude);
            anim.SetBool("IsGrounded", isVisuallyGrounded);

            Vector3 localVelocity = transform.InverseTransformDirection(currentVelocityMove);
            anim.SetFloat("MoveX", Mathf.Clamp(localVelocity.x / moveSpeed, -1f, 1f));
            anim.SetFloat("MoveZ", Mathf.Clamp(localVelocity.z / moveSpeed, -1f, 1f));

            if (isGroundedNow && !isCurrentlyLocked)
            {
                if (!isCampMode)
                {
                    if (Input.GetMouseButtonDown(0))
                    {
                        if (!isAimingGrenade && Time.unscaledTime >= lastAttackTime + attackCooldown)
                        {
                            lastAttackTime = Time.unscaledTime;

                            Transform combatTarget = GetClosestEnemyForFocus(7f, 90f);
                            if (combatTarget != null)
                            {
                                Vector3 attackDir = (combatTarget.position - transform.position).normalized;
                                attackDir.y = 0;
                                transform.rotation = Quaternion.LookRotation(attackDir);
                            }
                            else if (camForward.sqrMagnitude > 0.01f)
                            {
                                transform.rotation = Quaternion.LookRotation(camForward);
                            }

                            int randAnim = Random.Range(0, 3);
                            if (randAnim == lastAttackIndex) randAnim = (randAnim + 1) % 3;
                            lastAttackIndex = randAnim;

                            if (anim != null) anim.SetInteger("AttackIndex", randAnim);
                            LockAction("Attack", 0.6f);
                        }
                        else if (isAimingGrenade) CancelGrenadeAim();
                    }

                    if (Input.GetMouseButtonDown(1) && UnityEngine.SceneManagement.SceneManager.GetActiveScene().name != "Lvl_1")
                    {
                        if (Time.unscaledTime >= lastGrenadeTime + grenadeCooldown)
                        {
                            isAimingGrenade = true;

                            if (!isBulletTime)
                            {
                                Time.timeScale = aimSlowMotion;
                                Time.fixedDeltaTime = 0.02f * Time.timeScale;
                            }

                            if (trajectoryLine != null) trajectoryLine.positionCount = linePoints;
                            if (aoeMarkerLine != null) aoeMarkerLine.enabled = true;
                            if (innerMarkerLine != null) innerMarkerLine.enabled = true;
                        }
                    }
                }
            }

            if (!isCampMode && !isCurrentlyLocked && Input.GetMouseButton(1) && isAimingGrenade) UpdateGrenadeAiming();
            if (!isCampMode && (!isCurrentlyLocked && Input.GetMouseButtonUp(1) || (isCurrentlyLocked && isAimingGrenade)))
            {
                if (isAimingGrenade)
                {
                    CancelGrenadeAim();
                    if (isGroundedNow) LockAction("Throw", 0.4f);
                    else ExecuteThrow();
                }
            }
        }

        if (!isCampMode && currentHealth < maxHealth && healthRegenRate > 0)
        {
            currentHealth += healthRegenRate * dt;
            currentHealth = Mathf.Min(currentHealth, maxHealth);
            UpdateHUD();
        }
    }

    private void UpdateGrenadeAiming()
    {
        Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);
        Vector3 hitPoint = transform.position + transform.forward * 5f;

        if (Physics.Raycast(ray, out RaycastHit hit, 100f, LayerMask.GetMask("Default", "Terrain", "Ground")))
        {
            hitPoint = hit.point;
        }
        else
        {
            Plane groundPlane = new Plane(Vector3.up, new Vector3(0, transform.position.y, 0));
            if (groundPlane.Raycast(ray, out float enter)) hitPoint = ray.GetPoint(enter);
        }

        Collider[] magnetHits = Physics.OverlapSphere(hitPoint, aimAssistRadius);
        Transform bestTarget = null;
        float minDist = float.MaxValue;

        foreach (var mHit in magnetHits)
        {
            if (mHit.CompareTag("Enemy"))
            {
                float d = Vector3.Distance(hitPoint, mHit.transform.position);
                if (d < minDist) { minDist = d; bestTarget = mHit.transform; }
            }
        }

        if (bestTarget != null)
        {
            Vector3 magneticTarget = bestTarget.position;
            magneticTarget.y = hitPoint.y;
            hitPoint = Vector3.Lerp(hitPoint, magneticTarget, 0.4f);
        }

        Vector3 offset = hitPoint - transform.position;
        offset.y = 0;
        if (offset.magnitude > maxThrowDistance)
        {
            hitPoint = transform.position + offset.normalized * maxThrowDistance;
            if (Terrain.activeTerrain != null) hitPoint.y = Terrain.activeTerrain.SampleHeight(hitPoint) + Terrain.activeTerrain.transform.position.y;
        }

        currentGrenadeTarget = hitPoint;

        Collider[] blastHits = Physics.OverlapSphere(currentGrenadeTarget, grenadeExplosionRadius);
        bool enemyInBlast = false;
        foreach (var bHit in blastHits)
        {
            if (bHit.CompareTag("Enemy")) { enemyInBlast = true; break; }
        }

        Color currentAimColor = enemyInBlast ? new Color(1f, 0.1f, 0.1f, 0.8f) : new Color(0f, 0.8f, 1f, 0.8f);

        if (trajectoryLine != null)
        {
            trajectoryLine.startColor = currentAimColor;
            trajectoryLine.endColor = currentAimColor;
            trajectoryLine.material.mainTextureOffset -= new Vector2(Time.unscaledDeltaTime * 2.5f, 0);
        }

        if (aoeMarkerLine != null) { aoeMarkerLine.startColor = currentAimColor; aoeMarkerLine.endColor = currentAimColor; }
        if (innerMarkerLine != null) { innerMarkerLine.startColor = currentAimColor; innerMarkerLine.endColor = currentAimColor; }

        DrawAoEMarker(currentGrenadeTarget);
        DrawPreciseTrajectory(currentGrenadeTarget);

        Vector3 aimDir = (currentGrenadeTarget - transform.position).normalized;
        aimDir.y = 0;
        if (aimDir.sqrMagnitude > 0.01f)
        {
            transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(aimDir), rotationSpeed * Time.unscaledDeltaTime * 3f);
        }
    }

    private Vector3 CalculateThrowVelocity(Vector3 target)
    {
        Vector3 displacement = target - throwPoint.position;
        Vector3 displacementXZ = new Vector3(displacement.x, 0, displacement.z);
        float distanceXZ = displacementXZ.magnitude;

        float dynamicFlightTime = Mathf.Clamp(distanceXZ / grenadeThrowSpeed, 0.25f, 1.2f);
        float velY = (displacement.y / dynamicFlightTime) - (0.5f * Physics.gravity.y * dynamicFlightTime);
        Vector3 velXZ = displacementXZ / dynamicFlightTime;

        return velXZ + Vector3.up * velY;
    }

    private void DrawPreciseTrajectory(Vector3 target)
    {
        if (trajectoryLine == null) return;
        trajectoryLine.positionCount = linePoints;
        Vector3 startPos = throwPoint.position;
        Vector3 vel = CalculateThrowVelocity(target);

        Vector3 displacementXZ = new Vector3(target.x - startPos.x, 0, target.z - startPos.z);
        float flightTime = Mathf.Clamp(displacementXZ.magnitude / grenadeThrowSpeed, 0.25f, 1.2f);

        for (int i = 0; i < linePoints; i++)
        {
            float t = i * (flightTime / (linePoints - 1));
            Vector3 point = startPos + vel * t + Physics.gravity * 0.5f * t * t;
            trajectoryLine.SetPosition(i, point);
        }
    }

    private void DrawAoEMarker(Vector3 center)
    {
        if (aoeMarkerLine != null)
        {
            int segments = aoeMarkerLine.positionCount;
            float angle = 0f;
            for (int i = 0; i < segments; i++)
            {
                float x = Mathf.Sin(Mathf.Deg2Rad * angle) * grenadeExplosionRadius;
                float z = Mathf.Cos(Mathf.Deg2Rad * angle) * grenadeExplosionRadius;
                Vector3 point = center + new Vector3(x, 50f, z);
                point.y = GetGroundHeight(point) + 0.15f;
                aoeMarkerLine.SetPosition(i, point);
                angle += (360f / segments);
            }
        }

        if (innerMarkerLine != null)
        {
            float innerRadius = 0.8f;
            int segments = innerMarkerLine.positionCount;
            float angle = 0f;
            for (int i = 0; i < segments; i++)
            {
                float x = Mathf.Sin(Mathf.Deg2Rad * angle) * innerRadius;
                float z = Mathf.Cos(Mathf.Deg2Rad * angle) * innerRadius;
                Vector3 point = center + new Vector3(x, 50f, z);
                point.y = GetGroundHeight(point) + 0.15f;
                innerMarkerLine.SetPosition(i, point);
                angle += (360f / segments);
            }
        }
    }

    private float GetGroundHeight(Vector3 pos)
    {
        if (Physics.Raycast(pos, Vector3.down, out RaycastHit hit, 100f, LayerMask.GetMask("Default", "Terrain", "Ground"))) return hit.point.y;
        else if (Terrain.activeTerrain != null) return Terrain.activeTerrain.SampleHeight(pos) + Terrain.activeTerrain.transform.position.y;
        return 0f;
    }

    public void OpenPerfectDodgeWindow(Transform attacker, float duration)
    {
        dodgeWindowTimer = duration;
    }

    private IEnumerator PerfectDodgeSequence(Vector3 fallbackDirection)
    {
        dodgeWindowTimer = 0f;
        isNextAttackGuaranteedCrit = true;
        isBulletTime = true;

        Time.timeScale = perfectDodgeSlowMoScale;

        if (anim != null) anim.updateMode = AnimatorUpdateMode.UnscaledTime;

        lastAttackTime = -100f;
        actionLockEndTime = 0f;

        if (AudioManager.Instance != null) AudioManager.Instance.PlaySFX(AudioID.Player_Dash);
        if (perfectDodgeVFX != null) perfectDodgeVFX.SetActive(true);

        yield return StartCoroutine(BlinkBehindRoutine(fallbackDirection));

        yield return new WaitForSecondsRealtime(perfectDodgeDuration);

        if (!isAimingGrenade) Time.timeScale = 1f;
        isBulletTime = false;
        if (anim != null) anim.updateMode = AnimatorUpdateMode.Normal;
        if (perfectDodgeVFX != null) perfectDodgeVFX.SetActive(false);
    }

    private IEnumerator BlinkBehindRoutine(Vector3 fallbackDirection)
    {
        isDashing = true;
        lastDashTime = Time.unscaledTime;

        Transform threat = ThreatUI.Instance != null ? ThreatUI.Instance.GetCurrentThreat() : null;
        if (threat == null)
        {
            yield return StartCoroutine(DashRoutine(fallbackDirection, true));
            yield break;
        }

        Vector3 targetPos = threat.position - threat.forward * 2.5f;
        targetPos.y = GetGroundHeight(targetPos);

        float originalFOV = Camera.main.fieldOfView;
        Camera.main.fieldOfView = originalFOV + 20f;
        if (dashParticles != null) dashParticles.Play();
        if (cameraFollow != null) cameraFollow.TriggerShake(0.15f, 0.2f);

        characterController.enabled = false;

        float blinkDuration = 0.15f;
        float elapsed = 0f;
        Vector3 startPos = transform.position;

        while (elapsed < blinkDuration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = elapsed / blinkDuration;

            transform.position = Vector3.Lerp(startPos, targetPos, t);

            Vector3 lookDir = (threat.position - transform.position).normalized;
            lookDir.y = 0;
            if (lookDir != Vector3.zero) transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(lookDir), t * 15f);

            yield return null;
        }

        transform.position = targetPos;
        characterController.enabled = true;
        isDashing = false;

        elapsed = 0f;
        while (elapsed < 0.3f)
        {
            elapsed += Time.unscaledDeltaTime;
            Camera.main.fieldOfView = Mathf.Lerp(originalFOV + 20f, originalFOV, elapsed / 0.3f);
            yield return null;
        }
        Camera.main.fieldOfView = originalFOV;
    }

    private IEnumerator DashRoutine(Vector3 direction, bool isPerfectDodge = false)
    {
        isDashing = true;
        lastDashTime = Time.unscaledTime;
        float startTime = Time.realtimeSinceStartup;

        if (AudioManager.Instance != null && !isPerfectDodge) AudioManager.Instance.PlaySFX(AudioID.Player_Dash);

        float originalFOV = Camera.main.fieldOfView;
        float targetFOV = originalFOV + (isPerfectDodge ? 20f : 12f);

        if (dashParticles != null) dashParticles.Play();
        if (cameraFollow != null) cameraFollow.TriggerShake(0.15f, 0.2f);

        if (direction == Vector3.zero) direction = transform.forward;
        else
        {
            Vector3 camForward = Camera.main.transform.forward; Vector3 camRight = Camera.main.transform.right;
            camForward.y = 0f; camRight.y = 0f;
            direction = (camForward * direction.z + camRight * direction.x).normalized;
        }

        float currentDashSpeed = isPerfectDodge ? dashSpeed * 1.5f : dashSpeed;

        while (Time.realtimeSinceStartup < startTime + dashDuration)
        {
            float normalizedTime = (Time.realtimeSinceStartup - startTime) / dashDuration;
            float curve = Mathf.Sin(normalizedTime * Mathf.PI);

            characterController.Move(direction * currentDashSpeed * curve * Time.unscaledDeltaTime);
            Camera.main.fieldOfView = Mathf.Lerp(Camera.main.fieldOfView, targetFOV, normalizedTime);

            yield return null;
        }

        isDashing = false;

        float elapsed = 0f;
        while (elapsed < 0.3f)
        {
            elapsed += Time.unscaledDeltaTime;
            Camera.main.fieldOfView = Mathf.Lerp(targetFOV, originalFOV, elapsed / 0.3f);
            yield return null;
        }
        Camera.main.fieldOfView = originalFOV;
    }

    public void ExecuteAttack()
    {
        if (meleePoint == null || isCampMode) return;
        if (AudioManager.Instance != null) AudioManager.Instance.PlaySFX(AudioID.Player_Swing);

        if (cameraFollow != null) cameraFollow.SetCombatState();

        Collider[] hitObjects = Physics.OverlapSphere(meleePoint.position, meleeRadius);
        bool hitEnemy = false; bool hitResource = false;
        bool isCriticalHit = isNextAttackGuaranteedCrit || Random.value <= globalCritChance;

        float finalDmg = meleeDamage * globalDamageMultiplier;
        if (isCriticalHit) finalDmg *= (isNextAttackGuaranteedCrit ? 3.5f : 2.5f);

        foreach (Collider col in hitObjects)
        {
            if (col.TryGetComponent(out IDamageable damageable))
            {
                if (col.gameObject == this.gameObject) continue;

                Vector3 pushDir = (col.transform.position - transform.position).normalized; pushDir.y = 0;
                float kForce = isCriticalHit ? (isNextAttackGuaranteedCrit ? 20f : 12f) : 8f;

                DamageInfo hitInfo = new DamageInfo
                {
                    Amount = finalDmg,
                    IsCritical = isCriticalHit,
                    PushDirection = pushDir,
                    KnockbackForce = kForce,
                    StunDuration = isCriticalHit ? 1.0f : 0.4f,
                    HitPoint = col.ClosestPoint(meleePoint.position)
                };

                damageable.TakeDamage(hitInfo);
                if (col.CompareTag("Enemy")) hitEnemy = true; else hitResource = true;

                if (hitSparkVFXPrefab != null && ObjectPoolManager.Instance != null)
                {
                    Quaternion hitRot = pushDir != Vector3.zero ? Quaternion.LookRotation(pushDir) : Quaternion.identity;
                    GameObject vfx = ObjectPoolManager.Instance.SpawnFromPool(hitSparkVFXPrefab, hitInfo.HitPoint, hitRot);
                    if (vfx != null) vfx.transform.localScale = isNextAttackGuaranteedCrit ? Vector3.one * 3f : (isCriticalHit ? Vector3.one * 1.5f : Vector3.one);
                }
            }
        }

        isNextAttackGuaranteedCrit = false;

        if (hitEnemy)
        {
            if (AudioManager.Instance != null) AudioManager.Instance.PlaySFX(AudioID.Player_HitEnemy);
            Vector3 recoilDir = -transform.forward;
            if (isCriticalHit) { if (cameraFollow != null) cameraFollow.TriggerDirectionalShake(recoilDir, 1.5f, 0.3f, 0.2f); StartCoroutine(HitStopRoutine(0.12f)); }
            else { if (cameraFollow != null) cameraFollow.TriggerDirectionalShake(recoilDir, 0.5f, 0.1f, 0.05f); StartCoroutine(HitStopRoutine(0.04f)); }
        }
        else if (hitResource)
        {
            if (AudioManager.Instance != null) AudioManager.Instance.PlaySFX(AudioID.Player_HitResource);
            if (cameraFollow != null) cameraFollow.TriggerDirectionalShake(-transform.forward, 0.3f, 0.1f, 0.05f);
        }
    }

    public void ExecuteThrow()
    {
        if (!isBulletTime)
        {
            Time.timeScale = 1f;
            Time.fixedDeltaTime = 0.02f;
        }

        if (grenadePrefab != null && throwPoint != null && !isCampMode)
        {
            if (AudioManager.Instance != null) AudioManager.Instance.PlaySFX(AudioID.Player_Throw);
            GameObject grenade = Instantiate(grenadePrefab, throwPoint.position, throwPoint.rotation);
            Rigidbody rb = grenade.GetComponent<Rigidbody>();
            if (rb != null)
            {
                rb.linearVelocity = CalculateThrowVelocity(currentGrenadeTarget);
                rb.AddTorque(Random.insideUnitSphere * 50f, ForceMode.Impulse);
            }
            lastGrenadeTime = Time.unscaledTime;
        }
    }

    public void TakeDamage(DamageInfo info)
    {
        if (isDead || currentHealth <= 0) return;
        if (isCampMode || isDashing || isBulletTime) return;

        float finalDamage = info.Amount * (1f - damageReduction);
        currentHealth -= finalDamage;

        if (AudioManager.Instance != null) AudioManager.Instance.PlaySFX(AudioID.Player_Hurt);

        if (cameraFollow != null)
        {
            Vector3 shakeDir = info.PushDirection != Vector3.zero ? info.PushDirection : transform.forward;
            cameraFollow.TriggerDirectionalShake(shakeDir, 1.5f, 0.3f, 0.3f);
        }

        if (finalDamage >= maxHealth * 0.15f && hpFill != null)
            StartCoroutine(ShakeUIRoutine(hpFill.transform.parent.GetComponent<RectTransform>()));

        if (healthVisuals != null) healthVisuals.TriggerHitFlash();
        if (damageFlashImage != null) { StopAllCoroutines(); StartCoroutine(FlashRoutine()); }

        UpdateHUD();

        if (currentHealth <= 0) { if (hpCatchupFill != null) hpCatchupFill.fillAmount = 0; Die(); }
        else { LockAction("Hit", 0.35f); }
    }

    private IEnumerator ShakeUIRoutine(RectTransform uiElement)
    {
        if (uiElement == null) yield break;
        Vector2 originalPos = uiElement.anchoredPosition;
        float elapsed = 0f;
        while (elapsed < 0.25f)
        {
            elapsed += Time.unscaledDeltaTime;
            uiElement.anchoredPosition = originalPos + new Vector2(Random.Range(-15f, 15f), Random.Range(-10f, 10f));
            yield return null;
        }
        uiElement.anchoredPosition = originalPos;
    }

    private IEnumerator FlashRoutine()
    {
        float t = 0.3f;
        Color c = damageFlashImage.color; c.a = 0.6f; damageFlashImage.color = c;
        while (c.a > 0) { c.a -= Time.unscaledDeltaTime / t; damageFlashImage.color = c; yield return null; }
    }

    private IEnumerator HitStopRoutine(float duration)
    {
        if (isBulletTime) yield break;
        Time.timeScale = 0.05f;
        yield return new WaitForSecondsRealtime(duration);
        Time.timeScale = 1f;
    }

    private void Die()
    {
        if (isDead) return;
        isDead = true;

        WeaponOrbit weapon = FindFirstObjectByType<WeaponOrbit>();
        if (weapon != null) weapon.gameObject.SetActive(false);

        isControlBlocked = true;
        if (characterController != null) characterController.enabled = false;

        if (anim != null)
        {
            anim.ResetTrigger("Hit");
            anim.SetTrigger("Die");
        }

        if (DeathCinematicManager.Instance != null)
        {
            DeathCinematicManager.Instance.TriggerDeathCinematic();
        }
        else
        {
            string currentSceneName = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
            GameManager gm = FindFirstObjectByType<GameManager>();

            if (Level1_QuestManager.Instance != null) Level1_QuestManager.Instance.TriggerGameOver();
            else if (gm != null) gm.TriggerGameOver();
            else if (GlobalHUD.Instance != null)
            {
                if (currentSceneName == "Lvl_1") GlobalHUD.Instance.FadeAndLoadScene(currentSceneName);
                else GlobalHUD.Instance.FadeAndLoadScene("CampScene");
            }
        }
    }

    public void GainXP(float amount) { if (isCampMode) return; currentXP += amount; if (currentXP >= xpToNextLevel) LevelUp(); }

    public void GainDiamond(int amount = 1)
    {
        crystalsCollected += amount;
        if (ResourceManager.Instance != null) { ResourceManager.Instance.diamonds += amount; ResourceManager.Instance.SaveStash(); ResourceManager.Instance.UpdateUI(); }
        else { int currentDiamonds = PlayerPrefs.GetInt("PlayerDiamonds", 0); PlayerPrefs.SetInt("PlayerDiamonds", currentDiamonds + amount); PlayerPrefs.Save(); }
        if (AudioManager.Instance != null) AudioManager.Instance.PlaySFX(AudioID.Camp_CollectGem);
        UpdateHUD();
        if (MissionManager.Instance != null) MissionManager.Instance.AddProgress(MissionType.CollectCrystals, amount);
    }

    private void LevelUp()
    {
        currentLevel++; currentXP -= xpToNextLevel; xpToNextLevel *= 1.5f; visualXP = 0f;
        if (AudioManager.Instance != null) AudioManager.Instance.PlayUI(AudioID.UI_LevelUp);
        LevelUpManager lum = FindFirstObjectByType<LevelUpManager>();
        if (lum != null) lum.ShowMenu();
        UpdateHUD();
    }

    public void UpdateHUD()
    {
        float hpRatio = currentHealth / maxHealth;
        if (hpFill != null) hpFill.fillAmount = hpRatio;
        if (hpCatchupFill != null && hpCatchupFill.fillAmount < hpRatio) hpCatchupFill.fillAmount = hpRatio;
        if (hpText != null) hpText.text = Mathf.CeilToInt(currentHealth) + " / " + Mathf.CeilToInt(maxHealth);
        if (xpFill != null) xpFill.fillAmount = currentXP / xpToNextLevel;
        if (levelText != null) levelText.text = "LVL: " + currentLevel;
        if (crystalText != null) { int displayDiamonds = ResourceManager.Instance != null ? ResourceManager.Instance.diamonds : crystalsCollected; crystalText.text = $"Diamonds: {displayDiamonds}"; }
    }

    public void Heal(float amount) { currentHealth += amount; if (currentHealth > maxHealth) currentHealth = maxHealth; UpdateHUD(); }

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

    private void OnDrawGizmosSelected() { if (meleePoint != null) { Gizmos.color = Color.red; Gizmos.DrawWireSphere(meleePoint.position, meleeRadius); } }
    public void StartSwing() { if (weaponTrail != null) weaponTrail.emitting = true; }
    public void EndSwing() { if (weaponTrail != null) weaponTrail.emitting = false; }

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

    public void TriggerUIPop() { if (xpFill != null) StartCoroutine(PopUIRoutine(xpFill.transform.parent.GetComponent<RectTransform>())); }

    private IEnumerator PopUIRoutine(RectTransform uiElement)
    {
        if (uiElement == null) yield break;
        Vector3 origScale = Vector3.one;
        float elapsed = 0f;
        while (elapsed < 0.15f) { elapsed += Time.unscaledDeltaTime; float curve = Mathf.Sin((elapsed / 0.15f) * Mathf.PI); uiElement.localScale = origScale + new Vector3(0.15f, 0.15f, 0f) * curve; yield return null; }
        uiElement.localScale = origScale;
    }

    private float handIKWeight = 0f;
    private Vector3 handIKTarget;

    private void OnAnimatorIK(int layerIndex)
    {
        if (anim == null || isCampMode) return;

        // ‘≤ — Œœ“»Ã≤«¿÷≤Ø: “‡ÈÏÂ ‰Îˇ IK ÔÓ¯ÛÍÛ
        ikCheckTimer -= Time.deltaTime;
        if (ikCheckTimer <= 0f)
        {
            ikCheckTimer = 0.2f;
            ikTargetItem = null;
            Collider[] nearbyItems = Physics.OverlapSphere(transform.position, 5f);
            float minDist = float.MaxValue;

            foreach (var item in nearbyItems)
            {
                bool isValidTarget = false;

                XpCrystal crystal = item.GetComponent<XpCrystal>();
                if (crystal != null && crystal.IsMagnetized) isValidTarget = true;

                DiamondPickup diamond = item.GetComponent<DiamondPickup>();
                if (diamond != null && diamond.IsMagnetized) isValidTarget = true;

                if (isValidTarget)
                {
                    float d = Vector3.Distance(transform.position, item.transform.position);
                    if (d < minDist)
                    {
                        minDist = d;
                        ikTargetItem = item.transform;
                    }
                }
            }
        }

        if (ikTargetItem != null)
        {
            handIKWeight = Mathf.Lerp(handIKWeight, 0.8f, Time.deltaTime * 8f);
            handIKTarget = Vector3.Lerp(handIKTarget, ikTargetItem.position + Vector3.down * 0.2f, Time.deltaTime * 15f);
        }
        else
        {
            handIKWeight = Mathf.Lerp(handIKWeight, 0f, Time.deltaTime * 6f);
        }

        anim.SetIKPositionWeight(AvatarIKGoal.LeftHand, handIKWeight);
        anim.SetIKPosition(AvatarIKGoal.LeftHand, handIKTarget);
    }
}