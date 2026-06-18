using UnityEngine;
using System.Collections;
using System.Collections.Generic;

public class RegionTotem : MonoBehaviour
{
    [Header("Region Link")]
    public RegionData currentRegion;

    [Header("Visuals & Cinematic")]
    public ParticleSystem idleCorruptionVFX;

    [Tooltip("Жовтий магічний щит (при активації)")]
    public ParticleSystem activationShieldVFX;

    [Tooltip("Червоний вибух (Red Energy Explosion)")]
    public ParticleSystem purifyExplosionVFX;

    [Tooltip("Стовп світла в небо")]
    public ParticleSystem skyBeamVFX;

    public Light totemLight;

    [Header("Interaction")]
    public float interactionRadius = 8f;
    private bool isActivated = false;
    private bool isPurified = false;
    private bool isPromptShowing = false;

    private List<GameObject> activeBosses = new List<GameObject>();
    private Transform player;
    private PlayerController playerController;

    private void Start()
    {
        FindPlayer();

        if (totemLight != null) totemLight.color = Color.red;

        // --- ФІКС: Примусово ховаємо щит на старті ---
        if (activationShieldVFX != null)
        {
            activationShieldVFX.Stop();
            activationShieldVFX.gameObject.SetActive(false);
        }

        DayNightCycle dnc = FindFirstObjectByType<DayNightCycle>();
        if (dnc != null)
        {
            dnc.isWeatherLocked = true;
            dnc.weatherTransitionSpeed = 10f;
            dnc.ForceWeather(WeatherState.Storm);
        }
    }

    private void FindPlayer()
    {
        GameObject pObj = GameObject.FindGameObjectWithTag("Player");
        if (pObj != null)
        {
            player = pObj.transform;
            playerController = pObj.GetComponent<PlayerController>();
        }
    }

    private void Update()
    {
        if (isPurified || isActivated) return;

        if (player == null)
        {
            FindPlayer();
            if (player == null) return;
        }

        Vector2 totemPosXZ = new Vector2(transform.position.x, transform.position.z);
        Vector2 playerPosXZ = new Vector2(player.position.x, player.position.z);
        float dist = Vector2.Distance(totemPosXZ, playerPosXZ);

        if (dist <= interactionRadius)
        {
            if (!isPromptShowing)
            {
                if (GlobalHUD.Instance != null) GlobalHUD.Instance.ShowPrompt("[F] PURIFY TOTEM");
                isPromptShowing = true;
            }

            if (Input.GetKeyDown(KeyCode.F))
            {
                if (GlobalHUD.Instance != null) GlobalHUD.Instance.HidePrompt();
                isPromptShowing = false;
                StartCoroutine(ActivationEventRoutine());
            }
        }
        else
        {
            if (isPromptShowing)
            {
                if (GlobalHUD.Instance != null) GlobalHUD.Instance.HidePrompt();
                isPromptShowing = false;
            }
        }
    }

