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

    private Camera mainGameplayCamera;
    private int originalCullingMask;
    private Terrain mainTerrainComponent;

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

    [Header("Cinematic Pause (KCD2-style scenery)")]
    [Tooltip("Optional: PauseSceneController in the scene that owns the WaterfallPos camera + scenery. Auto-discovered on first TogglePause if left null.")]
    public PauseSceneController pauseSceneController;

    private bool isPaused = false;
    private bool isConfirmingGiveUp = false;
    private DepthOfField dofEffect;

    private Camera cachedMainCamera;
    private Camera cachedPauseCamera;
    private MonoBehaviour cachedCameraFollow;
    private bool wasPauseSceneActive = false; // Пам'ять про те, чи був увімкнений краєвид

    private Coroutine promptFadeCoroutine;
    private Coroutine promptTypingCoroutine;
    private RenderMode defaultRenderMode;
    private string currentPromptMessage = "";

    private readonly HashSet<GameObject> objectsHiddenByPause = new HashSet<GameObject>();
    private readonly HashSet<GameObject> hiddenByGlobalHUD = new HashSet<GameObject>();

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
        CreateLowHealthVignetteIfNeeded();
        CreatePickupPopupContainerIfNeeded();
    }

    private void ApplySavedSettings()
    {
        SettingsUI.ApplyFpsLimit(PlayerPrefs.GetInt("Settings_FPSLimit", 1) == 1);
    }

    private void OnEnable() { SceneManager.sceneLoaded += OnSceneLoaded; }
    private void OnDisable() { SceneManager.sceneLoaded -= OnSceneLoaded; }

    private void Update()
    {
        if (bossUIGroup != null && bossUIGroup.alpha > 0f)
        {
            if (bossHpFill != null)
            {
                bossHpFill.fillAmount = Mathf.Lerp(bossHpFill.fillAmount, targetBossHpRatio, Time.deltaTime * 10f);
                if (Mathf.Abs(bossHpFill.fillAmount - targetBossHpRatio) < 0.005f)
                    bossHpFill.fillAmount = targetBossHpRatio;
            }

            if (bossHpCatchupFill != null)
            {
                if (bossHpCatchupFill.fillAmount > targetBossHpRatio)
                {
                    bossHpCatchupFill.fillAmount = Mathf.Lerp(bossHpCatchupFill.fillAmount, targetBossHpRatio, Time.deltaTime * 2.5f);
                    if (Mathf.Abs(bossHpCatchupFill.fillAmount - targetBossHpRatio) < 0.005f)
                        bossHpCatchupFill.fillAmount = targetBossHpRatio;
                }
                else
                {
                    bossHpCatchupFill.fillAmount = targetBossHpRatio;
                }
            }
        }

        UpdateLowHealthVignette();

        // Pause key — Escape on keyboard, Start / Options on gamepad.
        if (InputCompat.PauseDown())
        {
            // No pause / no anything while a loading screen is up. Otherwise
            // ESC on the loading fade would open the pause menu behind it and
            // trap the player when the new scene finishes loading.
            if (LoadingManager.Instance != null && LoadingManager.Instance.isLoading) return;

            // Same guard for the intro cinematics — no pause while a story
            // beat is playing (menu intro OR the camp arrival narration).
            if (IntroCinematicManager.IsPlaying) return;
            if (CampDirector.IsPlaying) return;

            if (SettingsUI.Instance != null && SettingsUI.Instance.settingsPanel != null && SettingsUI.Instance.settingsPanel.activeInHierarchy)
            {
                SettingsUI.Instance.CloseSettings();
                return;
            }

            if (LevelUpManager.IsMenuOpen) return;
            if (MapTableInteract.IsMapActive) return;

            // Camp modal panels handle their own ESC and mark themselves open —
            // swallow the key here so pause menu doesn't cascade in on top.
            if (BarracksUpgradePanel.IsOpen) return;
            if (NoticeBoardManager.IsAnyBoardOpen) return;

            MapPanelUI mapPanel = FindFirstObjectByType<MapPanelUI>();
            if (mapPanel != null && mapPanel.IsPanelOpen())
            {
                mapPanel.ClosePanel();
                return;
            }

            string sceneName = SceneManager.GetActiveScene().name;
            if (sceneName == "ShopScene")
            {
                ShopManager shop = FindFirstObjectByType<ShopManager>();
                if (shop != null && shop.IsInsideCategory)
                {
                    shop.ReturnToArsenalGrid();
                    return;
                }
            }

            if (sceneName != "Menu")
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
        skipPromptText.text = LocalizationManager.Tr("Press <b>SPACE</b> to Skip");
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
        if (bossUIGroup == null)
        {
            Transform found = transform.Find("BossUI");
            if (found != null) bossUIGroup = found.GetComponent<CanvasGroup>();
        }
        if (bossUIGroup == null)
        {
            Debug.LogWarning("[GlobalHUD] ShowBossUI: bossUIGroup is null — wire it in the inspector.");
            return;
        }

        Transform t = bossUIGroup.transform;
        while (t != null)
        {
            if (!t.gameObject.activeSelf) t.gameObject.SetActive(true);
            t = t.parent;
        }

        if (bossUIGroup.transform.childCount > 0)
        {
            for (int i = 0; i < bossUIGroup.transform.childCount; i++)
            {
                bossUIGroup.transform.GetChild(i).gameObject.SetActive(true);
            }
        }

        bossUIGroup.ignoreParentGroups = true;
        bossUIGroup.alpha = 1f;
        bossUIGroup.blocksRaycasts = true;
        bossUIGroup.interactable = true;

        if (bossUIFadeRoutine != null) { StopCoroutine(bossUIFadeRoutine); bossUIFadeRoutine = null; }

        if (bossNameText != null) bossNameText.text = bossName;
        targetBossHpRatio = Mathf.Clamp01(maxHp > 0f ? currentHp / maxHp : 1f);
        if (bossHpFill != null) bossHpFill.fillAmount = targetBossHpRatio;
        if (bossHpCatchupFill != null) bossHpCatchupFill.fillAmount = targetBossHpRatio;
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
            bossUIGroup.alpha = Mathf.MoveTowards(bossUIGroup.alpha, targetAlpha, Time.unscaledDeltaTime * 3f);
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

        if (showGameplayUI)
        {
            if (pickupPopupContainer != null) pickupPopupContainer.gameObject.SetActive(true);
            if (widgetContainer != null) widgetContainer.gameObject.SetActive(true);
        }

        ClearAllPickupPopups();

        if (isPaused)
        {
            isPaused = false;
            Time.timeScale = 1f;
            if (pausePanelGroup != null) { pausePanelGroup.alpha = 0f; pausePanelGroup.gameObject.SetActive(false); }

            objectsHiddenByPause.Clear();
        }
    }

    private IEnumerator SyncCameraAndVolumeRoutine()
    {
        yield return new WaitForSeconds(0.5f);
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
                if (dofEffect != null)
                {
                    bool isShop = SceneManager.GetActiveScene().name == "ShopScene";
                    dofEffect.active = isShop || isPaused;
                }
                break;
            }
            yield return null;
        }
    }

    // Cinematic depth-of-field toggle — lets cutscene code rack the same
    // DoF override the pause/shop screens use, without exposing the field.
    // No-ops safely if the scene's global Volume has no DepthOfField.
    public void SetCinematicDoF(bool on)
    {
        if (dofEffect == null) return;
        // Never fight the pause menu's own DoF state.
        if (!on && isPaused) return;
        dofEffect.active = on;
    }

    public void FadeAndLoadScene(string sceneName)
    {
        if (isPaused) TogglePause();

        SceneLoader.LoadScene(sceneName);
    }

    public void ShowPrompt(string message)
    {
        string localised = LocalizationManager.Tr(message);
        if (promptCanvasGroup != null && promptCanvasGroup.alpha > 0f && currentPromptMessage == localised) return;
        currentPromptMessage = localised;

        // ФІКС: Примусово вмикаємо об'єкт перед запуском корутин, 
        // якщо його випадково вимкнула інша система (пауза чи катсцена)
        if (promptCanvasGroup != null && !promptCanvasGroup.gameObject.activeSelf)
        {
            promptCanvasGroup.gameObject.SetActive(true);
        }

        if (promptFadeCoroutine != null) StopCoroutine(promptFadeCoroutine);
        if (promptTypingCoroutine != null) StopCoroutine(promptTypingCoroutine);
        promptFadeCoroutine = StartCoroutine(FadeCanvasGroup(1f));
        promptTypingCoroutine = StartCoroutine(TypeTextRoutine(localised, promptText));
    }

    public void HidePrompt()
    {
        currentPromptMessage = "";
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

    public void CloseAllOtherActivePanels()
    {
        CampBuilding[] buildings = UnityEngine.Object.FindObjectsByType<CampBuilding>(FindObjectsSortMode.None);
        foreach (var b in buildings)
        {
            if (b != null && b.aaaPanel != null && b.aaaPanel.activeSelf)
            {
                b.aaaPanel.SetActive(false);
            }
        }

        GameObject missionBoardCanvas = GameObject.Find("MissionBoardCanvas");
        if (missionBoardCanvas == null) missionBoardCanvas = GameObject.Find("MissionBoard");
        if (missionBoardCanvas != null && missionBoardCanvas.activeSelf)
        {
            missionBoardCanvas.SetActive(false);
        }
    }

    // ==========================================
    // 🎬 AAA СВІТЧ КАМЕР ТА ПАУЗА
    // ==========================================
    public void TogglePause()
    {
        string currentScene = SceneManager.GetActiveScene().name;
        if (currentScene == "Menu" || currentScene == "MainMenu" || currentScene == "ShopScene")
        {
            return;
        }

        if (pausePanelGroup == null) return;

        Canvas canvas = GetComponent<Canvas>();
        if (canvas != null && !canvas.enabled) canvas.enabled = true;

        isPaused = !isPaused;

        if (AudioManager.Instance != null) AudioManager.Instance.PlayUI("UI_Click");

        // 1. ПОВЕРТАЄМО ВІДОБРАЖЕННЯ UI МЕНЮ
        if (isPaused)
        {
            // Скидаємо стан підтвердження "Are you sure?" — інакше воно зависає
            // з минулого сеансу паузи і при наступному відкритті всі інші кнопки
            // одразу неактивні.
            ResetGiveUpState();
            try { CloseAllOtherActivePanels(); } catch (System.Exception) { }
            StartCoroutine(ShowMenuRoutine());
        }
        else
        {
            ResetGiveUpState();
            StartCoroutine(HideMenuRoutine());
        }

        // НАДІЙНИЙ ПОШУК КОНТРОЛЕРА
        try
        {
            if (pauseSceneController != null && !pauseSceneController.gameObject.scene.IsValid())
            {
                pauseSceneController = null;
            }

            if (pauseSceneController == null)
            {
                pauseSceneController = UnityEngine.Object.FindFirstObjectByType<PauseSceneController>(FindObjectsInactive.Include);
            }
        }
        catch (System.Exception) { }

        // 2. ЧИСТИЙ БЛОК ПАУЗИ (БЕЗ ВІДКЛЮЧЕННЯ ТЕРЕЙНУ)
        if (isPaused)
        {
            if (pauseSceneController != null)
            {
                if (!pauseSceneController.gameObject.activeInHierarchy)
                {
                    pauseSceneController.gameObject.SetActive(true);
                }

                Time.timeScale = 1f;
                try { pauseSceneController.EnterPause(); } catch { }
            }
            else
            {
                Time.timeScale = 0f;
            }
        }
        // Фрагмент методу TogglePause() при виході з паузи:
        else
        {
            if (pauseSceneController != null)
            {
                Time.timeScale = 1f;
                try { pauseSceneController.ExitPause(); } catch { }
            }
            else
            {
                Time.timeScale = 1f;
            }

            // ЖОРСТКИЙ FAILSAFE КАМЕРИ
            CameraFollow camFollow = UnityEngine.Object.FindFirstObjectByType<CameraFollow>(FindObjectsInactive.Include);
            if (camFollow != null) camFollow.SnapToTarget();
        }

        if (dofEffect != null)
        {
            bool isShop = currentScene == "ShopScene";
            dofEffect.active = isShop || isPaused;
        }

        try { ToggleGameplayUIForPause(isPaused); } catch (System.Exception) { }

        // КЕРУВАННЯ КУРСОРOM
        if (currentScene == "ShopScene" || currentScene == "Menu" || currentScene == "MainMenu")
        {
            Cursor.visible = true;
            Cursor.lockState = CursorLockMode.None;
        }
        else
        {
            Cursor.visible = isPaused;
            Cursor.lockState = isPaused ? CursorLockMode.None : CursorLockMode.Locked;
        }
    }

    private void ToggleGameplayUIForPause(bool isPausedMenuVisible)
    {
        if (isPausedMenuVisible)
        {
            objectsHiddenByPause.Clear();
            if (gameplayPanels != null)
            {
                foreach (GameObject panel in gameplayPanels)
                {
                    if (panel != null && panel.activeSelf)
                    {
                        objectsHiddenByPause.Add(panel);
                        panel.SetActive(false);
                    }
                }
            }

            if (promptCanvasGroup != null && promptCanvasGroup.gameObject.activeSelf)
            {
                objectsHiddenByPause.Add(promptCanvasGroup.gameObject);
                promptCanvasGroup.gameObject.SetActive(false);
            }
            if (objectivePanelGroup != null && objectivePanelGroup.gameObject.activeSelf)
            {
                objectsHiddenByPause.Add(objectivePanelGroup.gameObject);
                objectivePanelGroup.gameObject.SetActive(false);
            }
            if (bossUIGroup != null && bossUIGroup.gameObject.activeSelf)
            {
                objectsHiddenByPause.Add(bossUIGroup.gameObject);
                bossUIGroup.gameObject.SetActive(false);
            }
            if (lowHealthVignette != null && lowHealthVignette.gameObject.activeSelf)
            {
                objectsHiddenByPause.Add(lowHealthVignette.gameObject);
                lowHealthVignette.gameObject.SetActive(false);
            }
            if (pickupPopupContainer != null && pickupPopupContainer.gameObject.activeSelf)
            {
                objectsHiddenByPause.Add(pickupPopupContainer.gameObject);
                pickupPopupContainer.gameObject.SetActive(false);
            }
            if (widgetContainer != null && widgetContainer.gameObject.activeSelf)
            {
                objectsHiddenByPause.Add(widgetContainer.gameObject);
                widgetContainer.gameObject.SetActive(false);
            }

            PlayerController pc = PlayerController.LocalInstance;
            if (pc == null) pc = UnityEngine.Object.FindFirstObjectByType<PlayerController>(FindObjectsInactive.Include);
            if (pc != null)
            {
                HidePlayerHUDElementForPause(pc.hpFill?.transform);
                HidePlayerHUDElementForPause(pc.dashStaminaFill?.transform);
                HidePlayerHUDElementForPause(pc.xpFill?.transform);
                HidePlayerHUDElementForPause(pc.levelText?.transform);
                HidePlayerHUDElementForPause(pc.crystalText?.transform);
            }
        }
        else
        {
            // ЖОРСТКИЙ FAILSAFE: Завжди вмикаємо UI назад, ігноруючи списки "що було вимкнено"
            if (gameplayPanels != null)
            {
                foreach (GameObject panel in gameplayPanels)
                {
                    if (panel != null) panel.SetActive(true);
                }
            }

            PlayerController pc = PlayerController.LocalInstance;
            if (pc == null) pc = UnityEngine.Object.FindFirstObjectByType<PlayerController>(FindObjectsInactive.Include);
            if (pc != null)
            {
                ForceShowParentPanel(pc.hpFill?.transform);
                ForceShowParentPanel(pc.dashStaminaFill?.transform);
                ForceShowParentPanel(pc.xpFill?.transform);
                ForceShowParentPanel(pc.levelText?.transform);
                ForceShowParentPanel(pc.crystalText?.transform);
            }

            // ==========================================
            // ФІКС: Відновлення Subtitle, Квестів та Босів
            // ==========================================
            if (promptCanvasGroup != null) promptCanvasGroup.gameObject.SetActive(true);
            if (objectivePanelGroup != null) objectivePanelGroup.gameObject.SetActive(true);
            if (bossUIGroup != null) bossUIGroup.gameObject.SetActive(true);
            // ==========================================

            if (lowHealthVignette != null) lowHealthVignette.gameObject.SetActive(true);
            if (pickupPopupContainer != null) pickupPopupContainer.gameObject.SetActive(true);
            if (widgetContainer != null) widgetContainer.gameObject.SetActive(true);

            objectsHiddenByPause.Clear();
        }
    }

    private void ForceShowParentPanel(Transform leaf)
    {
        if (leaf == null) return;
        Transform t = leaf;
        // Йдемо вгору, поки не знайдемо корінь панелі, який лежить прямо під Canvas
        while (t != null && t.parent != null && t.parent.GetComponent<Canvas>() == null)
        {
            t = t.parent;
        }

        if (t != null && t.parent != null && t.parent.GetComponent<Canvas>() != null)
        {
            t.gameObject.SetActive(true); // Вмикаємо саму панель
        }
    }

    private void HidePlayerHUDElementForPause(Transform leaf)
    {
        if (leaf == null) return;
        Transform t = leaf;
        while (t != null && t.parent != null && t.parent.GetComponent<Canvas>() == null)
            t = t.parent;

        if (t == null || t.parent == null || t.parent.GetComponent<Canvas>() == null) return;

        GameObject topPanel = t.gameObject;
        if (topPanel.activeSelf && !objectsHiddenByPause.Contains(topPanel))
        {
            objectsHiddenByPause.Add(topPanel);
            topPanel.SetActive(false);
        }
    }

    private IEnumerator ShowMenuRoutine()
    {
        if (pausePanelGroup == null) yield break;

        if (!pausePanelGroup.gameObject.activeSelf)
            pausePanelGroup.gameObject.SetActive(true);

        pausePanelGroup.blocksRaycasts = true;
        pausePanelGroup.interactable = true;

        float t = 0f;
        while (t < 1f)
        {
            t += Time.unscaledDeltaTime * 5f;
            pausePanelGroup.alpha = Mathf.Lerp(0f, 1f, t);
            yield return null;
        }
        pausePanelGroup.alpha = 1f;
    }

    private IEnumerator HideMenuRoutine()
    {
        if (pausePanelGroup == null) yield break;

        pausePanelGroup.blocksRaycasts = false;
        pausePanelGroup.interactable = false;

        float t = 0f;
        while (t < 1f)
        {
            t += Time.unscaledDeltaTime * 6f;
            pausePanelGroup.alpha = Mathf.Lerp(1f, 0f, t);
            yield return null;
        }

        pausePanelGroup.alpha = 0f;
        pausePanelGroup.gameObject.SetActive(false);
    }

    private IEnumerator AnimateButtonIn(RectTransform btn)
    {
        Vector3 targetScale = Vector3.one;
        float t = 0;
        while (t < 1) { t += Time.unscaledDeltaTime * 5f; float s = Mathf.Sin(t * Mathf.PI * 0.5f + 0.2f) * 1.15f; btn.localScale = new Vector3(s, s, s); yield return null; }
        btn.localScale = targetScale;
    }

    public void OnGiveUpClicked()
    {
        if (AudioManager.Instance != null) AudioManager.Instance.PlayUI(AudioID.UI_Click);
        string currentScene = SceneManager.GetActiveScene().name;

        if (!isConfirmingGiveUp)
        {
            isConfirmingGiveUp = true;
            if (giveUpText != null) giveUpText.text = LocalizationManager.Tr((currentScene == "GameScene") ? "You sure?\nAll journey progress will be lost" : "Are you sure?");
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
        if (giveUpText != null) giveUpText.text = LocalizationManager.Tr((currentScene == "GameScene") ? "Give Up" : "Back to Menu");
        foreach (var btn in pauseButtonGroups) { if (btn != null) { btn.alpha = 1f; btn.interactable = true; } }
    }

    // Wire to a "Restart Run" pause-menu button. Only exposed in arena
    // scenes — in camp / shop / menu we no-op so a stray button can't
    // wipe someone's day.
    public void OnRestartRunClicked()
    {
        if (AudioManager.Instance != null) AudioManager.Instance.PlayUI(AudioID.UI_Click);
        string currentScene = SceneManager.GetActiveScene().name;
        if (currentScene == "Menu" || currentScene == "MainMenu"
            || currentScene == "CampScene" || currentScene == "ShopScene") return;

        if (ResourceManager.Instance != null) ResourceManager.Instance.ClearRunInventory();
        FadeAndLoadScene(currentScene);
    }

    // Wire to a "Quit to Desktop" pause-menu button. Opens a proper
    // Yes/No modal — matches the main-menu confirm dialog rather than
    // the give-up-button two-tap so accidental clicks don't kill the
    // process.
    public void OnQuitToDesktopClicked()
    {
        if (AudioManager.Instance != null) AudioManager.Instance.PlayUI(AudioID.UI_Click);

        ConfirmDialog.Show(
            LocalizationManager.Tr("Really quit to desktop?"),
            onYes: () =>
            {
                try { SaveSystem.Save(); } catch (System.Exception) { }
                try { PlayerPrefs.Save(); } catch (System.Exception) { }
                Application.Quit();
#if UNITY_EDITOR
                UnityEditor.EditorApplication.isPlaying = false;
#endif
            });
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

        TogglePlayerHUDRoots(active);

        if (promptCanvasGroup != null)
        {
            if (!active) { promptCanvasGroup.alpha = 0f; promptCanvasGroup.blocksRaycasts = false; }
        }
        if (objectivePanelGroup != null)
        {
            if (!active) { objectivePanelGroup.alpha = 0f; objectivePanelGroup.blocksRaycasts = false; }
        }
        if (bossUIGroup != null)
        {
            if (!active && FindFirstObjectByType<TutorialBossAI>() == null)
            {
                bossUIGroup.alpha = 0f;
                bossUIGroup.blocksRaycasts = false;
            }
        }
        if (lowHealthVignette != null) lowHealthVignette.gameObject.SetActive(active);
        if (pickupPopupContainer != null) pickupPopupContainer.gameObject.SetActive(active);

        if (widgetContainer != null) widgetContainer.gameObject.SetActive(active);
    }

    private void TogglePlayerHUDRoots(bool active)
    {
        PlayerController pc = PlayerController.LocalInstance;
        if (pc == null) pc = FindFirstObjectByType<PlayerController>(FindObjectsInactive.Include);
        if (pc == null) return;
        RectTransform hudRect = GetComponent<RectTransform>();
        if (hudRect == null) return;

        ToggleAncestorPanel(pc.hpFill, hudRect, active);
        ToggleAncestorPanel(pc.dashStaminaFill, hudRect, active);
        ToggleAncestorPanel(pc.xpFill, hudRect, active);
        ToggleAncestorPanelText(pc.levelText, hudRect, active);
        ToggleAncestorPanelText(pc.crystalText, hudRect, active);
    }

    private void ToggleAncestorPanel(Image img, RectTransform hudRect, bool active)
    {
        if (img != null) ToggleTopCanvasPanel(img.transform, active);
    }

    private void ToggleAncestorPanelText(TMPro.TextMeshProUGUI txt, RectTransform hudRect, bool active)
    {
        if (txt != null) ToggleTopCanvasPanel(txt.transform, active);
    }

    private void ToggleTopCanvasPanel(Transform leaf, bool active)
    {
        if (leaf == null) return;
        Transform t = leaf;
        while (t != null && t.parent != null && t.parent.GetComponent<Canvas>() == null)
            t = t.parent;
        if (t == null || t.parent == null || t.parent.GetComponent<Canvas>() == null) return;

        GameObject go = t.gameObject;

        if (active)
        {
            if (hiddenByGlobalHUD.Remove(go) && !go.activeSelf) go.SetActive(true);
            return;
        }

        string n = go.name.ToLower();
        bool looksLikeHUDPanel =
            n.Contains("hp") || n.Contains("health") ||
            n.Contains("stamina") || n.Contains("dash") ||
            n.Contains("xp") || n.Contains("level") || n.Contains("lvl") ||
            n.Contains("diamond") || n.Contains("crystal") ||
            n.Contains("hud") || n.Contains("playerui");
        if (!looksLikeHUDPanel) return;

        if (go.activeSelf)
        {
            go.SetActive(false);
            hiddenByGlobalHUD.Add(go);
        }
    }

    private Image lowHealthVignette;
    private float lowHealthAlpha;

    private void CreateLowHealthVignetteIfNeeded()
    {
        if (lowHealthVignette != null) return;
        RectTransform hudRect = GetComponent<RectTransform>();
        if (hudRect == null) return;

        GameObject go = new GameObject("LowHealthVignette");
        RectTransform rt = go.AddComponent<RectTransform>();
        rt.SetParent(hudRect, false);
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
        rt.SetAsFirstSibling();

        lowHealthVignette = go.AddComponent<Image>();
        lowHealthVignette.color = new Color(0.6f, 0f, 0.02f, 0f);
        lowHealthVignette.raycastTarget = false;
        lowHealthVignette.maskable = false;
    }

    private void UpdateLowHealthVignette()
    {
        if (lowHealthVignette == null) return;

        if (PlayerPrefs.GetInt("Settings_LowHpVignette", 1) != 1)
        {
            lowHealthAlpha = Mathf.Lerp(lowHealthAlpha, 0f, Time.unscaledDeltaTime * 8f);
            Color cc = lowHealthVignette.color; cc.a = lowHealthAlpha; lowHealthVignette.color = cc;
            return;
        }

        PlayerController pc = PlayerController.LocalInstance;
        float targetAlpha = 0f;
        if (pc != null && !pc.isCampMode && pc.maxHealth > 0f)
        {
            float ratio = pc.currentHealth / pc.maxHealth;
            if (ratio < 0.25f && ratio > 0f)
            {
                float danger = 1f - (ratio / 0.25f);
                float pulse = (Mathf.Sin(Time.unscaledTime * (3f + danger * 4f)) * 0.5f + 0.5f);
                targetAlpha = Mathf.Lerp(0.08f, 0.28f, danger) * Mathf.Lerp(0.8f, 1f, pulse);
            }
        }
        lowHealthAlpha = Mathf.Lerp(lowHealthAlpha, targetAlpha, Time.unscaledDeltaTime * 8f);
        Color c = lowHealthVignette.color; c.a = lowHealthAlpha; lowHealthVignette.color = c;
    }

    private RectTransform pickupPopupContainer;
    private readonly List<RectTransform> activePickupPopups = new List<RectTransform>();
    // Track popup coroutines separately so ClearAllPickupPopups can
    // stop ONLY the popup animations, not the HUD's own boss-fade /
    // prompt-fade / cinematic-bars / etc coroutines (StopAllCoroutines
    // used to kill everything).
    private readonly List<Coroutine> activePickupCoroutines = new List<Coroutine>();

    private void CreatePickupPopupContainerIfNeeded()
    {
        if (pickupPopupContainer != null) return;
        RectTransform hudRect = GetComponent<RectTransform>();
        if (hudRect == null) return;

        GameObject go = new GameObject("PickupPopupContainer");
        RectTransform rt = go.AddComponent<RectTransform>();
        rt.SetParent(hudRect, false);
        rt.anchorMin = new Vector2(0f, 0f);
        rt.anchorMax = new Vector2(0f, 0f);
        rt.pivot = new Vector2(0f, 0f);
        rt.anchoredPosition = new Vector2(60f, 270f);
        rt.sizeDelta = new Vector2(400f, 300f);
        pickupPopupContainer = rt;
    }

    // Hard cap on concurrent pickup popups. Prevents the "wall of text"
    // stack when the player rapidly picks up dozens of resources.
    private const int MAX_PICKUP_POPUPS = 8;

    public void ShowPickupPopup(string text, Color color)
    {
        if (!gameObject.activeInHierarchy) return;
        CreatePickupPopupContainerIfNeeded();
        if (pickupPopupContainer == null) return;

        // Kill oldest until we're under the cap. Prevents unbounded growth
        // if pickups fire faster than the fade lifetime (e.g. clearing a
        // pile of gems).
        for (int i = activePickupPopups.Count - 1; i >= 0; i--)
        {
            if (activePickupPopups[i] == null) activePickupPopups.RemoveAt(i);
        }
        while (activePickupPopups.Count >= MAX_PICKUP_POPUPS)
        {
            var oldest = activePickupPopups[0];
            activePickupPopups.RemoveAt(0);
            if (oldest != null) Destroy(oldest.gameObject);
        }

        activePickupCoroutines.Add(StartCoroutine(PickupPopupRoutine(text, color)));
    }

    private IEnumerator PickupPopupRoutine(string text, Color color)
    {
        GameObject go = new GameObject("PickupPopup");
        RectTransform rt = go.AddComponent<RectTransform>();
        rt.SetParent(pickupPopupContainer, false);
        rt.pivot = new Vector2(0f, 0.5f);
        rt.anchorMin = new Vector2(0f, 0f);
        rt.anchorMax = new Vector2(0f, 0f);
        rt.sizeDelta = new Vector2(400f, 36f);
        rt.anchoredPosition = new Vector2(0f, 0f);

        TextMeshProUGUI tmp = go.AddComponent<TextMeshProUGUI>();
        tmp.text = text;
        tmp.fontSize = 26f;
        tmp.fontStyle = FontStyles.Bold;
        tmp.color = color;
        tmp.outlineWidth = 0.18f;
        tmp.outlineColor = Color.black;
        tmp.alignment = TextAlignmentOptions.Left;
        tmp.raycastTarget = false;

        activePickupPopups.Add(rt);
        RestackPickupPopups();

        // Hard failsafe on UNSCALED realtime — Unity's `Destroy(go, delay)`
        // uses scaled time, so at Time.timeScale==0 (pause / cinematic
        // freeze) the delay never elapses and orphan popups sit on screen
        // forever. Kick off a parallel coroutine that always destroys past
        // the fade lifetime regardless of timescale.
        activePickupCoroutines.Add(StartCoroutine(PickupPopupFailsafeRoutine(go, rt, 2.2f)));

        const float lifetime = 1.8f;
        float t = 0f;
        Vector2 startScale = Vector2.one * 0.7f;
        rt.localScale = startScale;

        while (t < lifetime)
        {
            // rt / tmp may have been destroyed by ClearAllPickupPopups —
            // bail cleanly instead of hitting a null-deref on the next
            // frame's access.
            if (rt == null || tmp == null) yield break;

            t += Time.unscaledDeltaTime;
            float k = t / lifetime;
            if (k < 0.12f) rt.localScale = Vector3.LerpUnclamped(startScale, Vector3.one * 1.05f, k / 0.12f);
            else if (k < 0.22f) rt.localScale = Vector3.LerpUnclamped(Vector3.one * 1.05f, Vector3.one, (k - 0.12f) / 0.1f);
            else rt.localScale = Vector3.one;

            Color cc = tmp.color;
            cc.a = k > 0.7f ? Mathf.Lerp(1f, 0f, (k - 0.7f) / 0.3f) : 1f;
            tmp.color = cc;

            yield return null;
        }

        activePickupPopups.Remove(rt);
        if (go != null) Destroy(go);
        RestackPickupPopups();
    }

    // Realtime destroy — survives Time.timeScale==0 pauses.
    private IEnumerator PickupPopupFailsafeRoutine(GameObject go, RectTransform rt, float delayRealtime)
    {
        yield return new WaitForSecondsRealtime(delayRealtime);
        if (go != null)
        {
            if (rt != null) activePickupPopups.Remove(rt);
            Destroy(go);
            RestackPickupPopups();
        }
    }

    // Prune stale null entries — the failsafe Destroy above may leave holes
    // in activePickupPopups if a coroutine was cancelled mid-lifetime.
    private void LateUpdate()
    {
        for (int i = activePickupPopups.Count - 1; i >= 0; i--)
            if (activePickupPopups[i] == null) activePickupPopups.RemoveAt(i);
    }

    private void RestackPickupPopups()
    {
        for (int i = 0; i < activePickupPopups.Count; i++)
        {
            RectTransform rt = activePickupPopups[i];
            if (rt == null) continue;
            float targetY = (activePickupPopups.Count - 1 - i) * 36f;
            rt.anchoredPosition = new Vector2(0f, targetY);
        }
    }

    private void ClearAllPickupPopups()
    {
        // Only stop the popup coroutines — not every routine on the HUD.
        // Previously this called StopAllCoroutines() which nuked
        // bossUIFadeRoutine, promptFadeCoroutine, promptTypingCoroutine,
        // cinematic bar routines, skip-prompt routines, etc.
        for (int i = 0; i < activePickupCoroutines.Count; i++)
        {
            if (activePickupCoroutines[i] != null) StopCoroutine(activePickupCoroutines[i]);
        }
        activePickupCoroutines.Clear();

        for (int i = 0; i < activePickupPopups.Count; i++)
        {
            if (activePickupPopups[i] == null) continue;
            Destroy(activePickupPopups[i].gameObject);
        }
        activePickupPopups.Clear();
        if (pickupPopupContainer != null)
        {
            for (int i = pickupPopupContainer.childCount - 1; i >= 0; i--)
                Destroy(pickupPopupContainer.GetChild(i).gameObject);
        }
    }
}