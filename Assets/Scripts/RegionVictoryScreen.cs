using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

// What you get for taking a region.
//
// ==== WHY THIS REPLACED THE CINEMATIC ====
//
// The old victory sequence was ninety seconds of camera work: a hero beat, a
// heal-the-land shockwave, a bird flythrough, a slow-mo dip, an FOV drift, a
// dolly, a title card, and a reward hold — around three hundred lines with
// fifteen places it could be skipped and a dozen pieces of global state to put
// back (camera mode, DoF, handheld drift, enemy freeze, weather lock, control
// block, timescale). Any one of them failing left the player standing in a
// conquered region with no way out, and that is exactly what kept happening,
// through three separate attempts to fix it.
//
// The failure was structural, not a bug to be found. A sequence that must
// traverse three hundred lines and restore twelve globals before the player can
// leave will always have a path that does not, and every new beat added another.
//
// So: black screen, the things you earned, a key to continue. It is shorter,
// it is legible, it tells the player something the cinematic never did — what
// they actually got — and it CANNOT strand anyone, because the load is not at
// the end of a long road. It is on a watchdog that fires whatever else happens.
[DisallowMultipleComponent]
public class RegionVictoryScreen : MonoBehaviour
{
    public static RegionVictoryScreen Instance { get; private set; }
    public static bool IsShowing { get; private set; }

    public struct Award
    {
        public Sprite icon;
        public string label;
        public int amount;
        public Color tint;
    }

    [Header("Timing")]
    [Tooltip("Fade to black.")]
    public float fadeIn = 0.6f;
    [Tooltip("Gap between one award landing and the next arriving. The stagger IS the reward — three numbers appearing at once is a receipt, three arriving in turn is a tally.")]
    [Tooltip("Gap between award tiles appearing. A ripple across the line, not a queue — this used to be 0.42 with the tiles stacked vertically, which made four rewards a two-second recital.")]
    public float rowStagger = 0.06f;
    [Tooltip("How long each number takes to count up to its value.")]
    public float countUp = 0.55f;
    [Tooltip("Hard limit before the screen gives up waiting for a key and continues on its own. The player must never be stuck here, whatever happens.")]
    public float watchdog = 45f;

    [Header("Look")]
    [Tooltip("How dark the world behind goes. Not fully black on purpose — a hint of the region you just cleansed is worth keeping.")]
    [Range(0.5f, 1f)] public float dim = 0.93f;

    // ==== AUTHOR IT, OR LET THE CODE BUILD IT ====
    //
    // Assign these — on a prefab, in a scene, however — and Build() leaves them
    // alone. Leave them empty and it builds exactly what it always did, so
    // nothing changes until somebody deliberately replaces a piece. The screen
    // the player sees on winning a region could not be art-directed at all
    // before this, because there was nothing in the project to open.
    [Header("Panel parts — leave empty to build them in code")]
    [SerializeField] private CanvasGroup _group;
    [SerializeField] private Image _veil;
    [SerializeField] private TextMeshProUGUI _title;
    [SerializeField] private TextMeshProUGUI _subtitle;
    [SerializeField] private RectTransform _rows;
    [SerializeField] private TextMeshProUGUI _continue;
    private Action _onDone;
    private bool _accepting;

    // The spoils of a region, as rows this screen can show.
    //
    // Static and shared, because there are TWO code paths that finish a region:
    // RegionManager's, and RegionTotem's fallback for when the totem never found
    // a manager. The second one used to be deliberately plain — "no cinematic,
    // just the bookkeeping and the door" — which was correct while the victory
    // presentation lived inside RegionManager and could not run without one.
    // It does not any more, so that reasoning expired, and the player captured a
    // region through that path and saw nothing at all.
    public static List<Award> AwardsFor(RegionData region)
    {
        var list = new List<Award>(4);
        if (region == null) return list;

        // Icons come from the exploration index because that is where the
        // project already keeps the three resource sprites resolved for runtime.
        // A missing one costs the row its picture and nothing else.
        var set = ReliquarySet.Load();
        void Add(Sprite icon, string key, int amount, Color tint)
        {
            if (amount <= 0) return;   // never show a reward of nothing
            list.Add(new Award { icon = icon, label = LocalizationManager.Tr(key), amount = amount, tint = tint });
        }

        Add(set != null ? set.woodIcon : null, "Wood", region.woodReward, new Color(0.85f, 0.6f, 0.35f));
        Add(set != null ? set.stoneIcon : null, "Stone", region.stoneReward, new Color(0.8f, 0.8f, 0.85f));
        Add(set != null ? set.foodIcon : null, "Food", region.foodReward, new Color(0.7f, 0.95f, 0.5f));
        Add(null, "Diamonds", region.diamondReward, new Color(0.7f, 0.85f, 1f));

        if (list.Count == 0)
            Debug.LogWarning($"[Victory] Region '{region.regionID}' pays no wood, stone, food or diamonds, so the " +
                             "victory screen has nothing to show. That is almost certainly unintended — check its " +
                             "reward fields.");
        return list;
    }

