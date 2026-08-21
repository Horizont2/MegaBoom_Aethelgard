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

        CameraShakeUtil.TryShake(0.4f, 0.15f);

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
            if (Random.value > 0.8f) CameraShakeUtil.TryShake(0.1f, 0.05f);

            yield return null;
        }

        // Вимикаємо ефект, коли він долетів
        if (corruptionTransferVFX != null) corruptionTransferVFX.SetActive(false);

        CameraShakeUtil.TryShake(0.5f, 0.3f);
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

        // --- AAA layer: duck the music bed for the whole sequence, rack
        // cinematic DoF on, and give the camera a living handheld drift.
        // All three are torn down in EarlyExitRoutine / the normal exit.
        if (AudioManager.Instance != null) AudioManager.Instance.DuckMusic(0.35f, 0.5f, 8.5f, 2.0f);
        if (GlobalHUD.Instance != null) GlobalHUD.Instance.SetCinematicDoF(true);
        CinematicHandheld.Begin(mainCam, 0.05f, 0.4f, 0.45f);

        // === PHASE 0: hero beat (0.7 s) =============================================
        // Hold on the player for a breath before anything erupts — the eye
        // needs an anchor shot so the shockwave that follows has context.
        // Slow push-in toward the player: classic anticipation framing.
        if (playerController != null && mainCam != null)
        {
            Vector3 preRollStart = mainCam.transform.position;
            Vector3 toPlayer = (playerController.transform.position + Vector3.up * 1.2f) - preRollStart;
            Vector3 preRollEnd = preRollStart + toPlayer.normalized * Mathf.Min(0.8f, toPlayer.magnitude * 0.1f);
            float preRoll = 0f;
            const float preRollDur = 0.7f;
            while (preRoll < preRollDur)
            {
                if (CheckSkipRequested()) { yield return EarlyExitRoutine(); yield break; }
                preRoll += Time.deltaTime;
                // Ease-in-out — the push starts and ends gently.
                float pt = Mathf.SmoothStep(0f, 1f, preRoll / preRollDur);
                mainCam.transform.position = Vector3.Lerp(preRollStart, preRollEnd, pt);
                yield return null;
            }
        }

        // === PHASE 1: shockwave purifies the world (1.2 s) ===========================
        if (AudioManager.Instance != null) AudioManager.Instance.PlaySFX(AudioID.Region_Shockwave);
        if (camFollow != null) camFollow.TriggerShake(0.5f, 0.4f);

        // Procedural corruption-departure beam — black/dark plume rising from the totem
        GameObject corruptionBeam = CreateCorruptionBeam(finalTotemPos);

        // Schedule every lingering EnemyAI to "evaporate" in a radial wave from the totem
        StartCoroutine(CleanseEnemiesWaveRoutine(finalTotemPos));

        if (CheckSkipRequested()) { yield return EarlyExitRoutine(); yield break; }
        yield return WaitOrSkip(1.2f);

        // === PHASE 2: camera rises to apex (2.0 s) ==================================
        // Slower than before (1.5 → 2.0 s) with an ease-in-out profile:
        // the old ease-out-only curve launched at full speed from frame
        // one, which read as a teleport-ish jolt. A crane operator
        // accelerates gently, cruises, then brakes into the apex.
        if (AudioManager.Instance != null) AudioManager.Instance.PlaySFX(AudioID.Cinematic_Whoosh);

        Vector3 startCamPos = mainCam.transform.position;
        Quaternion startCamRot = mainCam.transform.rotation;
        float startFov = mainCam.fieldOfView;

        float elapsed = 0f;
        const float riseDuration = 2.0f;
        while (elapsed < riseDuration)
        {
            if (CheckSkipRequested()) { yield return EarlyExitRoutine(); yield break; }
            elapsed += Time.deltaTime;
            float x = Mathf.Clamp01(elapsed / riseDuration);
            // Smoother-step (6x^5-15x^4+10x^3): zero velocity AND zero
            // acceleration at both ends — the camera never jerks.
            float t = x * x * x * (x * (x * 6f - 15f) + 10f);
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
        // AAA rewrite — single subtle "hold" moment instead of the
        // previous FOV-punch + full-screen flash + slow-mo + double
        // stinger stack, which read as strobe-disco rather than triumph.
        //
        // The beat sheet now:
        //  1. Small FOV drift (60→57°) over 0.9s — reads as a slow zoom-in
        //     rather than a punch. Nothing snaps.
        //  2. Very brief 0.15s slow-mo dip ramping back to 1× over 0.6s,
        //     so the world feels reverent for a beat but doesn't stutter.
        //  3. NO screen flash — the sky already brightened during
        //     phase 3, the title reveal shouldn't nuke the eye.
        //  4. Only the victory stinger fires (single audio hit).
        //  5. Title card fades in through CinematicTitleUI's own tween.
        if (mainCam != null)
        {
            float baseFov = mainCam.fieldOfView;
            StartCoroutine(SmoothFovRoutine(mainCam, baseFov, baseFov * 0.95f, 0.9f));
        }
        StartCoroutine(SlowMoRoutine(0.6f, 0.75f));

        if (AudioManager.Instance != null)
        {
            AudioManager.Instance.PlaySFX(AudioID.Region_VictoryStinger);
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

        // Slow non-blocking dolly toward the totem while the title +
        // reward cards sit on screen — a static camera under a title
        // card is the #1 "prototype" tell. ~1.5 m over the full 3 s.
        if (mainCam != null)
        {
            Vector3 pushDir = (finalTotemPos + Vector3.up * 2f - mainCam.transform.position).normalized;
            StartCoroutine(SlowDollyRoutine(mainCam, pushDir * 1.5f, 3.0f));
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
        // AAA layer teardown — handheld off (restores its base transform),
        // DoF back to gameplay. Duck restores itself on its own timeline.
        CinematicHandheld.End(mainCam);
        if (GlobalHUD.Instance != null) GlobalHUD.Instance.SetCinematicDoF(false);
        if (camFollow != null) { mainCam.fieldOfView = 60f; camFollow.isCinematicMode = false; }
        if (playerController != null) playerController.isControlBlocked = false;

        if (currentRegion != null)
        {
            // Bump the global conquered counter ONCE per region — this is what
            // the camp "capture your first region" mission and Elias' lore gate
            // on. Hand-played totem conquest never touched it before, so the
            // mission never ticked. Guard against a re-clear double-counting.
            bool wasAlreadyConquered = PlayerPrefs.GetInt("RegionState_" + currentRegion.regionID, 0) == 2;
            currentRegion.currentState = RegionState.Conquered;
            PlayerPrefs.SetInt("RegionState_" + currentRegion.regionID, 2);
            PlayerPrefs.SetInt("AutoOpenMap", 1);
            if (!wasAlreadyConquered)
                PlayerPrefs.SetInt("TotalConqueredRegions", PlayerPrefs.GetInt("TotalConqueredRegions", 0) + 1);
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

        // Hold on the reward screen so the player can actually read what they
        // earned before we leave for camp — it used to fade out instantly.
        // Skippable with Space / Enter / Esc.
        yield return StartCoroutine(WaitOrSkip(5f));

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
        // Tear down the AAA layer on skip too — otherwise the handheld
        // drift keeps nudging the gameplay camera after the cutscene.
        CinematicHandheld.End(mainCam);
        if (GlobalHUD.Instance != null) GlobalHUD.Instance.SetCinematicDoF(false);
        if (camFollow != null) { mainCam.fieldOfView = 60f; camFollow.isCinematicMode = false; }
        if (playerController != null) playerController.isControlBlocked = false;

        if (currentRegion != null)
        {
            // Bump the global conquered counter ONCE per region — this is what
            // the camp "capture your first region" mission and Elias' lore gate
            // on. Hand-played totem conquest never touched it before, so the
            // mission never ticked. Guard against a re-clear double-counting.
            bool wasAlreadyConquered = PlayerPrefs.GetInt("RegionState_" + currentRegion.regionID, 0) == 2;
            currentRegion.currentState = RegionState.Conquered;
            PlayerPrefs.SetInt("RegionState_" + currentRegion.regionID, 2);
            PlayerPrefs.SetInt("AutoOpenMap", 1);
            if (!wasAlreadyConquered)
                PlayerPrefs.SetInt("TotalConqueredRegions", PlayerPrefs.GetInt("TotalConqueredRegions", 0) + 1);
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

    // Constant-velocity dolly along `worldDelta` over `duration` seconds.
    // Non-blocking companion for static holds (title cards): the shot
    // keeps creeping forward, which is what separates a filmed moment
    // from a paused game. Aborts silently if the camera dies mid-push.
    private IEnumerator SlowDollyRoutine(Camera cam, Vector3 worldDelta, float duration)
    {
        if (cam == null) yield break;
        Vector3 from = cam.transform.position;
        float t = 0f;
        while (t < duration)
        {
            if (cam == null) yield break;
            t += Time.unscaledDeltaTime;
            // Linear on purpose — a creep should not visibly accelerate.
            cam.transform.position = from + worldDelta * Mathf.Clamp01(t / duration);
            yield return null;
        }
    }

    // Slow, one-way FOV drift — no snap-back. Used for the title-reveal
    // hold so the camera feels reverent, not punchy.
    private IEnumerator SmoothFovRoutine(Camera cam, float fromFov, float toFov, float duration)
    {
        if (cam == null) yield break;
        float t = 0f;
        while (t < duration)
        {
            if (cam == null) yield break;
            t += Time.unscaledDeltaTime;
            cam.fieldOfView = Mathf.Lerp(fromFov, toFov, Mathf.SmoothStep(0f, 1f, t / duration));
            yield return null;
        }
        cam.fieldOfView = toFov;
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