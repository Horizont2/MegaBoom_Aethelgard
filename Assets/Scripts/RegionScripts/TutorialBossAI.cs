using UnityEngine;
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

    [Header("Drops & Economy")]
    public GameObject xpCrystalPrefab;
    public GameObject diamondPrefab;
    public GameObject damagePopupPrefab; // ФІКС: Префаб тексту урону
    public GameObject deathVFXPrefab;
    public ParticleSystem dissolveAshVFX;

    public int xpCrystalsToDrop = 40;
    public int diamondsToDrop = 5;

    public bool isInvincible = false;

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

    private void Awake()
    {
        gameObject.layer = 9;
        animator = GetComponentInChildren<Animator>();
        if (animator != null) animator.applyRootMotion = false;

        // ФІКС: Ігноруємо ParticleSystemRenderer, щоб не ламати ефекти
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

        if (GlobalHUD.Instance != null)
        {
            GlobalHUD.Instance.ShowBossUI(bossName, currentHealth, maxHealth);
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
                    GlobalHUD.Instance.ShowPrompt("[F] EXECUTE");
                    isPromptShowing = true;
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
            {
                StartCoroutine(TelegraphAndAttackRoutine());
            }
        }
    }

    private IEnumerator TelegraphAndAttackRoutine()
    {
        isPreparingAttack = true;

        if (AudioManager.Instance != null) AudioManager.Instance.PlaySFX(AudioID.Enemy_Telegraph);
        SetColor(new Color(1f, 0.4f, 0f));

        if (playerTarget != null) playerTarget.OpenPerfectDodgeWindow(transform, attackTelegraphTime);
        if (ThreatUI.Instance != null) ThreatUI.Instance.ShowThreat(transform, attackTelegraphTime);

        yield return new WaitForSeconds(attackTelegraphTime);
        ResetColor();

        if (!isStaggered && !isDead && Vector3.Distance(transform.position, target.position) <= attackRange + 1f)
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

    public void TakeDamage(DamageInfo info)
    {
        if (isDead || isStaggered || isInvincible) return;

        currentHealth -= info.Amount;
        if (currentHealth < 0) currentHealth = 0;

        if (GlobalHUD.Instance != null) GlobalHUD.Instance.UpdateBossHealth(currentHealth, maxHealth);

        // ==========================================
        // 🛑 ФІКС: СПАВН ТЕКСТУ УРОНУ 🛑
        // ==========================================
        bool showPopups = PlayerPrefs.GetInt("Settings_DamagePopups", 1) == 1;
        if (damagePopupPrefab != null && showPopups && ObjectPoolManager.Instance != null)
        {
            GameObject popup = ObjectPoolManager.Instance.SpawnFromPool(damagePopupPrefab, transform.position + Vector3.up * 2f, Quaternion.identity);
            popup.GetComponent<DamagePopup>()?.Setup(info.Amount, info.IsCritical);
        }

        if (AudioManager.Instance != null) AudioManager.Instance.PlaySFX(AudioID.Enemy_Hurt);
        StartCoroutine(HitFlashRoutine());

        if (currentHealth <= maxHealth * staggerHealthThreshold)
        {
            EnterStaggerState();
        }
        else if (info.IsCritical)
        {
            if (animator != null) animator.SetTrigger("Hit");
        }
    }

    private void EnterStaggerState()
    {
        isStaggered = true;
        isPreparingAttack = false;

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

        StartCoroutine(StaggerPulseRoutine());
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

    private IEnumerator GloryKillRoutine()
    {
        isDead = true;
        isStaggered = false;

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
        Vector3 dirToPlayer = (playerStartPos - bossPos).normalized;
        dirToPlayer.y = 0;

        Vector3 idealPlayerPos = bossPos + dirToPlayer * 1.5f;
        if (Terrain.activeTerrain != null)
            idealPlayerPos.y = Terrain.activeTerrain.SampleHeight(idealPlayerPos) + Terrain.activeTerrain.transform.position.y;

        Quaternion idealPlayerRot = Quaternion.LookRotation((bossPos - idealPlayerPos).normalized);
        idealPlayerRot.x = 0; idealPlayerRot.z = 0;

        Vector3 sideDir = Vector3.Cross(Vector3.up, dirToPlayer).normalized;
        Vector3 midPoint = Vector3.Lerp(idealPlayerPos, bossPos, 0.5f);

        Vector3 startCamPos = mainCam.transform.position;
        Vector3 cinematicCamPos = midPoint + sideDir * 4f + Vector3.up * 1f;

        Time.timeScale = 0.1f;
        Time.fixedDeltaTime = 0.02f * Time.timeScale;

        Animator playerAnim = playerTarget.GetComponentInChildren<Animator>();
        if (playerAnim != null) playerAnim.updateMode = AnimatorUpdateMode.UnscaledTime;

        playerAnim?.SetTrigger("Attack");

        float elapsed = 0f;
        float cinematicDuration = 0.55f;

        while (elapsed < cinematicDuration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.SmoothStep(0f, 1f, elapsed / cinematicDuration);

            playerTarget.transform.position = Vector3.Lerp(playerStartPos, idealPlayerPos, t);
            playerTarget.transform.rotation = Quaternion.Slerp(playerStartRot, idealPlayerRot, t);

            mainCam.transform.position = Vector3.Lerp(startCamPos, cinematicCamPos, t);
            mainCam.fieldOfView = Mathf.Lerp(Camera.main.fieldOfView, 35f, t);

            Quaternion targetRotation = Quaternion.LookRotation((bossPos + Vector3.up * 1.2f) - mainCam.transform.position) * Quaternion.Euler(0, 0, 8f);
            mainCam.transform.rotation = Quaternion.Slerp(Camera.main.transform.rotation, targetRotation, t);

            yield return null;
        }

        Time.timeScale = 0.005f;
        camFollow?.TriggerShake(0.2f, 0.35f);

        animator.updateMode = AnimatorUpdateMode.UnscaledTime;
        animator.SetTrigger("Die");

        if (AudioManager.Instance != null) AudioManager.Instance.PlaySFX(AudioID.Enemy_Die);
        if (deathVFXPrefab != null) Instantiate(deathVFXPrefab, transform.position + Vector3.up * 1.5f, Quaternion.identity);

        foreach (Collider c in GetComponentsInChildren<Collider>()) c.enabled = false;
        ResetColor();

        if (xpCrystalPrefab != null)
        {
            for (int i = 0; i < xpCrystalsToDrop; i++)
            {
                if (ObjectPoolManager.Instance != null) ObjectPoolManager.Instance.SpawnFromPool(xpCrystalPrefab, transform.position + Vector3.up * 1.5f, Quaternion.identity);
                else Instantiate(xpCrystalPrefab, transform.position + Vector3.up * 1.5f, Quaternion.identity);
            }
        }

        if (diamondPrefab != null)
        {
            for (int i = 0; i < diamondsToDrop; i++)
            {
                if (ObjectPoolManager.Instance != null) ObjectPoolManager.Instance.SpawnFromPool(diamondPrefab, transform.position + Vector3.up * 1.5f, Quaternion.identity);
                else Instantiate(diamondPrefab, transform.position + Vector3.up * 1.5f, Quaternion.identity);
            }
        }

        yield return new WaitForSecondsRealtime(0.4f);

        Time.timeScale = 1f;
        Time.fixedDeltaTime = 0.02f;

        if (playerCC != null) playerCC.enabled = true;
        if (playerAnim != null) playerAnim.updateMode = AnimatorUpdateMode.Normal;
        if (animator != null) animator.updateMode = AnimatorUpdateMode.Normal;

        if (camFollow != null) camFollow.isCinematicMode = false;
        GlobalHUD.Instance?.HideCinematicBars();

        StartCoroutine(DeathDissolveRoutine());
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