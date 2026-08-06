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
        // �����Ҳ�: ����������� ���, ���� ����� ���� �������!
        Time.timeScale = 1f;

        // Only wipe the run timer on a FRESH scene load — a "Continue"
        // resumes the persisted position, so the timer must resume too
        // (was zeroing 00:00 even mid-run).
        bool continuing = PlayerPrefs.GetInt("IsContinuing", 0) == 1;
        if (!continuing)
        {
            survivalTime = 0f;
        }
        nextSurvivalTick = Mathf.Floor(survivalTime) + 1f;
        isGameOver = false;
        isTimerActive = false;

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

        // ������� ������: ������ ���������� ��������� ����
        WorldGenerator worldGen = FindFirstObjectByType<WorldGenerator>();
        if (worldGen != null)
        {
            // Failsafe timeout — if world generation throws mid-coroutine
            // (a spawn routine exception kills the whole GenerateWorld
            // chain before it sets IsGenerationDone), this used to spin
            // forever and the survival timer never started (stuck 00:00).
            // Cap the wait at 30s of real time so the timer always begins
            // even on a partially-failed generation.
            float genWaitTimeout = 30f;
            float genWaitElapsed = 0f;
            while (!WorldGenerator.IsGenerationDone && genWaitElapsed < genWaitTimeout)
            {
                genWaitElapsed += Time.unscaledDeltaTime;
                yield return null;
            }
            if (!WorldGenerator.IsGenerationDone)
                Debug.LogWarning("[GameManager] World generation didn't report done within 30s — starting the survival timer anyway. Check the Console for a generation exception.");
        }

        yield return new WaitForSeconds(0.5f);
        StartLevelTimer();
    }

    private void Update()
    {
        if (isGameOver || !isTimerActive) return;

        survivalTime += Time.deltaTime;

        if (survivalTime >= nextSurvivalTick)
        {
            nextSurvivalTick += 1f;

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
            // Only rewrite the TMP when the displayed whole-second value
            // actually changes. Previously wrote every frame — 60 string
            // allocs + TMP relayouts per second for a display that only
            // ticks once per second.
            int totalSec = Mathf.FloorToInt(survivalTime);
            if (totalSec != lastTimerSecond)
            {
                lastTimerSecond = totalSec;
                int minutes = totalSec / 60;
                int seconds = totalSec % 60;
                timerText.text = string.Format("{0:00}:{1:00}", minutes, seconds);
            }
        }
    }

    private int lastTimerSecond = -1;

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

        if (AudioManager.Instance != null) AudioManager.Instance.PlaySFX(AudioID.UI_GameOver);

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
        else SceneLoader.LoadScene("CampScene");
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
        else SceneLoader.LoadScene("Menu");
    }
}