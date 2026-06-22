using UnityEngine;
using UnityEngine.UI;
using TMPro;

// Drop this on your existing SettingsUI panel and wire any of the
// listed controls. The component handles:
//   * loading the saved value into the control on enable
//   * writing changes through to the matching settings store
//     (MouseSensitivitySettings, SubtitleSettings,
//     LocalizationManager, QualitySettings, PlayerPrefs)
//   * fallback: every reference is optional — leave blank to skip.
//
// You can keep your existing SettingsUI script for the audio sliders
// and toggles you've already wired; this just covers the new ones
// added by the Tier 3 / Tier 5 systems.
public class SettingsBindings : MonoBehaviour
{
    [Header("Mouse sensitivity (range maps to MouseSensitivitySettings.Multiplier)")]
    public Slider sensitivitySlider;
    public TMP_InputField sensitivityInput;
    [Tooltip("Slider value displayed and saved is multiplier × 100.")]
    public float sensitivitySliderMin = 25f;
    public float sensitivitySliderMax = 300f;

    [Header("Subtitles")]
    public Toggle subtitlesToggle;
    public TMP_Dropdown subtitleSizeDropdown; // expects items Small / Medium / Large

    [Header("Accessibility")]
    public Toggle colorblindToggle;

    [Header("Language")]
    public TMP_Dropdown languageDropdown; // expects items English / Ukrainian

    [Header("Graphics quality")]
    public TMP_Dropdown qualityDropdown; // expects 4 items (Low/Med/High/Ultra)

    [Header("Optional live readouts")]
    [Tooltip("If set, will display \"Achievements: 5/12\" etc.")]
    public TMP_Text achievementsReadout;
    public TMP_Text loreReadout;
    public TMP_Text statsReadout;

    [Header("Codex / Stats hotbuttons")]
    [Tooltip("Wire to a button that should open your codex UI when clicked.")]
    public Button openCodexButton;
    [Tooltip("Your codex UI controller — Click of openCodexButton routes here.")]
    public LoreCodexUIController codexController;

    private bool wired;

    private void OnEnable()
    {
        if (!wired) WireListeners();
        Refresh();
    }

    private void WireListeners()
    {
        wired = true;

        if (sensitivitySlider != null)
        {
            sensitivitySlider.minValue = sensitivitySliderMin;
            sensitivitySlider.maxValue = sensitivitySliderMax;
            sensitivitySlider.onValueChanged.AddListener(OnSensitivitySliderChanged);
        }
        if (sensitivityInput != null)
            sensitivityInput.onEndEdit.AddListener(OnSensitivityInputChanged);

        if (subtitlesToggle != null)
            subtitlesToggle.onValueChanged.AddListener(v => SubtitleSettings.Enabled = v);

        if (subtitleSizeDropdown != null)
            subtitleSizeDropdown.onValueChanged.AddListener(v => SubtitleSettings.CurrentSize = (SubtitleSettings.Size)Mathf.Clamp(v, 0, 2));

        if (colorblindToggle != null)
            colorblindToggle.onValueChanged.AddListener(v =>
            {
                PlayerPrefs.SetInt("Settings_Colorblind", v ? 1 : 0);
                PlayerPrefs.Save();
            });

        if (languageDropdown != null)
            languageDropdown.onValueChanged.AddListener(v => LocalizationManager.CurrentLanguage = (LocalizationManager.Lang)Mathf.Clamp(v, 0, 1));

        if (qualityDropdown != null)
            qualityDropdown.onValueChanged.AddListener(v => QualitySettings.SetQualityLevel(Mathf.Clamp(v, 0, QualitySettings.names.Length - 1), true));

        if (openCodexButton != null)
            openCodexButton.onClick.AddListener(() => { if (codexController != null) codexController.Open(); });
    }

    public void Refresh()
    {
        if (sensitivitySlider != null) sensitivitySlider.value = MouseSensitivitySettings.Multiplier * 100f;
        if (sensitivityInput != null) sensitivityInput.text = Mathf.RoundToInt(MouseSensitivitySettings.Multiplier * 100f).ToString();

        if (subtitlesToggle != null) subtitlesToggle.isOn = SubtitleSettings.Enabled;
        if (subtitleSizeDropdown != null) subtitleSizeDropdown.value = (int)SubtitleSettings.CurrentSize;

        if (colorblindToggle != null) colorblindToggle.isOn = PlayerPrefs.GetInt("Settings_Colorblind", 0) == 1;

        if (languageDropdown != null) languageDropdown.value = (int)LocalizationManager.CurrentLanguage;
        if (qualityDropdown != null) qualityDropdown.value = Mathf.Clamp(QualitySettings.GetQualityLevel(), 0, qualityDropdown.options.Count - 1);

        if (achievementsReadout != null)
            achievementsReadout.text = $"{AchievementManager.GetUnlockedCount()}/{AchievementManager.TotalCount}";
        if (loreReadout != null)
            loreReadout.text = $"{LoreCodexManager.UnlockedCount}/{LoreCodexManager.TotalCount}";
        if (statsReadout != null)
            statsReadout.text = RunStats.GetSummaryText();
    }

    private void OnSensitivitySliderChanged(float v)
    {
        MouseSensitivitySettings.Multiplier = v / 100f;
        if (sensitivityInput != null) sensitivityInput.text = Mathf.RoundToInt(v).ToString();
    }

    private void OnSensitivityInputChanged(string s)
    {
        if (!float.TryParse(s, out float result))
        {
            if (sensitivityInput != null) sensitivityInput.text = Mathf.RoundToInt(MouseSensitivitySettings.Multiplier * 100f).ToString();
            return;
        }
        result = Mathf.Clamp(result, sensitivitySliderMin, sensitivitySliderMax);
        if (sensitivitySlider != null) sensitivitySlider.value = result;
        else MouseSensitivitySettings.Multiplier = result / 100f;
    }
}
