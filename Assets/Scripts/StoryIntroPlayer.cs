using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public enum SlideTransition { Fade, HardCut }

[System.Serializable]
public class IntroSlide
{
    public Sprite image;
    [TextArea(2, 4)]
    [Tooltip("Subtitle for this slide (run through localization).")]
    public string subtitle;
    public AudioClip voiceover;
    [Tooltip("How long this slide stays on screen after it finishes typing (seconds).")]
    public float duration = 6f;

    [Header("Transition INTO this slide")]
    [Tooltip("Fade = smooth crossfade; HardCut = snaps in instantly (for punchy beats).")]
    public SlideTransition transition = SlideTransition.Fade;
    [Tooltip("Seconds of pure BLACK held before this slide appears — the dramatic hard-cut + silence beat (e.g. the bass drop at 0:09-0:11).")]
    public float blackHoldBefore = 0f;
}

[System.Serializable]
public class IntroSfxCue
{
    [Tooltip("Seconds from the START of the whole cutscene (straight from the timing table).")]
    public float time;
    public AudioClip clip;
    [Range(0f, 1f)] public float volume = 1f;
}

// Data-driven opening cutscene: a sequence of illustrated slides with narration
// and a timed SFX track (authored from a timing table), shown once when a new
// game begins on the Level 1 scene. On finish it hands off to the location-title
// reveal (which can descend the camera to the player).
public class StoryIntroPlayer : MonoBehaviour
{
    [Header("Content")]
    public IntroSlide[] slides;
    [Tooltip("SFX fired by ABSOLUTE time from the cutscene start — your SFX table.")]
    public IntroSfxCue[] sfxCues;

    [Header("UI refs")]
    public CanvasGroup rootGroup;      // whole overlay (black bg + image + subtitle)
    public Image imageDisplay;         // fullscreen picture
    public TextMeshProUGUI subtitleText;
    public float imageFadeDuration = 1f;
    public float typingSpeed = 0.035f;

    [Header("Audio")]
    public AudioSource voiceSource;    // narration
    public AudioSource sfxSource;      // sound effects

    [Header("Skip")]
    public bool allowSkip = true;
    public GameObject skipHint;        // optional "Press SPACE to skip"

    [Header("Play condition")]
    [Tooltip("Play only the first time per save (PlayerPrefs flag). Uncheck to always play.")]
    public bool onlyOncePerSave = true;
    public string playedFlag = "StoryIntroPlayed";

    [Header("On finish")]
    public LocationTitleReveal locationTitle;              // shown after the slides
    public UnityEngine.Events.UnityEvent onFinished;

    public static bool IsPlaying { get; private set; }

    private bool _skipped;

    private void Awake()
    {
        // Auto-wire the tedious bits so they don't have to be set by hand:
        // a CanvasGroup on this object (for the whole-overlay fade) and the two
        // AudioSources for narration + SFX. Assign them explicitly only if you
        // want them on other objects.
        if (rootGroup == null)
        {
            rootGroup = GetComponent<CanvasGroup>();
            if (rootGroup == null) rootGroup = gameObject.AddComponent<CanvasGroup>();
        }
        if (voiceSource == null)
        {
            voiceSource = gameObject.AddComponent<AudioSource>();
            voiceSource.playOnAwake = false;
            voiceSource.spatialBlend = 0f; // 2D narration
        }
        if (sfxSource == null)
        {
            sfxSource = gameObject.AddComponent<AudioSource>();
            sfxSource.playOnAwake = false;
            sfxSource.spatialBlend = 0f;
        }
    }

    private void Start()
    {
        if (onlyOncePerSave && PlayerPrefs.GetInt(playedFlag, 0) == 1)
        {
            HideImmediate();
            onFinished?.Invoke();
            if (locationTitle != null) locationTitle.Play();
            return;
        }
        StartCoroutine(PlayRoutine());
    }

    private void HideImmediate()
    {
        IsPlaying = false;
        if (rootGroup != null)
        {
            rootGroup.alpha = 0f;
            rootGroup.blocksRaycasts = false;
            rootGroup.gameObject.SetActive(false);
        }
        if (skipHint != null) skipHint.SetActive(false);
    }

