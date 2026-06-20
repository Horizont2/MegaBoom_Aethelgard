using UnityEngine;
using System.Collections;
using System.Collections.Generic;

public class RegionManager : MonoBehaviour
{
    [HideInInspector] public RegionData currentRegion;

    [Header("Region Totems")]
    public List<RegionTotem> totems;

    [Header("Cinematic VFX")]
    [Tooltip("Перетягни сюди об'єкт ефекту, який вже лежить у префабі цієї Арени")]
    public GameObject corruptionTransferVFX;

    private int currentTotemIndex = 0;
    private PlayerController playerController;

    private void Start()
    {
        // Вимикаємо ефект на самому початку гри
        if (corruptionTransferVFX != null) corruptionTransferVFX.SetActive(false);

        if (GameManager.Instance != null && GameManager.Instance.currentRegion != null)
        {
            currentRegion = GameManager.Instance.currentRegion;
        }
        if (currentRegion == null && MissionInitializer.PendingMissionRegion != null)
        {
            currentRegion = MissionInitializer.PendingMissionRegion;
        }

        bool isConquered = currentRegion != null && currentRegion.currentState == RegionState.Conquered;

        if (isConquered)
        {
            // ЗАПОБІЖНИК: Блокуємо спавн ворогів у вже захопленому регіоні
            EnemySpawner.IsSpawningBlocked = true;

            DayNightCycle dnc = FindFirstObjectByType<DayNightCycle>();
            if (dnc != null)
            {
                dnc.isWeatherLocked = false;
                dnc.ForceWeather(WeatherState.Clear);
            }

            for (int i = 0; i < totems.Count; i++)
            {
                totems[i].manager = this;
                totems[i].LockTotem(false);
                totems[i].isPurified = true;

                if (totems[i].skyBeamVFX != null) { totems[i].skyBeamVFX.gameObject.SetActive(true); totems[i].skyBeamVFX.Play(); }
                if (totems[i].totemLight != null) { totems[i].totemLight.color = new Color(0f, 0.8f, 1f); totems[i].totemLight.intensity *= 3f; }
                if (totems[i].idleCorruptionVFX != null) { totems[i].idleCorruptionVFX.Stop(); }
                if (totems[i].activationShieldVFX != null) { totems[i].activationShieldVFX.Stop(); totems[i].activationShieldVFX.gameObject.SetActive(false); }
            }

            GameObject pObj = GameObject.FindGameObjectWithTag("Player");
            if (pObj != null) playerController = pObj.GetComponent<PlayerController>();

            return;
        }

        for (int i = 0; i < totems.Count; i++)
        {
            totems[i].manager = this;
            totems[i].LockTotem(false);
        }

        DayNightCycle dncStorm = FindFirstObjectByType<DayNightCycle>();
        if (dncStorm != null)
        {
            dncStorm.isWeatherLocked = true;
            dncStorm.weatherTransitionSpeed = 10f;
            dncStorm.ForceWeather(WeatherState.Storm);
        }

        GameObject pObjStart = GameObject.FindGameObjectWithTag("Player");
        if (pObjStart != null) playerController = pObjStart.GetComponent<PlayerController>();

        StartCoroutine(IntroRoutine());
    }

    private IEnumerator IntroRoutine()
    {
        while (!WorldGenerator.IsGenerationDone) yield return null;
        yield return new WaitForSeconds(1f);

        string regionName = currentRegion != null ? currentRegion.regionName : "UNKNOWN REGION";
        string objective = "PURIFY THE CORRUPTED TOTEMS";

        if (CinematicTitleUI.Instance != null)
        {
            CinematicTitleUI.Instance.ShowTitle(regionName.ToUpper(), objective, false);
        }
    }

    public void OnTotemActivated(RegionTotem activatedTotem)
    {
        EnemySpawner.IsSpawningBlocked = true;

        foreach (var t in totems)
        {
            if (t != activatedTotem) t.LockTotem(true);
        }

        if (currentTotemIndex == 0)
        {
            if (activatedTotem.encounterType != EncounterType.Swarm)
            {
                RegionTotem swarmTotem = totems.Find(t => t.encounterType == EncounterType.Swarm);
                if (swarmTotem != null) SwapTotemConfigs(activatedTotem, swarmTotem);
            }
        }
        else if (currentTotemIndex == totems.Count - 1)
        {
            if (activatedTotem.encounterType != EncounterType.Boss)
            {
                RegionTotem bossTotem = totems.Find(t => t.encounterType == EncounterType.Boss);
                if (bossTotem != null) SwapTotemConfigs(activatedTotem, bossTotem);
            }
        }
    }

