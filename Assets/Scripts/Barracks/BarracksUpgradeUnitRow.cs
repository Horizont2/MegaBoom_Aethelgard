using TMPro;
using UnityEngine;
using UnityEngine.UI;

// Row displayed once per unit archetype in the UPGRADE UNITS tab.
public class BarracksUpgradeUnitRow : MonoBehaviour
{
    public Image iconImage;
    public TextMeshProUGUI nameText;
    // Flavour text like the hire row.
    public TextMeshProUGUI descriptionText;
    // 5 pip Images (diamond glyphs) get sprite-swapped based on the
    // unit's current level. Element 0 = leftmost pip.
    public Image[] levelPips;
    // 4 stat-preview slots — "ATK 25 → 33" is split into
    // (atkCurrentText, atkNextText). Same for HP.
    public TextMeshProUGUI atkCurrentText;
    public TextMeshProUGUI atkNextText;
    public TextMeshProUGUI hpCurrentText;
    public TextMeshProUGUI hpNextText;
    public TextMeshProUGUI costText;
    public Button upgradeButton;
    public TextMeshProUGUI upgradeButtonText;

    private MercenaryUnitData boundData;
    private MercenaryRoster boundRoster;
    private BarracksUpgradePanel boundPanel;

    public void Bind(MercenaryUnitData data, MercenaryRoster roster, BarracksUpgradePanel panel, Sprite customPortrait)
    {
        boundData = data;
        boundRoster = roster;
        boundPanel = panel;

        int lvl = roster.GetUpgradeLevel(data.unitID);
        int maxLvl = data.MaxLevel;

        if (iconImage != null)
        {
            if (customPortrait != null) iconImage.sprite = customPortrait;
            else if (data.icon != null) iconImage.sprite = data.icon;
        }

        if (nameText != null) nameText.text = LocalizationManager.Tr(data.displayName);
        if (descriptionText != null) descriptionText.text = LocalizationManager.Tr(data.flavourText);

        RefreshPips(lvl, maxLvl);

        int nextLvl = Mathf.Min(lvl + 1, maxLvl);
        int atkNow = data.AttackAtLevel(lvl);
        int atkNxt = data.AttackAtLevel(nextLvl);
        int hpNow = data.HPAtLevel(lvl);
        int hpNxt = data.HPAtLevel(nextLvl);

        if (atkCurrentText != null) atkCurrentText.text = atkNow.ToString();
        if (atkNextText != null) atkNextText.text = atkNxt.ToString();
        if (hpCurrentText != null) hpCurrentText.text = hpNow.ToString();
        if (hpNextText != null) hpNextText.text = hpNxt.ToString();

        bool isMax = lvl >= maxLvl;
        int cost = 0;
        if (!isMax && data.upgradePricePerLevel != null && lvl - 1 < data.upgradePricePerLevel.Length && lvl - 1 >= 0)
            cost = data.upgradePricePerLevel[lvl - 1];
        else if (!isMax && lvl == 0 && data.upgradePricePerLevel != null && data.upgradePricePerLevel.Length > 0)
            cost = data.upgradePricePerLevel[0];

        if (costText != null) costText.text = isMax ? LocalizationManager.Tr("MERC_BTN_MAX") : cost.ToString();
        if (upgradeButtonText != null) upgradeButtonText.text = isMax ? LocalizationManager.Tr("MERC_BTN_MAX") : LocalizationManager.Tr("MERC_BTN_UPGRADE");

        bool canAfford = !isMax && ResourceManager.Instance != null && ResourceManager.Instance.CanAffordDiamonds(cost);
        if (upgradeButton != null)
        {
            upgradeButton.interactable = canAfford;
            upgradeButton.onClick.RemoveAllListeners();
            upgradeButton.onClick.AddListener(() => OnUpgradeClick(cost));
        }
    }

    private void RefreshPips(int currentLevel, int maxLevel)
    {
        if (levelPips == null || levelPips.Length == 0 || boundPanel == null) return;
        var filled = boundPanel.GetUnitPipFilledSprite();
        var empty = boundPanel.GetUnitPipEmptySprite();
        for (int i = 0; i < levelPips.Length; i++)
        {
            var img = levelPips[i];
            if (img == null) continue;
            // Show only as many slots as the archetype supports — hide extras.
            img.enabled = i < maxLevel;
            img.sprite = (i < currentLevel) ? filled : empty;
        }
    }

    private void OnUpgradeClick(int cost)
    {
        if (boundData == null || boundRoster == null || ResourceManager.Instance == null) return;
        if (!ResourceManager.Instance.CanAffordDiamonds(cost)) return;

        int lvl = boundRoster.GetUpgradeLevel(boundData.unitID);
        if (lvl >= boundData.MaxLevel) return;

        ResourceManager.Instance.SpendDiamonds(cost);
        boundRoster.SetUpgradeLevel(boundData.unitID, lvl + 1);
        if (AudioManager.Instance != null) AudioManager.Instance.PlayUI(AudioID.UI_Click);
    }
}
