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

        SwitchCategory(0);
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
            if (i < categoryStripes.Count && categoryStripes[i] != null) categoryStripes[i].enabled = (i == idx);
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
