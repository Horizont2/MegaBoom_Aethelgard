using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

// Central applier: reads every "Settings_*" PlayerPref and pushes its
// value into the engine system that actually controls it. The runtime
// settings panel updates the PlayerPref immediately; this class applies
// the side-effect (screen resolution, render-scale, volume, etc.) and
// runs on game boot so prefs survive launches.
//
// All apply methods are null-safe — they no-op when the relevant
// engine handle isn't ready yet.
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
        GameObject go = new GameObject("[SettingsApplier]");
        DontDestroyOnLoad(go);
        Instance = go.AddComponent<SettingsApplier>();
        Instance.ApplyAll();
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
        ApplyVolumes();
    }

    // ============================================================
    // VIDEO
    // ============================================================

    public static void ApplyResolution()
    {
        int widthIndex = PlayerPrefs.GetInt("Settings_ResolutionIndex", -1);
        int mode = PlayerPrefs.GetInt("Settings_WindowMode", 0);
        int rrIdx = PlayerPrefs.GetInt("Settings_RefreshRateIndex", -1);

        // Resolve resolution
        Resolution[] all = Screen.resolutions;
        int width = Screen.width;
        int height = Screen.height;
        if (widthIndex >= 0)
        {
            // Builder dedupes by (w,h) and lists unique pairs in order.
            var list = new System.Collections.Generic.List<(int, int)>();
            var seen = new System.Collections.Generic.HashSet<(int, int)>();
            foreach (var r in all) if (seen.Add((r.width, r.height))) list.Add((r.width, r.height));
            if (widthIndex < list.Count) { width = list[widthIndex].Item1; height = list[widthIndex].Item2; }
        }

        // Resolve refresh rate
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
        int idx = PlayerPrefs.GetInt("Settings_FpsCapIndex", 2);
        int[] caps = { -1, 30, 60, 90, 120, 144, 240 };
        if (idx < 0 || idx >= caps.Length) idx = 2;
        QualitySettings.vSyncCount = 0; // never vsync when capping by frame rate
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
        // Off / FXAA / SMAA / TAA — map onto QualitySettings.antiAliasing (MSAA fallback).
        QualitySettings.antiAliasing = aa switch { 0 => 0, 1 => 2, 2 => 4, _ => 8 };
    }

    public static void ApplyTextureQuality()
    {
        int q = PlayerPrefs.GetInt("Settings_TextureQuality", 2);
        // Low / Medium / High / Ultra → MasterTextureLimit 3 / 2 / 1 / 0
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
        QualitySettings.shadowDistance = PlayerPrefs.GetFloat("Settings_ShadowDistance", 50f);
    }

    public static void ApplyRenderScale()
    {
        float rs = Mathf.Clamp(PlayerPrefs.GetFloat("Settings_RenderScale", 1f), 0.5f, 2f);
        var asset = UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
        if (asset != null) asset.renderScale = rs;
    }

    // For toggle-driven post-processing: we set a flag the project's
    // Volume profile / Volume component reads. Concrete bindings can be
    // tightened later by the gameplay team — these calls keep prefs
    // consistent regardless.
    public static void ApplyMotionBlur()      => Shader.SetGlobalFloat("_Settings_MotionBlur", PlayerPrefs.GetInt("Settings_MotionBlur", 0));
    public static void ApplyDepthOfField()    => Shader.SetGlobalFloat("_Settings_DOF",        PlayerPrefs.GetInt("Settings_DepthOfField", 0));
    public static void ApplyBloom()           => Shader.SetGlobalFloat("_Settings_Bloom",      PlayerPrefs.GetInt("Settings_Bloom", 1));
    public static void ApplyAmbientOcclusion() => Shader.SetGlobalFloat("_Settings_AO",        PlayerPrefs.GetInt("Settings_AO", 1));
    public static void ApplyVolumetrics()     => Shader.SetGlobalFloat("_Settings_Volumetrics",PlayerPrefs.GetInt("Settings_Volumetrics", 0));

    // ============================================================
    // CAMERA
    // ============================================================

    public static void ApplyFOV()
    {
        float fov = PlayerPrefs.GetFloat("Settings_FOV", 75f);
        Camera cam = Camera.main;
        if (cam != null) cam.fieldOfView = fov;
    }

    public static void ApplyBrightness()
    {
        float b = PlayerPrefs.GetFloat("Settings_Brightness", 1f);
        Shader.SetGlobalFloat("_Settings_Brightness", b);
        // Multiply ambient intensity so scenes get visibly brighter even
        // without a post-FX Volume profile bound.
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
        AudioManager.Instance.SetMasterVolume(PlayerPrefs.GetFloat("Settings_MasterVol", 100f) / 100f);
        AudioManager.Instance.SetMusicVolume(PlayerPrefs.GetFloat("Settings_MusicVol", 100f) / 100f);
        AudioManager.Instance.SetSFXVolume(PlayerPrefs.GetFloat("Settings_SFXVol", 100f) / 100f);
        AudioManager.Instance.SetUIVolume(PlayerPrefs.GetFloat("Settings_UIVol", 100f) / 100f);
        AudioManager.Instance.SetVoiceVolume(PlayerPrefs.GetFloat("Settings_VoiceVol", 100f) / 100f);
        AudioManager.Instance.SetAmbientVolume(PlayerPrefs.GetFloat("Settings_AmbientVol", 100f) / 100f);
    }

    // ============================================================
    // Mute on focus loss
    // ============================================================

    private void OnApplicationFocus(bool hasFocus)
    {
        if (PlayerPrefs.GetInt("Settings_MuteWhenUnfocused", 1) != 1) return;
        if (!hasFocus)
        {
            AudioListener.volume = 0f;
        }
        else
        {
            AudioListener.volume = PlayerPrefs.GetFloat("Settings_MasterVol", 100f) / 100f;
        }
    }
}
