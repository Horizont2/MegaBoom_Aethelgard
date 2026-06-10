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

        // 1. Fade Out (Затемнення екрану)
        if (blackFadeGroup != null)
        {
            blackFadeGroup.gameObject.SetActive(true);
            while (blackFadeGroup.alpha < 1f)
            {
                blackFadeGroup.alpha += Time.unscaledDeltaTime * sceneFadeSpeed * 2f;
                yield return null;
            }
        }

        // 2. Вмикаємо UI завантаження
        if (loadingCanvasGroup != null)
        {
            loadingCanvasGroup.gameObject.SetActive(true);
            loadingCanvasGroup.alpha = 1f;
        }

        if (hintCoroutine != null) StopCoroutine(hintCoroutine);
        if (gameHints.Length > 0) hintCoroutine = StartCoroutine(HintRoutine());

        // Знижуємо пріоритет фонового потоку, щоб не фризило анімацію завантаження
        Application.backgroundLoadingPriority = ThreadPriority.Low;

        AsyncOperation asyncLoad = SceneManager.LoadSceneAsync(sceneName);
        asyncLoad.allowSceneActivation = false;

        // ФАЗА 1: Завантаження асетів рушієм
        while (asyncLoad.progress < 0.9f)
        {
            float rawProgress = Mathf.Clamp01(asyncLoad.progress / 0.9f);
            int displayPercent = Mathf.FloorToInt(rawProgress * 50f);
            if (loadingText != null) loadingText.text = $"LOADING ASSETS... {displayPercent}%";
            yield return null;
        }

        // ВАЖЛИВИЙ ФІКС: Віддаємо всі ресурси процесора на генерацію сцени!
        Application.backgroundLoadingPriority = ThreadPriority.High;

        // 3. Активація сцени (Тут буде основний спайк процесора)
        asyncLoad.allowSceneActivation = true;
        while (!asyncLoad.isDone) yield return null;

        // Повертаємо нормальний пріоритет після важкого завантаження
        Application.backgroundLoadingPriority = ThreadPriority.Normal;

        // ФАЗА 2: Генерація світу
        WorldGenerator worldGen = FindFirstObjectByType<WorldGenerator>();

        if (worldGen != null)
        {
            while (!WorldGenerator.IsGenerationDone)
            {
                // Отримуємо прогрес генерації (0.0 до 1.0) і конвертуємо в діапазон 50-100%
                int displayPercent = Mathf.FloorToInt(50f + (Mathf.Clamp01(WorldGenerator.CurrentProgress) * 50f));
                if (loadingText != null) loadingText.text = $"GENERATING WORLD... {displayPercent}%";
                yield return null;
            }
        }

        if (loadingText != null) loadingText.text = "READY";
        yield return new WaitForSecondsRealtime(0.5f); // Коротка пауза, щоб гравець побачив 100%

        // 4. Fade In (Прибираємо екрани завантаження)
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

        // 5. Запуск логіки гри
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
                hintText.text = gameHints[Random.Range(0, gameHints.Length)];
            yield return new WaitForSecondsRealtime(hintChangeInterval);
        }
    }
}