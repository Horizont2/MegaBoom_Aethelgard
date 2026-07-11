using TMPro;
using UnityEngine;
using UnityEngine.UI;

// Row displayed once per unit archetype in the HIRE tab. The prefab wires
// widgets via SerializeField; Bind() is called by BarracksUpgradePanel every
// refresh to fill values and re-hook the button.
//
// Kept in its own file because Unity's script importer will not create a
// MonoScript asset for a MonoBehaviour class whose filename does not match
// the class name — the resulting prefab reference shows as "missing script".
public class BarracksHireRow : MonoBehaviour
{
    public Image iconImage;
    public TextMeshProUGUI nameText;
    // One-line flavour under the name, filled from MercenaryUnitData.flavourText.
    public TextMeshProUGUI descriptionText;
    public TextMeshProUGUI ownedText;
    public TextMeshProUGUI costText;
    public Button hireButton;

    private MercenaryUnitData boundData;
    private MercenaryRoster boundRoster;

    public void Bind(MercenaryUnitData data, MercenaryRoster roster, int barracksLevel, Sprite customPortrait)
    {
        boundData = data;
        boundRoster = roster;

        // Використовуємо кастомний портрет, якщо він є. Якщо ні - беремо стандартну іконку.
        if (iconImage != null)
        {
            if (customPortrait != null) iconImage.sprite = customPortrait;
            else if (data.icon != null) iconImage.sprite = data.icon;
        }

        if (nameText != null) nameText.text = data.displayName;
        if (descriptionText != null) descriptionText.text = data.flavourText;
        if (ownedText != null) ownedText.text = $"OWNED: {roster.CountAlive(data.unitID)}";

        int cost = data.baseHireCost;
        if (costText != null) costText.text = cost.ToString();

        bool unlocked = barracksLevel >= data.minBarracksLevel;
        bool canAfford = ResourceManager.Instance != null && ResourceManager.Instance.CanAffordDiamonds(cost);

        if (hireButton != null)
        {
            hireButton.interactable = unlocked && canAfford;
            hireButton.onClick.RemoveAllListeners();
            hireButton.onClick.AddListener(OnHireClick);
        }
    }

    private void OnHireClick()
    {
        if (boundData == null || boundRoster == null) return;
        if (ResourceManager.Instance == null) return;
        int cost = boundData.baseHireCost;
        if (!ResourceManager.Instance.CanAffordDiamonds(cost)) return;

        ResourceManager.Instance.SpendDiamonds(cost);
        boundRoster.Hire(boundData.unitID);
        if (AudioManager.Instance != null) AudioManager.Instance.PlayUI(AudioID.UI_Click);
    }
}
