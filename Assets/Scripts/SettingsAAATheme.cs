using UnityEngine;
using UnityEngine.UI;

// Drag-and-drop theme swapper. Sits on the SettingsPanelAAA root and
// holds slots for every art asset that comes out of Figma. Press
// "Apply Theme" in the inspector (or call ApplyAll at runtime) and
// the swapper walks the panel hierarchy, retargeting Image.sprite on
// every part it recognises by name.
//
// Two-layer mode: when both fill + stroke sprites are supplied for a
// part (currently only category buttons), the swapper paints the fill
// on the main Image and creates a sibling "Stroke" Image overlay on
// top. State colour is then tinted independently per layer, giving the
// painted gold-border-over-olive-fill look without baked sprite states.
//
// Empty slots are skipped so it works incrementally — replace pieces
// one at a time without breaking what's already wired up.
public class SettingsAAATheme : MonoBehaviour
{
    [Header("=== Panels & Cards ===")]
    [Tooltip("Soft brush-stroke card. Used for Sidebar, Center, Right Rail.")]
    public Sprite panelBg;
    [Tooltip("Inner card BG used by PREVIEW and DESCRIPTION blocks on the right rail.")]
    public Sprite innerCardBg;
    [Tooltip("Background fill for individual rows (Mouse Sensitivity, Field of View, …). Optional.")]
    public Sprite rowBg;

    [Header("=== Sidebar — Two-Layer (fill + stroke) ===")]
    [Tooltip("Default category card fill — no stroke baked in.")]
    public Sprite categoryBgFill;
    [Tooltip("Default category card brush-stroke overlay — transparent center, painted edge only.")]
    public Sprite categoryBgStroke;
    [Tooltip("Per-category 32×32 icons. Slot order matches the sidebar: General, Gameplay, Audio, Video, Graphics, Controls, Accessibility, Language.")]
    public Sprite[] categoryIcons = new Sprite[8];

    [Header("=== Sidebar — State Colours ===")]
    // Selected fill is a saturated dark olive (gold pulled way down on
    // value) so the bright gold text + brush stroke read on top of it —
    // matches the painted GAMEPLAY card in the reference exactly.
    public Color categoryFillDefault   = new Color(0.105f, 0.115f, 0.13f, 1f);
    public Color categoryStrokeDefault = new Color(0.04f,  0.05f,  0.06f, 1f);
    public Color categoryFillHover     = new Color(0.14f,  0.15f,  0.18f, 1f);
    public Color categoryStrokeHover   = new Color(0.45f,  0.37f,  0.11f, 1f);
    public Color categoryFillSelected  = new Color(0.42f,  0.33f,  0.10f, 1f);
    public Color categoryStrokeSelected= new Color(1f,     0.823f, 0.247f, 1f);
    public Color categoryFillDisabled  = new Color(0.10f,  0.10f,  0.12f, 0.5f);
    public Color categoryStrokeDisabled= new Color(0.20f,  0.20f,  0.22f, 0.5f);
    public Color categoryTextDefault   = new Color(0.96f,  0.94f,  0.90f, 1f);
    public Color categoryTextSelected  = new Color(1f,     0.92f,  0.55f, 1f);

    [Header("=== Sliders ===")]
    public Sprite sliderTrack;
    public Sprite sliderFill;
    public Sprite sliderHandle;

    [Header("=== Toggles ===")]
    [Tooltip("Pill background of every toggle.")]
    public Sprite togglePill;
    [Tooltip("Round knob image.")]
    public Sprite toggleKnob;

    [Header("=== Dropdowns ===")]
    public Sprite dropdownBg;
    public Sprite dropdownArrow;
    public Sprite dropdownTemplateBg;

    [Header("=== Inputs ===")]
    public Sprite inputBg;

    [Header("=== Buttons ===")]
    [Tooltip("Default button background — used by BACK, RESET DEFAULTS, DISCARD, CLOSE.")]
    public Sprite buttonBg;
    [Tooltip("Highlighted primary button — used by APPLY & CLOSE.")]
    public Sprite buttonPrimaryBg;

    [Header("=== Scrollbar ===")]
    public Sprite scrollbarTrack;
    public Sprite scrollbarHandle;

    [Header("=== Background ===")]
    [Tooltip("Optional full-screen background image (forest / scene art).")]
    public Sprite backgroundArt;

    private static readonly string[] CategoryOrder =
        { "General", "Gameplay", "Audio", "Video", "Graphics", "Controls", "Accessibility", "Language" };

