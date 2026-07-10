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

    [Header("Selected Glow (optional)")]
    [Tooltip("Optional Image that pulses while this card is the currently selected item. A soft glow / halo sprite works best.")]
    public Image selectedGlow;
    [Tooltip("How fast the selected glow breathes (Hz).")]
    public float selectedGlowSpeed = 1.4f;
    [Range(0f, 1f)] public float selectedGlowMinAlpha = 0.35f;
    [Range(0f, 1f)] public float selectedGlowMaxAlpha = 0.90f;

    [Header("Hover Sparkle (optional)")]
    [Tooltip("Optional ParticleSystem played on pointer enter and stopped on pointer exit. Small sparkles around the icon feel great.")]
    public ParticleSystem hoverSparkleVFX;

    [Header("Desaturation for Locked")]
    [Tooltip("If true, dim iconImage + nameText + tierFrame while the item is Locked. Owned/Equipped tiles show their full colour.")]
    public bool desaturateWhenLocked = true;
    [Tooltip("How dark the card looks when locked (multiplier on iconImage color). 0 = pure black, 1 = normal.")]
    [Range(0.1f, 1f)] public float lockedDimAmount = 0.35f;

    private Color iconOriginalColor = Color.white;
    private Color nameOriginalColor = Color.white;
    private bool colorCacheDone;

    private void CacheOriginalColors()
    {
        if (colorCacheDone) return;
        if (iconImage != null) iconOriginalColor = iconImage.color;
        if (nameText != null) nameOriginalColor = nameText.color;
        colorCacheDone = true;
    }

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
        CacheOriginalColors();

        if (lockedOverlay != null) lockedOverlay.SetActive(state == OwnedState.Locked);
        if (ownedCheckmark != null) ownedCheckmark.SetActive(state == OwnedState.Owned);
        if (equippedRibbon != null) equippedRibbon.SetActive(state == OwnedState.Equipped);

        // Silhouette / dim look for locked items — desaturates icon + darkens
        // the card so at-a-glance the eye sees "unlocked = colourful, locked
        // = ghosted". Both branches restore from the CACHED original so we
        // never compound dimming across successive state changes.
        if (desaturateWhenLocked)
        {
            if (state == OwnedState.Locked)
            {
                if (iconImage != null)
                {
                    Color g = Grayscale(iconOriginalColor);
                    iconImage.color = g * lockedDimAmount + new Color(0, 0, 0, iconOriginalColor.a * 0.9f);
                }
                if (nameText != null)
                {
                    Color dim = nameOriginalColor;
                    dim.r *= 0.6f; dim.g *= 0.6f; dim.b *= 0.6f;
                    nameText.color = dim;
                }
            }
            else
            {
                if (iconImage != null) iconImage.color = iconOriginalColor;
                if (nameText != null) nameText.color = nameOriginalColor;
            }
        }

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
        CacheOriginalColors();
        // Deliberately DOES NOT tint nameText — if the tier frame behind it
        // is also the same colour the title would fade into the background
        // and become unreadable. Only the frame carries the rarity tint.
        if (tierFrame != null) tierFrame.color = c;
    }

    private static Color Grayscale(Color c)
    {
        float l = c.r * 0.299f + c.g * 0.587f + c.b * 0.114f;
        return new Color(l, l, l, c.a);
    }

    [Header("Hover Juice")]
    [Tooltip("Extra scale multiplier on hover. 1 = no scale, 1.05 = 5% bigger.")]
    public float hoverScale = 1.05f;
    [Tooltip("Seconds to reach hover / rest state (unscaled time so pause doesn't freeze it).")]
    public float hoverAnimSpeed = 12f;

    private Vector3 restScale = Vector3.one;
    private bool hovering;
    private bool restCached;
    private bool isSelected;
    private float selectedGlowTime;

    private void CacheRestScale()
    {
        if (restCached) return;
        restScale = transform.localScale;
        restCached = true;
    }

    private void Update()
    {
        // Scale-only hover. DELIBERATELY does NOT touch anchoredPosition —
        // shop cards are laid out by a LayoutGroup which re-runs every frame,
        // so writing to anchoredPosition from here fights the layout and
        // stacks every card on top of each other.
        Vector3 targetScl = hovering ? restScale * hoverScale : restScale;
        float k = Mathf.Clamp01(Time.unscaledDeltaTime * hoverAnimSpeed);
        transform.localScale = Vector3.Lerp(transform.localScale, targetScl, k);

        // Breathing pulse on the selected glow — sine wave between min and
        // max alpha. Only runs while selected so unselected cards stay dark.
        if (isSelected && selectedGlow != null)
        {
            selectedGlowTime += Time.unscaledDeltaTime;
            float w = 0.5f + 0.5f * Mathf.Sin(selectedGlowTime * selectedGlowSpeed * Mathf.PI * 2f);
            Color c = selectedGlow.color;
            c.a = Mathf.Lerp(selectedGlowMinAlpha, selectedGlowMaxAlpha, w);
            selectedGlow.color = c;
        }
    }

    public void SetSelected(bool selected)
    {
        isSelected = selected;
        if (selectedGlow != null)
        {
            selectedGlow.gameObject.SetActive(selected);
            selectedGlowTime = 0f;
        }
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        CacheRestScale();
        // Ignore hover while the button is uninteractable so the user's cursor
        // doesn't grow-highlight a card that won't respond to a click.
        if (buttonComponent != null && !buttonComponent.interactable) return;
        hovering = true;
        if (hoverSparkleVFX != null)
        {
            hoverSparkleVFX.gameObject.SetActive(true);
            if (!hoverSparkleVFX.isPlaying) hoverSparkleVFX.Play();
        }
        OnHoverEnter?.Invoke(this);
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        hovering = false;
        if (hoverSparkleVFX != null && hoverSparkleVFX.isPlaying)
            hoverSparkleVFX.Stop(true, ParticleSystemStopBehavior.StopEmitting);
        OnHoverExit?.Invoke(this);
    }
}
