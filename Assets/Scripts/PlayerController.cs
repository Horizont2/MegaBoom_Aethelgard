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

    // === LvlUp-driven RPG stats (extended in this polish pass) ===
    [HideInInspector] public float critDamageMultiplier = 2.5f;   // base crit mult; LvlUp adds on top
    [HideInInspector] public float lifeStealFraction = 0f;        // 0..1, % of finalDmg returned as HP
    [HideInInspector] public float dodgeChance = 0f;              // 0..1, roll on TakeDamage
    [HideInInspector] public float thornDamageFraction = 0f;      // 0..1, % of incoming dmg reflected
    [HideInInspector] public float killHealAmount = 0f;           // flat HP gained on enemy kill
    [HideInInspector] public float xpGainMultiplier = 1f;         // multiplier on GainXP
    [HideInInspector] public float diamondBonusMultiplier = 1f;   // multiplier on GainDiamond

    // Static so EnemyAI.Die() can broadcast without a per-frame find — single-player only.
    public static System.Action OnEnemyKilled;
    public static PlayerController LocalInstance { get; private set; }

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
    private Camera mainCameraCached;
    private Transform mainCameraTransformCached;
    private HealthVisuals healthVisuals;
    private CharacterController characterController;
    private Vector3 velocity;
    private Animator anim;
    private bool isDead = false;

    private Transform visualModel;
    private bool wasGroundedLastFrame = true;

    private static readonly Collider[] s_overlapBuffer = new Collider[64];

    // Բ��� ����̲��ֲ�
    private float stackCheckTimer = 0f;
    private float ikCheckTimer = 0f;
    private float focusCheckTimer = 0f;
    private Transform ikTargetItem = null;
    private Transform currentFocusEnemy = null;

    private void OnEnable()
    {
        if (!isCampMode) LocalInstance = this;
        OnEnemyKilled += HandleEnemyKilled;
    }

    private void OnDisable()
    {
        OnEnemyKilled -= HandleEnemyKilled;
        if (LocalInstance == this) LocalInstance = null;
    }

    private void HandleEnemyKilled()
    {
        if (isDead || isCampMode) return;
        if (killHealAmount > 0f && currentHealth > 0f && currentHealth < maxHealth)
        {
            Heal(killHealAmount);
        }
    }

    private float lastGroundedRealTime = -1f;
    // Window during which we still treat the player as grounded even if the
    // CharacterController briefly reports false. This kills the "stumbles on
    // a 5cm rock" bug where the animator flickered into Fall/T-pose for a
    // single frame each time the CC pushed over a small bump.
    private const float COYOTE_GROUND_WINDOW = 0.12f;

    private void Awake()
    {
        gameObject.layer = 8;
        Physics.IgnoreLayerCollision(8, 9, true);

        characterController = GetComponent<CharacterController>();
        if (characterController != null)
        {
            // Default prefab value is 0.3, which is too short for the terrain
            // detail in the levels — the CC tripped over roots and small
            // boulders instead of auto-stepping over them. 0.45 covers typical
            // knee-height bumps without letting the player walk up cliffs.
            if (characterController.stepOffset < 0.45f) characterController.stepOffset = 0.45f;
        }
        anim = GetComponentInChildren<Animator>();
        if (anim != null)
        {
            anim.applyRootMotion = false;
            visualModel = anim.transform;
        }

        // Cache Camera.main + its transform — each Camera.main call iterates all
        // tagged cameras in the scene, and PlayerController reads it ~16 times.
        mainCameraCached = Camera.main;
        if (mainCameraCached != null)
        {
            mainCameraTransformCached = mainCameraCached.transform;
            cameraFollow = mainCameraCached.GetComponent<CameraFollow>();
        }
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

        if (!isCampMode) StartCoroutine(FireOnboardingHints());
        else StartCoroutine(FireCampHints());
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

    private IEnumerator FireCampHints()
    {
        yield return new WaitForSecondsRealtime(2.5f);
        if (TutorialHints.Instance == null) yield break;
        TutorialHints.Instance.ShowIfNew("CampOverview",
            "Welcome to camp — your safe hub. Walk up to a building slot and press <b>F</b> to inspect or build. Pick missions at the Notice Board.");
    }

    private IEnumerator FireOnboardingHints()
    {
        // Give the world a beat to settle so the very first prompt doesn't fight
        // the title cinematic / region name reveal.
        yield return new WaitForSecondsRealtime(2.5f);
        if (TutorialHints.Instance == null) yield break;

        TutorialHints.Instance.ShowIfNew("Move",
            "WASD to move, mouse to look. Hold <b>SHIFT</b> to dash and slip past attacks.");

        if (grenadePrefab != null)
        {
            yield return new WaitForSecondsRealtime(8f);
            TutorialHints.Instance.ShowIfNew("Grenade",
                "Hold <b>G</b> to aim a grenade — releases when you let go. Slows time while aiming.");
        }
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

    // Բ�� ����̲��ֲ�: ������ ��� �������� �������
    private void CheckStack()
    {
        if (isCampMode) return;

        stackCheckTimer -= Time.deltaTime;
        if (stackCheckTimer > 0f) return;
        stackCheckTimer = 0.25f;

        int count = Physics.OverlapSphereNonAlloc(transform.position, stackRadius, s_overlapBuffer, 1 << 9);
        currentStack = 0;
        for (int i = 0; i < count; i++) { if (s_overlapBuffer[i].CompareTag("Enemy")) currentStack++; }

        if (currentStack >= 30) currentMultiplier = 5;
        else if (currentStack >= 20) currentMultiplier = 4;
        else if (currentStack >= 15) currentMultiplier = 2;
        else currentMultiplier = 1;

        if (currentMultiplier > 1 && TutorialHints.Instance != null)
            TutorialHints.Instance.ShowIfNew("Stack",
                "STACK = enemies near you. At 15+ you start dealing multiplied damage. At 30+ you become a typhoon — but you also lose acceleration.", 6f);

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
        int hitCount = Physics.OverlapSphereNonAlloc(transform.position, maxDist, s_overlapBuffer);
        Transform bestTarget = null;
        float minDist = float.MaxValue;

        Vector3 playerForward = transform.forward;
        playerForward.y = 0;

        for (int i = 0; i < hitCount; i++)
        {
            Collider hit = s_overlapBuffer[i];
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

            Vector3 ncForward = mainCameraTransformCached.forward;
            Vector3 ncRight = mainCameraTransformCached.right;

            Vector3 dir = (ncForward * v + ncRight * h + Vector3.up * up).normalized;
            transform.position += dir * noclipSpeed * Time.unscaledDeltaTime;
            return;
        }

        bool isCurrentlyLocked = isControlBlocked || Time.unscaledTime < actionLockEndTime || TutorialPanelUI.IsTutorialActive;
        Vector3 inputDir = Vector3.zero;

        if (!isCurrentlyLocked)
        {
            inputDir = new Vector3(Input.GetAxisRaw("Horizontal"), 0f, Input.GetAxisRaw("Vertical")).normalized;

            if (Input.GetKeyDown(KeyCode.LeftShift) && Time.unscaledTime >= lastDashTime + dashCooldown)
            {
                if (!isAimingGrenade)
                {
                    // PerfectDodge needs a live threat window which never opens
                    // in camp, so skip it there and go straight to a normal dash.
                    if (!isCampMode && dodgeWindowTimer > 0f) StartCoroutine(PerfectDodgeSequence(inputDir));
                    else StartCoroutine(DashRoutine(inputDir, false));
                }
            }
        }

        if (isDashing || mainCameraTransformCached == null) return;

        Vector3 camForward = mainCameraTransformCached.forward;
        Vector3 camRight = mainCameraTransformCached.right;
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
            // Բ�� ����̲��ֲ�: ������ ������ ��� ������ �� ����� ����
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

        bool ccGroundedThisFrame = characterController.isGrounded;
        if (ccGroundedThisFrame) lastGroundedRealTime = Time.unscaledTime;

        // Coyote-grounded: keep the player "grounded" for ~120ms after the CC
        // last touched ground, as long as they're not actively rising (i.e.
        // genuine jumps still register as airborne). This matches what
        // animation typically wants and stops the IsGrounded param from
        // flickering when the CC steps up over micro-bumps.
        bool isGroundedNow = ccGroundedThisFrame
            || (Time.unscaledTime - lastGroundedRealTime < COYOTE_GROUND_WINDOW && velocity.y <= 0.5f);

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

        // Ground-snap: if we're descending slowly and ground is right below
        // (within ~0.5m), pin the vertical velocity. This prevents the player
        // from "floating" off the top of small bumps for a few frames after a
        // step-up, which is what usually triggered the fall/T-pose animation.
        if (!ccGroundedThisFrame && velocity.y < 0f && velocity.y > -8f)
        {
            if (Physics.SphereCast(transform.position + Vector3.up * 0.4f, 0.2f, Vector3.down, out RaycastHit groundHit, 0.7f, GetGrenadeBlockerMask(), QueryTriggerInteraction.Ignore))
            {
                if (!groundHit.collider.CompareTag("Player")) velocity.y = -2f;
            }
        }

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

                            if (trajectoryLine != null)
                            {
                                trajectoryLine.positionCount = linePoints;
                                // Wipe out the prefab's alpha gradient (which
                                // faded the line to transparent toward the
                                // tail and made the arc look like it stopped
                                // halfway, disconnected from the AoE ring).
                                ResetTrajectoryGradient();
                            }
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

    // Layer mask used to detect both aim-raycast and trajectory collisions.
    // Cached so we don't rebuild it every frame while aiming.
    private static int s_grenadeAimMask = -1;
    private static int s_grenadeBlockerMask = -1;

    private static int GetGrenadeAimMask()
    {
        if (s_grenadeAimMask == -1)
            s_grenadeAimMask = LayerMask.GetMask("Default", "Terrain", "Ground");
        return s_grenadeAimMask;
    }

    private static int GetGrenadeBlockerMask()
    {
        if (s_grenadeBlockerMask == -1)
        {
            // GrenadeLogic explodes on collision with anything tagged non-Player,
            // so the prediction has to mirror that — including foliage. Without
            // Foliage in the mask, a tree mid-arc would silently swallow the
            // real grenade while the line happily drew through it.
            int mask = LayerMask.GetMask("Default", "Terrain", "Ground", "Foliage");
            // LayerMask.GetMask returns 0 for unknown layer names. If Foliage
            // doesn't exist in this project, fall back to the safe trio so the
            // mask isn't silently empty.
            if (mask == 0) mask = LayerMask.GetMask("Default", "Terrain", "Ground");
            s_grenadeBlockerMask = mask;
        }
        return s_grenadeBlockerMask;
    }

    private void UpdateGrenadeAiming()
    {
        Ray ray = mainCameraCached.ScreenPointToRay(Input.mousePosition);
        Vector3 hitPoint = transform.position + transform.forward * 5f;
        int aimMask = GetGrenadeAimMask();

        if (Physics.Raycast(ray, out RaycastHit hit, 100f, aimMask))
        {
            hitPoint = hit.point;
        }
        else
        {
            Plane groundPlane = new Plane(Vector3.up, new Vector3(0, transform.position.y, 0));
            if (groundPlane.Raycast(ray, out float enter)) hitPoint = ray.GetPoint(enter);
        }

        // Soft aim-magnet to nearby enemy — but keep the magnet purely horizontal
        // so the predicted landing stays grounded. The old code carried the
        // enemy's elevated Y into the target, which is half the reason the
        // marker drifted away from the real explosion.
        int magnetCount = Physics.OverlapSphereNonAlloc(hitPoint, aimAssistRadius, s_overlapBuffer);
        Transform bestTarget = null;
        float minDist = float.MaxValue;
        for (int mi = 0; mi < magnetCount; mi++)
        {
            Collider mHit = s_overlapBuffer[mi];
            if (mHit != null && mHit.CompareTag("Enemy"))
            {
                float d = Vector3.Distance(hitPoint, mHit.transform.position);
                if (d < minDist) { minDist = d; bestTarget = mHit.transform; }
            }
        }
        if (bestTarget != null)
        {
            Vector3 magneticTarget = new Vector3(bestTarget.position.x, hitPoint.y, bestTarget.position.z);
            hitPoint = Vector3.Lerp(hitPoint, magneticTarget, 0.4f);
        }

        // Clamp to throw range in XZ.
        Vector3 offset = hitPoint - transform.position;
        offset.y = 0;
        if (offset.magnitude > maxThrowDistance)
        {
            hitPoint = transform.position + offset.normalized * maxThrowDistance;
            // Re-ground the clamped point (sky aim) since we just moved it
            // horizontally and the previous raycast hit no longer applies.
            hitPoint = ProjectAimToGround(hitPoint);
        }

        // currentGrenadeTarget stays at the player's actual aim — this is what
        // the throw will use for its initial velocity, and what the marker
        // shows the player is committing to. If the trajectory then clips a
        // wall, the AoE preview moves to the clip point (markerPosition) but
        // the throw still resolves identically because the same velocity is
        // simulated above and applied below.
        currentGrenadeTarget = hitPoint;

        // Simulate the throw with that target and find the real landing point.
        // The line writes points along the simulated arc; markerPosition is
        // where the grenade actually stops.
        Vector3 markerPosition;
        int simulatedCount = SimulateTrajectoryToLanding(currentGrenadeTarget, out markerPosition);

        int blastCount = Physics.OverlapSphereNonAlloc(markerPosition, grenadeExplosionRadius, s_overlapBuffer);
        bool enemyInBlast = false;
        for (int bi = 0; bi < blastCount; bi++)
        {
            if (s_overlapBuffer[bi] != null && s_overlapBuffer[bi].CompareTag("Enemy")) { enemyInBlast = true; break; }
        }

        Color currentAimColor = enemyInBlast ? new Color(1f, 0.1f, 0.1f, 0.8f) : new Color(0f, 0.8f, 1f, 0.8f);

        if (trajectoryLine != null)
        {
            trajectoryLine.startColor = currentAimColor;
            trajectoryLine.endColor = currentAimColor;
            trajectoryLine.positionCount = simulatedCount;
            if (trajectoryLine.material != null) trajectoryLine.material.mainTextureOffset -= new Vector2(Time.unscaledDeltaTime * 2.5f, 0);
        }

        if (aoeMarkerLine != null) { aoeMarkerLine.startColor = currentAimColor; aoeMarkerLine.endColor = currentAimColor; }
        if (innerMarkerLine != null) { innerMarkerLine.startColor = currentAimColor; innerMarkerLine.endColor = currentAimColor; }

        // AoE ring sits on the *real* landing — i.e. where the grenade will
        // actually explode after physics, not the player's raw aim point.
        DrawAoEMarker(markerPosition);

        Vector3 aimDir = (currentGrenadeTarget - transform.position).normalized;
        aimDir.y = 0;
        if (aimDir.sqrMagnitude > 0.01f)
        {
            transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(aimDir), rotationSpeed * Time.unscaledDeltaTime * 3f);
        }
    }

    // Snaps an XZ aim point to whatever ground is directly below it. Falls back
    // to terrain height, then to the original point if nothing was found.
    private Vector3 ProjectAimToGround(Vector3 worldPoint)
    {
        Vector3 from = worldPoint + Vector3.up * 30f;
        if (Physics.Raycast(from, Vector3.down, out RaycastHit groundHit, 100f, GetGrenadeBlockerMask()))
        {
            return groundHit.point;
        }
        if (Terrain.activeTerrain != null)
        {
            float ty = Terrain.activeTerrain.SampleHeight(worldPoint) + Terrain.activeTerrain.transform.position.y;
            return new Vector3(worldPoint.x, ty, worldPoint.z);
        }
        return worldPoint;
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

    // Physics-accurate trajectory simulation that mirrors what the live grenade
    // will do: it ballisticly steps with gravity, sphere-casts each segment
    // against terrain/world geometry, and writes the actual flight path into
    // trajectoryLine. Returns the final point count.
    //
    // Returning the simulated landing instead of just drawing it lets callers
    // realign the AoE marker (and the throw itself) so visuals can never lie
    // about where the grenade lands.
    private static readonly RaycastHit[] s_grenadeSimHitBuffer = new RaycastHit[8];
    private bool trajectoryGradientReset = false;

    // The trajectoryLine prefab ships with an alpha gradient that tapers to
    // zero, intended for a stylish "magic trail" head. While useful when the
    // line is a fixed-length flair, it makes the arc look chopped off in the
    // middle when the line traces a real ballistic path — the player sees a
    // bright stub near their hand and an apparently disconnected AoE ring at
    // the landing. Force a flat solid gradient once the first time we aim so
    // the whole arc stays visible.
    private void ResetTrajectoryGradient()
    {
        if (trajectoryGradientReset || trajectoryLine == null) return;
        Gradient flat = new Gradient();
        flat.SetKeys(
            new[]
            {
                new GradientColorKey(Color.white, 0f),
                new GradientColorKey(Color.white, 1f),
            },
            new[]
            {
                new GradientAlphaKey(1f, 0f),
                new GradientAlphaKey(1f, 1f),
            });
        trajectoryLine.colorGradient = flat;
        trajectoryGradientReset = true;
    }

    // Walks up the parent chain to see if the collider belongs to the player.
    // CompareTag("Player") alone misses children (weapon, armor pieces, etc.)
    // that are typically untagged but still inside the player hierarchy.
    private bool IsColliderPartOfPlayer(Collider c)
    {
        if (c == null) return false;
        Transform t = c.transform;
        while (t != null)
        {
            if (t == this.transform) return true;
            t = t.parent;
        }
        return c.CompareTag("Player");
    }

    private int SimulateTrajectoryToLanding(Vector3 requestedTarget, out Vector3 landing)
    {
        Vector3 start = throwPoint != null ? throwPoint.position : transform.position + Vector3.up;
        Vector3 vel = CalculateThrowVelocity(requestedTarget);

        const int MAX_STEPS = 64;
        const float STEP_TIME = 0.04f;
        const float COLLISION_RADIUS = 0.15f;

        int blockerMask = GetGrenadeBlockerMask();
        Vector3 prev = start;

        // Pre-size the line buffer so SetPosition() calls don't fail; caller
        // will trim positionCount down to the actual writtenPoints below.
        if (trajectoryLine != null && trajectoryLine.positionCount < MAX_STEPS)
            trajectoryLine.positionCount = MAX_STEPS;

        // First point is the throw origin.
        if (trajectoryLine != null) trajectoryLine.SetPosition(0, prev);

        int writtenPoints = 1;
        for (int i = 1; i < MAX_STEPS; i++)
        {
            float t = i * STEP_TIME;
            Vector3 next = start + vel * t + Physics.gravity * 0.5f * t * t;

            Vector3 segDir = next - prev;
            float segDist = segDir.magnitude;
            if (segDist > 0.0001f)
            {
                Vector3 segDirN = segDir / segDist;
                int hitCount = Physics.SphereCastNonAlloc(prev, COLLISION_RADIUS, segDirN, s_grenadeSimHitBuffer, segDist, blockerMask, QueryTriggerInteraction.Ignore);
                if (hitCount > 0)
                {
                    // Scan for the closest hit that isn't part of the player.
                    // The old code only checked CompareTag("Player"), which
                    // missed untagged child colliders (weapon, armor, hand
                    // bone box collider, etc.), so the trajectory used to
                    // terminate on the first step against the player's own
                    // weapon and the marker fell behind the visible arc.
                    float bestDist = float.MaxValue;
                    int bestIdx = -1;
                    for (int h = 0; h < hitCount; h++)
                    {
                        RaycastHit hh = s_grenadeSimHitBuffer[h];
                        if (hh.collider == null) continue;
                        if (IsColliderPartOfPlayer(hh.collider)) continue;
                        // SphereCast returns 0 distance / zero point when the
                        // cast started overlapping the collider — treat as a
                        // glancing self-overlap and skip too.
                        if (hh.distance <= 0.0001f) continue;
                        if (hh.distance < bestDist) { bestDist = hh.distance; bestIdx = h; }
                    }

                    if (bestIdx >= 0)
                    {
                        RaycastHit hit = s_grenadeSimHitBuffer[bestIdx];
                        Vector3 hitPoint = hit.point;
                        if (trajectoryLine != null) trajectoryLine.SetPosition(writtenPoints, hitPoint);
                        writtenPoints++;

                        // Visual drop marker: append a short vertical segment
                        // from the hit point down to the ground so the line
                        // unmistakably meets the AoE ring (which always sits
                        // at ground level). Without this, hits on slopes/wall
                        // sides looked detached from the marker.
                        float groundY = GetGroundHeight(hitPoint);
                        if (hitPoint.y - groundY > 0.05f && trajectoryLine != null)
                        {
                            trajectoryLine.SetPosition(writtenPoints, new Vector3(hitPoint.x, groundY + 0.1f, hitPoint.z));
                            writtenPoints++;
                        }

                        // Marker sits on the ground directly under the impact
                        // so it always reads as "where the AoE goes off."
                        landing = new Vector3(hitPoint.x, groundY, hitPoint.z);
                        return writtenPoints;
                    }
                }
            }

            if (trajectoryLine != null) trajectoryLine.SetPosition(writtenPoints, next);
            writtenPoints++;
            prev = next;

            // Hard safety: if we somehow drop way below the throw origin (deep
            // pit or off-map), stop here so we don't trace into the void.
            if (next.y < start.y - 50f)
            {
                float groundY = GetGroundHeight(next);
                landing = new Vector3(next.x, groundY, next.z);
                return writtenPoints;
            }
        }

        // Sim exhausted without a collision (very long, very flat arc). Drop
        // the marker straight down from the last simulated point.
        float exitGroundY = GetGroundHeight(prev);
        landing = new Vector3(prev.x, exitGroundY, prev.z);
        return writtenPoints;
    }

    private void DrawAoEMarker(Vector3 center)
    {
        int blockerMask = GetGrenadeBlockerMask();

        if (aoeMarkerLine != null)
        {
            int segments = aoeMarkerLine.positionCount;
            float angle = 0f;
            for (int i = 0; i < segments; i++)
            {
                float x = Mathf.Sin(Mathf.Deg2Rad * angle) * grenadeExplosionRadius;
                float z = Mathf.Cos(Mathf.Deg2Rad * angle) * grenadeExplosionRadius;

                // ФІКС: Пускаємо промінь лише на 2 метри вище центру, а не на 20, 
                // щоб коло не малювалось на деревах або дахах будівель!
                Vector3 point = center + new Vector3(x, 2f, z);
                if (Physics.Raycast(point, Vector3.down, out RaycastHit hit, 6f, blockerMask))
                    point.y = hit.point.y + 0.15f;
                else
                    point.y = center.y + 0.15f;

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

                Vector3 point = center + new Vector3(x, 2f, z);
                if (Physics.Raycast(point, Vector3.down, out RaycastHit hit, 6f, blockerMask))
                    point.y = hit.point.y + 0.15f;
                else
                    point.y = center.y + 0.15f;

                innerMarkerLine.SetPosition(i, point);
                angle += (360f / segments);
            }
        }
    }

    private float GetGroundHeight(Vector3 pos)
    {
        // ФІКС: Скануємо лише на 1 метр вгору і 5 вниз, щоб не хапати дахи та гілки!
        if (Physics.Raycast(pos + Vector3.up * 1f, Vector3.down, out RaycastHit hit, 5f, GetGrenadeBlockerMask()))
        {
            return hit.point.y;
        }
        else if (Terrain.activeTerrain != null)
        {
            return Terrain.activeTerrain.SampleHeight(pos) + Terrain.activeTerrain.transform.position.y;
        }

        return pos.y;
    }

    public void OpenPerfectDodgeWindow(Transform attacker, float duration)
    {
        dodgeWindowTimer = duration;

        if (TutorialHints.Instance != null)
            TutorialHints.Instance.ShowIfNew("PerfectDodge",
                "ELITE windup detected. Dash (<b>SHIFT</b>) right as their flash peaks to trigger Perfect Dodge — guaranteed crit + slow-mo.", 6f);
    }

    private IEnumerator PerfectDodgeSequence(Vector3 fallbackDirection)
    {
        dodgeWindowTimer = 0f;
        isNextAttackGuaranteedCrit = true;
        isBulletTime = true;

        if (AudioManager.Instance != null) AudioManager.Instance.PlaySFX(AudioID.Player_PerfectDodge);

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

        float originalFOV = mainCameraCached.fieldOfView;
        mainCameraCached.fieldOfView = originalFOV + 20f;
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
            mainCameraCached.fieldOfView = Mathf.Lerp(originalFOV + 20f, originalFOV, elapsed / 0.3f);
            yield return null;
        }
        mainCameraCached.fieldOfView = originalFOV;
    }

    private IEnumerator DashRoutine(Vector3 direction, bool isPerfectDodge = false)
    {
        isDashing = true;
        lastDashTime = Time.unscaledTime;
        float startTime = Time.realtimeSinceStartup;

        if (AudioManager.Instance != null && !isPerfectDodge) AudioManager.Instance.PlaySFX(AudioID.Player_Dash);

        float originalFOV = mainCameraCached.fieldOfView;
        float targetFOV = originalFOV + (isPerfectDodge ? 20f : 12f);

        if (dashParticles != null) dashParticles.Play();
        if (cameraFollow != null) cameraFollow.TriggerShake(0.15f, 0.2f);

        if (direction == Vector3.zero) direction = transform.forward;
        else
        {
            Vector3 camForward = mainCameraTransformCached.forward; Vector3 camRight = mainCameraTransformCached.right;
            camForward.y = 0f; camRight.y = 0f;
            direction = (camForward * direction.z + camRight * direction.x).normalized;
        }

        float currentDashSpeed = isPerfectDodge ? dashSpeed * 1.5f : dashSpeed;

        while (Time.realtimeSinceStartup < startTime + dashDuration)
        {
            float normalizedTime = (Time.realtimeSinceStartup - startTime) / dashDuration;
            float curve = Mathf.Sin(normalizedTime * Mathf.PI);

            characterController.Move(direction * currentDashSpeed * curve * Time.unscaledDeltaTime);
            mainCameraCached.fieldOfView = Mathf.Lerp(mainCameraCached.fieldOfView, targetFOV, normalizedTime);

            yield return null;
        }

        isDashing = false;

        float elapsed = 0f;
        while (elapsed < 0.3f)
        {
            elapsed += Time.unscaledDeltaTime;
            mainCameraCached.fieldOfView = Mathf.Lerp(targetFOV, originalFOV, elapsed / 0.3f);
            yield return null;
        }
        mainCameraCached.fieldOfView = originalFOV;
    }

    public void ExecuteAttack()
    {
        if (meleePoint == null || isCampMode) return;
        if (AudioManager.Instance != null) AudioManager.Instance.PlaySFX(AudioID.Player_Swing);

        if (TutorialHints.Instance != null)
            TutorialHints.Instance.ShowIfNew("Attack",
                "Hold <b>LMB</b> to chain melee swings. Killing enemies grows the STACK — every 15 stacks adds a damage multiplier.", 6f);

        if (cameraFollow != null) cameraFollow.SetCombatState();

        int hitCount = Physics.OverlapSphereNonAlloc(meleePoint.position, meleeRadius, s_overlapBuffer);
        bool hitEnemy = false; bool hitResource = false;
        bool isCriticalHit = isNextAttackGuaranteedCrit || Random.value <= globalCritChance;

        float finalDmg = meleeDamage * globalDamageMultiplier;
        // critDamageMultiplier is the LvlUp-scaled normal crit. Guaranteed crit
        // (e.g. perfect dodge) gets a fixed 1.4x bonus on top, so investing in
        // CritDamage always feels like an upgrade.
        if (isCriticalHit) finalDmg *= (isNextAttackGuaranteedCrit ? critDamageMultiplier * 1.4f : critDamageMultiplier);

        float totalLifestealDealt = 0f;

        for (int idx = 0; idx < hitCount; idx++)
        {
            Collider col = s_overlapBuffer[idx];
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
                if (col.CompareTag("Enemy")) { hitEnemy = true; totalLifestealDealt += finalDmg; }
                else hitResource = true;

                if (hitSparkVFXPrefab != null && ObjectPoolManager.Instance != null)
                {
                    Quaternion hitRot = pushDir != Vector3.zero ? Quaternion.LookRotation(pushDir) : Quaternion.identity;
                    GameObject vfx = ObjectPoolManager.Instance.SpawnFromPool(hitSparkVFXPrefab, hitInfo.HitPoint, hitRot);
                    if (vfx != null) vfx.transform.localScale = isNextAttackGuaranteedCrit ? Vector3.one * 3f : (isCriticalHit ? Vector3.one * 1.5f : Vector3.one);
                }
            }
        }

        isNextAttackGuaranteedCrit = false;

        if (lifeStealFraction > 0f && totalLifestealDealt > 0f && currentHealth < maxHealth)
        {
            Heal(totalLifestealDealt * lifeStealFraction);
        }

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

            // === ФІКС ФІЗИКИ 1: Щоб граната не врізалась у самого гравця при спавні і не відбивалась під ноги ===
            Collider grenadeCol = grenade.GetComponent<Collider>();
            Collider playerCol = GetComponent<Collider>();
            if (grenadeCol != null && playerCol != null)
            {
                Physics.IgnoreCollision(grenadeCol, playerCol, true);
            }

            Rigidbody rb = grenade.GetComponent<Rigidbody>();
            if (rb != null)
            {
                // === ФІКС ФІЗИКИ 2: Вимикаємо опір повітря (Drag), бо він гальмує політ і ламає математичну траєкторію ===
                rb.linearDamping = 0f;

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

        if (dodgeChance > 0f && UnityEngine.Random.value < dodgeChance)
        {
            // Treat dodge identically to a perfect dodge — visuals/SFX already
            // exist for that path, so the player feels the upgrade firing.
            if (AudioManager.Instance != null) AudioManager.Instance.PlaySFX(AudioID.Player_PerfectDodge);
            isNextAttackGuaranteedCrit = true;
            return;
        }

        float finalDamage = info.Amount * (1f - damageReduction);
        currentHealth -= finalDamage;

        if (thornDamageFraction > 0f)
        {
            float reflected = finalDamage * thornDamageFraction;
            // Attacker not exposed on DamageInfo, so apply as AoE thorn around player.
            int n = Physics.OverlapSphereNonAlloc(transform.position, 2.5f, s_overlapBuffer);
            for (int i = 0; i < n; i++)
            {
                Collider col = s_overlapBuffer[i];
                if (col == null || col.gameObject == this.gameObject) continue;
                if (!col.CompareTag("Enemy")) continue;
                if (col.TryGetComponent(out IDamageable d))
                {
                    d.TakeDamage(new DamageInfo { Amount = reflected, IsCritical = false, KnockbackForce = 0f, StunDuration = 0f, PushDirection = Vector3.zero, HitPoint = col.transform.position });
                }
            }
        }

        if (AudioManager.Instance != null) AudioManager.Instance.PlaySFX(AudioID.Player_Hurt);

        if (cameraFollow != null)
        {
            // --- ��� Բ�� ������ ������ ---
            // ���������: 0.15� (���� �������). 
            // ��������� ���: 0.08f (������ ����� �� ����, � ������ ���� ��������).
            // ��������: ���� ����� �� ������� ��������, �������� ������ ������ ����� (-transform.forward).
            Vector3 shakeDir = info.PushDirection != Vector3.zero ? info.PushDirection : -transform.forward;
            cameraFollow.TriggerDirectionalShake(shakeDir, 1.2f, 0.15f, 0.08f);
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

    public void GainXP(float amount)
    {
        if (isCampMode) return;
        float scaled = amount * xpGainMultiplier;
        currentXP += scaled;

        if (GlobalHUD.Instance != null && scaled > 0f)
            GlobalHUD.Instance.ShowPickupPopup($"+{Mathf.CeilToInt(scaled)} XP", new Color(0.4f, 0.85f, 1f));

        // Loop in case a single huge gain crosses multiple thresholds.
        while (currentXP >= xpToNextLevel) LevelUp();
    }

    public void GainDiamond(int amount = 1)
    {
        int finalAmount = Mathf.Max(amount, Mathf.RoundToInt(amount * diamondBonusMultiplier));
        crystalsCollected += finalAmount;
        if (ResourceManager.Instance != null) { ResourceManager.Instance.diamonds += finalAmount; ResourceManager.Instance.SaveStash(); ResourceManager.Instance.UpdateUI(); }
        else { int currentDiamonds = PlayerPrefs.GetInt("PlayerDiamonds", 0); PlayerPrefs.SetInt("PlayerDiamonds", currentDiamonds + finalAmount); PlayerPrefs.Save(); }
        if (AudioManager.Instance != null) AudioManager.Instance.PlaySFX(AudioID.Camp_CollectGem);

        if (GlobalHUD.Instance != null)
            GlobalHUD.Instance.ShowPickupPopup($"+{finalAmount} Diamond", new Color(0.85f, 0.55f, 1f));

        UpdateHUD();
        if (MissionManager.Instance != null) MissionManager.Instance.AddProgress(MissionType.CollectCrystals, finalAmount);
    }

    // Smooth quadratic XP curve. Replaces the prior 1.5x multiplier — at level 15
    // that curve cost ~9.7k xp, which made any post-mid-game run dead. New curve:
    //   L1->2: 50  L5: ~140  L10: ~340  L15: ~640  L20: ~1040  L25: ~1540
    public static float ComputeXpToNextLevel(int level)
    {
        // level is the level you ARE on (i.e. need this much to reach level+1).
        return 40f + level * level * 4f + level * 6f;
    }

    private void LevelUp()
    {
        currentXP -= xpToNextLevel;
        currentLevel++;
        xpToNextLevel = ComputeXpToNextLevel(currentLevel);
        visualXP = 0f;

        if (AudioManager.Instance != null) AudioManager.Instance.PlayUI(AudioID.UI_LevelUp);

        // Level-up rewards beyond the upgrade choice itself:
        // - heal a chunk of HP so the player isn't punished for leveling mid-fight
        // - drip-feed diamonds so progression always tangibly rewards XP
        // - milestone gifts at L5/L10/L15/... — bigger heal + diamond cache
        Heal(maxHealth * 0.4f);
        int diamondReward = 3 + currentLevel;
        bool milestone = currentLevel % 5 == 0;
        if (milestone)
        {
            diamondReward += 25;
            maxHealth += 10f;
            currentHealth = maxHealth;
            if (GlobalHUD.Instance != null)
                GlobalHUD.Instance.ShowPickupPopup($"MILESTONE LV{currentLevel}: +10 Max HP", new Color(1f, 0.85f, 0.3f));
        }
        GainDiamond(diamondReward);

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

    public void Heal(float amount)
    {
        if (amount <= 0f || isDead) return;
        float before = currentHealth;
        currentHealth += amount;
        if (currentHealth > maxHealth) currentHealth = maxHealth;
        float actuallyHealed = currentHealth - before;
        // Suppress popup spam from per-hit lifesteal/kill-heal; only show for
        // meaningful chunks (level-up reward, big consumable, milestone).
        if (actuallyHealed >= 5f && !isCampMode)
        {
            if (GlobalHUD.Instance != null)
                GlobalHUD.Instance.ShowPickupPopup($"+{Mathf.CeilToInt(actuallyHealed)} HP", new Color(0.55f, 1f, 0.55f));
        }
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

        // Բ�� ����̲��ֲ�: ������ ��� IK ������
        ikCheckTimer -= Time.deltaTime;
        if (ikCheckTimer <= 0f)
        {
            ikCheckTimer = 0.2f;
            ikTargetItem = null;
            int nearbyCount = Physics.OverlapSphereNonAlloc(transform.position, 5f, s_overlapBuffer);
            float minDist = float.MaxValue;

            for (int ni = 0; ni < nearbyCount; ni++)
            {
                Collider item = s_overlapBuffer[ni];
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