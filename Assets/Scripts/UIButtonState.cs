using UnityEngine;
using UnityEngine.UI;

// Makes an unavailable button look unavailable, not half-deleted.
//
// ==== WHY TRANSPARENCY IS THE WRONG SIGNAL ====
//
// Unity's default disabled colour is the normal colour at about half alpha, so
// a button the player cannot press fades toward whatever is behind it. On a
// busy panel that reads as "this control is broken" or "this control is
// vanishing", and in the barracks — where most rows are unaffordable most of
// the time — it makes the whole list look like it is failing to load.
//
// The information the player needs is "not yet", and that is a matter of
// CONTRAST, not opacity: keep the shape solid and legible, drain the colour out
// of it. The button stays obviously a button, obviously present, and obviously
// not ready. Its label stays readable too, which matters most of all, because
// the label is usually the thing that says WHY it is not ready.
//
// ==== WHY THIS DOES NOT TOUCH THE TRANSITION ====
//
// The barracks buttons are SpriteSwap, which ignores the ColorBlock entirely,
// so an earlier version of this switched them to ColorTint in order to have
// something to drive. That was wrong twice over: the authored highlighted and
// pressed sprites stopped being used, and the disabled state fell back to the
// normal sprite — an unavailable button drawn pixel-for-pixel like an available
// one.
//
// So nothing here writes `transition`, `colors` or `spriteState`. The dim is
// applied on top, to the Graphics themselves, which works the same for every
// transition mode and leaves the artist's sprites exactly as authored.
public static class UIButtonState
{
    // Sets interactable AND installs the dimmer, so no prefab has to be
    // re-authored one button at a time.
    public static void SetInteractable(Selectable target, bool on)
    {
        if (target == null) return;
        Dim(target);
        target.interactable = on;
    }

    // Installs the dimmer on a single Selectable. Idempotent.
    public static void Dim(Selectable target)
    {
        if (target == null) return;
        if (target.GetComponent<DisabledDimmer>() == null)
            target.gameObject.AddComponent<DisabledDimmer>();
    }

    // Applies the same treatment to every Selectable under a root — for panels
    // that build their rows at runtime and would otherwise need the call at
    // every construction site.
    public static void SolidifyAllUnder(Component root)
    {
        if (root == null) return;
        foreach (var s in root.GetComponentsInChildren<Selectable>(true))
            Dim(s);
    }

    // Older call sites used this name.
    public static void SolidifyDisabledColour(Selectable target) => Dim(target);
}

// Drains colour out of a Selectable's graphics while it is not interactable.
//
// Alpha is left EXACTLY as authored — fading is the thing this exists to
// replace. Only hue saturation and value move, so the button keeps its shape,
// its sprite and its text, and simply reads as greyed out.
[DisallowMultipleComponent]
public class DisabledDimmer : MonoBehaviour
{
    [Tooltip("Fraction of the original saturation kept while disabled. 0 = flat grey.")]
    public float saturation = 0.15f;

    [Tooltip("Fraction of the original brightness kept while disabled.")]
    public float brightness = 0.45f;

    private Selectable _target;
    private Graphic[] _graphics;
    private Color[] _original;
    private bool _captured;
    private bool _dimmed;

    private void Awake()
    {
        _target = GetComponent<Selectable>();
    }

    private void OnEnable()
    {
        // The row may have been re-pooled with different colours; re-read them.
        _captured = false;
        _dimmed = false;
    }

    private void Capture()
    {
        _graphics = GetComponentsInChildren<Graphic>(true);
        _original = new Color[_graphics.Length];
        for (int i = 0; i < _graphics.Length; i++)
            _original[i] = _graphics[i] != null ? _graphics[i].color : Color.white;
        _captured = true;
    }

    private void LateUpdate()
    {
        if (_target == null) return;

        bool wantDim = !_target.interactable;
        if (wantDim == _dimmed && _captured) return;

        // Capture only once we know the authored colours are the live ones:
        // the first frame the state actually matters.
        if (!_captured) Capture();

        for (int i = 0; i < _graphics.Length; i++)
        {
            var g = _graphics[i];
            if (g == null) continue;

            if (wantDim)
            {
                Color src = _original[i];
                Color.RGBToHSV(src, out float h, out float s, out float v);
                Color dim = Color.HSVToRGB(h, s * saturation, v * brightness);
                dim.a = src.a;          // opacity is the artist's call, not ours
                g.color = dim;
            }
            else
            {
                g.color = _original[i];
            }
        }

        _dimmed = wantDim;
    }
}
