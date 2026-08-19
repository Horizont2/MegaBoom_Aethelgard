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
    [Tooltip("Minimum time this slide stays after it finishes typing (seconds). " +
             "If a voiceover is assigned and Hold For Voiceover is on, the slide " +
             "automatically waits out the whole narration line as well.")]
    public float duration = 4f;

    [Header("Transition INTO this slide")]
    [Tooltip("Fade = smooth crossfade; HardCut = snaps in instantly (for punchy beats).")]
    public SlideTransition transition = SlideTransition.Fade;
    [Tooltip("Seconds of pure BLACK held before this slide appears — the dramatic hard-cut + silence beat (e.g. the bass drop at 0:09-0:11).")]
    public float blackHoldBefore = 0f;

    [Header("Camera motion (Ken Burns) — the anti-'wooden' slow drift")]
    [Tooltip("Slowly pans + zooms the still image so it feels alive, like a moving camera over concept art.")]
    public bool useKenBurns = true;
    [Tooltip("Zoom at the START of the slide (1 = no zoom). >1 gives overscan room to pan without showing edges.")]
    public float zoomFrom = 1.04f;
    [Tooltip("Zoom at the END of the slide. A gentle push-in (e.g. 1.04 -> 1.16) reads as a slow dolly toward the subject.")]
    public float zoomTo = 1.16f;
    [Tooltip("Pan offset (px) at the START — drift the framing. Leave 0 for a pure push-in.")]
    public Vector2 panFrom = Vector2.zero;
    [Tooltip("Pan offset (px) at the END. e.g. (40,0) drifts right, (0,-30) sinks down.")]
    public Vector2 panTo = Vector2.zero;

    [Header("Impact as this slide cuts in (for HardCut beats)")]
    [Tooltip("Fire a screen flash + shake the instant this slide appears — use on the bass-drop / reveal / finale frames.")]
    public bool impactOnStart = false;
    [Range(0f, 1f)] public float impactFlash = 0.6f;
    [Tooltip("Screen-shake amplitude in px (0 = none). ~16-24 for a solid hit.")]
    public float impactShake = 18f;
}

[System.Serializable]
public class IntroSfxCue
{
    [Tooltip("Seconds from the START of the whole cutscene (straight from the timing table).")]
    public float time;
    public AudioClip clip;
    [Range(0f, 1f)] public float volume = 1f;

    [Header("Sync visual punch to this beat")]
    [Tooltip("Flash the screen when this SFX fires (bass drop, taiko, whoosh).")]
    public bool flash = false;
    [Range(0f, 1f)] public float flashStrength = 0.6f;
    [Tooltip("Screen-shake amplitude in px when this SFX fires (growl, impact, tree hit). 0 = none.")]
    public float shake = 0f;
}

