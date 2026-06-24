using UnityEngine;
using UnityEngine.UI;

// Drag-and-drop theme swapper. Sits on the SettingsPanelAAA root and
// holds slots for every art asset that comes out of Figma. Press
// "Apply Theme" in the inspector (or call ApplyAll at runtime) and
// the swapper walks the panel hierarchy, retargeting Image.sprite on
// every part it recognises by name.
//
// Add sprites here as you export them; empty slots are skipped so it
// works incrementally — replace pieces one at a time without breaking
// what's already wired up.
public class SettingsAAATheme : MonoBehaviour
{
    [Header("=== Panels & Cards ===")]
    [Tooltip("Soft brush-stroke card. Used for Header, Footer, Sidebar, Center, Right Rail.")]
    public Sprite panelBg;
    [Tooltip("Inner card BG used by PREVIEW and DESCRIPTION blocks on the right rail.")]
    public Sprite innerCardBg;
    [Tooltip("Background fill for individual rows (Mouse Sensitivity, Field of View, …). Optional.")]
    public Sprite rowBg;

    [Header("=== Sidebar ===")]
    [Tooltip("Background of every category button.")]
    public Sprite categoryBg;
    [Tooltip("Background of the currently selected category (gold-tinted card).")]
    public Sprite categorySelectedBg;
    [Tooltip("Per-category 32×32 icons. Slot order matches the sidebar: General, Gameplay, Audio, Video, Graphics, Controls, Accessibility, Language.")]
    public Sprite[] categoryIcons = new Sprite[8];

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

            // Root overlay BG.
            if (img.gameObject == this.gameObject)
            {
                if (backgroundArt != null) Slice(img, backgroundArt);
                continue;
            }

            // Header / Footer / Sidebar / Center / RightRail / Preview / Description.
            string parentName = t.parent != null ? t.parent.name : "";
            string grandName = (t.parent != null && t.parent.parent != null) ? t.parent.parent.name : "";

            if (panelBg != null && (n == "Header" || n == "Footer" || n == "Sidebar" || n == "Center" || n == "RightRail"))
            { Slice(img, panelBg); continue; }
            if (innerCardBg != null && (n == "Preview" || n == "Description"))
            { Slice(img, innerCardBg); continue; }

            // Category buttons in sidebar.
            if (categoryBg != null && n.StartsWith("Cat_"))
            { Slice(img, categoryBg); continue; }

            // Per-row background plate.
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

        // Dropdown arrow + per-category icons live in TMP labels we
        // can't drive by sprite — they're set up as text glyphs.
        // Skip them silently; the user can replace those with Image
        // children manually if they want sprite icons.

        Debug.Log("[Settings Theme] Applied current sprite slots to the panel.");
    }

    private static void Slice(Image img, Sprite s)
    {
        img.sprite = s;
        img.type = Image.Type.Sliced;
        img.pixelsPerUnitMultiplier = 1f;
    }
}
