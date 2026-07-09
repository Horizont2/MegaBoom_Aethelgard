using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using TMPro;

// One card in the shop's category list. Extended from the original minimal
// (name + icon + button) so each row can convey at a glance:
//   * whether the item is locked, owned, or currently equipped
//   * the price / MAX badge
//   * a rarity/tier tint on the frame
//   * hover-enter / hover-exit events for delta-stat previews
// All inspector references are optional — if you leave any field null it
// just doesn't render that piece, so this drops into old prefabs without
// breaking them.
public class ShopItemButton : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    public enum OwnedState { Locked, Owned, Equipped }

    [Header("Core (existing)")]
    public TextMeshProUGUI nameText;
    public Image iconImage;
    public Button buttonComponent;

    [Header("State Overlays (optional)")]
    [Tooltip("Displayed when the item is locked. Should be a lock icon over the card.")]
    public GameObject lockedOverlay;
    [Tooltip("Displayed when the item is bought but not equipped. A small check mark works well.")]
    public GameObject ownedCheckmark;
    [Tooltip("Displayed when the item is currently equipped. Ribbon along the top or side of the card.")]
    public GameObject equippedRibbon;

    [Header("Price / MAX Badge (optional)")]
    [Tooltip("Small text on the card that shows the buy price for locked items and \"MAX\" when the item is at max upgrade level.")]
    public TextMeshProUGUI priceBadgeText;
    [Tooltip("The parent GameObject of priceBadgeText — toggled off when the item is Owned/Equipped and not at MAX so we don't show a stray price.")]
    public GameObject priceBadgeRoot;
    public Color affordableColor = new Color(0.92f, 0.85f, 0.55f);
    public Color unaffordableColor = new Color(0.90f, 0.55f, 0.55f);
    public Color maxColor = new Color(0.95f, 0.80f, 0.45f);

    [Header("Rarity / Tier Tint (optional)")]
    [Tooltip("Optional Image to tint by the item's tier (usually the card frame).")]
    public Image tierFrame;

    // Fires when the pointer enters this card. ShopManager listens to power
    // the delta-stat hover preview.
    public System.Action<ShopItemButton> OnHoverEnter;
    public System.Action<ShopItemButton> OnHoverExit;

    // Data-bag so hover callbacks can read what this button represents
    // without ShopManager keeping its own parallel lookup. Set by ShopManager
    // when the button is created.
    [HideInInspector] public WeaponData boundWeapon;
    [HideInInspector] public ArmorData boundArmor;

    public void SetState(OwnedState state, int currentLevel, int maxLevel, int price, int playerDiamonds)
    {
        if (lockedOverlay != null) lockedOverlay.SetActive(state == OwnedState.Locked);
        if (ownedCheckmark != null) ownedCheckmark.SetActive(state == OwnedState.Owned);
        if (equippedRibbon != null) equippedRibbon.SetActive(state == OwnedState.Equipped);

        bool atMax = currentLevel >= maxLevel;
        if (priceBadgeText != null)
        {
            if (state == OwnedState.Locked)
            {
                priceBadgeText.text = price.ToString("N0");
                priceBadgeText.color = playerDiamonds >= price ? affordableColor : unaffordableColor;
                if (priceBadgeRoot != null) priceBadgeRoot.SetActive(true);
            }
            else if (atMax)
            {
                priceBadgeText.text = "MAX";
                priceBadgeText.color = maxColor;
                if (priceBadgeRoot != null) priceBadgeRoot.SetActive(true);
            }
            else
            {
                if (priceBadgeRoot != null) priceBadgeRoot.SetActive(false);
            }
        }
    }

    public void SetTierColor(Color c)
    {
        if (tierFrame != null) tierFrame.color = c;
        // Tint the item's name to match its tier for at-a-glance rarity read.
        // Small alpha lift keeps the text legible on dark cards.
        if (nameText != null)
        {
            Color nameC = c;
            nameC.a = 1f;
            nameText.color = nameC;
        }
    }

    public void OnPointerEnter(PointerEventData eventData) { OnHoverEnter?.Invoke(this); }
    public void OnPointerExit(PointerEventData eventData) { OnHoverExit?.Invoke(this); }
}