    private void SwapTotemConfigs(RegionTotem a, RegionTotem b)
    {
        EncounterType tempType = a.encounterType;
        a.encounterType = b.encounterType;
        b.encounterType = tempType;

        var wP = a.weakPrefabs; a.weakPrefabs = b.weakPrefabs; b.weakPrefabs = wP;
        int wC = a.weakCount; a.weakCount = b.weakCount; b.weakCount = wC;

        var mP = a.mediumPrefabs; a.mediumPrefabs = b.mediumPrefabs; b.mediumPrefabs = mP;
        int mC = a.mediumCount; a.mediumCount = b.mediumCount; b.mediumCount = mC;

        var eP = a.elitePrefabs; a.elitePrefabs = b.elitePrefabs; b.elitePrefabs = eP;
        int eC = a.eliteCount; a.eliteCount = b.eliteCount; b.eliteCount = eC;
    }

    public void OnTotemPurified(RegionTotem purifiedTotem)
    {
        currentTotemIndex++;

        if (currentTotemIndex < totems.Count)
        {
            RegionTotem nextTotem = totems.Find(t => !t.isPurified && t != purifiedTotem);
            if (nextTotem != null)
            {
                StartCoroutine(TransferCorruptionRoutine(purifiedTotem.transform.position, nextTotem));
            }
        }
        else
        {
            StartCoroutine(FinalRegionPurificationRoutine(purifiedTotem.transform.position));
        }
    }

    private IEnumerator TransferCorruptionRoutine(Vector3 startPos, RegionTotem nextTotem)
    {
        yield return new WaitForSeconds(2f);

        if (corruptionTransferVFX != null)
        {
            // Ставимо ефект над очищеним тотемом і вмикаємо його
            corruptionTransferVFX.transform.position = startPos + Vector3.up * 2f;
            corruptionTransferVFX.SetActive(true);

            // Перезапускаємо всі частинки, щоб вони почали малюватися з нуля
            ParticleSystem[] pSystems = corruptionTransferVFX.GetComponentsInChildren<ParticleSystem>();
            foreach (var ps in pSystems)
            {
                ps.Stop();
                ps.Play();
            }
        }

        if (Camera.main != null) Camera.main.GetComponent<CameraFollow>().TriggerShake(0.4f, 0.15f);

        float duration = 2.5f;
        float elapsed = 0f;
        Vector3 targetPos = nextTotem.transform.position + Vector3.up * 2f;

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = elapsed / duration;

            Vector3 currentPos = Vector3.Lerp(startPos, targetPos, t);
            currentPos.y += Mathf.Sin(t * Mathf.PI) * 8f; // Дуга польоту

            if (corruptionTransferVFX != null) corruptionTransferVFX.transform.position = currentPos;
            if (Camera.main != null && Random.value > 0.8f) Camera.main.GetComponent<CameraFollow>().TriggerShake(0.1f, 0.05f);

            yield return null;
        }

        // Вимикаємо ефект, коли він долетів
        if (corruptionTransferVFX != null) corruptionTransferVFX.SetActive(false);

        if (Camera.main != null) Camera.main.GetComponent<CameraFollow>().TriggerShake(0.5f, 0.3f);
        if (AudioManager.Instance != null) AudioManager.Instance.PlaySFX(AudioID.Enemy_Telegraph);

        DayNightCycle dnc = FindFirstObjectByType<DayNightCycle>();
        if (dnc != null) dnc.ForceWeather(WeatherState.Precipitation);

        EnemySpawner.IsSpawningBlocked = false;

