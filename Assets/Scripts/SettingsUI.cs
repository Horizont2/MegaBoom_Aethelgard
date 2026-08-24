using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using TMPro;
using System.Collections;
using System.Collections.Generic;

public class SettingsUI : MonoBehaviour
{
    public static SettingsUI Instance;

    [Header("UI Panels & Animation")]
    public GameObject settingsPanel;
    public CanvasGroup panelCanvasGroup;
    public RectTransform panelRect;
    public float animationSpeed = 10f;

    [Header("Tabs Navigation")]
    public RectTransform underline;
    public TextMeshProUGUI[] tabTexts;
    public GameObject[] tabPanels;

    private int currentTabIndex = 0;
    private float targetLineLocalX;

    [Header("Audio Sliders")]
    public Slider masterSlider;
    public Slider musicSlider;
    public Slider sfxSlider;

    [Header("Audio Input Fields")]
    public TMP_InputField masterInput;
    public TMP_InputField musicInput;
    public TMP_InputField sfxInput;

    [Header("Gameplay Toggles")]
    public Toggle damagePopupsToggle;
    public Toggle screenShakeToggle;
    public Toggle limitFPSToggle;
    public Toggle showFPSToggle;

    [Header("Checkmark Graphics (UI)")]
    public Graphic damageCheckmark;
    public Graphic screenShakeCheckmark;
    public Graphic fpsLimitCheckmark;
    public Graphic showFPSCheckmark;

    [Header("Mouse / Camera")]
    public Slider sensitivitySlider;
    public TMP_InputField sensitivityInput;
    public Toggle invertYToggle;
    public Graphic invertYCheckmark;

    [Header("Subtitles")]
    public Toggle subtitlesToggle;
    public Graphic subtitlesCheckmark;
    public TMP_Dropdown subtitleSizeDropdown;

    [Header("Accessibility")]
    public Toggle colorblindToggle;
    public Graphic colorblindCheckmark;
    public Toggle lowHpVignetteToggle;
    public Graphic lowHpVignetteCheckmark;
    public Toggle hitStopToggle;
    public Graphic hitStopCheckmark;

    [Header("Graphics")]
    public TMP_Dropdown qualityDropdown;
    public Slider renderScaleSlider;
    public TMP_InputField renderScaleInput;
    public Toggle postFXToggle;
    public Graphic postFXCheckmark;
    public Toggle dynamicShadowsToggle;
    public Graphic dynamicShadowsCheckmark;

    [Header("Language")]
    public TMP_Dropdown languageDropdown;
    public TMP_Dropdown voiceLanguageDropdown;

    [Header("Video — Resolution / Window")]
    public TMP_Dropdown resolutionDropdown;
    public TMP_Dropdown windowModeDropdown;
    public TMP_Dropdown refreshRateDropdown;
    public TMP_Dropdown monitorDropdown;
    public Toggle vsyncToggle;
    public Graphic vsyncCheckmark;
    public TMP_Dropdown fpsCapDropdown;

    [Header("Video — Camera")]
    public Slider fovSlider;
    public TMP_InputField fovInput;
    public Slider brightnessSlider;
    public TMP_InputField brightnessInput;
    public Slider gammaSlider;
    public TMP_InputField gammaInput;

    [Header("Graphics — Quality Tiers")]
    public TMP_Dropdown antiAliasingDropdown;
    public TMP_Dropdown textureQualityDropdown;
    public TMP_Dropdown shadowQualityDropdown;
    public Slider shadowDistanceSlider;
    public Toggle motionBlurToggle;
    public Graphic motionBlurCheckmark;
    public Toggle depthOfFieldToggle;
    public Graphic depthOfFieldCheckmark;
    public Toggle bloomToggle;
    public Graphic bloomCheckmark;
    public Toggle ambientOcclusionToggle;
    public Graphic ambientOcclusionCheckmark;
    public Toggle volumetricsToggle;
    public Graphic volumetricsCheckmark;

    [Header("Audio — Extended")]
    public Slider voiceSlider;
    public TMP_InputField voiceInput;
    public Slider uiSlider;
    public TMP_InputField uiInput;
    public Slider ambientSlider;
    public TMP_InputField ambientInput;
    public Toggle muteWhenUnfocusedToggle;
    public Graphic muteWhenUnfocusedCheckmark;

    [Header("Gameplay — Extended")]
    public TMP_Dropdown difficultyDropdown;
    public Toggle tutorialHintsToggle;
    public Graphic tutorialHintsCheckmark;
    public Toggle holdToggleSprintToggle;
    public Graphic holdToggleSprintCheckmark;
    public Toggle autoSaveToggle;
    public Graphic autoSaveCheckmark;

    [Header("Accessibility — Extended")]
    public Slider uiScaleSlider;
    public TMP_InputField uiScaleInput;
    public Toggle subtitleBackgroundToggle;
    public Graphic subtitleBackgroundCheckmark;
    public Toggle highContrastToggle;
    public Graphic highContrastCheckmark;
    public Toggle reduceMotionToggle;
    public Graphic reduceMotionCheckmark;
    public Toggle photosensitivityToggle;
    public Graphic photosensitivityCheckmark;

    [Header("Controls — Extended")]
    public Toggle controllerVibrationToggle;
    public Graphic controllerVibrationCheckmark;
    public Slider aimAssistSlider;
    public TMP_InputField aimAssistInput;

    [Header("Buttons")]
    public Button closeButton;
    public TextMeshProUGUI closeButtonText;
    public Button saveButton;

    private Coroutine panelAnimCoroutine;

    private void Awake()
    {
        if (Instance == null) Instance = this;
    }

