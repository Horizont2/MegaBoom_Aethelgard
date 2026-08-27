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

    // Guards the Start() failsafe below: true once any scene has been
    // initialised (via OnSceneLoaded or the Start fallback).
    private bool sceneInitDone = false;

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        sceneInitDone = true;
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
        // Force the next Update to rewrite the timer TMP even if the whole-
        // second value is unchanged — the object was just (re)linked.
        lastTimerSecond = -1;

        // Re-link the timer TMP on EVERY scene load. This singleton persists
        // (DontDestroyOnLoad), so after a "Try Again" reload its inspector-
        // assigned reference points at the DESTROYED text object from the
        // previous life — the display then froze (survivalTime kept ticking
        // internally, but nothing was ever written to the new object). The old
        // Find only matched the literal name "TimerText"; the arena's object is
        // actually named "Timer_Text", so the re-link silently failed and the
        // stale destroyed reference was kept. Resolve by all known names,
        // including inactive objects, and always replace when found.
        var freshTimer = ResolveTimerText();
        if (freshTimer != null) timerText = freshTimer;

        GameObject panelObj = GameObject.Find("GameOverPanel");
        if (panelObj != null) gameOverPanel = panelObj.GetComponent<CanvasGroup>();

        if (gameOverPanel != null) gameOverPanel.alpha = 0f;

        // Arm the Update-driven timer watchdog on gameplay (arena) scenes only.
        // This is a resilient backup for StartLevelTimer that cannot be killed
        // by a coroutine exception mid-generation, nor left stuck if a second
        // sceneLoaded (e.g. an additive HUD/lighting scene) re-zeroes
        // isTimerActive after the coroutine already finished. It force-starts
        // the survival timer the moment loading + generation report done.
        string sn = scene.name;
        bool nonArena = sn == "Menu" || sn == "MainMenu" || sn == "CampScene"
                        || sn == "ShopScene" || sn == "0_BootLogo";
        timerWatchdogArmed = !nonArena;
        timerWatchdogElapsed = 0f;

        StartCoroutine(CheckForLoadingManager());
    }

    private void Start()
    {
        survivalTime = 0f;
        nextSurvivalTick = 1f;
        isGameOver = false;

        // FAILSAFE: on the very first scene GameManager is created in, the
        // sceneLoaded event already fired before Awake subscribed to it, so
        // OnSceneLoaded never ran — which meant the survival timer flow never
        // started and the timer sat frozen at 00:00 (only the FIRST region of a
        // session; later scenes work because the singleton persists and does
        // receive the event). Run the same init now if nothing handled it.
        if (!sceneInitDone)
            OnSceneLoaded(SceneManager.GetActiveScene(), LoadSceneMode.Single);
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
            // Cap the wait so the timer always begins even on a slow or
            // partially-failed generation. 8s is plenty for a normal gen and
            // stops the "timer stuck at 00:00" feel when a region's gen stalls.
            float genWaitTimeout = 8f;
            float genWaitElapsed = 0f;
            while (!WorldGenerator.IsGenerationDone && genWaitElapsed < genWaitTimeout)
            {
                genWaitElapsed += Time.unscaledDeltaTime;
                yield return null;
            }
            if (!WorldGenerator.IsGenerationDone)
                Debug.LogWarning("[GameManager] World generation didn't report done within 30s — starting the survival timer anyway. Check the Console for a generation exception.");
        }

        // Realtime, NOT WaitForSeconds — the latter uses scaled time, so
        // if a loading overlay / intro left Time.timeScale at 0 when we
        // reached here, the wait never completed and StartLevelTimer was
        // never called (timer stuck at 00:00). Realtime always ticks.
        yield return new WaitForSecondsRealtime(0.5f);

        // Re-resolve the timer text if the initial Find missed it (the
        // HUD object can be inactive at OnSceneLoaded time). Unity's ==
        // override makes a destroyed reference compare == null, so this also
        // recovers after a scene reload wiped the previous life's object.
        if (timerText == null)
        {
            var fresh = ResolveTimerText();
            if (fresh != null) timerText = fresh;
        }

        StartLevelTimer();
    }

    // Resilient backup that guarantees the survival timer starts on any arena
    // scene even if the coroutine path fails or is undone. Runs BEFORE the
    // isTimerActive gate below.
    private bool timerWatchdogArmed = false;
    private float timerWatchdogElapsed = 0f;

    private void TickTimerWatchdog()
    {
        if (!timerWatchdogArmed || isGameOver || isTimerActive) return;

        timerWatchdogElapsed += Time.unscaledDeltaTime;

        bool loadingDone = LoadingManager.Instance == null || !LoadingManager.Instance.isLoading;
        // Natural start: loading overlay gone and the world reported generated.
        if (loadingDone && WorldGenerator.IsGenerationDone && timerWatchdogElapsed >= 0.4f)
        {
            StartLevelTimer();
            return;
        }
        // Absolute fallback: start anyway if nothing ever reports done (a
        // generation exception, a missing LoadingManager, etc.).
        if (timerWatchdogElapsed >= 12f)
        {
            StartLevelTimer();
        }
    }

    private void Update()
    {
        TickTimerWatchdog();

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

    // Finds the survival-timer TMP by any of the names it ships under across
    // scenes ("Timer_Text" in the arenas, "TimerText" in CampScene). Searches
    // inactive objects too, since the HUD can still be disabled at load time.
    private TextMeshProUGUI ResolveTimerText()
    {
        foreach (var t in FindObjectsByType<TextMeshProUGUI>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (t == null) continue;
            string n = t.gameObject.name;
            if (n == "Timer_Text" || n == "TimerText") return t;
        }
        return null;
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