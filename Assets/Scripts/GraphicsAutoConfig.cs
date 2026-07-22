using UnityEngine;

// First-launch hardware auto-detection. AAA titles never boot a fresh
// install at whatever quality level the project ships with — they probe
// the GPU/CPU/RAM and pick a sensible tier so a weak laptop doesn't open
// on Ultra and chug. This runs ONCE (guarded by Settings_AutoConfigured),
// writes the Settings_* PlayerPrefs for the detected tier, then lets
// SettingsApplier apply them. The player can still override everything in
// the settings menu afterwards, and a "Recommended" button can re-run
// DetectAndApply() on demand.
public static class GraphicsAutoConfig
{
    public enum Tier { Low = 0, Medium = 1, High = 2, Ultra = 3 }

    private const string PP_DONE = "Settings_AutoConfigured";
    private const string PP_TIER = "Settings_AutoTier";

    // Called by SettingsApplier.Bootstrap BEFORE ApplyAll so the prefs are
    // already written when settings are first applied.
    public static void EnsureFirstRun()
    {
        if (PlayerPrefs.GetInt(PP_DONE, 0) == 1) return;
        Tier tier = DetectTier();
        WriteTierPrefs(tier);
        PlayerPrefs.SetInt(PP_DONE, 1);
        PlayerPrefs.SetInt(PP_TIER, (int)tier);
        PlayerPrefs.Save();
        Debug.Log($"[GraphicsAutoConfig] First launch — detected tier {tier} " +
                  $"(GPU '{SystemInfo.graphicsDeviceName}', {SystemInfo.graphicsMemorySize}MB VRAM, " +
                  $"{SystemInfo.systemMemorySize}MB RAM, {SystemInfo.processorCount} threads). " +
                  "Applied recommended graphics settings.");
    }

    // Public entry point for a settings-menu "Auto-detect / Recommended"
    // button — recomputes and applies immediately.
    public static Tier DetectAndApply()
    {
        Tier tier = DetectTier();
        WriteTierPrefs(tier);
        PlayerPrefs.SetInt(PP_DONE, 1);
        PlayerPrefs.SetInt(PP_TIER, (int)tier);
        PlayerPrefs.Save();
        if (SettingsApplier.Instance != null) SettingsApplier.Instance.ApplyAll();
        return tier;
    }

    public static Tier CurrentTier() => (Tier)PlayerPrefs.GetInt(PP_TIER, (int)Tier.Medium);

    // ------------------------------------------------------------------
    //  Hardware scoring
    // ------------------------------------------------------------------
    private static Tier DetectTier()
    {
        int score = 0;

        // VRAM — the strongest single signal for a GPU's headroom.
        int vram = SystemInfo.graphicsMemorySize; // MB
        if (vram >= 8000) score += 4;
        else if (vram >= 6000) score += 3;
        else if (vram >= 4000) score += 2;
        else if (vram >= 2000) score += 1;
        // < 2GB → 0

        // System RAM.
        int ram = SystemInfo.systemMemorySize; // MB
        if (ram >= 32000) score += 3;
        else if (ram >= 16000) score += 2;
        else if (ram >= 8000) score += 1;

        // CPU logical cores.
        int threads = SystemInfo.processorCount;
        if (threads >= 12) score += 3;
        else if (threads >= 8) score += 2;
        else if (threads >= 4) score += 1;

        // GPU model heuristics — VRAM alone can't tell a modern mid-range
        // from an old card that happened to ship with a lot of memory.
        string gpu = (SystemInfo.graphicsDeviceName ?? "").ToLowerInvariant();
        if (gpu.Contains("rtx 40") || gpu.Contains("rtx 50") ||
            gpu.Contains("rx 79") || gpu.Contains("rx 78") || gpu.Contains("rx 77")) score += 3;
        else if (gpu.Contains("rtx 30") || gpu.Contains("rtx 20") ||
                 gpu.Contains("rx 66") || gpu.Contains("rx 67") || gpu.Contains("rx 68") ||
                 gpu.Contains("arc a")) score += 2;
        else if (gpu.Contains("gtx 16") || gpu.Contains("gtx 10") ||
                 gpu.Contains("rx 55") || gpu.Contains("rx 56") || gpu.Contains("rx 57") ||
                 gpu.Contains("rx 58") || gpu.Contains("m1") || gpu.Contains("m2") ||
                 gpu.Contains("m3") || gpu.Contains("apple")) score += 1;
        // Integrated graphics — clamp down hard.
        bool integrated = gpu.Contains("intel") &&
                          (gpu.Contains("uhd") || gpu.Contains("hd graphics") || gpu.Contains("iris"));
        if (integrated) score -= 3;
        if (gpu.Contains("vega") && gpu.Contains("radeon graphics")) score -= 1; // APU

        // Shader level floor — anything below SM4.5 can't run our stack well.
        if (SystemInfo.graphicsShaderLevel < 45) return Tier.Low;

        // Map the summed score (roughly 0..13) into a tier.
        if (score <= 2) return Tier.Low;
        if (score <= 6) return Tier.Medium;
        if (score <= 10) return Tier.High;
        return Tier.Ultra;
    }

