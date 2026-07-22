using UnityEngine;
using UnityEngine.UI;
using TMPro;
using UnityEngine.SceneManagement;
using System.Collections;

public class LoadingManager : MonoBehaviour
{
    public static LoadingManager Instance { get; private set; }

    [Header("UI References")]
    public CanvasGroup loadingCanvasGroup;
    public TextMeshProUGUI loadingText;
    public TextMeshProUGUI hintText;
    public CanvasGroup blackFadeGroup;
    public RectTransform loadingSpinner;

    [Header("Dynamic Backgrounds")]
    public Image loadingArt;
    public Sprite[] loadingSprites;

    [Header("Settings")]
    public float sceneFadeSpeed = 1.5f;
    public float hintChangeInterval = 5f;
    public float spinnerRotationSpeed = 150f;

    [TextArea(2, 3)]
    public string[] gameHints;

    public bool isLoading { get; private set; } = false;
    private Coroutine hintCoroutine;

    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);

            if (loadingCanvasGroup != null) { loadingCanvasGroup.alpha = 0f; loadingCanvasGroup.gameObject.SetActive(false); }
            if (blackFadeGroup != null) { blackFadeGroup.alpha = 0f; blackFadeGroup.gameObject.SetActive(false); }
        }
        else
        {
            Destroy(gameObject);
        }
    }

    private void Update()
    {
        if (isLoading && loadingSpinner != null)
        {
            loadingSpinner.Rotate(0, 0, -spinnerRotationSpeed * Time.unscaledDeltaTime);
        }
    }

    public void LoadScene(string sceneName)
    {
        if (isLoading) return;

        if (loadingSprites != null && loadingSprites.Length > 0 && loadingArt != null)
        {
            loadingArt.sprite = loadingSprites[Random.Range(0, loadingSprites.Length)];
        }

        StartCoroutine(LoadRoutine(sceneName));
    }

    private IEnumerator LoadRoutine(string sceneName)
    {
        isLoading = true;

        // Блокуємо керування гравцем відразу при початку завантаження
        if (PlayerController.LocalInstance != null)
        {
            PlayerController.LocalInstance.isControlBlocked = true;
        }

        // 1. Fade Out (затемнення екрану)
        if (blackFadeGroup != null)
        {
            blackFadeGroup.gameObject.SetActive(true);
            while (blackFadeGroup.alpha < 1f)
            {
                blackFadeGroup.alpha += Time.unscaledDeltaTime * sceneFadeSpeed * 2f;
                yield return null;
            }
        }

        // 2. Показуємо UI завантаження
        if (loadingCanvasGroup != null)
        {
            loadingCanvasGroup.gameObject.SetActive(true);
            loadingCanvasGroup.alpha = 1f;
        }

        if (hintCoroutine != null) StopCoroutine(hintCoroutine);
        if (gameHints.Length > 0) hintCoroutine = StartCoroutine(HintRoutine());

        Application.backgroundLoadingPriority = ThreadPriority.Low;

        AsyncOperation asyncLoad = SceneManager.LoadSceneAsync(sceneName);
        asyncLoad.allowSceneActivation = false;

        // Чекаємо готовності сцени на 90%
        while (asyncLoad.progress < 0.9f)
        {
            float rawProgress = Mathf.Clamp01(asyncLoad.progress / 0.9f);
            int displayPercent = Mathf.FloorToInt(rawProgress * 50f);
            if (loadingText != null) loadingText.text = LocalizationManager.Tr("LOADING ASSETS... {0}%", displayPercent);
            yield return null;
        }

        Application.backgroundLoadingPriority = ThreadPriority.High;

        // Активуємо сцену
        asyncLoad.allowSceneActivation = true;
        while (!asyncLoad.isDone) yield return null;

        Application.backgroundLoadingPriority = ThreadPriority.Normal;

        // 3. Чекаємо поки WorldGenerator згенерує світ
        WorldGenerator worldGen = FindFirstObjectByType<WorldGenerator>();
        if (worldGen != null)
        {
            while (!WorldGenerator.IsGenerationDone)
            {
                int displayPercent = Mathf.FloorToInt(50f + (Mathf.Clamp01(WorldGenerator.CurrentProgress) * 50f));
                if (loadingText != null) loadingText.text = LocalizationManager.Tr("GENERATING WORLD... {0}%", displayPercent);
                yield return null;
            }
        }

        // Розблоковуємо гравця, бо сцена завантажена і світ згенеровано
        if (PlayerController.LocalInstance != null)
        {
            PlayerController.LocalInstance.isControlBlocked = false;
        }

        if (loadingText != null) loadingText.text = LocalizationManager.Tr("READY");
        yield return new WaitForSecondsRealtime(0.5f);

        // 4. Fade In (плавне повернення до гри)
        if (loadingCanvasGroup != null)
        {
            while (loadingCanvasGroup.alpha > 0f)
            {
                loadingCanvasGroup.alpha -= Time.unscaledDeltaTime * sceneFadeSpeed;
                yield return null;
            }
            loadingCanvasGroup.gameObject.SetActive(false);
        }

        if (blackFadeGroup != null)
        {
            while (blackFadeGroup.alpha > 0f)
            {
                blackFadeGroup.alpha -= Time.unscaledDeltaTime * sceneFadeSpeed;
                yield return null;
            }
            blackFadeGroup.gameObject.SetActive(false);
        }

        if (hintCoroutine != null) StopCoroutine(hintCoroutine);
        isLoading = false;

        if (GameManager.Instance != null)
        {
            GameManager.Instance.StartLevelTimer();
        }
    }

    private IEnumerator HintRoutine()
    {
        while (true)
        {
            if (hintText != null)
                // Wrap through Tr — hint strings are authored in English
                // in the Inspector; the localisation table can override
                // each one via self-keyed entries.
                hintText.text = LocalizationManager.Tr(gameHints[Random.Range(0, gameHints.Length)]);
            yield return new WaitForSecondsRealtime(hintChangeInterval);
        }
    }
}