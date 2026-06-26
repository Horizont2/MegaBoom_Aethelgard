using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// Tiny per-row component that pushes its description into the right
// rail when the pointer enters the row, and restores the default text
// when the pointer leaves.
public class SettingsAAARowHover : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    public string description;
    public SettingsAAARuntime runtime;

    private Image bg;
    private Color baseColor;
    private Color hoverColor;
    private bool initialised;

    private void Awake()
    {
        bg = GetComponent<Image>();
        if (bg != null) { baseColor = bg.color; hoverColor = new Color(1f, 0.82f, 0.24f, 0.10f); }
        initialised = true;
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        if (!initialised) Awake();
        if (runtime != null) runtime.SetDescription(description);
        if (bg != null) bg.color = hoverColor;
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        if (runtime != null) runtime.ClearDescription();
        if (bg != null) bg.color = baseColor;
    }
}
