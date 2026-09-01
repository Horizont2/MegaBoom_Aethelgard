using UnityEngine;
using System.Collections;
using UnityEngine.SceneManagement;

public enum WeatherState { Clear, Precipitation, Storm }

public class DayNightCycle : MonoBehaviour
{
    [Header("Time Settings")]
    public float dayDurationInSeconds = 300f;
    [Range(0f, 24f)] public float timeOfDay = 12f;

    [Header("AAA Skyboxes")]
    public Material daySkybox;
    public Material nightSkybox;
    public Material stormSkybox;
    public float skyboxFadeSpeed = 0.8f;

    [Header("Light Sources")]
    public Light sunLight;
    public Light moonLight;
    public Light lightningLight;

    [Header("Atmosphere (Gradients)")]
    public Gradient sunColor;
    public Gradient fogColorClear;
    public Gradient fogColorStorm;

    [Header("Intensity Curves")]
    public AnimationCurve sunIntensity;
    public AnimationCurve moonIntensity;

    [Header("Weather System")]
    public WeatherState currentWeather = WeatherState.Clear;
    public float weatherChangeInterval = 15f;
    public float weatherTransitionSpeed = 0.05f;
    [HideInInspector] public bool isWeatherLocked = false;

    [Header("Fog Settings (Linear)")]
    public float fogStartDistance = 50f;
    public float fogEndDistance = 800f;

    [Header("VFX & Particles")]
    public ParticleSystem starsParticles;
    public GameObject firefliesVFX;
    public ParticleSystem rainVFX;
    [Tooltip("God-ray / sun-shaft effect (e.g. P_Sunshaft). Fades in at dawn & dusk, orients along the sun, hidden at midday/night. Assign like firefliesVFX.")]
    public GameObject godRaysObject;
    [Tooltip("OFF by default — the procedural camera-follow placement read as a strange cluster of vertical beams. Re-enable only after a P_Sunshaft instance is hand-placed in the scene.")]
    public bool enableGodRays = false;
    [Tooltip("Peak scale of the god-ray effect at the height of golden hour.")]
    public float godRayMaxScale = 1f;
    public ParticleSystem snowVFX;
    public ParticleSystem dustVFX;

    [Header("AAA Storm Effects")]
    public GameObject lightningVFXPrefab;
    public float lightningSpawnRadius = 60f;

    private float weatherBlend = 0f;
    private float weatherTimer = 0f;
    private int currentBiome = 0;
    private Coroutine lightningCoroutine;

    private enum SkyboxType { None, Day, Night, Storm }
    private SkyboxType activeSkyboxType = SkyboxType.None;
    private Coroutine skyboxBlendCoroutine;

    private float initialRainRate = 0f;
    private float initialSnowRate = 0f;
    private float initialDustRate = 0f;

    private Camera mainCam;
    private ParticleSystem[] weatherVFXCache;

