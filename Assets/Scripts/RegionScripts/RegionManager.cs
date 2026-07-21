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
            // Захоплений регіон = гравець просто "заходить у гості". Мобі
            // мають спавнитися як у звичайному world-gen (радіально навколо
            // гравця), тільки без totem-місії та без патрулів по мапі —
            // атмосфера "територія відвойована, але не безпечна".
            // Раніше тут стояло EnemySpawner.IsSpawningBlocked = true, що
            // повністю вимикало радіальний спавн і робило регіон порожнім.
            EnemySpawner.IsSpawningBlocked = false;

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

        string regionName = currentRegion != null
            ? LocalizationManager.Tr(currentRegion.regionName)
            : LocalizationManager.Tr("UNKNOWN REGION");
        string objective = LocalizationManager.Tr("PURIFY THE CORRUPTED TOTEMS");

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

        // Wave over — release the scene-wide "any activating" latch so the
        // next totem's F prompt starts responding again.
        RegionTotem.AnyActivatingRightNow = false;

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
        // Lock down: stop random spawns + clean lingering bosses so nothing kills
        // the player mid-victory. Regular EnemyAI will be cleared visually by a
        // shockwave below, not yanked from the world.
        EnemySpawner.IsSpawningBlocked = true;

        TutorialBossAI[] remainingBosses = Object.FindObjectsByType<TutorialBossAI>(FindObjectsSortMode.None);
        foreach (TutorialBossAI boss in remainingBosses)
        {
            if (boss != null) Destroy(boss.gameObject);
        }

        if (GlobalHUD.Instance != null)
        {
            GlobalHUD.Instance.HideLevelObjective();
            GlobalHUD.Instance.ShowCinematicBars();
            GlobalHUD.Instance.SetGameplayPanelsActive(false);
            GlobalHUD.Instance.ShowSkipPrompt(LocalizationManager.Tr("Press <b>SPACE</b> to Skip"));
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

        // === PHASE 1: shockwave purifies the world (1.2 s) ===========================
        if (AudioManager.Instance != null) AudioManager.Instance.PlaySFX(AudioID.Region_Shockwave);
        if (camFollow != null) camFollow.TriggerShake(0.5f, 0.4f);

        // Procedural corruption-departure beam — black/dark plume rising from the totem
        GameObject corruptionBeam = CreateCorruptionBeam(finalTotemPos);

        // Schedule every lingering EnemyAI to "evaporate" in a radial wave from the totem
        StartCoroutine(CleanseEnemiesWaveRoutine(finalTotemPos));

        if (CheckSkipRequested()) { yield return EarlyExitRoutine(); yield break; }
        yield return WaitOrSkip(1.2f);

        // === PHASE 2: camera glides up to apex (1.5 s) ==============================
        if (AudioManager.Instance != null) AudioManager.Instance.PlaySFX(AudioID.Cinematic_Whoosh);

        Vector3 startCamPos = mainCam.transform.position;
        Quaternion startCamRot = mainCam.transform.rotation;
        float startFov = mainCam.fieldOfView;

        float elapsed = 0f;
        while (elapsed < 1.5f)
        {
            if (CheckSkipRequested()) { yield return EarlyExitRoutine(); yield break; }
            elapsed += Time.deltaTime;
            float t = 1f - Mathf.Pow(1f - (elapsed / 1.5f), 3f);
            mainCam.transform.position = Vector3.Lerp(startCamPos, apexCamPos, t);
            mainCam.fieldOfView = Mathf.Lerp(startFov, 70f, t);
            mainCam.transform.rotation = Quaternion.Slerp(startCamRot,
                Quaternion.LookRotation(finalTotemPos - apexCamPos), t);
            yield return null;
        }

        // Quest-complete sting at apex
        if (AudioManager.Instance != null)
        {
            AudioManager.Instance.PlaySFX(AudioID.Region_VictoryStinger);
            AudioManager.Instance.PlayUI(AudioID.UI_QuestComplete);
        }
        if (camFollow != null) camFollow.TriggerShake(0.4f, 0.15f);

        // === PHASE 3: orbit camera around totem (2.5 s) =============================
        float initialFogStart = RenderSettings.fogStartDistance;
        float initialFogEnd = RenderSettings.fogEndDistance;
        float targetFogStart = initialFogStart + mapScale;
        float targetFogEnd = initialFogEnd + (mapScale * 1.5f);
        Color initialAmbient = RenderSettings.ambientLight;

        Vector3 toApex = apexCamPos - finalTotemPos;
        float orbitRadius = new Vector2(toApex.x, toApex.z).magnitude;
        float orbitHeight = toApex.y;
        float baseAngle = Mathf.Atan2(toApex.z, toApex.x);

        elapsed = 0f;
        const float orbitDuration = 2.5f;
        while (elapsed < orbitDuration)
        {
            if (CheckSkipRequested()) { yield return EarlyExitRoutine(); yield break; }
            elapsed += Time.deltaTime;
            float t = elapsed / orbitDuration;
            float smoothT = t * t * (3f - 2f * t);

            float angle = baseAngle + Mathf.Lerp(0f, 30f * Mathf.Deg2Rad, smoothT);
            Vector3 orbitPos = finalTotemPos + new Vector3(
                Mathf.Cos(angle) * orbitRadius,
                orbitHeight + Mathf.Sin(elapsed * 0.6f) * 1.5f,
                Mathf.Sin(angle) * orbitRadius);
            mainCam.transform.position = orbitPos;
            mainCam.transform.rotation = Quaternion.LookRotation(finalTotemPos - orbitPos);

            RenderSettings.fogStartDistance = Mathf.Lerp(initialFogStart, targetFogStart, smoothT);
            RenderSettings.fogEndDistance = Mathf.Lerp(initialFogEnd, targetFogEnd, smoothT);
            RenderSettings.ambientLight = Color.Lerp(initialAmbient,
                new Color(initialAmbient.r + 0.3f, initialAmbient.g + 0.3f, initialAmbient.b + 0.3f), t);

            yield return null;
        }

        if (corruptionBeam != null) Destroy(corruptionBeam);

        // === PHASE 4: title card + reward summary (3 s) =============================
        // Punch camera FOV in for 0.2s (dramatic zoom on totem) then out
        // slightly past the base for the title reveal. Sells the "beat drop".
        if (mainCam != null)
        {
            float baseFov = mainCam.fieldOfView;
            StartCoroutine(FovPunchRoutine(mainCam, baseFov, baseFov * 0.75f, baseFov * 1.08f, 0.55f));
        }

        // Bright white screen flash + short slow-mo — universal "moment of
        // triumph" combo. Slow-mo capped at 0.4 * duration real seconds
        // (uses unscaledDeltaTime everywhere in cinematic already).
        StartCoroutine(FlashScreenRoutine(0.32f, new Color(1f, 0.94f, 0.75f, 0.85f)));
        StartCoroutine(SlowMoRoutine(0.35f, 0.55f));

        // Camera shake as the flash lands.
        if (camFollow != null) camFollow.TriggerShake(0.55f, 0.25f);

        // Bigger sting on the moment the title lands, layered over the
        // existing victory stinger from phase 2.
        if (AudioManager.Instance != null)
        {
            AudioManager.Instance.PlaySFX(AudioID.Region_VictoryStinger);
            AudioManager.Instance.PlayUI(AudioID.UI_QuestComplete);
        }

        if (CinematicTitleUI.Instance != null)
        {
            CinematicTitleUI.Instance.ShowTitle(
                LocalizationManager.Tr("REGION CONQUERED"),
                LocalizationManager.Tr("THE CURSE HAS BEEN LIFTED"),
                true);
        }
        else if (GlobalHUD.Instance != null)
        {
            GlobalHUD.Instance.ShowPrompt(LocalizationManager.Tr("REGION CONQUERED") + "!");
        }

        if (CheckSkipRequested()) { yield return EarlyExitRoutine(); yield break; }
        yield return WaitOrSkip(1.5f);

        // Reward summary card via GlobalHUD prompt (fast + readable)
        if (currentRegion != null && GlobalHUD.Instance != null)
        {
            string summary = BuildRewardSummary(currentRegion);
            GlobalHUD.Instance.ShowPrompt(summary);
        }

        if (CheckSkipRequested()) { yield return EarlyExitRoutine(); yield break; }
        yield return WaitOrSkip(1.5f);

        if (GlobalHUD.Instance != null) GlobalHUD.Instance.HideSkipPrompt();
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
            // Mirror the pickup popups that resource drops trigger so the
            // big left-side reward toast shows up on region capture too —
            // previously only ResourceManager's small +N text next to the
            // resource panel fired, which felt inconsistent with normal
            // mission play.
            ShowRegionRewardToast(currentRegion);
        }

        if (dnc != null) dnc.isWeatherLocked = false;
        if (GlobalHUD.Instance != null)
        {
            GlobalHUD.Instance.HidePrompt();
            GlobalHUD.Instance.FadeAndLoadScene("CampScene");
        }
    }

    // ============================================================
    // Cinematic helpers
    // ============================================================

    private static bool CheckSkipRequested()
    {
        // Don't read skip input while a tutorial hint is on screen —
        // the player's Space press is for dismissing the hint, not for
        // skipping the cutscene underneath it.
        if (TutorialHints.IsAnyHintShowing) return false;
        if (TutorialPanelUI.IsTutorialActive) return false;
        return Input.GetKeyDown(KeyCode.Space) || Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.Escape);
    }

    private IEnumerator WaitOrSkip(float seconds)
    {
        float t = 0f;
        while (t < seconds)
        {
            if (CheckSkipRequested()) yield break;
            t += Time.deltaTime;
            yield return null;
        }
    }

    private IEnumerator EarlyExitRoutine()
    {
        if (GlobalHUD.Instance != null)
        {
            GlobalHUD.Instance.HideSkipPrompt();
            GlobalHUD.Instance.HidePrompt();
        }

        Camera mainCam = Camera.main;
        CameraFollow camFollow = mainCam != null ? mainCam.GetComponent<CameraFollow>() : null;
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
            // Mirror the pickup popups that resource drops trigger so the
            // big left-side reward toast shows up on region capture too —
            // previously only ResourceManager's small +N text next to the
            // resource panel fired, which felt inconsistent with normal
            // mission play.
            ShowRegionRewardToast(currentRegion);
        }

        DayNightCycle dnc = FindFirstObjectByType<DayNightCycle>();
        if (dnc != null) dnc.isWeatherLocked = false;

        if (GlobalHUD.Instance != null)
        {
            GlobalHUD.Instance.HideCinematicBars();
            GlobalHUD.Instance.FadeAndLoadScene("CampScene");
        }
        yield break;
    }

    // Punch the FOV inward briefly, then out to `outFov` past base, then
    // settle back to `baseFov`. Reads as a "beat drop" zoom.
    private IEnumerator FovPunchRoutine(Camera cam, float baseFov, float inFov, float outFov, float duration)
    {
        if (cam == null) yield break;
        float half = duration * 0.5f;
        float t = 0f;
        while (t < half)
        {
            if (cam == null) yield break;
            t += Time.unscaledDeltaTime;
            cam.fieldOfView = Mathf.Lerp(baseFov, inFov, Mathf.SmoothStep(0f, 1f, t / half));
            yield return null;
        }
        t = 0f;
        while (t < half)
        {
            if (cam == null) yield break;
            t += Time.unscaledDeltaTime;
            cam.fieldOfView = Mathf.Lerp(inFov, outFov, Mathf.SmoothStep(0f, 1f, t / half));
            yield return null;
        }
        // Settle back to base FOV over 0.3s.
        t = 0f;
        while (t < 0.3f)
        {
            if (cam == null) yield break;
            t += Time.unscaledDeltaTime;
            cam.fieldOfView = Mathf.Lerp(outFov, baseFov, Mathf.SmoothStep(0f, 1f, t / 0.3f));
            yield return null;
        }
        cam.fieldOfView = baseFov;
    }

    // Full-screen colour flash — quick fade-in then long fade-out via
    // GlobalHUD's cinematic-bar canvas group as a stand-in overlay. If
    // the HUD doesn't expose a flash, this method spawns its own throw-away
    // canvas so it always works.
    private IEnumerator FlashScreenRoutine(float duration, Color color)
    {
        var go = new GameObject("CinematicFlash");
        var canvas = go.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 32767; // above everything
        var img = new GameObject("Fill").AddComponent<UnityEngine.UI.Image>();
        img.transform.SetParent(go.transform, false);
        var rt = img.rectTransform;
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
        img.raycastTarget = false;
        img.color = new Color(color.r, color.g, color.b, 0f);

        // Fade in (fast)
        float half = duration * 0.25f;
        float t = 0f;
        while (t < half)
        {
            t += Time.unscaledDeltaTime;
            img.color = new Color(color.r, color.g, color.b, Mathf.Lerp(0f, color.a, t / half));
            yield return null;
        }
        // Fade out (long tail)
        float tail = duration * 0.75f;
        t = 0f;
        while (t < tail)
        {
            t += Time.unscaledDeltaTime;
            img.color = new Color(color.r, color.g, color.b, Mathf.Lerp(color.a, 0f, t / tail));
            yield return null;
        }
        Destroy(go);
    }

    // Ramp Time.timeScale down, hold, then ramp back to 1. All cinematic
    // coroutines already use unscaledDeltaTime, so their timing isn't
    // affected — only the world / VFX / physics feel the slow-mo.
    private IEnumerator SlowMoRoutine(float minScale, float duration)
    {
        float baseScale = Time.timeScale;
        float rampIn = 0.08f;
        float hold = Mathf.Max(0f, duration - rampIn - 0.25f);
        float rampOut = 0.25f;

        float t = 0f;
        while (t < rampIn)
        {
            t += Time.unscaledDeltaTime;
            Time.timeScale = Mathf.Lerp(baseScale, minScale, t / rampIn);
            yield return null;
        }
        Time.timeScale = minScale;
        yield return new WaitForSecondsRealtime(hold);
        t = 0f;
        while (t < rampOut)
        {
            t += Time.unscaledDeltaTime;
            Time.timeScale = Mathf.Lerp(minScale, baseScale, t / rampOut);
            yield return null;
        }
        Time.timeScale = baseScale;
    }

    private GameObject CreateCorruptionBeam(Vector3 totemPos)
    {
        GameObject beam = new GameObject("CorruptionDeparture");
        beam.transform.position = totemPos + Vector3.up * 2f;

        LineRenderer lr = beam.AddComponent<LineRenderer>();
        lr.positionCount = 2;
        lr.SetPosition(0, totemPos + Vector3.up * 1.5f);
        lr.SetPosition(1, totemPos + Vector3.up * 80f);
        lr.startWidth = 1.8f;
        lr.endWidth = 0.6f;
        lr.useWorldSpace = true;
        lr.numCapVertices = 4;

        Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
        if (shader == null) shader = Shader.Find("Unlit/Color");
        if (shader == null) shader = Shader.Find("Sprites/Default");
        Material mat = new Material(shader);
        Color dark = new Color(0.1f, 0.05f, 0.15f, 0.85f);
        if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", dark);
        if (mat.HasProperty("_Color")) mat.SetColor("_Color", dark);
        if (mat.HasProperty("_Surface"))
        {
            mat.SetFloat("_Surface", 1f);
            mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            mat.SetInt("_ZWrite", 0);
            mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
        }
        lr.material = mat;
        lr.startColor = dark;
        lr.endColor = new Color(dark.r, dark.g, dark.b, 0f);

        return beam;
    }

    private IEnumerator CleanseEnemiesWaveRoutine(Vector3 origin)
    {
        EnemyAI[] enemies = Object.FindObjectsByType<EnemyAI>(FindObjectsSortMode.None);
        if (enemies == null || enemies.Length == 0) yield break;

        // Sort by distance so close ones go first — feels like a real shockwave
        System.Array.Sort(enemies, (a, b) =>
        {
            float da = a == null ? float.MaxValue : (a.transform.position - origin).sqrMagnitude;
            float db = b == null ? float.MaxValue : (b.transform.position - origin).sqrMagnitude;
            return da.CompareTo(db);
        });

        for (int i = 0; i < enemies.Length; i++)
        {
            EnemyAI enemy = enemies[i];
            if (enemy == null) continue;

            // Spawn a quick puff at the enemy's location, then destroy them.
            // Using Destroy keeps the existing 'they're gone' semantic; the
            // staggered timing is what sells the wave.
            Destroy(enemy.gameObject);
            yield return new WaitForSeconds(0.05f);
        }
    }

    private string BuildRewardSummary(RegionData region)
    {
        return $"<b>{LocalizationManager.Tr("REGION REWARDS")}</b>\n" +
               $"<color=#A0E0FF>+{region.diamondReward}</color> {LocalizationManager.Tr("Diamonds")}   " +
               $"<color=#D4B07A>+{region.woodReward}</color> {LocalizationManager.Tr("Wood")}\n" +
               $"<color=#B0B0B0>+{region.stoneReward}</color> {LocalizationManager.Tr("Stone")}   " +
               $"<color=#E0C260>+{region.foodReward}</color> {LocalizationManager.Tr("Food")}";
    }

    // Fire the same left-side pickup popups that ResourceDrop uses when
    // the player picks up wood/stone/food during normal play — so the
    // region-clear reward feels consistent with the rest of the game.
    private void ShowRegionRewardToast(RegionData region)
    {
        if (GlobalHUD.Instance == null || region == null) return;
        if (region.woodReward > 0)
            GlobalHUD.Instance.ShowPickupPopup($"+{region.woodReward} {LocalizationManager.Tr("Wood")}", new Color(0.85f, 0.6f, 0.35f));
        if (region.stoneReward > 0)
            GlobalHUD.Instance.ShowPickupPopup($"+{region.stoneReward} {LocalizationManager.Tr("Stone")}", new Color(0.8f, 0.8f, 0.85f));
        if (region.foodReward > 0)
            GlobalHUD.Instance.ShowPickupPopup($"+{region.foodReward} {LocalizationManager.Tr("Food")}", new Color(0.7f, 0.95f, 0.5f));
        if (region.diamondReward > 0)
            GlobalHUD.Instance.ShowPickupPopup($"+{region.diamondReward} {LocalizationManager.Tr("Diamonds")}", new Color(0.63f, 0.88f, 1f));
    }
}