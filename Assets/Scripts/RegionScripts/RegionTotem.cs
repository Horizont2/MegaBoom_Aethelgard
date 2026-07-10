using UnityEngine;
using System.Collections;
using System.Collections.Generic;

public enum EncounterType { Boss, Swarm }

public class RegionTotem : MonoBehaviour
{
    [HideInInspector] public RegionManager manager;

    [Header("Encounter Settings")]
    public EncounterType encounterType = EncounterType.Boss;

    [Header("If Swarm: Setup Mobs")]
    public GameObject[] weakPrefabs;
    public int weakCount = 10;
    public GameObject[] mediumPrefabs;
    public int mediumCount = 5;
    public GameObject[] elitePrefabs;
    public int eliteCount = 2;

    [Header("Visuals & Cinematic")]
    public ParticleSystem idleCorruptionVFX;
    public ParticleSystem activationShieldVFX;
    public ParticleSystem skyBeamVFX;
    [Tooltip("Local-Y offset applied to the sky beam so it looks like it emerges from the top of the totem instead of the totem's origin. Negative = lower.")]
    public float skyBeamYOffset = -1.5f;
    public Light totemLight;

    [Tooltip("Ефект, який працює як маяк і показує, що з тотемом можна взаємодіяти")]
    public GameObject interactHintVFX;

    [Header("Interaction")]
    public float interactionRadius = 8f;

    [HideInInspector] public bool isPurified = false;

    private bool isLocked = false;
    [HideInInspector] public bool isActivated = false;
    private bool isPromptShowing = false;

    private List<GameObject> activeEnemies = new List<GameObject>();
    private Transform player;

    private void Start()
    {
        FindPlayer();
        if (totemLight != null) totemLight.color = Color.red;
        if (activationShieldVFX != null) { activationShieldVFX.Stop(); activationShieldVFX.gameObject.SetActive(false); }

        if (interactHintVFX != null) interactHintVFX.SetActive(true);

        // Nudge the sky beam down along local Y so it visually plugs into the
        // totem instead of hanging above it. Done once in Start so it applies
        // whether the beam is triggered by purification or by re-entering an
        // already-conquered region.
        if (skyBeamVFX != null && Mathf.Abs(skyBeamYOffset) > 0.001f)
        {
            Vector3 lp = skyBeamVFX.transform.localPosition;
            skyBeamVFX.transform.localPosition = new Vector3(lp.x, lp.y + skyBeamYOffset, lp.z);
        }
    }

    private void FindPlayer()
    {
        GameObject pObj = GameObject.FindGameObjectWithTag("Player");
        if (pObj != null) player = pObj.transform;
    }

    public void LockTotem(bool locked)
    {
        isLocked = locked;
        if (idleCorruptionVFX != null)
        {
            if (locked) idleCorruptionVFX.Stop();
            else if (!isPurified) idleCorruptionVFX.Play();
        }

        if (interactHintVFX != null) interactHintVFX.SetActive(!locked && !isPurified);
    }

    public void PlayCorruptionFlare()
    {
        if (idleCorruptionVFX != null) idleCorruptionVFX.Emit(50);
        if (totemLight != null) { totemLight.intensity *= 3f; StartCoroutine(DimLightRoutine()); }
    }

    private IEnumerator DimLightRoutine()
    {
        float start = totemLight.intensity;
        float elapsed = 0f;
        while (elapsed < 1f) { elapsed += Time.deltaTime; totemLight.intensity = Mathf.Lerp(start, start / 3f, elapsed); yield return null; }
    }

    private void Update()
    {
        if (isPurified || isActivated || isLocked) return;
        if (player == null) { FindPlayer(); if (player == null) return; }

        float dist = Vector2.Distance(new Vector2(transform.position.x, transform.position.z), new Vector2(player.position.x, player.position.z));

        if (dist <= interactionRadius)
        {
            if (!isPromptShowing && GlobalHUD.Instance != null)
            {
                if (TutorialHints.Instance != null && !TutorialHints.Instance.HasSeen("Totem"))
                {
                    TutorialHints.Instance.ShowIfNew("Totem",
                        "Stand on the corrupted totem and press <b>F</b> to purify it. A wave of enemies will spawn — survive to claim the region.", 6f);
                }
                else
                {
                    GlobalHUD.Instance.ShowPrompt("[F] PURIFY TOTEM");
                }
                isPromptShowing = true;
            }

            if (Input.GetKeyDown(KeyCode.F))
            {
                if (GlobalHUD.Instance != null) GlobalHUD.Instance.HidePrompt();
                isPromptShowing = false;

                if (interactHintVFX != null) interactHintVFX.SetActive(false);

                if (AudioManager.Instance != null) AudioManager.Instance.PlaySFX(AudioID.Totem_Activate);

                if (manager != null) manager.OnTotemActivated(this);

                StartCoroutine(ActivationEventRoutine());
            }
        }
        else if (isPromptShowing && GlobalHUD.Instance != null)
        {
            GlobalHUD.Instance.HidePrompt();
            isPromptShowing = false;
        }
    }

