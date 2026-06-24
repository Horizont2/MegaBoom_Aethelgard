using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using UnityEngine.EventSystems;

// Runtime controller attached to the AAA Settings root by the editor
// builder. Drives category switching, hover-to-describe on the right
// rail, and the Reset / Discard / Apply footer buttons.
public class SettingsAAARuntime : MonoBehaviour
{
    [System.Serializable]
    public class HoverRow
    {
        public GameObject row;
        public string description;
    }

    public SettingsUI settingsUI;
    public CanvasGroup canvasGroup;
    public TextMeshProUGUI descriptionText;

    public List<RectTransform> categoryRoots = new List<RectTransform>();
    public List<Image> categoryStripes = new List<Image>();

    public Color accentColor = new Color(1f, 0.82f, 0.24f, 1f);
    public Color dimAccent = new Color(1f, 0.82f, 0.24f, 0.12f);

    public List<HoverRow> hoverRows = new List<HoverRow>();

    private int activeIndex = 0;
    private string defaultDescription = "Mouse over any option to read what it does.";

    // Editor-time AddListener calls don't survive scene save / play-mode
    // reloads — only persistent UnityEvent links from the inspector do.
    // Re-wire every button ourselves in OnEnable so the panel works
    // whether the user re-built it just now or loaded the saved scene.
    private void Awake() { Initialise(); }
    private void OnEnable() { Initialise(); }

    public void Initialise()
    {
        // Hook hover behaviour into the runtime; the editor builder already
        // attaches a SettingsAAARowHover stub per row that defers to us.
        foreach (var hr in hoverRows)
        {
            if (hr.row == null) continue;
            SettingsAAARowHover h = hr.row.GetComponent<SettingsAAARowHover>();
            if (h == null) h = hr.row.AddComponent<SettingsAAARowHover>();
            h.description = hr.description;
            h.runtime = this;
        }
        // Hook category buttons.
        for (int i = 0; i < categoryRoots.Count; i++)
        {
            int captured = i;
            Transform parent = transform;
            Transform btnHost = FindChildRecursive(parent, "Cat_" + categoryNameAt(i));
            if (btnHost != null)
            {
                Button btn = btnHost.GetComponent<Button>();
                if (btn != null)
                {
                    btn.onClick.RemoveAllListeners();
                    btn.onClick.AddListener(() => SwitchCategory(captured));
                }
            }
        }

        // Header buttons.
        Button back = FindButtonByName("BackButton");
        if (back != null) { back.onClick.RemoveAllListeners(); back.onClick.AddListener(Close); }
        Button close = FindButtonByName("CloseButton");
        if (close != null) { close.onClick.RemoveAllListeners(); close.onClick.AddListener(Close); }

        // Footer buttons.
        Button reset = FindButtonByName("ResetButton");
        if (reset != null) { reset.onClick.RemoveAllListeners(); reset.onClick.AddListener(ResetDefaults); }
        Button discard = FindButtonByName("DiscardButton");
        if (discard != null) { discard.onClick.RemoveAllListeners(); discard.onClick.AddListener(Discard); }
        Button apply = FindButtonByName("ApplyButton");
        if (apply != null) { apply.onClick.RemoveAllListeners(); apply.onClick.AddListener(Apply); }

        WireGraphicsPresetLogic();
        SwitchCategory(0);
    }

    // ===== Graphics preset linkage =====
    // - When the user picks Low / Medium / High / Ultra → all individual
    //   tier dropdowns + toggles get snapped to the matching values.
    // - When the user touches any individual tier manually → preset is
    //   pushed to "Custom" so the UI reflects that it's no longer the
    //   stock preset.
    //
    // The Preset dropdown is rebuilt with 5 entries (Low / Medium / High
    // / Ultra / Custom). Other tier dropdowns are looked up via reflection
    // so the runtime stays decoupled from the exact field names.

    private bool suppressTierEcho;
    private bool suppressPresetEcho;

