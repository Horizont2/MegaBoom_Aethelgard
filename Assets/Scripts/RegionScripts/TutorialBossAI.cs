using UnityEngine;
// TutorialBossAI head — bossName default remains English; call-sites wrap
// through LocalizationManager.Tr where the name is displayed.
using System.Collections;
using System.Collections.Generic;

public class TutorialBossAI : MonoBehaviour, IDamageable
{
    [Header("Boss Stats (Tutorial)")]
    public string bossName = "SKELETON OVERLORD";
    public float maxHealth = 500f;
    public float moveSpeed = 2f;
    public float damage = 25f;
    public float staggerHealthThreshold = 0.15f;

    [Header("Combat Settings")]
    public float attackRange = 3.5f;
    public float attackCooldown = 3f;
    public float attackTelegraphTime = 1.2f;

    [Header("Stagger & Weapon Drop")]
    public GameObject bossWeapon;

    [Header("Slam AOE Attack")]
    [Tooltip("Boss sometimes telegraphs a ground slam at the player's position — a readable, dodgeable AOE instead of only a melee poke.")]
    public bool useSlamAttack = true;
    [Tooltip("Optional flat ground decal/ring prefab marking where the slam lands (scaled to 2×radius). Leave empty for telegraph-only.")]
    public GameObject slamIndicatorPrefab;
    public float slamRadius = 4.5f;
    [Tooltip("Slam damage as a multiple of base damage.")]
    public float slamDamageMult = 1.4f;
    [Range(0f, 1f)]
    [Tooltip("Chance a given attack is the slam AOE instead of the melee swing.")]
    public float slamChance = 0.4f;

    [Header("Enrage (low HP)")]
    [Tooltip("Below this HP fraction the boss speeds up, slams more, and (optionally) summons adds once.")]
    public bool useEnrage = true;
    [Range(0f, 1f)] public float enrageThreshold = 0.35f;
    public GameObject[] enrageAddPrefabs;
    public int enrageAddCount = 3;
    private bool isEnraged = false;
    [Tooltip("Колір кругу під ногами боса в стейтджері (procedural)")]
    public Color staggerRingColor = new Color(1f, 0.65f, 0.1f, 0.8f);
    [Tooltip("Радіус кругу-маркера під босом у стагері")]
    public float staggerRingRadius = 3.5f;

    [Header("Charge Attack (gap-closer, phase 2+)")]
    [Tooltip("Boss telegraphs then dashes at the player to close distance, damaging anyone it runs over.")]
    public bool useCharge = true;
    public float chargeSpeed = 13f;
    public float chargeDamageMult = 1.25f;
    public float chargeMaxDistance = 13f;

    [Header("Cleave — 360° sweep around the boss (close range)")]
    [Tooltip("A radial slam centred on the BOSS (not the player) — punishes standing right next to it.")]
    public bool useCleave = true;
    public float cleaveRadius = 4f;
    public float cleaveDamageMult = 1.15f;
    [Range(0f, 1f)] public float cleaveChance = 0.28f;

    [Header("Summon adds (periodic, phase 2+)")]
    [Tooltip("Mid-fight reinforcements. Falls back to Enrage Add Prefabs if left empty.")]
    public bool useSummon = true;
    public GameObject[] summonPrefabs;
    public int summonCount = 3;
    public float summonCooldown = 15f;
    private float lastSummonTime = -999f;

    [Header("Drops & Economy")]
    public GameObject xpCrystalPrefab;
    public GameObject diamondPrefab;
    public GameObject damagePopupPrefab;
    public GameObject deathVFXPrefab;
    public ParticleSystem dissolveAshVFX;

    public int xpCrystalsToDrop = 40;
    public int diamondsToDrop = 5;

    public bool isInvincible = false;

    [Header("Glory-Kill Cinematic Timing (AAA sync)")]
    // Seconds AFTER the player's Attack trigger fires until the moment
    // the impact should land (freeze frame + hit VFX + boss death anim).
    // Tune this to the mid-swing frame of your player's attack clip.
    // Default 0.55 works for most 1.0-1.2s sword swings.
    [Range(0.1f, 1.5f)] public float gloryKillImpactOffset = 0.55f;
    // How long the freeze frame lasts before Phase 4 pull-back starts.
    [Range(0.05f, 0.4f)] public float gloryKillFreezeDuration = 0.12f;
    // Player Attack trigger fires at start of Phase 2. Give the anim
    // this long total to breathe before returning to normal timescale.
    [Range(0.4f, 2f)] public float gloryKillAftermathDuration = 0.9f;

    private float currentHealth;
    private bool isDead = false;
    private bool isStaggered = false;
    private bool isPreparingAttack = false;
    private bool isPromptShowing = false;
    private float lastAttackTime;

    private Transform target;
    private PlayerController playerTarget;
    private Animator animator;

    private Renderer[] meshRenderers;
    private Color[] originalColors;
    private GameObject staggerRing;
    private float staggerRumbleTimer;

    private bool hasShownBossUI = false;

    private void Awake()
    {
        gameObject.layer = 9;
        animator = GetComponentInChildren<Animator>();
        if (animator != null) animator.applyRootMotion = false;

        Renderer[] allRenderers = GetComponentsInChildren<Renderer>();
        List<Renderer> validRenderers = new List<Renderer>();
        foreach (var r in allRenderers)
        {
            if (r is ParticleSystemRenderer) continue;
            if (r.material.HasProperty("_Color")) validRenderers.Add(r);
        }

        meshRenderers = validRenderers.ToArray();
        originalColors = new Color[meshRenderers.Length];
        for (int i = 0; i < meshRenderers.Length; i++)
        {
            originalColors[i] = meshRenderers[i].material.color;
        }
    }

    public void InitializeBoss(float hpMultiplier, float dmgMultiplier)
    {
        maxHealth *= hpMultiplier;
        damage *= dmgMultiplier;
        currentHealth = maxHealth;
    }