    private IEnumerator ActivationEventRoutine()
    {
        isActivated = true;
        EnemySpawner.IsSpawningBlocked = true;

        if (TutorialHints.Instance != null)
            TutorialHints.Instance.ShowIfNew("PurifyWave",
                "Activating a totem summons a wave. Defeat <b>every</b> enemy to purify it — the next totem unlocks afterward.", 6f);

        if (activationShieldVFX != null) { activationShieldVFX.gameObject.SetActive(true); activationShieldVFX.Play(); }
        if (Camera.main != null) Camera.main.GetComponent<CameraFollow>().TriggerShake(0.3f, 0.1f);
        if (AudioManager.Instance != null) AudioManager.Instance.PlaySFX3D(AudioID.Enemy_Telegraph, transform.position);

        if (GlobalHUD.Instance != null) GlobalHUD.Instance.SetLevelObjective(encounterType == EncounterType.Boss ? "SLAY THE OVERLORD!" : "SURVIVE THE SWARM!");
        yield return new WaitForSeconds(2f);

        int playerPower = PowerSystemManager.Instance != null ? PowerSystemManager.Instance.CalculatePlayerPower() : 100;
        int recommendedPower = manager.currentRegion != null ? manager.currentRegion.recommendedPower : 100;
        float hpMultBase = manager.currentRegion != null ? manager.currentRegion.enemyHpMultiplier : 1f;
        float dmgMultBase = manager.currentRegion != null ? manager.currentRegion.enemyDamageMultiplier : 1f;

        float difficultyMult = PowerSystemManager.CalculateDifficultyMultiplier(playerPower, recommendedPower);
        float finalHpMult = hpMultBase * difficultyMult;
        float finalDmgMult = dmgMultBase * difficultyMult;

        if (encounterType == EncounterType.Boss && manager.currentRegion != null)
        {
            if (manager.currentRegion.regionBossPrefabs == null || manager.currentRegion.regionBossPrefabs.Length == 0)
            {
                Debug.LogError("🚨 ПОМИЛКА: Ти забув додати префаб Боса в 'Region Boss Prefabs' в RegionData цього регіону!");
                yield break;
            }

            for (int i = 0; i < manager.currentRegion.regionBossPrefabs.Length; i++)
            {
                SpawnEntity(manager.currentRegion.regionBossPrefabs[i], finalHpMult, finalDmgMult);
                yield return new WaitForSeconds(0.8f);
            }
        }
        else
        {
            yield return StartCoroutine(SpawnSwarmRoutine(weakPrefabs, weakCount, finalHpMult * 0.8f, finalDmgMult * 0.8f));
            yield return StartCoroutine(SpawnSwarmRoutine(mediumPrefabs, mediumCount, finalHpMult, finalDmgMult));
            yield return StartCoroutine(SpawnSwarmRoutine(elitePrefabs, eliteCount, finalHpMult * 1.5f, finalDmgMult * 1.2f));
        }

        StartCoroutine(MonitorCombatRoutine());
    }

    private IEnumerator SpawnSwarmRoutine(GameObject[] prefabs, int count, float hpMult, float dmgMult)
    {
        if (prefabs == null || prefabs.Length == 0 || count <= 0) yield break;
        for (int i = 0; i < count; i++)
        {
            SpawnEntity(prefabs[Random.Range(0, prefabs.Length)], hpMult, dmgMult);
            yield return new WaitForSeconds(0.15f);
        }
    }

    private void SpawnEntity(GameObject prefab, float hpMult, float dmgMult)
    {
        Vector3 spawnPos = transform.position + (Vector3)(Random.insideUnitCircle.normalized * Random.Range(6f, 12f));
        spawnPos.y = GetGroundHeight(spawnPos);

        GameObject entity = Instantiate(prefab, spawnPos, Quaternion.identity);
        activeEnemies.Add(entity);

        // ФІКС: Більше ніяких isBoss прапорців. Перевіряємо напряму, чи це бос!
        TutorialBossAI bossAI = entity.GetComponent<TutorialBossAI>();
        if (bossAI != null)
        {
            bossAI.InitializeBoss(hpMult, dmgMult);
            bossAI.ActivateBoss();
            if (Camera.main != null) Camera.main.GetComponent<CameraFollow>().TriggerShake(0.2f, 0.1f);
        }
        else
        {
            EnemyAI enemyAI = entity.GetComponent<EnemyAI>();
            if (enemyAI != null)
            {
                enemyAI.maxHealth *= hpMult;
                enemyAI.damage *= dmgMult;
            }
        }
    }

    private IEnumerator MonitorCombatRoutine()
    {
        while (true)
        {
            activeEnemies.RemoveAll(item => item == null);

            if (activeEnemies.Count == 0)
            {
                yield return new WaitForSeconds(3f);
                LocalPurify();
                break;
            }
            yield return new WaitForSeconds(1f);
        }
    }

    private void LocalPurify()
    {
        isPurified = true;

        if (activationShieldVFX != null) activationShieldVFX.Stop();
        if (idleCorruptionVFX != null) idleCorruptionVFX.Stop();
        if (interactHintVFX != null) interactHintVFX.SetActive(false);

        if (skyBeamVFX != null) { skyBeamVFX.gameObject.SetActive(true); skyBeamVFX.Play(); }
        if (totemLight != null) { totemLight.color = new Color(0f, 0.8f, 1f); totemLight.intensity *= 3f; }
        if (Camera.main != null) Camera.main.GetComponent<CameraFollow>().TriggerShake(0.4f, 0.1f);

        if (manager != null) manager.OnTotemPurified(this);
    }

    private float GetGroundHeight(Vector3 pos)
    {
        if (Physics.Raycast(pos + Vector3.up * 50f, Vector3.down, out RaycastHit hit, 100f, LayerMask.GetMask("Default", "Terrain", "Ground"))) return hit.point.y;
        if (Terrain.activeTerrain != null) return Terrain.activeTerrain.SampleHeight(pos) + Terrain.activeTerrain.transform.position.y;
        return pos.y;
    }
}