using UnityEngine;

// A small health bar over a freed companion.
//
// ==== WHY IT IS BUILT IN CODE AND NOT A PREFAB ====
//
// The ally is instantiated from whichever hero prefab the encounter was wired
// with, at runtime, in a generated world. A bar that has to be authored onto
// that prefab is a bar that exists on one hero and not the others, and this
// project has lost three features to exactly that — a component nobody
// remembered to add. Building it on demand means every companion has one, for
// free, whatever prefab it came from.
//
// ==== AND WHY IT IS NOT WORLD-SPACE CANVAS ====
//
// A Canvas per ally is three components, a raycaster and a layout rebuild for
// something that draws two quads. Two sprite renderers on a billboarded child
// cost almost nothing and cannot interact with the UI event system by accident.
[DisallowMultipleComponent]
public class AllyHealthBar : MonoBehaviour
{
    private AllyAI _ally;
    private Transform _root;
    private Transform _fill;
    private SpriteRenderer _fillSr;
    private SpriteRenderer _backSr;
    private Camera _cam;
    private static Sprite s_quad;

    public static void Attach(AllyAI ally)
    {
        if (ally == null || ally.GetComponent<AllyHealthBar>() != null) return;
        ally.gameObject.AddComponent<AllyHealthBar>().Build(ally);
    }

    private void Build(AllyAI ally)
    {
        _ally = ally;

        var rootGo = new GameObject("HealthBar");
        _root = rootGo.transform;
        _root.SetParent(transform, false);
        _root.localPosition = Vector3.up * ally.healthBarHeight;

        _backSr = MakeQuad("Back", new Color(0.05f, 0.04f, 0.03f, 0.85f), 0);
        _backSr.transform.localScale = new Vector3(ally.healthBarWidth, 0.13f, 1f);

        _fillSr = MakeQuad("Fill", new Color(0.45f, 0.85f, 0.4f, 1f), 1);
        _fill = _fillSr.transform;
        // Pivot the fill at its LEFT edge so shrinking it drains from the right
        // instead of from both sides — a bar that closes inward reads as a
        // loading spinner, not as damage.
        _fill.localPosition = new Vector3(-ally.healthBarWidth * 0.5f, 0f, -0.01f);
        _fill.localScale = new Vector3(ally.healthBarWidth, 0.10f, 1f);
    }

    private SpriteRenderer MakeQuad(string name, Color c, int order)
    {
        var go = new GameObject(name);
        go.transform.SetParent(_root, false);
        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = Quad();
        sr.color = c;
        sr.sortingOrder = 500 + order;
        return sr;
    }

    private void LateUpdate()
    {
        if (_ally == null || _ally.IsDead) { Destroy(gameObject.GetComponent<AllyHealthBar>()); if (_root != null) Destroy(_root.gameObject); return; }

        if (_cam == null || !_cam.isActiveAndEnabled) _cam = Camera.main;
        if (_cam == null || _root == null) return;

        // Hidden at full health. A bar that is always there is furniture; one
        // that appears when the companion is hurt is information.
        bool show = _ally.showHealthBar && _ally.Health01 < 0.999f;
        if (_backSr.enabled != show) { _backSr.enabled = show; _fillSr.enabled = show; }
        if (!show) return;

        _root.localPosition = Vector3.up * _ally.healthBarHeight;
        // Billboard: face the camera's plane rather than the camera's position,
        // so a row of bars stays parallel instead of fanning out.
        _root.rotation = _cam.transform.rotation;

        float k = _ally.Health01;
        var sc = _fill.localScale;
        sc.x = _ally.healthBarWidth * k;
        _fill.localScale = sc;
        // Left-anchored: the quad's own pivot is its centre, so it has to be
        // pushed right by half of whatever it lost.
        var lp = _fill.localPosition;
        lp.x = -_ally.healthBarWidth * 0.5f + sc.x * 0.5f;
        _fill.localPosition = lp;

        _fillSr.color = k > 0.5f ? new Color(0.45f, 0.85f, 0.4f)
                      : k > 0.25f ? new Color(0.95f, 0.8f, 0.3f)
                                  : new Color(0.9f, 0.3f, 0.25f);
    }

    // One white pixel, shared by every bar in the scene.
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
