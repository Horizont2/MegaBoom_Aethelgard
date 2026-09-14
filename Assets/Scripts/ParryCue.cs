using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

// "Press block NOW." One mark per incoming attack.
//
// ==== WHY THE FIGHT NEEDED THIS BEFORE IT NEEDED ANYTHING ELSE ====
//
// Parrying is a timing read, and the game was asking the player to make it with
// no clock. The tell existed — a colour pulse on the enemy and a growl — but
// neither says WHEN, only THAT. So the player could see an attack coming and
// still have no way to know which moment inside it was the one, and the
// mechanic could only be learned by dying into it a hundred times.
//
// A shrinking ring collapsing onto a fixed dot answers "when" in the only way
// that needs no explanation: press when they meet. It is the same reason a
// swing-meter works — the information is a distance, and a distance is read
// instantly and from the corner of the eye, which is where this will usually
// be read from.
//
// ==== AND WHY IT HAS TO WORK FOR ENEMIES YOU CANNOT SEE ====
//
// A guard is a direction, so an attack from behind is a real threat that the
// player must answer by turning. But turning is only possible if they know it
// is coming and from where — otherwise being flanked is not a challenge, it is
// an ambush every few seconds, and the shield stops being worth holding at all.
//
// So a cue for an off-screen attacker pins itself to the screen edge in that
// attacker's direction. Same ring, same timing, and the position tells you
// which way to turn.
[DisallowMultipleComponent]
public class ParryCue : MonoBehaviour
{
    public static ParryCue Instance { get; private set; }

    [Tooltip("Pixel size of the dot the ring collapses onto.")]
    public float dotSize = 22f;
    [Tooltip("How wide the ring starts, as a multiple of the dot. The distance between them IS the countdown, so it needs room to travel.")]
    public float ringStartScale = 4.2f;
    [Tooltip("Margin from the screen edge for an attacker behind the camera.")]
    public float edgeMargin = 90f;

    private static readonly Color Waiting = new Color(1f, 0.72f, 0.25f, 1f);   // wind-up
    private static readonly Color Window = new Color(1f, 0.98f, 0.85f, 1f);   // parry NOW
    private static readonly Color Unblockable = new Color(1f, 0.35f, 0.75f, 1f);   // do not block this

    private class Cue
    {
        public EnemyAI who;
        public float startedAt;
        public float telegraph;
        public float window;
        public bool unblockable;
        public RectTransform dot;
        public RectTransform ring;
        public Image dotImg;
        public Image ringImg;
    }

    private readonly List<Cue> _live = new List<Cue>(8);
    private readonly Stack<Cue> _pool = new Stack<Cue>(8);
    private Transform _player;
    private Camera _cam;
    private static Sprite s_disc;
    private static Sprite s_ring;

    // Called by the enemy the instant it commits to a swing.
    public static void Show(EnemyAI who, float telegraph, float window, bool unblockable)
    {
        if (who == null || telegraph <= 0.01f) return;
        if (Instance == null)
        {
            var go = new GameObject("[ParryCue]");
            DontDestroyOnLoad(go);
            go.AddComponent<ParryCue>();
        }
        if (Instance != null) Instance.Begin(who, telegraph, window, unblockable);
    }

    // The attack resolved (blocked, parried, landed, or the enemy was staggered
    // out of it). The mark should go immediately — one that lingers after the
    // moment has passed teaches the wrong timing.
    public static void Cancel(EnemyAI who)
    {
        if (Instance == null || who == null) return;
        for (int i = Instance._live.Count - 1; i >= 0; i--)
            if (Instance._live[i].who == who) Instance.Retire(i);
    }

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;

        var canvas = gameObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        // Under the parry banner and the reward reveal, over the ordinary HUD.
        canvas.sortingOrder = 3850;