    private void Start()
    {
        mainCam = Camera.main;

        // The godRaysObject field was wired to the P_Sunshaft PREFAB ASSET, not
        // a scene instance, so it never existed in the running world. When
        // instantiated + camera-followed it read as a strange cluster of
        // vertical beams, so it's OFF by default now (enableGodRays) until a
        // P_Sunshaft is hand-placed in the scene. If the reference is a prefab,
        // instantiate a live working copy only when enabled.
        if (enableGodRays && godRaysObject != null && !godRaysObject.scene.IsValid())
        {
            godRaysObject = Instantiate(godRaysObject);
            godRaysObject.name = "Sunshaft_Runtime";
        }
        if (godRaysObject != null) godRaysObject.SetActive(false);

        if (sunLight == null)
        {
            GameObject dirLightObj = GameObject.Find("Directional Light");
            if (dirLightObj != null) sunLight = dirLightObj.GetComponent<Light>();
        }

        currentBiome = PlayerPrefs.GetInt("RegionBiomeType", 0);
        string currentScene = SceneManager.GetActiveScene().name;

        if (currentScene != "Lvl_1" && PlayerPrefs.HasKey("SavedTimeOfDay"))
            timeOfDay = PlayerPrefs.GetFloat("SavedTimeOfDay") * 24f;

        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.Linear;
        RenderSettings.fogStartDistance = fogStartDistance;
        RenderSettings.fogEndDistance = fogEndDistance;
        RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;

        if (lightningLight != null) lightningLight.intensity = 0f;
        if (moonLight != null) moonLight.color = new Color(0.6f, 0.7f, 1f);

        if (rainVFX != null) initialRainRate = rainVFX.emission.rateOverTimeMultiplier;
        if (snowVFX != null) initialSnowRate = snowVFX.emission.rateOverTimeMultiplier;
        if (dustVFX != null) initialDustRate = dustVFX.emission.rateOverTimeMultiplier;

        weatherVFXCache = new ParticleSystem[] { rainVFX, snowVFX, dustVFX };

        // Make rain/snow behave like real weather instead of a narrow column that
        // vanishes when the camera turns. Two fixes:
        //  1) World simulation space — camera-local space made existing drops
        //     jump/streak on rotation and briefly disappear.
        //  2) A wide overhead box emitter — the drops used to fall in a thin row.
        ConfigureWeatherVolume(rainVFX);
        ConfigureWeatherVolume(snowVFX);

        CheckAndUpdateSkybox();
    }

    [Header("Weather Volume")]
    [Tooltip("Horizontal size of the rain/snow emitter box (metres). Should comfortably exceed the visible radius so it never looks like a narrow strip.")]
    public float weatherBoxSize = 75f;
    [Tooltip("Height above the player the weather emitter sits at.")]
    public float weatherBoxHeight = 28f;
    private Transform weatherFollowTarget;

    private void ConfigureWeatherVolume(ParticleSystem ps)
    {
        if (ps == null) return;
        var main = ps.main;
        main.simulationSpace = ParticleSystemSimulationSpace.World; // don't rotate with the camera
        var shape = ps.shape;
        shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Box;
        shape.scale = new Vector3(weatherBoxSize, 1f, weatherBoxSize);
        // Emit straight down regardless of where the camera looks.
        ps.transform.rotation = Quaternion.identity;
    }

    // Keep the weather volume centred over the player each frame (position only —
    // never inherit rotation, so the downpour always falls vertically).
    private void FollowWeatherToPlayer()
    {
        if (rainVFX == null && snowVFX == null) return;
        if (weatherFollowTarget == null)
        {
            var p = GameObject.FindGameObjectWithTag("Player");
            if (p != null) weatherFollowTarget = p.transform;
            if (weatherFollowTarget == null) return;
        }
        Vector3 pos = weatherFollowTarget.position + Vector3.up * weatherBoxHeight;
        if (rainVFX != null) { rainVFX.transform.position = pos; rainVFX.transform.rotation = Quaternion.identity; }
        if (snowVFX != null) { snowVFX.transform.position = pos; snowVFX.transform.rotation = Quaternion.identity; }
    }

