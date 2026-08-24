using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

[DefaultExecutionOrder(-500)]
public class SettingsApplier : MonoBehaviour
{
    public static SettingsApplier Instance { get; private set; }

    [Tooltip("URP asset whose render scale gets driven by Settings_RenderScale. Auto-resolved if left null.")]
    public UniversalRenderPipelineAsset urpAsset;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Bootstrap()
    {
        if (Instance != null) return;

        // ФІКС: Конвертуємо старі зламані збереження (1%) у нормальні (100%)
        SanitizeLegacyVolumes();

        // First-launch hardware detection — picks a graphics tier and
        // writes the Settings_* prefs BEFORE we apply them, so a fresh
        // install opens at a sane quality level for the player's PC
        // instead of whatever the project shipped as the default.
        GraphicsAutoConfig.EnsureFirstRun();

        GameObject go = new GameObject("[SettingsApplier]");
        DontDestroyOnLoad(go);
        Instance = go.AddComponent<SettingsApplier>();
        Instance.ApplyAll();

        // ФІКС: Чекаємо завантаження AudioManager, щоб звук точно застосувався при старті
        Instance.StartCoroutine(Instance.WaitAndApplyVolumes());
    }

    private static void SanitizeLegacyVolumes()
    {
        string[] volKeys = { "Settings_MasterVol", "Settings_MusicVol", "Settings_SFXVol", "Settings_VoiceVol", "Settings_UIVol", "Settings_AmbientVol" };
        foreach (var key in volKeys)
        {
            if (PlayerPrefs.HasKey(key))
            {
                float val = PlayerPrefs.GetFloat(key);
                // Якщо збережене значення <= 1 (це залишилося від старого багу 0-1)
                if (val > 0f && val <= 1.0f)
                {
                    PlayerPrefs.SetFloat(key, val * 100f); // Перетворюємо на шкалу 0-100
                }
            }
        }
        PlayerPrefs.Save();
    }

    private System.Collections.IEnumerator WaitAndApplyVolumes()
    {
        while (AudioManager.Instance == null) yield return null;
        ApplyVolumes();
    }

    public void ApplyAll()
    {
        ApplyResolution();
        ApplyMonitor();
        ApplyFpsCap();
        ApplyVSync();
        ApplyQualityLevel();
        ApplyAntiAliasing();
        ApplyTextureQuality();
        ApplyShadowQuality();
        ApplyShadowDistance();
        ApplyRenderScale();
        ApplyMotionBlur();
        ApplyDepthOfField();
        ApplyBloom();
        ApplyAmbientOcclusion();
        ApplyVolumetrics();
        ApplyFOV();
        ApplyBrightness();
        ApplyGamma();
        ApplyUIScale();
        // Гучність тепер викликається з корутини
    }

    // ============================================================
    // VIDEO
    // ============================================================

    public static void ApplyResolution()
    {
        int widthIndex = PlayerPrefs.GetInt("Settings_ResolutionIndex", -1);
        int mode = PlayerPrefs.GetInt("Settings_WindowMode", 0);
        int rrIdx = PlayerPrefs.GetInt("Settings_RefreshRateIndex", -1);

        Resolution[] all = Screen.resolutions;
        int width = Screen.width;
        int height = Screen.height;
        if (widthIndex >= 0)
        {
            var list = new System.Collections.Generic.List<(int, int)>();
            var seen = new System.Collections.Generic.HashSet<(int, int)>();
            foreach (var r in all) if (seen.Add((r.width, r.height))) list.Add((r.width, r.height));
            if (widthIndex < list.Count) { width = list[widthIndex].Item1; height = list[widthIndex].Item2; }
        }

        int refresh = 60;
        var rates = new System.Collections.Generic.HashSet<int>();
        foreach (var r in all) rates.Add((int)System.Math.Round(r.refreshRateRatio.value));
        var rateList = new System.Collections.Generic.List<int>(rates);
        rateList.Sort();
        if (rrIdx >= 0 && rrIdx < rateList.Count) refresh = rateList[rrIdx];

        FullScreenMode fsMode = mode switch
        {
            0 => FullScreenMode.ExclusiveFullScreen,
            1 => FullScreenMode.FullScreenWindow,
            _ => FullScreenMode.Windowed,
        };
#if UNITY_2022_2_OR_NEWER
        Screen.SetResolution(width, height, fsMode, new RefreshRate { numerator = (uint)refresh, denominator = 1 });
#else
        Screen.SetResolution(width, height, fsMode, refresh);
#endif
    }

