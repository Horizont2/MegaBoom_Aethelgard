using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using TMPro;
using System.Collections;

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

    [Header("Checkmark Graphics (��� ��������)")]
    public Graphic damageCheckmark;
    public Graphic screenShakeCheckmark;
    public Graphic fpsLimitCheckmark;
    public Graphic showFPSCheckmark;

    // ============================================================
    // NEW (extended Tier 3 / Tier 5 settings — wire from inspector)
    // Every reference below is optional; leave any blank to skip.
    // ============================================================

    [Header("Mouse / Camera")]
    [Tooltip("Slider with min=10, max=300 (10 %..300 % of base). Persisted as Settings_MouseSensitivity (0.10..3.0).")]
    public Slider sensitivitySlider;
    public TMP_InputField sensitivityInput;
    [Tooltip("Toggle that flips the camera vertical axis. Persisted as Settings_InvertYAxis.")]
    public Toggle invertYToggle;
    public Graphic invertYCheckmark;

    [Header("Subtitles")]
    public Toggle subtitlesToggle;
    public Graphic subtitlesCheckmark;
    [Tooltip("3-option dropdown (Small/Medium/Large). Persisted as Settings_SubtitleSize (0/1/2).")]
    public TMP_Dropdown subtitleSizeDropdown;

    [Header("Accessibility")]
    public Toggle colorblindToggle;
    public Graphic colorblindCheckmark;
    [Tooltip("Toggle that disables the low-health red vignette. Persisted as Settings_LowHpVignette.")]
    public Toggle lowHpVignetteToggle;
    public Graphic lowHpVignetteCheckmark;
    [Tooltip("Toggle that disables hit-stop micro-pauses on heavy hits. Persisted as Settings_HitStop.")]
    public Toggle hitStopToggle;
    public Graphic hitStopCheckmark;

    [Header("Graphics")]
    [Tooltip("4-option dropdown (Low/Medium/High/Ultra). Calls QualitySettings.SetQualityLevel.")]
    public TMP_Dropdown qualityDropdown;
    [Tooltip("Slider with min=70, max=100 (% of native). Persisted as Settings_RenderScale.")]
    public Slider renderScaleSlider;
    public TMP_InputField renderScaleInput;
    [Tooltip("Toggle for post-processing (Bloom, ColorGrading). Persisted as Settings_PostFX.")]
    public Toggle postFXToggle;
    public Graphic postFXCheckmark;
    [Tooltip("Toggle for dynamic shadows on minor lights. Persisted as Settings_DynamicShadows.")]
    public Toggle dynamicShadowsToggle;
    public Graphic dynamicShadowsCheckmark;

    [Header("Language")]
    [Tooltip("2-option dropdown (English / Українська). Drives LocalizationManager.CurrentLanguage.")]
    public TMP_Dropdown languageDropdown;

    [Header("Buttons (��� ��������)")]
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

        // ---- Extended controls ----
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

        if (subtitlesToggle) subtitlesToggle.onValueChanged.AddListener(v =>
        {
            PlayerPrefs.SetInt("Settings_Subtitles", v ? 1 : 0);
        });
        if (subtitleSizeDropdown) subtitleSizeDropdown.onValueChanged.AddListener(v =>
        {
            PlayerPrefs.SetInt("Settings_SubtitleSize", Mathf.Clamp(v, 0, 2));
        });

        if (colorblindToggle) colorblindToggle.onValueChanged.AddListener(v =>
        {
            PlayerPrefs.SetInt("Settings_Colorblind", v ? 1 : 0);
        });
        if (lowHpVignetteToggle) lowHpVignetteToggle.onValueChanged.AddListener(v =>
        {
            PlayerPrefs.SetInt("Settings_LowHpVignette", v ? 1 : 0);
        });
        if (hitStopToggle) hitStopToggle.onValueChanged.AddListener(v =>
        {
            PlayerPrefs.SetInt("Settings_HitStop", v ? 1 : 0);
        });

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
        if (postFXToggle) postFXToggle.onValueChanged.AddListener(v =>
        {
            PlayerPrefs.SetInt("Settings_PostFX", v ? 1 : 0);
        });
        if (dynamicShadowsToggle) dynamicShadowsToggle.onValueChanged.AddListener(v =>
        {
            PlayerPrefs.SetInt("Settings_DynamicShadows", v ? 1 : 0);
        });

        if (languageDropdown) languageDropdown.onValueChanged.AddListener(v =>
        {
            PlayerPrefs.SetInt("Settings_Language", Mathf.Clamp(v, 0, 1));
        });

        if (closeButton != null) closeButton.gameObject.AddComponent<AutoButtonAnimator>().Setup(closeButtonText, true);

        if (saveButton != null)
        {
            saveButton.gameObject.AddComponent<AutoButtonAnimator>().Setup(null, false);
            saveButton.onClick.RemoveAllListeners();
            saveButton.onClick.AddListener(CloseSettings);
        }
    }

    private void Update()
    {
        if (settingsPanel != null && settingsPanel.activeInHierarchy)
        {
            AnimateCheckmark(damageCheckmark, damagePopupsToggle.isOn);
            AnimateCheckmark(screenShakeCheckmark, screenShakeToggle.isOn);
            AnimateCheckmark(fpsLimitCheckmark, limitFPSToggle.isOn);
            AnimateCheckmark(showFPSCheckmark, showFPSToggle.isOn);

            // Extended checkmarks (all null-safe).
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

        if (AudioManager.Instance != null && settingsPanel.activeSelf)
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
        if (AudioManager.Instance != null) AudioManager.Instance.PlayUI(AudioID.UI_Click);

        if (SceneManager.GetActiveScene().name != "Menu") Time.timeScale = 0f;

        settingsPanel.SetActive(true);
        settingsPanel.transform.SetAsLastSibling();

        if (tabTexts != null && tabTexts.Length > 0 && tabPanels != null && tabPanels.Length > 0)
        {
            currentTabIndex = 0;
            Vector3 targetWorldPos = tabTexts[0].rectTransform.position;
            Vector3 targetLocalPos = underline.parent.InverseTransformPoint(targetWorldPos);
            targetLineLocalX = targetLocalPos.x;

            if (underline != null)
            {
                Vector3 pos = underline.localPosition;
                pos.x = targetLineLocalX;
                underline.localPosition = pos;
            }

            for (int i = 0; i < tabPanels.Length; i++)
            {
                if (tabPanels[i] != null) tabPanels[i].SetActive(i == 0);
            }

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

        if (masterSlider) masterSlider.value = PlayerPrefs.GetFloat("Settings_MasterVol", 100f);
        if (musicSlider) musicSlider.value = PlayerPrefs.GetFloat("Settings_MusicVol", 100f);
        if (sfxSlider) sfxSlider.value = PlayerPrefs.GetFloat("Settings_SFXVol", 100f);

        if (masterInput && masterSlider) masterInput.text = masterSlider.value.ToString("0");
        if (musicInput && musicSlider) musicInput.text = musicSlider.value.ToString("0");
        if (sfxInput && sfxSlider) sfxInput.text = sfxSlider.value.ToString("0");

        if (damagePopupsToggle) damagePopupsToggle.isOn = PlayerPrefs.GetInt("Settings_DamagePopups", 1) == 1;
        if (screenShakeToggle) screenShakeToggle.isOn = PlayerPrefs.GetInt("Settings_ScreenShake", 1) == 1;
        if (limitFPSToggle) limitFPSToggle.isOn = PlayerPrefs.GetInt("Settings_FPSLimit", 1) == 1;
        if (showFPSToggle) showFPSToggle.isOn = PlayerPrefs.GetInt("Settings_ShowFPS", 0) == 1;

        ForceCheckmarkState(damageCheckmark, damagePopupsToggle.isOn);
        ForceCheckmarkState(screenShakeCheckmark, screenShakeToggle.isOn);
        ForceCheckmarkState(fpsLimitCheckmark, limitFPSToggle.isOn);
        ForceCheckmarkState(showFPSCheckmark, showFPSToggle.isOn);

        // ---- Extended controls: populate from PlayerPrefs ----
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

        if (languageDropdown) languageDropdown.value = Mathf.Clamp(PlayerPrefs.GetInt("Settings_Language", 0), 0, 1);

        if (saveButton != null)
        {
            TextMeshProUGUI btnText = saveButton.GetComponentInChildren<TextMeshProUGUI>();
            if (btnText != null) btnText.text = "SAVE & CLOSE";
        }

        if (panelAnimCoroutine != null) StopCoroutine(panelAnimCoroutine);
        panelAnimCoroutine = StartCoroutine(AnimatePanelIn());
    }

    public void CloseSettings()
    {
        if (AudioManager.Instance != null) AudioManager.Instance.PlayUI(AudioID.UI_Click);

        if (masterSlider) PlayerPrefs.SetFloat("Settings_MasterVol", masterSlider.value);
        if (musicSlider) PlayerPrefs.SetFloat("Settings_MusicVol", musicSlider.value);
        if (sfxSlider) PlayerPrefs.SetFloat("Settings_SFXVol", sfxSlider.value);

        if (damagePopupsToggle) PlayerPrefs.SetInt("Settings_DamagePopups", damagePopupsToggle.isOn ? 1 : 0);
        if (screenShakeToggle) PlayerPrefs.SetInt("Settings_ScreenShake", screenShakeToggle.isOn ? 1 : 0);
        if (limitFPSToggle) PlayerPrefs.SetInt("Settings_FPSLimit", limitFPSToggle.isOn ? 1 : 0);
        if (showFPSToggle) PlayerPrefs.SetInt("Settings_ShowFPS", showFPSToggle.isOn ? 1 : 0);

        // ---- Extended controls: persist on close so even unfocused
        //      sliders / dropdowns commit their final value ----
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

        PlayerPrefs.Save();

        // Force-apply the FPS limit via the shared helper so vSync is
        // never accidentally turned on (which would lock the build to
        // the monitor's refresh rate instead of 60).
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
        panelCanvasGroup.alpha = 0f;
        panelRect.localScale = new Vector3(0.8f, 0.8f, 0.8f);

        float t = 0f;
        while (t < 1f)
        {
            t += Time.unscaledDeltaTime * animationSpeed;
            float easeOutQuart = 1f - Mathf.Pow(1f - t, 4f);
            panelCanvasGroup.alpha = Mathf.Lerp(0f, 1f, easeOutQuart);
            panelRect.localScale = Vector3.Lerp(new Vector3(0.8f, 0.8f, 0.8f), Vector3.one, easeOutQuart);
            yield return null;
        }
        panelCanvasGroup.alpha = 1f;
        panelRect.localScale = Vector3.one;
    }

    private IEnumerator AnimatePanelOut()
    {
        if (panelCanvasGroup == null || panelRect == null) { FinishClosing(); yield break; }
        float t = 0f;
        while (t < 1f)
        {
            t += Time.unscaledDeltaTime * (animationSpeed * 1.5f);
            float easeInQuad = t * t;
            panelCanvasGroup.alpha = Mathf.Lerp(1f, 0f, easeInQuad);
            panelRect.localScale = Vector3.Lerp(Vector3.one, new Vector3(0.9f, 0.9f, 0.9f), easeInQuad);
            yield return null;
        }
        FinishClosing();
    }

    private void FinishClosing()
    {
        settingsPanel.SetActive(false);
        if (SceneManager.GetActiveScene().name == "Menu") return;

        bool isPauseMenuOpen = GlobalHUD.Instance != null && GlobalHUD.Instance.pausePanelGroup.gameObject.activeInHierarchy;
        if (!isPauseMenuOpen) Time.timeScale = 1f;
    }

    private void ForceCheckmarkState(Graphic checkmark, bool isOn)
    {
        if (checkmark == null) return;
        checkmark.rectTransform.localScale = isOn ? Vector3.one : Vector3.zero;
        Color c = checkmark.color;
        c.a = isOn ? 1f : 0f;
        checkmark.color = c;
    }

    private void OnMasterVolumeChanged(float value)
    {
        if (masterInput) masterInput.text = value.ToString("0");
        if (AudioManager.Instance != null) AudioManager.Instance.SetMasterVolume(value / 100f);
    }

    private void OnMusicVolumeChanged(float value)
    {
        if (musicInput) musicInput.text = value.ToString("0");
        if (AudioManager.Instance != null) AudioManager.Instance.SetMusicVolume(value / 100f);
    }

    private void OnSFXVolumeChanged(float value)
    {
        if (sfxInput) sfxInput.text = value.ToString("0");
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

    private void OnDamagePopupsChanged(bool isOn) { if (AudioManager.Instance != null && settingsPanel.activeSelf) AudioManager.Instance.PlayUI(AudioID.UI_Hover); }
    private void OnScreenShakeChanged(bool isOn) { if (AudioManager.Instance != null && settingsPanel.activeSelf) AudioManager.Instance.PlayUI(AudioID.UI_Hover); }

    private void OnFPSLimitChanged(bool isOn)
    {
        if (AudioManager.Instance != null && settingsPanel.activeSelf) AudioManager.Instance.PlayUI(AudioID.UI_Hover);
        ApplyFpsLimit(isOn);
    }

    // The previous implementation set vSyncCount=1 AND targetFrameRate=60.
    // In a build, vSyncCount=1 takes precedence and locks the frame rate
    // to the monitor's refresh rate (commonly 144 / 165 / 240 Hz), so the
    // "Limit FPS to 60" toggle did nothing for high-refresh-rate
    // displays. The build also persists vSyncCount, so the lock survived
    // across scene loads. Force vSyncCount OFF whenever the limit is on,
    // so targetFrameRate is what actually drives the cap.
    public static void ApplyFpsLimit(bool isOn)
    {
        if (isOn)
        {
            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = 60;
        }
        else
        {
            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = -1;
        }
    }

    private void OnShowFPSChanged(bool isOn)
    {
        if (AudioManager.Instance != null && settingsPanel.activeSelf) AudioManager.Instance.PlayUI(AudioID.UI_Hover);
        PlayerPrefs.SetInt("Settings_ShowFPS", isOn ? 1 : 0);
        if (FPSDisplay.Instance != null) FPSDisplay.Instance.UpdateVisibility();
    }

    // ---- Extended slider / input handlers ----

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

    public void OnPointerEnter(PointerEventData eventData) { if (AudioManager.Instance != null) AudioManager.Instance.PlayUI("UI_Hover"); targetScale = Vector3.one * 1.05f; if (isCloseButton) targetColor = hoverColor; }
    public void OnPointerExit(PointerEventData eventData) { targetScale = Vector3.one; if (isCloseButton) targetColor = normalColor; }
    public void OnPointerDown(PointerEventData eventData) { targetScale = Vector3.one * 0.95f; }
    public void OnPointerUp(PointerEventData eventData) { targetScale = Vector3.one * 1.05f; }
}