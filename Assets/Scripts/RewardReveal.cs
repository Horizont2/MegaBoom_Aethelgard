using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

// The moment a find is worth stopping for.
//
// A weapon or a piece of armour appearing as a line of text in the corner is
// indistinguishable from picking up a coin, and the player learns to ignore it.
// Rare drops need a beat: the game pauses on the thing, holds it in the middle of
// the screen where nobody can miss it, and makes light come off it.
//
// Built entirely in code and self-installing, for the same reason the trailer's
// overlay is: it has to work in any scene, from any system, with no prefab wired
// anywhere. Everything runs on UNSCALED time so it still plays if something has
// frozen the game — and something usually has, because a good drop moment is
// exactly where a hit-stop or a level-up screen wants to be.
[DisallowMultipleComponent]
public class RewardReveal : MonoBehaviour
{
    public static RewardReveal Instance { get; private set; }

    [Header("Timing")]
    public float riseTime = 0.45f;
    [Tooltip("Default seconds the reveal sits on screen. Callers can ask for longer — a supply haul is three numbers to read, an armour name is a glance.")]
    public float holdTime = 2.6f;
    public float fadeTime = 0.5f;

    [Header("Look")]
    [Tooltip("Icon size at rest, in reference pixels (1920x1080).")]
    public float iconSize = 240f;
    [Tooltip("How far past its final size the icon overshoots on the way in. A little is impact; a lot is a bouncy castle.")]
    public float overshoot = 1.14f;
    [Tooltip("How many sun rays radiate from behind the icon.")]
    public float rayCount = 12f;
    public float raySpinSpeed = 18f;

    // ==== AUTHOR IT, OR LET THE CODE BUILD IT ====
    //
    // This panel was built entirely in code so it would work in any scene with
    // nothing wired anywhere, which is the right default and a bad ceiling: the
    // one screen the player stares at when they find something rare could not be
    // art-directed at all, because there was nothing in the project to open.
    //
    // Every part is assignable now. Assign them — on a prefab, in a scene,
    // however — and Build() leaves them alone. Leave them empty and the code
    // builds exactly what it always did, so nothing that exists today changes
    // until somebody deliberately replaces a piece.
    //
    // The two procedural sprites are exposed for the same reason. They are
    // drawn as a stand-in for art nobody had made yet, not as a preference.
    [Header("Panel parts — leave empty to build them in code")]
    [SerializeField] private Canvas _canvas;
    [SerializeField] private CanvasGroup _group;
    [SerializeField] private RectTransform _iconRT;
    [SerializeField] private Image _icon;
    [SerializeField] private Image _burst;
    [SerializeField] private RectTransform _burstRT;
    [SerializeField] private RectTransform _raysRT;
    [SerializeField] private TextMeshProUGUI _title;
    [SerializeField] private TextMeshProUGUI _subtitle;

    [Header("Art — leave empty for the procedural stand-ins")]
    [Tooltip("The sunburst behind the icon. Drawn in code when empty.")]
    public Sprite raySprite;
    [Tooltip("The soft glow behind the icon. Drawn in code when empty.")]
    public Sprite glowSprite;

    private Coroutine _playing;

    // Drop a prefab at Assets/Resources/UI/RewardReveal.prefab and it is used
    // instead of the bare object. That is the whole opt-in: no scene wiring, no
    // reference to keep alive across a load, and the code path still stands
    // behind it for any scene that has no prefab.
    public const string PrefabResource = "UI/RewardReveal";

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Install()
    {
        if (Instance != null) return;

        var prefab = Resources.Load<GameObject>(PrefabResource);
        GameObject go = prefab != null ? Instantiate(prefab) : new GameObject("[RewardReveal]");
        go.name = "[RewardReveal]";
        DontDestroyOnLoad(go);

        if (go.GetComponent<RewardReveal>() == null) go.AddComponent<RewardReveal>();
    }

