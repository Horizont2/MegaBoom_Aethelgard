using UnityEngine;
using System.Collections;
using UnityEngine.SceneManagement;

public enum WeatherState { Clear, Precipitation, Storm }

public class DayNightCycle : MonoBehaviour
{
    [Header("Time Settings")]
    public float dayDurationInSeconds = 300f;
    [Range(0f, 24f)] public float timeOfDay = 12f;

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
    [Tooltip("Префаб блискавки з паку магічних ефектів")]
    public GameObject lightningVFXPrefab;
    [Tooltip("Як близько до гравця може вдарити блискавка")]
    public float lightningSpawnRadius = 60f;

    private float weatherBlend = 0f;
    private float weatherTimer = 0f;
    private int currentBiome = 0;
    private Coroutine lightningCoroutine;

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
        {
            timeOfDay = PlayerPrefs.GetFloat("SavedTimeOfDay") * 24f;
        }

        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.Linear;
        RenderSettings.fogStartDistance = fogStartDistance;
        RenderSettings.fogEndDistance = fogEndDistance;
        RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;

        if (lightningLight != null) lightningLight.intensity = 0f;
        if (moonLight != null) moonLight.color = new Color(0.6f, 0.7f, 1f);

        UpdateWeatherVFX();
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

        Color targetSky = Color.Lerp(skyColorNight, skyColorDay, dayMultiplier);
        Color targetEquator = Color.Lerp(equatorColorNight, equatorColorDay, dayMultiplier);
        Color targetGround = Color.Lerp(groundColorNight, groundColorDay, dayMultiplier);

        RenderSettings.ambientSkyColor = Color.Lerp(targetSky, new Color(0.2f, 0.22f, 0.27f), weatherBlend);
        RenderSettings.ambientEquatorColor = Color.Lerp(targetEquator, new Color(0.15f, 0.18f, 0.22f), weatherBlend);
        RenderSettings.ambientGroundColor = Color.Lerp(targetGround, new Color(0.08f, 0.1f, 0.12f), weatherBlend);

        UpdateVFXPositions();
    }

    private void UpdateVFXPositions()
    {
        if (Camera.main == null) return;
        Vector3 camPos = Camera.main.transform.position;

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
                vfx.transform.position = camPos + Vector3.up * 12f;
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

        if (firefliesVFX != null)
        {
            bool showFireflies = isNight && currentBiome == 0 && currentWeather == WeatherState.Clear;
            firefliesVFX.SetActive(showFireflies);
        }
    }

    private void ChangeWeatherRandomly()
    {
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

        UpdateWeatherVFX();

        if (currentWeather == WeatherState.Storm && currentBiome == 0)
        {
            if (lightningCoroutine == null) lightningCoroutine = StartCoroutine(LightningRoutine());
        }
        else
        {
            if (lightningCoroutine != null) { StopCoroutine(lightningCoroutine); lightningCoroutine = null; }
        }
    }

    private void UpdateWeatherVFX()
    {
        if (rainVFX != null) rainVFX.gameObject.SetActive(false);
        if (snowVFX != null) snowVFX.gameObject.SetActive(false);
        if (dustVFX != null) dustVFX.gameObject.SetActive(false);

        if (currentWeather != WeatherState.Clear)
        {
            float emissionMultiplier = (currentWeather == WeatherState.Storm) ? 2f : 1f;

            if (currentBiome == 0 && rainVFX != null)
            {
                rainVFX.gameObject.SetActive(true);
                var em = rainVFX.emission; em.rateOverTimeMultiplier *= emissionMultiplier;
            }
            else if (currentBiome == 1 && dustVFX != null)
            {
                dustVFX.gameObject.SetActive(true);
                var em = dustVFX.emission; em.rateOverTimeMultiplier *= emissionMultiplier;
            }
            else if (currentBiome == 2 && snowVFX != null)
            {
                snowVFX.gameObject.SetActive(true);
                var em = snowVFX.emission; em.rateOverTimeMultiplier *= emissionMultiplier;
            }
        }
    }

    // --- ОНОВЛЕНИЙ ААА МЕТОД ГРОЗИ ---
    private IEnumerator LightningRoutine()
    {
        while (currentWeather == WeatherState.Storm)
        {
            // Випадкова пауза між ударами
            yield return new WaitForSeconds(Random.Range(5f, 15f));

            // 1. Блимаємо небом (глобальне світло)
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

            // 2. Спавнимо фізичну блискавку
            if (lightningVFXPrefab != null && Camera.main != null)
            {
                // Шукаємо випадкову точку навколо камери
                Vector2 randomCircle = Random.insideUnitCircle * lightningSpawnRadius;
                Vector3 spawnPos = Camera.main.transform.position + new Vector3(randomCircle.x, 0, randomCircle.y);

                // --- ААА РАНДОМІЗАТОР БЛИСКАВКИ ---
                bool strikeGround = Random.value > 0.4f; // 60% шанс вдарити в землю
                Quaternion spawnRot = Quaternion.identity;

                if (strikeGround)
                {
                    // Удар точно в рельєф
                    if (Terrain.activeTerrain != null)
                    {
                        spawnPos.y = Terrain.activeTerrain.SampleHeight(spawnPos) + Terrain.activeTerrain.transform.position.y;
                    }
                }
                else
                {
                    // Блискавка між хмарами (високо в небі)
                    spawnPos.y = Camera.main.transform.position.y + Random.Range(80f, 150f);

                    // Сильно нахиляємо її по осях X та Z, щоб вона йшла горизонтально/під кутом через небо
                    spawnRot = Quaternion.Euler(Random.Range(-70f, 70f), Random.Range(0f, 360f), Random.Range(-70f, 70f));
                }

                GameObject lightning = Instantiate(lightningVFXPrefab, spawnPos, spawnRot);

                // Якщо блискавка в небі, робимо її масивнішою, щоб вона розтягнулася на пів екрану
                if (!strikeGround)
                {
                    lightning.transform.localScale *= Random.Range(1.5f, 3.0f);
                }

                Destroy(lightning, 2f);

                // 3. Відкладений звук грому
                if (AudioManager.Instance != null)
                {
                    // Чим далі блискавка (навіть якщо вона високо в небі), тим довше йде звук
                    float dist = Vector3.Distance(Camera.main.transform.position, spawnPos);
                    StartCoroutine(PlayThunderSoundDelayed(dist / 30f));
                }
            }
        }
        lightningCoroutine = null;
    }

    private IEnumerator PlayThunderSoundDelayed(float delay)
    {
        yield return new WaitForSeconds(delay);
        // Тут треба викликати звук грому (додай свій AudioID, якщо є)
        // AudioManager.Instance.PlaySFX(AudioID.Thunder); 
    }

    private void OnDestroy()
    {
        string currentScene = SceneManager.GetActiveScene().name;

        if (currentScene != "Lvl_1")
        {
            PlayerPrefs.SetFloat("SavedTimeOfDay", timeOfDay / 24f);
            PlayerPrefs.Save();
        }
    }
}