    public static void Show(string title, string subtitle, List<Award> awards, Action onDone)
    {
        if (Instance == null)
        {
            var prefab = Resources.Load<GameObject>(PrefabResource);
            GameObject go = prefab != null ? Instantiate(prefab) : new GameObject("[RegionVictory]");
            go.name = "[RegionVictory]";
            DontDestroyOnLoad(go);
            if (go.GetComponent<RegionVictoryScreen>() == null) go.AddComponent<RegionVictoryScreen>();
        }
        if (Instance != null) Instance.Play(title, subtitle, awards, onDone);
    }

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        Build();
        _group.alpha = 0f;
    }

    private void OnDestroy()
    {
        if (Instance == this) { Instance = null; IsShowing = false; }
    }

    // ---- construction ---------------------------------------------------------

    // Drop a prefab at Assets/Resources/UI/RegionVictory.prefab and it is used
    // instead of the bare object.
    public const string PrefabResource = "UI/RegionVictory";

    // Fills in whatever was NOT authored, so a half-converted screen works.
    private void Build()
    {
        var canvas = GetComponent<Canvas>();
        if (canvas == null)
        {
            canvas = gameObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            // Above everything, including the reward reveal — this is a scene
            // transition, and nothing may draw over it.
            canvas.sortingOrder = 5000;
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
        _group.blocksRaycasts = true;

        if (_veil == null)
        {
            _veil = MakeImage("Veil", transform);
            var vrt = _veil.rectTransform;
            vrt.anchorMin = Vector2.zero;
            vrt.anchorMax = Vector2.one;
            vrt.offsetMin = vrt.offsetMax = Vector2.zero;
            _veil.color = new Color(0f, 0f, 0f, dim);
        }

        if (_title == null) _title = MakeText("Title", 250f, 74f, FontStyles.Bold);
        if (_subtitle == null) _subtitle = MakeText("Subtitle", 180f, 30f, FontStyles.Normal);

        if (_rows == null)
        {
            var rowsGo = new GameObject("Rows", typeof(RectTransform));
            _rows = rowsGo.GetComponent<RectTransform>();
            _rows.SetParent(transform, false);
            _rows.anchorMin = _rows.anchorMax = new Vector2(0.5f, 0.5f);
            _rows.pivot = new Vector2(0.5f, 0.5f);
            _rows.anchoredPosition = new Vector2(0f, 10f);
        }

        if (_continue == null) _continue = MakeText("Continue", -300f, 30f, FontStyles.Normal);
    }

    private static Image MakeImage(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var img = go.AddComponent<Image>();
        img.raycastTarget = false;
        return img;
    }

    private TextMeshProUGUI MakeText(string name, float y, float size, FontStyles style)
    {
        var go = new GameObject(name, typeof(RectTransform));
        var rt = go.GetComponent<RectTransform>();
        rt.SetParent(transform, false);
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = new Vector2(0f, y);
        rt.sizeDelta = new Vector2(1400f, size * 1.7f);
        var t = go.AddComponent<TextMeshProUGUI>();
        t.alignment = TextAlignmentOptions.Center;
        t.fontSize = size;
        t.fontStyle = style;
        t.raycastTarget = false;
        return t;
    }

    // One award line: icon, name, amount. Built per show and torn down after,
    // because the set of awards differs per region and pooling four rows is not
    // worth the state it would carry between screens.
    private class Row
    {
        public CanvasGroup group;
        public RectTransform rt;
        public Image icon;
        public TextMeshProUGUI amount;
        public int target;
    }

    private readonly List<Row> _built = new List<Row>(4);

    // ---- the beat -------------------------------------------------------------

    private void Play(string title, string subtitle, List<Award> awards, Action onDone)
    {
        StopAllCoroutines();
        _onDone = onDone;
        StartCoroutine(Routine(title, subtitle, awards));
    }

    private IEnumerator Routine(string title, string subtitle, List<Award> awards)
    {
        IsShowing = true;
        _accepting = false;

        foreach (var r in _built) if (r != null && r.rt != null) Destroy(r.rt.gameObject);
        _built.Clear();

        _title.text = title ?? "";
        _title.color = new Color(1f, 0.93f, 0.72f);
        _subtitle.text = subtitle ?? "";
        _subtitle.color = new Color(0.72f, 0.72f, 0.70f);
        _continue.text = "";

        // Lay the rows out first, invisible, so the block is centred as a whole
        // rather than growing downward and dragging the eye with it.
        // ==== ONE LINE, NOT A LIST READ OUT TO YOU ====
        //
        // The awards were stacked vertically and revealed one at a time on a
        // 0.42s stagger, then given 0.55s to finish counting. Four rewards is
        // therefore a 0.6s fade plus about two and a quarter seconds of waiting
        // before the player is even allowed to press a key — for four numbers
        // they can read in one glance.
        //
        // A capture screen should land, not recite. They sit side by side now
        // and arrive together.
        const float tileWidth = 190f;
        float left = -(awards.Count - 1) * tileWidth * 0.5f;
        for (int i = 0; i < awards.Count; i++)
        {
            var a = awards[i];
            var row = BuildRow(a, new Vector2(left + i * tileWidth, 0f));
            row.target = a.amount;
            _built.Add(row);
        }

        // FADE IN. Everything on unscaled time — the game may well be paused or
        // in slow motion when a region falls, and a victory screen that waits on
        // a stopped clock is the exact bug this whole screen exists to end.
        float t = 0f;
        while (t < fadeIn)
        {
            t += Time.unscaledDeltaTime;
            _group.alpha = Mathf.Clamp01(t / fadeIn);
            yield return null;
        }
        _group.alpha = 1f;

        // AWARDS, together. The stagger is a ripple across the line rather than
        // a queue — enough that the eye sees them arrive, far too short to wait
        // through. One sound for the lot, because four identical chimes in a row
        // is not four times as satisfying as one.
        if (AudioManager.Instance != null) AudioManager.Instance.PlayUI(AudioID.Camp_CollectItem);
        for (int i = 0; i < _built.Count; i++)
        {
            StartCoroutine(RevealRow(_built[i]));
            float wait = 0f;
            while (wait < rowStagger) { wait += Time.unscaledDeltaTime; yield return null; }
        }

        float settle = 0f;
        while (settle < countUp) { settle += Time.unscaledDeltaTime; yield return null; }

        // NOW take input — never before. Accepting a key while the numbers are
        // still counting means a player holding a movement key skips the entire
        // screen without seeing a single one of them.
        _accepting = true;
        float elapsed = 0f;
        while (elapsed < watchdog)
        {
            elapsed += Time.unscaledDeltaTime;
            // Blink, so the line reads as "waiting for you" and not as decoration.
            float k = 0.45f + 0.55f * Mathf.Abs(Mathf.Sin(Time.unscaledTime * 2.1f));
            _continue.text = LocalizationManager.Tr("PRESS_ANY_KEY");
            _continue.color = new Color(0.9f, 0.9f, 0.88f, k);
            if (Input.anyKeyDown) break;
            yield return null;
        }

        IsShowing = false;
        _accepting = false;
        _onDone?.Invoke();

        // The screen stays up through the load and fades once the next scene has
        // it, so the player never sees a frame of the conquered region again.
        t = 0f;
        while (t < 0.45f)
        {
            t += Time.unscaledDeltaTime;
            _group.alpha = 1f - Mathf.Clamp01(t / 0.45f);
            yield return null;
        }
        _group.alpha = 0f;
    }

    private Row BuildRow(Award a, Vector2 pos)
    {
        var go = new GameObject("Award", typeof(RectTransform));
        var rt = go.GetComponent<RectTransform>();
        rt.SetParent(_rows, false);
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = pos;
        rt.sizeDelta = new Vector2(180f, 170f);

        var group = go.AddComponent<CanvasGroup>();
        group.alpha = 0f;

        // A tile, read top to bottom: what it is, then how much. Side by side
        // these scan as one line of loot rather than as a list of statements.
        var icon = MakeImage("Icon", rt);
        icon.sprite = a.icon;
        icon.preserveAspect = true;
        icon.color = a.tint;
        // No sprite is not a reason to show nothing — the label and the number
        // still carry the information, and an empty Image renders as a white box.
        icon.enabled = a.icon != null;
        var irt = icon.rectTransform;
        irt.anchorMin = irt.anchorMax = new Vector2(0.5f, 1f);
        irt.pivot = new Vector2(0.5f, 1f);
        irt.anchoredPosition = new Vector2(0f, -6f);
        irt.sizeDelta = new Vector2(68f, 68f);

        var amount = new GameObject("Amount", typeof(RectTransform)).GetComponent<RectTransform>();
        amount.SetParent(rt, false);
        amount.anchorMin = amount.anchorMax = new Vector2(0.5f, 1f);
        amount.pivot = new Vector2(0.5f, 1f);
        amount.anchoredPosition = new Vector2(0f, -82f);
        amount.sizeDelta = new Vector2(176f, 52f);
        var at = amount.gameObject.AddComponent<TextMeshProUGUI>();
        at.text = "0";
        at.fontSize = 44f;
        at.fontStyle = FontStyles.Bold;
        at.alignment = TextAlignmentOptions.Center;
        at.color = a.tint;
        at.raycastTarget = false;

        var label = new GameObject("Label", typeof(RectTransform)).GetComponent<RectTransform>();
        label.SetParent(rt, false);
        label.anchorMin = label.anchorMax = new Vector2(0.5f, 1f);
        label.pivot = new Vector2(0.5f, 1f);
        label.anchoredPosition = new Vector2(0f, -134f);
        label.sizeDelta = new Vector2(176f, 34f);
        var lt = label.gameObject.AddComponent<TextMeshProUGUI>();
        lt.text = a.label;
        lt.fontSize = 24f;
        lt.alignment = TextAlignmentOptions.Center;
        lt.color = new Color(0.78f, 0.78f, 0.76f);
        lt.raycastTarget = false;

        return new Row { group = group, rt = rt, icon = icon, amount = at };
    }

    private IEnumerator RevealRow(Row row)
    {
        // Slides in from the left and overshoots slightly. The number counts up
        // rather than appearing: a number that ticks reads as something being
        // handed over, and one that is simply there reads as a receipt.
        Vector2 home = row.rt.anchoredPosition;
        // Up from below now that the tiles sit side by side. Sliding in from the
        // left made every tile cross its neighbour on the way to its place.
        Vector2 from = home + new Vector2(0f, -40f);

        float t = 0f;
        const float slide = 0.3f;
        while (t < slide)
        {
            t += Time.unscaledDeltaTime;
            float k = Mathf.Clamp01(t / slide);
            float eased = 1f - Mathf.Pow(1f - k, 3f);
            row.group.alpha = eased;
            row.rt.anchoredPosition = Vector2.LerpUnclamped(from, home, eased);
            float pop = 1f + Mathf.Sin(k * Mathf.PI) * 0.12f;
            if (row.icon != null) row.icon.rectTransform.localScale = Vector3.one * pop;
            yield return null;
        }
        row.group.alpha = 1f;
        row.rt.anchoredPosition = home;
        if (row.icon != null) row.icon.rectTransform.localScale = Vector3.one;

        t = 0f;
        while (t < countUp)
        {
            t += Time.unscaledDeltaTime;
            float k = 1f - Mathf.Pow(1f - Mathf.Clamp01(t / countUp), 3f);
            row.amount.text = "+" + Mathf.RoundToInt(Mathf.Lerp(0f, row.target, k));
            yield return null;
        }
        row.amount.text = "+" + row.target;
    }

    // Lets the caller know whether a key press would land, so a skip elsewhere
    // does not fight this screen for the same input.
    public bool AcceptingInput => _accepting;
}
