using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using FMODUnity;

public enum SlideTransition { Fade, HardCut }

// How an inserted overlay effect animates itself on the canvas.
public enum OverlayMotion { None, Drift, Rise, Pulse, SlowZoom }

// A drop-in visual effect for a slide. Insert a full-screen sprite (fog, embers
// sheet, light rays, film grain, vignette) OR a prefab (e.g. a particle system),
// and the player auto-places it on the canvas at the right size, animates it,
// and cleans it up when the slide ends. You only fill the fields you need.
[System.Serializable]
public class IntroEffect
{
    [Tooltip("Full-screen overlay sprite (fog / embers / light rays / grain / vignette). Auto-stretched to fill unless 'Fullscreen' is off.")]
    public Sprite sprite;
    [Tooltip("OR a prefab to spawn on the canvas (e.g. a particle system). Destroyed automatically when the slide ends.")]
    public GameObject prefab;

    [Range(0f, 1f)] public float opacity = 1f;
    public Color tint = Color.white;

    [Header("Auto motion")]
    public OverlayMotion motion = OverlayMotion.Drift;
    [Tooltip("Strength: px for Drift/Rise, scale delta for SlowZoom, alpha swing for Pulse.")]
    public float motionAmount = 40f;
    [Tooltip("Speed multiplier of the motion.")]
    public float motionSpeed = 1f;
    public float fadeIn = 0.6f;

    [Header("Size / placement")]
    [Tooltip("On = fill the whole screen. Off = use the Size + Anchored Pos below.")]
    public bool fullscreen = true;
    public Vector2 size = new Vector2(600f, 600f);
    public Vector2 anchoredPos = Vector2.zero;
}

[System.Serializable]
public class IntroSlide
{
    public Sprite image;
    [TextArea(2, 4)]
    [Tooltip("Subtitle for this slide (run through localization).")]
    public string subtitle;

    [Header("Narration — use EITHER an FMOD event OR a clip")]
    [Tooltip("Drag an FMOD event for this narration line (event:/...). Preferred. If set, the clip below is ignored.")]
    public EventReference fmodVoice;
    [Tooltip("Fallback Unity AudioClip, used only when no FMOD event is set.")]
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

    [Header("Overlay effects on this slide")]
    [Tooltip("Drop in fog / embers / light rays / grain / vignette sprites or prefabs — " +
             "they auto-size to the screen, animate, and are removed when the slide ends.")]
    public IntroEffect[] effects;
}

[System.Serializable]
public class IntroSfxCue
{
    [Tooltip("Seconds from the START of the whole cutscene (straight from the timing table).")]
    public float time;

    [Header("Sound — use EITHER an FMOD event OR a clip")]
    [Tooltip("Drag an FMOD event here (event:/...). Preferred. If set, the clip below is ignored.")]
    public EventReference fmodEvent;
    [Tooltip("Fallback Unity AudioClip, used only when no FMOD event is set.")]
    public AudioClip clip;
    [Range(0f, 1f)]
    [Tooltip("Volume for the Unity AudioClip fallback. FMOD events set their own level in the mixer.")]
    public float volume = 1f;

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

    [Header("Cutscene music (its own track, replaces the level music)")]
    [Tooltip("Dedicated music for the cutscene. Drag an FMOD event here (preferred). If set, the Level 1 track is silenced for the cutscene and restored at the end.")]
    public EventReference cutsceneMusicEvent;
    [Tooltip("OR a Unity AudioClip (looped) if you don't want to author an FMOD event.")]
    public AudioClip cutsceneMusicClip;
    [Range(0f, 1f)] public float cutsceneMusicVolume = 0.55f;
    public float cutsceneMusicFadeIn = 1.5f;
    public float cutsceneMusicFadeOut = 1.5f;

    [Header("Audio")]
    public AudioSource voiceSource;    // narration
    public AudioSource sfxSource;      // sound effects
    [Tooltip("Duck the FMOD music bus while a narration line plays so the voice sits on top.")]
    public bool duckMusicUnderNarration = true;
    [Range(0f, 1f)]
    [Tooltip("How far the music drops under the voice (0.18 = down to ~18% — narration clearly on top).")]
    public float musicDuckLevel = 0.18f;
    [Range(0f, 1f)]
    [Tooltip("Narration gain. Bump toward 1 if the recorded VO is quiet.")]
    public float voiceVolume = 1f;
    [Tooltip("Slides wait out their whole voiceover clip (+tail) before advancing, so narration is never cut off.")]
    public bool holdForVoiceover = true;
    public float voiceoverTail = 0.6f;

    [Header("Cinematic feel")]
    [Tooltip("Animated black letterbox bars (top+bottom) — instantly filmic. Auto-created.")]
    public bool letterbox = true;
    [Range(0f, 0.2f)]
    [Tooltip("Height of each bar as a fraction of screen height.")]
    public float letterboxFraction = 0.1f;

