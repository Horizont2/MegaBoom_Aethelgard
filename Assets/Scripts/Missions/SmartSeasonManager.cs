using UnityEngine;
using System.Collections;
using System.Collections.Generic;

public enum Season { Summer, EarlyAutumn, Autumn, LateAutumn, Winter, Spring }

public class SmartSeasonManager : MonoBehaviour
{
    [Header("Time & Save System")]
    public float minutesPerSeason = 20f;
    private float totalSecondsPerSeason;
    public float currentSeasonTimer = 0f;

    [Header("Current State")]
    public Season currentSeason = Season.Summer;
    public Material globalMaterial;
    public bool isRaining = false;

    [Header("Day & Night Cycle (NEW)")]
    public bool enableDayNight = true;
    public float nightSpeedMultiplier = 2.5f;
    public float dayDurationMinutes = 15f;
    [Range(0f, 1f)] public float timeOfDay = 0.4f;
    public Color nightAmbientColor = new Color(0.05f, 0.05f, 0.15f);
    public Color nightFogColor = new Color(0.02f, 0.02f, 0.08f);
    private float defaultSunIntensity;

    private Color currentSeasonSunColor;
    private Color currentSeasonFogColor;

    [Header("Realism Settings")]
    public float propsDelay = 15f;

    [Header("Generated Textures")]
    public Texture2D summerTexture;
    public Texture2D earlyAutumnTexture;
    public Texture2D autumnTexture;
    public Texture2D lateAutumnTexture;
    public Texture2D winterTexture;
    public Texture2D springTexture;

    [Header("Foliage Materials (AAA Swapping)")]
    [Tooltip("Перетягни сюди папки (контейнери) з усіма деревами та кущами табору")]
    public Transform[] natureRoots;
    public Material foliageSummerMat;
    public Material foliageAutumnMat;
    public Material foliageWinterMat;

    [Header("Lighting - Sun (Base Colors)")]
    public Light directionalLight;
    public Color sunSummer = new Color(1f, 0.95f, 0.8f);
    public Color sunEarlyAutumn = new Color(1f, 0.9f, 0.7f);
    public Color sunAutumn = new Color(1f, 0.7f, 0.4f);
    public Color sunLateAutumn = new Color(0.8f, 0.75f, 0.7f);
    public Color sunWinter = new Color(0.7f, 0.8f, 1f);
    public Color sunSpring = new Color(0.9f, 0.95f, 0.9f);

    [Header("Lighting - Fog (Base Colors)")]
    public Color fogSummer = new Color(0.4f, 0.5f, 0.4f);
    public Color fogEarlyAutumn = new Color(0.5f, 0.5f, 0.35f);
    public Color fogAutumn = new Color(0.6f, 0.4f, 0.3f);
    public Color fogLateAutumn = new Color(0.45f, 0.4f, 0.4f);
    public Color fogWinter = new Color(0.6f, 0.7f, 0.8f);
    public Color fogSpring = new Color(0.5f, 0.6f, 0.5f);
    public Color fogRain = new Color(0.3f, 0.35f, 0.4f);

    [Header("VFX & Particles")]
    public GameObject snowParticles;
    public GameObject leavesParticles;
    public GameObject firefliesParticles;
    public GameObject dustParticles;
    public GameObject rainParticles;
    public GameObject playerFootprints;

    [Header("Props (Physical Objects)")]
    public GameObject winterProps;
    public GameObject autumnProps;

    private Coroutine activePropsCoroutine;
    private bool isMissionMode = false;

    [Header("AAA Transitions")]
    public float transitionDuration = 12f;
    private Coroutine transitionCoroutine;

    // ОПТИМІЗАЦІЯ: Кешовані масиви для уникнення Garbage Collection
    private ParticleSystem[] cachedWeatherVFX;
    private List<Renderer> cachedFoliageRenderers = new List<Renderer>();
    private Camera mainCam;

    private void Start()
    {
        mainCam = Camera.main;
        totalSecondsPerSeason = minutesPerSeason * 60f;
        if (directionalLight != null) defaultSunIntensity = directionalLight.intensity;

        // Кешуємо VFX один раз
        cachedWeatherVFX = new ParticleSystem[] {
            rainParticles != null ? rainParticles.GetComponent<ParticleSystem>() : null,
            snowParticles != null ? snowParticles.GetComponent<ParticleSystem>() : null,
            dustParticles != null ? dustParticles.GetComponent<ParticleSystem>() : null
        };

        LoadProgress();
        CacheFoliageRenderers();
        InvokeRepeating("UpdateDynamicWeather", 10f, 180f);
        ApplySeason(currentSeason);
    }

    private void CacheFoliageRenderers()
    {
        if (natureRoots == null || natureRoots.Length == 0) return;

        foreach (Transform root in natureRoots)
        {
            if (root == null) continue;
            Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
            foreach (Renderer rend in renderers)
            {
                if (rend is ParticleSystemRenderer) continue;
                if (IsVFX(rend.gameObject.name)) continue;
                cachedFoliageRenderers.Add(rend);
            }
        }
    }

