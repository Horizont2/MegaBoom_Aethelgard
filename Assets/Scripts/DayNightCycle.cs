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

    private void Start()
    {
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

        CheckAndUpdateSkybox();
    }

    private void Update()
    {
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
        weatherBlend = Mathf.Lerp(weatherBlend, targetBlend, Time.deltaTime * weatherTransitionSpeed);

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

        // ФІКС: І в Storm, і в Precipitation небо залишається похмурим!
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

        if (currentMat != null)
        {
            Material tempOutMat = new Material(currentMat);
            RenderSettings.skybox = tempOutMat;
            Color startTint = tempOutMat.HasProperty("_Tint") ? tempOutMat.GetColor("_Tint") : Color.gray;

            float t = 0;
            while (t < 1f)
            {
                t += Time.deltaTime * skyboxFadeSpeed;
                if (tempOutMat.HasProperty("_Tint")) tempOutMat.SetColor("_Tint", Color.Lerp(startTint, currentFogColor, t));
                yield return null;
            }
        }

        Material tempInMat = new Material(newSkyboxMat);
        RenderSettings.skybox = tempInMat;
        Color originalNewTint = newSkyboxMat.HasProperty("_Tint") ? newSkyboxMat.GetColor("_Tint") : Color.gray;
        if (tempInMat.HasProperty("_Tint")) tempInMat.SetColor("_Tint", currentFogColor);

        float fadeInTimer = 0;
        while (fadeInTimer < 1f)
        {
            fadeInTimer += Time.deltaTime * skyboxFadeSpeed;
            if (tempInMat.HasProperty("_Tint")) tempInMat.SetColor("_Tint", Color.Lerp(currentFogColor, originalNewTint, fadeInTimer));
            yield return null;
        }
        RenderSettings.skybox = newSkyboxMat;
    }

    private void UpdateVFXPositions()
    {
        if (Camera.main == null) return;

        Vector3 camPos = Camera.main.transform.position;

        // Беремо напрямок камери, але ігноруємо нахил вгору/вниз
        Vector3 camForward = Camera.main.transform.forward;
        camForward.y = 0;
        camForward.Normalize();

        if (starsParticles != null)
        {
            starsParticles.transform.position = camPos;
            starsParticles.transform.rotation = Quaternion.identity;
        }

        ParticleSystem[] weatherVFX = { rainVFX, snowVFX, dustVFX };
        foreach (var vfx in weatherVFX)
        {
            if (vfx != null && vfx.gameObject.activeSelf)
            {
                // ФІКС ОПТИМІЗАЦІЇ: Зсуваємо погоду на 15м вгору і на 12м ВПЕРЕД від камери!
                vfx.transform.position = camPos + (Vector3.up * 15f) + (camForward * 12f);

                // Фіксуємо обертання, щоб дощ падав рівно вниз
                vfx.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
            }
        }
    }

    private void ManageNightVFX(float timePercent)
    {
        bool isNight = timeOfDay < 5f || timeOfDay > 19f;
        if (starsParticles != null && Camera.main != null)
        {
            var main = starsParticles.main;
            float starAlpha = isNight ? (1f - weatherBlend) : 0f;
            main.startColor = new Color(1f, 1f, 1f, starAlpha);
        }
        if (firefliesVFX != null) firefliesVFX.SetActive(isNight && currentBiome == 0 && currentWeather == WeatherState.Clear);
    }

    private void ChangeWeatherRandomly()
    {
        if (isWeatherLocked) return;

        float roll = Random.value;
        if (currentWeather == WeatherState.Clear)
        {
            if (roll < 0.4f) currentWeather = WeatherState.Precipitation;
            else if (roll < 0.6f) currentWeather = WeatherState.Storm;
            else currentWeather = WeatherState.Clear;
        }
        else
        {
            if (roll < 0.7f) currentWeather = WeatherState.Clear;
            else currentWeather = WeatherState.Precipitation;
        }

        if (currentWeather == WeatherState.Storm && currentBiome == 0)
        {
            if (lightningCoroutine == null) lightningCoroutine = StartCoroutine(LightningRoutine());
        }
        else { if (lightningCoroutine != null) { StopCoroutine(lightningCoroutine); lightningCoroutine = null; } }
    }

    public void ForceWeather(WeatherState state)
    {
        currentWeather = state;
        CheckAndUpdateSkybox();

        if (currentWeather == WeatherState.Storm && currentBiome == 0)
        {
            if (lightningCoroutine == null) lightningCoroutine = StartCoroutine(LightningRoutine());
        }
        else { if (lightningCoroutine != null) { StopCoroutine(lightningCoroutine); lightningCoroutine = null; } }
    }

    private IEnumerator LightningRoutine()
    {
        while (currentWeather == WeatherState.Storm)
        {
            yield return new WaitForSeconds(Random.Range(5f, 15f));
            if (lightningLight != null)
            {
                lightningLight.intensity = Random.Range(3f, 6f);
                yield return new WaitForSeconds(0.05f);
                lightningLight.intensity = 0f;
                yield return new WaitForSeconds(0.1f);
                lightningLight.intensity = Random.Range(1f, 3f);
                yield return new WaitForSeconds(0.05f);
                lightningLight.intensity = 0f;
            }

            if (lightningVFXPrefab != null && Camera.main != null)
            {
                Vector2 randomCircle = Random.insideUnitCircle * lightningSpawnRadius;
                Vector3 spawnPos = Camera.main.transform.position + new Vector3(randomCircle.x, 0, randomCircle.y);
                bool strikeGround = Random.value > 0.4f;
                Quaternion spawnRot = Quaternion.identity;

                if (strikeGround && Terrain.activeTerrain != null)
                    spawnPos.y = Terrain.activeTerrain.SampleHeight(spawnPos) + Terrain.activeTerrain.transform.position.y;
                else
                {
                    spawnPos.y = Camera.main.transform.position.y + Random.Range(80f, 150f);
                    spawnRot = Quaternion.Euler(Random.Range(-70f, 70f), Random.Range(0f, 360f), Random.Range(-70f, 70f));
                }

                GameObject lightning = Instantiate(lightningVFXPrefab, spawnPos, spawnRot);
                if (!strikeGround) lightning.transform.localScale *= Random.Range(1.5f, 3.0f);
                Destroy(lightning, 2f);
            }
        }
        lightningCoroutine = null;
    }
}