    [Header("Auto-cinematic (no setup needed)")]
    [Tooltip("Give every slide a DISTINCT camera move (pan + zoom, varied per slide) instead of a plain zoom. " +
             "Slides that have their own Pan values set keep them.")]
    public bool autoCameraMoves = true;
    [Tooltip("Generate a soft dark vignette over the whole cutscene (mood + subtitle legibility). No art needed.")]
    public bool autoVignette = true;
    [Range(0f, 1f)] public float vignetteStrength = 0.55f;
    [Tooltip("Generate a gentle field of drifting embers/dust over the cutscene. No art needed.")]
    public bool autoEmbers = true;
    [Range(0f, 1f)] public float emberOpacity = 0.5f;
    public Color emberColor = new Color(1f, 0.6f, 0.25f, 1f);
    [Tooltip("Drifting soft fog banks across the frame (UI overlay).")]
    public bool autoFog = true;
    [Range(0f, 1f)] public float fogOpacity = 0.3f;
    [Tooltip("Full-screen cinematic colour grade tint (moody teal). Clearly visible, unifies the look.")]
    public bool autoColorGrade = true;
    [Range(0f, 1f)] public float colorGradeOpacity = 0.2f;
    public Color colorGradeTint = new Color(0.09f, 0.16f, 0.22f, 1f);
    [Tooltip("Subtle animated film grain over everything (UI overlay). Off by default — it can look noisy.")]
    public bool autoGrain = false;
    [Range(0f, 1f)] public float grainOpacity = 0.04f;
    [Tooltip("Soft light shaft glowing from the top of the frame (UI overlay).")]
    public bool autoLightRays = true;
    [Range(0f, 1f)] public float lightRayOpacity = 0.22f;
    public Color lightRayColor = new Color(1f, 0.85f, 0.55f, 1f);

    [Header("Skip")]
    public bool allowSkip = true;
    public GameObject skipHint;        // optional "Press SPACE to skip"

    [Header("Play condition")]
    [Tooltip("Play only the first time per save (PlayerPrefs flag). Uncheck to always play.")]
    public bool onlyOncePerSave = true;
    public string playedFlag = "StoryIntroPlayed";

    [Header("On finish")]
    public LocationTitleReveal locationTitle;              // shown after the slides
    [Tooltip("If no LocationTitleReveal is assigned, build one automatically on its own top canvas so the location name animates over the Timeline handoff.")]
    public bool autoLocationTitle = true;
    public string locationTitleKey = "THE BLIGHTED WOODS";
    public string locationSubtitleKey = "";
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

    // Live per-slide overlay effects (destroyed when the slide ends).
    private readonly List<GameObject> _activeEffects = new List<GameObject>();

    // Tracked FMOD narration instance so a skip can cut it off.
    private FMOD.Studio.EventInstance _voiceInstance;
    private bool _voiceInstanceValid;

    // Dedicated cutscene music (FMOD instance or Unity source).
    private AudioSource _musicSource;
    private FMOD.Studio.EventInstance _cutMusicInstance;
    private bool _cutMusicValid;
    private Coroutine _cutMusicFade;
    private bool HasCutsceneMusic => !cutsceneMusicEvent.IsNull || cutsceneMusicClip != null;

    // Generated (code-only) cinematic overlays for the whole cutscene.
    private readonly List<GameObject> _ambientGOs = new List<GameObject>();
    private readonly List<Coroutine> _ambientRoutines = new List<Coroutine>();
    private Sprite _vignetteSprite;
    private Sprite _dotSprite;
    private Sprite _noiseSprite;
    private Sprite _vGradSprite;

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
        // Dedicated 2D looping source for the cutscene music clip path.
        _musicSource = gameObject.AddComponent<AudioSource>();
        _musicSource.playOnAwake = false;
        _musicSource.spatialBlend = 0f;
        _musicSource.loop = true;

        if (imageDisplay != null)
        {
            _imgRT = imageDisplay.rectTransform;
            _imgHomePos = _imgRT.anchoredPosition;
        }

        // Make the subtitle always fit its frame: wrap long lines and auto-size
        // the font down so a 2-3 sentence narration never spills out of the box.
        if (subtitleText != null)
        {
            subtitleText.enableWordWrapping = true;
            subtitleText.enableAutoSizing = true;
            subtitleText.fontSizeMin = 20f;
            subtitleText.fontSizeMax = 38f;
            subtitleText.overflowMode = TextOverflowModes.Overflow;
            var srt = subtitleText.rectTransform;
            // A roomier box (kept bottom-anchored) so wrapped lines have space.
            srt.sizeDelta = new Vector2(Mathf.Max(srt.sizeDelta.x, 1400f), Mathf.Max(srt.sizeDelta.y, 170f));
            if (srt.anchoredPosition.y < 90f)
                srt.anchoredPosition = new Vector2(srt.anchoredPosition.x, 90f);
        }

        BuildCinematicOverlays();

