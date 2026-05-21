using UnityEngine;
using UnityEngine.SceneManagement;
using System.Collections;
using TMPro;

public class GameManager : MonoBehaviour
{
    public static GameManager Instance;

    [Header("Region Data")]
    public RegionData currentRegion;
    public bool isRegionMission = false;

    [Header("UI References")]
    public CanvasGroup gameOverPanel;
    public TextMeshProUGUI timerText;

    [Header("Settings")]
    public float fadeDuration = 2f;
    public float waitBeforeRestart = 1.5f;

    public static float survivalTime = 0f;
    private bool isGameOver = false;
    public bool isTimerActive = false;

    private float nextSurvivalTick = 1f;

    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            transform.parent = null;
            DontDestroyOnLoad(gameObject);

            SceneManager.sceneLoaded += OnSceneLoaded;
        }
        else
        {
            Destroy(gameObject);
        }
    }

    private void OnDestroy()
    {
        if (Instance == this) SceneManager.sceneLoaded -= OnSceneLoaded;
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        survivalTime = 0f;
        nextSurvivalTick = 1f;
        isGameOver = false;
        isTimerActive = false; // Таймер стоїть, поки йде завантаження

        GameObject timerObj = GameObject.Find("TimerText");
        if (timerObj != null) timerText = timerObj.GetComponent<TextMeshProUGUI>();

        GameObject panelObj = GameObject.Find("GameOverPanel");
        if (panelObj != null) gameOverPanel = panelObj.GetComponent<CanvasGroup>();

        if (gameOverPanel != null) gameOverPanel.alpha = 0f;

        StartCoroutine(CheckForLoadingManager());
    }

    private void Start()
    {
        survivalTime = 0f;
        nextSurvivalTick = 1f;
        isGameOver = false;
    }

    private IEnumerator CheckForLoadingManager()
    {
        yield return null;
        // Якщо екрану завантаження немає (тест в редакторі), стартуємо одразу
        if (LoadingManager.Instance == null) StartLevelTimer();
    }

    private void Update()
    {
        if (isGameOver || !isTimerActive) return;

        survivalTime += Time.deltaTime;

        // Коли проходить секунда
        if (survivalTime >= nextSurvivalTick)
        {
            nextSurvivalTick += 1f;

            // ФІКС ЗАВИСАННЯ: Захищаємо код від крашу, якщо MissionManager глючить
            try
            {
                if (MissionManager.Instance != null)
                {
                    MissionManager.Instance.AddProgress(MissionType.Survive, 1);
                }
            }
            catch (System.Exception e)
            {
                Debug.LogWarning("MissionManager error suppressed to keep timer running: " + e.Message);
            }
        }

        if (timerText != null)
        {
            int minutes = Mathf.FloorToInt(survivalTime / 60f);
            int seconds = Mathf.FloorToInt(survivalTime % 60f);
            timerText.text = string.Format("{0:00}:{1:00}", minutes, seconds);
        }
    }

    public void StartLevelTimer()
    {
        isTimerActive = true;
    }

    public void TriggerGameOver()
    {
        if (isGameOver) return;

        isGameOver = true;
        isTimerActive = false;

        PlayerPrefs.SetInt("IsRunActive", 0);
        PlayerPrefs.Save();

        StartCoroutine(GameOverSequence());
    }

    private IEnumerator GameOverSequence()
    {
        float timer = 0f;
        while (timer < fadeDuration)
        {
            timer += Time.deltaTime;
            if (gameOverPanel != null) gameOverPanel.alpha = Mathf.Clamp01(timer / fadeDuration);
            yield return null;
        }

        yield return new WaitForSeconds(waitBeforeRestart);

        if (GlobalHUD.Instance != null) GlobalHUD.Instance.FadeAndLoadScene("CampScene");
        else SceneManager.LoadScene("CampScene");
    }

    public void ReturnToMenu()
    {
        PlayerPrefs.SetInt("IsRunActive", 1);

        GameObject player = GameObject.FindGameObjectWithTag("Player");
        if (player != null)
        {
            PlayerPrefs.SetFloat("PlayerPosX", player.transform.position.x);
            PlayerPrefs.SetFloat("PlayerPosY", player.transform.position.y);
            PlayerPrefs.SetFloat("PlayerPosZ", player.transform.position.z);
        }

        PlayerPrefs.Save();

        if (GlobalHUD.Instance != null) GlobalHUD.Instance.FadeAndLoadScene("Menu");
        else SceneManager.LoadScene("Menu");
    }
}