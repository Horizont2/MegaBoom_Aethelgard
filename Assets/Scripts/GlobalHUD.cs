using UnityEngine;
using UnityEngine.UI;
using TMPro;
using UnityEngine.SceneManagement;
using System.Collections;
using System.Collections.Generic;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using System;

public class GlobalHUD : MonoBehaviour
{
    public static GlobalHUD Instance;

    [Header("Visibility Control")]
    public GameObject[] gameplayPanels;

    [Header("Interaction Prompt")]
    public CanvasGroup promptCanvasGroup;
    public TextMeshProUGUI promptText;
    public float promptFadeSpeed = 5f;
    public float typingSpeed = 0.03f;

    [Header("Level Objective UI")]
    public CanvasGroup objectivePanelGroup;
    public TextMeshProUGUI objectiveText;

    [Header("Boss UI (AAA Style)")]
    public CanvasGroup bossUIGroup;
    public TextMeshProUGUI bossNameText;
    public Image bossHpFill;
    public Image bossHpCatchupFill;

    private float targetBossHpRatio = 1f;
    private Coroutine bossUIFadeRoutine;

    [Header("Cinematic Bars")]
    private RectTransform topCinematicBar;
    private RectTransform bottomCinematicBar;
    private Coroutine barsRoutine;

    [Header("Cinematic Skip Prompt")]
    private CanvasGroup skipPromptGroup;
    private TextMeshProUGUI skipPromptText;
    private Coroutine skipPromptRoutine;

    [Header("Pause Menu Settings")]
    public CanvasGroup pausePanelGroup;
    public GameObject[] pauseButtons;
    public CanvasGroup[] pauseButtonGroups;
    public CanvasGroup giveUpButtonGroup;
    public TextMeshProUGUI giveUpText;
    public float buttonDelay = 0.05f;

    [Header("Global Upgrade Tracking")]
    public GameObject buildWidgetPrefab;
    public Transform widgetContainer;

    private bool isPaused = false;
    private bool isConfirmingGiveUp = false;
    private DepthOfField dofEffect;

