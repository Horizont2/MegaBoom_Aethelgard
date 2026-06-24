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

    public void Refresh()
    {
        if (theme == null) return;
        Color fill, stroke, text;
        if (!isInteractable)        { fill = theme.categoryFillDisabled;  stroke = theme.categoryStrokeDisabled;  text = theme.categoryTextDefault; }
        else if (isSelected)        { fill = theme.categoryFillSelected;  stroke = theme.categoryStrokeSelected;  text = theme.categoryTextSelected; }
        else if (isHovering)        { fill = theme.categoryFillHover;     stroke = theme.categoryStrokeHover;     text = theme.categoryTextDefault; }
        else                        { fill = theme.categoryFillDefault;   stroke = theme.categoryStrokeDefault;   text = theme.categoryTextDefault; }

        if (fillImg   != null) fillImg.color   = fill;
        if (strokeImg != null) strokeImg.color = stroke;
        if (labelText != null) labelText.color = text;
        if (labelTMP  != null) labelTMP.color  = text;
    }
}
