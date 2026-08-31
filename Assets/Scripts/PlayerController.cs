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
    // Set during victory / defeat cinematics: the player can't act, so lingering
    // enemies must not be able to kill them while they watch the flythrough.
    [HideInInspector] public bool isCinematicInvincible = false;
    private Coroutine activeFlashRoutine;
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

    // Cached HUD fill values so the per-frame Update() can skip the
    // fillAmount setter when nothing changed. Writing the setter every
    // frame triggers a CanvasRenderer rebuild even with identical
    // values, which shows up in the Profiler as Canvas.SendWillRenderCanvases.
    private float lastHpFill = -1f;
    private float lastHpCatchupFill = -1f;
    private float lastXpFill = -1f;
    private float lastDashStaminaFill = -1f;

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

    // === Gear-stat wiring tunables ===
    // Armor's HP bonus + damage reduction and the weapon's crit + attack speed
    // used to be written by the shop but never applied — gear only moved the
    // Power number. These scale the now-live effects so a full set is strong
    // but not invincible. Tune here after playtesting.
    private const float ARMOR_HP_SCALE = 0.5f;       // fraction of raw armor HP applied
    private const float ARMOR_DR_SCALE = 0.4f;       // fraction of raw armor reduction applied
    private const float MAX_TOTAL_DR = 0.65f;        // hard cap on damage reduction from gear
    private const float ATTACK_SPEED_SCALE = 0.5f;   // dampening on the weapon attack-speed bonus
    private const float MIN_ATTACK_COOLDOWN = 0.28f; // floor so attacks can't get trivially fast
    private const float MAX_CRIT_CHANCE = 0.85f;     // clamp so maxed crit stays below certainty
    private float baseAttackCooldown = 0f;           // captured inspector cooldown before gear scaling

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

    [Header("Game-Feel VFX (Hovl pack — assigned on the Player prefab)")]
    [Tooltip("Golden star aura burst on level-up.")]
    public GameObject levelUpVFX;
    [Tooltip("Aura pulse when the STACK first hits 15 (damage starts multiplying).")]
    public GameObject stack15VFX;
    [Tooltip("Lightning aura when the STACK first hits 30 (typhoon tier).")]
    public GameObject stack30VFX;
    [Tooltip("Green heal aura when HP is restored.")]
    public GameObject healVFX;
    [Tooltip("Red spark burst when the player takes a hit.")]
    public GameObject hitVFX;
    // Stack-milestone edge tracking so the aura fires once on crossing, not every frame.
    private bool stack15Fired = false;
    private bool stack30Fired = false;

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

    // Horizontal world-space velocity (m/s). Used by ranged enemies to LEAD
    // their shots at where the player is going, not where they are.
    public Vector3 HorizontalVelocity =>
        characterController != null ? new Vector3(characterController.velocity.x, 0f, characterController.velocity.z) : Vector3.zero;

    private bool isDead = false;
    public bool IsDead => isDead;

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
        // Always register, even in camp mode. The low-health vignette
        // already gates itself on isCampMode internally; GlobalHUD's
        // hide-on-map-open path needs to find the camp player to walk
        // its hpFill / dashStaminaFill up to the right canvas root,
        // and that lookup was returning null when this gate excluded
        // camp instances — leaving the stamina bar visible behind the
        // region map.
        LocalInstance = this;
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
    private const float COYOTE_GROUND_WINDOW = 0.2f;

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
        if (characterController == null) return;
        Vector3 horizontalVel = new Vector3(characterController.velocity.x, 0, characterController.velocity.z);
        if (!characterController.isGrounded || horizontalVel.sqrMagnitude <= 0.1f) return;

        if (runDustParticles != null) runDustParticles.Emit(1);

        // ФІКС: Прибрано !isCampMode, щоб звук відтворювався всюди
        if (AudioManager.Instance != null && !isDead)
        {
            // Використовуємо 3D версію і передаємо позицію ніг гравця
            AudioManager.Instance.PlaySFX3D(AudioID.Player_Footstep, transform.position);
            lastAnimFootstepTime = Time.unscaledTime;
        }
    }

    // Fallback footstep trigger for movement not driven by an animator
    // event. Fires at a travel-distance interval that scales with speed
    // so walk and sprint stay rhythmically believable. Suppressed if
    // the animation events have been firing recently — no double taps.
    private float lastAnimFootstepTime = -10f;
    private float footstepDistanceAccum;
    private Vector3 footstepLastPos;
    private void UpdateFootstepFallback(bool grounded)
    {
        // ФІКС: Прибрано isCampMode з умови переривання
        if (!grounded || isDead) { footstepDistanceAccum = 0f; footstepLastPos = transform.position; return; }
        if (Time.timeScale <= 0.01f) return;

        if (Time.unscaledTime - lastAnimFootstepTime < 1.0f) { footstepLastPos = transform.position; return; }

        Vector3 delta = transform.position - footstepLastPos;
        delta.y = 0f;
        footstepLastPos = transform.position;

        float dist = delta.magnitude;
        if (dist < 0.001f) return;
        footstepDistanceAccum += dist;

        float stride = currentVelocityMove.magnitude > moveSpeed * 0.8f ? 2.4f : 1.9f;
        if (footstepDistanceAccum >= stride)
        {
            footstepDistanceAccum = 0f;
            if (AudioManager.Instance != null)
                AudioManager.Instance.PlaySFX3D(AudioID.Player_Footstep, transform.position);
        }
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
            // Weapons ship without a trail — build a clean AAA one so swings read.
            if (weaponTrail == null) weaponTrail = CreateWeaponTrail(currentWeapon);
            if (weaponTrail != null) weaponTrail.emitting = false;
        }
    }

    // Builds a short, tapered blade trail near the weapon tip. Additive + soft
    // so it glows subtly under bloom without smearing the screen. Emitting is
    // driven per-swing by TriggerWeaponTrail (and the StartSwing/EndSwing anim
    // events if the clip has them).
    private TrailRenderer CreateWeaponTrail(GameObject weapon)
    {
        if (weapon == null) return null;
        // Anchor at the ACTUAL blade tip. The old code hardcoded 1m up +Y, which
        // only fit the starter sword — longer/shorter bought weapons put the
        // trail point off the blade, so their trail read as crooked. Measure the
        // weapon's combined mesh bounds and anchor at the top-centre, converted
        // to weapon-local so it tracks the blade.
        var tip = new GameObject("BladeTrailPoint");
        tip.transform.SetParent(weapon.transform, false);
        // Measure ONLY the solid blade mesh. The previous version used
        // GetComponentsInChildren<Renderer>(), which also catches the weapon's
        // particle/trail/glow renderers — their huge bounds flung the anchor far
        // off the blade, giving the "enormous, curled" trail on some weapons.
        Vector3 localTip = new Vector3(0f, 0.9f, 0f);
        Bounds b = default; bool has = false;
        foreach (var mr in weapon.GetComponentsInChildren<MeshRenderer>())
        { if (mr == null) continue; if (!has) { b = mr.bounds; has = true; } else b.Encapsulate(mr.bounds); }
        foreach (var smr in weapon.GetComponentsInChildren<SkinnedMeshRenderer>())
        { if (smr == null) continue; if (!has) { b = smr.bounds; has = true; } else b.Encapsulate(smr.bounds); }
        if (has)
        {
            Vector3 worldTip = new Vector3(b.center.x, b.max.y, b.center.z);
            localTip = weapon.transform.InverseTransformPoint(worldTip);
            // Clamp to a sane blade-tip window so an odd mesh can't blow the
            // trail out to a giant arc.
            localTip = new Vector3(Mathf.Clamp(localTip.x, -0.25f, 0.25f),
                                   Mathf.Clamp(localTip.y, 0.45f, 1.4f),
                                   Mathf.Clamp(localTip.z, -0.25f, 0.25f));
        }
        tip.transform.localPosition = localTip;

        var tr = tip.AddComponent<TrailRenderer>();
        tr.time = 0.11f;                       // tighter ribbon, no lingering curl
        tr.minVertexDistance = 0.03f;
        tr.numCornerVertices = 2;
        tr.numCapVertices = 2;
        tr.autodestruct = false;
        tr.emitting = false;
        tr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        tr.receiveShadows = false;
        tr.alignment = LineAlignment.View;

        // Taper the width to a point — thinner so it reads as a blade edge.
        tr.widthCurve = new AnimationCurve(new Keyframe(0f, 0.09f), new Keyframe(1f, 0f));

        // Soft cool-white ribbon fading out.
        var grad = new Gradient();
        grad.SetKeys(
            new[] { new GradientColorKey(new Color(0.8f, 0.9f, 1f), 0f), new GradientColorKey(new Color(0.5f, 0.7f, 1f), 1f) },
            new[] { new GradientAlphaKey(0.55f, 0f), new GradientAlphaKey(0f, 1f) });
        tr.colorGradient = grad;

        Shader sh = Shader.Find("Universal Render Pipeline/Particles/Unlit") ?? Shader.Find("Sprites/Default");
        var mat = new Material(sh);
        if (mat.HasProperty("_Surface")) mat.SetFloat("_Surface", 1f);
        if (mat.HasProperty("_Blend")) mat.SetFloat("_Blend", 1f);
        if (mat.HasProperty("_SrcBlend")) mat.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
        if (mat.HasProperty("_DstBlend")) mat.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.One);
        if (mat.HasProperty("_ZWrite")) mat.SetFloat("_ZWrite", 0f);
        mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
        tr.material = mat;

        // Keep it off the minimap like the other effects.
        VFXAutoFade.HideFromMinimap(tip);
        return tr;
    }

    private Coroutine weaponTrailRoutine;

    private void TriggerWeaponTrail(bool crit)
    {
        if (weaponTrail == null) return;
        // Warmer + slightly stronger on a crit.
        var grad = new Gradient();
        if (crit)
            grad.SetKeys(
                new[] { new GradientColorKey(new Color(1f, 0.85f, 0.4f), 0f), new GradientColorKey(new Color(1f, 0.5f, 0.15f), 1f) },
                new[] { new GradientAlphaKey(0.8f, 0f), new GradientAlphaKey(0f, 1f) });
        else
            grad.SetKeys(
                new[] { new GradientColorKey(new Color(0.8f, 0.9f, 1f), 0f), new GradientColorKey(new Color(0.5f, 0.7f, 1f), 1f) },
                new[] { new GradientAlphaKey(0.55f, 0f), new GradientAlphaKey(0f, 1f) });
        weaponTrail.colorGradient = grad;

        weaponTrail.Clear();
        weaponTrail.emitting = true;
        if (weaponTrailRoutine != null) StopCoroutine(weaponTrailRoutine);
        weaponTrailRoutine = StartCoroutine(StopWeaponTrailRoutine());
    }

    private System.Collections.IEnumerator StopWeaponTrailRoutine()
    {
        // Emit only for the swing window, then stop so idle poses draw nothing.
        yield return new WaitForSeconds(0.22f);
        if (weaponTrail != null) weaponTrail.emitting = false;
        weaponTrailRoutine = null;
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
        AchievementSystem.Unlock("HOMESTEAD");
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

        // Teach the PRIMARY weapon upfront. The old flow only surfaced this the
        // first time the player happened to press LMB, so a new player was told
        // to dash and throw grenades but never that LMB is their main attack.
        // Same "Attack" key AND identical string as the on-swing hint, so that
        // one dedupes away and this reuses the existing localized entry.
        yield return new WaitForSecondsRealtime(4f);
        TutorialHints.Instance.ShowIfNew("Attack",
            "Hold <b>LMB</b> to chain melee swings. Killing enemies grows the STACK — every 15 stacks adds a damage multiplier.", 6f);

        if (grenadePrefab != null)
        {
            yield return new WaitForSecondsRealtime(8f);
            TutorialHints.Instance.ShowIfNew("Grenade",
                "Hold <b>RMB</b> to aim a grenade. Time slows while aiming. Release to throw.");
        }
    }

    private void ApplyMetaUpgrades()
    {
        // The "Meta" perk levels (MetaHealth/Speed/Magnet/Armor/Damage) had no
        // purchase UI anywhere — SetUpgradeLevel was never called, so they were
        // always 0 and only inflated the Power score with phantom levels. The
        // dead system was removed; progression comes from gear + the forge.
        // Base combat multiplier starts neutral (was 1 + MetaDamage*0.1).
        globalDamageMultiplier = 1f;

        float weaponDmgBonus = PlayerPrefs.GetFloat("EquippedWeaponDamage", 0f);
        meleeDamage += weaponDmgBonus;

        // Weapon crit — now sourced from the equipped weapon (the shop finally
        // writes EquippedWeaponCrit; before, this key was never set so crit was
        // permanently the 5% default and weapon critChance/perLevel were dead).
        globalCritChance = Mathf.Clamp(PlayerPrefs.GetFloat("EquippedWeaponCrit", 0.05f), 0f, MAX_CRIT_CHANCE);

        // Weapon attack speed → attack cooldown. Normalised against the starter
        // weapon's 1.0 so only the bonus ABOVE baseline speeds you up, dampened
        // and floored so the best weapon can't trivialise the attack loop.
        if (baseAttackCooldown <= 0f) baseAttackCooldown = attackCooldown;
        float atkSpd = PlayerPrefs.GetFloat("EquippedWeaponAttackSpeed", 1f);
        float atkSpdBonus = Mathf.Max(0f, atkSpd - 1f) * ATTACK_SPEED_SCALE;
        attackCooldown = Mathf.Max(MIN_ATTACK_COOLDOWN, baseAttackCooldown / (1f + atkSpdBonus));

        // Armor defences — HP bonus + damage reduction were written by the shop
        // but never applied (armor was only a Power number). Now armor actually
        // protects: HP scaled by ARMOR_HP_SCALE, reduction by ARMOR_DR_SCALE and
        // capped so a full set is tanky, not invincible.
        float armorHP = PlayerPrefs.GetFloat("EquippedArmorHealth", 0f);
        maxHealth += armorHP * ARMOR_HP_SCALE;
        float armorDR = PlayerPrefs.GetFloat("EquippedArmorReduction", 0f);
        damageReduction = Mathf.Clamp(damageReduction + armorDR * ARMOR_DR_SCALE, 0f, MAX_TOTAL_DR);

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

        // Milestone auras — fire ONCE on crossing each threshold, reset when the
        // stack drops back below so re-stacking re-triggers them.
        if (currentStack >= 15 && !stack15Fired) { stack15Fired = true; SpawnFeelFX(stack15VFX, attach: true, life: 2f); }
        else if (currentStack < 15) stack15Fired = false;
        if (currentStack >= 30 && !stack30Fired) { stack30Fired = true; SpawnFeelFX(stack30VFX, attach: true, life: 3f); }
        else if (currentStack < 30) stack30Fired = false;

        if (currentMultiplier > 1)
        {
            if (TutorialHints.Instance != null)
                TutorialHints.Instance.ShowIfNew("Stack",
                    "STACK = enemies near you. At 15+ you start dealing multiplied damage. At 30+ you become a typhoon — but you also lose acceleration.", 6f);
            AchievementSystem.Unlock("STACK_15");
        }

        if (stackText != null)
        {
            stackText.text = LocalizationManager.Tr("STACK: {0}  |  x{1}", currentStack, currentMultiplier);
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

        SaveCampHeartbeat();

        if (dashStaminaFill != null)
        {
            float dashTarget = Mathf.Lerp(dashStaminaFill.fillAmount, Mathf.Clamp01((Time.unscaledTime - lastDashTime) / dashCooldown), Time.unscaledDeltaTime * 15f);
            if (Mathf.Abs(dashTarget - lastDashStaminaFill) > 0.001f)
            {
                dashStaminaFill.fillAmount = dashTarget;
                lastDashStaminaFill = dashTarget;
            }
        }

        float targetHpFill = currentHealth / maxHealth;
        if (hpFill != null && Mathf.Abs(targetHpFill - lastHpFill) > 0.0005f)
        {
            hpFill.fillAmount = targetHpFill;
            lastHpFill = targetHpFill;
        }

        // Smoothly drain the white catch-up bar down to the current HP. The old
        // code had a write-gate (|Δ| > 0.0005) COARSER than its snap threshold,
        // so the bar could park a hair above the red fill and never get written
        // to target — leaving a permanent white sliver (very visible under an
        // archer's steady chip damage). Now it always writes while converging and
        // snaps the final sliver clean.
        if (hpCatchupFill != null)
        {
            float cur = hpCatchupFill.fillAmount;
            if (cur > targetHpFill + 0.0002f)
            {
                float catchup = Mathf.Lerp(cur, targetHpFill, Time.unscaledDeltaTime * uiLerpSpeed);
                if (catchup - targetHpFill < 0.004f) catchup = targetHpFill; // snap last sliver
                hpCatchupFill.fillAmount = catchup;
                lastHpCatchupFill = catchup;
            }
            else if (cur < targetHpFill)
            {
                hpCatchupFill.fillAmount = targetHpFill; // healing → white catches up instantly
                lastHpCatchupFill = targetHpFill;
            }
        }

        float targetXpFill = currentXP / xpToNextLevel;
        if (xpFill != null && visualXP < currentXP)
        {
            visualXP = Mathf.Lerp(visualXP, currentXP, Time.unscaledDeltaTime * uiLerpSpeed);
            float xpAmt = visualXP / xpToNextLevel;
            if (Mathf.Abs(xpAmt - lastXpFill) > 0.0005f)
            {
                xpFill.fillAmount = xpAmt;
                lastXpFill = xpAmt;
            }
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

        // Block WASD when paused (Time.timeScale == 0 covers both the
        // regular pause menu and any cinematic that freezes the game)
        // and while a tutorial hint is on screen — without this the
        // player could still pivot through the air while reading the
        // pause menu or hovering a hint.
        // LoadingManager.isLoading is OR'd in so loading transitions
        // freeze input WITHOUT writing to `isControlBlocked` — otherwise
        // the loader clobbers the tutorial's intended block state on
        // scene-in (Level1_QuestManager.Start races the loader's post-
        // load unblock).
        bool loaderLocking = LoadingManager.Instance != null && LoadingManager.Instance.isLoading;
        bool isCurrentlyLocked = isControlBlocked || loaderLocking || Time.unscaledTime < actionLockEndTime || TutorialPanelUI.IsTutorialActive || Time.timeScale == 0f || TutorialHints.IsAnyHintShowing;
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
            // Landing sound fires on a REAL landing from a height (fall/jump),
            // scaled by how hard the drop was — not at the end of a dash on flat
            // ground. Anything softer than a jump (-8) is just a step and stays
            // silent.
            if (yVelocityBeforeMove < -8f && AudioManager.Instance != null)
                AudioManager.Instance.PlaySFX(AudioID.Player_Land);

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

        UpdateFootstepFallback(isGroundedNow);

        if (visualModel != null && visualModel != transform && !isCampMode)
        {
            Vector3 localVel = transform.InverseTransformDirection(currentVelocityMove);
            float leanX = (localVel.z / moveSpeed) * 8f;
            float leanZ = -(localVel.x / moveSpeed) * 10f;

            Quaternion targetLean = Quaternion.Euler(leanX, 0, leanZ);
            visualModel.localRotation = Quaternion.Slerp(visualModel.localRotation, targetLean, Time.deltaTime * 18f);
        }

        bool isVisuallyGrounded = isGroundedNow;
        if (!isVisuallyGrounded && velocity.y <= 1f)
        {
            // Was: LayerMask.GetMask("Default", "Terrain", "Ground") — but this
            // project has no Terrain / Ground layers (see TagManager.asset).
            // The mask silently resolved to "Default only", so the raycast
            // missed the actual terrain (which sits on the Nature layer) and
            // the animator's IsGrounded param flipped to false every time
            // the player walked DOWN a slope. The visible result was the
            // hovering T-pose during descents because the fall transition
            // played in mid-air.
            // GetGrenadeBlockerMask already includes Default + Nature +
            // Obstacles, which is exactly what "is there ground under me"
            // wants here too. Widened the cast (sphere not raycast, 0.9m
            // vs 0.4m) so even fast descents catch the terrain reliably.
            if (Physics.SphereCast(transform.position + Vector3.up * 0.3f, 0.25f, Vector3.down, out RaycastHit groundHit, 0.9f, GetGrenadeBlockerMask(), QueryTriggerInteraction.Ignore))
            {
                if (!groundHit.collider.CompareTag("Player")) isVisuallyGrounded = true;
            }
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

                    // Aim-mode: HOLD (default) aims while RMB is held and
                    // throws on release; TOGGLE starts aiming on the first
                    // RMB click and throws on the second. Controlled by
                    // the Settings_HoldToggleSprint key (there is no sprint
                    // — the key was repurposed; relabel its toggle text to
                    // "Hold to Aim" in the settings prefab).
                    bool aimHold = GameplaySettings.GrenadeAimHold;
                    if (Input.GetMouseButtonDown(1) && UnityEngine.SceneManagement.SceneManager.GetActiveScene().name != "Lvl_1")
                    {
                        if (!isAimingGrenade)
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
                                    ResetTrajectoryGradient();
                                    trajectoryLine.textureMode = LineTextureMode.Stretch;
                                }
                                if (aoeMarkerLine != null) aoeMarkerLine.enabled = true;
                                if (innerMarkerLine != null) innerMarkerLine.enabled = true;
                            }
                        }
                        else if (!aimHold)
                        {
                            // Toggle mode: a second RMB click throws.
                            CancelGrenadeAim();
                            if (isGroundedNow) LockAction("Throw", 0.4f);
                            else ExecuteThrow();
                        }
                    }
                }
            }

            // Update the aim while aiming — hold mode requires RMB held,
            // toggle mode aims continuously until the throw click.
            bool aimHoldMode = GameplaySettings.GrenadeAimHold;
            if (!isCampMode && !isCurrentlyLocked && isAimingGrenade
                && (!aimHoldMode || Input.GetMouseButton(1)))
                UpdateGrenadeAiming();

            // Hold mode: release throws. (Toggle mode throws via the
            // second click above.) The locked-state fallback still throws
            // so a hit/action mid-aim doesn't strand a live aim.
            if (!isCampMode && aimHoldMode
                && (!isCurrentlyLocked && Input.GetMouseButtonUp(1) || (isCurrentlyLocked && isAimingGrenade)))
            {
                if (isAimingGrenade)
                {
                    CancelGrenadeAim();
                    if (isGroundedNow) LockAction("Throw", 0.4f);
                    else ExecuteThrow();
                }
            }
            else if (!isCampMode && !aimHoldMode && isCurrentlyLocked && isAimingGrenade)
            {
                // Toggle mode safety: if the player gets locked (hit /
                // action) mid-aim, throw rather than stranding the aim.
                CancelGrenadeAim();
                ExecuteThrow();
            }
        }

        if (!isCampMode && currentHealth < maxHealth && healthRegenRate > 0)
        {
            // Regen on SCALED time, not `dt`. While aiming a grenade / in
            // perfect-dodge bullet-time `dt` is unscaled, so the player healed
            // at real-world rate while the world crawled at 0.25-0.4x — up to
            // ~4x effective regen, i.e. an infinite-heal exploit from holding aim.
            currentHealth += healthRegenRate * Time.deltaTime;
            currentHealth = Mathf.Min(currentHealth, maxHealth);
            UpdateHUD();
        }

        // Low-health heartbeat warning: on below 25% HP (alive, in combat), off
        // once healed back above the threshold. AudioManager de-dupes the loop.
        if (AudioManager.Instance != null)
        {
            bool danger = !isCampMode && currentHealth > 0f && currentHealth <= maxHealth * 0.25f;
            AudioManager.Instance.SetLowHealthWarning(danger);
        }
    }

    // Layer mask used to detect both aim-raycast and trajectory collisions.
    // Cached so we don't rebuild it every frame while aiming.
    private static int s_grenadeAimMask = -1;
    private static int s_grenadeBlockerMask = -1;

    // This project's layer setup (see ProjectSettings/TagManager.asset)
    // doesn't include literal "Terrain" / "Ground" / "Foliage" names.
    // Terrain in GameScene sits on the "Nature" layer (15), so the prior
    // masks resolved to "Default only" — the sphere cast had nothing to
    // terminate against, the simulation ran out the entire 64-step loop,
    // and the marker landed wherever flight time happened to dump it
    // (often off-map). Build the masks from layers we actually know to
    // exist, with safe fallbacks so projects that DO use the canonical
    // names still work.
    private static int BuildGrenadeMask()
    {
        int mask = 0;
        string[] candidates = { "Default", "Nature", "Obstacles", "InvisibleWall", "Terrain", "Ground", "Foliage" };
        for (int i = 0; i < candidates.Length; i++)
        {
            int layer = LayerMask.NameToLayer(candidates[i]);
            if (layer >= 0) mask |= 1 << layer;
        }
        if (mask == 0) mask = ~0; // last-resort: hit everything; better than hitting nothing
        return mask;
    }

    private static int GetGrenadeAimMask()
    {
        if (s_grenadeAimMask == -1) s_grenadeAimMask = BuildGrenadeMask();
        return s_grenadeAimMask;
    }

    private static int GetGrenadeBlockerMask()
    {
        if (s_grenadeBlockerMask == -1) s_grenadeBlockerMask = BuildGrenadeMask();
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
        // Aim-assist magnet strength now comes from the Aim Assist
        // setting (0 = off, 1 = strong). Was a hardcoded 0.4 that the
        // slider didn't affect. A small floor (0.15) keeps a touch of
        // the original feel even at low settings so the throw still lands
        // near enemies; at 0 the magnet is fully off.
        float assist = GameplaySettings.AimAssist;
        if (bestTarget != null && assist > 0.001f)
        {
            Vector3 magneticTarget = new Vector3(bestTarget.position.x, hitPoint.y, bestTarget.position.z);
            hitPoint = Vector3.Lerp(hitPoint, magneticTarget, Mathf.Lerp(0.15f, 0.7f, assist));
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
            trajectoryLine.positionCount = simulatedCount;
            ApplySolidTrajectoryGradient(currentAimColor);
            if (trajectoryLine.widthMultiplier < 0.18f) trajectoryLine.widthMultiplier = 0.22f;
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
    private static Material s_solidTrajectoryMaterial;
    private bool trajectoryMaterialReplaced = false;

    // Replace the prefab's "magic trail" material with a flat URP unlit
    // material. The original material was a Hovl trail effect with a
    // texture whose alpha falls off along its length — perfect for a
    // VFX trail, terrible for a precise aiming line, because the far
    // tail of the arc rendered invisible no matter what colour we set.
    private void EnsureSolidTrajectoryMaterial()
    {
        if (trajectoryMaterialReplaced || trajectoryLine == null) return;
        if (s_solidTrajectoryMaterial == null)
        {
            Shader sh = Shader.Find("Universal Render Pipeline/Unlit");
            if (sh == null) sh = Shader.Find("Sprites/Default");
            if (sh == null) sh = Shader.Find("Unlit/Color");
            s_solidTrajectoryMaterial = new Material(sh);
            // Transparent so the trajectory line can lay over geometry softly.
            if (s_solidTrajectoryMaterial.HasProperty("_Surface"))
            {
                s_solidTrajectoryMaterial.SetFloat("_Surface", 1f);
                s_solidTrajectoryMaterial.SetFloat("_Blend", 0f);
                s_solidTrajectoryMaterial.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                s_solidTrajectoryMaterial.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                s_solidTrajectoryMaterial.SetInt("_ZWrite", 0);
                s_solidTrajectoryMaterial.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                s_solidTrajectoryMaterial.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            }
            s_solidTrajectoryMaterial.color = Color.white; // gradient drives the look
        }
        trajectoryLine.material = s_solidTrajectoryMaterial;
        // Stretch the (non-existent) texture across the whole line so any
        // residual tiling doesn't carve gaps mid-arc.
        trajectoryLine.textureMode = LineTextureMode.Stretch;
        trajectoryMaterialReplaced = true;
    }

    private static readonly RaycastHit[] s_grenadeSimHitBuffer = new RaycastHit[8];
    private bool trajectoryGradientReset = false;

    // Reusable gradient + keyframe arrays so we can rebuild the line's
    // colour gradient every aim frame without allocating per-call.
    private static readonly Gradient s_trajectoryGradient = new Gradient();
    private static readonly GradientColorKey[] s_trajectoryColorKeys = new GradientColorKey[2]
    {
        new GradientColorKey(Color.white, 0f),
        new GradientColorKey(Color.white, 1f),
    };
    private static readonly GradientAlphaKey[] s_trajectoryAlphaKeys = new GradientAlphaKey[2]
    {
        new GradientAlphaKey(1f, 0f),
        new GradientAlphaKey(1f, 1f),
    };

    private void ApplySolidTrajectoryGradient(Color color)
    {
        if (trajectoryLine == null) return;
        s_trajectoryColorKeys[0].color = color;
        s_trajectoryColorKeys[1].color = color;
        s_trajectoryAlphaKeys[0].alpha = color.a;
        s_trajectoryAlphaKeys[1].alpha = color.a;
        s_trajectoryGradient.SetKeys(s_trajectoryColorKeys, s_trajectoryAlphaKeys);
        trajectoryLine.colorGradient = s_trajectoryGradient;
    }

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
        AchievementSystem.Unlock("PERFECT_DODGE");
        RunSession.AddPerfectDodge();
        // Sharp, short rumble to reward the perfect-dodge timing.
        InputCompat.Rumble(0.2f, 0.8f, 0.12f);

        if (AudioManager.Instance != null) AudioManager.Instance.PlaySFX(AudioID.Player_PerfectDodge);

        Time.timeScale = perfectDodgeSlowMoScale;

        if (anim != null) anim.updateMode = AnimatorUpdateMode.UnscaledTime;

        lastAttackTime = -100f;
        actionLockEndTime = 0f;

        if (AudioManager.Instance != null) AudioManager.Instance.PlaySFX(AudioID.Player_Dash);
        if (perfectDodgeVFX != null) ActivateSceneVFX(perfectDodgeVFX);

        yield return StartCoroutine(BlinkBehindRoutine(fallbackDirection));

        yield return new WaitForSecondsRealtime(perfectDodgeDuration);

        if (!isAimingGrenade) Time.timeScale = 1f;
        isBulletTime = false;
        if (anim != null) anim.updateMode = AnimatorUpdateMode.Normal;
        if (perfectDodgeVFX != null) StartCoroutine(FadeOutSceneVFX(perfectDodgeVFX));
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
        // (Landing sound moved to the real hard-landing detection — a dash on
        // flat ground is not a landing.)

        float elapsed = 0f;
        while (elapsed < 0.3f)
        {
            elapsed += Time.unscaledDeltaTime;
            mainCameraCached.fieldOfView = Mathf.Lerp(targetFOV, originalFOV, elapsed / 0.3f);
            yield return null;
        }
        mainCameraCached.fieldOfView = originalFOV;
    }

    // Trailer/cinematic hook: perform ONE full melee swing at the nearest enemy
    // on demand (same as an LMB press — faces the target, plays the swing anim,
    // and the animator's hit event drives ExecuteAttack for damage + trail).
    // Used by AutoTrailerDirector to film combat beats without a human at the
    // controls. Respects the normal attack cooldown so swings look natural.
    public void TrailerAutoAttack()
    {
        if (isCampMode || Time.unscaledTime < lastAttackTime + attackCooldown) return;
        lastAttackTime = Time.unscaledTime;

        Transform tgt = GetClosestEnemyForFocus(10f, 360f);
        if (tgt != null)
        {
            Vector3 d = tgt.position - transform.position; d.y = 0f;
            if (d.sqrMagnitude > 0.01f) transform.rotation = Quaternion.LookRotation(d.normalized);
        }

        int randAnim = Random.Range(0, 3);
        if (randAnim == lastAttackIndex) randAnim = (randAnim + 1) % 3;
        lastAttackIndex = randAnim;
        if (anim != null) anim.SetInteger("AttackIndex", randAnim);
        LockAction("Attack", 0.6f);
    }

    // Trailer/cinematic: one dash in a world direction (respects the cooldown).
    public void TrailerDash(Vector3 worldDir)
    {
        if (isDashing || Time.unscaledTime < lastDashTime + dashCooldown) return;
        Vector3 d = worldDir; d.y = 0f;
        if (d.sqrMagnitude < 0.01f) d = transform.forward;
        StartCoroutine(DashRoutine(d.normalized, false));
    }

    // Trailer/cinematic: hurl a grenade at the nearest enemy on demand.
    public void TrailerThrowGrenade()
    {
        if (grenadePrefab == null || isCampMode) return;
        Transform tgt = GetClosestEnemyForFocus(16f, 360f);
        if (tgt != null)
        {
            Vector3 d = tgt.position - transform.position; d.y = 0f;
            if (d.sqrMagnitude > 0.01f) transform.rotation = Quaternion.LookRotation(d.normalized);
        }
        ExecuteThrow();
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

        // AAA blade trail — a short, tapered ribbon along the swing arc. Warmer/
        // brighter on a crit. Kept subtle and brief so it never clutters play.
        TriggerWeaponTrail(isCriticalHit);

        float finalDmg = meleeDamage * globalDamageMultiplier;
        // critDamageMultiplier is the LvlUp-scaled normal crit. Guaranteed crit
        // (e.g. perfect dodge) gets a fixed 1.4x bonus on top, so investing in
        // CritDamage always feels like an upgrade.
        if (isCriticalHit) finalDmg *= (isNextAttackGuaranteedCrit ? critDamageMultiplier * 1.4f : critDamageMultiplier);

        float totalLifestealDealt = 0f;

        for (int idx = 0; idx < hitCount; idx++)
        {
            Collider col = s_overlapBuffer[idx];
            // Search PARENTS, not just the collider's own object — an archer (or
            // any enemy) whose collider sits on a child would otherwise take no
            // damage at all.
            IDamageable damageable = col.GetComponentInParent<IDamageable>();
            if (damageable != null)
            {
                GameObject dmgGO = ((MonoBehaviour)damageable).gameObject;
                if (dmgGO == this.gameObject) continue;

                Vector3 pushDir = (col.transform.position - transform.position).normalized; pushDir.y = 0;
                float kForce = isCriticalHit ? (isNextAttackGuaranteedCrit ? 20f : 12f) : 8f;

                bool isEnemy = damageable is EnemyAI || dmgGO.CompareTag("Enemy") || col.CompareTag("Enemy");
                // STACK payoff: the x2/x4/x5 multiplier the HUD shows for standing
                // in a crowd now actually multiplies damage — but ONLY vs enemies,
                // never resource nodes (chopping a tree shouldn't scale with the
                // swarm). This is the game's signature mechanic; it was computed
                // and displayed but never applied to any damage.
                float dmgForThis = isEnemy ? finalDmg * currentMultiplier : finalDmg;

                DamageInfo hitInfo = new DamageInfo
                {
                    Amount = dmgForThis,
                    IsCritical = isCriticalHit,
                    PushDirection = pushDir,
                    KnockbackForce = kForce,
                    StunDuration = isCriticalHit ? 1.0f : 0.4f,
                    HitPoint = col.ClosestPoint(meleePoint.position)
                };

                damageable.TakeDamage(hitInfo);
                if (isEnemy)
                {
                    hitEnemy = true; totalLifestealDealt += dmgForThis;
                    if (AudioManager.Instance != null) AudioManager.Instance.NotifyCombat();
                }
                else
                {
                    hitResource = true;
                    // Impact SFX (Player_HitResource_Stone / Wood) fires
                    // from inside ResourceNode.TakeDamage, which runs
                    // synchronously as part of the loop above. That IS
                    // the frame the axe visually connects with the rock
                    // or tree, so the sound lands with the hit instead
                    // of leading it or trailing behind a late animation
                    // event. This block is left only for camera-shake.
                }

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
            if (AudioManager.Instance != null) AudioManager.Instance.PlaySFX(GetEquippedHitSfx());
            // Extra crunch layer on a critical hit so crits read audibly.
            if (isCriticalHit && AudioManager.Instance != null) AudioManager.Instance.PlaySFX(AudioID.Player_Crit);
            Vector3 recoilDir = -transform.forward;
            if (isCriticalHit) { if (cameraFollow != null) cameraFollow.TriggerDirectionalShake(recoilDir, 1.5f, 0.3f, 0.2f); StartCoroutine(HitStopRoutine(0.12f)); }
            else { if (cameraFollow != null) cameraFollow.TriggerDirectionalShake(recoilDir, 0.5f, 0.1f, 0.05f); StartCoroutine(HitStopRoutine(0.04f)); }
        }
        else if (hitResource)
        {
            // SFX now fires in-loop at contact detection above; only the
            // camera-shake feedback remains here.
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
        if (isCinematicInvincible) return; // untouchable during the victory flythrough
        if (isCampMode || isDashing || isBulletTime) return;
        if (AudioManager.Instance != null) AudioManager.Instance.NotifyCombat(); // getting hit = combat

        // Feed the death recap's "Slain by ___" line. Overwrites on
        // every hit — whatever landed the LAST blow before Die() wins.
        RunSession.NoteDamageSource(info.SourceName);

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
        SpawnFeelFX(hitVFX, attach: false, life: 1.2f);

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

        // Gamepad rumble on taking a hit (gated by the vibration toggle).
        InputCompat.Rumble(0.35f, 0.5f, 0.18f);

        if (healthVisuals != null) healthVisuals.TriggerHitFlash();
        if (damageFlashImage != null)
        {
            // Only stop the previous flash — the old code called
            // StopAllCoroutines() here, which killed every player
            // coroutine (Dash, PerfectDodge, HitStop, ShakeUI) on every
            // hit. A grazed player would have their dash cancelled mid-
            // roll.
            if (activeFlashRoutine != null) StopCoroutine(activeFlashRoutine);
            activeFlashRoutine = StartCoroutine(FlashRoutine());
        }

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
        // Photosensitivity: dim the damage flash (still visible so the
        // player knows they were hit, just not a hard full-screen pulse).
        float peak = GameplaySettings.Photosensitive ? 0.22f : 0.6f;
        Color c = damageFlashImage.color; c.a = peak; damageFlashImage.color = c;
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

        // Silence the low-health heartbeat the instant the player dies.
        if (AudioManager.Instance != null) AudioManager.Instance.SetLowHealthWarning(false);

        // Fold this run's tallies into the persistent career stats before
        // the run ends, then record the death. CommitToCareer no-ops if
        // the run was never Begin()'d, so this is safe on any scene.
        RunSession.CommitToCareer();
        RunStats.Add(RunStats.Stat.DeathsCount);
        RunSession.End();

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
        // Dead players don't level up: an XP crystal still flying in when the
        // player dies could cross a level threshold, pop the upgrade menu
        // (timeScale=0) over the death flow and hard-freeze the game.
        if (isDead || currentHealth <= 0) return;
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
            GlobalHUD.Instance.ShowPickupPopup($"+{finalAmount} {LocalizationManager.Tr("Diamond")}", new Color(0.85f, 0.55f, 1f));

        UpdateHUD();
        if (MissionManager.Instance != null) MissionManager.Instance.AddProgress(MissionType.CollectCrystals, finalAmount);
        RunSession.AddDiamonds(finalAmount);
    }

    // Smooth quadratic XP curve. Replaces the prior 1.5x multiplier — at level 15
    // that curve cost ~9.7k xp, which made any post-mid-game run dead. New curve:
    //   L1->2: 50  L5: ~140  L10: ~340  L15: ~640  L20: ~1040  L25: ~1540
    public static float ComputeXpToNextLevel(int level)
    {
        // level is the level you ARE on (i.e. need this much to reach level+1).
        return 40f + level * level * 4f + level * 6f;
    }

    // Spawns a Hovl game-feel effect. Auras (attach=true) parent to the player
    // so they follow; impacts spawn free at the player's chest. Everything
    // auto-destroys since the pack's particle systems loop.
    private void SpawnFeelFX(GameObject prefab, bool attach, float life, float scale = 1f)
    {
        if (prefab == null) return;
        Vector3 pos = transform.position + Vector3.up * 1f;
        GameObject fx = Instantiate(prefab, pos, Quaternion.identity);
        if (attach && fx != null) fx.transform.SetParent(transform, true);
        if (fx != null && !Mathf.Approximately(scale, 1f)) fx.transform.localScale *= scale;
        // Fade out gracefully + World simulation so turning the player doesn't
        // drag the aura around (see VFXAutoFade).
        if (fx != null) fx.AddComponent<VFXAutoFade>().Configure(life, world: true);
    }

    // Enable a PERSISTENT (scene-child) VFX object: switch its systems to World
    // sim so it doesn't smear when the player turns, then (re)play them.
    private void ActivateSceneVFX(GameObject go)
    {
        if (go == null) return;
        go.SetActive(true);
        foreach (var ps in go.GetComponentsInChildren<ParticleSystem>(true))
        {
            if (ps == null) continue;
            var main = ps.main;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            ps.Clear(true);
            ps.Play(true);
        }
    }

    // Turn a persistent VFX object off GRACEFULLY: stop emitting, let living
    // particles fade over their lifetime, THEN deactivate (no hard cut).
    private IEnumerator FadeOutSceneVFX(GameObject go)
    {
        if (go == null) yield break;
        float maxLife = 0.5f;
        foreach (var ps in go.GetComponentsInChildren<ParticleSystem>(true))
        {
            if (ps == null) continue;
            ps.Stop(true, ParticleSystemStopBehavior.StopEmitting);
            float l = Mathf.Max(ps.main.startLifetime.constant, ps.main.startLifetime.constantMax);
            if (l > maxLife) maxLife = l;
        }
        yield return new WaitForSeconds(maxLife + 0.3f);
        if (go != null) go.SetActive(false);
    }

    private void LevelUp()
    {
        currentXP -= xpToNextLevel;
        currentLevel++;
        xpToNextLevel = ComputeXpToNextLevel(currentLevel);
        visualXP = 0f;

        if (AudioManager.Instance != null) AudioManager.Instance.PlayUI(AudioID.UI_LevelUp);

        RunSession.AddLevelUp(currentLevel);

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
                GlobalHUD.Instance.ShowPickupPopup(LocalizationManager.Tr("MILESTONE_LEVEL_HP", currentLevel), new Color(1f, 0.85f, 0.3f));
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
        if (levelText != null) levelText.text = LocalizationManager.Tr("LVL: {0}", currentLevel);
        if (crystalText != null) { int displayDiamonds = ResourceManager.Instance != null ? ResourceManager.Instance.diamonds : crystalsCollected; crystalText.text = LocalizationManager.Tr("Diamonds: {0}", displayDiamonds); }
    }

    // Level-up "Attack Speed" pick. The orbit weapon it used to modify was
    // removed, so the card did nothing; route it to the real melee cadence.
    // Each pick shortens the swing cooldown ~9%, floored so it can't trivialize.
    public void ApplyLevelUpAttackSpeed()
    {
        if (baseAttackCooldown <= 0f) baseAttackCooldown = attackCooldown;
        attackCooldown = Mathf.Max(MIN_ATTACK_COOLDOWN, attackCooldown * 0.91f);
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
            // Same threshold as the popup — trickle heals stay quiet so
            // lifesteal doesn't turn into a heal spam channel.
            if (AudioManager.Instance != null)
                AudioManager.Instance.PlaySFX(AudioID.Player_Heal);
            SpawnFeelFX(healVFX, attach: true, life: 1.6f);
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

    // Map the shop's ItemCategory enum (0=Sword, 1=Axe, 2=Bow) to the
    // matching FMOD hit event so bow/axe swings sound different from
    // sword swings. Axe falls back to the hammer event (closest heavy
    // weapon in the current bank). Missing pref → sword default.
    private string GetEquippedHitSfx()
    {
        int cat = PlayerPrefs.GetInt("EquippedWeaponCategory", 0);
        switch (cat)
        {
            case 2: return AudioID.Player_HitEnemy_Bow;
            case 1: return AudioID.Player_HitEnemy_Hammer;
            default: return AudioID.Player_HitEnemy_Sword;
        }
    }

    private void OnDrawGizmosSelected() { if (meleePoint != null) { Gizmos.color = Color.red; Gizmos.DrawWireSphere(meleePoint.position, meleeRadius); } }
    public void StartSwing() { if (weaponTrail != null) weaponTrail.emitting = true; }
    public void EndSwing() { if (weaponTrail != null) weaponTrail.emitting = false; }

    private void OnDestroy()
    {
        SaveCampPosition();
    }

    // Save on app-pause + app-quit too — OnDestroy runs on scene unload
    // but doesn't fire on a force-kill or power loss. Also autosaves at
    // a 30s heartbeat via SaveCampHeartbeat.
    private void OnApplicationPause(bool paused)
    {
        if (paused) SaveCampPosition();
    }

    private void OnApplicationQuit()
    {
        SaveCampPosition();
    }

    private float campSaveTimer = 0f;
    private void SaveCampHeartbeat()
    {
        if (!isCampMode) return;
        // AutoSave OFF skips the periodic 30s heartbeat. The on-quit /
        // on-pause save still runs regardless — that's a safety net, not
        // "autosave", so turning autosave off doesn't risk a hard loss.
        if (!GameplaySettings.AutoSave) return;
        campSaveTimer += Time.unscaledDeltaTime;
        if (campSaveTimer < 30f) return;
        campSaveTimer = 0f;
        SaveCampPosition();
    }

    private void SaveCampPosition()
    {
        if (!isCampMode) return;
        PlayerPrefs.SetFloat("CampPosX", transform.position.x);
        PlayerPrefs.SetFloat("CampPosY", transform.position.y);
        PlayerPrefs.SetFloat("CampPosZ", transform.position.z);
        PlayerPrefs.SetInt("HasCampSave", 1);
        PlayerPrefs.Save();
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