    private Coroutine promptFadeCoroutine;
    private Coroutine promptTypingCoroutine;
    private RenderMode defaultRenderMode;

    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }
        else
        {
            Destroy(gameObject);
            return;
        }

        Canvas canvas = GetComponent<Canvas>();
        if (canvas != null) defaultRenderMode = canvas.renderMode;

        ApplySavedSettings();
        CheckActiveUpgradesOnLoad();

        if (promptCanvasGroup != null) promptCanvasGroup.alpha = 0f;
        if (objectivePanelGroup != null) objectivePanelGroup.alpha = 0f;
        if (bossUIGroup != null) bossUIGroup.alpha = 0f;
        if (pausePanelGroup != null) pausePanelGroup.gameObject.SetActive(false);

        CreateCinematicBarsIfNeeded();
    }

    private void ApplySavedSettings()
    {
        bool isLimited = PlayerPrefs.GetInt("Settings_FPSLimit", 1) == 1;

        if (isLimited)
        {
            QualitySettings.vSyncCount = 1;
            Application.targetFrameRate = 60;
        }
        else
        {
            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = -1;
        }
    }

    private void OnEnable() { SceneManager.sceneLoaded += OnSceneLoaded; }
    private void OnDisable() { SceneManager.sceneLoaded -= OnSceneLoaded; }

    private void Update()
    {
        if (bossUIGroup != null && bossUIGroup.alpha > 0f)
        {
            if (bossHpFill != null)
                bossHpFill.fillAmount = Mathf.Lerp(bossHpFill.fillAmount, targetBossHpRatio, Time.deltaTime * 10f);

            if (bossHpCatchupFill != null && bossHpCatchupFill.fillAmount > targetBossHpRatio)
                bossHpCatchupFill.fillAmount = Mathf.Lerp(bossHpCatchupFill.fillAmount, targetBossHpRatio, Time.deltaTime * 2.5f);
        }

        if (Input.GetKeyDown(KeyCode.Escape))
        {
            if (SettingsUI.Instance != null && SettingsUI.Instance.settingsPanel != null && SettingsUI.Instance.settingsPanel.activeInHierarchy)
            {
                SettingsUI.Instance.CloseSettings();
                return;
            }

            if (MapTableInteract.IsMapActive) return;

            MapPanelUI mapPanel = FindFirstObjectByType<MapPanelUI>();
            if (mapPanel != null && mapPanel.IsPanelOpen())
            {
                mapPanel.ClosePanel();
                return;
            }

            string sceneName = SceneManager.GetActiveScene().name;
            if (sceneName == "GameScene" || sceneName == "CampScene" || sceneName == "ShopScene" || sceneName == "Lvl_1")
            {
                TogglePause();
            }
        }
    }

    public void StartTrackingUpgrade(string buildingID, string buildingName, Sprite bIcon, float duration, long startTimeBinary)
    {
        string list = PlayerPrefs.GetString("ActiveUpgradesList", "");
        if (!list.Contains(buildingID))
        {
            list += (string.IsNullOrEmpty(list) ? "" : ",") + buildingID;
            PlayerPrefs.SetString("ActiveUpgradesList", list);
        }

        PlayerPrefs.SetString("UpgName_" + buildingID, buildingName);
        PlayerPrefs.SetFloat("UpgDur_" + buildingID, duration);
        PlayerPrefs.SetString("UpgStart_" + buildingID, startTimeBinary.ToString());
        PlayerPrefs.Save();

        if (buildWidgetPrefab != null && widgetContainer != null)
        {
            GameObject banner = Instantiate(buildWidgetPrefab, widgetContainer);
            ActiveBuildWidget tracker = banner.GetComponent<ActiveBuildWidget>();
            if (tracker != null)
            {
                DateTime startTime = DateTime.FromBinary(startTimeBinary);
                DateTime targetTime = startTime.AddSeconds(duration);
                tracker.Setup(buildingID, buildingName, bIcon, targetTime, duration);
            }
        }
    }

    public void RemoveUpgradeFromList(string buildingID)
    {
        string list = PlayerPrefs.GetString("ActiveUpgradesList", "");
        if (!string.IsNullOrEmpty(list))
        {
            List<string> ids = new List<string>(list.Split(','));
            if (ids.Contains(buildingID))
            {
                ids.Remove(buildingID);
                PlayerPrefs.SetString("ActiveUpgradesList", string.Join(",", ids));
                PlayerPrefs.Save();
            }
        }
    }

    private void CheckActiveUpgradesOnLoad()
    {
        string list = PlayerPrefs.GetString("ActiveUpgradesList", "");
        if (string.IsNullOrEmpty(list)) return;

        string[] ids = list.Split(',');
        foreach (string id in ids)
        {
            if (string.IsNullOrEmpty(id)) continue;

            if (PlayerPrefs.GetInt("IsUpgrading_" + id, 0) == 1)
            {
                string bName = PlayerPrefs.GetString("UpgName_" + id, "Building");
                float dur = PlayerPrefs.GetFloat("UpgDur_" + id, 10f);
                string startStr = PlayerPrefs.GetString("UpgStart_" + id, "0");

                if (long.TryParse(startStr, out long startBin))
                {
                    if (buildWidgetPrefab != null && widgetContainer != null)
                    {
                        GameObject banner = Instantiate(buildWidgetPrefab, widgetContainer);
                        ActiveBuildWidget tracker = banner.GetComponent<ActiveBuildWidget>();
                        if (tracker != null)
                        {
                            DateTime targetTime = DateTime.FromBinary(startBin).AddSeconds(dur);
                            tracker.Setup(id, bName, null, targetTime, dur);
                        }
                    }
                }
            }
            else
            {
                RemoveUpgradeFromList(id);
            }
        }
    }

    private void CreateCinematicBarsIfNeeded()
    {
        if (topCinematicBar != null && bottomCinematicBar != null) return;

        // Important: parent directly to the GlobalHUD canvas (RectTransform). The
        // previous version created a plain GameObject ("CinematicBars_Auto") and
        // parented Image children to its non-RectTransform — RectTransform anchors
        // don't work properly when an ancestor in the chain isn't a RectTransform,
        // which is why the bars never showed up on cutscenes.
        Transform parent = this.transform;

        GameObject topObj = new GameObject("CinematicBar_Top", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        topCinematicBar = topObj.GetComponent<RectTransform>();
        topCinematicBar.SetParent(parent, false);
        topCinematicBar.anchorMin = new Vector2(0, 1);
        topCinematicBar.anchorMax = new Vector2(1, 1);
        topCinematicBar.pivot = new Vector2(0.5f, 0);
        topCinematicBar.sizeDelta = new Vector2(0, 150);
        topCinematicBar.anchoredPosition = new Vector2(0, 150);
        Image topImg = topObj.GetComponent<Image>();
        topImg.color = Color.black;
        topImg.raycastTarget = false;

        GameObject botObj = new GameObject("CinematicBar_Bottom", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        bottomCinematicBar = botObj.GetComponent<RectTransform>();
        bottomCinematicBar.SetParent(parent, false);
        bottomCinematicBar.anchorMin = new Vector2(0, 0);
        bottomCinematicBar.anchorMax = new Vector2(1, 0);
        bottomCinematicBar.pivot = new Vector2(0.5f, 1);
        bottomCinematicBar.sizeDelta = new Vector2(0, 150);
        bottomCinematicBar.anchoredPosition = new Vector2(0, -150);
        Image botImg = botObj.GetComponent<Image>();
        botImg.color = Color.black;
        botImg.raycastTarget = false;

        topCinematicBar.SetAsLastSibling();
        bottomCinematicBar.SetAsLastSibling();
    }

    private void CreateSkipPromptIfNeeded()
    {
        if (skipPromptGroup != null) return;

        GameObject host = new GameObject("CinematicSkipPrompt", typeof(RectTransform), typeof(CanvasGroup));
        RectTransform hostRect = host.GetComponent<RectTransform>();
        hostRect.SetParent(this.transform, false);
        hostRect.anchorMin = new Vector2(0.5f, 0);
        hostRect.anchorMax = new Vector2(0.5f, 0);
        hostRect.pivot = new Vector2(0.5f, 0);
        hostRect.sizeDelta = new Vector2(420f, 60f);
        hostRect.anchoredPosition = new Vector2(0, 40f);

        skipPromptGroup = host.GetComponent<CanvasGroup>();
        skipPromptGroup.alpha = 0f;
        skipPromptGroup.interactable = false;
        skipPromptGroup.blocksRaycasts = false;

        GameObject txtObj = new GameObject("Label", typeof(RectTransform), typeof(CanvasRenderer));
        RectTransform txtRect = txtObj.GetComponent<RectTransform>();
        txtRect.SetParent(hostRect, false);
        txtRect.anchorMin = Vector2.zero;
        txtRect.anchorMax = Vector2.one;
        txtRect.offsetMin = Vector2.zero;
        txtRect.offsetMax = Vector2.zero;

        skipPromptText = txtObj.AddComponent<TextMeshProUGUI>();
        skipPromptText.text = "Press <b>SPACE</b> to Skip";
        skipPromptText.fontSize = 24f;
        skipPromptText.alignment = TextAlignmentOptions.Center;
        skipPromptText.color = new Color(1f, 0.92f, 0.72f, 0.9f);
        skipPromptText.fontStyle = FontStyles.Normal;
        skipPromptText.outlineWidth = 0.2f;
        skipPromptText.outlineColor = Color.black;
        skipPromptText.raycastTarget = false;

        hostRect.SetAsLastSibling();
    }

    public void ShowSkipPrompt(string customText = null)
    {
        CreateSkipPromptIfNeeded();
        if (skipPromptGroup == null) return;
        if (!string.IsNullOrEmpty(customText) && skipPromptText != null)
            skipPromptText.text = customText;
        if (skipPromptRoutine != null) StopCoroutine(skipPromptRoutine);
        skipPromptRoutine = StartCoroutine(SkipPromptRoutine());
    }

    public void HideSkipPrompt()
    {
        if (skipPromptRoutine != null) StopCoroutine(skipPromptRoutine);
        if (skipPromptGroup != null) StartCoroutine(FadeSkipPromptOut());
    }

    private IEnumerator SkipPromptRoutine()
    {
        // Fade in once, then breathe alpha forever until HideSkipPrompt() stops us.
        float fadeIn = 0.5f;
        float t = 0f;
        while (t < fadeIn)
        {
            t += Time.unscaledDeltaTime;
            skipPromptGroup.alpha = Mathf.Lerp(0f, 1f, t / fadeIn);
            yield return null;
        }
        while (true)
        {
            // breathe: sin wave 0.55 → 1.0 → 0.55 each ~1.4s
            float k = (Mathf.Sin(Time.unscaledTime * 4.5f) + 1f) * 0.5f;
            skipPromptGroup.alpha = Mathf.Lerp(0.55f, 1f, k);
            yield return null;
        }
    }

    private IEnumerator FadeSkipPromptOut()
    {
        float startAlpha = skipPromptGroup.alpha;
        float t = 0f;
        while (t < 0.3f)
        {
            t += Time.unscaledDeltaTime;
            skipPromptGroup.alpha = Mathf.Lerp(startAlpha, 0f, t / 0.3f);
            yield return null;
        }
        skipPromptGroup.alpha = 0f;
    }

    public void ShowCinematicBars()
    {
        CreateCinematicBarsIfNeeded();
        if (barsRoutine != null) StopCoroutine(barsRoutine);
        barsRoutine = StartCoroutine(AnimateBars(0f));
    }

    public void HideCinematicBars()
    {
        CreateCinematicBarsIfNeeded();
        if (barsRoutine != null) StopCoroutine(barsRoutine);
        barsRoutine = StartCoroutine(AnimateBars(150f));
    }

    private IEnumerator AnimateBars(float targetY)
    {
        if (topCinematicBar == null || bottomCinematicBar == null) yield break;
        float currentTopY = topCinematicBar.anchoredPosition.y;
        float currentBottomY = bottomCinematicBar.anchoredPosition.y;
        float elapsed = 0f;

        while (elapsed < 0.4f)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.SmoothStep(0f, 1f, elapsed / 0.4f);
            topCinematicBar.anchoredPosition = new Vector2(0, Mathf.Lerp(currentTopY, targetY, t));
            bottomCinematicBar.anchoredPosition = new Vector2(0, Mathf.Lerp(currentBottomY, -targetY, t));
            yield return null;
        }
    }

    public void ShowBossUI(string bossName, float currentHp, float maxHp)
    {
        if (bossUIGroup == null) return;
        if (bossNameText != null) bossNameText.text = bossName;
        targetBossHpRatio = currentHp / maxHp;
        if (bossHpFill != null) bossHpFill.fillAmount = targetBossHpRatio;
        if (bossHpCatchupFill != null) bossHpCatchupFill.fillAmount = targetBossHpRatio;
        if (bossUIFadeRoutine != null) StopCoroutine(bossUIFadeRoutine);
        bossUIFadeRoutine = StartCoroutine(FadeBossUIRoutine(1f));
    }

    public void UpdateBossHealth(float currentHp, float maxHp) { targetBossHpRatio = currentHp / maxHp; }

    public void HideBossUI()
    {
        if (bossUIGroup == null) return;
        if (bossUIFadeRoutine != null) StopCoroutine(bossUIFadeRoutine);
        bossUIFadeRoutine = StartCoroutine(FadeBossUIRoutine(0f));
    }

    private IEnumerator FadeBossUIRoutine(float targetAlpha)
    {
        while (Mathf.Abs(bossUIGroup.alpha - targetAlpha) > 0.01f)
        {
            bossUIGroup.alpha = Mathf.MoveTowards(bossUIGroup.alpha, targetAlpha, Time.deltaTime * 2f);
            yield return null;
        }
        bossUIGroup.alpha = targetAlpha;
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        StartCoroutine(SyncCameraAndVolumeRoutine());

        bool isTutorial = (scene.name == "Lvl_1");
        bool showGameplayUI = (scene.name != "Menu" && scene.name != "ShopScene");

        if (gameplayPanels != null)
        {
            foreach (GameObject panel in gameplayPanels)
            {
                if (panel != null)
                {
                    if (isTutorial && (panel.name == "Resources" || panel.name == "MissionUIParent")) panel.SetActive(false);
                    else panel.SetActive(showGameplayUI);
                }
            }
        }

        if (promptCanvasGroup != null) promptCanvasGroup.alpha = 0f;
        HideBossUI();

        if (isPaused)
        {
            isPaused = false;
            Time.timeScale = 1f;
            if (pausePanelGroup != null) { pausePanelGroup.alpha = 0f; pausePanelGroup.gameObject.SetActive(false); }
        }
    }

    private IEnumerator SyncCameraAndVolumeRoutine()
    {
        yield return new WaitForSeconds(0.5f); // Чекаємо півсекунди, щоб уникнути фрізу при завантаженні

        Canvas canvas = GetComponent<Canvas>();
        if (canvas != null)
        {
            canvas.renderMode = defaultRenderMode;
            canvas.sortingOrder = 50;
            if (defaultRenderMode == RenderMode.ScreenSpaceCamera)
            {
                Camera cam = Camera.main;
                if (cam == null) cam = FindFirstObjectByType<Camera>();
                canvas.worldCamera = cam;
            }
        }

        Volume[] allVolumes = FindObjectsByType<Volume>(FindObjectsSortMode.None);
        foreach (Volume v in allVolumes)
        {
            if (v.isGlobal && v.profile != null && v.profile.TryGet(out dofEffect))
            {
                bool isShop = SceneManager.GetActiveScene().name == "ShopScene";
                dofEffect.active = isShop || isPaused;
                break;
            }
            yield return null; // Розподіляємо навантаження на кілька кадрів
        }
    }

    public void FadeAndLoadScene(string sceneName)
    {
        if (isPaused) TogglePause();

        if (LoadingManager.Instance != null)
        {
            LoadingManager.Instance.LoadScene(sceneName);
        }
        else
        {
            SceneManager.LoadScene(sceneName);
        }
    }

    public void ShowPrompt(string message)
    {
        if (promptFadeCoroutine != null) StopCoroutine(promptFadeCoroutine);
        if (promptTypingCoroutine != null) StopCoroutine(promptTypingCoroutine);
        promptFadeCoroutine = StartCoroutine(FadeCanvasGroup(1f));
        promptTypingCoroutine = StartCoroutine(TypeTextRoutine(message, promptText));
    }

    public void HidePrompt()
    {
        if (promptFadeCoroutine != null) StopCoroutine(promptFadeCoroutine);
        if (promptTypingCoroutine != null) StopCoroutine(promptTypingCoroutine);
        promptFadeCoroutine = StartCoroutine(FadeCanvasGroup(0f));
    }

    private IEnumerator FadeCanvasGroup(float targetAlpha)
    {
        if (promptCanvasGroup == null) yield break;
        while (Mathf.Abs(promptCanvasGroup.alpha - targetAlpha) > 0.01f)
        {
            promptCanvasGroup.alpha = Mathf.MoveTowards(promptCanvasGroup.alpha, targetAlpha, Time.deltaTime * promptFadeSpeed);
            yield return null;
        }
        promptCanvasGroup.alpha = targetAlpha;
    }

    private IEnumerator TypeTextRoutine(string message, TextMeshProUGUI textTarget)
    {
        if (textTarget == null) yield break;
        textTarget.text = message;
        textTarget.ForceMeshUpdate();
        int totalChars = textTarget.textInfo.characterCount;
        textTarget.maxVisibleCharacters = 0;
        for (int i = 0; i <= totalChars; i++)
        {
            textTarget.maxVisibleCharacters = i;
            yield return new WaitForSecondsRealtime(typingSpeed);
        }
        textTarget.maxVisibleCharacters = 99999;
    }

    public void TogglePause()
    {
        if (pausePanelGroup == null) return;
        Canvas canvas = GetComponent<Canvas>();
        if (canvas != null && !canvas.enabled) canvas.enabled = true;

        isPaused = !isPaused;
        Time.timeScale = isPaused ? 0f : 1f;
        if (AudioManager.Instance != null) AudioManager.Instance.PlayUI(AudioID.UI_Click);

        if (dofEffect != null)
        {
            bool isShop = SceneManager.GetActiveScene().name == "ShopScene";
            dofEffect.active = isShop || isPaused;
        }

        if (isPaused) { ResetGiveUpState(); StartCoroutine(ShowMenuRoutine()); }
        else StartCoroutine(HideMenuRoutine());

        string currentScene = SceneManager.GetActiveScene().name;
        if (currentScene == "ShopScene" || currentScene == "Menu") { Cursor.visible = true; Cursor.lockState = CursorLockMode.None; }
        else { Cursor.visible = isPaused; Cursor.lockState = isPaused ? CursorLockMode.None : CursorLockMode.Locked; }
    }

    private IEnumerator ShowMenuRoutine()
    {
        pausePanelGroup.gameObject.SetActive(true);
        pausePanelGroup.blocksRaycasts = true;
        pausePanelGroup.interactable = true;
        string currentScene = SceneManager.GetActiveScene().name;

        if (giveUpButtonGroup != null)
        {
            giveUpButtonGroup.gameObject.SetActive(true);
            if (giveUpText != null) giveUpText.text = (currentScene == "GameScene") ? "Give Up" : "Back to Menu";
        }

        foreach (var btn in pauseButtons) if (btn != null) btn.GetComponent<RectTransform>().localScale = Vector3.zero;

        float t = 0;
        while (t < 1) { t += Time.unscaledDeltaTime * 6f; pausePanelGroup.alpha = Mathf.Lerp(0, 1, t); yield return null; }
        pausePanelGroup.alpha = 1f;

        foreach (var btn in pauseButtons)
        {
            if (btn != null && btn.activeSelf) { StartCoroutine(AnimateButtonIn(btn.GetComponent<RectTransform>())); yield return new WaitForSecondsRealtime(buttonDelay); }
        }
    }

    private IEnumerator AnimateButtonIn(RectTransform btn)
    {
        Vector3 targetScale = Vector3.one;
        float t = 0;
        while (t < 1) { t += Time.unscaledDeltaTime * 5f; float s = Mathf.Sin(t * Mathf.PI * 0.5f + 0.2f) * 1.15f; btn.localScale = new Vector3(s, s, s); yield return null; }
        btn.localScale = targetScale;
    }

    private IEnumerator HideMenuRoutine()
    {
        pausePanelGroup.interactable = false;
        pausePanelGroup.blocksRaycasts = false;
        float t = pausePanelGroup.alpha;
        while (t > 0) { t -= Time.unscaledDeltaTime * 10f; pausePanelGroup.alpha = t; yield return null; }
        pausePanelGroup.alpha = 0f;
        pausePanelGroup.gameObject.SetActive(false);
    }

    public void OnGiveUpClicked()
    {
        if (AudioManager.Instance != null) AudioManager.Instance.PlayUI(AudioID.UI_Click);
        string currentScene = SceneManager.GetActiveScene().name;

        if (!isConfirmingGiveUp)
        {
            isConfirmingGiveUp = true;
            if (giveUpText != null) giveUpText.text = (currentScene == "GameScene") ? "You sure?\nAll journey progress will be lost" : "Are you sure?";
            foreach (var btn in pauseButtonGroups) { if (btn != null && btn != giveUpButtonGroup) { btn.alpha = 0.3f; btn.interactable = false; } }
        }
        else
        {
            if (currentScene == "GameScene")
            {
                if (ResourceManager.Instance != null) ResourceManager.Instance.ClearRunInventory();
                FadeAndLoadScene("CampScene");
            }
            else FadeAndLoadScene("Menu");
        }
    }

    private void ResetGiveUpState()
    {
        isConfirmingGiveUp = false;
        string currentScene = SceneManager.GetActiveScene().name;
        if (giveUpText != null) giveUpText.text = (currentScene == "GameScene") ? "Give Up" : "Back to Menu";
        foreach (var btn in pauseButtonGroups) { if (btn != null) { btn.alpha = 1f; btn.interactable = true; } }
    }

    public void SetLevelObjective(string message) { if (objectiveText != null) objectiveText.text = message; if (objectivePanelGroup != null) objectivePanelGroup.alpha = 1f; }
    public void HideLevelObjective() { if (objectivePanelGroup != null && objectivePanelGroup.alpha > 0) StartCoroutine(HideObjectiveRoutine()); }

    private IEnumerator HideObjectiveRoutine()
    {
        RectTransform rect = objectivePanelGroup.GetComponent<RectTransform>();
        Vector2 startPos = rect.anchoredPosition;
        float t = 0;
        while (t < 1f) { t += Time.deltaTime * 6f; rect.anchoredPosition = Vector2.Lerp(startPos, startPos + new Vector2(30f, 0), Mathf.Sin(t * Mathf.PI * 0.5f)); yield return null; }
        t = 0;
        Vector2 midPos = rect.anchoredPosition;
        while (t < 1f) { t += Time.deltaTime * 3f; rect.anchoredPosition = Vector2.Lerp(midPos, midPos + new Vector2(-600f, 0), t * t * t); objectivePanelGroup.alpha = 1f - t; yield return null; }
        objectivePanelGroup.alpha = 0f;
        rect.anchoredPosition = startPos;
    }

    public void SetGameplayPanelsActive(bool active)
    {
        if (gameplayPanels != null) { foreach (GameObject panel in gameplayPanels) { if (panel != null) panel.SetActive(active); } }
    }
}