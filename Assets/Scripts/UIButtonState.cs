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
public static class UIButtonState
{
    [Tooltip("How much colour a disabled button keeps. 0 = flat grey, 1 = unchanged.")]
    private const float DisabledSaturation = 0.35f;
    private const float DisabledBrightness = 0.55f;

    // Sets interactable AND repairs the disabled tint, so no prefab has to be
    // re-authored one button at a time.
    public static void SetInteractable(Selectable target, bool on)
    {
        if (target == null) return;
        SolidifyDisabledColour(target);
        target.interactable = on;
    }

    // Rewrites just the disabled entry of the button's ColorBlock: fully
    // opaque, desaturated and darkened from whatever the normal colour is.
    private static bool s_warnedTransition;

    public static void SolidifyDisabledColour(Selectable target)
    {
        if (target == null) return;

        // This can only repair a ColorTint button — that is where the disabled
        // ALPHA lives. A button using Sprite Swap or Animation gets its faded
        // look from an authored sprite or an animation clip, and no code change
        // here can touch it; it has to be fixed in the prefab. Saying so once is
        // better than silently doing nothing to half the panel.
        if (target.transition != Selectable.Transition.ColorTint)
        {
            if (!s_warnedTransition)
            {
                s_warnedTransition = true;
                Debug.LogWarning($"[UI] '{target.name}' uses {target.transition} transition, not ColorTint, so its " +
                                 "disabled look comes from an authored sprite or clip and cannot be fixed in code. " +
                                 "Switch it to Color Tint, or author an opaque disabled sprite.", target);
            }
            return;
        }

        var c = target.colors;
        Color normal = c.normalColor;

        Color.RGBToHSV(normal, out float h, out float s, out float v);
        Color dim = Color.HSVToRGB(h, s * DisabledSaturation, v * DisabledBrightness);
        // Opaque, always. This is the entire point.
        dim.a = 1f;

        if (c.disabledColor != dim)
        {
            c.disabledColor = dim;
            target.colors = c;
        }
    }

    // Applies the same treatment to every Selectable under a root — for panels
    // that build their rows at runtime and would otherwise need the call at
    // every construction site.
    public static void SolidifyAllUnder(Component root)
    {
        if (root == null) return;
        foreach (var s in root.GetComponentsInChildren<Selectable>(true))
            SolidifyDisabledColour(s);
    }
}