    public void ActivateBoss()
    {
        // ФІКС: Робимо так, щоб HUD, рик і музика вмикалися суворо 1 раз, 
        // навіть якщо функція викликається з різних місць
        if (!hasShownBossUI && GlobalHUD.Instance != null)
        {
            GlobalHUD.Instance.ShowBossUI(LocalizationManager.Tr(bossName), currentHealth, maxHealth);
            hasShownBossUI = true;

            if (AudioManager.Instance != null) AudioManager.Instance.PlaySFX(AudioID.Boss_Roar);
            if (AudioManager.Instance != null) AudioManager.Instance.PlayMusic(AudioID.Music_Battle);
        }
    }


    private void Start()
    {
        GameObject playerObj = GameObject.FindGameObjectWithTag("Player");
        if (playerObj != null)
        {
            target = playerObj.transform;
            playerTarget = playerObj.GetComponent<PlayerController>();
        }

        lastAttackTime = Time.time;

        // ФІКС ЗАПОБІЖНИК: Якщо боса просто поставили на сцену (без тотему), 
        // він сам ініціалізує свій HUD при старті
        if (!hasShownBossUI)
        {
            if (currentHealth <= 0) currentHealth = maxHealth; // На випадок якщо InitializeBoss не викликали
            ActivateBoss();
        }
    }

