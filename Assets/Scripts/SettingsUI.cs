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

    [Header("Checkmark Graphics (Для анімації)")]
    public Graphic damageCheckmark;
    public Graphic screenShakeCheckmark;
    public Graphic fpsLimitCheckmark;
    public Graphic showFPSCheckmark;

    [Header("Buttons (Для анімації)")]
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

        PlayerPrefs.Save();

        // --- ФІКС FPS: Зберігаємо і розблоковуємо VSync разом із лімітом ---
        bool isLimited = limitFPSToggle != null && limitFPSToggle.isOn;
        QualitySettings.vSyncCount = isLimited ? 1 : 0;
        Application.targetFrameRate = isLimited ? 60 : -1;

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
        // --- ФІКС FPS: Змінюємо разом із галочкою ---
        QualitySettings.vSyncCount = isOn ? 1 : 0;
        Application.targetFrameRate = isOn ? 60 : -1;
    }

    private void OnShowFPSChanged(bool isOn)
    {
        if (AudioManager.Instance != null && settingsPanel.activeSelf) AudioManager.Instance.PlayUI(AudioID.UI_Hover);
        PlayerPrefs.SetInt("Settings_ShowFPS", isOn ? 1 : 0);
        if (FPSDisplay.Instance != null) FPSDisplay.Instance.UpdateVisibility();
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