using UnityEngine;
using System.Collections;
using System.Collections.Generic;

public class RegionTotem : MonoBehaviour
{
    [Header("Region Link")]
    public RegionData currentRegion;

    [Header("Visuals & Cinematic")]
    public ParticleSystem idleCorruptionVFX;
    public ParticleSystem purifyExplosionVFX;
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

        if (Camera.main != null) Camera.main.GetComponent<CameraFollow>().TriggerShake(0.4f, 0.1f);
        if (AudioManager.Instance != null) AudioManager.Instance.PlaySFX(AudioID.Enemy_Telegraph);

        if (GlobalHUD.Instance != null)
            GlobalHUD.Instance.SetLevelObjective("SLAY THE REGION BOSSES!");

        EnemySpawner.IsSpawningBlocked = true;

        yield return new WaitForSeconds(2f);

        int playerPower = PowerSystemManager.Instance != null ? PowerSystemManager.Instance.CalculatePlayerPower() : 100;
        int powerDelta = playerPower - currentRegion.recommendedPower;
        float difficultyMultiplier = powerDelta < 0 ? Mathf.Clamp(1f + (Mathf.Abs(powerDelta) * 0.02f), 1f, 3.0f) : Mathf.Clamp(1f - (powerDelta * 0.005f), 0.7f, 1f);

        float finalHpMult = currentRegion.enemyHpMultiplier * difficultyMultiplier;
        float finalDmgMult = currentRegion.enemyDamageMultiplier * difficultyMultiplier;

        for (int i = 0; i < currentRegion.regionBossPrefabs.Length; i++)
        {
            Vector2 randomCircle = Random.insideUnitCircle.normalized * 8f;
            Vector3 spawnPos = transform.position + new Vector3(randomCircle.x, 0, randomCircle.y);
            spawnPos.y = GetGroundHeight(spawnPos);

            GameObject bossObj = Instantiate(currentRegion.regionBossPrefabs[i], spawnPos, Quaternion.identity);

            TutorialBossAI bossAI = bossObj.GetComponent<TutorialBossAI>();
            if (bossAI != null) bossAI.InitializeBoss(finalHpMult, finalDmgMult);

            activeBosses.Add(bossObj);

            if (Camera.main != null) Camera.main.GetComponent<CameraFollow>().TriggerShake(0.2f, 0.15f);
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

        Camera mainCam = Camera.main;
        CameraFollow camFollow = mainCam != null ? mainCam.GetComponent<CameraFollow>() : null;

        Vector3 originalCamPos = mainCam.transform.position;
        Quaternion originalCamRot = mainCam.transform.rotation;
        float originalFOV = mainCam.fieldOfView;

        if (camFollow != null) camFollow.isCinematicMode = true;

        // =========================================================
        // --- ¿¿¿ ≈‘≈ “: —”œ≈–-ƒ–ŒÕ (Œ„Îˇ‰ ‚Ò¸Ó„Ó Â„≥ÓÌÛ) ---
        // =========================================================
        Vector3 terrainCenter = transform.position;
        float mapScale = 200f;
        if (Terrain.activeTerrain != null)
        {
            Vector3 tPos = Terrain.activeTerrain.transform.position;
            Vector3 tSize = Terrain.activeTerrain.terrainData.size;
            terrainCenter = tPos + new Vector3(tSize.x / 2f, 0f, tSize.z / 2f);
            mapScale = tSize.x;
        }

        // ‘≤ — 1: ∆ÓÒÚÍÓ Ó·ÏÂÊÛ∫ÏÓ ‚ËÒÓÚÛ ÔÓÎ¸ÓÚÛ (‚≥‰ 35 ‰Ó 70 ÏÂÚ≥‚ Ï‡ÍÒËÏÛÏ)
        float camHeight = Mathf.Clamp(mapScale * 0.15f, 35f, 70f);
        Vector3 apexCamPos = terrainCenter + new Vector3(0f, camHeight, -camHeight * 0.8f);
        Vector3 endPanPos = apexCamPos + new Vector3(camHeight * 0.5f, 0f, camHeight * 0.2f);
        Vector3 lookAtTarget = terrainCenter;

        // 1. ÿ¬»ƒ »… «À≤“ ” Õ≈¡Œ
        float elapsed = 0f;
        float riseDuration = 2.5f;
        while (elapsed < riseDuration)
        {
            elapsed += Time.deltaTime;
            float t = 1f - Mathf.Pow(1f - (elapsed / riseDuration), 3f);

            mainCam.transform.position = Vector3.Lerp(originalCamPos, apexCamPos, t);
            mainCam.fieldOfView = Mathf.Lerp(originalFOV, 70f, t);
            mainCam.transform.rotation = Quaternion.Slerp(originalCamRot, Quaternion.LookRotation(lookAtTarget - mainCam.transform.position), t);
            yield return null;
        }

        // --- ≈œ≤◊Õ»… ¬»¡”’ —¬≤“À¿ ---
        if (idleCorruptionVFX != null) idleCorruptionVFX.Stop();
        if (purifyExplosionVFX != null)
        {
            purifyExplosionVFX.gameObject.SetActive(true);
            purifyExplosionVFX.Play();
        }
        if (totemLight != null) { totemLight.color = new Color(0f, 0.8f, 1f); totemLight.intensity *= 5f; }

        if (camFollow != null) camFollow.TriggerShake(0.6f, 0.25f);
        if (AudioManager.Instance != null) AudioManager.Instance.PlayUI(AudioID.UI_QuestComplete);

        DayNightCycle dnc = FindFirstObjectByType<DayNightCycle>();
        if (dnc != null)
        {
            // ‘≤ — 2: «¿À»ÿ¿™ÃŒ œŒ√Œƒ” «¿¡ÀŒ Œ¬¿ÕŒﬁ! ŸÓ· ÌÂ ÔÓ˜‡‚Òˇ ‡Ì‰ÓÏÌËÈ ‰Ó˘
            dnc.isWeatherLocked = true;
            dnc.weatherTransitionSpeed = 4.0f;
            dnc.skyboxFadeSpeed = 3.0f;
            dnc.ForceWeather(WeatherState.Clear);
        }

        float initialFogStart = RenderSettings.fogStartDistance;
        float initialFogEnd = RenderSettings.fogEndDistance;

        float targetFogStart = initialFogStart + mapScale;
        float targetFogEnd = initialFogEnd + (mapScale * 1.5f);

        Color initialAmbient = RenderSettings.ambientLight;

        // 2. œÀ¿¬Õ¿ œ¿ÕŒ–¿Ã¿ “¿ ¬≤ƒ—“”œ “”Ã¿Õ”
        elapsed = 0f;
        float clearDuration = 4.5f;
        while (elapsed < clearDuration)
        {
            elapsed += Time.deltaTime;
            float t = elapsed / clearDuration;
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

        // --- œŒ¬≈–Õ≈ÕÕﬂ ---
        if (camFollow != null)
        {
            mainCam.fieldOfView = originalFOV;
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