    // The one entry point. Anything that hands the player something rare calls
    // this; it knows nothing about caches, armour or weapons.
    // `hold` overrides how long it sits on screen. Different rewards need
    // different reading time and the same number cannot serve both: a piece of
    // armour is a name and a rarity, read in a glance, while a supply haul is
    // three quantities the player has to actually parse before the numbers are
    // worth showing at all.
    public static void Show(Sprite icon, string title, string subtitle, Color accent, float hold = -1f)
    {
        if (Instance == null) Install();
        if (Instance != null) Instance.Enqueue(icon, title, subtitle, accent, hold, null);
    }

    // ==== A HAUL IS SEVERAL THINGS, AND IT SHOULD LOOK LIKE SEVERAL THINGS ====
    //
    // A chest that pays wood AND stone AND food was reduced to whichever one was
    // biggest, with the rest surviving only as words in the subtitle. From the
    // player's seat that is a single-resource reward with some text under it,
    // and the size of the find never lands.
    //
    // `row` draws each resource as its own icon with its amount beside it,
    // under the title. One entry falls back to the plain subtitle so a
    // single-resource haul is unchanged.
    public static void ShowHaul(Sprite icon, string title, string subtitle, Color accent,
                                System.Collections.Generic.List<(Sprite, int)> row, float hold = -1f)
    {
        if (Instance == null) Install();
        if (Instance != null) Instance.Enqueue(icon, title, subtitle, accent, hold, row);
    }

    // Reveals QUEUE rather than replacing each other.
    //
    // A chest can pay out two things at once — a piece of armour and a haul of
    // supplies — and the second call used to stop the first coroutine mid-beat.
    // The player saw the armour flash for a fifth of a second and then something
    // else, which reads as a glitch and, worse, hides the rarer of the two.
    private struct Pending
    {
        public Sprite icon;
        public string title, subtitle;
        public Color accent;
        public float hold;
        public System.Collections.Generic.List<(Sprite, int)> row;
    }

    private readonly System.Collections.Generic.Queue<Pending> _queue = new System.Collections.Generic.Queue<Pending>();

    private void Enqueue(Sprite icon, string title, string subtitle, Color accent, float hold,
                         System.Collections.Generic.List<(Sprite, int)> row)
    {
        // A cap, because a queue with no bound is a way for one silly frame to
        // lock the screen up for a minute. It says so when it drops one, since a
        // silently discarded reward is indistinguishable from a broken one.
        if (_queue.Count >= 4)
        {
            Debug.LogWarning($"[Reveal] Dropped '{title}' — four reveals are already queued. Something is handing " +
                             "out rewards faster than they can be shown.");
            return;
        }
        _queue.Enqueue(new Pending { icon = icon, title = title, subtitle = subtitle, accent = accent, hold = hold, row = row });
        PumpQueue();
    }

    // THE LATCH THAT COULD JAM.
    //
    // This used to be `if (_playing == null) _playing = StartCoroutine(Drain())`,
    // with Drain clearing _playing when it finished. That is a one-way door: if
    // Drain ever stopped without reaching its last line — the component
    // disabled for a frame during a load, the GameObject deactivated, an
    // exception inside a reveal — _playing stayed non-null forever. From that
    // moment every reward in the run was quietly appended to a queue nobody was
    // draining, the queue filled to four, and after that nothing was shown ever
    // again. It matches the symptom exactly: reveals work, then stop for good.
    //
    // A bool cleared in a finally cannot jam the same way, and the pump is
    // re-checked from OnEnable so even a component that WAS disabled mid-drain
    // picks its queue back up.
    private bool _draining;

    private void PumpQueue()
    {
        if (_draining || _queue.Count == 0) return;
        if (!isActiveAndEnabled) return;   // OnEnable will pump it
        _playing = StartCoroutine(Drain());
    }

    private void OnEnable() => PumpQueue();