    private void WireGraphicsPresetLogic()
    {
        if (settingsUI == null) return;
        var s = settingsUI;

        TMPro.TMP_Dropdown preset = s.qualityDropdown;
        if (preset != null)
        {
            preset.ClearOptions();
            preset.AddOptions(new List<string> { "Low", "Medium", "High", "Ultra", "Custom" });
            int saved = Mathf.Clamp(PlayerPrefs.GetInt("Settings_QualityLevel", 2), 0, 4);
            preset.SetValueWithoutNotify(saved);
            preset.RefreshShownValue();
            preset.onValueChanged.RemoveListener(OnPresetChanged);
            preset.onValueChanged.AddListener(OnPresetChanged);
        }

        HookTierDropdown(s.antiAliasingDropdown);
        HookTierDropdown(s.textureQualityDropdown);
        HookTierDropdown(s.shadowQualityDropdown);
        HookTierToggle(s.postFXToggle);
        HookTierToggle(s.dynamicShadowsToggle);
        HookTierToggle(s.motionBlurToggle);
        HookTierToggle(s.depthOfFieldToggle);
        HookTierToggle(s.bloomToggle);
        HookTierToggle(s.ambientOcclusionToggle);
        HookTierToggle(s.volumetricsToggle);
        HookTierSlider(s.shadowDistanceSlider);
        HookTierSlider(s.renderScaleSlider);
    }

    private void HookTierDropdown(TMPro.TMP_Dropdown d)
    {
        if (d == null) return;
        d.onValueChanged.RemoveListener(OnTierTouched);
        d.onValueChanged.AddListener(OnTierTouched);
    }
    private void HookTierToggle(Toggle t)
    {
        if (t == null) return;
        t.onValueChanged.RemoveListener(OnTierTouchedBool);
        t.onValueChanged.AddListener(OnTierTouchedBool);
    }
    private void HookTierSlider(Slider s)
    {
        if (s == null) return;
        s.onValueChanged.RemoveListener(OnTierTouchedFloat);
        s.onValueChanged.AddListener(OnTierTouchedFloat);
    }

    private void OnTierTouched(int _) { MarkCustomPreset(); }
    private void OnTierTouchedBool(bool _) { MarkCustomPreset(); }
    private void OnTierTouchedFloat(float _) { MarkCustomPreset(); }

    private void MarkCustomPreset()
    {
        if (suppressTierEcho) return;
        var preset = settingsUI != null ? settingsUI.qualityDropdown : null;
        if (preset == null) return;
        if (preset.value == 4) return;
        suppressPresetEcho = true;
        preset.SetValueWithoutNotify(4);
        preset.RefreshShownValue();
        suppressPresetEcho = false;
    }

    private void OnPresetChanged(int idx)
    {
        if (suppressPresetEcho) return;
        if (idx < 0 || idx > 3) return;
        var s = settingsUI;
        if (s == null) return;
        suppressTierEcho = true;

        // Tier values per preset (Low=0, Medium=1, High=2, Ultra=3).
        int aa            = new[] { 0, 1, 2, 3 }[idx];
        int texture       = new[] { 0, 1, 2, 3 }[idx];
        int shadow        = new[] { 0, 1, 2, 3 }[idx];
        float renderScale = new[] { 75f, 90f, 100f, 100f }[idx];
        float shadowDist  = new[] { 30f, 50f, 80f, 150f }[idx];
        bool postFX       = idx >= 1;
        bool dynShadows   = idx >= 1;
        bool motionBlur   = idx >= 3;
        bool dof          = idx >= 3;
        bool bloom        = idx >= 1;
        bool ao           = idx >= 1;
        bool volumetrics  = idx >= 2;

        if (s.antiAliasingDropdown   != null) s.antiAliasingDropdown.value   = aa;
        if (s.textureQualityDropdown != null) s.textureQualityDropdown.value = texture;
        if (s.shadowQualityDropdown  != null) s.shadowQualityDropdown.value  = shadow;
        if (s.renderScaleSlider      != null) s.renderScaleSlider.value      = renderScale;
        if (s.shadowDistanceSlider   != null) s.shadowDistanceSlider.value   = shadowDist;
        if (s.postFXToggle           != null) s.postFXToggle.isOn            = postFX;
        if (s.dynamicShadowsToggle   != null) s.dynamicShadowsToggle.isOn    = dynShadows;
        if (s.motionBlurToggle       != null) s.motionBlurToggle.isOn        = motionBlur;
        if (s.depthOfFieldToggle     != null) s.depthOfFieldToggle.isOn      = dof;
        if (s.bloomToggle            != null) s.bloomToggle.isOn             = bloom;
        if (s.ambientOcclusionToggle != null) s.ambientOcclusionToggle.isOn  = ao;
        if (s.volumetricsToggle      != null) s.volumetricsToggle.isOn       = volumetrics;

        suppressTierEcho = false;
    }