    private void Start()
    {
        if (settingsPanel != null)
        {
            settingsPanel.SetActive(false);
            if (panelCanvasGroup != null) panelCanvasGroup.alpha = 0f;
        }

        // Ініціалізація головних повзунків звуку
        if (masterSlider)
        {
            masterSlider.minValue = 0f;
            masterSlider.maxValue = 100f;
            masterSlider.SetValueWithoutNotify(PlayerPrefs.GetFloat("Settings_MasterVol", 100f));
        }
        if (musicSlider)
        {
            musicSlider.minValue = 0f;
            musicSlider.maxValue = 100f;
            musicSlider.SetValueWithoutNotify(PlayerPrefs.GetFloat("Settings_MusicVol", 100f));
        }
        if (sfxSlider)
        {
            sfxSlider.minValue = 0f;
            sfxSlider.maxValue = 100f;
            sfxSlider.SetValueWithoutNotify(PlayerPrefs.GetFloat("Settings_SFXVol", 100f));
        }

        // Ініціалізація додаткових повзунків звуку
        if (voiceSlider) voiceSlider.SetValueWithoutNotify(PlayerPrefs.GetFloat("Settings_VoiceVol", 100f));
        if (uiSlider) uiSlider.SetValueWithoutNotify(PlayerPrefs.GetFloat("Settings_UIVol", 100f));
        if (ambientSlider) ambientSlider.SetValueWithoutNotify(PlayerPrefs.GetFloat("Settings_AmbientVol", 100f));

        if (masterInput && masterSlider) masterInput.text = masterSlider.value.ToString("0");
        if (musicInput && musicSlider) musicInput.text = musicSlider.value.ToString("0");
        if (sfxInput && sfxSlider) sfxInput.text = sfxSlider.value.ToString("0");

        if (masterSlider) masterSlider.onValueChanged.AddListener(OnMasterVolumeChanged);
        if (musicSlider) musicSlider.onValueChanged.AddListener(OnMusicVolumeChanged);
        if (sfxSlider) sfxSlider.onValueChanged.AddListener(OnSFXVolumeChanged);

        if (masterInput) masterInput.onEndEdit.AddListener(OnMasterInputChanged);
        if (musicInput) musicInput.onEndEdit.AddListener(OnMusicInputChanged);
        if (sfxInput) sfxInput.onEndEdit.AddListener(OnSFXInputChanged);

        if (damagePopupsToggle) damagePopupsToggle.onValueChanged.AddListener(OnDamagePopupsChanged);
        if (screenShakeToggle) screenShakeToggle.onValueChanged.AddListener(OnScreenShakeChanged);
        if (limitFPSToggle) limitFPSToggle.onValueChanged.AddListener(OnFPSLimitChanged);
        if (showFPSToggle) showFPSToggle.onValueChanged.AddListener(OnShowFPSChanged);

        if (sensitivitySlider)
        {
            sensitivitySlider.minValue = 10f;
            sensitivitySlider.maxValue = 300f;
            sensitivitySlider.onValueChanged.AddListener(OnSensitivitySliderChanged);
        }
        if (sensitivityInput) sensitivityInput.onEndEdit.AddListener(OnSensitivityInputChanged);
        if (invertYToggle) invertYToggle.onValueChanged.AddListener(v =>
        {
            PlayerPrefs.SetInt("Settings_InvertYAxis", v ? 1 : 0);
            if (AudioManager.Instance != null && settingsPanel.activeSelf) AudioManager.Instance.PlayUI(AudioID.UI_Hover);
        });

        if (subtitlesToggle) subtitlesToggle.onValueChanged.AddListener(v => { PlayerPrefs.SetInt("Settings_Subtitles", v ? 1 : 0); });
        if (subtitleSizeDropdown) subtitleSizeDropdown.onValueChanged.AddListener(v => { PlayerPrefs.SetInt("Settings_SubtitleSize", Mathf.Clamp(v, 0, 2)); });

        if (colorblindToggle) colorblindToggle.onValueChanged.AddListener(v => { PlayerPrefs.SetInt("Settings_Colorblind", v ? 1 : 0); });
        if (lowHpVignetteToggle) lowHpVignetteToggle.onValueChanged.AddListener(v => { PlayerPrefs.SetInt("Settings_LowHpVignette", v ? 1 : 0); });
        if (hitStopToggle) hitStopToggle.onValueChanged.AddListener(v => { PlayerPrefs.SetInt("Settings_HitStop", v ? 1 : 0); });

        if (qualityDropdown) qualityDropdown.onValueChanged.AddListener(v =>
        {
            QualitySettings.SetQualityLevel(Mathf.Clamp(v, 0, QualitySettings.names.Length - 1), true);
            PlayerPrefs.SetInt("Settings_QualityLevel", v);
        });
        if (renderScaleSlider)
        {
            renderScaleSlider.minValue = 70f;
            renderScaleSlider.maxValue = 100f;
            renderScaleSlider.onValueChanged.AddListener(OnRenderScaleSliderChanged);
        }
        if (renderScaleInput) renderScaleInput.onEndEdit.AddListener(OnRenderScaleInputChanged);
        if (postFXToggle) postFXToggle.onValueChanged.AddListener(v => { PlayerPrefs.SetInt("Settings_PostFX", v ? 1 : 0); });
        if (dynamicShadowsToggle) dynamicShadowsToggle.onValueChanged.AddListener(v => { PlayerPrefs.SetInt("Settings_DynamicShadows", v ? 1 : 0); });

        if (languageDropdown) languageDropdown.onValueChanged.AddListener(v =>
        {
            // Apply immediately via LocalizationManager (its setter re-clamps
            // to MAX_SHIPPED_LANGUAGE — half-translated locales aren't
            // reachable until their coverage is complete).
            LocalizationManager.CurrentLanguage = v;
            StartCoroutine(RebuildSettingsLayout());
        });

        if (voiceLanguageDropdown) voiceLanguageDropdown.onValueChanged.AddListener(v =>
        {
            PlayerPrefs.SetInt("Settings_VoiceLanguage", Mathf.Clamp(v, 0, 1));
        });

        PopulateResolutionDropdown();
        PopulateRefreshRateDropdown();
        PopulateMonitorDropdown();
        PopulateWindowModeDropdown();
        PopulateFpsCapDropdown();

        if (resolutionDropdown) resolutionDropdown.onValueChanged.AddListener(idx => { PlayerPrefs.SetInt("Settings_ResolutionIndex", idx); SettingsApplier.ApplyResolution(); });
        if (windowModeDropdown) windowModeDropdown.onValueChanged.AddListener(idx => { PlayerPrefs.SetInt("Settings_WindowMode", Mathf.Clamp(idx, 0, 2)); SettingsApplier.ApplyResolution(); });
        if (refreshRateDropdown) refreshRateDropdown.onValueChanged.AddListener(idx => { PlayerPrefs.SetInt("Settings_RefreshRateIndex", idx); SettingsApplier.ApplyResolution(); });
        if (monitorDropdown) monitorDropdown.onValueChanged.AddListener(idx => { PlayerPrefs.SetInt("Settings_Monitor", idx); SettingsApplier.ApplyMonitor(); });
        if (vsyncToggle) vsyncToggle.onValueChanged.AddListener(v => { PlayerPrefs.SetInt("Settings_VSync", v ? 1 : 0); QualitySettings.vSyncCount = v ? 1 : 0; });
        if (fpsCapDropdown) fpsCapDropdown.onValueChanged.AddListener(idx => { PlayerPrefs.SetInt("Settings_FpsCapIndex", idx); SettingsApplier.ApplyFpsCap(); });

        if (fovSlider)
        {
            fovSlider.minValue = 60f;
            fovSlider.maxValue = 110f;
            fovSlider.onValueChanged.AddListener(v =>
            {
                if (fovInput) fovInput.text = Mathf.RoundToInt(v).ToString();
                PlayerPrefs.SetFloat("Settings_FOV", v);
                SettingsApplier.ApplyFOV();
            });
        }
        if (fovInput) fovInput.onEndEdit.AddListener(t => SyncSliderFromInput(t, fovSlider, fovInput));

        if (brightnessSlider)
        {
            brightnessSlider.minValue = 50f;
            brightnessSlider.maxValue = 150f;
            brightnessSlider.onValueChanged.AddListener(v =>
            {
                if (brightnessInput) brightnessInput.text = Mathf.RoundToInt(v).ToString();
                PlayerPrefs.SetFloat("Settings_Brightness", v / 100f);
                SettingsApplier.ApplyBrightness();
            });
        }
        if (brightnessInput) brightnessInput.onEndEdit.AddListener(t => SyncSliderFromInput(t, brightnessSlider, brightnessInput));

        if (gammaSlider)
        {
            gammaSlider.minValue = 50f;
            gammaSlider.maxValue = 150f;
            gammaSlider.onValueChanged.AddListener(v =>
            {
                if (gammaInput) gammaInput.text = Mathf.RoundToInt(v).ToString();
                PlayerPrefs.SetFloat("Settings_Gamma", v / 100f);
                SettingsApplier.ApplyGamma();
            });
        }
        if (gammaInput) gammaInput.onEndEdit.AddListener(t => SyncSliderFromInput(t, gammaSlider, gammaInput));

        if (antiAliasingDropdown) antiAliasingDropdown.onValueChanged.AddListener(idx => { PlayerPrefs.SetInt("Settings_AntiAliasing", Mathf.Clamp(idx, 0, 3)); SettingsApplier.ApplyAntiAliasing(); });
        if (textureQualityDropdown) textureQualityDropdown.onValueChanged.AddListener(idx => { PlayerPrefs.SetInt("Settings_TextureQuality", Mathf.Clamp(idx, 0, 3)); SettingsApplier.ApplyTextureQuality(); });
        if (shadowQualityDropdown) shadowQualityDropdown.onValueChanged.AddListener(idx => { PlayerPrefs.SetInt("Settings_ShadowQuality", Mathf.Clamp(idx, 0, 3)); SettingsApplier.ApplyShadowQuality(); });

        if (shadowDistanceSlider)
        {
            shadowDistanceSlider.minValue = 10f;
            shadowDistanceSlider.maxValue = 150f;
            shadowDistanceSlider.onValueChanged.AddListener(v => { PlayerPrefs.SetFloat("Settings_ShadowDistance", v); QualitySettings.shadowDistance = v; });
        }

        WireToggle(motionBlurToggle, "Settings_MotionBlur", SettingsApplier.ApplyMotionBlur);
        WireToggle(depthOfFieldToggle, "Settings_DepthOfField", SettingsApplier.ApplyDepthOfField);
        WireToggle(bloomToggle, "Settings_Bloom", SettingsApplier.ApplyBloom);
        WireToggle(ambientOcclusionToggle, "Settings_AO", SettingsApplier.ApplyAmbientOcclusion);
        WireToggle(volumetricsToggle, "Settings_Volumetrics", SettingsApplier.ApplyVolumetrics);

        WireVolumeSlider(voiceSlider, voiceInput, "Settings_VoiceVol", v => { if (AudioManager.Instance != null) AudioManager.Instance.SetVoiceVolume(v / 100f); });
        WireVolumeSlider(uiSlider, uiInput, "Settings_UIVol", v => { if (AudioManager.Instance != null) AudioManager.Instance.SetUIVolume(v / 100f); });
        WireVolumeSlider(ambientSlider, ambientInput, "Settings_AmbientVol", v => { if (AudioManager.Instance != null) AudioManager.Instance.SetAmbientVolume(v / 100f); });
        WireToggle(muteWhenUnfocusedToggle, "Settings_MuteWhenUnfocused", null);

        if (difficultyDropdown) difficultyDropdown.onValueChanged.AddListener(idx => { PlayerPrefs.SetInt("Settings_Difficulty", Mathf.Clamp(idx, 0, 3)); });
        WireToggle(tutorialHintsToggle, "Settings_TutorialHints", null);
        WireToggle(holdToggleSprintToggle, "Settings_HoldToggleSprint", null);
        WireToggle(autoSaveToggle, "Settings_AutoSave", null);

        if (uiScaleSlider)
        {
            uiScaleSlider.minValue = 80f;
            uiScaleSlider.maxValue = 150f;
            uiScaleSlider.onValueChanged.AddListener(v =>
            {
                if (uiScaleInput) uiScaleInput.text = Mathf.RoundToInt(v).ToString();
                PlayerPrefs.SetFloat("Settings_UIScale", v / 100f);
                SettingsApplier.ApplyUIScale();
            });
        }
        if (uiScaleInput) uiScaleInput.onEndEdit.AddListener(t => SyncSliderFromInput(t, uiScaleSlider, uiScaleInput));
        WireToggle(subtitleBackgroundToggle, "Settings_SubtitleBg", null);
        WireToggle(highContrastToggle, "Settings_HighContrast", null);
        WireToggle(reduceMotionToggle, "Settings_ReduceMotion", null);
        WireToggle(photosensitivityToggle, "Settings_Photosensitivity", null);

        WireToggle(controllerVibrationToggle, "Settings_ControllerVibration", null);
        if (aimAssistSlider)
        {
            aimAssistSlider.minValue = 0f;
            aimAssistSlider.maxValue = 100f;
            aimAssistSlider.onValueChanged.AddListener(v =>
            {
                if (aimAssistInput) aimAssistInput.text = Mathf.RoundToInt(v).ToString();
                PlayerPrefs.SetFloat("Settings_AimAssist", v / 100f);
            });
        }
        if (aimAssistInput) aimAssistInput.onEndEdit.AddListener(t => SyncSliderFromInput(t, aimAssistSlider, aimAssistInput));

        if (closeButton != null) closeButton.gameObject.AddComponent<AutoButtonAnimator>().Setup(closeButtonText, true);

        if (saveButton != null)
        {
            saveButton.gameObject.AddComponent<AutoButtonAnimator>().Setup(null, false);
            saveButton.onClick.RemoveAllListeners();
            saveButton.onClick.AddListener(CloseSettings);
        }
    }

