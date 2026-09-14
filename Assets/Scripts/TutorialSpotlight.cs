using System;
using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using TMPro;

// Darkens the whole screen except one control, points at it, and says why.
//
// ==== WHY FOUR PANELS AND NOT A MASK ====
//
// The obvious way to punch a hole in a dim layer is a shader or a UI mask, and
// both are worse here. A mask needs the dim to be a child of a masked object,
// which means restructuring whatever canvas it lands on; a shader needs a
// material that must survive the build and a stencil setup that fights anything
// else using the stencil buffer. Four opaque rectangles around the hole need
// neither, are pixel-exact, cost four draw calls, and work on any canvas in any
// render pipeline. The unglamorous version is the one that will not break.
//
// ==== WHY IT DOES NOT BLOCK THE CLICK ====
//
// The dim panels DO eat raycasts — that is most of the point, because a guided
// step where the player can click anything else is not guided. But the hole is
// genuinely open: nothing is drawn over the target, so the real button receives
// the real click and runs its real handler. The tutorial never simulates a
// press. It watches for the outcome instead, which means a player who ignores
// the arrow and reaches the same state another way is credited exactly the same.
[DisallowMultipleComponent]
public class TutorialSpotlight : MonoBehaviour
{
    public static TutorialSpotlight Instance { get; private set; }
    public static bool IsShowing => Instance != null && Instance._active;

    [Header("Look")]
    [Range(0f, 1f)] public float dim = 0.78f;
    [Tooltip("Slack around the target rect, in reference pixels. A hole cut exactly to the button looks like a rendering error; a little air reads as deliberate.")]
    public float padding = 14f;
    public float fadeTime = 0.28f;

    private Canvas _canvas;
    private CanvasGroup _group;
    private Image _top, _bottom, _left, _right;
    private RectTransform _ring;
    private RectTransform _arrow;
    private Image _arrowImg;
    private TextMeshProUGUI _title;
    private TextMeshProUGUI _body;
    private RectTransform _textPanelRT;
    private Image _textPanel;

    private RectTransform _target;
    private bool _active;
    private Coroutine _fade;

    public static void Show(RectTransform target, string title, string body)
    {
        Ensure();
        if (Instance != null) Instance.Begin(target, title, body);
    }

    public static void Hide()
    {
        if (Instance != null) Instance.End();
    }

    // Re-points at a new control without fading out and in. Used when a step
    // completes and the next one is on screen already — a full fade between two
    // adjacent buttons reads as a stutter.
    public static void Retarget(RectTransform target, string title, string body)
    {
        Ensure();
        if (Instance != null) Instance.Begin(target, title, body, instant: true);
    }