        nextTotem.LockTotem(false);
        nextTotem.PlayCorruptionFlare();
    }

    private IEnumerator FinalRegionPurificationRoutine(Vector3 finalTotemPos)
    {
        // ---------------------------------------------------------
        // ЗАПОБІЖНИК ВІД СМЕРТІ В КАТСЦЕНІ
        // Блокуємо спавн і вбиваємо всіх залишкових ворогів на мапі
        // ---------------------------------------------------------
        EnemySpawner.IsSpawningBlocked = true;

        EnemyAI[] remainingEnemies = Object.FindObjectsByType<EnemyAI>(FindObjectsSortMode.None);
        foreach (EnemyAI enemy in remainingEnemies)
        {
            if (enemy != null) Destroy(enemy.gameObject);
        }

        TutorialBossAI[] remainingBosses = Object.FindObjectsByType<TutorialBossAI>(FindObjectsSortMode.None);
        foreach (TutorialBossAI boss in remainingBosses)
        {
            if (boss != null) Destroy(boss.gameObject);
        }
        // ---------------------------------------------------------

        if (GlobalHUD.Instance != null)
        {
            GlobalHUD.Instance.HideLevelObjective();
            GlobalHUD.Instance.ShowCinematicBars();
            GlobalHUD.Instance.SetGameplayPanelsActive(false);
        }

        if (playerController != null) playerController.isControlBlocked = true;

        Camera mainCam = Camera.main;
        CameraFollow camFollow = mainCam != null ? mainCam.GetComponent<CameraFollow>() : null;
        if (camFollow != null) camFollow.isCinematicMode = true;

        DayNightCycle dnc = FindFirstObjectByType<DayNightCycle>();
        if (dnc != null)
        {
            dnc.isWeatherLocked = true;
            dnc.weatherTransitionSpeed = 0.5f;
            dnc.skyboxFadeSpeed = 0.5f;
            dnc.ForceWeather(WeatherState.Clear);
        }

        float mapScale = 200f;
        if (Terrain.activeTerrain != null) mapScale = Terrain.activeTerrain.terrainData.size.x;
        float camHeight = Mathf.Clamp(mapScale * 0.15f, 35f, 60f);

        Vector3 apexCamPos = finalTotemPos + new Vector3(0f, camHeight, -camHeight * 0.8f);
        Vector3 endPanPos = apexCamPos + new Vector3(30f, 0f, 10f);

        float elapsed = 0f;
        while (elapsed < 2.5f)
        {
            elapsed += Time.deltaTime;
            float t = 1f - Mathf.Pow(1f - (elapsed / 2.5f), 3f);

            mainCam.transform.position = Vector3.Lerp(mainCam.transform.position, apexCamPos, t);
            mainCam.fieldOfView = Mathf.Lerp(mainCam.fieldOfView, 70f, t);
            mainCam.transform.rotation = Quaternion.Slerp(mainCam.transform.rotation, Quaternion.LookRotation(finalTotemPos - mainCam.transform.position), t);
            yield return null;
        }

        yield return new WaitForSeconds(0.4f);

        if (camFollow != null) camFollow.TriggerShake(0.6f, 0.15f);
        if (AudioManager.Instance != null) AudioManager.Instance.PlayUI(AudioID.UI_QuestComplete);

        float initialFogStart = RenderSettings.fogStartDistance;
        float initialFogEnd = RenderSettings.fogEndDistance;
        float targetFogStart = initialFogStart + mapScale;
        float targetFogEnd = initialFogEnd + (mapScale * 1.5f);
        Color initialAmbient = RenderSettings.ambientLight;

        elapsed = 0f;
        while (elapsed < 4.5f)
        {
            elapsed += Time.deltaTime;
            float t = elapsed / 4.5f;
            float smoothT = t * t * (3f - 2f * t);

            mainCam.transform.position = Vector3.Lerp(apexCamPos, endPanPos, t);
            mainCam.transform.rotation = Quaternion.LookRotation(finalTotemPos - mainCam.transform.position);

            RenderSettings.fogStartDistance = Mathf.Lerp(initialFogStart, targetFogStart, smoothT);
            RenderSettings.fogEndDistance = Mathf.Lerp(initialFogEnd, targetFogEnd, smoothT);
            RenderSettings.ambientLight = Color.Lerp(initialAmbient, new Color(initialAmbient.r + 0.3f, initialAmbient.g + 0.3f, initialAmbient.b + 0.3f), t);

            yield return null;
        }

        if (CinematicTitleUI.Instance != null)
        {
            CinematicTitleUI.Instance.ShowTitle("REGION CONQUERED", "THE CURSE HAS BEEN LIFTED", true);
        }
        else if (GlobalHUD.Instance != null)
        {
            GlobalHUD.Instance.ShowPrompt("REGION CONQUERED!");
        }

        yield return new WaitForSeconds(5f);

        if (camFollow != null) { mainCam.fieldOfView = 60f; camFollow.isCinematicMode = false; }
        if (playerController != null) playerController.isControlBlocked = false;

        if (currentRegion != null)
        {
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
        }

        if (dnc != null) dnc.isWeatherLocked = false;
        if (GlobalHUD.Instance != null)
        {
            GlobalHUD.Instance.HidePrompt();
            GlobalHUD.Instance.FadeAndLoadScene("CampScene");
        }
    }
}