    private IEnumerator RebuildSettingsLayout()
    {
        yield return new WaitForEndOfFrame();
        if (settingsPanel != null)
        {
            LayoutRebuilder.ForceRebuildLayoutImmediate(settingsPanel.GetComponent<RectTransform>());
        }
    }

    private void Update()
    {
        if (settingsPanel != null && settingsPanel.activeInHierarchy)
        {
            AnimateCheckmark(damageCheckmark, damagePopupsToggle != null && damagePopupsToggle.isOn);
            AnimateCheckmark(screenShakeCheckmark, screenShakeToggle != null && screenShakeToggle.isOn);
            AnimateCheckmark(fpsLimitCheckmark, limitFPSToggle != null && limitFPSToggle.isOn);
            AnimateCheckmark(showFPSCheckmark, showFPSToggle != null && showFPSToggle.isOn);

            AnimateCheckmark(invertYCheckmark, invertYToggle != null && invertYToggle.isOn);
            AnimateCheckmark(subtitlesCheckmark, subtitlesToggle != null && subtitlesToggle.isOn);
            AnimateCheckmark(colorblindCheckmark, colorblindToggle != null && colorblindToggle.isOn);
            AnimateCheckmark(lowHpVignetteCheckmark, lowHpVignetteToggle != null && lowHpVignetteToggle.isOn);
            AnimateCheckmark(hitStopCheckmark, hitStopToggle != null && hitStopToggle.isOn);
            AnimateCheckmark(postFXCheckmark, postFXToggle != null && postFXToggle.isOn);
            AnimateCheckmark(dynamicShadowsCheckmark, dynamicShadowsToggle != null && dynamicShadowsToggle.isOn);

            if (underline != null && tabTexts != null && tabTexts.Length > 0)
            {
                Vector3 localPos = underline.localPosition;
                localPos.x = Mathf.Lerp(localPos.x, targetLineLocalX, Time.unscaledDeltaTime * 15f);
                underline.localPosition = localPos;

                for (int i = 0; i < tabTexts.Length; i++)
                {
                    if (tabTexts[i] != null)
                    {
                        Color c = tabTexts[i].color;
                        float targetAlpha = (i == currentTabIndex) ? 0.7f : 1f;
                        c.a = Mathf.Lerp(c.a, targetAlpha, Time.unscaledDeltaTime * 15f);
                        tabTexts[i].color = c;
                    }
                }
            }
        }
    }