        var scaler = gameObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        var group = gameObject.AddComponent<CanvasGroup>();
        group.blocksRaycasts = false;
        group.interactable = false;
    }

    private void OnDestroy() { if (Instance == this) Instance = null; }

    private void Begin(EnemyAI who, float telegraph, float window, bool unblockable)
    {
        // One mark per attacker. A second swing replaces the first rather than
        // stacking two countdowns on the same enemy.
        for (int i = _live.Count - 1; i >= 0; i--) if (_live[i].who == who) Retire(i);

        Cue c = _pool.Count > 0 ? _pool.Pop() : Build();
        c.who = who;
        c.startedAt = Time.time;
        c.telegraph = telegraph;
        c.window = Mathf.Clamp(window, 0.05f, telegraph);
        c.unblockable = unblockable;
        c.dot.gameObject.SetActive(true);
        c.ring.gameObject.SetActive(true);
        _live.Add(c);
    }

    private Cue Build()
    {
        var c = new Cue();

        c.dot = NewImage("CueDot", Disc(), out c.dotImg);
        c.ring = NewImage("CueRing", Ring(), out c.ringImg);
        // The ring is drawn behind the dot so the moment they meet reads as the
        // ring landing ON the target rather than swallowing it.
        c.ring.SetSiblingIndex(0);
        return c;
    }

    private RectTransform NewImage(string name, Sprite sprite, out Image img)
    {
        var go = new GameObject(name, typeof(RectTransform));
        var rt = go.GetComponent<RectTransform>();
        rt.SetParent(transform, false);
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        img = go.AddComponent<Image>();
        img.sprite = sprite;
        img.raycastTarget = false;
        img.preserveAspect = true;
        return rt;
    }

    private void Retire(int index)
    {
        var c = _live[index];
        c.who = null;
        c.dot.gameObject.SetActive(false);
        c.ring.gameObject.SetActive(false);
        _live.RemoveAt(index);
        _pool.Push(c);
    }

    private void LateUpdate()
    {
        if (_live.Count == 0) return;

        if (_player == null)
        {
            var p = GameObject.FindGameObjectWithTag("Player");
            if (p != null) _player = p.transform;
        }
        if (_cam == null || !_cam.isActiveAndEnabled) _cam = Camera.main;
        if (_cam == null) return;

        var canvasRect = (RectTransform)transform;
        float halfW = canvasRect.rect.width * 0.5f;
        float halfH = canvasRect.rect.height * 0.5f;

        for (int i = _live.Count - 1; i >= 0; i--)
        {
            var c = _live[i];
            if (c.who == null || c.who.IsDead) { Retire(i); continue; }

            float age = Time.time - c.startedAt;
            // A short tail past the strike so the mark does not vanish on the
            // exact frame of impact, which reads as a dropped frame.
            if (age > c.telegraph + 0.12f) { Retire(i); continue; }

            Vector2 pos = ScreenPositionFor(c.who, halfW, halfH, out bool offScreen);

            // The countdown: 0 at the start of the wind-up, 1 at the strike.
            float k = Mathf.Clamp01(age / c.telegraph);
            // The window opens this far through the wind-up.
            float windowOpensAt = 1f - (c.window / c.telegraph);
            bool inWindow = k >= windowOpensAt;

            float scale = Mathf.Lerp(ringStartScale, 1f, k);
            c.ring.anchoredPosition = pos;
            c.dot.anchoredPosition = pos;
            c.ring.sizeDelta = Vector2.one * dotSize * scale;
            c.dot.sizeDelta = Vector2.one * dotSize;

            Color tint = c.unblockable ? Unblockable : (inWindow ? Window : Waiting);

            // Fade in over the first fraction so a mark never pops, and dim the
            // whole thing for an attacker the player cannot see less than one
            // they can — off-screen marks are guidance, not the main event.
            float alpha = Mathf.Clamp01(k / 0.15f);
            if (offScreen) alpha *= 0.85f;

            var ringCol = tint; ringCol.a = alpha * (inWindow ? 1f : 0.75f);
            var dotCol = tint; dotCol.a = alpha * (inWindow ? 1f : 0.35f);
            c.ringImg.color = ringCol;
            c.dotImg.color = dotCol;

            // A single pulse the moment the window opens — the ring is already
            // saying it, and a change of SIZE as well as colour is what makes it
            // register in peripheral vision.
            if (inWindow)
            {
                float into = Mathf.InverseLerp(windowOpensAt, 1f, k);
                float pop = 1f + Mathf.Sin(into * Mathf.PI) * 0.22f;
                c.dot.sizeDelta = Vector2.one * dotSize * pop;
            }
        }
    }

    // Where the mark sits: over the enemy when they are in view, pinned to the
    // edge in their direction when they are not.
    private Vector2 ScreenPositionFor(EnemyAI who, float halfW, float halfH, out bool offScreen)
    {
        Vector3 head = who.transform.position + Vector3.up * 2.1f;
        Vector3 sp = _cam.WorldToScreenPoint(head);
        offScreen = sp.z <= 0f || sp.x < 0f || sp.x > Screen.width || sp.y < 0f || sp.y > Screen.height;

        // Behind the camera flips the projection, so the direction has to be
        // rebuilt rather than trusted.
        if (sp.z <= 0f) { sp.x = Screen.width - sp.x; sp.y = Screen.height - sp.y; }

        // Screen pixels -> canvas units, centred.
        float cx = (sp.x / Screen.width - 0.5f) * halfW * 2f;
        float cy = (sp.y / Screen.height - 0.5f) * halfH * 2f;
        var v = new Vector2(cx, cy);

        if (!offScreen) return v;

        // Pin to the edge along the same bearing, so the mark tells the player
        // which way to turn.
        float mx = Mathf.Max(10f, halfW - edgeMargin);
        float my = Mathf.Max(10f, halfH - edgeMargin);
        if (v.sqrMagnitude < 0.01f) v = Vector2.down;
        float scale = Mathf.Min(mx / Mathf.Max(0.001f, Mathf.Abs(v.x)),
                                my / Mathf.Max(0.001f, Mathf.Abs(v.y)));
        return v * scale;
    }

    // ---- procedural sprites -------------------------------------------------
    //
    // Generated rather than authored, for the same reason the reward reveal
    // builds its own starburst: an effect that has to be assigned in an
    // inspector is an effect that is missing in half the scenes, and this one is
    // load-bearing for a core mechanic.

    private static Sprite Disc()
    {
        if (s_disc != null) return s_disc;
        s_disc = Circle(64, 0f);
        return s_disc;
    }

    private static Sprite Ring()
    {
        if (s_ring != null) return s_ring;
        s_ring = Circle(64, 0.80f);
        return s_ring;
    }

    // innerHole 0 = filled disc; >0 = a ring with that fraction cut out.
    private static Sprite Circle(int size, float innerHole)
    {
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
        {
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp,
            hideFlags = HideFlags.HideAndDontSave,
        };

        float r = size * 0.5f;
        var px = new Color32[size * size];
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float dx = x + 0.5f - r, dy = y + 0.5f - r;
                float d = Mathf.Sqrt(dx * dx + dy * dy) / r;

                // Antialiased in texture space, so it stays clean at any size.
                float a = Mathf.Clamp01((1f - d) * r * 0.5f);
                if (innerHole > 0f) a = Mathf.Min(a, Mathf.Clamp01((d - innerHole) * r * 0.5f));
                px[y * size + x] = new Color32(255, 255, 255, (byte)(Mathf.Clamp01(a) * 255f));
            }
        }
        tex.SetPixels32(px);
        tex.Apply(false, false);

        var s = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
        s.hideFlags = HideFlags.HideAndDontSave;
        return s;
    }
}