    public void Close()
    {
        if (settingsUI != null) settingsUI.CloseSettings();
        else gameObject.SetActive(false);
    }

    private string categoryNameAt(int i)
    {
        // Mirrors SettingsPanelAAABuilder.Categories order.
        string[] names = { "General", "Gameplay", "Audio", "Video", "Graphics", "Controls", "Accessibility", "Language" };
        return i >= 0 && i < names.Length ? names[i] : "";
    }

    public void SwitchCategory(int idx)
    {
        activeIndex = idx;
        for (int i = 0; i < categoryRoots.Count; i++)
        {
            if (categoryRoots[i] != null) categoryRoots[i].gameObject.SetActive(i == idx);
        }
        // Drive per-button state via SettingsAAACategoryButton if present
        // (two-layer themed flow). Falls back to the old categoryStripes
        // toggle for setups that haven't been re-built since the migration.
        var stateBtns = GetComponentsInChildren<SettingsAAACategoryButton>(true);
        if (stateBtns != null && stateBtns.Length > 0)
        {
            var theme = GetComponent<SettingsAAATheme>();
            for (int i = 0; i < stateBtns.Length; i++)
            {
                if (stateBtns[i] == null) continue;
                if (theme != null) stateBtns[i].theme = theme;
                stateBtns[i].SetSelected(i == idx);
            }
        }
        else
        {
            for (int i = 0; i < categoryStripes.Count; i++)
                if (categoryStripes[i] != null) categoryStripes[i].enabled = (i == idx);
        }
        SetDescription(defaultDescription);
    }

    public void SetDescription(string text)
    {
        if (descriptionText != null) descriptionText.text = text;
    }

    public void ClearDescription()
    {
        if (descriptionText != null) descriptionText.text = defaultDescription;
    }

    public void Apply()
    {
        // CloseSettings persists everything to PlayerPrefs (it already
        // walks every slider/toggle on close). Re-applying here pushes
        // the new values into the live engine systems.
        if (settingsUI != null) settingsUI.CloseSettings();
        if (SettingsApplier.Instance != null) SettingsApplier.Instance.ApplyAll();
    }

    public void Discard()
    {
        // Values that weren't committed via slider/onValueChanged callbacks
        // won't persist beyond CloseSettings. Roll back the runtime engine
        // state so any live-applied changes (resolution, render scale,
        // FOV) revert to the saved PlayerPrefs values.
        if (settingsUI != null) settingsUI.CloseSettings();
        if (SettingsApplier.Instance != null) SettingsApplier.Instance.ApplyAll();
    }