    private void Update()
    {
        FollowWeatherToPlayer();

        timeOfDay += (Time.deltaTime / dayDurationInSeconds) * 24f;
        if (timeOfDay >= 24f) timeOfDay = 0f;

        float timePercent = timeOfDay / 24f;
        float sunAngle = ((timeOfDay - 6f) / 12f) * 180f;

        if (sunLight != null)
        {
            sunLight.transform.localRotation = Quaternion.Euler(sunAngle, 170f, 0f);
            sunLight.color = sunColor.Evaluate(timePercent);
            float baseIntensity = sunIntensity.Evaluate(timePercent);
            sunLight.intensity = Mathf.Lerp(baseIntensity, baseIntensity * 0.2f, weatherBlend);
        }

        if (moonLight != null)
        {
            moonLight.transform.localRotation = Quaternion.Euler(sunAngle - 180f, 170f, 0f);
            moonLight.intensity = moonIntensity.Evaluate(timePercent) * (1f - weatherBlend);
        }

        ManageNightVFX(timePercent);

        weatherTimer += Time.deltaTime;
        if (weatherTimer >= weatherChangeInterval)
        {
            ChangeWeatherRandomly();
            weatherTimer = 0f;
        }

        CheckAndUpdateSkybox();

        float targetBlend = (currentWeather == WeatherState.Clear) ? 0f : (currentWeather == WeatherState.Storm ? 1f : 0.5f);
        // unscaledDeltaTime so the fog / weather blend keeps ticking
        // through cutscene pauses — needed for the region-clear
        // ForceWeather(Clear) to actually finish blending mid-cinematic.
        weatherBlend = Mathf.Lerp(weatherBlend, targetBlend, Time.unscaledDeltaTime * weatherTransitionSpeed);

        Color clearFog = fogColorClear.Evaluate(timePercent);
        Color stormFog = fogColorStorm.Evaluate(timePercent);
        RenderSettings.fogColor = Color.Lerp(clearFog, stormFog, weatherBlend);

        float dayMultiplier = Mathf.Clamp01(Mathf.Sin(timePercent * Mathf.PI * 2f));
        Color skyColorDay = new Color(0.88f, 0.68f, 0.81f);
        Color equatorColorDay = new Color(0.53f, 0.45f, 0.61f);
        Color groundColorDay = new Color(0.12f, 0.18f, 0.13f);
        Color skyColorNight = new Color(0.12f, 0.13f, 0.18f);
        Color equatorColorNight = new Color(0.08f, 0.09f, 0.14f);
        Color groundColorNight = new Color(0.04f, 0.05f, 0.06f);

        RenderSettings.ambientSkyColor = Color.Lerp(Color.Lerp(skyColorNight, skyColorDay, dayMultiplier), new Color(0.2f, 0.22f, 0.27f), weatherBlend);
        RenderSettings.ambientEquatorColor = Color.Lerp(Color.Lerp(equatorColorNight, equatorColorDay, dayMultiplier), new Color(0.15f, 0.18f, 0.22f), weatherBlend);
        RenderSettings.ambientGroundColor = Color.Lerp(Color.Lerp(groundColorNight, groundColorDay, dayMultiplier), new Color(0.08f, 0.1f, 0.12f), weatherBlend);

        UpdateVFXPositions();

        SmoothVFX(rainVFX, initialRainRate, currentBiome == 0 ? weatherBlend : 0f);
        SmoothVFX(dustVFX, initialDustRate, currentBiome == 1 ? weatherBlend : 0f);
        SmoothVFX(snowVFX, initialSnowRate, currentBiome == 2 ? weatherBlend : 0f);
    }

    private void SmoothVFX(ParticleSystem ps, float baseRate, float blend)
    {
        if (ps == null) return;
        var em = ps.emission;
        em.rateOverTimeMultiplier = baseRate * blend * (currentWeather == WeatherState.Storm ? 1.5f : 1f);

        if (blend > 0.05f && !ps.gameObject.activeSelf) ps.gameObject.SetActive(true);
        else if (blend <= 0.05f && ps.gameObject.activeSelf) ps.gameObject.SetActive(false);
    }

    private void CheckAndUpdateSkybox()
    {
        SkyboxType targetSkybox = SkyboxType.Day;

        if (currentWeather == WeatherState.Storm || currentWeather == WeatherState.Precipitation)
            targetSkybox = SkyboxType.Storm;
        else if (timeOfDay < 5.5f || timeOfDay > 18.5f)
            targetSkybox = SkyboxType.Night;

        if (targetSkybox != activeSkyboxType)
        {
            activeSkyboxType = targetSkybox;
            Material targetMat = daySkybox;
            if (targetSkybox == SkyboxType.Night) targetMat = nightSkybox;
            else if (targetSkybox == SkyboxType.Storm) targetMat = stormSkybox;

            if (skyboxBlendCoroutine != null) StopCoroutine(skyboxBlendCoroutine);
            skyboxBlendCoroutine = StartCoroutine(TransitionSkyboxRoutine(targetMat));
        }
    }

