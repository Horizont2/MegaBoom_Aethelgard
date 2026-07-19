using TMPro;
using UnityEngine;
using UnityEngine.UI;

// Row displayed once per unit archetype in the pre-battle army selection.
// Split into its own file so Unity's script importer produces a MonoScript
// asset — otherwise saving the PreBattleUnitRow.prefab hits "missing script".
public class PreBattleUnitRow : MonoBehaviour
{
    public Image iconImage;
    public TextMeshProUGUI nameText;
    public TextMeshProUGUI availableText;
    public TextMeshProUGUI countText;
    public Button plusButton;
    public Button minusButton;

    private MercenaryUnitData boundData;
    private MercenaryRoster boundRoster;
    private PreBattlePanel boundPanel;

    public void Bind(MercenaryUnitData data, MercenaryRoster roster, PreBattlePanel panel, Sprite customPortrait = null)
    {
        boundData = data;
        boundRoster = roster;
        boundPanel = panel;

        if (iconImage != null)
        {
            if (customPortrait != null) iconImage.sprite = customPortrait;
            else if (data.icon != null) iconImage.sprite = data.icon;
        }
        if (nameText != null) nameText.text = LocalizationManager.Tr(data.displayName);

        if (plusButton != null)
        {
            plusButton.onClick.RemoveAllListeners();
            plusButton.onClick.AddListener(OnPlus);
        }
        if (minusButton != null)
        {
            minusButton.onClick.RemoveAllListeners();
            minusButton.onClick.AddListener(OnMinus);
        }

        Refresh();
    }

    public void Refresh()
    {
        if (boundData == null || boundRoster == null || boundPanel == null) return;
        int idle = boundRoster.CountIdle(boundData.unitID);
        int count = boundPanel.GetDesiredCount(boundData.unitID);
        if (availableText != null) availableText.text = LocalizationManager.Tr("MERC_AVAILABLE", idle);
        if (countText != null) countText.text = count.ToString();

        if (plusButton != null) plusButton.interactable = count < idle;
        if (minusButton != null) minusButton.interactable = count > 0;
    }

    private void OnPlus()
    {
        int count = boundPanel.GetDesiredCount(boundData.unitID);
        boundPanel.SetDesiredCount(boundData.unitID, count + 1);
    }

    private void OnMinus()
    {
        int count = boundPanel.GetDesiredCount(boundData.unitID);
        boundPanel.SetDesiredCount(boundData.unitID, count - 1);
    }
}
