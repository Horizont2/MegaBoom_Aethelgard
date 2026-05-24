using UnityEngine;
using UnityEngine.UI;
using TMPro;
using UnityEngine.SceneManagement;
using System.Collections;

public class LoadingManager : MonoBehaviour
{
    public static LoadingManager Instance;

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

    public bool isLoading = false;
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

        if (blackFadeGroup != null)
        {
            blackFadeGroup.gameObject.SetActive(true);
            while (blackFadeGroup.alpha < 1f)
            {
                float dt = Mathf.Min(Time.unscaledDeltaTime, 0.1f);
                blackFadeGroup.alpha += dt * sceneFadeSpeed * 2f;
                yield return null;
            }
        }

        if (loadingCanvasGroup != null)
        {
            loadingCanvasGroup.gameObject.SetActive(true);
            loadingCanvasGroup.alpha = 1f;
        }

        if (hintCoroutine != null) StopCoroutine(hintCoroutine);
        hintCoroutine = StartCoroutine(HintRoutine());

        Application.backgroundLoadingPriority = ThreadPriority.Low;

        AsyncOperation asyncLoad = SceneManager.LoadSceneAsync(sceneName);
        asyncLoad.allowSceneActivation = false;

        float visualProgress = 0f;

        // ФАЗА 1: Завантаження асетів (0% - 50%)
        while (asyncLoad.progress < 0.9f || visualProgress < 1f)
        {
            // Обмежуємо dt, щоб уникнути різких стрибків відсотків через лаги рушія
            float dt = Mathf.Min(Time.unscaledDeltaTime, 0.1f);
            float targetProgress = asyncLoad.progress / 0.9f;

            visualProgress = Mathf.MoveTowards(visualProgress, targetProgress, dt * 1.5f);
            if (loadingText != null) loadingText.text = $"LOADING ASSETS... {Mathf.FloorToInt(visualProgress * 50)}%";

            yield return null;
        }

        yield return new WaitForEndOfFrame();

        // 3. Активація сцени
        asyncLoad.allowSceneActivation = true;
        while (!asyncLoad.isDone) yield return null;

        // ФАЗА 2: Генерація світу (50% - 100%)
        WorldGenerator worldGen = FindFirstObjectByType<WorldGenerator>();

        Application.backgroundLoadingPriority = ThreadPriority.Normal;

        if (worldGen != null)
        {
            float genProgress = 0f;
            while (!WorldGenerator.IsGenerationDone)
            {
                float dt = Mathf.Min(Time.unscaledDeltaTime, 0.1f);
                // Фейковий плавний прогрес генерації, який доходить максимум до 99%
                genProgress = Mathf.MoveTowards(genProgress, 1f, dt * 0.5f);
                int displayPercent = Mathf.FloorToInt(50f + (genProgress * 49f));

                if (loadingText != null) loadingText.text = $"GENERATING WORLD... {displayPercent}%";
                yield return null;
            }
        }
        else
        {
            // Якщо генератора немає (ми вантажимо Табір)
            float prepProgress = 0f;
            while (prepProgress < 1f)
            {
                float dt = Mathf.Min(Time.unscaledDeltaTime, 0.1f);
                prepProgress += dt * 1.5f;
                if (loadingText != null) loadingText.text = $"PREPARING SCENE... {Mathf.FloorToInt(50 + (prepProgress * 50))}%";
                yield return null;
            }
        }

        if (loadingText != null) loadingText.text = "READY";

        yield return new WaitForSecondsRealtime(0.3f);

        if (loadingCanvasGroup != null)
        {
            while (loadingCanvasGroup.alpha > 0f)
            {
                float dt = Mathf.Min(Time.unscaledDeltaTime, 0.1f);
                loadingCanvasGroup.alpha -= dt * sceneFadeSpeed;
                yield return null;
            }
            loadingCanvasGroup.gameObject.SetActive(false);
        }

        if (blackFadeGroup != null)
        {
            while (blackFadeGroup.alpha > 0f)
            {
                float dt = Mathf.Min(Time.unscaledDeltaTime, 0.1f);
                blackFadeGroup.alpha -= dt * sceneFadeSpeed;
                yield return null;
            }
            blackFadeGroup.gameObject.SetActive(false);
        }

        isLoading = false;

        // --- ЗАПУСК ТАЙМЕРА ПІСЛЯ ЗНИКНЕННЯ ЕКРАНУ ---
        if (GameManager.Instance != null)
        {
            GameManager.Instance.StartLevelTimer();
        }
    }

    private IEnumerator HintRoutine()
    {
        while (true)
        {
            if (gameHints.Length > 0 && hintText != null)
                hintText.text = gameHints[Random.Range(0, gameHints.Length)];
            yield return new WaitForSecondsRealtime(hintChangeInterval);
        }
    }
}