using UnityEngine;
using UnityEngine.UI;
using TMPro;
using UnityEngine.SceneManagement;
using System.Collections;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

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

    [Header("Pause Menu Settings")]
    public CanvasGroup pausePanelGroup;
    public GameObject[] pauseButtons;
    public CanvasGroup[] pauseButtonGroups;
    public CanvasGroup giveUpButtonGroup;
    public TextMeshProUGUI giveUpText;
    public float buttonDelay = 0.05f;

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
    }

    private void OnEnable() { SceneManager.sceneLoaded += OnSceneLoaded; }
    private void OnDisable() { SceneManager.sceneLoaded -= OnSceneLoaded; }

    private void Update()
    {
        if (LoadingManager.Instance != null && LoadingManager.Instance.isLoading) return;

        if (Input.GetKeyDown(KeyCode.Escape))
        {
            // Безпечна перевірка SettingsUI
            if (SettingsUI.Instance != null && SettingsUI.Instance.settingsPanel != null && SettingsUI.Instance.settingsPanel.activeInHierarchy)
            {
                SettingsUI.Instance.CloseSettings();
                return;
            }

            // Безпечна перевірка Дошки Оголошень
            NoticeBoardManager noticeBoard = FindFirstObjectByType<NoticeBoardManager>();
            if (noticeBoard != null && noticeBoard.isBoardOpen)
            {
                noticeBoard.CloseBoard();
                return;
            }

            // Якщо мапа відкрита через стіл, HUD просто ігнорує ESC, 
            // даючи скрипту мапи можливість закритися самостійно.
            if (MapTableInteract.IsMapActive) return;

            // Якщо мапа відкрита через інтерфейс
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
                    if (isTutorial && (panel.name == "Resources" || panel.name == "MissionUIParent"))
                    {
                        panel.SetActive(false);
                    }
                    else
                    {
                        panel.SetActive(showGameplayUI);
                    }
                }
            }
        }

        if (isTutorial)
        {
            Transform res = transform.Find("Resources");
            if (res != null) res.gameObject.SetActive(false);

            Transform campMissions = transform.Find("MissionUIParent");
            if (campMissions != null) campMissions.gameObject.SetActive(false);
        }

        if (promptCanvasGroup != null) promptCanvasGroup.alpha = 0f;

        if (isPaused)
        {
            isPaused = false;
            Time.timeScale = 1f;
            if (pausePanelGroup != null)
            {
                pausePanelGroup.alpha = 0f;
                pausePanelGroup.gameObject.SetActive(false);
            }
        }
    }

    private IEnumerator SyncCameraAndVolumeRoutine()
    {
        yield return null;
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
            if (v.isGlobal && v.profile != null)
            {
                if (v.profile.TryGet(out dofEffect))
                {
                    bool isShop = SceneManager.GetActiveScene().name == "ShopScene";
                    dofEffect.active = isShop || isPaused;
                    break;
                }
            }
        }
    }

    public void FadeAndLoadScene(string sceneName)
    {
        if (isPaused) TogglePause();
        if (LoadingManager.Instance != null) LoadingManager.Instance.LoadScene(sceneName);
        else SceneManager.LoadScene(sceneName);
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

        // Гарантуємо, що Canvas точно увімкнений
        Canvas canvas = GetComponent<Canvas>();
        if (canvas != null && !canvas.enabled) canvas.enabled = true;

        if (isPaused && SettingsUI.Instance != null && SettingsUI.Instance.settingsPanel != null && SettingsUI.Instance.settingsPanel.activeInHierarchy)
        {
            SettingsUI.Instance.CloseSettings();
        }

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
            if (giveUpText != null)
            {
                giveUpText.text = (currentScene == "GameScene") ? "Give Up" : "Back to Menu";
            }
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

            if (giveUpText != null)
            {
                giveUpText.text = (currentScene == "GameScene") ? "You sure?\nAll journey progress will be lost" : "Are you sure?";
            }

            foreach (var btn in pauseButtonGroups)
            {
                if (btn != null && btn != giveUpButtonGroup)
                {
                    btn.alpha = 0.3f;
                    btn.interactable = false;
                }
            }
        }
        else
        {
            if (currentScene == "GameScene")
            {
                if (ResourceManager.Instance != null) ResourceManager.Instance.ClearRunInventory();
                FadeAndLoadScene("CampScene");
            }
            else
            {
                FadeAndLoadScene("Menu");
            }
        }
    }

    private void ResetGiveUpState()
    {
        isConfirmingGiveUp = false;
        string currentScene = SceneManager.GetActiveScene().name;

        if (giveUpText != null)
        {
            giveUpText.text = (currentScene == "GameScene") ? "Give Up" : "Back to Menu";
        }

        foreach (var btn in pauseButtonGroups)
        {
            if (btn != null)
            {
                btn.alpha = 1f;
                btn.interactable = true;
            }
        }
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