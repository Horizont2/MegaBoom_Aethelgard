using System;
using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
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

    // ==== AUTHOR IT, OR LET THE CODE BUILD IT ====
    //
    // Assign these and Build() leaves them alone; leave them empty and it builds
    // exactly what it always did. The guide's own card was the one piece of UI
    // in the game with no asset to open, so its wording could be changed but its
    // look could not.
    [Header("Panel parts — leave empty to build them in code")]
    [SerializeField] private Canvas _canvas;
    [SerializeField] private CanvasGroup _group;
    [SerializeField] private Image _top, _bottom, _left, _right;
    [SerializeField] private RectTransform _ring;
    [SerializeField] private RectTransform _arrow;
    [SerializeField] private Image _arrowImg;
    [SerializeField] private TextMeshProUGUI _title;
    [SerializeField] private TextMeshProUGUI _body;
    [SerializeField] private RectTransform _textPanelRT;
    [SerializeField] private Image _textPanel;

    [Header("Art — leave empty for the procedural stand-ins")]
    [Tooltip("The frame drawn around the highlighted control. Sliced; drawn in code when empty.")]
    public Sprite ringSprite;
    [Tooltip("The wedge pointing at the highlighted control. Drawn in code when empty.")]
    public Sprite arrowSprite;

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

        // Drop a prefab at Assets/Resources/UI/TutorialSpotlight.prefab and it is
        // used instead of the bare object.
        var prefab = Resources.Load<GameObject>(PrefabResource);
        GameObject go = prefab != null ? Instantiate(prefab) : new GameObject("[TutorialSpotlight]");
        go.name = "[TutorialSpotlight]";
        DontDestroyOnLoad(go);
        if (go.GetComponent<TutorialSpotlight>() == null) go.AddComponent<TutorialSpotlight>();
    }

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        Build();
        _group.alpha = 0f;
        gameObject.SetActive(true);
        SetPanelsActive(false);
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    private void OnDestroy()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        if (Instance == this) Instance = null;
    }

    // ==== A STEP BELONGS TO THE SCREEN IT WAS RAISED ON ====
    //
    // This object is DontDestroyOnLoad, and its target is a RectTransform in
    // whatever scene raised it. Leaving the shop destroyed that button and left
    // the spotlight holding a dead reference — so the final "Готово" card
    // followed the player back to camp and sat there, over a scene it had
    // nothing to say about, with no way to dismiss it.
    //
    // Anything the guide still wants to say after a scene change is the new
    // scene's business to raise.
    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (_active) End();
    }

    // ---- construction ---------------------------------------------------------

    public const string PrefabResource = "UI/TutorialSpotlight";

    // Fills in whatever was NOT authored, so a half-converted card works.
    private void Build()
    {
        if (_canvas == null) _canvas = GetComponent<Canvas>();
        if (_canvas == null)
        {
            _canvas = gameObject.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            // Over the shop UI, under the region victory screen.
            _canvas.sortingOrder = 4500;
        }
        if (GetComponent<GraphicRaycaster>() == null) gameObject.AddComponent<GraphicRaycaster>();

        if (GetComponent<CanvasScaler>() == null)
        {
            var scaler = gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;
        }

        if (_group == null) _group = GetComponent<CanvasGroup>();
        if (_group == null) _group = gameObject.AddComponent<CanvasGroup>();

        if (_top == null) _top = MakePanel("DimTop");
        if (_bottom == null) _bottom = MakePanel("DimBottom");
        if (_left == null) _left = MakePanel("DimLeft");
        if (_right == null) _right = MakePanel("DimRight");

        // A soft frame around the opening, so the eye is pulled to the hole
        // rather than merely permitted to look at it.
        if (_ring == null)
        {
            var ringGo = new GameObject("Ring", typeof(RectTransform));
            _ring = ringGo.GetComponent<RectTransform>();
            _ring.SetParent(transform, false);
            var ringImg = ringGo.AddComponent<Image>();
            ringImg.type = Image.Type.Sliced;
            ringImg.raycastTarget = false;
            ringImg.color = new Color(1f, 0.85f, 0.4f, 0.85f);
        }
        var ringImage = _ring.GetComponent<Image>();
        if (ringImage != null && ringImage.sprite == null)
            ringImage.sprite = ringSprite != null ? ringSprite : BuildRingSprite();

        if (_arrow == null)
        {
            var arrowGo = new GameObject("Arrow", typeof(RectTransform));
            _arrow = arrowGo.GetComponent<RectTransform>();
            _arrow.SetParent(transform, false);
            // Smaller than it was. At 64 the wedge was competing with the ring
            // for attention and covering the control underneath it — an arrow
            // only has to say WHICH thing, and the ring already says LOOK HERE.
            _arrow.sizeDelta = new Vector2(34f, 34f);
            _arrowImg = arrowGo.AddComponent<Image>();
            _arrowImg.raycastTarget = false;
            _arrowImg.color = new Color(1f, 0.85f, 0.4f, 1f);
        }
        if (_arrowImg == null) _arrowImg = _arrow.GetComponent<Image>();
        if (_arrowImg != null && _arrowImg.sprite == null)
            _arrowImg.sprite = arrowSprite != null ? arrowSprite : BuildArrowSprite();

        if (_textPanelRT == null)
        {
            var panelGo = new GameObject("TextPanel", typeof(RectTransform));
            _textPanelRT = panelGo.GetComponent<RectTransform>();
            _textPanelRT.SetParent(transform, false);
            _textPanelRT.sizeDelta = new Vector2(560f, 150f);
            // ==== NO PLATE BEHIND THE WORDS ====
            //
            // The panel was a near-opaque dark slab, and it read as a second
            // window pasted over the screen — heavier than the thing it was
            // explaining, and it hid whatever it landed on. The whole screen is
            // already dimmed for the step, which is what a backing plate is
            // normally there to achieve, so it was doing the job twice and
            // charging the layout for it. The text carries its own contrast
            // with an outline instead.
            _textPanel = panelGo.AddComponent<Image>();
            _textPanel.color = new Color(0f, 0f, 0f, 0f);
            _textPanel.raycastTarget = false;
        }
        if (_textPanel == null) _textPanel = _textPanelRT.GetComponent<Image>();

        if (_title == null)
        {
            _title = MakeText(_textPanelRT, "Title", 34f, FontStyles.Bold, new Vector2(0f, 40f));
            _title.color = new Color(1f, 0.87f, 0.55f);
        }
        if (_body == null)
        {
            _body = MakeText(_textPanelRT, "Body", 24f, FontStyles.Normal, new Vector2(0f, -18f));
            _body.color = new Color(0.90f, 0.90f, 0.88f);
        }

        // Without the plate the text has to hold on its own, over anything the
        // step happens to sit in front of. A hard outline does that everywhere
        // and costs nothing.
        foreach (var t in new[] { _title, _body })
        {
            t.outlineWidth = 0.22f;
            t.outlineColor = new Color32(8, 7, 5, 255);
        }
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
