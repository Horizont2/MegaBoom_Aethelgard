using TMPro;
using UnityEngine;
using UnityEngine.UI;

// One row of the CampaignStatusHUD strip. Fields are optional — the
// AutoWireIfNeeded() call walks children by name so a designer can drop a
// row prefab with matching child names and not have to hand-wire fields.
public class CampaignStatusRow : MonoBehaviour
{
    public TextMeshProUGUI regionNameText;
    public TextMeshProUGUI timeText;
    public Image phaseIcon;

    public void AutoWireIfNeeded()
    {
        if (regionNameText == null)
        {
            var t = FindChildRecursive(transform, "RegionName");
            if (t != null) regionNameText = t.GetComponent<TextMeshProUGUI>();
        }
        if (timeText == null)
        {
            var t = FindChildRecursive(transform, "TimeRemaining");
            if (t != null) timeText = t.GetComponent<TextMeshProUGUI>();
        }
        if (phaseIcon == null)
        {
            var t = FindChildRecursive(transform, "PhaseIcon");
            if (t != null) phaseIcon = t.GetComponent<Image>();
        }
    }

    private static Transform FindChildRecursive(Transform root, string name)
    {
        if (root.name == name) return root;
        for (int i = 0; i < root.childCount; i++)
        {
            var r = FindChildRecursive(root.GetChild(i), name);
            if (r != null) return r;
        }
        return null;
    }
}