    private IEnumerator ActivationEventRoutine()
    {
        isActivated = true;

        // --- АКТИВАЦІЯ ЩИТА ---
        if (activationShieldVFX != null)
        {
            activationShieldVFX.gameObject.SetActive(true);
            activationShieldVFX.Play();
        }

        // М'який поштовх при появі щита
        if (Camera.main != null) Camera.main.GetComponent<CameraFollow>().TriggerShake(0.3f, 0.1f);
        if (AudioManager.Instance != null) AudioManager.Instance.PlaySFX(AudioID.Enemy_Telegraph);

        if (GlobalHUD.Instance != null)
            GlobalHUD.Instance.SetLevelObjective("SLAY THE REGION BOSSES!");

        EnemySpawner.IsSpawningBlocked = true;

        yield return new WaitForSeconds(2f);

        int playerPower = PowerSystemManager.Instance != null ? PowerSystemManager.Instance.CalculatePlayerPower() : 100;
        float difficultyMultiplier = Mathf.Clamp(1f + (Mathf.Abs(playerPower - currentRegion.recommendedPower) * 0.02f), 0.7f, 3.0f);

        float finalHpMult = currentRegion.enemyHpMultiplier * difficultyMultiplier;
        float finalDmgMult = currentRegion.enemyDamageMultiplier * difficultyMultiplier;

        for (int i = 0; i < currentRegion.regionBossPrefabs.Length; i++)
        {
            Vector3 spawnPos = transform.position + (Vector3)(Random.insideUnitCircle.normalized * 8f);
            spawnPos.y = GetGroundHeight(spawnPos);

            GameObject bossObj = Instantiate(currentRegion.regionBossPrefabs[i], spawnPos, Quaternion.identity);

            TutorialBossAI bossAI = bossObj.GetComponent<TutorialBossAI>();
            if (bossAI != null)
            {
                bossAI.InitializeBoss(finalHpMult, finalDmgMult);
            }

            activeBosses.Add(bossObj);

            // Легка вібрація при падінні кожного боса
            if (Camera.main != null) Camera.main.GetComponent<CameraFollow>().TriggerShake(0.2f, 0.1f);

            yield return new WaitForSeconds(0.8f);
        }

        StartCoroutine(MonitorBossesRoutine());
    }

    private IEnumerator MonitorBossesRoutine()
    {
        while (true)
        {
            activeBosses.RemoveAll(item => item == null);

            if (activeBosses.Count == 0)
            {
                // Даємо час зібрати лут після вбивства
                yield return new WaitForSeconds(4f);
                StartCoroutine(PurifyCinematicRoutine());
                break;
            }
            yield return new WaitForSeconds(1f);
        }
    }