    private void Update()
    {
        if (isDead || target == null || playerTarget.currentHealth <= 0) return;

        if (isStaggered)
        {
            float distToPlayer = Vector3.Distance(transform.position, target.position);

            if (distToPlayer <= attackRange + 3.0f)
            {
                if (!isPromptShowing && GlobalHUD.Instance != null)
                {
                    GlobalHUD.Instance.ShowPrompt(LocalizationManager.Tr("[F] EXECUTE"));
                    isPromptShowing = true;
                    if (TutorialHints.Instance != null)
                    {
                        TutorialHints.Instance.ShowIfNew("GloryKill",
                            "A staggered boss can be executed with F. A short cinematic + free kill. Do it whenever the prompt appears.", 6f);
                    }
                }

                if (Input.GetKeyDown(KeyCode.F))
                {
                    if (GlobalHUD.Instance != null) GlobalHUD.Instance.HidePrompt();
                    isPromptShowing = false;
                    StartCoroutine(GloryKillRoutine());
                }
            }
            else
            {
                if (isPromptShowing && GlobalHUD.Instance != null)
                {
                    GlobalHUD.Instance.HidePrompt();
                    isPromptShowing = false;
                }
            }
            return;
        }

        if (isPreparingAttack) return;

        float distance = Vector3.Distance(transform.position, target.position);

        if (distance > attackRange)
        {
            // Phase 2+: close the gap with a telegraphed CHARGE instead of plodding.
            if (useCharge && Phase() >= 2 && distance <= chargeMaxDistance
                && Time.time >= lastAttackTime + attackCooldown)
            {
                StartCoroutine(ChargeAttackRoutine());
                return;
            }

            if (animator != null) animator.SetBool("isMoving", true);
            Vector3 dir = (target.position - transform.position).normalized;
            dir.y = 0;

            Vector3 nextPos = transform.position + dir * moveSpeed * Time.deltaTime;

            if (Terrain.activeTerrain != null)
                nextPos.y = Terrain.activeTerrain.SampleHeight(nextPos) + Terrain.activeTerrain.transform.position.y;

            transform.position = nextPos;

            if (dir != Vector3.zero)
                transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(dir), 5f * Time.deltaTime);
        }
        else
        {
            if (animator != null) animator.SetBool("isMoving", false);

            Vector3 dir = (target.position - transform.position).normalized;
            dir.y = 0;
            if (dir != Vector3.zero) transform.rotation = Quaternion.LookRotation(dir);

            if (Time.time >= lastAttackTime + attackCooldown)
                PickInRangeAttack();
        }
    }

    // Which third of its health the boss is in: 1 (>66%), 2 (33-66%), 3 (<33%).
    // Later phases unlock more of the move-set so the fight escalates.
    private int Phase()
    {
        float f = maxHealth > 0f ? currentHealth / maxHealth : 1f;
        if (f > 0.66f) return 1;
        if (f > 0.33f) return 2;
        return 3;
    }

    // Weighted move-set for when the player is in melee range. Summon takes
    // priority when it's off cooldown; otherwise a weighted roll between slam,
    // cleave and the plain melee swing (more variety in later phases).
    private void PickInRangeAttack()
    {
        int phase = Phase();

        if (useSummon && phase >= 2 && Time.time >= lastSummonTime + summonCooldown)
        {
            StartCoroutine(SummonRoutine());
            return;
        }

        float roll = Random.value;
        float slamW = useSlamAttack ? slamChance : 0f;
        float cleaveW = (useCleave && phase >= 1) ? cleaveChance : 0f;

        if (roll < slamW) StartCoroutine(SlamAttackRoutine());
        else if (roll < slamW + cleaveW) StartCoroutine(CleaveAttackRoutine());
        else StartCoroutine(TelegraphAndAttackRoutine());
    }

    private IEnumerator TelegraphAndAttackRoutine()
    {
        isPreparingAttack = true;

        if (AudioManager.Instance != null) AudioManager.Instance.PlaySFX3D(AudioID.Enemy_Telegraph, transform.position);

        SetColor(new Color(2f, 0.5f, 0f));

        if (playerTarget != null) playerTarget.OpenPerfectDodgeWindow(transform, attackTelegraphTime);
        if (ThreatUI.Instance != null) ThreatUI.Instance.ShowThreat(transform, attackTelegraphTime);

        float timer = 0f;
        while (timer < attackTelegraphTime)
        {
            timer += Time.deltaTime;
            if (timer < attackTelegraphTime * 0.7f && target != null && !isStaggered && !isDead)
            {
                Vector3 dir = (target.position - transform.position).normalized;
                dir.y = 0;
                if (dir != Vector3.zero)
                    transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(dir), 8f * Time.deltaTime);
            }
            yield return null;
        }

        ResetColor();

        if (!isStaggered && !isDead && Vector3.Distance(transform.position, target.position) <= attackRange + 1.5f)
        {
            lastAttackTime = Time.time;
            if (animator != null) animator.SetTrigger("Attack");
        }

        yield return new WaitForSeconds(0.5f);
        isPreparingAttack = false;
    }

    public void ExecuteAttackDamage()
    {
        if (isDead || isStaggered || playerTarget == null || playerTarget.currentHealth <= 0) return;

        if (Camera.main != null)
        {
            CameraFollow cam = Camera.main.GetComponent<CameraFollow>();
            if (cam != null) cam.TriggerShake(0.15f, 0.08f);
        }

        if (Vector3.Distance(transform.position, target.position) <= attackRange + 1.5f)
        {
            playerTarget.TakeDamage(new DamageInfo
            {
                Amount = damage,
                PushDirection = transform.forward,
                KnockbackForce = 15f
            });
        }
    }

    // Telegraphed ground slam: marks a circle where the player stands, gives
    // a wind-up to leave it, then damages anyone still inside. Fully dodgeable
    // — the whole point is a readable "get out of the red" moment.
    private IEnumerator SlamAttackRoutine()
    {
        isPreparingAttack = true;
        if (animator != null) animator.SetBool("isMoving", false);

        Vector3 center = target != null ? target.position : transform.position;
        center.y = (target != null ? target.position.y : transform.position.y);

        if (AudioManager.Instance != null) AudioManager.Instance.PlaySFX3D(AudioID.Enemy_Telegraph, transform.position);
        SetColor(new Color(2f, 0.2f, 0f));
        if (ThreatUI.Instance != null) ThreatUI.Instance.ShowThreat(transform, attackTelegraphTime);

        GameObject indicator = null;
        if (slamIndicatorPrefab != null)
        {
            indicator = Instantiate(slamIndicatorPrefab, center + Vector3.up * 0.05f, Quaternion.identity);
            Vector3 s = indicator.transform.localScale;
            indicator.transform.localScale = new Vector3(slamRadius * 2f, s.y, slamRadius * 2f);
        }

        float windup = attackTelegraphTime * (isEnraged ? 0.7f : 1f);
        float t = 0f;
        while (t < windup && !isDead && !isStaggered) { t += Time.deltaTime; yield return null; }

        ResetColor();
        if (indicator != null) Destroy(indicator);

        if (!isDead && !isStaggered)
        {
            if (animator != null) { animator.ResetTrigger("Attack"); animator.SetTrigger("Attack"); }
            CameraShakeUtil.TryShake(0.35f, 0.15f);
            if (AudioManager.Instance != null) AudioManager.Instance.PlaySFX3D(AudioID.Enemy_Attack, center);

            if (playerTarget != null && playerTarget.currentHealth > 0)
            {
                Vector2 pXZ = new Vector2(playerTarget.transform.position.x, playerTarget.transform.position.z);
                Vector2 cXZ = new Vector2(center.x, center.z);
                if (Vector2.Distance(pXZ, cXZ) <= slamRadius)
                {
                    Vector3 push = (playerTarget.transform.position - center); push.y = 0f;
                    playerTarget.TakeDamage(new DamageInfo
                    {
                        Amount = damage * slamDamageMult,
                        PushDirection = push.normalized,
                        KnockbackForce = 20f,
                        SourceName = bossName
                    });
                }
            }
        }

        lastAttackTime = Time.time;
        yield return new WaitForSeconds(0.5f);
        isPreparingAttack = false;
    }

    // CHARGE: telegraph, lock the aim, then dash forward — damages the player if
    // it runs them over, and stops once it blows past them. A gap-closer that
    // makes distance dangerous instead of a safe zone.
    private IEnumerator ChargeAttackRoutine()
    {
        isPreparingAttack = true;
        if (animator != null) animator.SetBool("isMoving", false);
        if (AudioManager.Instance != null) AudioManager.Instance.PlaySFX3D(AudioID.Enemy_Telegraph, transform.position);

        SetColor(new Color(2f, 0.9f, 0f));
        if (ThreatUI.Instance != null) ThreatUI.Instance.ShowThreat(transform, attackTelegraphTime);

        Vector3 dir = transform.forward;
        float tw = 0f;
        while (tw < attackTelegraphTime && !isDead && !isStaggered)
        {
            tw += Time.deltaTime;
            if (target != null)
            {
                Vector3 d = target.position - transform.position; d.y = 0f;
                if (d != Vector3.zero) { dir = d.normalized; transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(dir), 8f * Time.deltaTime); }
            }
            yield return null;
        }
        ResetColor();

        if (!isDead && !isStaggered)
        {
            if (animator != null) animator.SetBool("isMoving", true);
            float traveled = 0f;
            bool hit = false;
            while (traveled < chargeMaxDistance && !isDead && !isStaggered)
            {
                float step = chargeSpeed * Time.deltaTime;
                Vector3 next = transform.position + dir * step;
                if (Terrain.activeTerrain != null)
                    next.y = Terrain.activeTerrain.SampleHeight(next) + Terrain.activeTerrain.transform.position.y;
                transform.position = next;
                traveled += step;

                if (!hit && playerTarget != null && playerTarget.currentHealth > 0)
                {
                    Vector3 fp = playerTarget.transform.position; fp.y = transform.position.y;
                    if (Vector3.Distance(transform.position, fp) <= attackRange)
                    {
                        hit = true;
                        CameraShakeUtil.TryShake(0.3f, 0.14f);
                        Vector3 push = (playerTarget.transform.position - transform.position); push.y = 0f;
                        playerTarget.TakeDamage(new DamageInfo { Amount = damage * chargeDamageMult, PushDirection = push.normalized, KnockbackForce = 25f, SourceName = bossName });
                    }
                }
                // Stop once we've run past the player.
                if (target != null) { Vector3 toT = target.position - transform.position; toT.y = 0f; if (Vector3.Dot(toT, dir) < 0f) break; }
                yield return null;
            }
            if (animator != null) animator.SetBool("isMoving", false);
        }

        lastAttackTime = Time.time;
        yield return new WaitForSeconds(0.6f);
        isPreparingAttack = false;
    }

    // CLEAVE: a radial sweep centred on the BOSS. Standing right next to it during
    // the swing is punished, so the player can't just hug its back forever.
    private IEnumerator CleaveAttackRoutine()
    {
        isPreparingAttack = true;
        if (animator != null) animator.SetBool("isMoving", false);
        if (AudioManager.Instance != null) AudioManager.Instance.PlaySFX3D(AudioID.Enemy_Telegraph, transform.position);
        SetColor(new Color(2f, 0.3f, 0.6f));
        if (ThreatUI.Instance != null) ThreatUI.Instance.ShowThreat(transform, attackTelegraphTime * 0.85f);

        float windup = attackTelegraphTime * (isEnraged ? 0.6f : 0.85f);
        float t = 0f;
        while (t < windup && !isDead && !isStaggered) { t += Time.deltaTime; yield return null; }
        ResetColor();

        if (!isDead && !isStaggered)
        {
            if (animator != null) { animator.ResetTrigger("Attack"); animator.SetTrigger("Attack"); }
            CameraShakeUtil.TryShake(0.3f, 0.13f);
            if (AudioManager.Instance != null) AudioManager.Instance.PlaySFX3D(AudioID.Enemy_Attack, transform.position);

            if (playerTarget != null && playerTarget.currentHealth > 0)
            {
                Vector3 bp = transform.position; bp.y = 0f;
                Vector3 pp = playerTarget.transform.position; pp.y = 0f;
                if (Vector3.Distance(bp, pp) <= cleaveRadius)
                {
                    Vector3 push = (playerTarget.transform.position - transform.position); push.y = 0f;
                    playerTarget.TakeDamage(new DamageInfo { Amount = damage * cleaveDamageMult, PushDirection = push.normalized, KnockbackForce = 18f, SourceName = bossName });
                }
            }
        }

        lastAttackTime = Time.time;
        yield return new WaitForSeconds(0.5f);
        isPreparingAttack = false;
    }

    // SUMMON: telegraph, then spawn a small pack of adds around the boss. Uses
    // summonPrefabs, falling back to the enrage add pool.
    private IEnumerator SummonRoutine()
    {
        isPreparingAttack = true;
        lastSummonTime = Time.time;
        if (animator != null) animator.SetBool("isMoving", false);
        if (AudioManager.Instance != null) AudioManager.Instance.PlaySFX3D(AudioID.Enemy_Telegraph, transform.position);
        SetColor(new Color(0.4f, 0f, 1.6f));

        float t = 0f;
        while (t < attackTelegraphTime && !isDead && !isStaggered) { t += Time.deltaTime; yield return null; }
        ResetColor();

        if (!isDead && !isStaggered)
        {
            GameObject[] pool = (summonPrefabs != null && summonPrefabs.Length > 0) ? summonPrefabs : enrageAddPrefabs;
            if (pool != null && pool.Length > 0)
            {
                CameraShakeUtil.TryShake(0.25f, 0.1f);
                for (int i = 0; i < summonCount; i++)
                {
                    GameObject prefab = pool[Random.Range(0, pool.Length)];
                    if (prefab == null) continue;
                    Vector3 p = transform.position + (Vector3)(Random.insideUnitCircle.normalized * Random.Range(3f, 6f));
                    p.y = transform.position.y;
                    Instantiate(prefab, p, Quaternion.identity);
                }
            }
        }

        lastAttackTime = Time.time;
        yield return new WaitForSeconds(0.5f);
        isPreparingAttack = false;
    }

    private void EnterEnrage()
    {
        isEnraged = true;
        attackCooldown *= 0.65f;
        moveSpeed *= 1.25f;
        slamChance = Mathf.Min(1f, slamChance + 0.2f);

        CameraShakeUtil.TryShake(0.5f, 0.2f);
        if (AudioManager.Instance != null) AudioManager.Instance.PlaySFX3D(AudioID.Enemy_Telegraph, transform.position);
        StartCoroutine(EnrageFlashRoutine());

        // Summon a one-off pack of adds so the low-HP phase spikes in threat.
        if (enrageAddPrefabs != null && enrageAddPrefabs.Length > 0)
        {
            for (int i = 0; i < enrageAddCount; i++)
            {
                GameObject prefab = enrageAddPrefabs[Random.Range(0, enrageAddPrefabs.Length)];
                if (prefab == null) continue;
                Vector3 p = transform.position + (Vector3)(Random.insideUnitCircle.normalized * Random.Range(3f, 6f));
                p.y = transform.position.y;
                Instantiate(prefab, p, Quaternion.identity);
            }
        }
    }

    private IEnumerator EnrageFlashRoutine()
    {
        // Quick red pulse as the "boss just powered up" cue.
        for (int i = 0; i < 3; i++)
        {
            SetColor(new Color(1.6f, 0f, 0f));
            yield return new WaitForSeconds(0.1f);
            if (!isPreparingAttack) ResetColor();
            yield return new WaitForSeconds(0.1f);
        }
    }

    public void TakeDamage(DamageInfo info)
    {
        if (isDead || isStaggered || isInvincible) return;

        currentHealth -= info.Amount;
        if (currentHealth < 0) currentHealth = 0;

        if (GlobalHUD.Instance != null) GlobalHUD.Instance.UpdateBossHealth(currentHealth, maxHealth);

        if (useEnrage && !isEnraged && !isDead && currentHealth <= maxHealth * enrageThreshold)
            EnterEnrage();

        bool showPopups = PlayerPrefs.GetInt("Settings_DamagePopups", 1) == 1;
        if (damagePopupPrefab != null && showPopups && ObjectPoolManager.Instance != null)
        {
            GameObject popup = ObjectPoolManager.Instance.SpawnFromPool(damagePopupPrefab, transform.position + Vector3.up * 2f, Quaternion.identity);
            popup.GetComponent<DamagePopup>()?.Setup(info.Amount, info.IsCritical);
        }

        if (AudioManager.Instance != null) AudioManager.Instance.PlaySFX3D(AudioID.Enemy_Hurt, transform.position);

        StartCoroutine(HitFlashRoutine());

        if (info.IsCritical && !isStaggered && !isDead)
        {
            StartCoroutine(HitStopRoutine(0.06f));
        }

        if (currentHealth <= maxHealth * staggerHealthThreshold)
        {
            EnterStaggerState();
        }
        else if (info.IsCritical)
        {
            if (animator != null) animator.SetTrigger("Hit");
        }
    }

    private IEnumerator HitStopRoutine(float duration)
    {
        if (PlayerPrefs.GetInt("Settings_HitStop", 1) != 1)
        {
            yield break;
        }
        Time.timeScale = 0.1f;
        yield return new WaitForSecondsRealtime(duration);
        if (!isStaggered && !isDead && !playerTarget.isControlBlocked)
        {
            Time.timeScale = 1f;
        }
    }

    private void EnterStaggerState()
    {
        isStaggered = true;
        isPreparingAttack = false;
        Time.timeScale = 1f;

        SnapToGround();

        if (animator != null)
        {
            animator.SetBool("isMoving", false);
            animator.SetTrigger("Stagger");
            animator.speed = 1f;
        }

        if (bossWeapon != null)
        {
            bossWeapon.transform.SetParent(null);
            Rigidbody weaponRb = bossWeapon.GetComponent<Rigidbody>();
            if (weaponRb == null) weaponRb = bossWeapon.gameObject.AddComponent<Rigidbody>();
            Collider weaponCol = bossWeapon.GetComponent<Collider>();
            if (weaponCol == null) weaponCol = bossWeapon.gameObject.AddComponent<BoxCollider>();

            weaponRb.isKinematic = false;
            weaponRb.useGravity = true;
            weaponRb.AddForce(transform.forward * 3f + transform.up * 2f, ForceMode.Impulse);
            weaponRb.AddTorque(Random.insideUnitSphere * 50f, ForceMode.Impulse);
        }

        if (AudioManager.Instance != null) AudioManager.Instance.PlaySFX(AudioID.Boss_Stagger);

        if (Camera.main != null)
        {
            CameraFollow cam = Camera.main.GetComponent<CameraFollow>();
            if (cam != null) cam.TriggerShake(0.45f, 0.25f);
        }

        SpawnStaggerRing();
        StartCoroutine(StaggerPulseRoutine());
        StartCoroutine(StaggerBreathingRoutine());
    }

    private void SpawnStaggerRing()
    {
        if (staggerRing != null) return;

        staggerRing = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        staggerRing.name = "BossStaggerRing";
        Collider col = staggerRing.GetComponent<Collider>();
        if (col != null) Destroy(col);

        staggerRing.transform.SetParent(null);
        Vector3 ringPos = transform.position;
        if (Terrain.activeTerrain != null)
            ringPos.y = Terrain.activeTerrain.SampleHeight(ringPos) + Terrain.activeTerrain.transform.position.y + 0.05f;
        staggerRing.transform.position = ringPos;
        staggerRing.transform.localScale = new Vector3(staggerRingRadius * 2f, 0.02f, staggerRingRadius * 2f);

        Material mat = BuildRingMaterial(staggerRingColor);
        Renderer rend = staggerRing.GetComponent<Renderer>();
        rend.sharedMaterial = mat;
        rend.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        rend.receiveShadows = false;
    }

    private Material BuildRingMaterial(Color color)
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
        if (shader == null) shader = Shader.Find("Unlit/Color");
        if (shader == null) shader = Shader.Find("Standard");
        Material mat = new Material(shader);
        if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", color);
        if (mat.HasProperty("_Color")) mat.SetColor("_Color", color);
        if (mat.HasProperty("_Surface"))
        {
            mat.SetFloat("_Surface", 1f);
            mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.One);
            mat.SetInt("_ZWrite", 0);
            mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
        }
        if (mat.HasProperty("_EmissionColor"))
        {
            mat.SetColor("_EmissionColor", color * 4f);
            mat.EnableKeyword("_EMISSION");
        }
        return mat;
    }

    private IEnumerator StaggerBreathingRoutine()
    {
        Vector3 baseScale = transform.localScale;
        while (isStaggered && !isDead)
        {
            float pulse = (Mathf.Sin(Time.time * 2.4f) + 1f) * 0.5f;
            transform.localScale = baseScale + new Vector3(0f, baseScale.y * 0.04f * pulse, 0f);

            if (staggerRing != null)
            {
                float ringPulse = 1f + Mathf.Sin(Time.time * 5f) * 0.06f;
                staggerRing.transform.localScale = new Vector3(
                    staggerRingRadius * 2f * ringPulse, 0.02f, staggerRingRadius * 2f * ringPulse);
            }

            staggerRumbleTimer -= Time.deltaTime;
            if (staggerRumbleTimer <= 0f)
            {
                staggerRumbleTimer = 0.8f;
                if (AudioManager.Instance != null) AudioManager.Instance.PlaySFX(AudioID.Boss_Stagger);
            }

            yield return null;
        }
        transform.localScale = baseScale;
    }

    private void SnapToGround()
    {
        if (Terrain.activeTerrain != null)
        {
            Vector3 groundPos = transform.position;
            groundPos.y = Terrain.activeTerrain.SampleHeight(groundPos) + Terrain.activeTerrain.transform.position.y;
            transform.position = groundPos;
        }
    }

    private IEnumerator StaggerPulseRoutine()
    {
        while (isStaggered && !isDead)
        {
            SetColor(new Color(1f, 0.8f, 0.2f));
            yield return new WaitForSeconds(0.3f);
            ResetColor();
            yield return new WaitForSeconds(0.3f);
        }
    }

    // ================================================================
    //  BOSS GLORY-KILL CINEMATIC (4-phase, precisely timed)
    // ================================================================
    //
    //  Phase 1 · Setup (0.55s realtime, 0.1× slow-mo)
    //    - Freeze player, snap into an over-the-shoulder position with
    //      a strict LookAt at boss upper-chest.
    //    - Camera slides to a low three-quarter angle, FOV zooms 60→35°.
    //    - Ambient whoosh SFX + subtle camera shake as it settles.
    //
    //  Phase 2 · Wind-up (0.35s realtime, 0.05× slow-mo)
    //    - Player raises weapon (Attack trigger). Camera pushes slightly
    //      closer, FOV dips another 4° for the pre-swing tension.
    //    - Boss animator plays Die trigger frame-2 timing so the boss
    //      staggers back into the swing arc.
    //
    //  Phase 3 · Impact freeze (0.10s realtime, 0× time)
    //    - Time freezes entirely for a single "hit" frame.
    //    - Screen-space white flash (0.5 alpha → 0 over 0.35s).
    //    - Full-strength camera shake, deep bass SFX, boss goes white.
    //
    //  Phase 4 · Aftermath (0.55s realtime, 0.15× slow-mo)
    //    - Camera pulls back + up, FOV returns to 55°.
    //    - Boss dissolve VFX kicks in, drops spawn during pullback.
    //    - Time.timeScale ramps back to 1 in the last 0.15s.
    private IEnumerator GloryKillRoutine()
    {
        isDead = true;
        isStaggered = false;
        if (staggerRing != null) Destroy(staggerRing);
        AchievementSystem.Unlock("EXECUTIONER");
        RunSession.AddKill(isElite: true, isBoss: true);
        // Heavy, satisfying rumble on the glory-kill execution.
        InputCompat.Rumble(0.9f, 0.7f, 0.35f);

        if (GlobalHUD.Instance != null)
        {
            GlobalHUD.Instance.HideBossUI();
            GlobalHUD.Instance.ShowCinematicBars();
        }

        Camera mainCam = Camera.main;
        CameraFollow camFollow = mainCam != null ? mainCam.GetComponent<CameraFollow>() : null;
        if (camFollow != null) camFollow.isCinematicMode = true;

        CharacterController playerCC = playerTarget.GetComponent<CharacterController>();
        if (playerCC != null) playerCC.enabled = false;

        Vector3 playerStartPos = playerTarget.transform.position;
        Quaternion playerStartRot = playerTarget.transform.rotation;

        Vector3 bossPos = transform.position;
        Vector3 dirToPlayer = playerStartPos - bossPos; dirToPlayer.y = 0;
        if (dirToPlayer.sqrMagnitude < 0.01f) dirToPlayer = Vector3.forward;
        dirToPlayer.Normalize();

        Vector3 idealPlayerPos = bossPos + dirToPlayer * 1.5f;
        if (Terrain.activeTerrain != null)
            idealPlayerPos.y = Terrain.activeTerrain.SampleHeight(idealPlayerPos) + Terrain.activeTerrain.transform.position.y;
        Quaternion idealPlayerRot = Quaternion.LookRotation((bossPos - idealPlayerPos).normalized);
        idealPlayerRot.x = 0; idealPlayerRot.z = 0;

        Vector3 sideDir = Vector3.Cross(Vector3.up, dirToPlayer).normalized;
        Vector3 midPoint = Vector3.Lerp(idealPlayerPos, bossPos, 0.5f);

        Vector3 startCamPos = mainCam.transform.position;
        float startFov = mainCam.fieldOfView;
        Vector3 cinematicCamPos = midPoint + sideDir * 3.5f + Vector3.up * 1.1f;
        Vector3 aftermathCamPos = midPoint + sideDir * 5.5f + Vector3.up * 3.5f;

        Vector3 bossHead = bossPos + Vector3.up * 1.6f;

        float savedTimeScale = Time.timeScale;
        float savedFixedDelta = Time.fixedDeltaTime;
        Animator playerAnim = playerTarget.GetComponentInChildren<Animator>();
        AnimatorUpdateMode savedPlayerAnimMode = playerAnim != null ? playerAnim.updateMode : AnimatorUpdateMode.Normal;
        AnimatorUpdateMode savedBossAnimMode = animator != null ? animator.updateMode : AnimatorUpdateMode.Normal;

        try
        {
            if (playerAnim != null) playerAnim.updateMode = AnimatorUpdateMode.UnscaledTime;
            if (animator != null) animator.updateMode = AnimatorUpdateMode.UnscaledTime;

            // AAA layer: duck the music bed hard for the whole execution,
            // rack cinematic DoF, and put a tight handheld drift on the
            // camera. The drift runs on unscaled time, so it stays alive
            // inside the freeze frame — that residual micro-motion during
            // a timeScale=0 hold is the single strongest 'filmed, not
            // paused' cue. Torn down in finally.
            if (AudioManager.Instance != null) AudioManager.Instance.DuckMusic(0.25f, 0.15f, 2.2f, 1.4f);
            if (GlobalHUD.Instance != null) GlobalHUD.Instance.SetCinematicDoF(true);
            CinematicHandheld.Begin(mainCam, 0.03f, 0.3f, 0.6f);

            // ============================================================
            //  PHASE 1 · SETUP (0.55s realtime)
            // ============================================================
            Time.timeScale = 0.1f;
            Time.fixedDeltaTime = 0.02f * Time.timeScale;
            if (AudioManager.Instance != null) AudioManager.Instance.PlaySFX(AudioID.Cinematic_Whoosh);

            const float phase1Duration = 0.55f;
            float elapsed = 0f;
            while (elapsed < phase1Duration)
            {
                elapsed += Time.unscaledDeltaTime;
                float x = Mathf.Clamp01(elapsed / phase1Duration);
                // Ease-OUT cubic: the camera snaps toward the shoulder
                // fast and brakes into the composition — a whip-to-frame,
                // not an elevator ride. (Symmetric SmoothStep read slow
                // and mushy here.)
                float t = 1f - Mathf.Pow(1f - x, 3f);

                playerTarget.transform.position = Vector3.Lerp(playerStartPos, idealPlayerPos, t);
                playerTarget.transform.rotation = Quaternion.Slerp(playerStartRot, idealPlayerRot, t);

                mainCam.transform.position = Vector3.Lerp(startCamPos, cinematicCamPos, t);
                mainCam.fieldOfView = Mathf.Lerp(startFov, 35f, t);
                Quaternion targetRotation = Quaternion.LookRotation(bossHead - mainCam.transform.position)
                                          * Quaternion.Euler(0, 0, 8f);
                mainCam.transform.rotation = Quaternion.Slerp(mainCam.transform.rotation, targetRotation, t);

                yield return null;
            }
            if (camFollow != null) camFollow.TriggerShake(0.08f, 0.25f); // settle nudge

            // ============================================================
            //  PHASE 2 · WIND-UP + SWING (gloryKillImpactOffset realtime)
            // ============================================================
            // Fire the player Attack trigger at the START of the swing
            // so the animation has room to play. The freeze frame lands
            // exactly `gloryKillImpactOffset` seconds later — tune that
            // to the mid-swing frame of the player's attack clip.
            //
            // Slower slow-mo (0.05×) than phase 1 keeps the anticipation
            // tight. Camera pushes slightly closer over the wind-up so
            // the swing "reads" against the boss.
            Time.timeScale = 0.05f;
            Time.fixedDeltaTime = 0.02f * Time.timeScale;
            playerAnim?.SetTrigger("Attack");

            // Two-part audio: sword-raise whoosh right when the anim
            // starts, then Boss_Execute layered right before the impact.
            if (AudioManager.Instance != null) AudioManager.Instance.PlaySFX(AudioID.Cinematic_Whoosh);

            Vector3 phase2CamStart = mainCam.transform.position;
            Vector3 phase2CamEnd = cinematicCamPos + (bossHead - cinematicCamPos).normalized * 0.6f;
            float phase2Fov = 31f;
            elapsed = 0f;
            bool executeSfxFired = false;
            float sfxLeadTime = Mathf.Min(0.12f, gloryKillImpactOffset * 0.3f);
            while (elapsed < gloryKillImpactOffset)
            {
                elapsed += Time.unscaledDeltaTime;
                float x = Mathf.Clamp01(elapsed / gloryKillImpactOffset);
                // Ease-IN cubic: the push-in ACCELERATES into the impact —
                // building pressure toward the freeze, the inverse of the
                // arrival curve in phase 1.
                float t = x * x * x;
                mainCam.transform.position = Vector3.Lerp(phase2CamStart, phase2CamEnd, t);
                mainCam.fieldOfView = Mathf.Lerp(35f, phase2Fov, t);
                Quaternion targetRotation = Quaternion.LookRotation(bossHead - mainCam.transform.position)
                                          * Quaternion.Euler(0, 0, 8f);
                mainCam.transform.rotation = Quaternion.Slerp(mainCam.transform.rotation, targetRotation, t);

                // Fire Boss_Execute sfx a hair before the freeze so the
                // audio impact reads *at* the visual impact, not after.
                if (!executeSfxFired && elapsed >= gloryKillImpactOffset - sfxLeadTime)
                {
                    executeSfxFired = true;
                    if (AudioManager.Instance != null)
                        AudioManager.Instance.PlaySFX3D(AudioID.Boss_Execute, transform.position);
                }
                yield return null;
            }

            // ============================================================
            //  PHASE 3 · IMPACT FREEZE (gloryKillFreezeDuration, time = 0)
            //  Precisely aligned with the player's mid-swing frame.
            // ============================================================
            // Arm the time-scale safety net BEFORE we freeze. If this
            // boss GameObject is destroyed mid-glory-kill (pooled despawn,
            // scene reload) the finally that restores timeScale never
            // runs and time stays frozen forever — the guard force-
            // restores it once the expected window elapses.
            CinematicTimeGuard.Arm(gloryKillFreezeDuration + gloryKillAftermathDuration + 1.5f);

            Time.timeScale = 0f;
            Time.fixedDeltaTime = 0.02f;
            if (animator != null) animator.SetTrigger("Die");
            if (AudioManager.Instance != null)
            {
                AudioManager.Instance.PlaySFX3D(AudioID.Enemy_Die, transform.position);
            }
            if (camFollow != null) camFollow.TriggerShake(0.55f, 0.4f);
            SetColor(Color.white);
            if (deathVFXPrefab != null) Instantiate(deathVFXPrefab, transform.position + Vector3.up * 1.5f, Quaternion.identity);

            // Subtle rim-white flash — much tamer than the previous full-
            // screen burst so it reads as 'weapon impact', not 'seizure'.
            StartCoroutine(GloryKillFlashRoutine(0.28f, new Color(1f, 0.94f, 0.82f, 0.32f)));

            // Snap camera to a punched-in variant for the freeze frame.
            Vector3 phase3CamPos = phase2CamEnd + (bossHead - phase2CamEnd).normalized * 0.35f;
            mainCam.transform.position = phase3CamPos;
            mainCam.fieldOfView = phase2Fov - 2f;
            mainCam.transform.rotation = Quaternion.LookRotation(bossHead - mainCam.transform.position)
                                       * Quaternion.Euler(0, 0, 12f);

            yield return new WaitForSecondsRealtime(gloryKillFreezeDuration);
            ResetColor();
            foreach (Collider c in GetComponentsInChildren<Collider>()) c.enabled = false;

            // ============================================================
            //  PHASE 4 · AFTERMATH (gloryKillAftermathDuration, ramp to 1×)
            //  Slow-mo (0.2×) then ramps back over the last third so the
            //  boss anim + camera pull-back play out visibly.
            // ============================================================
            Time.timeScale = 0.2f;
            Time.fixedDeltaTime = 0.02f * Time.timeScale;

            // Drops spawn now — they'll gently rise into view as the camera pulls back.
            Vector3 dropOrigin = transform.position + Vector3.up * 1.5f;
            for (int i = 0; i < xpCrystalsToDrop; i++)
            {
                Vector2 off = Random.insideUnitCircle * 1.2f;
                Vector3 pos = dropOrigin + new Vector3(off.x, 0.3f, off.y);
                if (xpCrystalPrefab != null)
                {
                    if (ObjectPoolManager.Instance != null)
                        ObjectPoolManager.Instance.SpawnFromPool(xpCrystalPrefab, pos, Quaternion.identity);
                    else
                        Instantiate(xpCrystalPrefab, pos, Quaternion.identity);
                }
            }
            for (int i = 0; i < diamondsToDrop; i++)
            {
                Vector2 off = Random.insideUnitCircle * 1.4f;
                Vector3 pos = dropOrigin + new Vector3(off.x, 0.4f, off.y);
                if (diamondPrefab != null)
                {
                    if (ObjectPoolManager.Instance != null)
                        ObjectPoolManager.Instance.SpawnFromPool(diamondPrefab, pos, Quaternion.identity);
                    else
                        Instantiate(diamondPrefab, pos, Quaternion.identity);
                }
            }

            Vector3 phase4CamStart = mainCam.transform.position;
            float phase4FovStart = mainCam.fieldOfView;
            elapsed = 0f;
            while (elapsed < gloryKillAftermathDuration)
            {
                elapsed += Time.unscaledDeltaTime;
                float t = Mathf.SmoothStep(0f, 1f, elapsed / gloryKillAftermathDuration);

                mainCam.transform.position = Vector3.Lerp(phase4CamStart, aftermathCamPos, t);
                mainCam.fieldOfView = Mathf.Lerp(phase4FovStart, 55f, t);
                Quaternion targetRotation = Quaternion.LookRotation((bossPos + Vector3.up * 0.8f) - mainCam.transform.position)
                                          * Quaternion.Euler(0, 0, 4f * (1f - t));
                mainCam.transform.rotation = Quaternion.Slerp(mainCam.transform.rotation, targetRotation, t);

                // Ramp timescale back to normal over the last third of the phase.
                if (t > 0.65f)
                {
                    float ramp = (t - 0.65f) / 0.35f;
                    Time.timeScale = Mathf.Lerp(0.2f, 1f, ramp);
                    Time.fixedDeltaTime = 0.02f * Time.timeScale;
                }
                yield return null;
            }
        }
        finally
        {
            // Always return to FULL speed after a glory kill. Restoring
            // `savedTimeScale` stranded time slow when the kill was
            // triggered mid-hitstop/slowmo (savedTimeScale captured < 1).
            // A glory kill is a complete cinematic that hands control
            // back to normal gameplay — 1.0 is always correct here.
            Time.timeScale = 1f;
            Time.fixedDeltaTime = 0.02f;
            _ = savedTimeScale; _ = savedFixedDelta; // kept for reference
            if (playerAnim != null) playerAnim.updateMode = savedPlayerAnimMode;
            if (animator != null) animator.updateMode = savedBossAnimMode;
            if (playerCC != null) playerCC.enabled = true;
            if (camFollow != null) camFollow.isCinematicMode = false;
            if (GlobalHUD.Instance != null)
            {
                GlobalHUD.Instance.HideCinematicBars();
                GlobalHUD.Instance.SetCinematicDoF(false);
            }
            CinematicHandheld.End(mainCam);
        }

        StartCoroutine(DeathDissolveRoutine());
    }

    // Full-screen warm flash used during the impact freeze. Self-hosted
    // canvas so this class doesn't need to reach into GlobalHUD.
    private IEnumerator GloryKillFlashRoutine(float duration, Color color)
    {
        // Photosensitivity: skip the glory-kill screen flash entirely —
        // it's a bright full-screen pulse and pure juice, safe to drop.
        if (GameplaySettings.Photosensitive) yield break;

        var go = new GameObject("GloryKillFlash");
        var canvas = go.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 32766;
        var img = new GameObject("Fill").AddComponent<UnityEngine.UI.Image>();
        img.transform.SetParent(go.transform, false);
        var rt = img.rectTransform;
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
        img.raycastTarget = false;
        img.color = color;

        float t = 0f;
        while (t < duration)
        {
            t += Time.unscaledDeltaTime;
            float a = Mathf.Lerp(color.a, 0f, t / duration);
            img.color = new Color(color.r, color.g, color.b, a);
            yield return null;
        }
        Destroy(go);
    }

    private IEnumerator HitFlashRoutine()
    {
        SetColor(Color.white);
        yield return new WaitForSeconds(0.1f);
        if (!isStaggered && !isPreparingAttack) ResetColor();
    }

    private void SetColor(Color c)
    {
        if (meshRenderers == null) return;
        foreach (var r in meshRenderers)
        {
            if (r != null && r.material != null && r.material.HasProperty("_Color"))
                r.material.color = c;
        }
    }

    private void ResetColor()
    {
        if (meshRenderers == null || originalColors == null) return;
        for (int i = 0; i < meshRenderers.Length; i++)
        {
            if (meshRenderers[i] != null && meshRenderers[i].material != null && meshRenderers[i].material.HasProperty("_Color"))
                meshRenderers[i].material.color = originalColors[i];
        }
    }

    private IEnumerator DeathDissolveRoutine()
    {
        yield return new WaitForSeconds(2.5f);

        if (dissolveAshVFX != null) dissolveAshVFX.Play();

        float dissolveDuration = 2f;
        float elapsed = 0f;
        Vector3 startScale = transform.localScale;
        Vector3 targetScale = new Vector3(startScale.x, 0.05f, startScale.z);
        Vector3 startPos = transform.position;
        Vector3 targetPos = startPos - new Vector3(0, 1f, 0);

        while (elapsed < dissolveDuration)
        {
            elapsed += Time.deltaTime;
            float t = elapsed / dissolveDuration;

            transform.localScale = Vector3.Lerp(startScale, targetScale, t);
            transform.position = Vector3.Lerp(startPos, targetPos, t);

            if (meshRenderers != null && meshRenderers.Length > 0 && meshRenderers[0] != null && meshRenderers[0].material.HasProperty("_Color"))
            {
                SetColor(Color.Lerp(originalColors[0], Color.black, t));
            }

            yield return null;
        }

        Destroy(gameObject);
        if (bossWeapon != null) Destroy(bossWeapon);
    }
}