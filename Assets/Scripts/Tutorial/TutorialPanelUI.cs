using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Video;
using TMPro;
using System.Collections;

public class TutorialPanelUI : MonoBehaviour
{
    public static TutorialPanelUI Instance { get; private set; }

    // НОВЕ: Публічний прапорець, щоб інші скрипти (гравець, зброя) знали, що зараз туторіал
    public static bool IsTutorialActive { get; private set; }

    [Header("Refs (wire in inspector)")]
    public CanvasGroup canvasGroup;
    public RectTransform contentRect;
    public TextMeshProUGUI titleText;
    public TextMeshProUGUI bodyText;

    [Header("Icon (optional)")]
    public GameObject iconHolder;
    public Image iconImage;

    [Header("Video Guide (optional)")]
    public GameObject videoHolder;
    public RawImage videoSurface;
    public VideoPlayer videoPlayer;
    public RenderTexture videoTexture;

    [Header("Audio")]
    public AudioSource audioSource;

    [Header("Animation")]
    public float fadeDuration = 0.35f;
    public Vector2 slideFrom = new Vector2(0f, -40f);
    [Range(0f, 0.3f)] public float pulseAmplitude = 0.05f;
    public float pulseSpeed = 2.5f;
    public Image backgroundFrame;

    [Header("Input")]
    public KeyCode dismissKey = KeyCode.Space;

    private Coroutine activeRoutine;
    private Vector2 contentBasePos;
    private Color frameBaseColor;
    private bool hasCachedFrameColor;

    private float savedTimeScale = 1f;
    private bool isPaused = false;
    private bool savedCursorVisible;
    private CursorLockMode savedCursorLockState;
    private bool skipRequested = false; // ФІКС СЬКІПУ

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        DontDestroyOnLoad(gameObject);

        if (contentRect != null) contentBasePos = contentRect.anchoredPosition;
        if (canvasGroup != null) canvasGroup.alpha = 0f;
        if (gameObject.activeInHierarchy) gameObject.SetActive(false);

