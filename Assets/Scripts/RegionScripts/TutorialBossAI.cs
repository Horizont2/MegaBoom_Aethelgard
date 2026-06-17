using UnityEngine;
using System.Collections;

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

    [Header("Glory Kill & Loot")]
    public GameObject xpCrystalPrefab;
    public GameObject diamondPrefab;
    public GameObject deathVFXPrefab;
    public ParticleSystem dissolveAshVFX;

    public int xpCrystalsToDrop = 40;
    public int diamondsToDrop = 5;

    private float currentHealth;
    private bool isDead = false;
    private bool isStaggered = false;
    private bool isPreparingAttack = false;
    private bool isPromptShowing = false;
    private float lastAttackTime;

    private Transform target;
    private PlayerController playerTarget;
    private Animator animator;
    private MeshRenderer[] meshRenderers;
    private Color[] originalColors;

    private void Awake()
    {
        gameObject.layer = 9; // Шар ворогів
        animator = GetComponentInChildren<Animator>();
        if (animator != null) animator.applyRootMotion = false;

        meshRenderers = GetComponentsInChildren<MeshRenderer>();
        originalColors = new Color[meshRenderers.Length];
        for (int i = 0; i < meshRenderers.Length; i++)
        {
            originalColors[i] = meshRenderers[i].material.color;
        }
    }

    // Ініціалізація ХП відбувається з RegionTotem перед тим, як показати UI
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

            // Тримаємо боса на рівні землі
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

        // Викликаємо оновлений ThreatUI із правильним часом замаху
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

    // Викликається через Animation Event під час удару боса
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
        if (isDead || isStaggered) return;

        currentHealth -= info.Amount;
        if (currentHealth < 0) currentHealth = 0;

        if (GlobalHUD.Instance != null) GlobalHUD.Instance.UpdateBossHealth(currentHealth, maxHealth);

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

        SnapToGround(); // Притискаємо до землі перед стаггером

        if (animator != null)
        {
            animator.SetBool("isMoving", false);
            animator.SetTrigger("Stagger");
            animator.speed = 1f;
        }

        // Викидаємо зброю
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

    // ==========================================================
    // --- ЕПІЧНА КАТСЦЕНА ДОБИВАННЯ (GLORY KILL) ---
    // ==========================================================
    private IEnumerator GloryKillRoutine()
    {
        isDead = true;
        isStaggered = false;
        SnapToGround(); // Фінальне притискання перед смертю

        if (GlobalHUD.Instance != null)
        {
            GlobalHUD.Instance.HideBossUI();
            GlobalHUD.Instance.ShowCinematicBars();
        }

        Camera mainCam = Camera.main;
        CameraFollow camFollow = mainCam != null ? mainCam.GetComponent<CameraFollow>() : null;

        Vector3 originalCamPos = mainCam.transform.position;
        Quaternion originalCamRot = mainCam.transform.rotation;
        float originalFOV = mainCam.fieldOfView;

        if (camFollow != null) camFollow.isCinematicMode = true;

        Vector3 playerPos = playerTarget.transform.position;
        Vector3 bossPos = transform.position;
        Vector3 midPoint = Vector3.Lerp(playerPos, bossPos, 0.5f);

        Vector3 dirToPlayer = (playerPos - bossPos).normalized; dirToPlayer.y = 0;
        Vector3 sideDir = Vector3.Cross(Vector3.up, dirToPlayer).normalized;

        // Позиції камери для ефекту Dutch Angle
        Vector3 startCinematicCamPos = midPoint + sideDir * 5f + Vector3.up * 1f;

        // Вмикаємо сповільнення для початку замаху
        Time.timeScale = 0.1f;
        Time.fixedDeltaTime = 0.02f * Time.timeScale;

        Animator playerAnim = playerTarget.GetComponentInChildren<Animator>();
        if (playerAnim != null) playerAnim.updateMode = AnimatorUpdateMode.UnscaledTime;

        if (AudioManager.Instance != null) AudioManager.Instance.PlaySFX(AudioID.Player_Swing);
        if (playerAnim != null) playerAnim.SetTrigger("Attack");

        // --- 1. ШВИДКИЙ НАЇЗД КАМЕРИ (Синхронізація з мечем) ---
        float elapsed = 0f;
        float cinematicDuration = 0.55f; // Ідеально збігається з моментом удару мечем

        while (elapsed < cinematicDuration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.SmoothStep(0f, 1f, elapsed / cinematicDuration);

            mainCam.transform.position = Vector3.Lerp(originalCamPos, startCinematicCamPos, t);
            mainCam.fieldOfView = Mathf.Lerp(originalFOV, 35f, t);

            // Завалюємо горизонт на 8 градусів
            Quaternion targetRotation = Quaternion.LookRotation((bossPos + Vector3.up * 1.2f) - mainCam.transform.position) * Quaternion.Euler(0, 0, 8f);
            mainCam.transform.rotation = Quaternion.Slerp(originalCamRot, targetRotation, t);

            yield return null;
        }

        // --- 2. МАСИВНИЙ HIT STOP (Заморожуємо час у момент удару) ---
        Time.timeScale = 0.005f;

        // Короткий і різкий удар камери
        if (camFollow != null) camFollow.TriggerShake(0.2f, 0.35f);

        if (animator != null)
        {
            animator.speed = 1f;
            animator.updateMode = AnimatorUpdateMode.UnscaledTime;
            animator.SetTrigger("Die");
        }

        if (AudioManager.Instance != null) AudioManager.Instance.PlaySFX(AudioID.Enemy_Die);
        if (deathVFXPrefab != null) Instantiate(deathVFXPrefab, transform.position + Vector3.up * 1.5f, Quaternion.identity);

        foreach (Collider c in GetComponentsInChildren<Collider>()) c.enabled = false;
        ResetColor();

        // Спавн луту фонтаном
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

        // Тримаємо Hit Stop
        yield return new WaitForSecondsRealtime(0.4f);

        // --- 3. ФОЛЛОУ-ТРУ (Трохи відпускаємо час, щоб показати падіння) ---
        Time.timeScale = 0.2f;

        Vector3 pushCamPos = startCinematicCamPos + dirToPlayer * 1.5f;
        elapsed = 0f;
        while (elapsed < 1.0f)
        {
            elapsed += Time.unscaledDeltaTime;
            mainCam.transform.position = Vector3.Lerp(startCinematicCamPos, pushCamPos, elapsed / 1.0f);
            yield return null;
        }

        // --- ПОВЕРНЕННЯ ---
        Time.timeScale = 1f;
        Time.fixedDeltaTime = 0.02f;

        if (playerAnim != null) playerAnim.updateMode = AnimatorUpdateMode.Normal;
        if (animator != null) animator.updateMode = AnimatorUpdateMode.Normal;

        if (camFollow != null)
        {
            mainCam.transform.rotation = originalCamRot;
            mainCam.fieldOfView = originalFOV;
            camFollow.isCinematicMode = false;
        }

        if (GlobalHUD.Instance != null) GlobalHUD.Instance.HideCinematicBars();

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
        foreach (var r in meshRenderers) if (r != null && r.material != null) r.material.color = c;
    }

    private void ResetColor()
    {
        if (meshRenderers == null || originalColors == null) return;
        for (int i = 0; i < meshRenderers.Length; i++)
            if (meshRenderers[i] != null && meshRenderers[i].material != null) meshRenderers[i].material.color = originalColors[i];
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
            SetColor(Color.Lerp(originalColors[0], Color.black, t));

            yield return null;
        }

        Destroy(gameObject);
        if (bossWeapon != null) Destroy(bossWeapon);
    }
}