    private IEnumerator TransitionSkyboxRoutine(Material newSkyboxMat)
    {
        if (newSkyboxMat == null) yield break;
        Material currentMat = RenderSettings.skybox;
        Color currentFogColor = RenderSettings.fogColor;

        // Use unscaledDeltaTime so the skybox crossfade still ticks during
        // cutscenes (Time.timeScale = 0/0.1). The region-clear cinematic
        // calls ForceWeather(Clear) while the world is paused — with the
        // old Time.deltaTime path the storm sky never finished blending
        // back to clear, so the player exited the cutscene under stormy
        // light.
        if (currentMat != null && currentMat.shader != null)
        {
            // Guard against null source — Material's source-copy ctor
            // throws ArgumentNullException for sources without a shader.
            Material tempOutMat = new Material(currentMat);
            RenderSettings.skybox = tempOutMat;
            Color startTint = tempOutMat.HasProperty("_Tint") ? tempOutMat.GetColor("_Tint") : Color.gray;

            float t = 0;
            while (t < 1f)
            {
                t += Time.unscaledDeltaTime * skyboxFadeSpeed;
                if (tempOutMat.HasProperty("_Tint")) tempOutMat.SetColor("_Tint", Color.Lerp(startTint, currentFogColor, t));
                yield return null;
            }
        }

        if (newSkyboxMat.shader == null) yield break;
        Material tempInMat = new Material(newSkyboxMat);
        RenderSettings.skybox = tempInMat;
        Color originalNewTint = newSkyboxMat.HasProperty("_Tint") ? newSkyboxMat.GetColor("_Tint") : Color.gray;
        if (tempInMat.HasProperty("_Tint")) tempInMat.SetColor("_Tint", currentFogColor);

        float fadeInTimer = 0;
        while (fadeInTimer < 1f)
        {
            fadeInTimer += Time.unscaledDeltaTime * skyboxFadeSpeed;
            if (tempInMat.HasProperty("_Tint")) tempInMat.SetColor("_Tint", Color.Lerp(currentFogColor, originalNewTint, fadeInTimer));
            yield return null;
        }
        RenderSettings.skybox = newSkyboxMat;
    }

    private void UpdateVFXPositions()
    {
        if (mainCam == null) return;

        Vector3 camPos = mainCam.transform.position;
        Vector3 camForward = mainCam.transform.forward;
        camForward.y = 0;
        camForward.Normalize();

        if (starsParticles != null)
        {
            starsParticles.transform.position = camPos;
            starsParticles.transform.rotation = Quaternion.identity;
        }

        if (weatherVFXCache != null)
        {
            for (int i = 0; i < weatherVFXCache.Length; i++)
            {
                ParticleSystem vfx = weatherVFXCache[i];
                if (vfx != null && vfx.gameObject.activeSelf)
                {
                    vfx.transform.position = camPos + (Vector3.up * 15f) + (camForward * 12f);
                    vfx.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
                }
            }
        }
    }

    private void ManageNightVFX(float timePercent)
    {
        bool isNight = timeOfDay < 5f || timeOfDay > 19f;
        if (starsParticles != null && mainCam != null)
        {
            var main = starsParticles.main;
            float starAlpha = isNight ? (1f - weatherBlend) : 0f;
            main.startColor = new Color(1f, 1f, 1f, starAlpha);
        }
        if (firefliesVFX != null) firefliesVFX.SetActive(isNight && currentBiome == 0 && currentWeather == WeatherState.Clear);

        ManageGodRays();
    }