        WireVideoTexture();
    }

    // НОВЕ: Надійно перехоплюємо інпут у системному Update
    private void Update()
    {
        if (IsTutorialActive && gameObject.activeInHierarchy)
        {
            if (Input.GetKeyDown(dismissKey) || Input.GetKeyDown(KeyCode.Escape) || Input.GetKeyDown(KeyCode.Return))
            {
                skipRequested = true;
            }
        }
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    private void WireVideoTexture()
    {
        if (videoPlayer == null) return;
        if (videoTexture != null)
        {
            videoPlayer.renderMode = VideoRenderMode.RenderTexture;
            videoPlayer.targetTexture = videoTexture;
            if (videoSurface != null) videoSurface.texture = videoTexture;
        }
    }

    public bool IsVisible => gameObject.activeInHierarchy && canvasGroup != null && canvasGroup.alpha > 0.01f;

    public void Show(TutorialHintData data)
    {
        if (data == null) return;
        if (activeRoutine != null)
        {
            StopCoroutine(activeRoutine);
            RestorePause();
        }

        gameObject.SetActive(true);
        IsTutorialActive = true; // Блокуємо гру
        skipRequested = false;

        activeRoutine = StartCoroutine(ShowRoutine(data));
    }

    public void Hide()
    {
        if (activeRoutine != null) StopCoroutine(activeRoutine);
        RestorePause();
        activeRoutine = StartCoroutine(HideRoutine());
    }

    private void OnDisable()
    {
        RestorePause();
    }

    private void RestorePause()
    {
        IsTutorialActive = false;
        if (!isPaused) return;

        Time.timeScale = savedTimeScale;
        isPaused = false;

        // Restore the cursor to the state it had when the hint opened
        // instead of force-locking it. The previous hardcoded
        // (visible=false, Locked) erased the shop/camp/menu cursor and
        // left the player unable to click anything after dismissing a
        // hint in those scenes.
        Cursor.visible = savedCursorVisible;
        Cursor.lockState = savedCursorLockState;
    }

    private IEnumerator ShowRoutine(TutorialHintData data)
    {
        ApplyContent(data);

        if (data.showSound != null && audioSource != null)
            audioSource.PlayOneShot(data.showSound);

        // ФІКС БЛОКУВАННЯ: Примусово зупиняємо час і показуємо курсор.
        // Snapshot the caller's cursor state so we can restore it
        // verbatim when the hint closes — scenes like Shop / Camp /
        // Menu need the cursor to STAY visible after the hint.
        savedTimeScale = Time.timeScale;
        savedCursorVisible = Cursor.visible;
        savedCursorLockState = Cursor.lockState;
        isPaused = true;
        Time.timeScale = 0f;
        Cursor.visible = true;
        Cursor.lockState = CursorLockMode.None;

        if (contentRect != null) contentRect.anchoredPosition = contentBasePos + slideFrom;
        if (canvasGroup != null) canvasGroup.alpha = 0f;

        float t = 0f;
        while (t < fadeDuration)
        {
            t += Time.unscaledDeltaTime;
            float k = Mathf.Clamp01(t / fadeDuration);
            float ease = 1f - Mathf.Pow(1f - k, 3f);
            if (canvasGroup != null) canvasGroup.alpha = ease;
            if (contentRect != null)
                contentRect.anchoredPosition = Vector2.Lerp(contentBasePos + slideFrom, contentBasePos, ease);
            yield return null;
        }
        if (canvasGroup != null) canvasGroup.alpha = 1f;
        if (contentRect != null) contentRect.anchoredPosition = contentBasePos;

        // Захист від миттєвого скіпу: чекаємо, поки гравець відпустить пробіл, якщо тримав його
        while (Input.GetKey(dismissKey))
        {
            yield return null;
        }

        skipRequested = false;
        float remain = data.duration;

        while (true)
        {
            // Перевіряємо прапорець з Update()
            if (skipRequested) break;

            if (!data.waitForInput)
            {
                remain -= Time.unscaledDeltaTime;
                if (remain <= 0f) break;
            }

            PulseFrame();
            yield return null;
        }

        RestorePause();
        yield return HideRoutine();
    }

    private IEnumerator HideRoutine()
    {
        float startAlpha = canvasGroup != null ? canvasGroup.alpha : 1f;
        Vector2 startPos = contentRect != null ? contentRect.anchoredPosition : Vector2.zero;
        Vector2 endPos = contentBasePos + slideFrom;

        float t = 0f;
        while (t < fadeDuration)
        {
            t += Time.unscaledDeltaTime;
            float k = Mathf.Clamp01(t / fadeDuration);
            if (canvasGroup != null) canvasGroup.alpha = Mathf.Lerp(startAlpha, 0f, k);
            if (contentRect != null)
                contentRect.anchoredPosition = Vector2.Lerp(startPos, endPos, k);
            yield return null;
        }

        if (canvasGroup != null) canvasGroup.alpha = 0f;
        if (videoPlayer != null && videoPlayer.isPlaying) videoPlayer.Stop();
        gameObject.SetActive(false);
        activeRoutine = null;
    }

    private void ApplyContent(TutorialHintData data)
    {
        if (titleText != null) titleText.text = string.IsNullOrEmpty(data.title)
            ? LocalizationManager.Tr("TUTORIAL_TIP_DEFAULT")
            : LocalizationManager.Tr(data.title);
        if (bodyText != null) bodyText.text = LocalizationManager.Tr(data.body);

        if (iconImage != null && iconHolder != null)
        {
            bool has = data.icon != null;
            iconImage.sprite = data.icon;
            iconHolder.SetActive(has);
        }

        if (videoPlayer != null)
        {
            bool hasClip = data.videoClip != null;
            if (hasClip)
            {
                videoPlayer.clip = data.videoClip;
                videoPlayer.isLooping = true;
                videoPlayer.Play();
            }
            else
            {
                videoPlayer.Stop();
            }
            if (videoHolder != null) videoHolder.SetActive(hasClip);
        }
        else if (videoHolder != null)
        {
            videoHolder.SetActive(false);
        }
    }

    private void PulseFrame()
    {
        if (backgroundFrame == null || pulseAmplitude <= 0f) return;
        if (!hasCachedFrameColor)
        {
            frameBaseColor = backgroundFrame.color;
            hasCachedFrameColor = true;
        }
        float pulse = Mathf.Sin(Time.unscaledTime * pulseSpeed) * pulseAmplitude;
        backgroundFrame.color = new Color(
            frameBaseColor.r,
            frameBaseColor.g,
            frameBaseColor.b,
            Mathf.Clamp01(frameBaseColor.a + pulse));
    }
}