    private bool IsWoodOrTrunk(string name)
    {
        return name.IndexOf("trunk", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
               name.IndexOf("wood", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
               name.IndexOf("bark", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
               name.IndexOf("branch", System.StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private bool IsVFX(string name)
    {
        return name.IndexOf("vfx", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
               name.IndexOf("smoke", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
               name.IndexOf("effect", System.StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private void UpdateFoliageMaterials(Season season)
    {
        Material targetMat = foliageSummerMat;
        if (season == Season.Autumn || season == Season.EarlyAutumn || season == Season.LateAutumn) targetMat = foliageAutumnMat;
        else if (season == Season.Winter) targetMat = foliageWinterMat;
        else targetMat = foliageSummerMat;

        if (targetMat == null || cachedFoliageRenderers.Count == 0) return;

        foreach (Renderer rend in cachedFoliageRenderers)
        {
            if (rend == null) continue;
            Material[] mats = rend.sharedMaterials;
            bool changed = false;

            for (int i = 0; i < mats.Length; i++)
            {
                if (mats[i] == null) continue;
                if (IsWoodOrTrunk(rend.gameObject.name) || IsWoodOrTrunk(mats[i].name)) continue;

                mats[i] = targetMat;
                changed = true;
            }
            if (changed) rend.sharedMaterials = mats;
        }
    }

    private void Update()
    {
        if (isMissionMode) return;

        currentSeasonTimer += Time.deltaTime;
        if (currentSeasonTimer >= totalSecondsPerSeason)
        {
            AdvanceToNextSeason();
        }

        if (enableDayNight)
        {
            float speed = 1f;
            if (timeOfDay < 0.2f || timeOfDay > 0.8f) speed = nightSpeedMultiplier;

            timeOfDay += (Time.deltaTime * speed) / (dayDurationMinutes * 60f);
            if (timeOfDay >= 1f) timeOfDay -= 1f;
            UpdateDayNightVisuals();
        }
    }

    private void UpdateDayNightVisuals()
    {
        if (directionalLight == null) return;

        float sunAngle = (timeOfDay * 360f) - 90f;
        directionalLight.transform.localRotation = Quaternion.Euler(sunAngle, 170f, 0f);

        float intensityMultiplier = Mathf.Clamp01(Mathf.Sin(timeOfDay * Mathf.PI));
        directionalLight.intensity = defaultSunIntensity * intensityMultiplier;

        directionalLight.color = Color.Lerp(nightAmbientColor, currentSeasonSunColor, intensityMultiplier);
        RenderSettings.fogColor = Color.Lerp(nightFogColor, currentSeasonFogColor, intensityMultiplier);

        if (currentSeason == Season.Summer && !isRaining && firefliesParticles)
        {
            firefliesParticles.SetActive(intensityMultiplier < 0.2f);
        }
    }

    private void AdvanceToNextSeason()
    {
        currentSeasonTimer = 0f;
        currentSeason = (Season)(((int)currentSeason + 1) % 6);
        UpdateDynamicWeather();
        ApplySeason(currentSeason);
        SaveProgress();
    }

    private void ForceSeason(Season newSeason)
    {
        currentSeason = newSeason;
        currentSeasonTimer = 0f;
        ApplySeason(currentSeason);
        if (!isMissionMode) SaveProgress();
    }

    private void UpdateDynamicWeather()
    {
        if (isMissionMode) return;
        isRaining = false;
        if (currentSeason == Season.Autumn || currentSeason == Season.LateAutumn || currentSeason == Season.Spring)
        {
            if (Random.Range(0, 100) < 40) isRaining = true;
        }
        ApplySeason(currentSeason);
    }

    public void ApplySeason(Season targetSeason)
    {
        if (activePropsCoroutine != null) StopCoroutine(activePropsCoroutine);

        SetParticlesActive(snowParticles, targetSeason == Season.Winter);
        SetParticlesActive(leavesParticles, targetSeason == Season.Autumn || targetSeason == Season.LateAutumn);
        SetParticlesActive(firefliesParticles, targetSeason == Season.Summer && !isRaining);
        SetParticlesActive(dustParticles, targetSeason == Season.EarlyAutumn && !isRaining);
        SetParticlesActive(rainParticles, isRaining);

        if (winterProps) winterProps.SetActive(false);
        if (autumnProps) autumnProps.SetActive(false);
        if (playerFootprints) playerFootprints.SetActive(targetSeason == Season.Winter);

        // ЗМІНА МАТЕРІАЛІВ ДЕРЕВ
        UpdateFoliageMaterials(targetSeason);

        Color targetSun = Color.white;
        Color targetFog = Color.white;
        Texture2D targetTex = null;

        switch (targetSeason)
        {
            case Season.Summer: targetSun = sunSummer; targetFog = fogSummer; targetTex = summerTexture; break;
            case Season.EarlyAutumn: targetSun = sunEarlyAutumn; targetFog = fogEarlyAutumn; targetTex = earlyAutumnTexture; break;
            case Season.Autumn:
                targetSun = sunAutumn; targetFog = fogAutumn; targetTex = autumnTexture;
                if (autumnProps) activePropsCoroutine = StartCoroutine(ShowPropsDelayed(autumnProps, propsDelay)); break;
            case Season.LateAutumn: targetSun = sunLateAutumn; targetFog = fogLateAutumn; targetTex = lateAutumnTexture; break;
            case Season.Winter:
                targetSun = sunWinter; targetFog = fogWinter; targetTex = winterTexture;
                if (winterProps) activePropsCoroutine = StartCoroutine(ShowPropsDelayed(winterProps, propsDelay)); break;
            case Season.Spring: targetSun = sunSpring; targetFog = fogSpring; targetTex = springTexture; break;
        }

        if (isRaining)
        {
            targetFog = fogRain;
            targetSun = Color.Lerp(targetSun, new Color(0.3f, 0.35f, 0.45f), 0.75f);
        }

        if (transitionCoroutine != null) StopCoroutine(transitionCoroutine);
        transitionCoroutine = StartCoroutine(EnvironmentTransitionRoutine(targetSun, targetFog, targetTex));
    }

    private void SetParticlesActive(GameObject vfx, bool active)
    {
        if (vfx == null) return;
        ParticleSystem ps = vfx.GetComponent<ParticleSystem>();

        if (ps != null)
        {
            if (active)
            {
                vfx.SetActive(true);
                if (!ps.isPlaying) ps.Play();
            }
            else
            {
                ps.Stop(true, ParticleSystemStopBehavior.StopEmitting);
            }
        }
        else vfx.SetActive(active);
    }

    private IEnumerator EnvironmentTransitionRoutine(Color targetSun, Color targetFog, Texture2D tex)
    {
        Color startSun = currentSeasonSunColor;
        Color startFog = currentSeasonFogColor;

        if (globalMaterial != null && tex != null) globalMaterial.SetTexture("_BaseMap", tex);

        float elapsed = 0f;
        float actualDuration = (startSun == new Color(0, 0, 0, 0)) ? 0.1f : transitionDuration;

        while (elapsed < actualDuration)
        {
            elapsed += Time.deltaTime;
            float t = elapsed / actualDuration;

            t = t * t * (3f - 2f * t);

            currentSeasonSunColor = Color.Lerp(startSun, targetSun, t);
            currentSeasonFogColor = Color.Lerp(startFog, targetFog, t);

            if (!enableDayNight || isMissionMode)
            {
                if (directionalLight) directionalLight.color = currentSeasonSunColor;
                RenderSettings.fogColor = currentSeasonFogColor;
            }

            yield return null;
        }

        currentSeasonSunColor = targetSun;
        currentSeasonFogColor = targetFog;

        if (!enableDayNight || isMissionMode)
        {
            if (directionalLight) directionalLight.color = currentSeasonSunColor;
            RenderSettings.fogColor = currentSeasonFogColor;
        }
    }

    private IEnumerator ShowPropsDelayed(GameObject propsObject, float delayTime)
    {
        yield return new WaitForSeconds(delayTime);
        if (propsObject != null) propsObject.SetActive(true);
    }

    private void SaveProgress()
    {
        PlayerPrefs.SetInt("SavedSeason", (int)currentSeason);
        PlayerPrefs.SetFloat("SavedTimer", currentSeasonTimer);
        PlayerPrefs.SetFloat("SavedTimeOfDay", timeOfDay);
        PlayerPrefs.Save();
    }

    private void LoadProgress()
    {
        if (PlayerPrefs.HasKey("SavedSeason"))
        {
            currentSeason = (Season)PlayerPrefs.GetInt("SavedSeason");
            currentSeasonTimer = PlayerPrefs.GetFloat("SavedTimer");
            timeOfDay = PlayerPrefs.GetFloat("SavedTimeOfDay", 0.4f);
        }
    }

    private void OnApplicationQuit()
    {
        SaveProgress();
        if (globalMaterial != null && summerTexture != null) globalMaterial.SetTexture("_BaseMap", summerTexture);
    }

    private void OnDestroy()
    {
        SaveProgress();
        if (globalMaterial != null && summerTexture != null) globalMaterial.SetTexture("_BaseMap", summerTexture);
    }

    public void LockSeasonForMission(int biomeIndex)
    {
        isMissionMode = true;
        enableDayNight = false;
        isRaining = false;

        if (biomeIndex == 0) ForceSeason(Season.Summer);
        else if (biomeIndex == 1) ForceSeason(Season.EarlyAutumn);
        else if (biomeIndex == 2) ForceSeason(Season.Winter);

        if (directionalLight != null)
        {
            directionalLight.intensity = defaultSunIntensity;
            directionalLight.transform.localRotation = Quaternion.Euler(50f, 170f, 0f);
        }
    }
}