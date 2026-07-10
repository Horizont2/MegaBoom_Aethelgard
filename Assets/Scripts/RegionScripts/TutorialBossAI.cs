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
    [Tooltip("Колір кругу під ногами боса в стейтджері (procedural)")]
    public Color staggerRingColor = new Color(1f, 0.65f, 0.1f, 0.8f);
    [Tooltip("Радіус кругу-маркера під босом у стагері")]
    public float staggerRingRadius = 3.5f;

    [Header("Drops & Economy")]
    public GameObject xpCrystalPrefab;
    public GameObject diamondPrefab;
    public GameObject damagePopupPrefab;
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
            GlobalHUD.Instance.ShowBossUI(bossName, currentHealth, maxHealth);
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

    public void TakeDamage(DamageInfo info)
    {
        if (isDead || isStaggered || isInvincible) return;

        currentHealth -= info.Amount;
        if (currentHealth < 0) currentHealth = 0;

        if (GlobalHUD.Instance != null) GlobalHUD.Instance.UpdateBossHealth(currentHealth, maxHealth);

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

    private IEnumerator GloryKillRoutine()
    {
        isDead = true;
        isStaggered = false;

        if (staggerRing != null) Destroy(staggerRing);

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

        float savedTimeScale = Time.timeScale;
        float savedFixedDelta = Time.fixedDeltaTime;
        Animator playerAnim = playerTarget.GetComponentInChildren<Animator>();
        AnimatorUpdateMode savedPlayerAnimMode = playerAnim != null ? playerAnim.updateMode : AnimatorUpdateMode.Normal;
        AnimatorUpdateMode savedBossAnimMode = animator != null ? animator.updateMode : AnimatorUpdateMode.Normal;

        try
        {
            Time.timeScale = 0.1f;
            Time.fixedDeltaTime = 0.02f * Time.timeScale;
            if (playerAnim != null) playerAnim.updateMode = AnimatorUpdateMode.UnscaledTime;

            if (AudioManager.Instance != null) AudioManager.Instance.PlaySFX(AudioID.Cinematic_Whoosh);

            float elapsed = 0f;
            const float cinematicDuration = 0.55f;
            while (elapsed < cinematicDuration)
            {
                elapsed += Time.unscaledDeltaTime;
                float t = Mathf.SmoothStep(0f, 1f, elapsed / cinematicDuration);

                playerTarget.transform.position = Vector3.Lerp(playerStartPos, idealPlayerPos, t);
                playerTarget.transform.rotation = Quaternion.Slerp(playerStartRot, idealPlayerRot, t);

                mainCam.transform.position = Vector3.Lerp(startCamPos, cinematicCamPos, t);
                mainCam.fieldOfView = Mathf.Lerp(mainCam.fieldOfView, 35f, t);

                Quaternion targetRotation =
                    Quaternion.LookRotation((bossPos + Vector3.up * 1.2f) - mainCam.transform.position)
                    * Quaternion.Euler(0, 0, 8f);
                mainCam.transform.rotation = Quaternion.Slerp(mainCam.transform.rotation, targetRotation, t);

                yield return null;
            }

            Time.timeScale = 0.005f;
            if (animator != null) animator.updateMode = AnimatorUpdateMode.UnscaledTime;

            playerAnim?.SetTrigger("Attack");
            yield return new WaitForSecondsRealtime(0.18f);

            if (animator != null) animator.SetTrigger("Die");
            if (AudioManager.Instance != null)
            {
                AudioManager.Instance.PlaySFX3D(AudioID.Boss_Execute, transform.position);
                AudioManager.Instance.PlaySFX3D(AudioID.Enemy_Die, transform.position);
            }
            camFollow?.TriggerShake(0.45f, 0.45f);
            SetColor(Color.white);
            if (deathVFXPrefab != null) Instantiate(deathVFXPrefab, transform.position + Vector3.up * 1.5f, Quaternion.identity);

            yield return new WaitForSecondsRealtime(0.08f);
            ResetColor();

            foreach (Collider c in GetComponentsInChildren<Collider>()) c.enabled = false;

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

            yield return new WaitForSecondsRealtime(0.55f);
        }
        finally
        {
            Time.timeScale = savedTimeScale > 0f ? savedTimeScale : 1f;
            Time.fixedDeltaTime = savedFixedDelta > 0f ? savedFixedDelta : 0.02f;
            if (playerAnim != null) playerAnim.updateMode = savedPlayerAnimMode;
            if (animator != null) animator.updateMode = savedBossAnimMode;
            if (playerCC != null) playerCC.enabled = true;
            if (camFollow != null) camFollow.isCinematicMode = false;
            if (GlobalHUD.Instance != null) GlobalHUD.Instance.HideCinematicBars();
        }

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