    private IEnumerator PurifyCinematicRoutine()
    {
        isPurified = true;

        EnemyAI[] remainingEnemies = FindObjectsByType<EnemyAI>(FindObjectsSortMode.None);
        foreach (var enemy in remainingEnemies)
        {
            if (enemy != null && !enemy.isInvincible)
                enemy.TakeDamage(new DamageInfo { Amount = 99999f, KnockbackForce = 0f });
        }

        if (GlobalHUD.Instance != null)
        {
            GlobalHUD.Instance.HideLevelObjective();
            GlobalHUD.Instance.ShowCinematicBars();
            GlobalHUD.Instance.SetGameplayPanelsActive(false);
        }

        if (playerController != null) playerController.isControlBlocked = true;

        if (activationShieldVFX != null) activationShieldVFX.Stop();
        if (idleCorruptionVFX != null) idleCorruptionVFX.Stop();

        Camera mainCam = Camera.main;
        CameraFollow camFollow = mainCam != null ? mainCam.GetComponent<CameraFollow>() : null;
        if (camFollow != null) camFollow.isCinematicMode = true;

        // =========================================================
        // --- ФІКС: ПОЧИНАЄМО ОЧИЩЕННЯ ПОГОДИ ОДРАЗУ ЗІ СТАРТУ ---
        // =========================================================
        DayNightCycle dnc = FindFirstObjectByType<DayNightCycle>();
        if (dnc != null)
        {
            dnc.isWeatherLocked = true;
            dnc.weatherTransitionSpeed = 0.5f; // Плавний перехід на всі 7 секунд катсцени
            dnc.skyboxFadeSpeed = 0.5f;
            dnc.ForceWeather(WeatherState.Clear);
        }

        float mapScale = 200f;
        if (Terrain.activeTerrain != null) mapScale = Terrain.activeTerrain.terrainData.size.x;

        float camHeight = Mathf.Clamp(mapScale * 0.15f, 35f, 60f);

        Vector3 apexCamPos = transform.position + new Vector3(0f, camHeight, -camHeight * 0.8f);
        Vector3 endPanPos = apexCamPos + new Vector3(30f, 0f, 10f);
        Vector3 lookAtTarget = transform.position;

        // 1. ЗЛІТ
        float elapsed = 0f;
        while (elapsed < 2.5f)
        {
            elapsed += Time.deltaTime;
            float t = 1f - Mathf.Pow(1f - (elapsed / 2.5f), 3f);

            mainCam.transform.position = Vector3.Lerp(mainCam.transform.position, apexCamPos, t);
            mainCam.fieldOfView = Mathf.Lerp(mainCam.fieldOfView, 70f, t);
            mainCam.transform.rotation = Quaternion.Slerp(mainCam.transform.rotation, Quaternion.LookRotation(lookAtTarget - mainCam.transform.position), t);
            yield return null;
        }

        if (purifyExplosionVFX != null)
        {
            purifyExplosionVFX.gameObject.SetActive(true);
            purifyExplosionVFX.Play();
        }

        yield return new WaitForSeconds(0.4f);

        if (camFollow != null) camFollow.TriggerShake(0.6f, 0.15f);
        if (AudioManager.Instance != null) AudioManager.Instance.PlayUI(AudioID.UI_QuestComplete);

        if (skyBeamVFX != null)
        {
            skyBeamVFX.gameObject.SetActive(true);
            skyBeamVFX.Play();
        }
        if (totemLight != null) { totemLight.color = new Color(0f, 0.8f, 1f); totemLight.intensity *= 5f; }

        float initialFogStart = RenderSettings.fogStartDistance;
        float initialFogEnd = RenderSettings.fogEndDistance;
        float targetFogStart = initialFogStart + mapScale;
        float targetFogEnd = initialFogEnd + (mapScale * 1.5f);
        Color initialAmbient = RenderSettings.ambientLight;

        // 2. ПАНОРАМА ТА ВІДСТУП ТУМАНУ
        elapsed = 0f;
        while (elapsed < 4.5f)
        {
            elapsed += Time.deltaTime;
            float t = elapsed / 4.5f;
            float smoothT = t * t * (3f - 2f * t);

            mainCam.transform.position = Vector3.Lerp(apexCamPos, endPanPos, t);
            mainCam.transform.rotation = Quaternion.LookRotation(lookAtTarget - mainCam.transform.position);

            RenderSettings.fogStartDistance = Mathf.Lerp(initialFogStart, targetFogStart, smoothT);
            RenderSettings.fogEndDistance = Mathf.Lerp(initialFogEnd, targetFogEnd, smoothT);
            RenderSettings.ambientLight = Color.Lerp(initialAmbient, new Color(initialAmbient.r + 0.3f, initialAmbient.g + 0.3f, initialAmbient.b + 0.3f), t);

            yield return null;
        }

        if (GlobalHUD.Instance != null) GlobalHUD.Instance.ShowPrompt("REGION CONQUERED!");

        yield return new WaitForSeconds(3f);

        if (camFollow != null)
        {
            mainCam.fieldOfView = 60f; // Відновлюємо FOV
            camFollow.isCinematicMode = false;
        }
        if (playerController != null) playerController.isControlBlocked = false;

        currentRegion.currentState = RegionState.Conquered;
        PlayerPrefs.SetInt("RegionState_" + currentRegion.regionID, 2);
        PlayerPrefs.SetInt("AutoOpenMap", 1);
        PlayerPrefs.Save();

        if (ResourceManager.Instance != null)
        {
            ResourceManager.Instance.AddStashResources(currentRegion.woodReward, currentRegion.stoneReward, currentRegion.foodReward);
            ResourceManager.Instance.diamonds += currentRegion.diamondReward;
            ResourceManager.Instance.UpdateUI();
        }

        if (dnc != null) dnc.isWeatherLocked = false;

        if (GlobalHUD.Instance != null)
        {
            GlobalHUD.Instance.HidePrompt();
            GlobalHUD.Instance.FadeAndLoadScene("CampScene");
        }
    }

    private float GetGroundHeight(Vector3 pos)
    {
        if (Physics.Raycast(pos + Vector3.up * 50f, Vector3.down, out RaycastHit hit, 100f, LayerMask.GetMask("Default", "Terrain", "Ground")))
            return hit.point.y;
        if (Terrain.activeTerrain != null)
            return Terrain.activeTerrain.SampleHeight(pos) + Terrain.activeTerrain.transform.position.y;
        return pos.y;
    }
}