    // Golden-hour god rays: fade in around dawn (~5-8h) and dusk (~17-20h),
    // hidden at midday and night, and orient along the sun so shafts read as
    // sunlight. Fades via scale so it never pops.
    private void ManageGodRays()
    {
        if (!enableGodRays || godRaysObject == null) return;

        // Triangular bumps peaking at 6.5 (dawn) and 18.5 (dusk), ~3.5h wide
        // (widened from 2.5h so the effect is on-screen long enough to notice).
        float dawn = 1f - Mathf.Clamp01(Mathf.Abs(timeOfDay - 6.5f) / 3.5f);
        float dusk = 1f - Mathf.Clamp01(Mathf.Abs(timeOfDay - 18.5f) / 3.5f);
        float factor = Mathf.Max(dawn, dusk) * (1f - weatherBlend);   // clear skies only

        bool on = factor > 0.03f;
        if (godRaysObject.activeSelf != on) godRaysObject.SetActive(on);
        if (!on) return;

        godRaysObject.transform.localScale = Vector3.one * (godRayMaxScale * factor);

        // Follow the camera toward the sun so the shafts are actually in view.
        // Without this the prefab sat at its authored spot far from the roaming
        // player and was never seen. Place the source between the camera and the
        // sun, slightly elevated.
        if (mainCam != null)
        {
            Vector3 sunDir = (sunLight != null) ? -sunLight.transform.forward : Vector3.up;
            godRaysObject.transform.position =
                mainCam.transform.position + sunDir * 25f + Vector3.up * 6f;
        }
        // Point the shafts along the sun's travel so they read as sunlight.
        if (sunLight != null)
            godRaysObject.transform.rotation = Quaternion.LookRotation(sunLight.transform.forward, Vector3.up);
    }

    private void ChangeWeatherRandomly()
    {
        if (isWeatherLocked) return;

        float roll = Random.value;
        if (currentWeather == WeatherState.Clear)
        {
            if (roll < 0.4f) currentWeather = WeatherState.Precipitation;
            else if (roll < 0.65f) currentWeather = WeatherState.Storm;
            else currentWeather = WeatherState.Clear;
        }
        else if (currentWeather == WeatherState.Storm)
        {
            // Storms linger and can renew instead of always ending after one
            // interval — otherwise lightning never had time to strike.
            if (roll < 0.5f) currentWeather = WeatherState.Storm;
            else if (roll < 0.8f) currentWeather = WeatherState.Precipitation;
            else currentWeather = WeatherState.Clear;
        }
        else // Precipitation
        {
            if (roll < 0.55f) currentWeather = WeatherState.Clear;
            else if (roll < 0.85f) currentWeather = WeatherState.Precipitation;
            else currentWeather = WeatherState.Storm;
        }

        // Lightning in ANY biome now (was gated to biome 0, so deserts/winter
        // never got storms with lightning).
        if (currentWeather == WeatherState.Storm)
        {
            if (lightningCoroutine == null) lightningCoroutine = StartCoroutine(LightningRoutine());
        }
        else { if (lightningCoroutine != null) { StopCoroutine(lightningCoroutine); lightningCoroutine = null; } }

        ApplyRainAmbience();
    }

    // Start/stop the looping rain bed to match the current weather. Rain only
    // exists in the forest biome (biome 0), same as the rain VFX above.
    private void ApplyRainAmbience()
    {
        if (AudioManager.Instance == null) return;
        bool wet = currentBiome == 0 &&
                   (currentWeather == WeatherState.Precipitation || currentWeather == WeatherState.Storm);
        if (wet) AudioManager.Instance.PlaySFX(AudioID.Ambient_Rain);
        else AudioManager.Instance.StopLoopedBed(AudioID.Ambient_Rain);
    }

    public void ForceWeather(WeatherState state)
    {
        currentWeather = state;
        CheckAndUpdateSkybox();

        if (currentWeather == WeatherState.Storm)
        {
            if (lightningCoroutine == null) lightningCoroutine = StartCoroutine(LightningRoutine());
        }
        else { if (lightningCoroutine != null) { StopCoroutine(lightningCoroutine); lightningCoroutine = null; } }

        ApplyRainAmbience();
    }

