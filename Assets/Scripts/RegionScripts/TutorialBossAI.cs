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
        gameObject.layer = 9;
        animator = GetComponentInChildren<Animator>();
        if (animator != null) animator.applyRootMotion = false;

        meshRenderers = GetComponentsInChildren<MeshRenderer>();
        originalColors = new Color[meshRenderers.Length];
        for (int i = 0; i < meshRenderers.Length; i++)
        {
            originalColors[i] = meshRenderers[i].material.color;
        }

        currentHealth = maxHealth;
    }

    private void Start()
    {
        GameObject playerObj = GameObject.FindGameObjectWithTag("Player");
        if (playerObj != null)
        {
            target = playerObj.transform;
            playerTarget = playerObj.GetComponent<PlayerController>();
        }

        if (GlobalHUD.Instance != null)
        {
            GlobalHUD.Instance.ShowBossUI(bossName, currentHealth, maxHealth);
        }

        lastAttackTime = Time.time;
    }

    private void Update()
    {
        if (isDead || target == null || playerTarget.currentHealth <= 0) return;

        // --- ЛОГІКА ДОБИВАННЯ (GLORY KILL) ---
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
            if (Terrain.activeTerrain != null) nextPos.y = Terrain.activeTerrain.SampleHeight(nextPos) + Terrain.activeTerrain.transform.position.y;
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

        Vector3 originalCamPos = mainCam.transform.position;
        Quaternion originalCamRot = mainCam.transform.rotation;
        float originalFOV = mainCam.fieldOfView;

        if (camFollow != null) camFollow.isCinematicMode = true;

        // =========================================================
        // --- ВДОСКОНАЛЕНА КАМЕРА (Зйомка збоку в Профіль) ---
        // =========================================================
        Vector3 playerPos = playerTarget.transform.position;
        Vector3 bossPos = transform.position;
        Vector3 midPoint = Vector3.Lerp(playerPos, bossPos, 0.5f);

        // Рахуємо вектор вбік від битви
        Vector3 dirToPlayer = (playerPos - bossPos).normalized; dirToPlayer.y = 0;
        Vector3 sideDir = Vector3.Cross(Vector3.up, dirToPlayer).normalized;

        // Камера стоїть в 6 метрах збоку і дивиться точно на удар
        Vector3 cinematicCamPos = midPoint + sideDir * 6f + Vector3.up * 0.8f;
        Vector3 lookAtTarget = midPoint + Vector3.up * 1.5f;

        // Сповільнюємо світ майже до нуля
        Time.timeScale = 0.05f;
        Time.fixedDeltaTime = 0.02f * Time.timeScale;

        // ВАЖЛИВО: Гравець рухається швидко (в реальному часі)
        Animator playerAnim = playerTarget.GetComponentInChildren<Animator>();
        if (playerAnim != null) playerAnim.updateMode = AnimatorUpdateMode.UnscaledTime;

        if (AudioManager.Instance != null) AudioManager.Instance.PlaySFX(AudioID.Player_Swing);
        if (playerAnim != null) playerAnim.SetTrigger("Attack");

        // 1. Наїзд камери (триває 0.4 секунди РЕАЛЬНОГО часу, поки йде замах)
        float elapsed = 0f;
        float cinematicDuration = 0.4f;
        while (elapsed < cinematicDuration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = elapsed / cinematicDuration;
            float curveT = 1f - Mathf.Pow(1f - t, 3f); // Плавне гальмування камери

            mainCam.transform.position = Vector3.Lerp(originalCamPos, cinematicCamPos, curveT);
            mainCam.fieldOfView = Mathf.Lerp(originalFOV, 35f, curveT); // Сильний зум
            mainCam.transform.rotation = Quaternion.Slerp(originalCamRot, Quaternion.LookRotation(lookAtTarget - mainCam.transform.position), curveT);

            yield return null;
        }

        // =========================================================
        // --- 2. МОМЕНТ УДАРУ (HIT STOP) ---
        // =========================================================
        if (camFollow != null) camFollow.TriggerShake(1.5f, 0.4f); // Дуже сильна тряска

        if (animator != null)
        {
            animator.speed = 1f;
            animator.updateMode = AnimatorUpdateMode.UnscaledTime; // Щоб впав епічно
            animator.SetTrigger("Die");
        }

        if (AudioManager.Instance != null) AudioManager.Instance.PlaySFX(AudioID.Enemy_Die);
        if (deathVFXPrefab != null) Instantiate(deathVFXPrefab, transform.position + Vector3.up * 2f, Quaternion.identity);

        foreach (Collider c in GetComponentsInChildren<Collider>()) c.enabled = false;
        ResetColor();

        // 3. ФОНТАН ЛУТУ
        if (xpCrystalPrefab != null)
        {
            for (int i = 0; i < xpCrystalsToDrop; i++)
            {
                if (ObjectPoolManager.Instance != null) ObjectPoolManager.Instance.SpawnFromPool(xpCrystalPrefab, transform.position + Vector3.up * 2f, Quaternion.identity);
                else Instantiate(xpCrystalPrefab, transform.position + Vector3.up * 2f, Quaternion.identity);
            }
        }

        if (diamondPrefab != null)
        {
            for (int i = 0; i < diamondsToDrop; i++)
            {
                if (ObjectPoolManager.Instance != null) ObjectPoolManager.Instance.SpawnFromPool(diamondPrefab, transform.position + Vector3.up * 2f, Quaternion.identity);
                else Instantiate(diamondPrefab, transform.position + Vector3.up * 2f, Quaternion.identity);
            }
        }

        // 4. МИЛУЄМОСЯ СМЕРТЮ В СПОВІЛЬНЕННІ
        yield return new WaitForSecondsRealtime(1.2f); // Камера зависає на секунду!

        // =========================================================
        // --- ПОВЕРНЕННЯ ЧАСУ І КЕРУВАННЯ ---
        // =========================================================
        Time.timeScale = 1f;
        Time.fixedDeltaTime = 0.02f;

        if (playerAnim != null) playerAnim.updateMode = AnimatorUpdateMode.Normal;
        if (animator != null) animator.updateMode = AnimatorUpdateMode.Normal;

        if (camFollow != null)
        {
            mainCam.fieldOfView = originalFOV;
            camFollow.isCinematicMode = false; // Камера різко стрибає назад
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