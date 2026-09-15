using UnityEngine;
using UnityEngine.UI;

// A health bar over a freed companion — the one authored on the prefab if there
// is one, otherwise a plain built-in bar.
//
// ==== WHY BOTH ====
//
// The ally is instantiated from whichever hero prefab the encounter was wired
// with, at runtime, in a generated world. A bar that MUST be authored onto that
// prefab is a bar that exists on one hero and not the others, and this project
// has lost features to exactly that — a component nobody remembered to add. So
// an ally with nothing assigned still gets one.
//
// But a generated bar can never match the game's art, and it should not be the
// only option. Assign barRoot and a fill below and this drives yours instead,
// touching nothing it was not given: it positions, billboards, scales the fill
// and tints it, and leaves every other part of your hierarchy alone.
//
// ==== WHY THE FALLBACK IS NOT A WORLD-SPACE CANVAS ====
//
// A Canvas per ally is three components, a raycaster and a layout rebuild for
// something that draws two quads. Two sprite renderers on a billboarded child
// cost almost nothing and cannot interact with the UI event system by accident.
// An assigned bar may of course be a Canvas — that is your call, not this
// script's.
[DisallowMultipleComponent]
public class AllyHealthBar : MonoBehaviour
{
    [Header("Who this belongs to")]
    [Tooltip("Left empty, the AllyAI on this object (or a parent) is used.")]
    public AllyAI ally;

    [Header("Your own bar (optional)")]
    [Tooltip("The root of the health bar you authored. Left empty, a plain bar is built at runtime. Assign this and nothing is generated.")]
    public Transform barRoot;

    [Tooltip("Turn the bar to face the camera each frame. Switch off if your bar is already a billboard, or is parented to something that handles it.")]
    public bool billboard = true;

    [Tooltip("Reposition barRoot to healthBarHeight above the ally every frame. Switch off to keep the bar exactly where you placed it in the prefab.")]
    public bool drivePosition = false;

    [Header("The fill")]
    [Tooltip("A UI Image to drive. If its Image Type is Filled, fillAmount is used; otherwise it is scaled horizontally like a sprite.")]
    public Image fillImage;

    [Tooltip("A sprite/transform to scale horizontally instead. Ignored when fillImage is set.")]
    public Transform fillTransform;

    [Tooltip("Width the fill has at full health, in the fill's own local units. Only used when scaling — read from the fill on Awake if left at zero.")]
    public float fullFillWidth = 0f;

    [Tooltip("Anchor the scaling fill to its left edge so it drains from the right. Switch off if your fill's pivot is already on its left edge.")]
    public bool fillDrainsFromRight = true;

    [Header("Colour")]
    [Tooltip("Tint the fill by remaining health. Switch off to keep the colours you authored.")]
    public bool tintByHealth = true;
    public Color healthyColour = new Color(0.45f, 0.85f, 0.40f);
    public Color hurtColour = new Color(0.95f, 0.80f, 0.30f);
    public Color criticalColour = new Color(0.90f, 0.30f, 0.25f);

    private Transform _fill;
    private SpriteRenderer _fillSr;
    private SpriteRenderer _backSr;
    private Graphic[] _graphics;
    private Renderer[] _renderers;
    private Camera _cam;
    private bool _generated;
    private float _fillLeftEdge;
    private static Sprite s_quad;

    // Called by AllyAI for companions that have no bar authored. No-ops when one
    // is already present, so an assigned bar is never replaced.
    public static void Attach(AllyAI ally)
    {
        if (ally == null || ally.GetComponent<AllyHealthBar>() != null) return;
        ally.gameObject.AddComponent<AllyHealthBar>();
    }

    private void Awake()
    {
        if (ally == null) ally = GetComponentInParent<AllyAI>();
        if (ally == null) { enabled = false; return; }

        if (barRoot == null) BuildFallbackBar();
        else AdoptAuthoredBar();
    }

    private void AdoptAuthoredBar()
    {
        _generated = false;

        _fill = fillImage != null ? fillImage.transform : fillTransform;
        if (_fill != null)
        {
            if (fullFillWidth <= 0f) fullFillWidth = Mathf.Abs(_fill.localScale.x);
            // Where the fill's LEFT edge sits when it is full. Everything below
            // keeps that edge fixed and moves the centre, which is what draining
            // from the right means for a centre-pivoted object.
            _fillLeftEdge = _fill.localPosition.x - fullFillWidth * 0.5f;
        }

        // Cached so showing and hiding costs nothing per frame. Both kinds are
        // collected because an authored bar may be UI, sprites, or a mix.
        _graphics = barRoot.GetComponentsInChildren<Graphic>(true);
        _renderers = barRoot.GetComponentsInChildren<Renderer>(true);
    }

