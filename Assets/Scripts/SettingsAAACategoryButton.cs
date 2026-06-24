using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using TMPro;

// Per-category sidebar button with default / hover / selected / disabled
// visual states. Sits next to the category Button + Image. Tints the
// fill image, the brush-stroke overlay (if any), and the icon/label
// text colours via the parent SettingsAAATheme palette — so designers
// only need to touch one set of fields to retheme every category.
public class SettingsAAACategoryButton : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    public SettingsAAATheme theme;
    public Image fillImg;
    public Image strokeImg;
    public Text labelText;            // Legacy fallback if used
    public TextMeshProUGUI labelTMP;  // Preferred — that's what the builder spawns

    public bool isSelected;
    public bool isInteractable = true;

    private bool isHovering;

    public void SetSelected(bool selected)
    {
        isSelected = selected;
        Refresh();
    }

    public void SetInteractable(bool interactable)
    {
        isInteractable = interactable;
        Refresh();
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        if (!isInteractable) return;
        isHovering = true;
        Refresh();
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        isHovering = false;
        Refresh();
    }

    // Tween target — colour lerps smoothly toward this each frame
    // instead of snapping. Reads as much more polished on hover.
    private Color targetFill, targetStroke, targetText;
    private Vector3 targetScale = Vector3.one;
    private bool hasTargets;

    public void Refresh()
    {
        if (theme == null) return;
        Color fill, stroke, text;
        Vector3 scale = Vector3.one;
        if (!isInteractable)        { fill = theme.categoryFillDisabled;  stroke = theme.categoryStrokeDisabled;  text = theme.categoryTextDefault; }
        else if (isSelected)        { fill = theme.categoryFillSelected;  stroke = theme.categoryStrokeSelected;  text = theme.categoryTextSelected; scale = new Vector3(1.04f, 1.04f, 1f); }
        else if (isHovering)        { fill = theme.categoryFillHover;     stroke = theme.categoryStrokeHover;     text = theme.categoryTextDefault; scale = new Vector3(1.02f, 1.02f, 1f); }
        else                        { fill = theme.categoryFillDefault;   stroke = theme.categoryStrokeDefault;   text = theme.categoryTextDefault; }

        targetFill = fill;
        targetStroke = stroke;
        targetText = text;
        targetScale = scale;
        hasTargets = true;
    }

    private void Update()
    {
        if (!hasTargets) return;
        // Smooth ~150ms colour & scale tween — purely cosmetic but
        // separates AAA from "raw" feel. Driven on unscaledDeltaTime so
        // the settings panel still animates while Time.timeScale = 0.
        float t = 1f - Mathf.Exp(-Time.unscaledDeltaTime * 14f);
        if (fillImg   != null) fillImg.color   = Color.Lerp(fillImg.color,   targetFill,   t);
        if (strokeImg != null) strokeImg.color = Color.Lerp(strokeImg.color, targetStroke, t);
        if (labelText != null) labelText.color = Color.Lerp(labelText.color, targetText,   t);
        if (labelTMP  != null) labelTMP.color  = Color.Lerp(labelTMP.color,  targetText,   t);
        transform.localScale = Vector3.Lerp(transform.localScale, targetScale, t);
    }
}
