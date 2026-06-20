using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Video;
using TMPro;
using System.Collections;

/// <summary>
/// Worldspace/screen tutorial panel that shows a title, body text, optional icon
/// and a looping video clip (AC-style). Wire the inspector fields in a prefab.
/// </summary>
public class TutorialPanelUI : MonoBehaviour
{
    public static TutorialPanelUI Instance { get; private set; }

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
    [Tooltip("RenderTexture to which the VideoPlayer writes. The RawImage's texture should be the same.")]
    public RenderTexture videoTexture;

    [Header("Audio")]
    public AudioSource audioSource;

    [Header("Animation")]
    public float fadeDuration = 0.35f;
    [Tooltip("Зсув при появі (рух з-під), у px")]
    public Vector2 slideFrom = new Vector2(0f, -40f);
    [Tooltip("Пульсація рамки при появі (0 = вимкнено)")]
    [Range(0f, 0.3f)] public float pulseAmplitude = 0.05f;
    public float pulseSpeed = 2.5f;
    public Image backgroundFrame;

    [Header("Input")]
    [Tooltip("Якщо хінт чекає на ввід, ця клавіша закриває панель")]
    public KeyCode dismissKey = KeyCode.Space;

    private Coroutine activeRoutine;
    private Vector2 contentBasePos;
    private Color frameBaseColor;
    private bool hasCachedFrameColor;

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
        if (activeRoutine != null) StopCoroutine(activeRoutine);
        gameObject.SetActive(true);
        activeRoutine = StartCoroutine(ShowRoutine(data));
    }

    public void Hide()
    {
        if (activeRoutine != null) StopCoroutine(activeRoutine);
        activeRoutine = StartCoroutine(HideRoutine());
    }

    private IEnumerator ShowRoutine(TutorialHintData data)
    {
        ApplyContent(data);

        if (data.showSound != null && audioSource != null)
            audioSource.PlayOneShot(data.showSound);

        // Fade + slide in
        if (contentRect != null) contentRect.anchoredPosition = contentBasePos + slideFrom;
        if (canvasGroup != null) canvasGroup.alpha = 0f;

        float t = 0f;
        while (t < fadeDuration)
        {
            t += Time.unscaledDeltaTime;
            float k = Mathf.Clamp01(t / fadeDuration);
            float ease = 1f - Mathf.Pow(1f - k, 3f); // ease-out cubic
            if (canvasGroup != null) canvasGroup.alpha = ease;
            if (contentRect != null)
                contentRect.anchoredPosition = Vector2.Lerp(contentBasePos + slideFrom, contentBasePos, ease);
            yield return null;
        }
        if (canvasGroup != null) canvasGroup.alpha = 1f;
        if (contentRect != null) contentRect.anchoredPosition = contentBasePos;

        // Hold (timer or input)
        if (data.waitForInput)
        {
            while (!Input.GetKeyDown(dismissKey) && !Input.GetMouseButtonDown(0))
            {
                PulseFrame();
                yield return null;
            }
        }
        else
        {
            float remain = data.duration;
            while (remain > 0f)
            {
                remain -= Time.unscaledDeltaTime;
                PulseFrame();
                yield return null;
            }
        }

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
        if (titleText != null) titleText.text = string.IsNullOrEmpty(data.title) ? "TIP" : data.title;
        if (bodyText != null) bodyText.text = data.body;

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