    public static void ApplyMonitor()
    {
        int idx = PlayerPrefs.GetInt("Settings_Monitor", 0);
        if (idx > 0 && idx < Display.displays.Length && !Display.displays[idx].active)
            Display.displays[idx].Activate();
    }

    public static void ApplyFpsCap()
    {
        // Default OFF (uncapped), like most AAA PC games — the player opts INTO a
        // cap. Auto-capping to 60 on first launch felt like the game was throttled.
        bool limited = PlayerPrefs.GetInt("Settings_FPSLimit", 0) == 1;
        if (!limited)
        {
            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = -1;
            return;
        }
        int idx = PlayerPrefs.GetInt("Settings_FpsCapIndex", 2);
        int[] caps = { -1, 30, 60, 90, 120, 144, 240 };
        if (idx < 0 || idx >= caps.Length) idx = 2;
        QualitySettings.vSyncCount = 0;
        Application.targetFrameRate = caps[idx];
    }

    public static void ApplyVSync()
    {
        QualitySettings.vSyncCount = PlayerPrefs.GetInt("Settings_VSync", 0);
    }

    // ============================================================
    // GRAPHICS
    // ============================================================

    public static void ApplyQualityLevel()
    {
        int q = PlayerPrefs.GetInt("Settings_QualityLevel", QualitySettings.GetQualityLevel());
        QualitySettings.SetQualityLevel(Mathf.Clamp(q, 0, QualitySettings.names.Length - 1), true);
    }

    public static void ApplyAntiAliasing()
    {
        int aa = PlayerPrefs.GetInt("Settings_AntiAliasing", 1);
        QualitySettings.antiAliasing = aa switch { 0 => 0, 1 => 2, 2 => 4, _ => 8 };
    }

    public static void ApplyTextureQuality()
    {
        // Default 3 = full-resolution textures (mipmap limit 0). AAA titles
        // ship full-res textures on by default; the auto-config tier lowers
        // this on weaker hardware.
        int q = PlayerPrefs.GetInt("Settings_TextureQuality", 3);
        QualitySettings.globalTextureMipmapLimit = Mathf.Clamp(3 - q, 0, 3);
    }

    public static void ApplyShadowQuality()
    {
        int q = PlayerPrefs.GetInt("Settings_ShadowQuality", 2);
        QualitySettings.shadows = q switch
        {
            0 => UnityEngine.ShadowQuality.Disable,
            1 => UnityEngine.ShadowQuality.HardOnly,
            _ => UnityEngine.ShadowQuality.All,
        };
        QualitySettings.shadowResolution = q switch
        {
            0 => UnityEngine.ShadowResolution.Low,
            1 => UnityEngine.ShadowResolution.Low,
            2 => UnityEngine.ShadowResolution.Medium,
            _ => UnityEngine.ShadowResolution.High,
        };
    }

    public static void ApplyShadowDistance()
    {
        // 80m default — 50 clipped shadows visibly close on open terrain.
        // Auto-config overrides per tier (30 low → 130 ultra).
        QualitySettings.shadowDistance = PlayerPrefs.GetFloat("Settings_ShadowDistance", 80f);
    }

    public static void ApplyRenderScale()
    {
        float rs = Mathf.Clamp(PlayerPrefs.GetFloat("Settings_RenderScale", 1f), 0.5f, 2f);
        var asset = UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
        if (asset != null) asset.renderScale = rs;
    }

