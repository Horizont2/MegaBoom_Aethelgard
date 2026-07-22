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

        if (nameText != null) nameText.text = LocalizationManager.Tr(data.displayName);
        if (descriptionText != null) descriptionText.text = LocalizationManager.Tr(data.flavourText);
        if (ownedText != null) ownedText.text = LocalizationManager.Tr("MERC_OWNED", roster.CountAlive(data.unitID));

        int cost = data.baseHireCost;
        if (costText != null) costText.text = cost.ToString();

        bool unlocked = barracksLevel >= data.minBarracksLevel;
        bool canAfford = ResourceManager.Instance != null && ResourceManager.Instance.CanAffordDiamonds(cost);
        bool hasRoom = !roster.IsAtCapacity;

        if (hireButton != null)
        {
            hireButton.interactable = unlocked && canAfford && hasRoom;
            hireButton.onClick.RemoveAllListeners();
            hireButton.onClick.AddListener(OnHireClick);
        }
    }

    private void OnHireClick()
    {
        if (boundData == null || boundRoster == null) return;
        if (ResourceManager.Instance == null) return;
        // Capacity check BEFORE spending — Hire() also refuses at cap,
        // but by then the diamonds would already be gone.
        if (boundRoster.IsAtCapacity)
        {
            ToastManager.Show(LocalizationManager.Tr("MERC_TOAST_ARMY_FULL", boundRoster.maxArmySize), ToastManager.ToastKind.Warning);
            return;
        }
        int cost = boundData.baseHireCost;
        if (!ResourceManager.Instance.CanAffordDiamonds(cost)) return;

        var hired = boundRoster.Hire(boundData.unitID);
        if (hired == null) return; // refused (race) — nothing charged
        ResourceManager.Instance.SpendDiamonds(cost);
        if (AudioManager.Instance != null) AudioManager.Instance.PlayUI(AudioID.UI_Click);
    }
}