    public void SelectTab(int index)
    {
        if (tabTexts == null || index < 0 || index >= tabTexts.Length) return;

        // Play unconditionally — a tab is only clickable while settings are open,
        // and the old activeSelf guard could suppress the click sound.
        if (AudioManager.Instance != null)
            AudioManager.Instance.PlayUI(AudioID.UI_Click);

        currentTabIndex = index;
        Vector3 targetWorldPos = tabTexts[index].rectTransform.position;
        Vector3 targetLocalPos = underline.parent.InverseTransformPoint(targetWorldPos);
        targetLineLocalX = targetLocalPos.x;

        if (tabPanels != null)
        {
            for (int i = 0; i < tabPanels.Length; i++)
            {
                if (tabPanels[i] != null)
                    tabPanels[i].SetActive(i == index);
            }
        }
    }

    private void AnimateCheckmark(Graphic checkmark, bool isOn)
    {
        if (checkmark == null) return;
        float targetAlpha = isOn ? 1f : 0f;
        Vector3 targetScale = isOn ? Vector3.one : Vector3.zero;
        float speed = 18f * Time.unscaledDeltaTime;

        checkmark.rectTransform.localScale = Vector3.Lerp(checkmark.rectTransform.localScale, targetScale, speed);
        Color c = checkmark.color;
        c.a = Mathf.Lerp(c.a, targetAlpha, speed);
        checkmark.color = c;
    }