    public void ResetDefaults()
    {
        // Apply per-category PlayerPrefs defaults, then re-open the panel
        // so reload-from-prefs picks them up.
        // 8 categories: General, Gameplay, Audio, Video, Graphics,
        // Controls, Accessibility, Language. Each branch only resets the
        // prefs it owns so other tabs stay untouched.
        switch (activeIndex)
        {
            case 0: // General
                PlayerPrefs.SetInt("Settings_FPSLimit", 1);
                PlayerPrefs.SetInt("Settings_ShowFPS", 0);
                PlayerPrefs.SetInt("Settings_AutoSave", 1);
                break;
            case 1: // Gameplay
                PlayerPrefs.SetInt("Settings_DamagePopups", 1);
                PlayerPrefs.SetInt("Settings_ScreenShake", 1);
                PlayerPrefs.SetInt("Settings_HitStop", 1);
                PlayerPrefs.SetInt("Settings_LowHpVignette", 1);
                PlayerPrefs.SetInt("Settings_TutorialHints", 1);
                PlayerPrefs.SetInt("Settings_Difficulty", 1);
                PlayerPrefs.SetInt("Settings_HoldToggleSprint", 0);
                break;
            case 2: // Audio
                PlayerPrefs.SetFloat("Settings_MasterVol", 100f);
                PlayerPrefs.SetFloat("Settings_MusicVol", 100f);
                PlayerPrefs.SetFloat("Settings_SFXVol", 100f);
                PlayerPrefs.SetFloat("Settings_VoiceVol", 100f);
                PlayerPrefs.SetFloat("Settings_UIVol", 100f);
                PlayerPrefs.SetFloat("Settings_AmbientVol", 100f);
                PlayerPrefs.SetInt("Settings_MuteWhenUnfocused", 1);
                break;
            case 3: // Video
                PlayerPrefs.DeleteKey("Settings_ResolutionIndex");
                PlayerPrefs.DeleteKey("Settings_RefreshRateIndex");
                PlayerPrefs.SetInt("Settings_WindowMode", 0);
                PlayerPrefs.SetInt("Settings_Monitor", 0);
                PlayerPrefs.SetInt("Settings_VSync", 0);
                PlayerPrefs.SetInt("Settings_FpsCapIndex", 2);
                PlayerPrefs.SetFloat("Settings_FOV", 75f);
                PlayerPrefs.SetFloat("Settings_Brightness", 1f);
                PlayerPrefs.SetFloat("Settings_Gamma", 1f);
                break;
            case 4: // Graphics
                PlayerPrefs.SetInt("Settings_QualityLevel", QualitySettings.GetQualityLevel());
                PlayerPrefs.SetFloat("Settings_RenderScale", 1f);
                PlayerPrefs.SetInt("Settings_AntiAliasing", 1);
                PlayerPrefs.SetInt("Settings_TextureQuality", 2);
                PlayerPrefs.SetInt("Settings_ShadowQuality", 2);
                PlayerPrefs.SetFloat("Settings_ShadowDistance", 50f);
                PlayerPrefs.SetInt("Settings_PostFX", 1);
                PlayerPrefs.SetInt("Settings_DynamicShadows", 1);
                PlayerPrefs.SetInt("Settings_MotionBlur", 0);
                PlayerPrefs.SetInt("Settings_DepthOfField", 0);
                PlayerPrefs.SetInt("Settings_Bloom", 1);
                PlayerPrefs.SetInt("Settings_AO", 1);
                PlayerPrefs.SetInt("Settings_Volumetrics", 0);
                break;
            case 5: // Controls
                PlayerPrefs.SetFloat("Settings_MouseSensitivity", 1f);
                PlayerPrefs.SetInt("Settings_InvertYAxis", 0);
                PlayerPrefs.SetInt("Settings_ControllerVibration", 1);
                PlayerPrefs.SetFloat("Settings_AimAssist", 0.4f);
                break;
            case 6: // Accessibility
                PlayerPrefs.SetInt("Settings_Subtitles", 1);
                PlayerPrefs.SetInt("Settings_SubtitleSize", 1);
                PlayerPrefs.SetInt("Settings_SubtitleBg", 1);
                PlayerPrefs.SetInt("Settings_Colorblind", 0);
                PlayerPrefs.SetInt("Settings_HighContrast", 0);
                PlayerPrefs.SetInt("Settings_ReduceMotion", 0);
                PlayerPrefs.SetInt("Settings_Photosensitivity", 0);
                PlayerPrefs.SetFloat("Settings_UIScale", 1f);
                break;
            case 7: // Language
                PlayerPrefs.SetInt("Settings_Language", 0);
                PlayerPrefs.SetInt("Settings_VoiceLanguage", 0);
                break;
        }
        PlayerPrefs.Save();
        SettingsApplier.Instance?.ApplyAll();

        // Re-trigger SettingsUI population by closing and re-opening.
        if (settingsUI != null)
        {
            settingsUI.CloseSettings();
            settingsUI.OpenSettings();
        }
    }

    private Button FindButtonByName(string name)
    {
        Button[] buttons = GetComponentsInChildren<Button>(true);
        foreach (var b in buttons) if (b.name == name) return b;
        return null;
    }

    private Transform FindChildRecursive(Transform parent, string name)
    {
        for (int i = 0; i < parent.childCount; i++)
        {
            Transform c = parent.GetChild(i);
            if (c.name == name) return c;
            Transform deep = FindChildRecursive(c, name);
            if (deep != null) return deep;
        }
        return null;
    }
}