        if (locationTitle == null && autoLocationTitle)
            locationTitle = BuildAutoLocationTitle();
    }

    // Builds a self-contained location-title overlay (its own top canvas + big
    // TMP text) and returns a configured LocationTitleReveal, so the location
    // name animates over the Timeline even when nothing was wired by hand. It
    // lives on its OWN object (not under the cutscene canvas) so it survives the
    // cutscene teardown.
    private LocationTitleReveal BuildAutoLocationTitle()
    {
        var canvasGO = new GameObject("AutoLocationTitle",
            typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        var canvas = canvasGO.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 5000; // above the cutscene canvas
        var scaler = canvasGO.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        var group = canvasGO.AddComponent<CanvasGroup>();
        group.alpha = 0f;
        group.blocksRaycasts = false;
        group.interactable = false;

        // Big centred title.
        var titleGO = new GameObject("Title", typeof(RectTransform));
        titleGO.transform.SetParent(canvasGO.transform, false);
        var trt = (RectTransform)titleGO.transform;
        trt.anchorMin = new Vector2(0.1f, 0.42f);
        trt.anchorMax = new Vector2(0.9f, 0.62f);
        trt.offsetMin = Vector2.zero; trt.offsetMax = Vector2.zero;
        var title = titleGO.AddComponent<TextMeshProUGUI>();
        if (subtitleText != null && subtitleText.font != null) title.font = subtitleText.font;
        title.fontSize = 90f;
        title.enableAutoSizing = true; title.fontSizeMin = 40f; title.fontSizeMax = 110f;
        title.alignment = TextAlignmentOptions.Center;
        title.color = Color.white;
        title.fontStyle = FontStyles.SmallCaps;
        title.characterSpacing = 8f;

        // Smaller line under it.
        var subGO = new GameObject("Subtitle", typeof(RectTransform));
        subGO.transform.SetParent(canvasGO.transform, false);
        var srt = (RectTransform)subGO.transform;
        srt.anchorMin = new Vector2(0.1f, 0.35f);
        srt.anchorMax = new Vector2(0.9f, 0.42f);
        srt.offsetMin = Vector2.zero; srt.offsetMax = Vector2.zero;
        var sub = subGO.AddComponent<TextMeshProUGUI>();
        if (subtitleText != null && subtitleText.font != null) sub.font = subtitleText.font;
        sub.fontSize = 34f;
        sub.alignment = TextAlignmentOptions.Center;
        sub.color = new Color(0.85f, 0.85f, 0.85f, 1f);
        sub.characterSpacing = 6f;

        var reveal = canvasGO.AddComponent<LocationTitleReveal>();
        reveal.group = group;
        reveal.titleText = title;
        reveal.subtitleText = sub;
        reveal.locationKey = locationTitleKey;
        reveal.subtitleKey = locationSubtitleKey;
        reveal.descendCamera = false; // the Timeline owns the camera descent
        canvasGO.SetActive(true);
        return reveal;
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
        if (voiceSource != null) voiceSource.Stop();
        StopVoiceInstance();
        StopCutsceneMusic(0.25f);
        if (duckMusicUnderNarration && AudioManager.Instance != null)
        {
            AudioManager.Instance.UnduckMusic(0.3f);
            AudioManager.Instance.UnduckMusicInstance(0.4f);
        }
        if (_barTop != null) _barTop.sizeDelta = new Vector2(0f, 0f);
        if (_barBottom != null) _barBottom.sizeDelta = new Vector2(0f, 0f);
        DespawnEffects(0f);
        DespawnAmbientOverlays();
        if (rootGroup != null)
        {
            rootGroup.alpha = 0f;
            rootGroup.blocksRaycasts = false;
            rootGroup.gameObject.SetActive(false);
        }
        if (skipHint != null) skipHint.SetActive(false);
    }

    // ---- Drop-in overlay effects -------------------------------------------

    // Spawns every effect on the slide as a canvas child, auto-sized and
    // animated, then keeps the bars/subtitle/flash drawn above them.
    private void SpawnEffects(IntroSlide slide)
    {
        if (slide == null || slide.effects == null || rootGroup == null) return;
        Transform parent = rootGroup.transform;

        foreach (IntroEffect fx in slide.effects)
        {
            if (fx == null) continue;
            GameObject go = null;

            if (fx.prefab != null)
            {
                go = Instantiate(fx.prefab, parent, false);
                // If it's a UI prefab, make it fill the screen; world prefabs
                // keep their own transform.
                RectTransform prt = go.transform as RectTransform;
                if (prt != null) StretchOrSize(prt, fx);
            }
            else if (fx.sprite != null)
            {
                go = new GameObject("Effect_" + fx.sprite.name, typeof(RectTransform), typeof(Image));
                go.transform.SetParent(parent, false);
                var img = go.GetComponent<Image>();
                img.sprite = fx.sprite;
                img.raycastTarget = false;
                Color c = fx.tint; c.a = 0f; img.color = c;         // fade in
                StretchOrSize((RectTransform)go.transform, fx);
                StartCoroutine(EffectRoutine(img, (RectTransform)go.transform, fx));
            }

            if (go != null) _activeEffects.Add(go);
        }

        BringOverlaysToFront();
    }

    private void StretchOrSize(RectTransform rt, IntroEffect fx)
    {
        if (fx.fullscreen)
        {
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
        }
        else
        {
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = fx.size;
            rt.anchoredPosition = fx.anchoredPos;
        }
    }

    // Fades the overlay in, then drives its chosen motion until destroyed.
    private IEnumerator EffectRoutine(Image img, RectTransform rt, IntroEffect fx)
    {
        Vector2 home = rt.anchoredPosition;
        Vector3 homeScale = rt.localScale;
        float targetA = Mathf.Clamp01(fx.opacity);

        // fade in
        float t = 0f;
        float fin = Mathf.Max(0.01f, fx.fadeIn);
        while (t < fin && img != null)
        {
            t += Time.unscaledDeltaTime;
            Color c = img.color; c.a = Mathf.Lerp(0f, targetA, t / fin); img.color = c;
            yield return null;
        }
        if (img != null) { Color c = img.color; c.a = targetA; img.color = c; }

        // continuous motion
        float phase = 0f;
        while (img != null && rt != null)
        {
            phase += Time.unscaledDeltaTime * fx.motionSpeed;
            switch (fx.motion)
            {
                case OverlayMotion.Drift:
                    rt.anchoredPosition = home + new Vector2(Mathf.Sin(phase * 0.5f) * fx.motionAmount,
                                                             Mathf.Cos(phase * 0.35f) * fx.motionAmount * 0.4f);
                    break;
                case OverlayMotion.Rise:
                    rt.anchoredPosition = home + new Vector2(Mathf.Sin(phase * 0.6f) * fx.motionAmount * 0.3f,
                                                             Mathf.Sin(phase * 0.8f) * fx.motionAmount);
                    break;
                case OverlayMotion.Pulse:
                    {
                        Color c = img.color;
                        c.a = Mathf.Clamp01(targetA * (0.65f + 0.35f * Mathf.Sin(phase * 1.5f)));
                        img.color = c;
                    }
                    break;
                case OverlayMotion.SlowZoom:
                    {
                        float z = 1f + (Mathf.Sin(phase * 0.4f) * 0.5f + 0.5f) * (fx.motionAmount * 0.01f);
                        rt.localScale = homeScale * z;
                    }
                    break;
            }
            yield return null;
        }
    }

    private void DespawnEffects(float fadeOut)
    {
        if (_activeEffects.Count == 0) return;
        foreach (GameObject go in _activeEffects)
        {
            if (go == null) continue;
            var img = go.GetComponent<Image>();
            if (fadeOut > 0.01f && img != null) StartCoroutine(FadeAndDestroy(go, img, fadeOut));
            else Destroy(go);
        }
        _activeEffects.Clear();
    }

    private IEnumerator FadeAndDestroy(GameObject go, Image img, float dur)
    {
        float start = img != null ? img.color.a : 0f;
        float t = 0f;
        while (t < dur && img != null)
        {
            t += Time.unscaledDeltaTime;
            Color c = img.color; c.a = Mathf.Lerp(start, 0f, t / dur); img.color = c;
            yield return null;
        }
        if (go != null) Destroy(go);
    }

    private void BringOverlaysToFront()
    {
        if (rootGroup == null) return;
        Transform p = rootGroup.transform;
        if (_barTop != null && _barTop.parent == p) _barTop.SetAsLastSibling();
        if (_barBottom != null && _barBottom.parent == p) _barBottom.SetAsLastSibling();
        if (subtitleText != null && subtitleText.transform.parent == p) subtitleText.transform.SetAsLastSibling();
        if (skipHint != null && skipHint.transform.parent == p) skipHint.transform.SetAsLastSibling();
        if (_flashOverlay != null && _flashOverlay.transform.parent == p) _flashOverlay.transform.SetAsLastSibling();
    }

    // ---- Auto camera moves --------------------------------------------------

    // A distinct pan+zoom per slide so the cutscene never reads as a flat zoom.
    // Six moves cycle by slide index; pans stay inside the zoom overscan so no
    // edges show. Directions alternate (L→R, push-in, R→L, pull-back, diagonal,
    // sink) to keep consecutive slides feeling different.
    private void AutoMove(int index, out float zF, out float zT, out Vector2 pF, out Vector2 pT)
    {
        switch (((index % 6) + 6) % 6)
        {
            case 0: zF = 1.12f; zT = 1.15f; pF = new Vector2(-60f, 0f);  pT = new Vector2(60f, 0f);   break; // pan right
            case 1: zF = 1.08f; zT = 1.18f; pF = new Vector2(0f, -22f);  pT = new Vector2(0f, 22f);   break; // push-in + rise
            case 2: zF = 1.14f; zT = 1.14f; pF = new Vector2(55f, 12f);  pT = new Vector2(-55f, -12f);break; // pan left
            case 3: zF = 1.20f; zT = 1.08f; pF = new Vector2(24f, 16f);  pT = new Vector2(-14f, -8f); break; // pull back / reveal
            case 4: zF = 1.10f; zT = 1.16f; pF = new Vector2(45f, -20f); pT = new Vector2(-35f, 20f); break; // diagonal drift
            default:zF = 1.12f; zT = 1.16f; pF = new Vector2(0f, 34f);   pT = new Vector2(0f, -28f);  break; // slow sink
        }
    }

    // ---- Generated cinematic overlays (no art assets required) --------------

    private void SpawnAmbientOverlays()
    {
        if (rootGroup == null) return;
        // Parent to the IMAGE's own parent so overlays always draw ABOVE the
        // full-screen art. If the art is nested under a panel, parenting to the
        // canvas root would leave the overlays hidden behind that panel — which
        // is why they weren't showing.
        Transform parent = (imageDisplay != null && imageDisplay.transform.parent != null)
                         ? imageDisplay.transform.parent
                         : rootGroup.transform;

        // Cinematic colour grade — a full-screen tint just above the art.
        if (autoColorGrade)
        {
            var img = NewOverlayImage("ColorGrade", parent, null);
            Color c = colorGradeTint; c.a = colorGradeOpacity; img.color = c;
        }

        // Soft top light shaft (behind everything else atmospheric).
        if (autoLightRays)
        {
            if (_vGradSprite == null) _vGradSprite = MakeVerticalGradientSprite();
            var img = NewOverlayImage("LightRays", parent, _vGradSprite);
            Color c = lightRayColor; c.a = lightRayOpacity; img.color = c;
        }

        if (autoVignette)
        {
            if (_vignetteSprite == null) _vignetteSprite = MakeVignetteSprite();
            var img = NewOverlayImage("Vignette", parent, _vignetteSprite);
            img.color = new Color(0f, 0f, 0f, Mathf.Clamp01(vignetteStrength));
        }

        if (autoFog)
        {
            if (_dotSprite == null) _dotSprite = MakeDotSprite();
            var fogGO = NewOverlayContainer("Fog", parent);
            _ambientRoutines.Add(StartCoroutine(FogBanks((RectTransform)fogGO.transform)));
        }

        if (autoEmbers)
        {
            if (_dotSprite == null) _dotSprite = MakeDotSprite();
            var emberGO = NewOverlayContainer("Embers", parent);
            _ambientRoutines.Add(StartCoroutine(EmberField((RectTransform)emberGO.transform)));
        }

        if (autoGrain)
        {
            if (_noiseSprite == null) _noiseSprite = MakeNoiseSprite();
            var img = NewOverlayImage("FilmGrain", parent, _noiseSprite);
            img.color = new Color(1f, 1f, 1f, grainOpacity);
            // oversize so jitter never shows an edge
            var rt = img.rectTransform;
            rt.offsetMin = new Vector2(-40f, -40f); rt.offsetMax = new Vector2(40f, 40f);
            _ambientRoutines.Add(StartCoroutine(GrainJitter(rt)));
        }

        BringOverlaysToFront();
    }

    private Image NewOverlayImage(string name, Transform parent, Sprite sprite)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Image));
        go.transform.SetParent(parent, false);
        var rt = (RectTransform)go.transform;
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
        var img = go.GetComponent<Image>();
        img.sprite = sprite;
        img.raycastTarget = false;
        _ambientGOs.Add(go);
        return img;
    }

    private GameObject NewOverlayContainer(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var rt = (RectTransform)go.transform;
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
        _ambientGOs.Add(go);
        return go;
    }

    // A few big, very soft blobs drifting sideways at different speeds/heights —
    // reads as slow fog banks.
    private IEnumerator FogBanks(RectTransform field)
    {
        const int N = 4;
        float w = Screen.width, h = Screen.height;
        var rts = new RectTransform[N];
        var spd = new float[N];
        var yoff = new float[N];
        for (int i = 0; i < N; i++)
        {
            var go = new GameObject("fog", typeof(RectTransform), typeof(Image));
            go.transform.SetParent(field, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            float size = UnityEngine.Random.Range(h * 0.7f, h * 1.3f);
            rt.sizeDelta = new Vector2(size * 1.6f, size);
            yoff[i] = UnityEngine.Random.Range(-h * 0.3f, h * 0.3f);
            rt.anchoredPosition = new Vector2(UnityEngine.Random.Range(-w * 0.6f, w * 0.6f), yoff[i]);
            var img = go.GetComponent<Image>();
            img.sprite = _dotSprite;
            img.raycastTarget = false;
            img.color = new Color(0.6f, 0.65f, 0.72f, fogOpacity * UnityEngine.Random.Range(0.5f, 1f));
            rts[i] = rt;
            spd[i] = UnityEngine.Random.Range(6f, 16f) * (UnityEngine.Random.value < 0.5f ? -1f : 1f);
        }
        while (true)
        {
            float dt = Time.unscaledDeltaTime;
            for (int i = 0; i < N; i++)
            {
                if (rts[i] == null) continue;
                Vector2 p = rts[i].anchoredPosition;
                p.x += spd[i] * dt;
                if (p.x > w * 0.7f) p.x = -w * 0.7f;
                else if (p.x < -w * 0.7f) p.x = w * 0.7f;
                rts[i].anchoredPosition = p;
            }
            yield return null;
        }
    }

    // Jitters the grain sheet a few px each frame so the noise shimmers.
    private IEnumerator GrainJitter(RectTransform rt)
    {
        while (rt != null)
        {
            rt.anchoredPosition = new Vector2(UnityEngine.Random.Range(-20f, 20f), UnityEngine.Random.Range(-20f, 20f));
            yield return null;
        }
    }

    // ~12 soft motes that rise slowly and wrap, with gentle horizontal sway —
    // reads as drifting embers / dust without any particle system.
    private IEnumerator EmberField(RectTransform field)
    {
        const int N = 26;
        var dots = new RectTransform[N];
        var speed = new float[N];
        var sway = new float[N];
        var phase = new float[N];
        var scale = new float[N];
        float w = Screen.width, h = Screen.height;

        for (int i = 0; i < N; i++)
        {
            var go = new GameObject("ember", typeof(RectTransform), typeof(Image));
            go.transform.SetParent(field, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            scale[i] = UnityEngine.Random.Range(5f, 14f);
            rt.sizeDelta = new Vector2(scale[i], scale[i]);
            rt.anchoredPosition = new Vector2(UnityEngine.Random.Range(-w * 0.5f, w * 0.5f),
                                              UnityEngine.Random.Range(-h * 0.5f, h * 0.5f));
            var img = go.GetComponent<Image>();
            img.sprite = _dotSprite;
            img.raycastTarget = false;
            Color c = emberColor; c.a = emberOpacity * UnityEngine.Random.Range(0.5f, 1f); img.color = c;
            dots[i] = rt;
            speed[i] = UnityEngine.Random.Range(14f, 40f);
            sway[i] = UnityEngine.Random.Range(10f, 26f);
            phase[i] = UnityEngine.Random.Range(0f, 10f);
        }

        while (true)
        {
            float dt = Time.unscaledDeltaTime;
            for (int i = 0; i < N; i++)
            {
                if (dots[i] == null) continue;
                Vector2 p = dots[i].anchoredPosition;
                p.y += speed[i] * dt;
                phase[i] += dt;
                p.x += Mathf.Sin(phase[i] * 0.8f) * sway[i] * dt;
                if (p.y > h * 0.55f) { p.y = -h * 0.55f; p.x = UnityEngine.Random.Range(-w * 0.5f, w * 0.5f); }
                dots[i].anchoredPosition = p;
            }
            yield return null;
        }
    }

    private Sprite MakeVignetteSprite()
    {
        const int S = 128;
        var tex = new Texture2D(S, S, TextureFormat.RGBA32, false);
        tex.wrapMode = TextureWrapMode.Clamp;
        Vector2 c = new Vector2(S * 0.5f, S * 0.5f);
        float maxD = c.magnitude;
        var px = new Color32[S * S];
        for (int y = 0; y < S; y++)
        for (int x = 0; x < S; x++)
        {
            float d = Vector2.Distance(new Vector2(x, y), c) / maxD;    // 0 center → 1 corner
            float a = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.55f, 1f, d)); // clear middle, dark edges
            px[y * S + x] = new Color32(0, 0, 0, (byte)(a * 255f));
        }
        tex.SetPixels32(px);
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, S, S), new Vector2(0.5f, 0.5f), 100f);
    }

    private Sprite MakeDotSprite()
    {
        const int S = 32;
        var tex = new Texture2D(S, S, TextureFormat.RGBA32, false);
        tex.wrapMode = TextureWrapMode.Clamp;
        Vector2 c = new Vector2(S * 0.5f, S * 0.5f);
        float r = S * 0.5f;
        var px = new Color32[S * S];
        for (int y = 0; y < S; y++)
        for (int x = 0; x < S; x++)
        {
            float d = Vector2.Distance(new Vector2(x, y), c) / r;       // 0 center → 1 edge
            float a = Mathf.Clamp01(1f - d);
            a *= a;                                                      // soft falloff
            px[y * S + x] = new Color32(255, 255, 255, (byte)(a * 255f));
        }
        tex.SetPixels32(px);
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, S, S), new Vector2(0.5f, 0.5f), 100f);
    }

    // Warm top light shaft: bright at the top, fading to nothing below.
    private Sprite MakeVerticalGradientSprite()
    {
        const int W = 4, H = 128;
        var tex = new Texture2D(W, H, TextureFormat.RGBA32, false);
        tex.wrapMode = TextureWrapMode.Clamp;
        var px = new Color32[W * H];
        for (int y = 0; y < H; y++)
        {
            float t = y / (float)(H - 1);            // 0 bottom → 1 top
            float a = Mathf.SmoothStep(0f, 1f, t);
            a *= a;
            for (int x = 0; x < W; x++) px[y * W + x] = new Color32(255, 255, 255, (byte)(a * 255f));
        }
        tex.SetPixels32(px);
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, W, H), new Vector2(0.5f, 0.5f), 100f);
    }

    // Fine random grain.
    private Sprite MakeNoiseSprite()
    {
        const int S = 256;
        var tex = new Texture2D(S, S, TextureFormat.RGBA32, false);
        tex.wrapMode = TextureWrapMode.Repeat;
        var px = new Color32[S * S];
        for (int i = 0; i < px.Length; i++)
        {
            byte v = (byte)UnityEngine.Random.Range(0, 256);
            px[i] = new Color32(v, v, v, v);
        }
        tex.SetPixels32(px);
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, S, S), new Vector2(0.5f, 0.5f), 100f);
    }

    private void DespawnAmbientOverlays()
    {
        foreach (var r in _ambientRoutines) if (r != null) StopCoroutine(r);
        _ambientRoutines.Clear();
        foreach (var go in _ambientGOs) if (go != null) Destroy(go);
        _ambientGOs.Clear();
    }

    // ---- Cutscene music (FMOD event OR Unity clip) -------------------------

    private void StartCutsceneMusic()
    {
        if (!cutsceneMusicEvent.IsNull)
        {
            StopCutsceneMusic(0f);
            _cutMusicInstance = RuntimeManager.CreateInstance(cutsceneMusicEvent);
            _cutMusicInstance.setVolume(0f);
            _cutMusicInstance.start();
            _cutMusicValid = true;
            if (_cutMusicFade != null) StopCoroutine(_cutMusicFade);
            _cutMusicFade = StartCoroutine(FadeFmodMusic(cutsceneMusicVolume, cutsceneMusicFadeIn));
        }
        else if (cutsceneMusicClip != null && _musicSource != null)
        {
            _musicSource.clip = cutsceneMusicClip;
            _musicSource.volume = 0f;
            _musicSource.Play();
            if (_cutMusicFade != null) StopCoroutine(_cutMusicFade);
            _cutMusicFade = StartCoroutine(FadeUnityMusic(cutsceneMusicVolume, cutsceneMusicFadeIn));
        }
    }

    private void StopCutsceneMusic(float fade)
    {
        if (_cutMusicFade != null) { StopCoroutine(_cutMusicFade); _cutMusicFade = null; }
        if (_cutMusicValid)
        {
            // Fade handled by the event's own AHDSR on stop; ALLOWFADEOUT keeps it smooth.
            _cutMusicInstance.stop(FMOD.Studio.STOP_MODE.ALLOWFADEOUT);
            _cutMusicInstance.release();
            _cutMusicValid = false;
        }
        if (_musicSource != null && _musicSource.isPlaying)
        {
            if (fade > 0.01f) StartCoroutine(FadeOutAndStopUnity(fade));
            else _musicSource.Stop();
        }
    }

    private IEnumerator FadeUnityMusic(float target, float dur)
    {
        float start = _musicSource.volume;
        float t = 0f;
        while (t < dur)
        {
            t += Time.unscaledDeltaTime;
            _musicSource.volume = Mathf.Lerp(start, target, t / dur);
            yield return null;
        }
        _musicSource.volume = target;
    }

    private IEnumerator FadeOutAndStopUnity(float dur)
    {
        float start = _musicSource.volume;
        float t = 0f;
        while (t < dur && _musicSource != null)
        {
            t += Time.unscaledDeltaTime;
            _musicSource.volume = Mathf.Lerp(start, 0f, t / dur);
            yield return null;
        }
        if (_musicSource != null) { _musicSource.Stop(); _musicSource.volume = 0f; }
    }

    private IEnumerator FadeFmodMusic(float target, float dur)
    {
        float start = 0f;
        float t = 0f;
        while (t < dur && _cutMusicValid)
        {
            t += Time.unscaledDeltaTime;
            _cutMusicInstance.setVolume(Mathf.Lerp(start, target, t / dur));
            yield return null;
        }
        if (_cutMusicValid) _cutMusicInstance.setVolume(target);
    }

    // ---- Narration (FMOD event OR Unity clip) ------------------------------

    // Duration of a slide's narration in seconds, whichever source is used.
    private float SlideVoiceLength(IntroSlide s)
    {
        if (s == null) return 0f;
        if (!s.fmodVoice.IsNull) return GetFmodLengthSeconds(s.fmodVoice);
        if (s.voiceover != null) return s.voiceover.length;
        return 0f;
    }

    // Starts the slide's narration. FMOD event wins; else the Unity clip.
    private void PlaySlideVoice(IntroSlide s)
    {
        if (s == null) return;
        StopVoiceInstance();
        if (!s.fmodVoice.IsNull)
        {
            _voiceInstance = RuntimeManager.CreateInstance(s.fmodVoice);
            _voiceInstance.start();
            _voiceInstanceValid = true;
        }
        else if (voiceSource != null && s.voiceover != null)
        {
            voiceSource.PlayOneShot(s.voiceover, voiceVolume);
        }
    }

    private void StopVoiceInstance()
    {
        if (!_voiceInstanceValid) return;
        _voiceInstance.stop(FMOD.Studio.STOP_MODE.ALLOWFADEOUT);
        _voiceInstance.release();
        _voiceInstanceValid = false;
    }

    // Reads an FMOD event's authored length (ms → s); 0 if unavailable.
    private float GetFmodLengthSeconds(EventReference ev)
    {
        try
        {
            FMOD.Studio.EventDescription desc = RuntimeManager.GetEventDescription(ev);
            if (desc.isValid() && desc.getLength(out int ms) == FMOD.RESULT.OK && ms > 0)
                return ms / 1000f;
        }
        catch { }
        return 0f;
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

        // Duck/silence the level music for the ENTIRE cutscene. If a dedicated
        // cutscene track is set, push the level music to near-silence so the two
        // don't clash; otherwise just duck it under the narration. Duck BOTH the
        // music bus AND the current music EventInstance (the level track isn't on
        // the music bus, so the instance duck is what actually lowers it).
        if (duckMusicUnderNarration && AudioManager.Instance != null)
        {
            float lvl = HasCutsceneMusic ? 0.0f : musicDuckLevel;
            AudioManager.Instance.DuckMusic(lvl, 0.5f, 0f, 0.6f);
            AudioManager.Instance.DuckMusicInstance(lvl, 0.5f);
        }

        // Start the dedicated cutscene music (FMOD event or Unity clip).
        StartCutsceneMusic();

        SpawnAmbientOverlays();   // vignette + embers for the whole cutscene

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

                // Resolve the camera move: use the slide's own pan if it set one,
                // otherwise auto-assign a DISTINCT move per slide so it never
                // looks like a plain zoom.
                float zF = slide.zoomFrom, zT = slide.zoomTo;
                Vector2 pF = slide.panFrom, pT = slide.panTo;
                if (autoCameraMoves && pF == pT)
                    AutoMove(s, out zF, out zT, out pF, out pT);

                // Reset framing + (re)start the Ken-Burns move for this slide so
                // the image is drifting the whole time it's on screen.
                if (_kenBurns != null) { StopCoroutine(_kenBurns); _kenBurns = null; }
                if (_imgRT != null)
                {
                    float z0 = slide.useKenBurns ? zF : 1f;
                    _imgRT.localScale = new Vector3(z0, z0, 1f);
                    _imgRT.anchoredPosition = _imgHomePos + pF;
                }
                if (slide.useKenBurns && _imgRT != null)
                {
                    float motionDur = imageFadeDuration
                                    + (subtitleText != null ? LocalizationManager.Tr(slide.subtitle).Length * typingSpeed : 0f)
                                    + Mathf.Max(slide.duration, SlideVoiceLength(slide));
                    _kenBurns = StartCoroutine(KenBurns(zF, zT, pF, pT, motionDur));
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

            // Spawn this slide's overlay effects (fog / embers / rays / grain…).
            SpawnEffects(slide);

            // Punch on arrival (bass drop / reveal / finale frames).
            if (slide.impactOnStart)
            {
                TriggerFlash(slide.impactFlash, 0.35f);
                TriggerShake(slide.impactShake);
            }

            float slideStartTime = Time.unscaledTime;
            float voLen = SlideVoiceLength(slide);
            PlaySlideVoice(slide);   // music is already ducked for the whole cutscene

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
            if (holdForVoiceover && voLen > 0f)
                voRemaining = (voLen + voiceoverTail) - (Time.unscaledTime - slideStartTime);
            float target = Mathf.Max(slide.duration, voRemaining);
            float held = 0f;
            while (held < target && !CheckSkip())
            {
                held += Time.unscaledDeltaTime;
                yield return null;
            }

            // Clear this slide's overlays before the next slide (fade them out).
            DespawnEffects(slide.transition == SlideTransition.Fade ? imageFadeDuration : 0f);
        }

        if (cueRunner != null) StopCoroutine(cueRunner);
        if (_kenBurns != null) { StopCoroutine(_kenBurns); _kenBurns = null; }
        if (voiceSource != null) voiceSource.Stop();
        StopVoiceInstance();
        StopCutsceneMusic(cutsceneMusicFadeOut);
        if (duckMusicUnderNarration && AudioManager.Instance != null)
        {
            AudioManager.Instance.UnduckMusic(0.6f);
            AudioManager.Instance.UnduckMusicInstance(0.8f);
        }

        // Hand off NOW — while the overlay is still up — so the Timeline camera
        // starts moving and the location title animates BEHIND the fade. If we
        // waited until after the fade, the plain gameplay camera (behind the
        // player) would flash on screen for ~1s before the Timeline kicked in.
        IsPlaying = false;
        onFinished?.Invoke();
        if (locationTitle != null) locationTitle.Play();

        yield return AnimateLetterbox(false);
        yield return FadeGroup(rootGroup, 0f, imageFadeDuration);
        HideImmediate();
    }

    // Slow pan + zoom over the still, with an additive decaying shake on top.
    private IEnumerator KenBurns(float zFrom, float zTo, Vector2 pFrom, Vector2 pTo, float dur)
    {
        if (_imgRT == null) yield break;
        float t = 0f;
        while (true)
        {
            // LINEAR (constant velocity) — SmoothStep is flat at the start, which
            // made the image sit still during the narration (played at the slide
            // start). Linear drifts visibly from the very first frame.
            float e = dur > 0.01f ? Mathf.Clamp01(t / dur) : 1f;
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
                if (!cue.fmodEvent.IsNull) RuntimeManager.PlayOneShot(cue.fmodEvent);
                else if (cue.clip != null && sfxSource != null) sfxSource.PlayOneShot(cue.clip, cue.volume);
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