    private IEnumerator LightningRoutine()
    {
        while (currentWeather == WeatherState.Storm)
        {
            // Shorter gap so a strike actually lands within a storm's lifetime
            // (was 5–15s, which usually outlived the storm → no visible bolts).
            yield return new WaitForSeconds(Random.Range(2f, 6f));

            // Decide strike position first so the local flash light can be
            // co-located with the VFX. Old code pumped the GLOBAL directional
            // light, which lit the whole world like a daylight blink. The new
            // version drops a point light at the strike so only the sky near
            // the bolt brightens, matching how storms look on screen in AAA
            // titles.
            Vector3 spawnPos;
            Quaternion spawnRot = Quaternion.identity;
            bool strikeGround = Random.value > 0.4f;
            float flashRange = strikeGround ? 35f : 60f;

            if (mainCam != null)
            {
                // Sample from an annulus instead of a disk so a strike can
                // never land near the player. Old `insideUnitCircle * 60`
                // had no inner radius. Use the player's transform when we
                // have it (camera can sit a few metres behind/above the
                // player) so the inner ring is measured from the actual
                // target, not the orbit camera.
                const float minDistance = 35f;
                Transform reference = mainCam.transform;
                var playerObj = GameObject.FindGameObjectWithTag("Player");
                if (playerObj != null) reference = playerObj.transform;

                Vector2 dir = Random.insideUnitCircle.normalized;
                if (dir.sqrMagnitude < 0.0001f) dir = Vector2.right;
                float dist = Random.Range(minDistance, lightningSpawnRadius);
                Vector2 offset = dir * dist;
                spawnPos = reference.position + new Vector3(offset.x, 0, offset.y);
                if (strikeGround && Terrain.activeTerrain != null)
                    spawnPos.y = Terrain.activeTerrain.SampleHeight(spawnPos) + Terrain.activeTerrain.transform.position.y;
                else
                {
                    spawnPos.y = reference.position.y + Random.Range(80f, 150f);
                    // Air bolts: keep nearly vertical, vary only on Y for
                    // rotational presentation. ±70° tilt made them lie
                    // sideways like billboards.
                    spawnRot = Quaternion.Euler(
                        Random.Range(-12f, 12f),
                        Random.Range(0f, 360f),
                        Random.Range(-12f, 12f));
                }
            }
            else spawnPos = transform.position;

            StartCoroutine(LocalLightningFlash(spawnPos, flashRange));
            // Just a hint of global atmosphere scatter, not a daylight pulse.
            // Keep this subtle so the lightning reads as "in the sky near the
            // bolt" rather than "the world briefly turned to noon."
            if (lightningLight != null)
            {
                StartCoroutine(GlobalAmbientPulse(0.5f, 1.1f, 0.08f));
            }

            if (lightningVFXPrefab != null)
            {
                GameObject lightning;
                if (ObjectPoolManager.Instance != null)
                {
                    lightning = ObjectPoolManager.Instance.SpawnFromPool(lightningVFXPrefab, spawnPos, spawnRot);
                    ScaleLightningStrike(lightning, strikeGround);
                    StartCoroutine(ReturnToPoolAfterDelay(lightning, 2f));
                }
                else
                {
                    lightning = Instantiate(lightningVFXPrefab, spawnPos, spawnRot);
                    ScaleLightningStrike(lightning, strikeGround);
                    Destroy(lightning, 2f);
                }
            }

            // Fire the thunder clap after a realistic gap so light + sound
            // separation reads "distant strike"; ~0.3 s for a close ground
            // bolt, longer for far air bolts. Env_Thunder was defined in
            // the FMOD bank but no code triggered it before.
            float thunderGap = strikeGround ? Random.Range(0.2f, 0.6f) : Random.Range(0.8f, 2.5f);
            StartCoroutine(ThunderClapDelayed(thunderGap));
        }
        lightningCoroutine = null;
    }

    private IEnumerator ThunderClapDelayed(float delay)
    {
        yield return new WaitForSeconds(delay);
        if (AudioManager.Instance != null) AudioManager.Instance.PlaySFX(AudioID.Env_Thunder);
    }