    private void BuildFallbackBar()
    {
        _generated = true;

        var rootGo = new GameObject("HealthBar");
        barRoot = rootGo.transform;
        barRoot.SetParent(transform, false);
        barRoot.localPosition = Vector3.up * ally.healthBarHeight;
        billboard = true;
        drivePosition = true;

        _backSr = MakeQuad("Back", new Color(0.05f, 0.04f, 0.03f, 0.85f), 0);
        _backSr.transform.localScale = new Vector3(ally.healthBarWidth, 0.13f, 1f);

        _fillSr = MakeQuad("Fill", healthyColour, 1);
        _fill = _fillSr.transform;
        fullFillWidth = ally.healthBarWidth;
        // Anchor the fill's LEFT edge so shrinking it drains from the right
        // instead of from both sides — a bar that closes inward reads as a
        // loading spinner, not as damage.
        _fillLeftEdge = -ally.healthBarWidth * 0.5f;
        _fill.localPosition = new Vector3(0f, 0f, -0.01f);
        _fill.localScale = new Vector3(ally.healthBarWidth, 0.10f, 1f);
        fillDrainsFromRight = true;
    }

    private SpriteRenderer MakeQuad(string name, Color c, int order)
    {
        var go = new GameObject(name);
        go.transform.SetParent(barRoot, false);
        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = Quad();
        sr.color = c;
        sr.sortingOrder = 500 + order;
        return sr;
    }

    private void SetVisible(bool on)
    {
        if (_generated)
        {
            if (_backSr != null) _backSr.enabled = on;
            if (_fillSr != null) _fillSr.enabled = on;
            return;
        }

        // An authored bar is switched at the root, which also covers frames,
        // pips, text and anything else hanging off it.
        if (barRoot != null && barRoot.gameObject.activeSelf != on)
            barRoot.gameObject.SetActive(on);
    }

    private void LateUpdate()
    {
        if (ally == null || ally.IsDead)
        {
            if (_generated && barRoot != null) Destroy(barRoot.gameObject);
            else SetVisible(false);
            enabled = false;
            return;
        }

        if (barRoot == null) return;

        // Hidden at full health. A bar that is always there is furniture; one
        // that appears when the companion is hurt is information.
        //
        // And hidden entirely while the AllyAI is switched off. A caged captive
        // has not run OnEnable yet, so its health is still zero — without this
        // the prisoner sits in the cage under a full red empty bar.
        bool show = ally.isActiveAndEnabled && ally.showHealthBar && ally.Health01 < 0.999f;
        SetVisible(show);
        if (!show) return;

        if (drivePosition) barRoot.localPosition = Vector3.up * ally.healthBarHeight;

        if (billboard)
        {
            if (_cam == null || !_cam.isActiveAndEnabled) _cam = Camera.main;
            // Face the camera's PLANE rather than its position, so a row of bars
            // stays parallel instead of fanning out.
            if (_cam != null) barRoot.rotation = _cam.transform.rotation;
        }

        float k = ally.Health01;

        if (fillImage != null && fillImage.type == Image.Type.Filled)
        {
            fillImage.fillAmount = k;
        }
        else if (_fill != null && fullFillWidth > 0f)
        {
            var sc = _fill.localScale;
            sc.x = fullFillWidth * k;
            _fill.localScale = sc;

            if (fillDrainsFromRight)
            {
                // A centre-pivoted fill has to be pushed left by half of
                // whatever it lost, or it would shrink toward its middle.
                var lp = _fill.localPosition;
                lp.x = _fillLeftEdge + sc.x * 0.5f;
                _fill.localPosition = lp;
            }
        }

        if (!tintByHealth) return;

        Color c = k > 0.5f ? healthyColour : k > 0.25f ? hurtColour : criticalColour;
        if (fillImage != null) fillImage.color = c;
        else if (_fillSr != null) _fillSr.color = c;
        else if (_fill != null)
        {
            var sr = _fill.GetComponent<SpriteRenderer>();
            if (sr != null) sr.color = c;
        }
    }

    // One white pixel, shared by every generated bar in the scene.
    private static Sprite Quad()
    {
        if (s_quad != null) return s_quad;
        var tex = new Texture2D(1, 1, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave };
        tex.SetPixel(0, 0, Color.white);
        tex.Apply();
        s_quad = Sprite.Create(tex, new Rect(0, 0, 1, 1), new Vector2(0.5f, 0.5f), 1f);
        s_quad.hideFlags = HideFlags.HideAndDontSave;
        return s_quad;
    }
}
