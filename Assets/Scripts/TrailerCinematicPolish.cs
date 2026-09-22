using System.Collections;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;

// The presentation layer of the trailer: letterbox, fades, time ramps and impact
// punches. Deliberately independent of the shot machinery — it touches only the
// screen, the clock and the post volume, so it works regardless of what the
// cameras, animators or terrain are doing.
//
// Created automatically by TrailerSequenceDirector.
public class TrailerCinematicPolish : MonoBehaviour
{
    public static TrailerCinematicPolish Instance { get; private set; }

    [Header("Letterbox")]
    [Tooltip("Height of each bar as a fraction of the screen. 0.11 is roughly 2.39:1 on a 16:9 display.")]
    [Range(0f, 0.25f)] public float barHeight = 0.11f;
    public float letterboxTime = 0.9f;

    [Header("Fades")]
    public float openFade = 1.4f;
    public Color fadeColor = Color.black;

    private Image _top, _bottom, _fade;
    private float _bars;          // 0..1 of barHeight
    private Coroutine _ramp;

    public static TrailerCinematicPolish GetOrCreate()
    {
        if (Instance != null) return Instance;
        var go = new GameObject("TrailerCinematicPolish");
        Instance = go.AddComponent<TrailerCinematicPolish>();
        return Instance;
    }

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(this); return; }
        Instance = this;
        BuildOverlay();
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
        Time.timeScale = 1f;
    }

    private void BuildOverlay()
    {
        var canvasGO = new GameObject("TrailerOverlay");
        canvasGO.transform.SetParent(transform, false);
        var canvas = canvasGO.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 5000;                 // above every gameplay HUD
        var scaler = canvasGO.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);

        _top = MakeBar(canvasGO.transform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, 1f));
        _bottom = MakeBar(canvasGO.transform, new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0f, 0f));

        var fadeGO = new GameObject("Fade", typeof(RectTransform));
        fadeGO.transform.SetParent(canvasGO.transform, false);
        var frt = (RectTransform)fadeGO.transform;
        frt.anchorMin = Vector2.zero; frt.anchorMax = Vector2.one;
        frt.offsetMin = Vector2.zero; frt.offsetMax = Vector2.zero;
        _fade = fadeGO.AddComponent<Image>();
        _fade.raycastTarget = false;
        _fade.color = new Color(fadeColor.r, fadeColor.g, fadeColor.b, 0f);

        SetBars(0f);
    }

    private Image MakeBar(Transform parent, Vector2 aMin, Vector2 aMax, Vector2 pivot)
    {
        var go = new GameObject("Bar", typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var rt = (RectTransform)go.transform;
        rt.anchorMin = aMin; rt.anchorMax = aMax; rt.pivot = pivot;
        rt.offsetMin = new Vector2(0f, rt.offsetMin.y);
        rt.offsetMax = new Vector2(0f, rt.offsetMax.y);
        var img = go.AddComponent<Image>();
        img.color = Color.black;
        img.raycastTarget = false;
        return img;
    }

    private void SetBars(float t)
    {
        _bars = Mathf.Clamp01(t);
        SetBarPixels(barHeight * _bars * Screen.height);
    }

    private void SetBarPixels(float h)
    {
        if (_top != null) ((RectTransform)_top.transform).sizeDelta = new Vector2(0f, h);
        if (_bottom != null) ((RectTransform)_bottom.transform).sizeDelta = new Vector2(0f, h);
    }

    // The letterbox closes all the way, like a shutter, and the shot is over.
    //
    // A shot that simply holds on black has no end — it has a stop, and an
    // editor cutting the trailer has nothing to cut ON. This is a transition
    // made out of the frame the shot has been wearing the whole time: the bars
    // that have been sitting at the top and bottom since the first frame come
    // together over the picture. Nothing new is introduced at the last second,
    // which is what stops it reading as an effect.
    public void CloseBars(float seconds)
    {
        if (_close != null) StopCoroutine(_close);
        _close = StartCoroutine(CloseBarsRoutine(seconds));
    }

    private Coroutine _close;

    private IEnumerator CloseBarsRoutine(float seconds)
    {
        float from = barHeight * _bars * Screen.height;
        float to = Screen.height * 0.5f;                  // the two meet in the middle

        float t = 0f;
        while (t < seconds)
        {
            t += Time.unscaledDeltaTime;
            // Accelerating. Bars that close at a constant rate read as a menu
            // animation; bars that snap shut at the end read as a shutter.
            float k = Mathf.Clamp01(t / Mathf.Max(0.01f, seconds));
            SetBarPixels(Mathf.Lerp(from, to, k * k));
            yield return null;
        }
        SetBarPixels(to);
        _close = null;
    }

    // ── The lens strike ──────────────────────────────────────────────────
    //
    // ==== WHY A FLAT COLOUR READS AS CHEAP ====
    //
    // SetFlash fills one screen-sized Image with one colour, and whatever colour
    // that is, the eye reads it as the picture being REPLACED. Nothing in it has
    // a position, a shape or a direction, so a red screen is a red screen; it is
    // not light arriving. Between two black letterbox bars it is unmistakably a
    // rectangle being tinted.
    //
    // Light actually hitting a lens is the opposite of uniform. It has a source
    // somewhere in frame. It blooms out from that point rather than filling
    // evenly. The glass smears it sideways into a streak — wide and thin, which
    // is the single most recognisable thing about a real flare. The elements
    // bounce ghosts of it back along the line through the middle of the frame.
    // And only at the very end does the sensor give up and go white.
    //
    // So those pieces are drawn, anchored to where the light IS on screen. The
    // caller recomputes that point every frame, so the whole flare slides as the
    // operator is knocked back — which is most of what sells it. A flare that
    // moves is a thing in the world; a flare that sits still is a decal.
    //
    // UGUI and not world geometry, deliberately: two earlier attempts at drawing
    // this as quads rendered nothing at all, and the canvas that visibly draws
    // the letterbox every shot is the one surface here proven to work.

    private RectTransform _flare;
    private Image _glow, _streak, _streakUp;
    private Image[] _ghosts;
    private static Texture2D s_dot, s_bar;

    /// <summary>
    /// Draw the lens flare for one frame.
    /// </summary>
    /// <param name="viewport">Where the light is, in 0..1 viewport coordinates.</param>
    /// <param name="intensity">0 hides it; 1 is a full blow-out.</param>
    public void LensStrike(Vector2 viewport, float intensity, Color hot)
    {
        if (intensity <= 0.001f)
        {
            if (_flare != null) _flare.gameObject.SetActive(false);
            return;
        }

        BuildFlare();
        _flare.gameObject.SetActive(true);

        float k = Mathf.Clamp01(intensity);
        // Hot at the edges, white at the core — the core saturates first, which
        // is what makes the colour read as heat rather than as a tint.
        Color core = Color.Lerp(hot, Color.white, k * k);

        Place(_glow, viewport, new Vector2(1f, 1f) * Mathf.Lerp(220f, 2600f, k * k));
        _glow.color = Fade(core, Mathf.Clamp01(k * 1.15f));

        // Anamorphic: very wide, barely tall. The proportion is the whole tell.
        Place(_streak, viewport, new Vector2(Mathf.Lerp(300f, 4200f, k), Mathf.Lerp(18f, 150f, k * k)));
        _streak.color = Fade(core, Mathf.Clamp01(k * 0.95f));

        // A weaker vertical, so the peak reads as a star rather than as a bar.
        // Sized in the piece's OWN axes and then turned: it carries a 90 degree
        // local rotation, so the long side is written first and comes out
        // vertical on screen.
        Place(_streakUp, viewport, new Vector2(Mathf.Lerp(120f, 1100f, k * k), Mathf.Lerp(10f, 70f, k)));
        _streakUp.color = Fade(core, k * 0.45f);

        // Ghosts march back through the centre of the frame, which is where a
        // lens puts them, and they are the part an audience never names and
        // always misses when it is absent.
        Vector2 centre = new Vector2(0.5f, 0.5f);
        float[] along = { 0.7f, 1.45f, 2.15f };
        float[] size = { 0.30f, 0.16f, 0.42f };
        for (int i = 0; i < _ghosts.Length; i++)
        {
            Vector2 at = viewport + (centre - viewport) * along[i];
            Place(_ghosts[i], at, Vector2.one * Mathf.Lerp(60f, 900f, k) * size[i]);
            // Cool, because a ghost is a reflection and loses the warm end first.
            Color g = Color.Lerp(hot, new Color(0.62f, 0.78f, 1f), 0.55f);
            _ghosts[i].color = Fade(g, k * 0.30f);
        }
    }

    // The matte is not outside the exposure.
    //
    // Bars that stay pure black while the rest of the frame blows out are the
    // one thing that gives the whole move away as an overlay: real light spills
    // past a matte, and for the two or three frames at the peak there is no
    // frame line at all.
    public void SetBarTint(Color c)
    {
        if (_top != null) _top.color = c;
        if (_bottom != null) _bottom.color = c;
    }

    private static Color Fade(Color c, float a) { c.a = Mathf.Clamp01(a); return c; }

    private void Place(Image img, Vector2 viewport, Vector2 size)
    {
        var rt = (RectTransform)img.transform;
        rt.anchorMin = rt.anchorMax = viewport;
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = Vector2.zero;
        rt.sizeDelta = size;
    }

    private void BuildFlare()
    {
        if (_flare != null) return;

        var root = new GameObject("LensStrike", typeof(RectTransform));
        root.transform.SetParent(_top.transform.parent, false);
        _flare = (RectTransform)root.transform;
        _flare.anchorMin = Vector2.zero; _flare.anchorMax = Vector2.one;
        _flare.offsetMin = Vector2.zero; _flare.offsetMax = Vector2.zero;

        _glow = MakePiece("Glow", Dot());
        _streak = MakePiece("Streak", Bar());
        _streakUp = MakePiece("StreakUp", Bar());
        ((RectTransform)_streakUp.transform).localRotation = Quaternion.Euler(0f, 0f, 90f);
        _ghosts = new[] { MakePiece("Ghost0", Dot()), MakePiece("Ghost1", Dot()), MakePiece("Ghost2", Dot()) };

        // Order matters: the flare sits under the matte, the matte under the
        // fade. The fade is what finally takes the frame to black, and nothing
        // may draw over it.
        _top.transform.SetAsLastSibling();
        _bottom.transform.SetAsLastSibling();
        _fade.transform.SetAsLastSibling();
    }

    private Image MakePiece(string name, Texture2D tex)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(_flare, false);
        var img = go.AddComponent<Image>();
        img.sprite = Sprite.Create(tex, new Rect(0f, 0f, tex.width, tex.height), new Vector2(0.5f, 0.5f));
        img.raycastTarget = false;
        return img;
    }

    // A UI Image with no sprite is a hard-edged rectangle, which is exactly what
    // a script-built flare looks like when it looks cheap. Squared falloff on
    // both of these: a linear ramp still shows an edge.
    private static Texture2D Dot()
    {
        if (s_dot != null) return s_dot;
        const int N = 128;
        s_dot = new Texture2D(N, N, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Clamp };
        var px = new Color32[N * N];
        float c = (N - 1) * 0.5f;
        for (int y = 0; y < N; y++)
        for (int x = 0; x < N; x++)
        {
            float d = Mathf.Sqrt((x - c) * (x - c) + (y - c) * (y - c)) / c;
            float a = Mathf.Clamp01(1f - d); a *= a * a;
            // A small hard core, so the middle of the flare is a source and not
            // the top of a gradient.
            a = Mathf.Clamp01(a + (d < 0.06f ? 1f : 0f));
            px[y * N + x] = new Color32(255, 255, 255, (byte)(a * 255f));
        }
        s_dot.SetPixels32(px);
        s_dot.Apply(true);
        return s_dot;
    }

    private static Texture2D Bar()
    {
        if (s_bar != null) return s_bar;
        const int W = 256, H = 32;
        s_bar = new Texture2D(W, H, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Clamp };
        var px = new Color32[W * H];
        for (int y = 0; y < H; y++)
        for (int x = 0; x < W; x++)
        {
            float u = Mathf.Abs(x / (W - 1f) - 0.5f) * 2f;
            float v = Mathf.Abs(y / (H - 1f) - 0.5f) * 2f;
            // Long soft taper along the streak, tight falloff across it.
            float a = Mathf.Clamp01(1f - u); a *= a;
            float b = Mathf.Clamp01(1f - v); b *= b * b;
            px[y * W + x] = new Color32(255, 255, 255, (byte)(a * b * 255f));
        }
        s_bar.SetPixels32(px);
        s_bar.Apply(true);
        return s_bar;
    }

    // ── Public beats ─────────────────────────────────────────────────────

    public void OpenTrailer()
    {
        StartCoroutine(OpenRoutine());
    }

    private IEnumerator OpenRoutine()
    {
        _fade.color = new Color(fadeColor.r, fadeColor.g, fadeColor.b, 1f);
        StartCoroutine(BarsTo(1f, letterboxTime));

        // Unscaled: the fade must run even while a time ramp is active.
        float t = 0f;
        while (t < openFade)
        {
            t += Time.unscaledDeltaTime;
            _fade.color = new Color(fadeColor.r, fadeColor.g, fadeColor.b, 1f - Mathf.Clamp01(t / openFade));
            yield return null;
        }
        _fade.color = new Color(fadeColor.r, fadeColor.g, fadeColor.b, 0f);
    }

    public void FadeToBlack(float duration) { _fadeRoutine = StartCoroutine(FadeRoutine(1f, duration)); }
    public void FadeFromBlack(float duration) { _fadeRoutine = StartCoroutine(FadeRoutine(0f, duration)); }

    // Paint the full-screen overlay any colour, for a flash or a flood.
    //
    // Shot 1's lens flare was drawn as quads parented to the camera and never
    // appeared. Rather than keep debugging invisible geometry, it goes through
    // this overlay — the same Image that already draws the letterbox fades, on a
    // canvas that is demonstrably rendering. Reusing a proven path beats
    // inventing a second one that has to be proven all over again.
    public void SetFlash(Color c)
    {
        if (_fadeRoutine != null) { StopCoroutine(_fadeRoutine); _fadeRoutine = null; }
        if (_fade != null) _fade.color = c;
    }

    private Coroutine _fadeRoutine;

    private IEnumerator FadeRoutine(float target, float duration)
    {
        float from = _fade.color.a, t = 0f;
        while (t < duration)
        {
            t += Time.unscaledDeltaTime;
            float a = Mathf.Lerp(from, target, Mathf.Clamp01(t / Mathf.Max(0.01f, duration)));
            _fade.color = new Color(fadeColor.r, fadeColor.g, fadeColor.b, a);
            yield return null;
        }
        _fade.color = new Color(fadeColor.r, fadeColor.g, fadeColor.b, target);
    }

    private IEnumerator BarsTo(float target, float duration)
    {
        float from = _bars, t = 0f;
        while (t < duration)
        {
            t += Time.unscaledDeltaTime;
            SetBars(Mathf.Lerp(from, target, Mathf.SmoothStep(0f, 1f, t / Mathf.Max(0.01f, duration))));
            yield return null;
        }
        SetBars(target);
    }

    // A held slow-motion beat with eased entry and exit. Everything here is
    // unscaled, or the ramp could never end itself.
    public void TimeRamp(float scale, float hold, float easeIn = 0.08f, float easeOut = 0.5f)
    {
        if (_ramp != null) StopCoroutine(_ramp);
        _ramp = StartCoroutine(RampRoutine(Mathf.Clamp(scale, 0.05f, 1f), hold, easeIn, easeOut));
    }

    private IEnumerator RampRoutine(float scale, float hold, float easeIn, float easeOut)
    {
        float start = Time.timeScale;

        float t = 0f;
        while (t < easeIn)
        {
            t += Time.unscaledDeltaTime;
            SetScale(Mathf.Lerp(start, scale, t / Mathf.Max(0.01f, easeIn)));
            yield return null;
        }
        SetScale(scale);

        yield return new WaitForSecondsRealtime(hold);

        t = 0f;
        while (t < easeOut)
        {
            t += Time.unscaledDeltaTime;
            SetScale(Mathf.Lerp(scale, 1f, t / Mathf.Max(0.01f, easeOut)));
            yield return null;
        }
        SetScale(1f);
        _ramp = null;
    }

    // The project's own fixed step, captured before anything scales it. Hard-coding
    // 0.02 here put the step back to Unity's default rather than to whatever the
    // project is actually set to, every time a ramp ended.
    private static float s_baseFixedDelta = -1f;

    // How far the fixed step may be shortened. A time ramp wants physics to stay
    // smooth while the world slows down, but the naive version scaled the step by
    // timeScale directly: at 0.32 that is a 0.0064 second step — a hundred and
    // fifty physics ticks a second. Land that on the one frame the statue bursts
    // and drops twenty rigid bodies into the scene and PhysX cannot finish a tick
    // inside a frame, so Unity runs more ticks to catch up, and that is a spiral
    // the editor does not come out of. Half the step is all the smoothness this
    // is really buying.
    private const float MinScaleForPhysics = 0.5f;

    private static void SetScale(float s)
    {
        if (s_baseFixedDelta <= 0f) s_baseFixedDelta = Time.fixedDeltaTime;

        Time.timeScale = s;
        // Keep physics stepping in proportion, or a slow beat also makes physics
        // coarse and the fall visibly stutters — but never faster than twice.
        Time.fixedDeltaTime = s_baseFixedDelta * Mathf.Clamp(s, MinScaleForPhysics, 1f);
    }

    // ── Post punch ───────────────────────────────────────────────────────

    private Vignette _vignette;
    private ChromaticAberration _ca;
    private bool _postResolved;

    private void ResolvePost()
    {
        if (_postResolved) return;
        _postResolved = true;

        Volume best = null;
        foreach (var v in Object.FindObjectsByType<Volume>(FindObjectsSortMode.None))
        {
            if (v == null || !v.isActiveAndEnabled || !v.isGlobal || v.profile == null) continue;
            if (best == null || v.priority > best.priority) best = v;
        }
        if (best == null) return;
        best.profile.TryGet(out _vignette);
        best.profile.TryGet(out _ca);
    }

    // A short lens punch on impact: the vignette closes and the edges smear,
    // then both ease back. This is what makes a hit feel like a hit.
    public void ImpactPunch(float strength = 1f, float duration = 0.45f)
    {
        ResolvePost();

        // ==== PUNCHES OVERLAP, AND THEY USED TO RATCHET ====
        //
        // Every fracture fires one of these, so during the build there are always
        // two or three running at once. Each used to read the vignette's CURRENT
        // value as its rest point and restore it on the way out — so a punch that
        // started while another was at its peak took that peak as "rest" and left
        // it there. Ten cracks in, the vignette is pinned near 1 and the frame is
        // a black tunnel that never opens again.
        //
        // The rest value is now captured ONCE, and overlapping punches share one
        // routine: a new punch raises the level, it decays from wherever it is.
        if (!_punchRestCaptured)
        {
            _punchRestCaptured = true;
            _vignetteRest = _vignette != null ? _vignette.intensity.value : 0f;
            _caRest = _ca != null ? _ca.intensity.value : 0f;
        }

        _punchLevel = Mathf.Max(_punchLevel, strength);
        _punchDecay = 1f / Mathf.Max(0.05f, duration);
        if (_punch == null) _punch = StartCoroutine(PunchRoutine());
    }

    private Coroutine _punch;
    private bool _punchRestCaptured;
    private float _vignetteRest, _caRest, _punchLevel, _punchDecay;

    private IEnumerator PunchRoutine()
    {
        while (_punchLevel > 0.001f)
        {
            float e = _punchLevel * _punchLevel;          // sharp attack, soft tail
            if (_vignette != null) _vignette.intensity.Override(Mathf.Clamp01(_vignetteRest + 0.32f * e));
            if (_ca != null) _ca.intensity.Override(Mathf.Clamp01(_caRest + 0.55f * e));

            yield return null;
            _punchLevel -= _punchDecay * Time.unscaledDeltaTime;
        }

        _punchLevel = 0f;
        if (_vignette != null) _vignette.intensity.Override(_vignetteRest);
        if (_ca != null) _ca.intensity.Override(_caRest);
        _punch = null;
    }
}