    private IEnumerator Drain()
    {
        _draining = true;
        try
        {
            while (_queue.Count > 0)
            {
                var p = _queue.Dequeue();
                yield return Routine(p.icon, p.title, p.subtitle, p.accent, p.hold, p.row);
            }
        }
        finally
        {
            _draining = false;
            _playing = null;
        }
    }

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        Build();
        _group.alpha = 0f;
    }

    private void OnDestroy() { if (Instance == this) Instance = null; }

    // ---- construction --------------------------------------------------------

    // Fills in whatever was NOT authored. Every step is guarded, so a panel that
    // has a hand-built icon but no title gets a code-built title and keeps the
    // icon — mixing is allowed, because half-converting a screen is a normal
    // state to be in while you are converting it.
    private void Build()
    {
        if (_canvas == null) _canvas = GetComponent<Canvas>();
        if (_canvas == null)
        {
            _canvas = gameObject.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            // Above the HUD but below the letterbox overlay the trailer uses, so
            // a drop during a cinematic does not punch through the bars.
            _canvas.sortingOrder = 4000;
        }

        if (GetComponent<CanvasScaler>() == null)
        {
            var scaler = gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;
        }

        if (_group == null) _group = GetComponent<CanvasGroup>();
        if (_group == null) _group = gameObject.AddComponent<CanvasGroup>();
        _group.blocksRaycasts = false;   // never eat a click; this is not a dialog
        _group.interactable = false;

        // Layer order matters: rays behind the soft burst, burst behind the icon.
        if (_raysRT == null)
        {
            _raysRT = MakeChild("Rays", 0f).GetComponent<RectTransform>();
            var rays = _raysRT.gameObject.AddComponent<Image>();
            rays.raycastTarget = false;
            _raysRT.sizeDelta = new Vector2(iconSize * 4.2f, iconSize * 4.2f);
        }
        var raysImg = _raysRT.GetComponent<Image>();
        if (raysImg != null && raysImg.sprite == null)
            raysImg.sprite = raySprite != null ? raySprite : BuildRaySprite(Mathf.Max(3, Mathf.RoundToInt(rayCount)));

        if (_burstRT == null)
        {
            _burstRT = MakeChild("Burst", 0f).GetComponent<RectTransform>();
            _burst = _burstRT.gameObject.AddComponent<Image>();
            _burst.raycastTarget = false;
            _burstRT.sizeDelta = new Vector2(iconSize * 2.6f, iconSize * 2.6f);
        }
        if (_burst == null) _burst = _burstRT.GetComponent<Image>();
        if (_burst != null && _burst.sprite == null)
            _burst.sprite = glowSprite != null ? glowSprite : BuildGlowSprite();

        if (_iconRT == null)
        {
            _iconRT = MakeChild("Icon", 0f).GetComponent<RectTransform>();
            _icon = _iconRT.gameObject.AddComponent<Image>();
            _icon.raycastTarget = false;
            _icon.preserveAspect = true;
            _iconRT.sizeDelta = new Vector2(iconSize, iconSize);
        }
        if (_icon == null) _icon = _iconRT.GetComponent<Image>();

        if (_title == null) _title = MakeText("Title", -iconSize * 0.78f, 54f, FontStyles.Bold);
        if (_subtitle == null) _subtitle = MakeText("Subtitle", -iconSize * 0.78f - 52f, 30f, FontStyles.Normal);
    }

    private GameObject MakeChild(string name, float y)
    {
        var go = new GameObject(name, typeof(RectTransform));
        var rt = go.GetComponent<RectTransform>();
        rt.SetParent(transform, false);
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = new Vector2(0f, y);
        return go;
    }

    private TextMeshProUGUI MakeText(string name, float y, float size, FontStyles style)
    {
        var go = MakeChild(name, y);
        var rt = go.GetComponent<RectTransform>();
        rt.sizeDelta = new Vector2(1200f, size * 1.6f);
        var t = go.AddComponent<TextMeshProUGUI>();
        t.alignment = TextAlignmentOptions.Center;
        t.fontSize = size;
        t.fontStyle = style;
        t.raycastTarget = false;
        t.enableWordWrapping = false;
        return t;
    }

    // ==== DRAWN ONCE, NOT ONCE PER REVEAL ====
    //
    // "Drawn once into a texture" was the intent and not what the code did:
    // this had no cache, so every reward reveal allocated a fresh 256x256
    // RGBA texture — a quarter of a megabyte — and never released it. The glow
    // below did the same at 64KB. A run that opens twenty chests leaks about
    // six megabytes of texture memory that nothing will ever reclaim, and the
    // reveal fires on every chest, every armour drop and every altar.
    //
    // That is the shape of a crash that arrives "randomly, sometimes when you
    // take a screenshot": memory climbs all session, and the process dies on
    // whichever allocation happens to be next — which is very often the large
    // one a screen capture or an overlay asks for.
    //
    // Cached per lobe count, and marked DontSave so a scene change cannot
    // strand them either.
    private static readonly Dictionary<int, Sprite> s_rays = new Dictionary<int, Sprite>(4);

    private static Sprite BuildRaySprite(int lobes)
    {
        if (s_rays.TryGetValue(lobes, out var cached) && cached != null) return cached;
        const int S = 256;
        var tex = new Texture2D(S, S, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
        var px = new Color[S * S];
        Vector2 c = new Vector2(S * 0.5f, S * 0.5f);

        for (int y = 0; y < S; y++)
        {
            for (int x = 0; x < S; x++)
            {
                Vector2 d = new Vector2(x, y) - c;
                float r = d.magnitude / (S * 0.5f);
                float ang = Mathf.Atan2(d.y, d.x);
                // Alternating wedges, softened at the tips and hollow in the
                // middle so the icon is never sitting on a bright disc.
                float wedge = Mathf.Pow(Mathf.Abs(Mathf.Cos(ang * lobes * 0.5f)), 8f);
                float radial = Mathf.Clamp01(1f - r) * Mathf.Clamp01((r - 0.22f) * 4f);
                px[y * S + x] = new Color(1f, 1f, 1f, wedge * radial * 0.85f);
            }
        }
        tex.SetPixels(px);
        tex.Apply();
        tex.hideFlags = HideFlags.HideAndDontSave;
        var raySprite = Sprite.Create(tex, new Rect(0, 0, S, S), new Vector2(0.5f, 0.5f));
        raySprite.hideFlags = HideFlags.HideAndDontSave;
        s_rays[lobes] = raySprite;
        return raySprite;
    }

    // The stand-in for a reward whose real icon could not be resolved.
    private static Sprite s_token;
    private static Sprite BuildTokenSprite()
    {
        if (s_token != null) return s_token;
        const int S = 128;
        var tex = new Texture2D(S, S, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
        var px = new Color[S * S];
        Vector2 c = new Vector2(S * 0.5f, S * 0.5f);
        for (int y = 0; y < S; y++)
            for (int x = 0; x < S; x++)
            {
                float r = Vector2.Distance(new Vector2(x, y), c) / (S * 0.5f);
                // A ring, not a disc: unmistakably a placeholder, and it does not
                // hide behind the glow the way a filled circle would.
                float a = r < 0.62f ? 0.18f : (r < 0.92f ? 1f : Mathf.Clamp01((1f - r) * 12f));
                px[y * S + x] = new Color(1f, 1f, 1f, a);
            }
        tex.SetPixels(px);
        tex.Apply();
        s_token = Sprite.Create(tex, new Rect(0, 0, S, S), new Vector2(0.5f, 0.5f));
        return s_token;
    }

    // The icon strip under the title, one entry per resource in the haul.
    //
    // Rebuilt each reveal rather than pooled: a reveal happens a handful of
    // times a run, the row is at most three cells, and a pool here would be
    // more state to get wrong than it could ever save.
    private RectTransform _row;

    private void BuildRow(System.Collections.Generic.List<(Sprite, int)> row, Color accent)
    {
        if (_row != null)
            for (int i = _row.childCount - 1; i >= 0; i--) Destroy(_row.GetChild(i).gameObject);

        if (row == null || row.Count == 0)
        {
            if (_row != null) _row.gameObject.SetActive(false);
            return;
        }

        if (_row == null)
        {
            var go = new GameObject("HaulRow", typeof(RectTransform));
            _row = go.GetComponent<RectTransform>();
            _row.SetParent(_subtitle.transform.parent, false);
            _row.anchorMin = _row.anchorMax = new Vector2(0.5f, 0.5f);
            _row.pivot = new Vector2(0.5f, 0.5f);
            _row.anchoredPosition = _subtitle.rectTransform.anchoredPosition;
            _row.sizeDelta = new Vector2(720f, 74f);

            var layout = go.AddComponent<UnityEngine.UI.HorizontalLayoutGroup>();
            layout.spacing = 34f;
            layout.childAlignment = TextAnchor.MiddleCenter;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = false;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
        }
        _row.gameObject.SetActive(true);

        foreach (var (sprite, amount) in row)
        {
            var cell = new GameObject("Haul", typeof(RectTransform));
            cell.transform.SetParent(_row, false);
            var cl = cell.AddComponent<UnityEngine.UI.HorizontalLayoutGroup>();
            cl.spacing = 8f;
            cl.childAlignment = TextAnchor.MiddleLeft;
            cl.childForceExpandWidth = false;
            cl.childForceExpandHeight = false;
            cl.childControlWidth = true;
            cl.childControlHeight = true;
            cell.AddComponent<UnityEngine.UI.ContentSizeFitter>().horizontalFit =
                UnityEngine.UI.ContentSizeFitter.FitMode.PreferredSize;

            var ico = new GameObject("Icon", typeof(RectTransform));
            ico.transform.SetParent(cell.transform, false);
            var img = ico.AddComponent<UnityEngine.UI.Image>();
            img.sprite = sprite;
            img.preserveAspect = true;
            img.raycastTarget = false;
            var le = ico.AddComponent<UnityEngine.UI.LayoutElement>();
            le.preferredWidth = 56f; le.preferredHeight = 56f;

            var num = new GameObject("Amount", typeof(RectTransform));
            num.transform.SetParent(cell.transform, false);
            var tmp = num.AddComponent<TextMeshProUGUI>();
            tmp.text = amount.ToString();
            tmp.fontSize = 40f;
            tmp.fontStyle = FontStyles.Bold;
            tmp.color = accent;
            tmp.alignment = TextAlignmentOptions.MidlineLeft;
            tmp.raycastTarget = false;
            tmp.enableWordWrapping = false;
            num.AddComponent<UnityEngine.UI.ContentSizeFitter>().horizontalFit =
                UnityEngine.UI.ContentSizeFitter.FitMode.PreferredSize;
        }
    }

    // A soft round falloff — the same trick TrailerSoftSprite uses to stop
    // particles rendering as hard squares.
    private static Sprite s_glow;

    private static Sprite BuildGlowSprite()
    {
        if (s_glow != null) return s_glow;
        const int S = 128;
        var tex = new Texture2D(S, S, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
        var px = new Color[S * S];
        Vector2 c = new Vector2(S * 0.5f, S * 0.5f);
        for (int y = 0; y < S; y++)
        {
            for (int x = 0; x < S; x++)
            {
                float r = Vector2.Distance(new Vector2(x, y), c) / (S * 0.5f);
                float a = Mathf.Clamp01(1f - r);
                px[y * S + x] = new Color(1f, 1f, 1f, a * a * a * 0.7f);
            }
        }
        tex.SetPixels(px);
        tex.Apply();
        tex.hideFlags = HideFlags.HideAndDontSave;
        s_glow = Sprite.Create(tex, new Rect(0, 0, S, S), new Vector2(0.5f, 0.5f));
        s_glow.hideFlags = HideFlags.HideAndDontSave;
        return s_glow;
    }

    // ---- the beat ------------------------------------------------------------

    private IEnumerator Routine(Sprite icon, string title, string subtitle, Color accent, float hold,
                                System.Collections.Generic.List<(Sprite, int)> row)
    {
        float stay = hold > 0f ? hold : holdTime;
        // A MISSING SPRITE STILL GETS A SHAPE.
        //
        // This used to switch the icon off entirely, which is defensible — an
        // Image with no sprite renders as a white square — but it means a
        // reveal with an unresolved icon looks exactly like a reveal that never
        // fired, and those are opposite problems. A tinted disc says "the beat
        // played, the picture is missing", which is a bug report someone can act
        // on rather than a mystery.
        _icon.sprite = icon != null ? icon : BuildTokenSprite();
        _icon.enabled = true;
        _icon.color = icon != null ? Color.white : accent;

        _title.text = title ?? "";
        _title.color = accent;
        _subtitle.text = subtitle ?? "";
        _subtitle.color = new Color(0.85f, 0.85f, 0.85f);
        BuildRow(row, accent);

        Color glow = accent; glow.a = 1f;
        _burst.color = glow;
        _raysRT.GetComponent<Image>().color = new Color(accent.r, accent.g, accent.b, 0.55f);

        if (AudioManager.Instance != null && AudioManager.Instance.HasEvent(AudioID.UI_QuestComplete))
            AudioManager.Instance.PlaySFX(AudioID.UI_QuestComplete);

        // RISE. The icon overshoots slightly and settles; the rays expand from
        // nothing. Unscaled throughout — see the class note.
        float t = 0f;
        while (t < riseTime)
        {
            t += Time.unscaledDeltaTime;
            float k = Mathf.Clamp01(t / riseTime);
            float eased = 1f - Mathf.Pow(1f - k, 3f);

            _group.alpha = eased;
            float s = Mathf.LerpUnclamped(0.55f, overshoot, eased);
            // Settle back from the overshoot over the last third of the rise.
            if (k > 0.66f) s = Mathf.Lerp(overshoot, 1f, (k - 0.66f) / 0.34f);
            _iconRT.localScale = Vector3.one * s;
            _burstRT.localScale = Vector3.one * Mathf.LerpUnclamped(0.2f, 1f, eased);
            _raysRT.localScale = Vector3.one * Mathf.LerpUnclamped(0.1f, 1f, eased);
            Spin();
            yield return null;
        }
        _iconRT.localScale = Vector3.one;

        // HOLD. The rays keep turning and the glow breathes, so the frame is
        // never static — a still image reads as the game having hung.
        t = 0f;
        while (t < stay)
        {
            t += Time.unscaledDeltaTime;
            float breathe = 1f + Mathf.Sin(Time.unscaledTime * 3.2f) * 0.045f;
            _burstRT.localScale = Vector3.one * breathe;
            _iconRT.localScale = Vector3.one * (1f + Mathf.Sin(Time.unscaledTime * 2.1f) * 0.015f);
            Spin();
            yield return null;
        }

        // FADE, drifting up a little so it leaves rather than switches off.
        t = 0f;
        Vector2 from = _iconRT.anchoredPosition;
        while (t < fadeTime)
        {
            t += Time.unscaledDeltaTime;
            float k = Mathf.Clamp01(t / fadeTime);
            _group.alpha = 1f - k;
            _iconRT.anchoredPosition = from + Vector2.up * (k * 40f);
            Spin();
            yield return null;
        }

        _group.alpha = 0f;
        _iconRT.anchoredPosition = from;
    }

    private void Spin()
    {
        _raysRT.localRotation = Quaternion.Euler(0f, 0f, Time.unscaledTime * raySpinSpeed);
    }
}