    public void OpenSettings()
    {
        if (AudioManager.Instance != null) AudioManager.Instance.PlayUI(AudioID.UI_Open);

        settingsPanel.SetActive(true);
        settingsPanel.transform.SetAsLastSibling();

        if (tabTexts != null && tabTexts.Length > 0 && tabPanels != null && tabPanels.Length > 0)
        {
            currentTabIndex = 0;
            if (tabTexts[0] != null && underline != null && underline.parent != null)
            {
                Vector3 targetWorldPos = tabTexts[0].rectTransform.position;
                Vector3 targetLocalPos = underline.parent.InverseTransformPoint(targetWorldPos);
                targetLineLocalX = targetLocalPos.x;
                underline.localPosition = new Vector3(targetLineLocalX, underline.localPosition.y, underline.localPosition.z);
            }
            for (int i = 0; i < tabPanels.Length; i++) if (tabPanels[i] != null) tabPanels[i].SetActive(i == 0);
            for (int i = 0; i < tabTexts.Length; i++)
            {
                if (tabTexts[i] != null)
                {
                    Color c = tabTexts[i].color;
                    c.a = (i == 0) ? 0.7f : 1f;
                    tabTexts[i].color = c;
                }
            }
        }

        if (panelAnimCoroutine != null) StopCoroutine(panelAnimCoroutine);
        panelAnimCoroutine = StartCoroutine(AnimatePanelIn());

        Cursor.visible = true;
        Cursor.lockState = CursorLockMode.None;

        if (masterSlider) masterSlider.value = PlayerPrefs.GetFloat("Settings_MasterVol", 100f);
        if (musicSlider) musicSlider.value = PlayerPrefs.GetFloat("Settings_MusicVol", 100f);
        if (sfxSlider) sfxSlider.value = PlayerPrefs.GetFloat("Settings_SFXVol", 100f);

        if (masterInput && masterSlider) masterInput.text = masterSlider.value.ToString("0");
        if (musicInput && musicSlider) musicInput.text = musicSlider.value.ToString("0");
        if (sfxInput && sfxSlider) sfxInput.text = sfxSlider.value.ToString("0");

        if (damagePopupsToggle) damagePopupsToggle.isOn = PlayerPrefs.GetInt("Settings_DamagePopups", 1) == 1;
        if (screenShakeToggle) screenShakeToggle.isOn = PlayerPrefs.GetInt("Settings_ScreenShake", 1) == 1;
        if (limitFPSToggle) limitFPSToggle.isOn = PlayerPrefs.GetInt("Settings_FPSLimit", 0) == 1;
        if (showFPSToggle) showFPSToggle.isOn = PlayerPrefs.GetInt("Settings_ShowFPS", 0) == 1;

        ForceCheckmarkState(damageCheckmark, damagePopupsToggle.isOn);
        ForceCheckmarkState(screenShakeCheckmark, screenShakeToggle.isOn);
        ForceCheckmarkState(fpsLimitCheckmark, limitFPSToggle.isOn);
        ForceCheckmarkState(showFPSCheckmark, showFPSToggle.isOn);

        if (sensitivitySlider) sensitivitySlider.value = PlayerPrefs.GetFloat("Settings_MouseSensitivity", 1f) * 100f;
        if (sensitivityInput && sensitivitySlider) sensitivityInput.text = Mathf.RoundToInt(sensitivitySlider.value).ToString();
        if (invertYToggle) invertYToggle.isOn = PlayerPrefs.GetInt("Settings_InvertYAxis", 0) == 1;
        ForceCheckmarkState(invertYCheckmark, invertYToggle != null && invertYToggle.isOn);

        if (subtitlesToggle) subtitlesToggle.isOn = PlayerPrefs.GetInt("Settings_Subtitles", 1) == 1;
        ForceCheckmarkState(subtitlesCheckmark, subtitlesToggle != null && subtitlesToggle.isOn);
        if (subtitleSizeDropdown) subtitleSizeDropdown.value = Mathf.Clamp(PlayerPrefs.GetInt("Settings_SubtitleSize", 1), 0, 2);

        if (colorblindToggle) colorblindToggle.isOn = PlayerPrefs.GetInt("Settings_Colorblind", 0) == 1;
        ForceCheckmarkState(colorblindCheckmark, colorblindToggle != null && colorblindToggle.isOn);
        if (lowHpVignetteToggle) lowHpVignetteToggle.isOn = PlayerPrefs.GetInt("Settings_LowHpVignette", 1) == 1;
        ForceCheckmarkState(lowHpVignetteCheckmark, lowHpVignetteToggle != null && lowHpVignetteToggle.isOn);
        if (hitStopToggle) hitStopToggle.isOn = PlayerPrefs.GetInt("Settings_HitStop", 1) == 1;
        ForceCheckmarkState(hitStopCheckmark, hitStopToggle != null && hitStopToggle.isOn);

        if (qualityDropdown)
        {
            int saved = PlayerPrefs.GetInt("Settings_QualityLevel", Mathf.Clamp(QualitySettings.GetQualityLevel(), 0, qualityDropdown.options.Count - 1));
            qualityDropdown.value = Mathf.Clamp(saved, 0, qualityDropdown.options.Count - 1);
        }
        if (renderScaleSlider) renderScaleSlider.value = PlayerPrefs.GetFloat("Settings_RenderScale", 1f) * 100f;
        if (renderScaleInput && renderScaleSlider) renderScaleInput.text = Mathf.RoundToInt(renderScaleSlider.value).ToString();
        if (postFXToggle) postFXToggle.isOn = PlayerPrefs.GetInt("Settings_PostFX", 1) == 1;
        ForceCheckmarkState(postFXCheckmark, postFXToggle != null && postFXToggle.isOn);
        if (dynamicShadowsToggle) dynamicShadowsToggle.isOn = PlayerPrefs.GetInt("Settings_DynamicShadows", 1) == 1;
        ForceCheckmarkState(dynamicShadowsCheckmark, dynamicShadowsToggle != null && dynamicShadowsToggle.isOn);

        EnsureFullLanguageOptions(languageDropdown);
        EnsureFullLanguageOptions(voiceLanguageDropdown);
        if (languageDropdown) languageDropdown.value = Mathf.Clamp(PlayerPrefs.GetInt("Settings_Language", 0), 0, languageDropdown.options.Count - 1);
        if (voiceLanguageDropdown) voiceLanguageDropdown.value = Mathf.Clamp(PlayerPrefs.GetInt("Settings_VoiceLanguage", 0), 0, voiceLanguageDropdown.options.Count - 1);

        if (resolutionDropdown && resolutionDropdown.options.Count > 0)
            resolutionDropdown.value = Mathf.Clamp(PlayerPrefs.GetInt("Settings_ResolutionIndex", DefaultResolutionIndex()), 0, resolutionDropdown.options.Count - 1);
        if (windowModeDropdown)
            windowModeDropdown.value = Mathf.Clamp(PlayerPrefs.GetInt("Settings_WindowMode", 0), 0, 2);
        if (refreshRateDropdown && refreshRateDropdown.options.Count > 0)
            refreshRateDropdown.value = Mathf.Clamp(PlayerPrefs.GetInt("Settings_RefreshRateIndex", 0), 0, refreshRateDropdown.options.Count - 1);
        if (monitorDropdown && monitorDropdown.options.Count > 0)
            monitorDropdown.value = Mathf.Clamp(PlayerPrefs.GetInt("Settings_Monitor", 0), 0, monitorDropdown.options.Count - 1);
        if (vsyncToggle) vsyncToggle.isOn = PlayerPrefs.GetInt("Settings_VSync", 0) == 1;
        ForceCheckmarkState(vsyncCheckmark, vsyncToggle != null && vsyncToggle.isOn);
        if (fpsCapDropdown && fpsCapDropdown.options.Count > 0)
            fpsCapDropdown.value = Mathf.Clamp(PlayerPrefs.GetInt("Settings_FpsCapIndex", 2), 0, fpsCapDropdown.options.Count - 1);

        if (fovSlider) fovSlider.SetValueWithoutNotify(PlayerPrefs.GetFloat("Settings_FOV", 75f));
        if (fovInput && fovSlider) fovInput.text = Mathf.RoundToInt(fovSlider.value).ToString();

        if (brightnessSlider) brightnessSlider.SetValueWithoutNotify(PlayerPrefs.GetFloat("Settings_Brightness", 1f) * 100f);
        if (brightnessInput && brightnessSlider) brightnessInput.text = Mathf.RoundToInt(brightnessSlider.value).ToString();

        if (gammaSlider) gammaSlider.SetValueWithoutNotify(PlayerPrefs.GetFloat("Settings_Gamma", 1f) * 100f);
        if (gammaInput && gammaSlider) gammaInput.text = Mathf.RoundToInt(gammaSlider.value).ToString();

        if (antiAliasingDropdown && antiAliasingDropdown.options.Count > 0)
            antiAliasingDropdown.value = Mathf.Clamp(PlayerPrefs.GetInt("Settings_AntiAliasing", 1), 0, antiAliasingDropdown.options.Count - 1);
        if (textureQualityDropdown && textureQualityDropdown.options.Count > 0)
            textureQualityDropdown.value = Mathf.Clamp(PlayerPrefs.GetInt("Settings_TextureQuality", 2), 0, textureQualityDropdown.options.Count - 1);
        if (shadowQualityDropdown && shadowQualityDropdown.options.Count > 0)
            shadowQualityDropdown.value = Mathf.Clamp(PlayerPrefs.GetInt("Settings_ShadowQuality", 2), 0, shadowQualityDropdown.options.Count - 1);
        if (shadowDistanceSlider) shadowDistanceSlider.value = PlayerPrefs.GetFloat("Settings_ShadowDistance", 50f);
        if (motionBlurToggle) motionBlurToggle.isOn = PlayerPrefs.GetInt("Settings_MotionBlur", 0) == 1;
        ForceCheckmarkState(motionBlurCheckmark, motionBlurToggle != null && motionBlurToggle.isOn);
        if (depthOfFieldToggle) depthOfFieldToggle.isOn = PlayerPrefs.GetInt("Settings_DepthOfField", 0) == 1;
        ForceCheckmarkState(depthOfFieldCheckmark, depthOfFieldToggle != null && depthOfFieldToggle.isOn);
        if (bloomToggle) bloomToggle.isOn = PlayerPrefs.GetInt("Settings_Bloom", 1) == 1;
        ForceCheckmarkState(bloomCheckmark, bloomToggle != null && bloomToggle.isOn);
        if (ambientOcclusionToggle) ambientOcclusionToggle.isOn = PlayerPrefs.GetInt("Settings_AO", 1) == 1;
        ForceCheckmarkState(ambientOcclusionCheckmark, ambientOcclusionToggle != null && ambientOcclusionToggle.isOn);
        if (volumetricsToggle) volumetricsToggle.isOn = PlayerPrefs.GetInt("Settings_Volumetrics", 0) == 1;
        ForceCheckmarkState(volumetricsCheckmark, volumetricsToggle != null && volumetricsToggle.isOn);

        if (voiceSlider) voiceSlider.value = PlayerPrefs.GetFloat("Settings_VoiceVol", 100f);
        if (voiceInput && voiceSlider) voiceInput.text = Mathf.RoundToInt(voiceSlider.value).ToString();
        if (uiSlider) uiSlider.value = PlayerPrefs.GetFloat("Settings_UIVol", 100f);
        if (uiInput && uiSlider) uiInput.text = Mathf.RoundToInt(uiSlider.value).ToString();
        if (ambientSlider) ambientSlider.value = PlayerPrefs.GetFloat("Settings_AmbientVol", 100f);
        if (ambientInput && ambientSlider) ambientInput.text = Mathf.RoundToInt(ambientSlider.value).ToString();
        if (muteWhenUnfocusedToggle) muteWhenUnfocusedToggle.isOn = PlayerPrefs.GetInt("Settings_MuteWhenUnfocused", 1) == 1;
        ForceCheckmarkState(muteWhenUnfocusedCheckmark, muteWhenUnfocusedToggle != null && muteWhenUnfocusedToggle.isOn);

        if (difficultyDropdown && difficultyDropdown.options.Count > 0)
            difficultyDropdown.value = Mathf.Clamp(PlayerPrefs.GetInt("Settings_Difficulty", 1), 0, difficultyDropdown.options.Count - 1);
        if (tutorialHintsToggle) tutorialHintsToggle.isOn = PlayerPrefs.GetInt("Settings_TutorialHints", 1) == 1;
        ForceCheckmarkState(tutorialHintsCheckmark, tutorialHintsToggle != null && tutorialHintsToggle.isOn);
        if (holdToggleSprintToggle) holdToggleSprintToggle.isOn = PlayerPrefs.GetInt("Settings_HoldToggleSprint", 0) == 1;
        ForceCheckmarkState(holdToggleSprintCheckmark, holdToggleSprintToggle != null && holdToggleSprintToggle.isOn);
        if (autoSaveToggle) autoSaveToggle.isOn = PlayerPrefs.GetInt("Settings_AutoSave", 1) == 1;
        ForceCheckmarkState(autoSaveCheckmark, autoSaveToggle != null && autoSaveToggle.isOn);

        if (uiScaleSlider) uiScaleSlider.value = PlayerPrefs.GetFloat("Settings_UIScale", 1f) * 100f;
        if (uiScaleInput && uiScaleSlider) uiScaleInput.text = Mathf.RoundToInt(uiScaleSlider.value).ToString();
        if (subtitleBackgroundToggle) subtitleBackgroundToggle.isOn = PlayerPrefs.GetInt("Settings_SubtitleBg", 1) == 1;
        ForceCheckmarkState(subtitleBackgroundCheckmark, subtitleBackgroundToggle != null && subtitleBackgroundToggle.isOn);
        if (highContrastToggle) highContrastToggle.isOn = PlayerPrefs.GetInt("Settings_HighContrast", 0) == 1;
        ForceCheckmarkState(highContrastCheckmark, highContrastToggle != null && highContrastToggle.isOn);
        if (reduceMotionToggle) reduceMotionToggle.isOn = PlayerPrefs.GetInt("Settings_ReduceMotion", 0) == 1;
        ForceCheckmarkState(reduceMotionCheckmark, reduceMotionToggle != null && reduceMotionToggle.isOn);
        if (photosensitivityToggle) photosensitivityToggle.isOn = PlayerPrefs.GetInt("Settings_Photosensitivity", 0) == 1;
        ForceCheckmarkState(photosensitivityCheckmark, photosensitivityToggle != null && photosensitivityToggle.isOn);

        if (controllerVibrationToggle) controllerVibrationToggle.isOn = PlayerPrefs.GetInt("Settings_ControllerVibration", 1) == 1;
        ForceCheckmarkState(controllerVibrationCheckmark, controllerVibrationToggle != null && controllerVibrationToggle.isOn);
        if (aimAssistSlider) aimAssistSlider.value = PlayerPrefs.GetFloat("Settings_AimAssist", 0.4f) * 100f;
        if (aimAssistInput && aimAssistSlider) aimAssistInput.text = Mathf.RoundToInt(aimAssistSlider.value).ToString();

        if (saveButton != null)
        {
            TextMeshProUGUI btnText = saveButton.GetComponentInChildren<TextMeshProUGUI>();
            if (btnText != null) btnText.text = LocalizationManager.Tr("SAVE & CLOSE");
        }

        if (panelAnimCoroutine != null) StopCoroutine(panelAnimCoroutine);
        panelAnimCoroutine = StartCoroutine(AnimatePanelIn());
    }