    // ------------------------------------------------------------------
    //  Per-tier PlayerPrefs
    // ------------------------------------------------------------------
    private static void WriteTierPrefs(Tier t)
    {
        int qualityLevels = QualitySettings.names.Length;
        int Clamp(int q) => Mathf.Clamp(q, 0, Mathf.Max(0, qualityLevels - 1));

        switch (t)
        {
            case Tier.Low:
                SetInt("Settings_QualityLevel", Clamp(0));
                SetInt("Settings_AntiAliasing", 0);   // off
                SetInt("Settings_TextureQuality", 1); // half-res
                SetInt("Settings_ShadowQuality", 1);  // hard only, low res
                SetFloat("Settings_ShadowDistance", 30f);
                SetFloat("Settings_RenderScale", 0.8f);
                SetInt("Settings_Bloom", 0);
                SetInt("Settings_AO", 0);
                SetInt("Settings_Volumetrics", 0);
                SetInt("Settings_MotionBlur", 0);
                SetInt("Settings_DepthOfField", 0);
                SetFpsCap(2);                          // 60
                break;

            case Tier.Medium:
                SetInt("Settings_QualityLevel", Clamp(1));
                SetInt("Settings_AntiAliasing", 1);   // 2x
                SetInt("Settings_TextureQuality", 2);
                SetInt("Settings_ShadowQuality", 2);  // all, medium
                SetFloat("Settings_ShadowDistance", 60f);
                SetFloat("Settings_RenderScale", 1.0f);
                SetInt("Settings_Bloom", 1);
                SetInt("Settings_AO", 1);
                SetInt("Settings_Volumetrics", 0);
                SetInt("Settings_MotionBlur", 0);
                SetInt("Settings_DepthOfField", 0);
                SetFpsCap(2);                          // 60
                break;

            case Tier.High:
                SetInt("Settings_QualityLevel", Clamp(2));
                SetInt("Settings_AntiAliasing", 2);   // 4x
                SetInt("Settings_TextureQuality", 3); // full-res
                SetInt("Settings_ShadowQuality", 2);  // all, medium
                SetFloat("Settings_ShadowDistance", 95f);
                SetFloat("Settings_RenderScale", 1.0f);
                SetInt("Settings_Bloom", 1);
                SetInt("Settings_AO", 1);
                SetInt("Settings_Volumetrics", 1);
                SetInt("Settings_MotionBlur", 0);
                SetInt("Settings_DepthOfField", 0);
                SetFpsCap(4);                          // 120
                break;

            default: // Ultra
                SetInt("Settings_QualityLevel", Clamp(qualityLevels - 1));
                SetInt("Settings_AntiAliasing", 3);   // 8x
                SetInt("Settings_TextureQuality", 3); // full-res
                SetInt("Settings_ShadowQuality", 3);  // high res
                SetFloat("Settings_ShadowDistance", 130f);
                SetFloat("Settings_RenderScale", 1.0f);
                SetInt("Settings_Bloom", 1);
                SetInt("Settings_AO", 1);
                SetInt("Settings_Volumetrics", 1);
                SetInt("Settings_MotionBlur", 0);
                SetInt("Settings_DepthOfField", 0);
                SetFpsCap(5);                          // 144
                break;
        }

        // Common AAA-correct defaults regardless of tier — only set when
        // the player hasn't already chosen (first launch).
        DefaultInt("Settings_VSync", 0);              // off — we cap FPS instead
        DefaultInt("Settings_FPSLimit", 1);           // cap enabled
        DefaultInt("Settings_MuteWhenUnfocused", 1);  // pause audio on alt-tab
        DefaultFloat("Settings_FOV", 75f);
        DefaultInt("Settings_WindowMode", 1);         // borderless fullscreen — safest default
    }

    private static void SetFpsCap(int index)
    {
        SetInt("Settings_FPSLimit", 1);
        SetInt("Settings_FpsCapIndex", index);
    }

    private static void SetInt(string k, int v) => PlayerPrefs.SetInt(k, v);
    private static void SetFloat(string k, float v) => PlayerPrefs.SetFloat(k, v);
    private static void DefaultInt(string k, int v) { if (!PlayerPrefs.HasKey(k)) PlayerPrefs.SetInt(k, v); }
    private static void DefaultFloat(string k, float v) { if (!PlayerPrefs.HasKey(k)) PlayerPrefs.SetFloat(k, v); }
}