    private IEnumerator PlayRoutine()
    {
        IsPlaying = true;
        _skipped = false;
        if (onlyOncePerSave) { PlayerPrefs.SetInt(playedFlag, 1); PlayerPrefs.Save(); }

        if (rootGroup != null)
        {
            rootGroup.gameObject.SetActive(true);
            rootGroup.alpha = 1f;
            rootGroup.blocksRaycasts = true;
        }
        if (skipHint != null) skipHint.SetActive(allowSkip);
        if (subtitleText != null) subtitleText.text = "";
        if (imageDisplay != null) imageDisplay.color = new Color(1f, 1f, 1f, 0f);

        int token = SubtitleGuard.Claim();
        float startTime = Time.unscaledTime;
        Coroutine cueRunner = StartCoroutine(CueRunner(startTime));

        for (int s = 0; s < slides.Length && !_skipped; s++)
        {
            IntroSlide slide = slides[s];

            if (imageDisplay != null)
            {
                // Optional dramatic dip to black + silence before the slide
                // (the hard-cut beat in the timing table).
                if (slide.blackHoldBefore > 0f)
                {
                    yield return FadeGraphicAlpha(imageDisplay, 0f, imageFadeDuration * 0.5f);
                    float bt = 0f;
                    while (bt < slide.blackHoldBefore && !CheckSkip()) { bt += Time.unscaledDeltaTime; yield return null; }
                }

                if (slide.image != null) imageDisplay.sprite = slide.image;

                if (slide.transition == SlideTransition.HardCut)
                {
                    Color c = imageDisplay.color; c.a = 1f; imageDisplay.color = c; // snap in
                }
                else
                {
                    Color c = imageDisplay.color; c.a = 0f; imageDisplay.color = c;
                    yield return FadeGraphicAlpha(imageDisplay, 1f, imageFadeDuration);
                }
            }

            if (voiceSource != null && slide.voiceover != null)
                voiceSource.PlayOneShot(slide.voiceover);

            string full = LocalizationManager.Tr(slide.subtitle);
            if (subtitleText != null)
            {
                for (int i = 0; i <= full.Length; i++)
                {
                    if (CheckSkip() || !SubtitleGuard.Owns(token)) break;
                    subtitleText.text = full.Substring(0, i) + "<alpha=#00>" + full.Substring(i);
                    subtitleText.ForceMeshUpdate();
                    yield return new WaitForSecondsRealtime(typingSpeed);
                }
                if (SubtitleGuard.Owns(token))
                {
                    subtitleText.text = full;
                    subtitleText.ForceMeshUpdate();
                }
            }

            float held = 0f;
            while (held < slide.duration && !CheckSkip())
            {
                held += Time.unscaledDeltaTime;
                yield return null;
            }
        }

        if (cueRunner != null) StopCoroutine(cueRunner);
        if (voiceSource != null) voiceSource.Stop();

        yield return FadeGroup(rootGroup, 0f, imageFadeDuration);
        HideImmediate();

        onFinished?.Invoke();
        if (locationTitle != null) locationTitle.Play();
    }

    // Fires SFX cues by absolute elapsed time, in parallel with the slides.
    private IEnumerator CueRunner(float startTime)
    {
        int next = 0;
        int count = sfxCues != null ? sfxCues.Length : 0;
        while (next < count)
        {
            float elapsed = Time.unscaledTime - startTime;
            while (next < count && elapsed >= sfxCues[next].time)
            {
                IntroSfxCue cue = sfxCues[next++];
                if (cue.clip != null && sfxSource != null) sfxSource.PlayOneShot(cue.clip, cue.volume);
            }
            yield return null;
        }
    }

    private bool CheckSkip()
    {
        if (!allowSkip || _skipped) return _skipped;
        if (Input.GetKeyDown(KeyCode.Space) || Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.Escape))
            _skipped = true;
        return _skipped;
    }

    private IEnumerator FadeGraphicAlpha(Graphic g, float target, float dur)
    {
        if (g == null) yield break;
        float start = g.color.a;
        float t = 0f;
        while (t < dur && !CheckSkip())
        {
            t += Time.unscaledDeltaTime;
            Color c = g.color; c.a = Mathf.Lerp(start, target, t / dur); g.color = c;
            yield return null;
        }
        Color final = g.color; final.a = target; g.color = final;
    }

    private IEnumerator FadeGroup(CanvasGroup grp, float target, float dur)
    {
        if (grp == null) yield break;
        float start = grp.alpha;
        float t = 0f;
        while (t < dur)
        {
            t += Time.unscaledDeltaTime;
            grp.alpha = Mathf.Lerp(start, target, t / dur);
            yield return null;
        }
        grp.alpha = target;
    }
}