    public void CloseSettings()
    {
        if (AudioManager.Instance != null) AudioManager.Instance.PlayUI(AudioID.UI_Back);

        if (masterSlider) PlayerPrefs.SetFloat("Settings_MasterVol", masterSlider.value);
        if (musicSlider) PlayerPrefs.SetFloat("Settings_MusicVol", musicSlider.value);
        if (sfxSlider) PlayerPrefs.SetFloat("Settings_SFXVol", sfxSlider.value);

        if (damagePopupsToggle) PlayerPrefs.SetInt("Settings_DamagePopups", damagePopupsToggle.isOn ? 1 : 0);
        if (screenShakeToggle) PlayerPrefs.SetInt("Settings_ScreenShake", screenShakeToggle.isOn ? 1 : 0);
        if (limitFPSToggle) PlayerPrefs.SetInt("Settings_FPSLimit", limitFPSToggle.isOn ? 1 : 0);
        if (showFPSToggle) PlayerPrefs.SetInt("Settings_ShowFPS", showFPSToggle.isOn ? 1 : 0);

        if (sensitivitySlider) PlayerPrefs.SetFloat("Settings_MouseSensitivity", sensitivitySlider.value / 100f);
        if (invertYToggle) PlayerPrefs.SetInt("Settings_InvertYAxis", invertYToggle.isOn ? 1 : 0);
        if (subtitlesToggle) PlayerPrefs.SetInt("Settings_Subtitles", subtitlesToggle.isOn ? 1 : 0);
        if (subtitleSizeDropdown) PlayerPrefs.SetInt("Settings_SubtitleSize", subtitleSizeDropdown.value);
        if (colorblindToggle) PlayerPrefs.SetInt("Settings_Colorblind", colorblindToggle.isOn ? 1 : 0);
        if (lowHpVignetteToggle) PlayerPrefs.SetInt("Settings_LowHpVignette", lowHpVignetteToggle.isOn ? 1 : 0);
        if (hitStopToggle) PlayerPrefs.SetInt("Settings_HitStop", hitStopToggle.isOn ? 1 : 0);
        if (qualityDropdown) PlayerPrefs.SetInt("Settings_QualityLevel", qualityDropdown.value);
        if (renderScaleSlider) PlayerPrefs.SetFloat("Settings_RenderScale", renderScaleSlider.value / 100f);
        if (postFXToggle) PlayerPrefs.SetInt("Settings_PostFX", postFXToggle.isOn ? 1 : 0);
        if (dynamicShadowsToggle) PlayerPrefs.SetInt("Settings_DynamicShadows", dynamicShadowsToggle.isOn ? 1 : 0);
        if (languageDropdown) PlayerPrefs.SetInt("Settings_Language", languageDropdown.value);
        if (voiceLanguageDropdown) PlayerPrefs.SetInt("Settings_VoiceLanguage", voiceLanguageDropdown.value);

        if (resolutionDropdown) PlayerPrefs.SetInt("Settings_ResolutionIndex", resolutionDropdown.value);
        if (windowModeDropdown) PlayerPrefs.SetInt("Settings_WindowMode", windowModeDropdown.value);
        if (refreshRateDropdown) PlayerPrefs.SetInt("Settings_RefreshRateIndex", refreshRateDropdown.value);
        if (monitorDropdown) PlayerPrefs.SetInt("Settings_Monitor", monitorDropdown.value);
        if (vsyncToggle) PlayerPrefs.SetInt("Settings_VSync", vsyncToggle.isOn ? 1 : 0);
        if (fpsCapDropdown) PlayerPrefs.SetInt("Settings_FpsCapIndex", fpsCapDropdown.value);

        if (fovSlider) PlayerPrefs.SetFloat("Settings_FOV", fovSlider.value);
        if (brightnessSlider) PlayerPrefs.SetFloat("Settings_Brightness", brightnessSlider.value / 100f);
        if (gammaSlider) PlayerPrefs.SetFloat("Settings_Gamma", gammaSlider.value / 100f);

        if (antiAliasingDropdown) PlayerPrefs.SetInt("Settings_AntiAliasing", antiAliasingDropdown.value);
        if (textureQualityDropdown) PlayerPrefs.SetInt("Settings_TextureQuality", textureQualityDropdown.value);
        if (shadowQualityDropdown) PlayerPrefs.SetInt("Settings_ShadowQuality", shadowQualityDropdown.value);
        if (shadowDistanceSlider) PlayerPrefs.SetFloat("Settings_ShadowDistance", shadowDistanceSlider.value);
        if (motionBlurToggle) PlayerPrefs.SetInt("Settings_MotionBlur", motionBlurToggle.isOn ? 1 : 0);
        if (depthOfFieldToggle) PlayerPrefs.SetInt("Settings_DepthOfField", depthOfFieldToggle.isOn ? 1 : 0);
        if (bloomToggle) PlayerPrefs.SetInt("Settings_Bloom", bloomToggle.isOn ? 1 : 0);
        if (ambientOcclusionToggle) PlayerPrefs.SetInt("Settings_AO", ambientOcclusionToggle.isOn ? 1 : 0);
        if (volumetricsToggle) PlayerPrefs.SetInt("Settings_Volumetrics", volumetricsToggle.isOn ? 1 : 0);

        if (voiceSlider) PlayerPrefs.SetFloat("Settings_VoiceVol", voiceSlider.value);
        if (uiSlider) PlayerPrefs.SetFloat("Settings_UIVol", uiSlider.value);
        if (ambientSlider) PlayerPrefs.SetFloat("Settings_AmbientVol", ambientSlider.value);
        if (muteWhenUnfocusedToggle) PlayerPrefs.SetInt("Settings_MuteWhenUnfocused", muteWhenUnfocusedToggle.isOn ? 1 : 0);

        if (difficultyDropdown) PlayerPrefs.SetInt("Settings_Difficulty", difficultyDropdown.value);
        if (tutorialHintsToggle) PlayerPrefs.SetInt("Settings_TutorialHints", tutorialHintsToggle.isOn ? 1 : 0);
        if (holdToggleSprintToggle) PlayerPrefs.SetInt("Settings_HoldToggleSprint", holdToggleSprintToggle.isOn ? 1 : 0);
        if (autoSaveToggle) PlayerPrefs.SetInt("Settings_AutoSave", autoSaveToggle.isOn ? 1 : 0);

        if (uiScaleSlider) PlayerPrefs.SetFloat("Settings_UIScale", uiScaleSlider.value / 100f);
        if (subtitleBackgroundToggle) PlayerPrefs.SetInt("Settings_SubtitleBg", subtitleBackgroundToggle.isOn ? 1 : 0);
        if (highContrastToggle) PlayerPrefs.SetInt("Settings_HighContrast", highContrastToggle.isOn ? 1 : 0);
        if (reduceMotionToggle) PlayerPrefs.SetInt("Settings_ReduceMotion", reduceMotionToggle.isOn ? 1 : 0);
        if (photosensitivityToggle) PlayerPrefs.SetInt("Settings_Photosensitivity", photosensitivityToggle.isOn ? 1 : 0);

        if (controllerVibrationToggle) PlayerPrefs.SetInt("Settings_ControllerVibration", controllerVibrationToggle.isOn ? 1 : 0);
        if (aimAssistSlider) PlayerPrefs.SetFloat("Settings_AimAssist", aimAssistSlider.value / 100f);

        PlayerPrefs.Save();

        bool isLimited = limitFPSToggle != null && limitFPSToggle.isOn;
        ApplyFpsLimit(isLimited);

        if (FPSDisplay.Instance != null)
        {
            FPSDisplay.Instance.UpdateVisibility();
        }
        else
        {
            FPSDisplay fps = FindFirstObjectByType<FPSDisplay>();
            if (fps != null) fps.UpdateVisibility();
        }

        if (panelAnimCoroutine != null) StopCoroutine(panelAnimCoroutine);
        panelAnimCoroutine = StartCoroutine(AnimatePanelOut());
    }

