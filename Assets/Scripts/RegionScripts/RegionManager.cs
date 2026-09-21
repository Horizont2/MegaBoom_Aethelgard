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
    private bool _finalPurificationStarted = false; // guards the victory cinematic against re-entry
    // True while the final victory cinematic is playing. GlobalHUD checks this so
    // ESC can't open the pause menu mid-flythrough (which set timeScale=1 and
    // broke the sequence). The cinematic has its own SPACE-to-skip.
    public static bool CinematicActive = false;
    private PlayerController playerController;

    // A static that gates other systems must not survive the scene that owns it.
    // Leaving a region while the flag was still set (a death, a forced load, an
    // exception mid-cinematic) would carry "a cinematic is playing" into camp,
    // where nothing ever clears it — and LevelUpManager, which now holds picks
    // back during cinematics, would never show a card again.
    private void OnDestroy()
    {
        CinematicActive = false;
    }

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

        // Drop destroyed entries first: a null in the list makes the predicate
        // below throw, and the region then never finishes at all.
        totems.RemoveAll(t => t == null);

        // Decide on what is actually LEFT, not on an index against the list
        // length. Those two disagree the moment the list holds a totem that was
        // already purified, destroyed, or added as an extra capture point — and
        // when they disagreed, this branch was taken, no next totem was found,
        // and the method simply returned: region complete, nothing happens, the
        // player stranded in a cleansed region with no way back to camp.
        RegionTotem nextTotem = totems.Find(t => !t.isPurified && t != purifiedTotem);

        if (nextTotem != null)
        {
            StartCoroutine(TransferCorruptionRoutine(purifiedTotem.transform.position, nextTotem));
        }
        else
        {
            // Guard: only run the victory cinematic (and its stingers) ONCE.
            // If OnTotemPurified is somehow reached again, the second run stacked
            // another victory stinger — that was the "victory sound plays many
            // times" bug.
            if (_finalPurificationStarted) return;
            _finalPurificationStarted = true;
            // Longer than the cinematic can plausibly run. The watchdog now
            // escalates to a hard scene load rather than firing once and giving
            // up, so a false positive would cut a legitimate cutscene short —
            // worth erring on the generous side.
            // 100 seconds, then two polite retries six seconds apart, then a
            // hard load: the player was looking at a frozen region for nearly
            // two minutes before the net caught them, which is long past the
            // point where anyone would have quit. 30s, and the escalation is
            // quicker behind it.
            RegionExitWatchdog.Arm(30f);
            StartCoroutine(FinalRegionPurificationRoutine(purifiedTotem.transform.position));
        }
    }

    // Last-resort guarantee that a won region hands the player back to camp.
    //
    // Everything that returns the player runs inside one long coroutine on this
    // component. A coroutine is not a promise: disable or destroy the owner, let
    // an exception escape any step, and every line after it -- including the
    // scene load at the very end -- silently never runs, leaving the player
    // stranded in a region they have already beaten with no way out but Alt+F4.
    // This runs on its own DontDestroyOnLoad object, on unscaled time, and
    // stands down the moment the scene actually changes.
    private class RegionExitWatchdog : MonoBehaviour
    {
        private static RegionExitWatchdog s_instance;
        private float _deadline;
        private int _attempts;
        private float _nextAttempt;

        public static void Arm(float seconds)
        {
            if (s_instance == null)
            {
                var go = new GameObject("[RegionExitWatchdog]");
                DontDestroyOnLoad(go);
                s_instance = go.AddComponent<RegionExitWatchdog>();
            }
            s_instance._deadline = Time.unscaledTime + seconds;
            s_instance._attempts = 0;
            s_instance._nextAttempt = 0f;
            s_instance.enabled = true;
        }

        private void Update()
        {
            string active = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
            if (active == "CampScene") { enabled = false; return; }
            if (Time.unscaledTime < _deadline) return;
            if (Time.unscaledTime < _nextAttempt) return;

            // It used to fire once and switch itself off, which made it useless
            // against the failure it exists for: SceneLoader hands off to
            // LoadingManager, and LoadingManager was silently dropping the
            // request whenever its isLoading latch was stuck up. The watchdog
            // "did its job", disabled itself, and the player stayed stranded.
            // So it now VERIFIES — it keeps watching until the scene actually
            // changes, and escalates if the polite route is not working.
            _attempts++;
            _nextAttempt = Time.unscaledTime + 4f;

            // Whatever stalled may well have been a zero timeScale.
            if (Time.timeScale < 0.01f) Time.timeScale = 1f;
            CinematicActive = false;
            EnemyAI.GlobalFreeze = false;

            if (_attempts <= 2)
            {
                Debug.LogWarning($"[RegionManager] Victory sequence never returned to camp (attempt {_attempts}) — forcing the load so the player isn't stranded in a conquered region.");
                SceneLoader.LoadScene("CampScene");
                return;
            }

            // Last resort: a hard, synchronous load. No fade, no loading art,
            // nothing that can be latched or swallowed. It looks abrupt, and an
            // abrupt return to camp is enormously better than a region the
            // player can only leave by killing the process.
            Debug.LogError("[RegionManager] Still in the region after repeated load attempts — hard-loading CampScene.");
            enabled = false;
            UnityEngine.SceneManagement.SceneManager.LoadScene("CampScene");
        }
    }

    private IEnumerator TransferCorruptionRoutine(Vector3 startPos, RegionTotem nextTotem)
    {
        // Realtime: a level-up card screen opening on the kill that purified the
        // totem sets timeScale to 0, and a scaled wait here would leave the next
        // totem permanently un-activated -- the region becomes uncompletable.
        yield return new WaitForSecondsRealtime(2f);

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
            elapsed += Time.unscaledDeltaTime;
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

    // Debug hook: fire the full victory purification cinematic on demand
    // without having to clear every totem.
    public void DebugTriggerVictoryCinematic()
    {
        if (_finalPurificationStarted) return;
        _finalPurificationStarted = true;
        Vector3 pos = transform.position;
        if (totems != null && totems.Count > 0 && totems[totems.Count - 1] != null)
            pos = totems[totems.Count - 1].transform.position;
        StartCoroutine(FinalRegionPurificationRoutine(pos));
    }

    // Taking a region: stop the fight, hand over the spoils, show them, leave.
    //
    // This replaced a three-hundred-line cinematic — hero beat, heal-the-land
    // shockwave, bird flythrough, slow-mo, FOV drift, dolly, title card, reward
    // hold — with fifteen skip points and a dozen globals to restore before the
    // player could go anywhere. Any one of them failing stranded the player in a
    // conquered region, and that kept happening across three separate attempts
    // to fix it. The failure was structural: a path that long always has a
    // branch that does not reach the end.
    //
    // Everything that MUST happen now happens in the first twenty lines, before
    // any presentation at all. The reward is granted, the region is marked
    // conquered and the counter is bumped whether or not a single frame of the
    // screen ever draws. See RegionVictoryScreen for the rest.
    private IEnumerator FinalRegionPurificationRoutine(Vector3 finalTotemPos)
    {
        EnemySpawner.IsSpawningBlocked = true;
        EnemyAI.GlobalFreeze = true;
        CinematicActive = true;

        foreach (var boss in Object.FindObjectsByType<TutorialBossAI>(FindObjectsSortMode.None))
            if (boss != null) Destroy(boss.gameObject);

        if (playerController != null) { playerController.isControlBlocked = true; playerController.isCinematicInvincible = true; }
        if (GlobalHUD.Instance != null)
        {
            GlobalHUD.Instance.HideLevelObjective();
            GlobalHUD.Instance.SetGameplayPanelsActive(false);
            GlobalHUD.Instance.HidePrompt();
        }
        if (AudioManager.Instance != null)
            AudioManager.Instance.PlaySFXOnce(AudioID.Region_VictoryStinger, 6f);

        // BANK IT FIRST. Not after the screen, not on the way out — here, where
        // nothing can be skipped past it.
        //
        // ==== AND NOTHING HERE MAY KILL THE COROUTINE ====
        //
        // This is the failure that survived three rewrites. An exception thrown
        // anywhere in this method does not log a stack trace against the region
        // and move on — it ENDS the coroutine on the spot, and every line after
        // it never runs. By this point control is already blocked, the HUD is
        // already hidden and the enemies are already frozen, so what the player
        // gets is a scene that has stopped responding with no screen and no way
        // out. GrantRegionRewards reaches into ResourceManager's UI, which
        // belongs to the CAMP scene and may be half-destroyed out here; that is
        // exactly the kind of NRE this has to survive.
        try { GrantRegionRewards(); }
        catch (System.Exception e)
        {
            Debug.LogError("[RegionManager] Granting the region reward threw — the region is still marked " +
                           "conquered and the player still goes home. " + e);
        }

        // A breath before the black, so the killing blow reads before the UI
        // arrives. Unscaled: a level-up card or the pause menu must not stop it.
        float t = 0f;
        while (t < 0.7f) { t += Time.unscaledDeltaTime; yield return null; }

        bool leaving = false;
        try
        {
            var awards = RegionVictoryScreen.AwardsFor(currentRegion);
            RegionVictoryScreen.Show(
                LocalizationManager.Tr("REGION CONQUERED"),
                LocalizationManager.Tr("THE CURSE HAS BEEN LIFTED"),
                awards,
                () => leaving = true);
        }
        catch (System.Exception e)
        {
            // The presentation is the one part of this that is allowed to fail.
            // If it cannot be built, the player does not stand in a dead region
            // waiting for a screen that is never coming — they go home now.
            Debug.LogError("[RegionManager] The victory screen could not be shown. Going straight to camp. " + e);
            leaving = true;
        }

        // The screen has its own watchdog; this one guards against the screen
        // itself never being built. Two independent timers, because "the player
        // cannot leave the region" is the one failure this must not have again.
        //
        // Sixty seconds was far too generous for a safety net. The screen's own
        // watchdog releases at 45s, so this only ever runs when the screen is
        // NOT there — and a player looking at a frozen region does not wait a
        // minute to find out whether the game is broken, they quit. Eighteen
        // seconds is long enough to read four numbers and short enough that the
        // failure is a hiccup rather than a hang.
        float guard = 0f;
        while (!leaving && guard < 18f) { guard += Time.unscaledDeltaTime; yield return null; }
        if (!leaving)
            Debug.LogError("[RegionManager] The victory screen never reported done in 18s — it was probably " +
                           "never built or never drew. Loading CampScene anyway.");

        EnemyAI.GlobalFreeze = false;
        CinematicActive = false;
        if (playerController != null) { playerController.isControlBlocked = false; playerController.isCinematicInvincible = false; }
        DayNightCycle dnc = FindFirstObjectByType<DayNightCycle>();
        if (dnc != null) dnc.isWeatherLocked = false;

        Debug.Log("[RegionManager] Victory screen dismissed — loading CampScene.");
        SceneLoader.LoadScene("CampScene");
    }

    // Marks the region taken and pays for it. Idempotent: a second call cannot
    // double-count the campaign counter, which several camp missions gate on.
    private void GrantRegionRewards()
    {
        if (currentRegion == null) return;

        bool wasAlreadyConquered = PlayerPrefs.GetInt("RegionState_" + currentRegion.regionID, 0) == 2;
        currentRegion.currentState = RegionState.Conquered;
        PlayerPrefs.SetInt("RegionState_" + currentRegion.regionID, 2);
        PlayerPrefs.SetInt("AutoOpenMap", 1);
        if (!wasAlreadyConquered)
            PlayerPrefs.SetInt("TotalConqueredRegions", PlayerPrefs.GetInt("TotalConqueredRegions", 0) + 1);
        PlayerPrefs.Save();

        if (_rewardsGranted) return;
        _rewardsGranted = true;

        if (ResourceManager.Instance != null)
        {
            ResourceManager.Instance.AddStashResources(currentRegion.woodReward, currentRegion.stoneReward, currentRegion.foodReward);
            ResourceManager.Instance.diamonds += currentRegion.diamondReward;
            ResourceManager.Instance.SaveStash();
            ResourceManager.Instance.UpdateUI();
        }
    }

    private bool _rewardsGranted;

    // ============================================================
    // Cinematic helpers
    //
    // MOSTLY RETIRED. The victory cinematic these served is gone — see
    // FinalRegionPurificationRoutine for why. They are left in place rather
    // than swept out in the same change as a behaviour fix, because the block
    // is interleaved: the corruption-transfer flow between totems still runs
    // and still uses CreateCorruptionBeam, GroundHeightAt and CreateLifeMotes,
    // so deleting the range wholesale would take live code with it. Clearing
    // out the genuinely dead ones is a separate, careful pass.
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

    // UNSCALED on purpose. The victory sequence runs its own slow-motion ramps,
    // and anything else that zeroes Time.timeScale while it plays -- a level-up
    // card screen, the pause menu -- used to stop this dead. The routine then
    // never reached the FadeAndLoadScene at its end, so the player sat on the
    // victory title card in the conquered region forever. No wait in this
    // sequence may depend on scaled time.
    private IEnumerator WaitOrSkip(float seconds)
    {
        float t = 0f;
        while (t < seconds)
        {
            if (CheckSkipRequested()) yield break;
            t += Time.unscaledDeltaTime;
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
        if (playerController != null) { playerController.isControlBlocked = false; playerController.isCinematicInvincible = false; }
        EnemyAI.GlobalFreeze = false;
        RegionManager.CinematicActive = false;

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
        else
        {
            // The main victory routine already had this fallback; skipping the
            // cinematic went down this branch instead and simply ended with no
            // scene load at all if the HUD singleton was gone.
            SceneLoader.LoadScene("CampScene");
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

    // ─────────────────────────────────────────────────────────────────────
    //  HEAL THE LAND — bird flight REMOVED per feedback. Keep the good part
    //  (cursed trees blooming + fog/sun warming) but hold a calm, slow crane
    //  on the totem instead of the disliked swooping flythrough.
    // ─────────────────────────────────────────────────────────────────────
    private IEnumerator HealTheLandRoutine(Camera cam, Vector3 apexCamPos, Vector3 totemPos, float mapScale)
    {
        if (cam == null) yield break;

        // Snapshot the corrupt render state we lerp AWAY from.
        float fog0Start = RenderSettings.fogStartDistance;
        float fog0End = RenderSettings.fogEndDistance;
        float fog0Dens = RenderSettings.fogDensity;
        Color fog0Col = RenderSettings.fogColor;
        Color amb0 = RenderSettings.ambientLight;

        // Healed targets — SUBTLE. The old values slammed ambient +0.35 and the
        // sun to 1.6x, which (together with the instant green tree "bloom") read
        // as a fake switch flipping. Now it's a gentle "the storm passes and light
        // returns" — the fog thins and warms a touch, the sun lifts slightly.
        float fogTStart = fog0Start + mapScale * 0.4f;
        float fogTEnd = fog0End + mapScale * 1.1f;
        float fogTDens = fog0Dens * 0.6f;
        Color fogTCol = Color.Lerp(fog0Col, new Color(0.80f, 0.84f, 0.90f), 0.6f);
        Color ambT = new Color(Mathf.Min(1f, amb0.r + 0.16f), Mathf.Min(1f, amb0.g + 0.18f), Mathf.Min(1f, amb0.b + 0.14f));

        // Optional gentle sun warm.
        DayNightCycle dnc = FindFirstObjectByType<DayNightCycle>();
        Light sun = dnc != null ? dnc.sunLight : null;
        float sun0 = sun != null ? sun.intensity : 1f;
        Color sunC0 = sun != null ? sun.color : Color.white;
        Color sunCT = Color.Lerp(sunC0, new Color(1f, 0.97f, 0.9f), 0.6f);

        const float healDur = 5f;

        // NOTE: the green "cursed trees bloom outward" effect was REMOVED per
        // feedback — it looked artificial. The land now heals purely through the
        // sky/light clearing (the storm was already switched to Clear earlier in
        // the sequence), which reads as natural rather than a plant-regrow switch.

        GameObject motes = CreateLifeMotes(cam.transform);

        // === PURGE BURST — a sharp climactic beat BEFORE the calm settle ======
        // The first pass faded straight into a quiet crane, which read as
        // anticlimactic ("the capture looks boring"). Now a shard of light
        // erupts from the totem, the camera punches in with a shake, and the
        // bloom wave rockets outward — so the capture LANDS as a payoff.
        var burstLightGO = new GameObject("PurgeBurst");
        burstLightGO.transform.position = totemPos + Vector3.up * 3f;
        var burstLight = burstLightGO.AddComponent<Light>();
        burstLight.type = LightType.Point;
        burstLight.color = new Color(1f, 0.97f, 0.85f);
        burstLight.range = Mathf.Max(40f, mapScale * 0.8f);
        burstLight.intensity = 0f;

        if (AudioManager.Instance != null) AudioManager.Instance.PlaySFXOnce(AudioID.Region_Shockwave, 4f);

        Vector3 punchStart = cam.transform.position;
        Vector3 punchTarget = Vector3.Lerp(punchStart, totemPos + Vector3.up * 4f, 0.28f); // push ~28% in
        Quaternion punchRotStart = cam.transform.rotation;
        const float burstDur = 0.9f;
        float bt = 0f;
        while (bt < burstDur)
        {
            if (CheckSkipRequested()) { if (burstLightGO) Destroy(burstLightGO); if (motes) Destroy(motes); yield return EarlyExitRoutine(); yield break; }
            bt += Time.unscaledDeltaTime;
            float u = Mathf.Clamp01(bt / burstDur);
            float pe = 1f - (1f - u) * (1f - u); // ease-out push
            Vector3 shake = new Vector3(Mathf.PerlinNoise(bt * 40f, 0f) - 0.5f,
                                        Mathf.PerlinNoise(0f, bt * 40f) - 0.5f, 0f) * (0.6f * (1f - u));
            cam.transform.position = Vector3.Lerp(punchStart, punchTarget, pe) + shake;
            cam.transform.rotation = Quaternion.Slerp(punchRotStart,
                Quaternion.LookRotation(totemPos + Vector3.up * 3f - cam.transform.position), pe);
            burstLight.intensity = Mathf.Sin(u * Mathf.PI) * 8f;      // spike then fall
            yield return null;
        }
        if (burstLightGO != null) Destroy(burstLightGO, 0.2f);

        // Slow crane: hold roughly at the apex, drift gently up-and-back over
        // the totem so the shot breathes while the land comes alive. No flight.
        Vector3 craneStart = cam.transform.position;
        Quaternion rotStart = cam.transform.rotation;
        float fovStart = cam.fieldOfView;
        // Gentle lift ONLY (was mapScale*0.05+6 which climbed the shot up out of
        // tree-render range). A small +4 m rise keeps the trees in frame.
        Vector3 craneEnd = craneStart + Vector3.up * 4f - (totemPos - craneStart).normalized * 3f;

        float elapsed = 0f;
        while (elapsed < healDur)
        {
            if (CheckSkipRequested()) { if (motes) Destroy(motes); yield return EarlyExitRoutine(); yield break; }
            elapsed += Time.unscaledDeltaTime;
            float e = Mathf.Clamp01(elapsed / healDur);
            float s = e * e * (3f - 2f * e); // ease in/out

            cam.transform.position = Vector3.Lerp(craneStart, craneEnd, s);
            cam.transform.rotation = Quaternion.Slerp(rotStart,
                Quaternion.LookRotation(totemPos + Vector3.up * 3f - cam.transform.position), s);
            cam.fieldOfView = Mathf.Lerp(fovStart, 62f, s);

            // Heal the world naturally through the sky/light only — the fog thins
            // and warms, the sun lifts a touch. No tree-color swap.
            RenderSettings.fogStartDistance = Mathf.Lerp(fog0Start, fogTStart, s);
            RenderSettings.fogEndDistance = Mathf.Lerp(fog0End, fogTEnd, s);
            RenderSettings.fogDensity = Mathf.Lerp(fog0Dens, fogTDens, s);
            RenderSettings.fogColor = Color.Lerp(fog0Col, fogTCol, s);
            RenderSettings.ambientLight = Color.Lerp(amb0, ambT, s);
            if (sun != null)
            {
                sun.intensity = Mathf.Lerp(sun0, sun0 * 1.25f, s);
                sun.color = Color.Lerp(sunC0, sunCT, s);
            }
            yield return null;
        }

        if (motes != null) Destroy(motes, 3f); // let the last motes drift out
    }

    // ─────────────────────────────────────────────────────────────────────
    //  BIRD-FLIGHT REVEAL — swooping flythrough while the world heals
    //  (RETIRED — no longer called; superseded by HealTheLandRoutine)
    // ─────────────────────────────────────────────────────────────────────
    private IEnumerator BirdFlightRevealRoutine(Camera cam, CameraFollow camFollow, Vector3 totemPos, float mapScale)
    {
        if (cam == null) yield break;

        // Snapshot the corrupt render state we lerp AWAY from.
        float fog0Start = RenderSettings.fogStartDistance;
        float fog0End = RenderSettings.fogEndDistance;
        float fog0Dens = RenderSettings.fogDensity;
        Color fog0Col = RenderSettings.fogColor;
        Color amb0 = RenderSettings.ambientLight;

        // Healed targets: airy, warm, bright.
        float fogTStart = fog0Start + mapScale * 0.6f;
        float fogTEnd = fog0End + mapScale * 1.6f;
        float fogTDens = fog0Dens * 0.4f;
        Color fogTCol = new Color(0.82f, 0.87f, 0.93f);
        Color ambT = new Color(Mathf.Min(1f, amb0.r + 0.35f), Mathf.Min(1f, amb0.g + 0.38f), Mathf.Min(1f, amb0.b + 0.30f));

        // Optional sun swell.
        DayNightCycle dnc = FindFirstObjectByType<DayNightCycle>();
        Light sun = dnc != null ? dnc.sunLight : null;
        float sun0 = sun != null ? sun.intensity : 1f;
        Color sunC0 = sun != null ? sun.color : Color.white;
        Color sunCT = new Color(1f, 0.96f, 0.85f);

        List<Vector3> path = BuildBirdPath(totemPos, mapScale);
        GameObject motes = CreateLifeMotes(cam.transform);

        float startFov = cam.fieldOfView;
        Vector3 prevFwd = cam.transform.forward; prevFwd.y = 0f; if (prevFwd.sqrMagnitude < 0.001f) prevFwd = Vector3.forward; prevFwd.Normalize();
        float roll = 0f;

        // Slow, majestic glide. 18s (was 13) so the reveal breathes and the
        // camera lingers long enough to actually read the trees coming alive.
        const float flightDur = 18f;

        // Sweep the curse off the land: blighted trees bloom outward from the
        // totem over the flight, so the camera literally flies over the wave of
        // life returning. Radius must reach the FARTHEST tree from the totem —
        // a totem placed off-centre sits up to ~1.4x mapScale from the opposite
        // corner, so 1.3x left far trees stuck as dead husks ("blooms only near
        // the totem, empty/dead further out"). 2.0x guarantees full coverage.
        CursedTree.BeginWorldBloom(totemPos, flightDur, mapScale * 2.0f);

        float elapsed = 0f;
        float camY = 0f; bool camYInit = false;   // smoothed altitude
        while (elapsed < flightDur)
        {
            if (CheckSkipRequested()) { if (motes) Destroy(motes); yield return EarlyExitRoutine(); yield break; }
            elapsed += Time.unscaledDeltaTime;
            float u = Mathf.Clamp01(elapsed / flightDur);
            float e = u * u * (3f - 2f * u); // ease in/out

            Vector3 pos = SampleBirdPath(path, e);
            // Terrain-follow WITHOUT the jerk: the old code hard-snapped Y to
            // ground+10 every frame, so the camera bobbed violently over every
            // hill and peak. Instead take the highest ground across a lookahead
            // window (so it rises BEFORE a peak, not on top of it) and ease the
            // altitude toward it. A hard floor still prevents clipping through a
            // very sharp ridge.
            float aheadGround = GroundHeightAt(pos);
            aheadGround = Mathf.Max(aheadGround, GroundHeightAt(SampleBirdPath(path, Mathf.Min(1f, e + 0.05f))));
            aheadGround = Mathf.Max(aheadGround, GroundHeightAt(SampleBirdPath(path, Mathf.Min(1f, e + 0.10f))));
            // Higher, gentler follow: cruise ~24m over the canopy (was 14, which
            // skimmed too close and clipped hills) with a slow lerp so peaks
            // don't jolt it, and a generous 12m hard floor so it can never slam
            // into the ground on a sharp ridge.
            float targetY = Mathf.Max(pos.y, aheadGround + 24f);
            if (!camYInit) { camY = targetY; camYInit = true; }
            camY = Mathf.Lerp(camY, targetY, Time.unscaledDeltaTime * 1.4f);
            camY = Mathf.Max(camY, GroundHeightAt(pos) + 12f);
            pos.y = camY;
            cam.transform.position = pos;

            // Bloom the cursed trees the camera is flying over, right now, so the
            // land visibly comes alive under the flight — not just a distant
            // radial wave the path never crosses (which left only green puffs).
            CursedTree.BloomNear(pos, 60f);

            // Forward = velocity along the path; gaze tilts down toward the land.
            Vector3 ahead = SampleBirdPath(path, Mathf.Min(1f, e + 0.02f));
            Vector3 fwd = ahead - pos;
            if (fwd.sqrMagnitude < 0.0001f) fwd = cam.transform.forward;
            fwd.Normalize();
            Vector3 gaze = (fwd + Vector3.down * 0.30f).normalized;
            Quaternion targetRot = Quaternion.LookRotation(gaze, Vector3.up);

            // Bank into turns like a bird: roll proportional to heading change.
            Vector3 fwdFlat = new Vector3(fwd.x, 0f, fwd.z).normalized;
            float turn = Vector3.SignedAngle(prevFwd, fwdFlat, Vector3.up);
            prevFwd = fwdFlat;
            float targetRoll = Mathf.Clamp(-turn * 4.5f, -28f, 28f);
            roll = Mathf.Lerp(roll, targetRoll, Time.unscaledDeltaTime * 3f);
            cam.transform.rotation = targetRot * Quaternion.Euler(0f, 0f, roll);

            // A touch wider FOV mid-flight for a sense of speed, easing back.
            cam.fieldOfView = Mathf.Lerp(startFov, 72f, Mathf.Sin(u * Mathf.PI));

            // Heal the world proportionally to how far we've flown.
            RenderSettings.fogStartDistance = Mathf.Lerp(fog0Start, fogTStart, e);
            RenderSettings.fogEndDistance = Mathf.Lerp(fog0End, fogTEnd, e);
            RenderSettings.fogDensity = Mathf.Lerp(fog0Dens, fogTDens, e);
            RenderSettings.fogColor = Color.Lerp(fog0Col, fogTCol, e);
            RenderSettings.ambientLight = Color.Lerp(amb0, ambT, e);
            if (sun != null)
            {
                sun.intensity = Mathf.Lerp(sun0, sun0 * 1.6f, e);
                sun.color = Color.Lerp(sunC0, sunCT, e);
            }
            yield return null;
        }

        // Crane up into a wide hero shot over the cleansed region, facing the totem.
        Vector3 heroStartPos = cam.transform.position;
        Quaternion heroStartRot = cam.transform.rotation;
        float heroFov = cam.fieldOfView;
        float heroH = Mathf.Clamp(mapScale * 0.18f, 40f, 70f);
        Vector3 heroPos = totemPos + new Vector3(heroH * 0.6f, heroH, -heroH * 0.6f);
        float ct = 0f; const float craneDur = 1.6f;
        while (ct < craneDur)
        {
            if (CheckSkipRequested()) { if (motes) Destroy(motes); yield return EarlyExitRoutine(); yield break; }
            ct += Time.unscaledDeltaTime;
            float k = ct / craneDur; k = k * k * (3f - 2f * k);
            cam.transform.position = Vector3.Lerp(heroStartPos, heroPos, k);
            cam.transform.rotation = Quaternion.Slerp(heroStartRot, Quaternion.LookRotation(totemPos + Vector3.up * 3f - heroPos), k);
            cam.fieldOfView = Mathf.Lerp(heroFov, 60f, k);
            yield return null;
        }

        if (motes != null) Destroy(motes, 3f); // let the last motes drift out
    }

    // Scenic S-curve across the map at bird height, starting by the totem.
    private List<Vector3> BuildBirdPath(Vector3 totemPos, float mapScale)
    {
        var pts = new List<Vector3>();
        Terrain terr = Terrain.activeTerrain;
        Vector3 origin = terr != null ? terr.transform.position : Vector3.zero;
        float size = terr != null ? terr.terrainData.size.x : mapScale;
        Vector3 center = origin + new Vector3(size * 0.5f, 0f, size * 0.5f);

        Vector3 dir = center - totemPos; dir.y = 0f;
        if (dir.sqrMagnitude < 1f) dir = Vector3.forward;
        dir.Normalize();
        Vector3 rightV = Vector3.Cross(Vector3.up, dir);
        float span = size * 0.55f;

        pts.Add(totemPos - dir * 8f + Vector3.up * 4f);              // launch by the totem
        pts.Add(totemPos + dir * span * 0.25f + rightV * span * 0.22f);
        pts.Add(totemPos + dir * span * 0.55f - rightV * span * 0.24f);
        pts.Add(totemPos + dir * span * 0.85f + rightV * span * 0.16f);
        pts.Add(center);                                            // finish over the interior

        for (int i = 0; i < pts.Count; i++)
        {
            Vector3 p = pts[i];
            // Clamp inside the map so we never fly off the edge into skybox.
            p.x = Mathf.Clamp(p.x, origin.x + 12f, origin.x + size - 12f);
            p.z = Mathf.Clamp(p.z, origin.z + 12f, origin.z + size - 12f);
            p.y = GroundHeightAt(p) + 14f;                          // bird height above local ground
            pts[i] = p;
        }
        return pts;
    }

    // Catmull-Rom sample over the waypoint list, param in [0,1].
    private Vector3 SampleBirdPath(List<Vector3> pts, float t)
    {
        if (pts == null || pts.Count == 0) return Vector3.zero;
        if (pts.Count == 1) return pts[0];
        int seg = pts.Count - 1;
        float ft = Mathf.Clamp01(t) * seg;
        int i = Mathf.Min((int)ft, seg - 1);
        float lt = ft - i;
        Vector3 p0 = pts[Mathf.Max(0, i - 1)];
        Vector3 p1 = pts[i];
        Vector3 p2 = pts[i + 1];
        Vector3 p3 = pts[Mathf.Min(pts.Count - 1, i + 2)];
        return 0.5f * ((2f * p1) + (-p0 + p2) * lt
            + (2f * p0 - 5f * p1 + 4f * p2 - p3) * lt * lt
            + (-p0 + 3f * p1 - 3f * p2 + p3) * lt * lt * lt);
    }

    private float GroundHeightAt(Vector3 pos)
    {
        Terrain t = Terrain.activeTerrain;
        if (t != null) return t.SampleHeight(pos) + t.transform.position.y;
        return 0f;
    }

    // Golden life-motes that drift up around the camera — "life returning".
    private GameObject CreateLifeMotes(Transform follow)
    {
        var go = new GameObject("VictoryLifeMotes");
        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop();
        var main = ps.main;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.startLifetime = new ParticleSystem.MinMaxCurve(3.5f, 6f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(0.3f, 1.0f);
        // Small, varied motes read as delicate spores of life, not blobs.
        main.startSize = new ParticleSystem.MinMaxCurve(0.06f, 0.22f);
        main.startColor = new Color(1f, 0.88f, 0.55f, 1f);
        main.startRotation = new ParticleSystem.MinMaxCurve(0f, 6.28f);
        main.maxParticles = 140;
        main.gravityModifier = -0.03f; // gentle rise
        var em = ps.emission; em.rateOverTime = 28f;
        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Box;
        shape.scale = new Vector3(45f, 8f, 45f);

        // Fade in, hold, fade out — with a soft twinkle.
        var col = ps.colorOverLifetime; col.enabled = true;
        var grad = new Gradient();
        grad.SetKeys(
            new[] { new GradientColorKey(new Color(1f, 0.95f, 0.7f), 0f), new GradientColorKey(new Color(1f, 0.8f, 0.4f), 1f) },
            new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(0.85f, 0.25f), new GradientAlphaKey(0.85f, 0.7f), new GradientAlphaKey(0f, 1f) });
        col.color = grad;

        // Size swells then settles so each mote "breathes".
        var sol = ps.sizeOverLifetime; sol.enabled = true;
        var sizeCurve = new AnimationCurve(new Keyframe(0f, 0.2f), new Keyframe(0.35f, 1f), new Keyframe(1f, 0.5f));
        sol.size = new ParticleSystem.MinMaxCurve(1f, sizeCurve);

        // Slow drift/rotation for life.
        var rot = ps.rotationOverLifetime; rot.enabled = true;
        rot.z = new ParticleSystem.MinMaxCurve(-0.6f, 0.6f);
        var noise = ps.noise; noise.enabled = true; noise.strength = 0.25f; noise.frequency = 0.3f; noise.scrollSpeed = 0.2f;

        var rend = ps.GetComponent<ParticleSystemRenderer>();
        var shader = Shader.Find("Universal Render Pipeline/Particles/Unlit") ?? Shader.Find("Legacy Shaders/Particles/Additive") ?? Shader.Find("Sprites/Default");
        // Assigned, not read back: `rend.material = new Material(...)` followed
        // by `rend.material.color` builds TWO materials and leaks both, since
        // destroying the object frees neither.
        var moteMat = new Material(shader) { color = new Color(1f, 0.88f, 0.55f, 1f) };
        rend.sharedMaterial = moteMat;
        OwnedMaterial.Attach(rend.gameObject, moteMat);
        rend.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        rend.receiveShadows = false;
        // Spread the emitter box AHEAD of and below the camera so motes rise
        // through frame rather than clumping at one point.
        if (follow != null) { go.transform.SetParent(follow, false); go.transform.localPosition = new Vector3(0f, -4f, 16f); }
        ps.Play();
        return go;
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
        lr.sharedMaterial = mat;
        OwnedMaterial.Attach(beam, mat);
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
            GlobalHUD.Instance.ShowResourceGain(region.woodReward, LocalizationManager.Tr("Wood"), new Color(0.85f, 0.6f, 0.35f));
        if (region.stoneReward > 0)
            GlobalHUD.Instance.ShowResourceGain(region.stoneReward, LocalizationManager.Tr("Stone"), new Color(0.8f, 0.8f, 0.85f));
        if (region.foodReward > 0)
            GlobalHUD.Instance.ShowResourceGain(region.foodReward, LocalizationManager.Tr("Food"), new Color(0.7f, 0.95f, 0.5f));
        if (region.diamondReward > 0)
            GlobalHUD.Instance.ShowResourceGain(region.diamondReward, LocalizationManager.Tr("Diamonds"), new Color(0.63f, 0.88f, 1f));
    }
}