    // Scale + child-disable pass for every strike.
    //   * Air bolts get scaled 1.5×–3× for sky visibility (original behaviour).
    //   * Ground bolts stay at 0.5× scale so the "Zap Add Floor" decal
    //     doesn't paint a camp-sized scorch on the terrain.
    //   * The pooled prefab survives between strikes, so we ALWAYS reset
    //     the scale before applying the new multiplier — otherwise the
    //     scale stacked turn after turn (after a few storms a "ground"
    //     bolt rendered as a 50-metre cylinder).
    private void ScaleLightningStrike(GameObject lightning, bool strikeGround)
    {
        if (lightning == null) return;
        lightning.transform.localScale = Vector3.one; // reset before scaling
        if (strikeGround)
        {
            lightning.transform.localScale *= 0.5f;
            // Disable the ground-decal child entirely — even at 0.5× scale
            // it reads as a giant black splotch. Bolt itself is plenty of
            // ground-impact feedback.
            Transform decal = lightning.transform.Find("Zap Add Floor");
            if (decal != null) decal.gameObject.SetActive(false);
        }
        else
        {
            lightning.transform.localScale *= Random.Range(1.5f, 3.0f);
            // Re-enable the floor decal in case a previous ground strike
            // disabled it on the pooled instance.
            Transform decal = lightning.transform.Find("Zap Add Floor");
            if (decal != null) decal.gameObject.SetActive(true);
        }
    }

    // Pooled GameObject + Light combos so strikes don't allocate a fresh
    // GO every 5-15 seconds during storms.
    private readonly System.Collections.Generic.Stack<GameObject> lightningFlashPool = new System.Collections.Generic.Stack<GameObject>(4);

    private GameObject AcquireLightningFlash()
    {
        while (lightningFlashPool.Count > 0)
        {
            GameObject g = lightningFlashPool.Pop();
            if (g != null) return g;
        }
        GameObject go = new GameObject("LightningFlash");
        Light l = go.AddComponent<Light>();
        l.type = LightType.Point;
        l.shadows = LightShadows.None;
        l.renderMode = LightRenderMode.ForcePixel;
        l.color = new Color(0.85f, 0.92f, 1f);
        return go;
    }

    private void ReleaseLightningFlash(GameObject go)
    {
        if (go == null) return;
        Light l = go.GetComponent<Light>();
        if (l != null) l.intensity = 0f;
        go.SetActive(false);
        lightningFlashPool.Push(go);
    }

    // Spawn a transient point light at the bolt position so only the local
    // sky / nearby terrain brightens. Two quick pulses approximate the
    // characteristic double-flash of a real strike.
    private IEnumerator LocalLightningFlash(Vector3 position, float range)
    {
        GameObject go = AcquireLightningFlash();
        go.transform.position = position;
        go.SetActive(true);
        Light l = go.GetComponent<Light>();
        l.range = range;
        l.intensity = 0f;

        float peak1 = Random.Range(80f, 120f);
        l.intensity = peak1;
        yield return new WaitForSeconds(0.05f);
        l.intensity = 0f;
        yield return new WaitForSeconds(0.07f);
        l.intensity = peak1 * Random.Range(0.4f, 0.7f);
        yield return new WaitForSeconds(0.05f);
        ReleaseLightningFlash(go);
    }

    // Very small global intensity wobble — sells "the air just lit up" without
    // making the screen white. The old code used 3-6 intensity which is what
    // washed everything out.
    private IEnumerator GlobalAmbientPulse(float minPeak, float maxPeak, float holdTime)
    {
        if (lightningLight == null) yield break;
        float baseI = lightningLight.intensity;
        lightningLight.intensity = baseI + Random.Range(minPeak, maxPeak);
        yield return new WaitForSeconds(holdTime);
        lightningLight.intensity = baseI;
    }

    private IEnumerator ReturnToPoolAfterDelay(GameObject obj, float delay)
    {
        yield return new WaitForSeconds(delay);
        if (obj != null && ObjectPoolManager.Instance != null)
            ObjectPoolManager.Instance.ReturnToPool(obj);
    }
}