    [ContextMenu("Apply Theme")]
    public void ApplyAll()
    {
        // Walk every Image in the panel once, decide what it is by name
        // + parent path, and assign the matching sprite if we have one.
        Image[] images = GetComponentsInChildren<Image>(true);
        foreach (var img in images)
        {
            if (img == null) continue;
            string n = img.gameObject.name;
            Transform t = img.transform;

            if (img.gameObject == this.gameObject)
            {
                if (backgroundArt != null) Slice(img, backgroundArt);
                continue;
            }

            string parentName = t.parent != null ? t.parent.name : "";
            string grandName = (t.parent != null && t.parent.parent != null) ? t.parent.parent.name : "";

            // Category buttons — two-layer pipeline.
            if (n.StartsWith("Cat_"))
            {
                if (categoryBgFill != null)
                {
                    Slice(img, categoryBgFill);
                    img.color = categoryFillDefault;
                }
                EnsureStrokeChild(img.transform, categoryBgStroke, categoryStrokeDefault);
                BindCategoryStateRefs(img);
                continue;
            }

            if (panelBg != null && (n == "Sidebar" || n == "Center" || n == "RightRail" || n == "Header" || n == "Footer"))
            { Slice(img, panelBg); continue; }
            if (innerCardBg != null && (n == "Preview" || n == "Description"))
            { Slice(img, innerCardBg); continue; }
            if (rowBg != null && n.StartsWith("Row_"))
            { Slice(img, rowBg); continue; }

            // Slider parts.
            if (sliderTrack != null && n == "Background" && parentName == "Slider")
            { Slice(img, sliderTrack); continue; }
            if (sliderFill != null && n == "Fill" && grandName == "Slider")
            { Slice(img, sliderFill); continue; }
            if (sliderHandle != null && n == "Handle" && grandName == "Slider")
            { Slice(img, sliderHandle); continue; }

            // Toggle parts.
            if (togglePill != null && n == "Background" && parentName == "Toggle")
            { Slice(img, togglePill); continue; }
            if (toggleKnob != null && n == "Checkmark" && grandName == "Toggle")
            { Slice(img, toggleKnob); continue; }

            // Dropdown.
            if (dropdownBg != null && n == "Dropdown")
            { Slice(img, dropdownBg); continue; }
            if (dropdownTemplateBg != null && n == "Template" && parentName == "Dropdown")
            { Slice(img, dropdownTemplateBg); continue; }

            // Input field.
            if (inputBg != null && n == "Input")
            { Slice(img, inputBg); continue; }

            // Buttons.
            if (buttonBg != null && (n == "BackButton" || n == "ResetButton" || n == "DiscardButton" || n == "CloseButton"))
            { Slice(img, buttonBg); continue; }
            if (buttonPrimaryBg != null && n == "ApplyButton")
            { Slice(img, buttonPrimaryBg); continue; }
        }

        Debug.Log("[Settings Theme] Applied current sprite slots to the panel.");
    }

    // For two-layer parts: spawn (or update) a sibling "Stroke" Image
    // on top of the fill, sized to match. Skipped when no stroke sprite
    // is supplied — caller falls back to single-layer behaviour.
    private static void EnsureStrokeChild(Transform parent, Sprite strokeSprite, Color strokeColor)
    {
        Transform existing = parent.Find("Stroke");
        Image strokeImg;
        if (existing == null)
        {
            GameObject go = new GameObject("Stroke", typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            RectTransform rt = go.GetComponent<RectTransform>();
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            strokeImg = go.GetComponent<Image>();
            strokeImg.raycastTarget = false;
        }
        else
        {
            strokeImg = existing.GetComponent<Image>();
            if (strokeImg == null) strokeImg = existing.gameObject.AddComponent<Image>();
        }
        if (strokeSprite != null)
        {
            Slice(strokeImg, strokeSprite);
            strokeImg.color = strokeColor;
        }
        else
        {
            // No stroke sprite supplied yet — make the layer invisible
            // so we don't end up with a solid dark plate on top of fill.
            strokeImg.sprite = null;
            strokeImg.color = new Color(0f, 0f, 0f, 0f);
        }
    }

    // Wire SettingsAAACategoryButton (creates one if missing) so it can
    // tint fill + stroke layers on hover / select.
    private void BindCategoryStateRefs(Image fill)
    {
        var btn = fill.GetComponent<SettingsAAACategoryButton>();
        if (btn == null) btn = fill.gameObject.AddComponent<SettingsAAACategoryButton>();
        btn.theme = this;
        btn.fillImg = fill;
        Transform strokeT = fill.transform.Find("Stroke");
        btn.strokeImg = strokeT != null ? strokeT.GetComponent<Image>() : null;
        Transform labelT = fill.transform.Find("Text");
        if (labelT != null) btn.labelText = labelT.GetComponent<Text>();
        var tmp = fill.GetComponentInChildren<TMPro.TextMeshProUGUI>(true);
        if (tmp != null) btn.labelTMP = tmp;
        btn.Refresh();
    }

    private static void Slice(Image img, Sprite s)
    {
        img.sprite = s;
        img.type = Image.Type.Sliced;
        img.pixelsPerUnitMultiplier = 1f;
    }
}