    private static void Ensure()
    {
        if (Instance != null) return;
        var go = new GameObject("[TutorialSpotlight]");
        DontDestroyOnLoad(go);
        go.AddComponent<TutorialSpotlight>();
    }

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        Build();
        _group.alpha = 0f;
        gameObject.SetActive(true);
        SetPanelsActive(false);
    }

    private void OnDestroy() { if (Instance == this) Instance = null; }

    // ---- construction ---------------------------------------------------------

    private void Build()
    {
        _canvas = gameObject.AddComponent<Canvas>();
        _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        // Over the shop UI, under the region victory screen.
        _canvas.sortingOrder = 4500;
        gameObject.AddComponent<GraphicRaycaster>();

        var scaler = gameObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        _group = gameObject.AddComponent<CanvasGroup>();

        _top = MakePanel("DimTop");
        _bottom = MakePanel("DimBottom");
        _left = MakePanel("DimLeft");
        _right = MakePanel("DimRight");

        // A soft frame around the opening, so the eye is pulled to the hole
        // rather than merely permitted to look at it.
        var ringGo = new GameObject("Ring", typeof(RectTransform));
        _ring = ringGo.GetComponent<RectTransform>();
        _ring.SetParent(transform, false);
        var ringImg = ringGo.AddComponent<Image>();
        ringImg.sprite = BuildRingSprite();
        ringImg.type = Image.Type.Sliced;
        ringImg.raycastTarget = false;
        ringImg.color = new Color(1f, 0.85f, 0.4f, 0.85f);

        var arrowGo = new GameObject("Arrow", typeof(RectTransform));
        _arrow = arrowGo.GetComponent<RectTransform>();
        _arrow.SetParent(transform, false);
        _arrow.sizeDelta = new Vector2(64f, 64f);
        _arrowImg = arrowGo.AddComponent<Image>();
        _arrowImg.sprite = BuildArrowSprite();
        _arrowImg.raycastTarget = false;
        _arrowImg.color = new Color(1f, 0.85f, 0.4f, 1f);

        var panelGo = new GameObject("TextPanel", typeof(RectTransform));
        _textPanelRT = panelGo.GetComponent<RectTransform>();
        _textPanelRT.SetParent(transform, false);
        _textPanelRT.sizeDelta = new Vector2(560f, 150f);
        _textPanel = panelGo.AddComponent<Image>();
        _textPanel.color = new Color(0.05f, 0.05f, 0.07f, 0.92f);
        _textPanel.raycastTarget = false;

        _title = MakeText(_textPanelRT, "Title", 34f, FontStyles.Bold, new Vector2(0f, 40f));
        _body = MakeText(_textPanelRT, "Body", 24f, FontStyles.Normal, new Vector2(0f, -18f));
        _title.color = new Color(1f, 0.87f, 0.55f);
        _body.color = new Color(0.86f, 0.86f, 0.84f);
    }

    private Image MakePanel(string name)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(transform, false);
        var img = go.AddComponent<Image>();
        img.color = new Color(0f, 0f, 0f, dim);
        // The dim eats clicks. A guided step where every other control is still
        // live is not a guided step.
        img.raycastTarget = true;
        var rt = img.rectTransform;
        rt.anchorMin = rt.anchorMax = Vector2.zero;
        rt.pivot = Vector2.zero;
        return img;
    }

    private TextMeshProUGUI MakeText(RectTransform parent, string name, float size, FontStyles style, Vector2 pos)
    {
        var go = new GameObject(name, typeof(RectTransform));
        var rt = go.GetComponent<RectTransform>();
        rt.SetParent(parent, false);
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = pos;
        rt.sizeDelta = new Vector2(510f, size * 3f);
        var t = go.AddComponent<TextMeshProUGUI>();
        t.alignment = TextAlignmentOptions.Center;
        t.fontSize = size;
        t.fontStyle = style;
        t.raycastTarget = false;
        t.enableWordWrapping = true;
        return t;
    }

    // ---- driving --------------------------------------------------------------

    private void Begin(RectTransform target, string title, string body, bool instant = false)
    {
        _target = target;
        _title.text = title ?? "";
        _body.text = body ?? "";
        SetPanelsActive(true);

        if (target == null)
        {
            // No control to point at is not a reason to say nothing — the text
            // still carries the instruction, and a step whose button could not
            // be resolved must not silently do nothing at all.
            Debug.LogWarning($"[Spotlight] No target for step '{title}'. Showing the text with no hole.", this);
        }

        _active = true;
        if (_fade != null) StopCoroutine(_fade);
        if (instant) { _group.alpha = 1f; }
        else _fade = StartCoroutine(FadeTo(1f));
    }

    private void End()
    {
        if (!_active) return;
        _active = false;
        if (_fade != null) StopCoroutine(_fade);
        _fade = StartCoroutine(FadeOutAndOff());
    }

    private IEnumerator FadeTo(float a)
    {
        float from = _group.alpha;
        float t = 0f;
        while (t < fadeTime)
        {
            t += Time.unscaledDeltaTime;
            _group.alpha = Mathf.Lerp(from, a, t / fadeTime);
            yield return null;
        }
        _group.alpha = a;
    }

    private IEnumerator FadeOutAndOff()
    {
        yield return FadeTo(0f);
        SetPanelsActive(false);
    }

    private void SetPanelsActive(bool on)
    {
        if (_top == null) return;
        _top.gameObject.SetActive(on);
        _bottom.gameObject.SetActive(on);
        _left.gameObject.SetActive(on);
        _right.gameObject.SetActive(on);
        _ring.gameObject.SetActive(on);
        _arrow.gameObject.SetActive(on);
        _textPanelRT.gameObject.SetActive(on);
        _group.blocksRaycasts = on;
    }

    private void LateUpdate()
    {
        if (!_active) return;

        // Recomputed every frame rather than once on show. Shop lists scroll,
        // panels tween in, and layout groups settle a frame or two after the
        // window opens — a hole positioned once ends up over empty space.
        Rect hole = _target != null ? ScreenRectOf(_target) : new Rect(-1000f, -1000f, 0f, 0f);
        hole.xMin -= padding; hole.yMin -= padding;
        hole.xMax += padding; hole.yMax += padding;

        float w = Screen.width, h = Screen.height;
        float scale = _canvas != null ? _canvas.scaleFactor : 1f;
        if (scale <= 0.0001f) scale = 1f;

        Place(_bottom, 0f, 0f, w, Mathf.Max(0f, hole.yMin), scale);
        Place(_top, 0f, Mathf.Min(h, hole.yMax), w, Mathf.Max(0f, h - hole.yMax), scale);
        Place(_left, 0f, Mathf.Max(0f, hole.yMin), Mathf.Max(0f, hole.xMin), Mathf.Max(0f, hole.height), scale);
        Place(_right, Mathf.Min(w, hole.xMax), Mathf.Max(0f, hole.yMin),
              Mathf.Max(0f, w - hole.xMax), Mathf.Max(0f, hole.height), scale);

        // Ring, breathing gently so the opening is alive.
        float pulse = 1f + Mathf.Sin(Time.unscaledTime * 3.4f) * 0.035f;
        _ring.anchorMin = _ring.anchorMax = Vector2.zero;
        _ring.pivot = new Vector2(0.5f, 0.5f);
        _ring.anchoredPosition = new Vector2(hole.center.x, hole.center.y) / scale;
        _ring.sizeDelta = new Vector2(hole.width, hole.height) / scale * pulse;

        // Arrow: sits above the hole when there is room, below it otherwise, and
        // bobs toward the target so the motion itself points.
        bool above = hole.yMax + 120f < h;
        float bob = Mathf.Sin(Time.unscaledTime * 4.2f) * 10f;
        _arrow.anchorMin = _arrow.anchorMax = Vector2.zero;
        _arrow.pivot = new Vector2(0.5f, 0.5f);
        float arrowY = above ? hole.yMax + 56f + bob : hole.yMin - 56f - bob;
        _arrow.anchoredPosition = new Vector2(hole.center.x, arrowY) / scale;
        // BuildArrowSprite draws the wedge with its POINT AT THE BOTTOM, so an
        // unrotated arrow already points down. This had it the other way round
        // and applied 180 when sitting above the target — so the arrow above the
        // button pointed up, away from the thing it was indicating, and the one
        // below pointed down. Exactly inverted, in both cases.
        _arrow.localRotation = Quaternion.Euler(0f, 0f, above ? 0f : 180f);

        // Text panel on the roomier side of the hole, clamped on screen.
        bool textAbove = !above;
        float panelH = _textPanelRT.sizeDelta.y * scale;
        float ty = textAbove ? hole.yMax + 120f + panelH * 0.5f : hole.yMin - 120f - panelH * 0.5f;
        ty = Mathf.Clamp(ty, panelH * 0.6f, h - panelH * 0.6f);
        // ==== BESIDE THE TARGET, NOT ON TOP OF IT ====
        //
        // This centred the panel on the hole horizontally, which is fine for a
        // target in the middle of the screen and wrong for the shop, where the
        // categories run down the left edge: the panel sat squarely over the
        // buttons it was telling the player to look at, and covered the rest of
        // the list as well.
        //
        // It now steps aside to whichever horizontal side has more room, and
        // clears the highlighted element by its own half-width. The panel is
        // explanation; the thing it explains has to stay visible.
        float panelHalf = _textPanelRT.sizeDelta.x * scale * 0.5f;
        float roomLeft = hole.xMin;
        float roomRight = w - hole.xMax;
        float tx = roomRight >= roomLeft
            ? hole.xMax + 28f + panelHalf
            : hole.xMin - 28f - panelHalf;
        tx = Mathf.Clamp(tx, panelHalf + 12f, w - panelHalf - 12f);
        _textPanelRT.anchorMin = _textPanelRT.anchorMax = Vector2.zero;
        _textPanelRT.pivot = new Vector2(0.5f, 0.5f);
        _textPanelRT.anchoredPosition = new Vector2(tx, ty) / scale;
    }

    private static void Place(Image img, float x, float y, float w, float h, float scale)
    {
        var rt = img.rectTransform;
        rt.anchoredPosition = new Vector2(x, y) / scale;
        rt.sizeDelta = new Vector2(w, h) / scale;
    }

    // Screen-space rect of a RectTransform, whatever canvas mode it lives on.
    private static Rect ScreenRectOf(RectTransform rt)
    {
        var corners = new Vector3[4];
        rt.GetWorldCorners(corners);

        var canvas = rt.GetComponentInParent<Canvas>();
        Camera cam = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay
                   ? canvas.worldCamera
                   : null;

        Vector2 min = RectTransformUtility.WorldToScreenPoint(cam, corners[0]);
        Vector2 max = min;
        for (int i = 1; i < 4; i++)
        {
            Vector2 p = RectTransformUtility.WorldToScreenPoint(cam, corners[i]);
            min = Vector2.Min(min, p);
            max = Vector2.Max(max, p);
        }
        return new Rect(min.x, min.y, max.x - min.x, max.y - min.y);
    }

    // ---- generated art --------------------------------------------------------

    // A hollow rounded frame, drawn once. Generated rather than authored for the
    // same reason the reward reveal's starburst is: no art dependency, nothing
    // to wire, and it cannot go missing from a build.
    private static Sprite s_ring;
    private static Sprite BuildRingSprite()
    {
        if (s_ring != null) return s_ring;
        const int S = 64, B = 5;
        var tex = new Texture2D(S, S, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
        var px = new Color[S * S];
        for (int y = 0; y < S; y++)
            for (int x = 0; x < S; x++)
            {
                bool edge = x < B || y < B || x >= S - B || y >= S - B;
                // Softer at the very outside so the frame does not read as a
                // hard 1-pixel box.
                float a = edge ? 1f : 0f;
                if (edge && (x < 2 || y < 2 || x >= S - 2 || y >= S - 2)) a = 0.45f;
                px[y * S + x] = new Color(1f, 1f, 1f, a);
            }
        tex.SetPixels(px);
        tex.Apply();
        s_ring = Sprite.Create(tex, new Rect(0, 0, S, S), new Vector2(0.5f, 0.5f), 100f, 0,
                               SpriteMeshType.FullRect, new Vector4(B + 3, B + 3, B + 3, B + 3));
        return s_ring;
    }

    // A solid triangle pointing down (rotated 180° when it must point up).
    private static Sprite s_arrow;
    private static Sprite BuildArrowSprite()
    {
        if (s_arrow != null) return s_arrow;
        const int S = 64;
        var tex = new Texture2D(S, S, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
        var px = new Color[S * S];
        for (int y = 0; y < S; y++)
        {
            // Width shrinks as y falls: a wedge with its point at the bottom.
            float halfWidth = (y / (float)(S - 1)) * (S * 0.5f);
            for (int x = 0; x < S; x++)
            {
                float d = Mathf.Abs(x - S * 0.5f);
                float a = d <= halfWidth ? 1f : Mathf.Clamp01(1f - (d - halfWidth) * 0.6f);
                px[y * S + x] = new Color(1f, 1f, 1f, a);
            }
        }
        tex.SetPixels(px);
        tex.Apply();
        s_arrow = Sprite.Create(tex, new Rect(0, 0, S, S), new Vector2(0.5f, 0.5f));
        return s_arrow;
    }
}