    private IEnumerator AnimatePanelIn()
    {
        if (panelCanvasGroup == null || panelRect == null) yield break;
        panelRect.localScale = Vector3.one;
        panelCanvasGroup.alpha = 0f;

        float t = 0f;
        while (t < 1f)
        {
            t += Time.unscaledDeltaTime * animationSpeed * 1.6f;
            float easeOutQuart = 1f - Mathf.Pow(1f - t, 4f);
            panelCanvasGroup.alpha = Mathf.Lerp(0f, 1f, easeOutQuart);
            yield return null;
        }
        panelCanvasGroup.alpha = 1f;
    }

    private IEnumerator AnimatePanelOut()
    {
        if (panelCanvasGroup == null || panelRect == null) { FinishClosing(); yield break; }
        panelRect.localScale = Vector3.one;
        float t = 0f;
        while (t < 1f)
        {
            t += Time.unscaledDeltaTime * (animationSpeed * 2f);
            float easeInQuad = t * t;
            panelCanvasGroup.alpha = Mathf.Lerp(1f, 0f, easeInQuad);
            yield return null;
        }
        FinishClosing();
    }

    private void FinishClosing()
    {
        settingsPanel.SetActive(false);

        string currentScene = SceneManager.GetActiveScene().name;
        if (currentScene == "Menu") return;

        bool isPauseMenuOpen = GlobalHUD.Instance != null &&
                               GlobalHUD.Instance.pausePanelGroup != null &&
                               GlobalHUD.Instance.pausePanelGroup.gameObject.activeInHierarchy;

        if (isPauseMenuOpen)
        {
            Cursor.visible = true;
            Cursor.lockState = CursorLockMode.None;
        }
        else
        {
            if (currentScene != "ShopScene")
            {
                Cursor.visible = false;
                Cursor.lockState = CursorLockMode.Locked;
            }
        }
    }

    private void ForceCheckmarkState(Graphic checkmark, bool isOn)
    {
        if (checkmark == null) return;
        checkmark.rectTransform.localScale = isOn ? Vector3.one : Vector3.zero;
        Color c = checkmark.color;
        c.a = isOn ? 1f : 0f;
        checkmark.color = c;
    }

    // ФІКС: Зберігаємо гучність відразу, як тільки повзунок рухається
    private void OnMasterVolumeChanged(float value)
    {
        if (masterInput) masterInput.text = value.ToString("0");
        PlayerPrefs.SetFloat("Settings_MasterVol", value);
        PlayerPrefs.Save();
        if (AudioManager.Instance != null) AudioManager.Instance.SetMasterVolume(value / 100f);
    }

    private void OnMusicVolumeChanged(float value)
    {
        if (musicInput) musicInput.text = value.ToString("0");
        PlayerPrefs.SetFloat("Settings_MusicVol", value);
        PlayerPrefs.Save();
        if (AudioManager.Instance != null) AudioManager.Instance.SetMusicVolume(value / 100f);
    }

    private void OnSFXVolumeChanged(float value)
    {
        if (sfxInput) sfxInput.text = value.ToString("0");
        PlayerPrefs.SetFloat("Settings_SFXVol", value);
        PlayerPrefs.Save();
        if (AudioManager.Instance != null) AudioManager.Instance.SetSFXVolume(value / 100f);
    }

    private void OnMasterInputChanged(string text)
    {
        if (float.TryParse(text, out float result))
        {
            result = Mathf.Clamp(result, masterSlider.minValue, masterSlider.maxValue);
            masterSlider.value = result;
        }
        else { if (masterInput) masterInput.text = masterSlider.value.ToString("0"); }
    }

    private void OnMusicInputChanged(string text)
    {
        if (float.TryParse(text, out float result))
        {
            result = Mathf.Clamp(result, musicSlider.minValue, musicSlider.maxValue);
            musicSlider.value = result;
        }
        else { if (musicInput) musicInput.text = musicSlider.value.ToString("0"); }
    }

    private void OnSFXInputChanged(string text)
    {
        if (float.TryParse(text, out float result))
        {
            result = Mathf.Clamp(result, sfxSlider.minValue, sfxSlider.maxValue);
            sfxSlider.value = result;
        }
        else { if (sfxInput) sfxInput.text = sfxSlider.value.ToString("0"); }
    }

    private void OnDamagePopupsChanged(bool isOn) { if (AudioManager.Instance != null && settingsPanel.activeSelf) AudioManager.Instance.PlayUI(AudioID.UI_Toggle); }
    private void OnScreenShakeChanged(bool isOn) { if (AudioManager.Instance != null && settingsPanel.activeSelf) AudioManager.Instance.PlayUI(AudioID.UI_Toggle); }

    private void OnFPSLimitChanged(bool isOn)
    {
        if (AudioManager.Instance != null && settingsPanel.activeSelf) AudioManager.Instance.PlayUI(AudioID.UI_Toggle);
        ApplyFpsLimit(isOn);
    }

    public static void ApplyFpsLimit(bool isOn)
    {
        PlayerPrefs.SetInt("Settings_FPSLimit", isOn ? 1 : 0);
        if (isOn)
        {
            QualitySettings.vSyncCount = 0;
            int idx = PlayerPrefs.GetInt("Settings_FpsCapIndex", 2);
            int[] caps = { -1, 30, 60, 90, 120, 144, 240 };
            if (idx < 0 || idx >= caps.Length) idx = 2;
            Application.targetFrameRate = caps[idx];
        }
        else
        {
            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = -1;
        }
    }