// Data-driven opening cutscene: a sequence of illustrated slides with narration
// and a timed SFX track (authored from a timing table), shown once when a new
// game begins on the Level 1 scene. It is NOT a static slideshow — every slide
// gets a slow Ken-Burns camera move, narration ducks the music so the voice is
// always heard, and flashes/shakes can be synced to musical beats. On finish it
// hands off to the location-title reveal (which can descend the camera to the
// player).
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
    [Tooltip("Duck the FMOD music bus while a narration line plays so the voice sits on top.")]
    public bool duckMusicUnderNarration = true;
    [Range(0f, 1f)]
    [Tooltip("How far the music drops under the voice (0.32 = down to ~32%).")]
    public float musicDuckLevel = 0.32f;
    [Tooltip("Slides wait out their whole voiceover clip (+tail) before advancing, so narration is never cut off.")]
    public bool holdForVoiceover = true;
    public float voiceoverTail = 0.6f;

    [Header("Cinematic feel")]
    [Tooltip("Animated black letterbox bars (top+bottom) — instantly filmic. Auto-created.")]
    public bool letterbox = true;
    [Range(0f, 0.2f)]
    [Tooltip("Height of each bar as a fraction of screen height.")]
    public float letterboxFraction = 0.1f;

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

    // Ken Burns / impact runtime state
    private RectTransform _imgRT;
    private Vector2 _imgHomePos;
    private Vector2 _basePan;
    private float _shakeAmp;
    private Vector2 _shakeOffset;
    private Coroutine _kenBurns;

    // Auto-created overlays
    private Image _flashOverlay;
    private Coroutine _flashRoutine;
    private RectTransform _barTop, _barBottom;
    private float _barTargetH;

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

        if (imageDisplay != null)
        {
            _imgRT = imageDisplay.rectTransform;
            _imgHomePos = _imgRT.anchoredPosition;
        }

        BuildCinematicOverlays();
    }

    // Creates the flash sheet + letterbox bars as children of the overlay and
    // keeps the subtitle / skip hint drawn above the bars.
    private void BuildCinematicOverlays()
    {
        if (rootGroup == null) return;
        Transform parent = rootGroup.transform;

        if (letterbox)
        {
            _barTargetH = Mathf.Max(1f, Screen.height * letterboxFraction);
            _barTop = MakeBar(parent, "Letterbox_Top", new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f));
            _barBottom = MakeBar(parent, "Letterbox_Bottom", new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0.5f, 0f));
        }

        // Full-screen white flash sheet (alpha 0 until triggered).
        var fgo = new GameObject("ImpactFlash", typeof(RectTransform), typeof(Image));
        fgo.transform.SetParent(parent, false);
        var frt = (RectTransform)fgo.transform;
        frt.anchorMin = Vector2.zero; frt.anchorMax = Vector2.one;
        frt.offsetMin = Vector2.zero; frt.offsetMax = Vector2.zero;
        _flashOverlay = fgo.GetComponent<Image>();
        _flashOverlay.color = new Color(1f, 1f, 1f, 0f);
        _flashOverlay.raycastTarget = false;

        // Keep the caption + skip hint above the bars/flash so they stay readable;
        // the flash sheet sits on top of everything so it whites out the frame.
        if (subtitleText != null && subtitleText.transform.parent == parent)
            subtitleText.transform.SetAsLastSibling();
        if (skipHint != null && skipHint.transform.parent == parent)
            skipHint.transform.SetAsLastSibling();
        fgo.transform.SetAsLastSibling();
    }

    private RectTransform MakeBar(Transform parent, string name, Vector2 aMin, Vector2 aMax, Vector2 pivot)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Image));
        go.transform.SetParent(parent, false);
        var rt = (RectTransform)go.transform;
        rt.anchorMin = aMin; rt.anchorMax = aMax; rt.pivot = pivot;
        rt.sizeDelta = new Vector2(0f, 0f);      // start collapsed, grow on play
        rt.anchoredPosition = Vector2.zero;
        var img = go.GetComponent<Image>();
        img.color = Color.black;
        img.raycastTarget = false;
        return rt;
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
        if (_kenBurns != null) { StopCoroutine(_kenBurns); _kenBurns = null; }
        if (_imgRT != null) { _imgRT.localScale = Vector3.one; _imgRT.anchoredPosition = _imgHomePos; }
        if (duckMusicUnderNarration && AudioManager.Instance != null) AudioManager.Instance.UnduckMusic(0.3f);
        if (_barTop != null) _barTop.sizeDelta = new Vector2(0f, 0f);
        if (_barBottom != null) _barBottom.sizeDelta = new Vector2(0f, 0f);
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

        StartCoroutine(AnimateLetterbox(true));

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
                    if (_kenBurns != null) { StopCoroutine(_kenBurns); _kenBurns = null; }
                    yield return FadeGraphicAlpha(imageDisplay, 0f, imageFadeDuration * 0.5f);
                    float bt = 0f;
                    while (bt < slide.blackHoldBefore && !CheckSkip()) { bt += Time.unscaledDeltaTime; yield return null; }
                }

                if (slide.image != null) imageDisplay.sprite = slide.image;

                // Reset framing + (re)start the Ken-Burns move for this slide so
                // the image is drifting the whole time it's on screen.
                if (_kenBurns != null) { StopCoroutine(_kenBurns); _kenBurns = null; }
                if (_imgRT != null)
                {
                    float z0 = slide.useKenBurns ? slide.zoomFrom : 1f;
                    _imgRT.localScale = new Vector3(z0, z0, 1f);
                    _imgRT.anchoredPosition = _imgHomePos + slide.panFrom;
                }
                if (slide.useKenBurns && _imgRT != null)
                {
                    float motionDur = imageFadeDuration
                                    + (subtitleText != null ? LocalizationManager.Tr(slide.subtitle).Length * typingSpeed : 0f)
                                    + Mathf.Max(slide.duration, slide.voiceover != null ? slide.voiceover.length : 0f);
                    _kenBurns = StartCoroutine(KenBurns(slide.zoomFrom, slide.zoomTo, slide.panFrom, slide.panTo, motionDur));
                }

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

            // Punch on arrival (bass drop / reveal / finale frames).
            if (slide.impactOnStart)
            {
                TriggerFlash(slide.impactFlash, 0.35f);
                TriggerShake(slide.impactShake);
            }

            float slideStartTime = Time.unscaledTime;
            if (voiceSource != null && slide.voiceover != null)
            {
                voiceSource.PlayOneShot(slide.voiceover);
                if (duckMusicUnderNarration && AudioManager.Instance != null)
                    AudioManager.Instance.DuckMusic(musicDuckLevel, 0.4f, slide.voiceover.length + 0.2f, 0.7f);
            }

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

            // Hold long enough to cover BOTH the authored duration and the whole
            // narration line (so the voice is never cut off mid-sentence).
            float voRemaining = 0f;
            if (holdForVoiceover && slide.voiceover != null)
                voRemaining = (slide.voiceover.length + voiceoverTail) - (Time.unscaledTime - slideStartTime);
            float target = Mathf.Max(slide.duration, voRemaining);
            float held = 0f;
            while (held < target && !CheckSkip())
            {
                held += Time.unscaledDeltaTime;
                yield return null;
            }
        }

        if (cueRunner != null) StopCoroutine(cueRunner);
        if (_kenBurns != null) { StopCoroutine(_kenBurns); _kenBurns = null; }
        if (voiceSource != null) voiceSource.Stop();
        if (duckMusicUnderNarration && AudioManager.Instance != null) AudioManager.Instance.UnduckMusic(0.6f);

        yield return AnimateLetterbox(false);
        yield return FadeGroup(rootGroup, 0f, imageFadeDuration);
        HideImmediate();

        onFinished?.Invoke();
        if (locationTitle != null) locationTitle.Play();
    }

    // Slow pan + zoom over the still, with an additive decaying shake on top.
    private IEnumerator KenBurns(float zFrom, float zTo, Vector2 pFrom, Vector2 pTo, float dur)
    {
        if (_imgRT == null) yield break;
        float t = 0f;
        while (true)
        {
            float k = dur > 0.01f ? Mathf.Clamp01(t / dur) : 1f;
            float e = Mathf.SmoothStep(0f, 1f, k);
            float z = Mathf.Lerp(zFrom, zTo, e);
            _imgRT.localScale = new Vector3(z, z, 1f);
            _basePan = Vector2.Lerp(pFrom, pTo, e);

            if (_shakeAmp > 0.01f)
            {
                _shakeOffset = new Vector2(UnityEngine.Random.Range(-1f, 1f), UnityEngine.Random.Range(-1f, 1f)) * _shakeAmp;
                _shakeAmp = Mathf.Lerp(_shakeAmp, 0f, Time.unscaledDeltaTime * 6f);
            }
            else _shakeOffset = Vector2.zero;

            _imgRT.anchoredPosition = _imgHomePos + _basePan + _shakeOffset;
            t += Time.unscaledDeltaTime;
            yield return null;
        }
    }

    private void TriggerShake(float amp)
    {
        if (amp <= 0f) return;
        _shakeAmp = Mathf.Max(_shakeAmp, amp);
    }

    private void TriggerFlash(float strength, float dur)
    {
        if (_flashOverlay == null || strength <= 0f) return;
        if (_flashRoutine != null) StopCoroutine(_flashRoutine);
        _flashRoutine = StartCoroutine(FlashRoutine(Mathf.Clamp01(strength), dur));
    }

    private IEnumerator FlashRoutine(float strength, float dur)
    {
        Color c = _flashOverlay.color;
        c.a = strength; _flashOverlay.color = c;
        float t = 0f;
        while (t < dur)
        {
            t += Time.unscaledDeltaTime;
            c.a = Mathf.Lerp(strength, 0f, t / dur);
            _flashOverlay.color = c;
            yield return null;
        }
        c.a = 0f; _flashOverlay.color = c;
    }

    private IEnumerator AnimateLetterbox(bool show)
    {
        if (!letterbox || _barTop == null || _barBottom == null) yield break;
        float from = _barTop.sizeDelta.y;
        float to = show ? _barTargetH : 0f;
        float dur = 0.7f;
        float t = 0f;
        while (t < dur)
        {
            t += Time.unscaledDeltaTime;
            float h = Mathf.Lerp(from, to, Mathf.SmoothStep(0f, 1f, t / dur));
            _barTop.sizeDelta = new Vector2(0f, h);
            _barBottom.sizeDelta = new Vector2(0f, h);
            yield return null;
        }
        _barTop.sizeDelta = new Vector2(0f, to);
        _barBottom.sizeDelta = new Vector2(0f, to);
    }

    // Fires SFX cues by absolute elapsed time, in parallel with the slides,
    // and triggers any flash/shake wired to that beat.
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
                if (cue.flash) TriggerFlash(cue.flashStrength, 0.4f);
                if (cue.shake > 0f) TriggerShake(cue.shake);
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
