using UnityEngine;
using UnityEngine.UI;

// Drives a toggle's knob position + colour. Unity's built-in Toggle
// hides `graphic` when isOn = false which is why our knob was vanishing
// instead of sliding. We bypass that — the knob is always visible — and
// animate it ourselves on every value change.
[RequireComponent(typeof(RectTransform))]
public class SettingsAAAToggleKnob : MonoBehaviour
{
    public Toggle toggle;
    public Image knob;
    public Color onColor = new Color(0.36f, 0.75f, 0.87f, 1f);
    public Color offColor = new Color(0.55f, 0.58f, 0.62f, 1f);
    public float padding = 3f;

    private RectTransform knobRT;

    private void Awake()
    {
        if (knob != null) knobRT = knob.GetComponent<RectTransform>();
        if (toggle != null)
        {
            toggle.onValueChanged.RemoveListener(Apply);
            toggle.onValueChanged.AddListener(Apply);
            Apply(toggle.isOn);
        }
    }

    private void OnEnable() { if (toggle != null) Apply(toggle.isOn); }

    private void Apply(bool on)
    {
        if (knobRT == null || knob == null) return;
        knobRT.anchorMin = new Vector2(on ? 1f : 0f, 0.5f);
        knobRT.anchorMax = new Vector2(on ? 1f : 0f, 0.5f);
        knobRT.pivot     = new Vector2(on ? 1f : 0f, 0.5f);
        knobRT.anchoredPosition = new Vector2(on ? -padding : padding, 0f);
        knob.color = on ? onColor : offColor;
    }
}