    public static void ApplyMotionBlur() => Shader.SetGlobalFloat("_Settings_MotionBlur", PlayerPrefs.GetInt("Settings_MotionBlur", 0));
    public static void ApplyDepthOfField() => Shader.SetGlobalFloat("_Settings_DOF", PlayerPrefs.GetInt("Settings_DepthOfField", 0));
    public static void ApplyBloom() => Shader.SetGlobalFloat("_Settings_Bloom", PlayerPrefs.GetInt("Settings_Bloom", 1));
    public static void ApplyAmbientOcclusion() => Shader.SetGlobalFloat("_Settings_AO", PlayerPrefs.GetInt("Settings_AO", 1));
    public static void ApplyVolumetrics() => Shader.SetGlobalFloat("_Settings_Volumetrics", PlayerPrefs.GetInt("Settings_Volumetrics", 0));

    // ============================================================
    // CAMERA
    // ============================================================

    public static void ApplyFOV()
    {
        if (SceneManager.GetActiveScene().name == "Menu") return;
        float fov = PlayerPrefs.GetFloat("Settings_FOV", 75f);
        Camera cam = Camera.main;
        if (cam != null) cam.fieldOfView = fov;
    }

    public static void ApplyBrightness()
    {
        float b = PlayerPrefs.GetFloat("Settings_Brightness", 1f);
        Shader.SetGlobalFloat("_Settings_Brightness", b);
        RenderSettings.ambientIntensity = b;
    }

    public static void ApplyGamma()
    {
        Shader.SetGlobalFloat("_Settings_Gamma", PlayerPrefs.GetFloat("Settings_Gamma", 1f));
    }

    // ============================================================
    // UI
    // ============================================================

    public static void ApplyUIScale()
    {
        float scale = PlayerPrefs.GetFloat("Settings_UIScale", 1f);
        UnityEngine.UI.CanvasScaler[] all = Object.FindObjectsByType<UnityEngine.UI.CanvasScaler>(FindObjectsSortMode.None);
        foreach (var s in all) s.scaleFactor = scale;
    }

    // ============================================================
    // AUDIO
    // ============================================================

    public static void ApplyVolumes()
    {
        if (AudioManager.Instance == null) return;
        // Defaults leave headroom — full-blast 100/100 everywhere had no
        // mix headroom and made UI clicks / dodges clip against the score.
        AudioManager.Instance.SetMasterVolume(PlayerPrefs.GetFloat("Settings_MasterVol", 80f) / 100f);
        AudioManager.Instance.SetMusicVolume(PlayerPrefs.GetFloat("Settings_MusicVol", 65f) / 100f);
        AudioManager.Instance.SetSFXVolume(PlayerPrefs.GetFloat("Settings_SFXVol", 90f) / 100f);
        AudioManager.Instance.SetUIVolume(PlayerPrefs.GetFloat("Settings_UIVol", 70f) / 100f);
        AudioManager.Instance.SetVoiceVolume(PlayerPrefs.GetFloat("Settings_VoiceVol", 100f) / 100f);
        AudioManager.Instance.SetAmbientVolume(PlayerPrefs.GetFloat("Settings_AmbientVol", 75f) / 100f);
    }

    private void OnApplicationFocus(bool hasFocus)
    {
        if (PlayerPrefs.GetInt("Settings_MuteWhenUnfocused", 1) != 1) return;

        // Smooth ramp — hard-snapping master to 0 on Alt-Tab clicked
        // the speakers audibly. The AudioManager fade multiplies on
        // top of the user's slider so we don't corrupt their setting.
        if (AudioManager.Instance != null)
        {
            AudioManager.Instance.FadeMasterVolume(hasFocus ? 1f : 0f, 0.35f);
        }
        else
        {
            AudioListener.volume = hasFocus ? (PlayerPrefs.GetFloat("Settings_MasterVol", 80f) / 100f) : 0f;
        }
    }
}