    private void OnShowFPSChanged(bool isOn)
    {
        if (AudioManager.Instance != null && settingsPanel.activeSelf) AudioManager.Instance.PlayUI(AudioID.UI_Toggle);
        PlayerPrefs.SetInt("Settings_ShowFPS", isOn ? 1 : 0);
        if (FPSDisplay.Instance != null) FPSDisplay.Instance.UpdateVisibility();
    }

    private void OnSensitivitySliderChanged(float value)
    {
        if (sensitivityInput) sensitivityInput.text = Mathf.RoundToInt(value).ToString();
        PlayerPrefs.SetFloat("Settings_MouseSensitivity", value / 100f);
    }

    private void OnSensitivityInputChanged(string text)
    {
        if (float.TryParse(text, out float result))
        {
            result = Mathf.Clamp(result, sensitivitySlider.minValue, sensitivitySlider.maxValue);
            sensitivitySlider.value = result;
        }
        else if (sensitivityInput) sensitivityInput.text = Mathf.RoundToInt(sensitivitySlider.value).ToString();
    }

    private void OnRenderScaleSliderChanged(float value)
    {
        if (renderScaleInput) renderScaleInput.text = Mathf.RoundToInt(value).ToString();
        PlayerPrefs.SetFloat("Settings_RenderScale", value / 100f);
    }

    private void OnRenderScaleInputChanged(string text)
    {
        if (float.TryParse(text, out float result))
        {
            result = Mathf.Clamp(result, renderScaleSlider.minValue, renderScaleSlider.maxValue);
            renderScaleSlider.value = result;
        }
        else if (renderScaleInput) renderScaleInput.text = Mathf.RoundToInt(renderScaleSlider.value).ToString();
    }

    private void WireToggle(Toggle t, string key, System.Action onApply)
    {
        if (t == null) return;
        t.onValueChanged.AddListener(v =>
        {
            PlayerPrefs.SetInt(key, v ? 1 : 0);
            onApply?.Invoke();
        });
    }

    private void WireVolumeSlider(Slider s, TMP_InputField input, string key, System.Action<float> apply)
    {
        if (s == null) return;
        s.minValue = 0f; s.maxValue = 100f;
        s.onValueChanged.AddListener(v =>
        {
            if (input) input.text = Mathf.RoundToInt(v).ToString();
            PlayerPrefs.SetFloat(key, v);
            apply?.Invoke(v);
        });
        if (input) input.onEndEdit.AddListener(t => SyncSliderFromInput(t, s, input));
    }

    private void SyncSliderFromInput(string text, Slider s, TMP_InputField input)
    {
        if (s == null) return;
        if (float.TryParse(text, out float r))
        {
            r = Mathf.Clamp(r, s.minValue, s.maxValue);
            s.value = r;
        }
        else if (input) input.text = Mathf.RoundToInt(s.value).ToString();
    }

    private List<Resolution> cachedResolutions;

    private void PopulateResolutionDropdown()
    {
        if (resolutionDropdown == null) return;
        Resolution[] all = Screen.resolutions;
        cachedResolutions = new List<Resolution>();
        HashSet<(int, int)> seen = new HashSet<(int, int)>();
        foreach (var r in all)
        {
            if (seen.Add((r.width, r.height))) cachedResolutions.Add(r);
        }
        resolutionDropdown.ClearOptions();
        List<string> labels = new List<string>();
        foreach (var r in cachedResolutions) labels.Add($"{r.width} × {r.height}");
        resolutionDropdown.AddOptions(labels);
    }

    private int DefaultResolutionIndex()
    {
        if (cachedResolutions == null) return 0;
        for (int i = 0; i < cachedResolutions.Count; i++)
        {
            if (cachedResolutions[i].width == Screen.currentResolution.width &&
                cachedResolutions[i].height == Screen.currentResolution.height) return i;
        }
        return Mathf.Max(0, cachedResolutions.Count - 1);
    }

    private void PopulateRefreshRateDropdown()
    {
        if (refreshRateDropdown == null) return;
        HashSet<int> rates = new HashSet<int>();
        foreach (var r in Screen.resolutions) rates.Add((int)System.Math.Round(r.refreshRateRatio.value));
        List<int> list = new List<int>(rates);
        list.Sort();
        refreshRateDropdown.ClearOptions();
        List<string> labels = new List<string>();
        foreach (int hz in list) labels.Add(hz + " Hz");
        refreshRateDropdown.AddOptions(labels);
    }

    private void PopulateMonitorDropdown()
    {
        if (monitorDropdown == null) return;
        monitorDropdown.ClearOptions();
        List<string> labels = new List<string>();
        int count = Mathf.Max(1, Display.displays.Length);
        for (int i = 0; i < count; i++) labels.Add($"Display {i + 1}");
        monitorDropdown.AddOptions(labels);
    }

    private void PopulateWindowModeDropdown()
    {
        if (windowModeDropdown == null) return;
        windowModeDropdown.ClearOptions();
        windowModeDropdown.AddOptions(new List<string> { "Fullscreen", "Borderless", "Windowed" });
    }

    private void PopulateFpsCapDropdown()
    {
        if (fpsCapDropdown == null) return;
        fpsCapDropdown.ClearOptions();
        fpsCapDropdown.AddOptions(new List<string> { "Unlimited", "30", "60", "90", "120", "144", "240" });
    }

    private static readonly string[] s_allLanguageNames =
        { "English", "Українська", "Русский", "Español", "Deutsch", "Français", "Polski" };

    private static void EnsureFullLanguageOptions(TMP_Dropdown dd)
    {
        if (dd == null) return;
        for (int i = dd.options.Count; i < s_allLanguageNames.Length; i++)
            dd.options.Add(new TMP_Dropdown.OptionData(s_allLanguageNames[i]));
        dd.RefreshShownValue();
    }
}

public class AutoButtonAnimator : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerDownHandler, IPointerUpHandler
{
    private TextMeshProUGUI textToColor;
    private bool isCloseButton;
    private Vector3 targetScale = Vector3.one;
    private Color targetColor;
    private Color normalColor = new Color(1f, 0.84f, 0f, 0.8f);
    private Color hoverColor = new Color(1f, 0.26f, 0.26f, 1f);

    public void Setup(TextMeshProUGUI textRef, bool isCloseBtn)
    {
        textToColor = textRef;
        isCloseButton = isCloseBtn;
        targetColor = normalColor;
        if (textToColor != null) textToColor.color = normalColor;
    }

    private void Update()
    {
        transform.localScale = Vector3.Lerp(transform.localScale, targetScale, Time.unscaledDeltaTime * 15f);
        if (textToColor != null) textToColor.color = Color.Lerp(textToColor.color, targetColor, Time.unscaledDeltaTime * 15f);
    }

    public void OnPointerEnter(PointerEventData eventData) { if (AudioManager.Instance != null) AudioManager.Instance.PlayUI(AudioID.UI_Hover); targetScale = Vector3.one * 1.05f; if (isCloseButton) targetColor = hoverColor; }
    public void OnPointerExit(PointerEventData eventData) { targetScale = Vector3.one; if (isCloseButton) targetColor = normalColor; }
    public void OnPointerDown(PointerEventData eventData) { targetScale = Vector3.one * 0.95f; }
    public void OnPointerUp(PointerEventData eventData) { targetScale = Vector3.one * 1.05f; }
}