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
        float h = barHeight * _bars * Screen.height;
        if (_top != null) ((RectTransform)_top.transform).sizeDelta = new Vector2(0f, h);
        if (_bottom != null) ((RectTransform)_bottom.transform).sizeDelta = new Vector2(0f, h);
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

    public void FadeToBlack(float duration) { StartCoroutine(FadeRoutine(1f, duration)); }
    public void FadeFromBlack(float duration) { StartCoroutine(FadeRoutine(0f, duration)); }

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

    private static void SetScale(float s)
    {
        Time.timeScale = s;
        // Keep physics stepping in proportion, or a slow beat also makes physics
        // coarse and the fall visibly stutters.
        Time.fixedDeltaTime = 0.02f * Mathf.Max(0.05f, s);
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
        StartCoroutine(PunchRoutine(strength, duration));
    }

    private IEnumerator PunchRoutine(float strength, float duration)
    {
        float v0 = _vignette != null ? _vignette.intensity.value : 0f;
        float c0 = _ca != null ? _ca.intensity.value : 0f;

        float t = 0f;
        while (t < duration)
        {
            t += Time.unscaledDeltaTime;
            float k = 1f - Mathf.Clamp01(t / duration);
            float e = k * k;                    // sharp attack, soft tail
            if (_vignette != null) _vignette.intensity.Override(Mathf.Clamp01(v0 + 0.32f * strength * e));
            if (_ca != null) _ca.intensity.Override(Mathf.Clamp01(c0 + 0.55f * strength * e));
            yield return null;
        }
        if (_vignette != null) _vignette.intensity.Override(v0);
        if (_ca != null) _ca.intensity.Override(c0);
    }
}
