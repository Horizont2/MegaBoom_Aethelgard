using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using TMPro;
using System.Collections;
using System.Collections.Generic;

public class ShopManager : MonoBehaviour
{
    [Header("Databases")]
    public WeaponData[] weapons;
    public ArmorData[] armors;

    [Header("Spawn Points")]
    public Transform heroPedestalPos;
    public GameObject dummyHeroPrefab;

    [Header("Scene Navigation")]
    public string campSceneName = "CampScene";

    [Header("UI Canvas Groups (For AAA Fades)")]
    public CanvasGroup arsenalGridGroup;
    public CanvasGroup arsenalContentGroup;
    public CanvasGroup arsenalDescriptionGroup;

    [Header("Stats Panel Dynamic Positioning")]
    public RectTransform statsPanelRect;
    public CanvasGroup statsPanelGroup;
    public Vector2 statsPosArsenal = new Vector2(1500, 0);

    [Header("Arsenal Dynamic List")]
    public Transform itemListContent;
    public GameObject itemButtonPrefab;
    public Button backToGridButton;
    [Tooltip("Optional. If wired, will be reset to verticalNormalizedPosition=1 (top) on every category switch.")]
    public ScrollRect itemListScrollRect;

    // True while the player is browsing a specific category (weapon or
    // armor) instead of looking at the top-level category grid. Read
    // by GlobalHUD's Esc handler so Esc backs out of a category before
    // it ever toggles the pause menu.
    public bool IsInsideCategory { get; private set; }

    [Header("Arsenal Category Buttons (WEAPONS)")]
    public Button btnCategorySwords;
    public Button btnCategoryAxes;
    public Button btnCategoryBows;

    [Header("Arsenal Category Buttons (ARMOR)")]
    public Button btnCategoryHelmets;
    public Button btnCategoryArmor;
    public Button btnCategoryGloves;
    public Button btnCategoryBelts;
    public Button btnCategoryLegs;
    public Button btnCategoryFeet;

    [Header("UI Labels & Theme Colors")]
    public TextMeshProUGUI stat1Label;
    public TextMeshProUGUI stat2Label;
    public TextMeshProUGUI stat3Label;
    public TextMeshProUGUI powerLabel;

    public Color wepStat1Color = new Color(1f, 0.5f, 0f);
    public Color wepStat2Color = new Color(1f, 0.9f, 0.1f);
    public Color wepStat3Color = new Color(0.1f, 1f, 0.8f);
    public Color powerColor = new Color(0.6f, 0.8f, 0.2f);

    private Color textNormalColor;
    private Color textErrorColor;
    private Color textSuccessColor;

    [Header("UI Fills (Images)")]
    public Image stat1Fill;
    public Image stat2Fill;
    public Image stat3Fill;
    public Image powerFill;

    [Header("UI Main Elements")]
    public TextMeshProUGUI itemNameText;
    public TextMeshProUGUI itemDescriptionText;
    public Image descriptionItemIcon;
    public TextMeshProUGUI priceText;
    public TextMeshProUGUI diamondBalanceText;
    public Button buyButton;
    public Button upgradeButton;
    public TextMeshProUGUI upgradePriceText;
    public Button backToCampButton;
    [Tooltip("Optional TMP shown under/next to the Buy button when the player can't afford the current item. Reads e.g. \"Need +45 diamonds\". Hidden otherwise.")]
    public TextMeshProUGUI affordabilityHintText;

    [Header("UI Stats Values")]
    public TextMeshProUGUI stat1PercentText;
    public TextMeshProUGUI stat2PercentText;
    public TextMeshProUGUI stat3PercentText;
    public TextMeshProUGUI powerPercentText;

    [Header("Delta Preview (Hover)")]
    [Tooltip("Optional. Small text next to stat1 that reads e.g. \"+15\" or \"-3\" when the player hovers over another item vs. the currently-equipped one. Colour is set automatically.")]
    public TextMeshProUGUI stat1DeltaText;
    public TextMeshProUGUI stat2DeltaText;
    public TextMeshProUGUI stat3DeltaText;
    public TextMeshProUGUI powerDeltaText;
    public Color deltaPositiveColor = new Color(0.60f, 0.85f, 0.55f);
    public Color deltaNegativeColor = new Color(0.90f, 0.55f, 0.55f);
    public Color deltaNeutralColor = new Color(0.75f, 0.72f, 0.65f);

    [Header("Rotation Settings")]
    public float rotationSpeed = 500f;

    [Header("Filter / Search (optional)")]
    [Tooltip("Optional Toggle that hides Locked items when checked. Wired to rebuild the currently-open category.")]
    public UnityEngine.UI.Toggle ownedOnlyToggle;
    [Tooltip("Optional TMP input field. Whatever the player types is used as a case-insensitive substring filter on item names.")]
    public TMP_InputField searchInput;

    [Header("Purchase Celebration (optional)")]
    [Tooltip("Optional ParticleSystem to Play() when the player buys or upgrades an item. Confetti / shockwave / sparkles all work.")]
    public ParticleSystem purchaseVFX;
    [Tooltip("Optional Image (full-screen faint overlay) that flashes on purchase. Colour is tweenable.")]
    public Image purchaseFlashOverlay;
    [Tooltip("Peak alpha of the full-screen flash overlay (0..1).")]
    [Range(0f, 1f)] public float purchaseFlashAlpha = 0.35f;
    [Tooltip("How long the flash overlay takes to fade back to 0.")]
    public float purchaseFlashDuration = 0.45f;
    [Tooltip("Scale multiplier used by the character-model \"got it!\" pulse.")]
    public float purchasePulseScale = 1.08f;

    // Per-transform pop tween registry so rapid upgrades don't stack overlapping
    // scale coroutines on the same text (they raced and could strand it enlarged).
    private readonly System.Collections.Generic.Dictionary<Transform, Coroutine> _popTweens
        = new System.Collections.Generic.Dictionary<Transform, Coroutine>();

    private void PopTextSafe(TextMeshProUGUI text)
    {
        if (text == null) return;
        Transform tr = text.transform;
        if (_popTweens.TryGetValue(tr, out var running) && running != null) StopCoroutine(running);
        _popTweens[tr] = StartCoroutine(PopText(text));
    }

    private GameObject currentHeroModel;
    private GameObject currentWeaponModel;
    private ModularArmorManager dummyArmorManager;

    private bool isViewingWeapon = false;

    private WeaponData selectedWeaponData;
    private ArmorData selectedArmorData;

    private const string DIAMONDS_KEY = "PlayerDiamonds";

    // ResourceManager is the runtime source of truth for diamond
    // balance — PlayerController.GainDiamond writes there first and
    // never touches the PlayerPrefs key while ResourceManager exists.
    // Reading PlayerPrefs directly in the shop meant the counter was
    // always stale (showing the last persisted value, not the live
    // balance). These helpers route through ResourceManager when it
    // exists and fall back to PlayerPrefs only for safety.
    private int ReadDiamonds()
    {
        if (ResourceManager.Instance != null) return ResourceManager.Instance.diamonds;
        return PlayerPrefs.GetInt(DIAMONDS_KEY, 0);
    }

    private void WriteDiamonds(int newAmount)
    {
        if (ResourceManager.Instance != null)
        {
            ResourceManager.Instance.diamonds = newAmount;
            ResourceManager.Instance.SaveStash();
            ResourceManager.Instance.UpdateUI();
        }
        PlayerPrefs.SetInt(DIAMONDS_KEY, newAmount);
        PlayerPrefs.Save();
    }

    private Dictionary<CanvasGroup, Coroutine> activeFades = new Dictionary<CanvasGroup, Coroutine>();

    // Tracks the last category opened so search / owned-only filter refresh
    // can rebuild the SAME list. Only one of these is populated at a time.
    private ItemCategory? currentWeaponCat;
    private ArmorCategory? currentArmorCat;
    private ShopItemButton currentSelectedButton;

    private void Awake()
    {
        ColorUtility.TryParseHtmlString("#EAE5D9", out textNormalColor);
        ColorUtility.TryParseHtmlString("#8B2E2E", out textErrorColor);
        ColorUtility.TryParseHtmlString("#758B2E", out textSuccessColor);
    }

    private void Start()
    {
        Cursor.visible = true;
        Cursor.lockState = CursorLockMode.None;

        if (TutorialHints.Instance != null)
            TutorialHints.Instance.ShowIfNew("Shop",
                "Spend diamonds to unlock and upgrade weapons & armor. Higher tiers boost your Power Score, which gates harder regions.", 7f);

        if (!PlayerPrefs.HasKey(DIAMONDS_KEY)) PlayerPrefs.SetInt(DIAMONDS_KEY, 0);
        if (!PlayerPrefs.HasKey("SelectedWeaponID"))
        {
            PlayerPrefs.SetInt("SelectedWeaponID", 0);
            PlayerPrefs.SetInt("WeaponUnlocked_0", 1);
            PlayerPrefs.Save();
        }

        AssignButtonListeners();
        SpawnDummyHero();

        if (statsPanelRect != null)
        {
            statsPanelRect.anchoredPosition = statsPosArsenal;
        }

        SetGroupAlphaInstant(arsenalContentGroup, 0f);
        SetGroupAlphaInstant(arsenalDescriptionGroup, 0f);
        SetGroupAlphaInstant(statsPanelGroup, 0f);
        SetGroupAlphaInstant(arsenalGridGroup, 1f);

        AutoResolveScrollRect();
        EnsureVerticalScrollbar();
    }

    private void OnEnable()
    {
        LocalizationManager.OnLanguageChanged += HandleLanguageChanged;
    }

    private void OnDisable()
    {
        LocalizationManager.OnLanguageChanged -= HandleLanguageChanged;
    }

    private void HandleLanguageChanged()
    {
        // Runtime-written labels (stat headers, price, MAX / EQUIP badges)
        // don't go through AutoLocalize — refresh them when the player
        // flips locale mid-browse.
        if (IsInsideCategory) SetupDynamicUI(isViewingWeapon);
        UpdateUI(false);
    }

    // ScrollRect wire-up is Inspector-optional. If it's missing, walk up
    // from itemListContent so ScrollListToTop actually has a rect to
    // reset. Without this the "reset scroll on category switch" fix was
    // silently doing nothing whenever the field was blank.
    private void AutoResolveScrollRect()
    {
        if (itemListScrollRect != null || itemListContent == null) return;
        itemListScrollRect = itemListContent.GetComponentInParent<ScrollRect>();
    }

    // Add a visible vertical scrollbar if the item list ScrollRect
    // doesn't have one wired. Runs once at Start — designers can still
    // author their own scrollbar and this becomes a no-op.
    private void EnsureVerticalScrollbar()
    {
        if (itemListScrollRect == null) return;
        if (itemListScrollRect.verticalScrollbar != null) return;

        RectTransform parent = itemListScrollRect.GetComponent<RectTransform>();
        if (parent == null) return;

        GameObject barGo = new GameObject("Scrollbar Vertical", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        barGo.transform.SetParent(parent, false);
        RectTransform barRect = barGo.GetComponent<RectTransform>();
        barRect.anchorMin = new Vector2(1, 0);
        barRect.anchorMax = new Vector2(1, 1);
        barRect.pivot = new Vector2(1, 1);
        barRect.sizeDelta = new Vector2(12f, 0f);
        barRect.anchoredPosition = Vector2.zero;
        Image barImg = barGo.GetComponent<Image>();
        barImg.color = new Color(0f, 0f, 0f, 0.35f);
        barImg.raycastTarget = true;

        GameObject slidingArea = new GameObject("Sliding Area", typeof(RectTransform));
        slidingArea.transform.SetParent(barGo.transform, false);
        RectTransform saRect = slidingArea.GetComponent<RectTransform>();
        saRect.anchorMin = Vector2.zero;
        saRect.anchorMax = Vector2.one;
        saRect.pivot = new Vector2(0.5f, 0.5f);
        saRect.sizeDelta = new Vector2(-4f, -4f);
        saRect.anchoredPosition = Vector2.zero;

        GameObject handle = new GameObject("Handle", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        handle.transform.SetParent(slidingArea.transform, false);
        RectTransform hRect = handle.GetComponent<RectTransform>();
        hRect.anchorMin = Vector2.zero;
        hRect.anchorMax = Vector2.one;
        hRect.pivot = new Vector2(0.5f, 0.5f);
        hRect.sizeDelta = Vector2.zero;
        hRect.anchoredPosition = Vector2.zero;
        Image handleImg = handle.GetComponent<Image>();
        handleImg.color = new Color(0.9f, 0.85f, 0.7f, 0.85f);

        Scrollbar scrollbar = barGo.AddComponent<Scrollbar>();
        scrollbar.direction = Scrollbar.Direction.BottomToTop;
        scrollbar.handleRect = hRect;
        scrollbar.targetGraphic = handleImg;

        itemListScrollRect.verticalScrollbar = scrollbar;
        // Deliberately NOT AutoHideAndExpandViewport — that mode makes the
        // ScrollRect resize the viewport at runtime based on scrollbar
        // visibility, which overrode the inspector-authored anchors and
        // caused the viewport to drop past the "Back" button. AutoHide only
        // toggles the scrollbar visibility and leaves the viewport RectTransform
        // exactly where the designer placed it.
        itemListScrollRect.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHide;
        itemListScrollRect.vertical = true;
    }

    private void AssignButtonListeners()
    {
        if (buyButton) buyButton.onClick.AddListener(() => { PlayButtonAnim(buyButton.transform); OnBuyOrSelectPressed(); });
        if (upgradeButton) upgradeButton.onClick.AddListener(() => { PlayButtonAnim(upgradeButton.transform); OnUpgradePressed(); });
        if (backToCampButton) backToCampButton.onClick.AddListener(() => { PlayButtonAnim(backToCampButton.transform); GoToCampScene(); });
        if (backToGridButton) backToGridButton.onClick.AddListener(() => { PlayButtonAnim(backToGridButton.transform); ReturnToArsenalGrid(); });

        if (btnCategorySwords) btnCategorySwords.onClick.AddListener(() => OpenWeaponCategory(ItemCategory.Sword));
        if (btnCategoryAxes) btnCategoryAxes.onClick.AddListener(() => OpenWeaponCategory(ItemCategory.Axe));
        if (btnCategoryBows) btnCategoryBows.onClick.AddListener(() => OpenWeaponCategory(ItemCategory.Bow));

        if (btnCategoryHelmets) btnCategoryHelmets.onClick.AddListener(() => OpenArmorCategory(ArmorCategory.Head));
        if (btnCategoryArmor) btnCategoryArmor.onClick.AddListener(() => OpenArmorCategory(ArmorCategory.Chest));
        if (btnCategoryGloves) btnCategoryGloves.onClick.AddListener(() => OpenArmorCategory(ArmorCategory.Arms));
        if (btnCategoryBelts) btnCategoryBelts.onClick.AddListener(() => OpenArmorCategory(ArmorCategory.Belt));
        if (btnCategoryLegs) btnCategoryLegs.onClick.AddListener(() => OpenArmorCategory(ArmorCategory.Legs));
        if (btnCategoryFeet) btnCategoryFeet.onClick.AddListener(() => OpenArmorCategory(ArmorCategory.Feet));

        // Filter / search: rebuild the currently open category when the
        // player toggles ownedOnly or types into the search field.
        if (ownedOnlyToggle != null) ownedOnlyToggle.onValueChanged.AddListener(_ => RefreshCurrentCategory());
        if (searchInput != null) searchInput.onValueChanged.AddListener(_ => RefreshCurrentCategory());
    }

    private void RefreshCurrentCategory()
    {
        if (currentWeaponCat.HasValue) OpenWeaponCategory(currentWeaponCat.Value);
        else if (currentArmorCat.HasValue) OpenArmorCategory(currentArmorCat.Value);
    }

    private bool PassesFilter(string itemName, bool isOwned)
    {
        if (ownedOnlyToggle != null && ownedOnlyToggle.isOn && !isOwned) return false;
        if (searchInput != null)
        {
            string s = searchInput.text;
            if (!string.IsNullOrWhiteSpace(s) && itemName.IndexOf(s, System.StringComparison.OrdinalIgnoreCase) < 0)
                return false;
        }
        return true;
    }

    private int lastDisplayedDiamonds = -1;

    private void Update()
    {
        // Defensive: the shop scene requires a free cursor. Skip when a
        // modal owns the cursor state (confirm dialog, settings, etc.),
        // otherwise the shop fights the modal every frame and the modal
        // never gets to hide the cursor.
        bool modalActive = ConfirmDialog.IsOpen;
        if (!modalActive && (!Cursor.visible || Cursor.lockState != CursorLockMode.None))
        {
            Cursor.visible = true;
            Cursor.lockState = CursorLockMode.None;
        }

        if (currentHeroModel != null && Input.GetMouseButton(0))
        {
            float rotX = Input.GetAxis("Mouse X") * rotationSpeed * Time.deltaTime;
            currentHeroModel.transform.Rotate(Vector3.up, -rotX, Space.World);
        }

        // Keep the displayed diamond balance in sync with the live
        // ResourceManager value. UpdateUI() only refreshes the text on
        // a category change or after a purchase — but the user can
        // earn diamonds in another scene before opening the shop, and
        // the cached on-load number was stale. Diff against the last
        // rendered value to avoid burning a GC string every frame.
        if (diamondBalanceText != null)
        {
            int live = ReadDiamonds();
            if (live != lastDisplayedDiamonds)
            {
                // Kick off the animated count-up so the number rolls between
                // the previous value and the new one instead of snapping.
                if (lastDisplayedDiamonds >= 0)
                    StartCoroutine(DiamondCounterRoutine(lastDisplayedDiamonds, live));
                else
                    diamondBalanceText.text = LocalizationManager.Tr("Diamonds:") + " " + live.ToString("N0");
                lastDisplayedDiamonds = live;
            }
        }
    }

    // Animated ticker between two diamond values with a punch pulse at the end.
    // Increases and decreases both look punchy: the text bounces from 1.0 to
    // 1.15 scale over ~0.15s and back — same "you got something!" cadence as
    // a level-up popup.
    private Coroutine diamondCounterCoroutine;
    private IEnumerator DiamondCounterRoutine(int from, int to)
    {
        if (diamondBalanceText == null) yield break;
        if (diamondCounterCoroutine != null) StopCoroutine(diamondCounterCoroutine);

        float dur = 0.35f;
        float t = 0f;
        while (t < 1f)
        {
            t += Time.unscaledDeltaTime / dur;
            float e = 1f - (1f - Mathf.Clamp01(t)) * (1f - Mathf.Clamp01(t));
            int shown = Mathf.RoundToInt(Mathf.Lerp(from, to, e));
            diamondBalanceText.text = LocalizationManager.Tr("Diamonds:") + " " + shown.ToString("N0");
            yield return null;
        }
        diamondBalanceText.text = LocalizationManager.Tr("Diamonds:") + " " + to.ToString("N0");

        // Quick scale pulse punch after the roll.
        Transform tr = diamondBalanceText.transform;
        Vector3 baseS = tr.localScale;
        float pulseDur = 0.18f;
        float pt = 0f;
        while (pt < 1f)
        {
            pt += Time.unscaledDeltaTime / pulseDur;
            float k = Mathf.Sin(Mathf.Clamp01(pt) * Mathf.PI);
            tr.localScale = baseS * (1f + 0.15f * k);
            yield return null;
        }
        tr.localScale = baseS;
    }

    private void SetGroupAlphaInstant(CanvasGroup group, float targetAlpha)
    {
        if (group == null) return;
        group.gameObject.SetActive(true);
        group.alpha = targetAlpha;
        group.interactable = (targetAlpha > 0.5f);
        group.blocksRaycasts = (targetAlpha > 0.5f);
    }

    private void FadeGroup(CanvasGroup group, float targetAlpha)
    {
        if (group == null) return;

        if (activeFades.ContainsKey(group) && activeFades[group] != null)
        {
            StopCoroutine(activeFades[group]);
        }

        activeFades[group] = StartCoroutine(FadeRoutine(group, targetAlpha));
    }

    private IEnumerator FadeRoutine(CanvasGroup group, float targetAlpha)
    {
        group.gameObject.SetActive(true);
        group.blocksRaycasts = (targetAlpha > 0.5f);
        group.interactable = (targetAlpha > 0.5f);

        float startAlpha = group.alpha;
        float t = 0;

        while (t < 1)
        {
            t += Time.unscaledDeltaTime * 10f;
            group.alpha = Mathf.Lerp(startAlpha, targetAlpha, t);
            yield return null;
        }

        group.alpha = targetAlpha;
    }

    private void PlayButtonAnim(Transform btnTransform)
    {
        StartCoroutine(ButtonScaleRoutine(btnTransform));
    }

    private IEnumerator ButtonScaleRoutine(Transform btnTransform)
    {
        Vector3 originalScale = Vector3.one;
        btnTransform.localScale = originalScale * 0.9f;
        float t = 0;
        while (t < 1)
        {
            t += Time.unscaledDeltaTime * 12f;
            btnTransform.localScale = Vector3.Lerp(originalScale * 0.9f, originalScale, t);
            yield return null;
        }
        btnTransform.localScale = originalScale;
    }

    // Trailer/showcase: drive the shop UI so the trailer SHOWS the player
    // selecting items, their stats/prices, and the Buy/Upgrade buttons —
    // instead of just orbiting the scene. Lives here so it can reach the
    // private item list + category logic directly.
    public System.Collections.IEnumerator TrailerShowcaseRoutine()
    {
        ItemCategory[] weaponCats = { ItemCategory.Sword, ItemCategory.Axe, ItemCategory.Bow };
        foreach (var cat in weaponCats)
        {
            OpenWeaponCategory(cat);
            yield return new WaitForSecondsRealtime(0.9f);
            yield return TrailerBrowseCurrentCategory(3);
        }

        // Finish on armour so the trailer shows gear too.
        OpenArmorCategory(ArmorCategory.Chest);
        yield return new WaitForSecondsRealtime(0.9f);
        yield return TrailerBrowseCurrentCategory(3);
    }

    // Select up to `max` items in the open category, pausing on each so the
    // stats panel, price, and the live Buy/Upgrade button are all readable.
    private System.Collections.IEnumerator TrailerBrowseCurrentCategory(int max)
    {
        if (itemListContent == null) yield break;
        int shown = 0;
        for (int i = 0; i < itemListContent.childCount && shown < max; i++)
        {
            var slot = itemListContent.GetChild(i).GetComponent<ShopItemButton>();
            if (slot == null || slot.buttonComponent == null || !slot.gameObject.activeInHierarchy) continue;

            slot.buttonComponent.onClick.Invoke();   // select -> shows stats + price
            shown++;

            // Draw the eye to whichever action button is live for this item.
            if (upgradeButton != null && upgradeButton.gameObject.activeInHierarchy)
                PlayButtonAnim(upgradeButton.transform);
            else if (buyButton != null && buyButton.gameObject.activeInHierarchy)
                PlayButtonAnim(buyButton.transform);

            yield return new WaitForSecondsRealtime(1.5f);
        }
        yield return new WaitForSecondsRealtime(0.3f);
    }

    public void OpenWeaponCategory(ItemCategory cat)
    {
        if (AudioManager.Instance != null) AudioManager.Instance.PlayUI(AudioID.UI_Click);

        isViewingWeapon = true;
        IsInsideCategory = true;
        currentWeaponCat = cat;
        currentArmorCat = null;
        currentSelectedButton = null;
        SetupDynamicUI(true);

        FadeGroup(arsenalGridGroup, 0f);

        ClearItemList();

        WeaponData firstWep = null;
        int myDiamonds = ReadDiamonds();
        int equippedWepID = PlayerPrefs.GetInt("SelectedWeaponID", 0);
        foreach (var w in weapons)
        {
            if (w.category != cat) continue;

            bool owned = PlayerPrefs.GetInt("WeaponUnlocked_" + w.weaponID, w.price == 0 ? 1 : 0) == 1;
            if (!PassesFilter(w.weaponName, owned)) continue;

            if (firstWep == null) firstWep = w;
            WeaponData captured = w;
            ShopItemButton btn = CreateListButton(w.weaponName, w.icon, () => SelectWeapon(captured));
            if (btn != null)
            {
                btn.boundWeapon = w;
                int lvl = PlayerPrefs.GetInt("WeaponLevel_" + w.weaponID, 0);
                bool equipped = owned && w.weaponID == equippedWepID;
                var state = equipped ? ShopItemButton.OwnedState.Equipped
                          : owned ? ShopItemButton.OwnedState.Owned
                                  : ShopItemButton.OwnedState.Locked;
                btn.SetState(state, lvl, w.maxUpgradeLevel, w.price, myDiamonds);
                btn.SetTierColor(GetWeaponTierColor(w));
                btn.OnHoverEnter += HandleItemHoverEnter;
                btn.OnHoverExit += HandleItemHoverExit;
            }
        }

        if (firstWep != null) SelectWeapon(firstWep);
        else SetEmptyUI();

        ShowContentPanels();
        ScrollListToTop();
    }

    private void SelectWeapon(WeaponData w)
    {
        if (AudioManager.Instance != null) AudioManager.Instance.PlayUI(AudioID.UI_Hover);
        selectedWeaponData = w;
        EquipWeaponToHero(w);
        UpdateUI(true);
        HighlightSelectedButtonForWeapon(w);
    }

    public void OpenArmorCategory(ArmorCategory cat)
    {
        if (AudioManager.Instance != null) AudioManager.Instance.PlayUI(AudioID.UI_Click);

        isViewingWeapon = false;
        IsInsideCategory = true;
        currentArmorCat = cat;
        currentWeaponCat = null;
        currentSelectedButton = null;
        SetupDynamicUI(false);

        FadeGroup(arsenalGridGroup, 0f);

        ClearItemList();

        ArmorData firstArm = null;
        int myDiamondsArm = ReadDiamonds();
        int equippedIndex = PlayerPrefs.GetInt($"EquippedArmor_{cat}", 0);
        if (armors != null)
        {
            foreach (var a in armors)
            {
                if (a == null || a.category != cat) continue;

                bool owned = PlayerPrefs.GetInt("ArmorUnlocked_" + a.armorID, a.price == 0 ? 1 : 0) == 1;
                if (!PassesFilter(a.armorName, owned)) continue;

                if (firstArm == null) firstArm = a;
                ArmorData captured = a;
                ShopItemButton btn = CreateListButton(a.armorName, a.icon, () => SelectArmor(captured));
                if (btn != null)
                {
                    btn.boundArmor = a;
                    int lvl = PlayerPrefs.GetInt("ArmorLevel_" + a.armorID, 0);
                    bool equipped = owned && a.prefabIndex == equippedIndex;
                    var state = equipped ? ShopItemButton.OwnedState.Equipped
                              : owned ? ShopItemButton.OwnedState.Owned
                                      : ShopItemButton.OwnedState.Locked;
                    btn.SetState(state, lvl, a.maxUpgradeLevel, a.price, myDiamondsArm);
                    btn.SetTierColor(GetArmorTierColor(a));
                    btn.OnHoverEnter += HandleItemHoverEnter;
                    btn.OnHoverExit += HandleItemHoverExit;
                }
            }
        }

        if (firstArm != null) SelectArmor(firstArm);
        else SetEmptyUI();

        ShowContentPanels();
        ScrollListToTop();
    }

    // Properly destroys old buttons immediately (DestroyImmediate isn't
    // safe in play mode, but explicitly removing the parent reference
    // and calling Destroy keeps the children out of the layout for the
    // current frame). Without an immediate clear, opening a new
    // category sometimes layered the new buttons on top of stale ones.
    private void ClearItemList()
    {
        if (itemListContent == null) return;
        for (int i = itemListContent.childCount - 1; i >= 0; i--)
        {
            Transform t = itemListContent.GetChild(i);
            t.SetParent(null, false);
            Destroy(t.gameObject);
        }
    }

    // Resets the item list's scroll position to the top whenever the
    // player switches categories. Previously the list inherited the
    // previous category's scroll offset — open a category, scroll to
    // the bottom, switch to another category, and you'd see the new
    // list scrolled below its first row. The one-frame delay is
    // required because Layout Groups defer their rebuild until the
    // next canvas update; setting verticalNormalizedPosition against
    // still-stale content sizes leaves the ScrollRect halfway.
    private void ScrollListToTop()
    {
        AutoResolveScrollRect();
        if (itemListScrollRect == null) return;

        StopCoroutine(nameof(ScrollToTopRoutine));
        StartCoroutine(ScrollToTopRoutine());
    }

    private IEnumerator ScrollToTopRoutine()
    {
        itemListScrollRect.velocity = Vector2.zero;
        itemListScrollRect.verticalNormalizedPosition = 1f;

        RectTransform contentRect = itemListScrollRect.content;
        if (contentRect != null) LayoutRebuilder.ForceRebuildLayoutImmediate(contentRect);
        Canvas.ForceUpdateCanvases();
        itemListScrollRect.verticalNormalizedPosition = 1f;

        // Layout Groups on the content spawn one more rebuild next
        // frame — reset again after that lands.
        yield return null;
        itemListScrollRect.velocity = Vector2.zero;
        itemListScrollRect.verticalNormalizedPosition = 1f;
    }

    private void SelectArmor(ArmorData a)
    {
        if (AudioManager.Instance != null) AudioManager.Instance.PlayUI(AudioID.UI_Hover);
        selectedArmorData = a;
        if (dummyArmorManager != null) dummyArmorManager.EquipArmorVisuals((ArmorSlot)a.category, a.prefabIndex);
        UpdateUI(true);
        HighlightSelectedButtonForArmor(a);
    }

    // Walk the current item list and turn on the SelectedGlow for the button
    // whose data matches the newly-selected item. Every other button gets
    // its glow turned off. Cheap linear scan — the list is short.
    private void HighlightSelectedButtonForWeapon(WeaponData w)
    {
        if (itemListContent == null) return;
        currentSelectedButton = null;
        for (int i = 0; i < itemListContent.childCount; i++)
        {
            var btn = itemListContent.GetChild(i).GetComponent<ShopItemButton>();
            if (btn == null) continue;
            bool match = btn.boundWeapon == w;
            btn.SetSelected(match);
            if (match) currentSelectedButton = btn;
        }
    }

    private void HighlightSelectedButtonForArmor(ArmorData a)
    {
        if (itemListContent == null) return;
        currentSelectedButton = null;
        for (int i = 0; i < itemListContent.childCount; i++)
        {
            var btn = itemListContent.GetChild(i).GetComponent<ShopItemButton>();
            if (btn == null) continue;
            bool match = btn.boundArmor == a;
            btn.SetSelected(match);
            if (match) currentSelectedButton = btn;
        }
    }

    private ShopItemButton CreateListButton(string name, Sprite icon, UnityEngine.Events.UnityAction action)
    {
        GameObject btnObj = Instantiate(itemButtonPrefab, itemListContent);
        btnObj.SetActive(true);
        ShopItemButton itemSlot = btnObj.GetComponent<ShopItemButton>();
        if (itemSlot != null)
        {
            if (itemSlot.nameText != null) itemSlot.nameText.text = LocalizationManager.Tr(name);
            if (itemSlot.iconImage != null && icon != null) itemSlot.iconImage.sprite = icon;
            if (itemSlot.buttonComponent != null)
            {
                // Wipe any stale serialised onClick listeners from the
                // prefab. If the prefab was wired with a now-dead
                // reference at design time, that listener could swallow
                // the click silently. Add ONLY the runtime delegate.
                itemSlot.buttonComponent.onClick.RemoveAllListeners();
                itemSlot.buttonComponent.interactable = true;
                ShopItemButton itemSlotCaptured = itemSlot;
                UnityEngine.Events.UnityAction actionCaptured = action;
                itemSlot.buttonComponent.onClick.AddListener(() =>
                {
                    if (AudioManager.Instance != null) AudioManager.Instance.PlayUI(AudioID.UI_Hover);
                    PlayButtonAnim(itemSlotCaptured.transform);
                    actionCaptured?.Invoke();
                });
            }
        }
        return itemSlot;
    }

    // Rarity/tier tint driven by the item's base Power. Muted palette
    // (desaturated + light-mid brightness) so the tint is still readable
    // as text on dark card backgrounds without screaming for attention.
    // Mapping: <30 common, <60 uncommon, <100 rare, <160 epic, else legendary.
    private static readonly Color TIER_COMMON     = new Color(0.88f, 0.85f, 0.78f); // parchment
    private static readonly Color TIER_UNCOMMON   = new Color(0.65f, 0.82f, 0.60f); // moss
    private static readonly Color TIER_RARE       = new Color(0.62f, 0.78f, 0.95f); // steel-blue
    private static readonly Color TIER_EPIC       = new Color(0.82f, 0.68f, 0.95f); // dusty violet
    private static readonly Color TIER_LEGENDARY  = new Color(0.95f, 0.82f, 0.50f); // muted honey

    private Color GetWeaponTierColor(WeaponData w)
    {
        int p = w.basePower + w.powerPerLevel * w.maxUpgradeLevel;
        return PickTier(p);
    }

    private Color GetArmorTierColor(ArmorData a)
    {
        int p = a.basePower + a.powerPerLevel * a.maxUpgradeLevel;
        return PickTier(p);
    }

    private Color PickTier(int totalPower)
    {
        if (totalPower < 30) return TIER_COMMON;
        if (totalPower < 60) return TIER_UNCOMMON;
        if (totalPower < 100) return TIER_RARE;
        if (totalPower < 160) return TIER_EPIC;
        return TIER_LEGENDARY;
    }

    // Hover previews compare the hovered item's stats against the currently
    // EQUIPPED item of the same category and display deltas next to each
    // stat (e.g. DMG "+15", ATK SPEED "-0.2"). When the pointer leaves the
    // card, we clear the delta so the panel goes back to plain stats.
    private void HandleItemHoverEnter(ShopItemButton btn)
    {
        if (btn == null) return;

        if (btn.boundWeapon != null)
        {
            WeaponData hovered = btn.boundWeapon;
            WeaponData equipped = FindEquippedWeaponInCategory(hovered.category);
            int hLvl = PlayerPrefs.GetInt("WeaponLevel_" + hovered.weaponID, 0);
            int eLvl = equipped != null ? PlayerPrefs.GetInt("WeaponLevel_" + equipped.weaponID, 0) : 0;

            float dHDamage = (hovered.damageBonus + hLvl * hovered.damagePerLevel)
                           - (equipped != null ? equipped.damageBonus + eLvl * equipped.damagePerLevel : 0f);
            float dHSpeed = (hovered.attackSpeed + hLvl * hovered.attackSpeedPerLevel)
                          - (equipped != null ? equipped.attackSpeed + eLvl * equipped.attackSpeedPerLevel : 0f);
            float dHCrit = (hovered.critChance + hLvl * hovered.critChancePerLevel)
                         - (equipped != null ? equipped.critChance + eLvl * equipped.critChancePerLevel : 0f);
            int dHPower = (hovered.basePower + hLvl * hovered.powerPerLevel)
                        - (equipped != null ? equipped.basePower + eLvl * equipped.powerPerLevel : 0);

            SetDelta(stat1DeltaText, dHDamage, "F0", "");
            SetDelta(stat2DeltaText, dHSpeed, "F2", "");
            SetDelta(stat3DeltaText, dHCrit * 100f, "F1", "%");
            SetDelta(powerDeltaText, dHPower, "F0", "");
        }
        else if (btn.boundArmor != null)
        {
            ArmorData hovered = btn.boundArmor;
            ArmorData equipped = FindEquippedArmorInCategory(hovered.category);
            int hLvl = PlayerPrefs.GetInt("ArmorLevel_" + hovered.armorID, 0);
            int eLvl = equipped != null ? PlayerPrefs.GetInt("ArmorLevel_" + equipped.armorID, 0) : 0;

            float dHHealth = (hovered.baseHealthBonus + hLvl * hovered.healthPerLevel)
                           - (equipped != null ? equipped.baseHealthBonus + eLvl * equipped.healthPerLevel : 0f);
            float dHReduction = (hovered.baseDamageReduction + hLvl * hovered.reductionPerLevel)
                              - (equipped != null ? equipped.baseDamageReduction + eLvl * equipped.reductionPerLevel : 0f);
            int dHPower = (hovered.basePower + hLvl * hovered.powerPerLevel)
                        - (equipped != null ? equipped.basePower + eLvl * equipped.powerPerLevel : 0);

            SetDelta(stat1DeltaText, dHHealth, "F0", " HP");
            SetDelta(stat2DeltaText, dHReduction * 100f, "F1", "%");
            SetDelta(stat3DeltaText, 0f, "F0", "");
            SetDelta(powerDeltaText, dHPower, "F0", "");
        }
    }

    private void HandleItemHoverExit(ShopItemButton btn)
    {
        ClearDeltas();
    }

    private void SetDelta(TextMeshProUGUI txt, float delta, string fmt, string suffix)
    {
        if (txt == null) return;
        if (Mathf.Abs(delta) < 0.001f)
        {
            txt.text = "";
            return;
        }
        string sign = delta > 0 ? "+" : "";
        txt.text = sign + delta.ToString(fmt) + suffix;
        txt.color = delta > 0 ? deltaPositiveColor : delta < 0 ? deltaNegativeColor : deltaNeutralColor;
    }

    private void ClearDeltas()
    {
        if (stat1DeltaText != null) stat1DeltaText.text = "";
        if (stat2DeltaText != null) stat2DeltaText.text = "";
        if (stat3DeltaText != null) stat3DeltaText.text = "";
        if (powerDeltaText != null) powerDeltaText.text = "";
    }

    private WeaponData FindEquippedWeaponInCategory(ItemCategory cat)
    {
        int equippedID = PlayerPrefs.GetInt("SelectedWeaponID", 0);
        if (weapons == null) return null;
        foreach (var w in weapons)
            if (w != null && w.category == cat && w.weaponID == equippedID) return w;
        return null;
    }

    private ArmorData FindEquippedArmorInCategory(ArmorCategory cat)
    {
        int equippedIndex = PlayerPrefs.GetInt($"EquippedArmor_{cat}", 0);
        if (armors == null) return null;
        foreach (var a in armors)
            if (a != null && a.category == cat && a.prefabIndex == equippedIndex) return a;
        return null;
    }

    private void ShowContentPanels()
    {
        FadeGroup(arsenalContentGroup, 1f);
        FadeGroup(arsenalDescriptionGroup, 1f);
        FadeGroup(statsPanelGroup, 1f);
    }

    private void SetEmptyUI()
    {
        if (itemNameText) itemNameText.text = LocalizationManager.Tr("Empty Category");
        if (itemDescriptionText) itemDescriptionText.text = LocalizationManager.Tr("There are no items in this category yet.");
        UpdateUI(false);
    }

    public void ReturnToArsenalGrid()
    {
        if (AudioManager.Instance != null) AudioManager.Instance.PlayUI(AudioID.UI_Click);

        isViewingWeapon = false;
        IsInsideCategory = false;
        currentWeaponCat = null;
        currentArmorCat = null;
        currentSelectedButton = null;

        // Reset the shop camera back to the default view. When a
        // category button is clicked, ShopCameraController.MoveToCategory
        // is wired via the inspector OnClick event and slides the camera
        // to the matching body part. Without this explicit MoveToDefault
        // call the camera stayed zoomed on the last viewed part after
        // Esc / Back-to-Categories.
        if (ShopCameraController.Instance != null) ShopCameraController.Instance.MoveToDefault();

        int savedWepID = PlayerPrefs.GetInt("SelectedWeaponID", 0);
        foreach (var w in weapons)
        {
            if (w.weaponID == savedWepID) EquipWeaponToHero(w);
        }

        if (dummyArmorManager != null) dummyArmorManager.LoadEquippedArmor();

        FadeGroup(arsenalContentGroup, 0f);
        FadeGroup(arsenalDescriptionGroup, 0f);
        FadeGroup(statsPanelGroup, 0f);
        FadeGroup(arsenalGridGroup, 1f);
    }

    private void SetupDynamicUI(bool isWeapon)
    {
        if (isWeapon)
        {
            if (stat1Label) stat1Label.text = LocalizationManager.Tr("DAMAGE");
            if (stat2Label) stat2Label.text = LocalizationManager.Tr("ATK SPEED");
            if (stat3Label) { stat3Label.gameObject.SetActive(true); stat3Label.text = LocalizationManager.Tr("CRIT"); }
            if (stat3Fill) stat3Fill.gameObject.SetActive(true);
            if (stat3PercentText) stat3PercentText.gameObject.SetActive(true);
        }
        else
        {
            if (stat1Label) stat1Label.text = LocalizationManager.Tr("HEALTH");
            if (stat2Label) stat2Label.text = LocalizationManager.Tr("DEFENSE");
            if (stat3Label) stat3Label.gameObject.SetActive(false);
            if (stat3Fill) stat3Fill.gameObject.SetActive(false);
            if (stat3PercentText) stat3PercentText.gameObject.SetActive(false);
        }
        if (powerLabel) powerLabel.text = LocalizationManager.Tr("POWER");
    }

    public void OnBuyOrSelectPressed()
    {
        int myDiamonds = ReadDiamonds();
        bool didPurchase = false;

        if (isViewingWeapon && selectedWeaponData != null)
        {
            int id = selectedWeaponData.weaponID;
            int price = selectedWeaponData.price;
            string unlockKey = "WeaponUnlocked_" + id;
            bool isBought = PlayerPrefs.GetInt(unlockKey, price == 0 ? 1 : 0) == 1;

            if (!isBought && myDiamonds >= price)
            {
                AudioManager.Instance?.PlayUI(AudioID.UI_Purchase);
                WriteDiamonds(myDiamonds - price);
                PlayerPrefs.SetInt(unlockKey, 1);
                PlayerPrefs.SetInt("SelectedWeaponID", id);
                // Persist the weapon's family so PlayerController can pick
                // the right hit SFX (sword vs axe vs bow) without needing
                // the full WeaponData asset available at runtime.
                PlayerPrefs.SetInt("EquippedWeaponCategory", (int)selectedWeaponData.category);
                didPurchase = true;
            }
            else if (isBought)
            {
                AudioManager.Instance?.PlayUI(AudioID.UI_Click);
                PlayerPrefs.SetInt("SelectedWeaponID", id);
                PlayerPrefs.SetInt("EquippedWeaponCategory", (int)selectedWeaponData.category);
            }
            else AudioManager.Instance?.PlayUI(AudioID.UI_Error);
        }
        else if (!isViewingWeapon && selectedArmorData != null)
        {
            int id = selectedArmorData.armorID;
            int price = selectedArmorData.price;
            string unlockKey = "ArmorUnlocked_" + id;
            bool isBought = PlayerPrefs.GetInt(unlockKey, price == 0 ? 1 : 0) == 1;

            if (!isBought && myDiamonds >= price)
            {
                AudioManager.Instance?.PlayUI(AudioID.UI_Purchase);
                WriteDiamonds(myDiamonds - price);
                PlayerPrefs.SetInt(unlockKey, 1);
                dummyArmorManager?.EquipAndSaveArmor((ArmorSlot)selectedArmorData.category, selectedArmorData.prefabIndex);
                didPurchase = true;
            }
            else if (isBought)
            {
                AudioManager.Instance?.PlayUI(AudioID.UI_Click);
                dummyArmorManager?.EquipAndSaveArmor((ArmorSlot)selectedArmorData.category, selectedArmorData.prefabIndex);
            }
            else AudioManager.Instance?.PlayUI(AudioID.UI_Error);
        }

        if (didPurchase)
        {
            PlayPurchaseCelebration();
            // Guide step 'visit the shop and buy something' keys off this.
            if (PlayerPrefs.GetInt("ShopFirstPurchase", 0) == 0)
            {
                PlayerPrefs.SetInt("ShopFirstPurchase", 1);
                PlayerPrefs.Save();
            }
            // Running total of diamonds spent — pushed past 500 → ach.
            int spent = PlayerPrefs.GetInt("TotalDiamondsSpent", 0) + (myDiamonds - ReadDiamonds());
            PlayerPrefs.SetInt("TotalDiamondsSpent", spent);
            if (spent >= 500) AchievementSystem.Unlock("SHOPKEEPER_FRIEND");
        }

        PlayerPrefs.Save();
        UpdateUI(true);
        RefreshItemListStates();
    }

    public void OnUpgradePressed()
    {
        int myDiamonds = ReadDiamonds();
        bool didUpgrade = false;

        if (isViewingWeapon && selectedWeaponData != null)
        {
            int id = selectedWeaponData.weaponID;
            int level = PlayerPrefs.GetInt("WeaponLevel_" + id, 0);

            if (level < selectedWeaponData.maxUpgradeLevel && myDiamonds >= selectedWeaponData.GetUpgradeCost(level))
            {
                AudioManager.Instance?.PlayUI(AudioID.UI_LevelUp);
                WriteDiamonds(myDiamonds - selectedWeaponData.GetUpgradeCost(level));
                PlayerPrefs.SetInt("WeaponLevel_" + id, level + 1);
                didUpgrade = true;
            }
            else AudioManager.Instance?.PlayUI(AudioID.UI_Error);
        }
        else if (!isViewingWeapon && selectedArmorData != null)
        {
            int id = selectedArmorData.armorID;
            int level = PlayerPrefs.GetInt("ArmorLevel_" + id, 0);

            if (level < selectedArmorData.maxUpgradeLevel && myDiamonds >= selectedArmorData.GetUpgradeCost(level))
            {
                AudioManager.Instance?.PlayUI(AudioID.UI_LevelUp);
                WriteDiamonds(myDiamonds - selectedArmorData.GetUpgradeCost(level));
                PlayerPrefs.SetInt("ArmorLevel_" + id, level + 1);
                didUpgrade = true;
            }
            else AudioManager.Instance?.PlayUI(AudioID.UI_Error);
        }

        if (didUpgrade)
        {
            PlayPurchaseCelebration();
            // Upgrading gear counts as a shop purchase for the guide/mission
            // that asks the player to spend in the shop — the flag was only set
            // on BUY, so upgrading gear you already owned never credited it.
            if (PlayerPrefs.GetInt("ShopFirstPurchase", 0) == 0)
                PlayerPrefs.SetInt("ShopFirstPurchase", 1);
        }

        PlayerPrefs.Save();
        UpdateUI(true);
        RefreshItemListStates();
    }

    // Reruns the SetState pass on every ShopItemButton in the currently
    // visible list. Called after a buy or upgrade so the tile flips from
    // Locked→Owned or Owned→Equipped without needing a full re-open of
    // the category.
    private void RefreshItemListStates()
    {
        if (itemListContent == null) return;
        int myDiamonds = ReadDiamonds();
        int equippedWepID = PlayerPrefs.GetInt("SelectedWeaponID", 0);

        for (int i = 0; i < itemListContent.childCount; i++)
        {
            var btn = itemListContent.GetChild(i).GetComponent<ShopItemButton>();
            if (btn == null) continue;
            if (btn.boundWeapon != null)
            {
                WeaponData w = btn.boundWeapon;
                int lvl = PlayerPrefs.GetInt("WeaponLevel_" + w.weaponID, 0);
                bool owned = PlayerPrefs.GetInt("WeaponUnlocked_" + w.weaponID, w.price == 0 ? 1 : 0) == 1;
                bool equipped = owned && w.weaponID == equippedWepID;
                var state = equipped ? ShopItemButton.OwnedState.Equipped
                          : owned ? ShopItemButton.OwnedState.Owned
                                  : ShopItemButton.OwnedState.Locked;
                btn.SetState(state, lvl, w.maxUpgradeLevel, w.price, myDiamonds);
            }
            else if (btn.boundArmor != null)
            {
                ArmorData a = btn.boundArmor;
                int equippedIndex = PlayerPrefs.GetInt($"EquippedArmor_{a.category}", 0);
                int lvl = PlayerPrefs.GetInt("ArmorLevel_" + a.armorID, 0);
                bool owned = PlayerPrefs.GetInt("ArmorUnlocked_" + a.armorID, a.price == 0 ? 1 : 0) == 1;
                bool equipped = owned && a.prefabIndex == equippedIndex;
                var state = equipped ? ShopItemButton.OwnedState.Equipped
                          : owned ? ShopItemButton.OwnedState.Owned
                                  : ShopItemButton.OwnedState.Locked;
                btn.SetState(state, lvl, a.maxUpgradeLevel, a.price, myDiamonds);
            }
        }
    }

    // Plays the "you got it!" flourish. Optional pieces (particle system,
    // full-screen flash overlay) are inspector-optional so leaving any
    // field null just gracefully skips that part.
    private void PlayPurchaseCelebration()
    {
        if (purchaseVFX != null)
        {
            purchaseVFX.gameObject.SetActive(true);
            purchaseVFX.Stop();
            purchaseVFX.Clear();
            purchaseVFX.Play();
        }
        // Reuse existing audio slots so we don't need new FMOD events:
        // Boss_Stagger is a punchy metallic hit, Region_VictoryStinger is a
        // celebration sting for the upgrade-level pop.
        if (AudioManager.Instance != null) AudioManager.Instance.PlaySFX(AudioID.Boss_Stagger);
        if (purchaseFlashOverlay != null) StartCoroutine(FlashOverlayRoutine());
        if (currentHeroModel != null) PulseHero(currentHeroModel.transform);
    }

    private Coroutine _heroPulseCo;
    private Vector3 _heroPulseBase = Vector3.one;
    private Transform _heroPulseTarget;

    // Stop any running pulse and restore the TRUE base scale before starting a
    // new one. The old code StartCoroutine'd each time and re-read t.localScale
    // as "original" — so a rapid second purchase captured the mid-pulse enlarged
    // scale as the baseline and the model compounded / froze bigger each buy.
    private void PulseHero(Transform t)
    {
        if (t == null) return;
        if (_heroPulseCo != null && _heroPulseTarget == t) { StopCoroutine(_heroPulseCo); t.localScale = _heroPulseBase; }
        if (_heroPulseTarget != t) { _heroPulseTarget = t; _heroPulseBase = t.localScale; }
        t.localScale = _heroPulseBase;
        _heroPulseCo = StartCoroutine(HeroPulseRoutine(t, _heroPulseBase));
    }

    private IEnumerator FlashOverlayRoutine()
    {
        Image img = purchaseFlashOverlay;
        if (img == null) yield break;
        img.gameObject.SetActive(true);
        img.raycastTarget = false;

        Color baseC = img.color;
        float peak = purchaseFlashAlpha;
        float dur = Mathf.Max(0.05f, purchaseFlashDuration);
        float t = 0f;
        while (t < dur)
        {
            t += Time.unscaledDeltaTime;
            float k = 1f - Mathf.Clamp01(t / dur);
            k = k * k;
            img.color = new Color(baseC.r, baseC.g, baseC.b, k * peak);
            yield return null;
        }
        img.color = new Color(baseC.r, baseC.g, baseC.b, 0f);
    }

    private IEnumerator HeroPulseRoutine(Transform t, Vector3 originalScale)
    {
        if (t == null) yield break;
        Vector3 peak = originalScale * Mathf.Max(1.01f, purchasePulseScale);

        float up = 0.12f, down = 0.22f;
        float e = 0f;
        while (e < up)
        {
            e += Time.unscaledDeltaTime;
            float k = e / up;
            k = 1f - (1f - k) * (1f - k);
            t.localScale = Vector3.Lerp(originalScale, peak, k);
            yield return null;
        }
        e = 0f;
        while (e < down)
        {
            e += Time.unscaledDeltaTime;
            float k = e / down;
            k = k * k;
            t.localScale = Vector3.Lerp(peak, originalScale, k);
            yield return null;
        }
        t.localScale = originalScale;
    }

    private void UpdateUI(bool animateText)
    {
        if (buyButton) buyButton.gameObject.SetActive(true);
        if (diamondBalanceText != null) diamondBalanceText.text = LocalizationManager.Tr("Diamonds:") + " " + ReadDiamonds().ToString("N0");
        int myDiamonds = ReadDiamonds();

        if (isViewingWeapon && selectedWeaponData != null)
        {
            WeaponData w = selectedWeaponData;
            int lvl = PlayerPrefs.GetInt("WeaponLevel_" + w.weaponID, 0);
            bool isBought = PlayerPrefs.GetInt("WeaponUnlocked_" + w.weaponID, w.price == 0 ? 1 : 0) == 1;
            bool isEquipped = PlayerPrefs.GetInt("SelectedWeaponID", 0) == w.weaponID;

            UpdateItemDetails(w.weaponName, w.description, w.icon, lvl, w.maxUpgradeLevel, w.price, isBought, isEquipped, myDiamonds, w.GetUpgradeCost(lvl));
            bool wPreview = isBought && lvl < w.maxUpgradeLevel;
            int nl = lvl + 1;
            UpdateStatsUI(w.damageBonus + (lvl * w.damagePerLevel), 400f, w.attackSpeed + (lvl * w.attackSpeedPerLevel), 3f, w.critChance + (lvl * w.critChancePerLevel), 1f, w.basePower + (lvl * w.powerPerLevel),
                wPreview,
                w.damageBonus + (nl * w.damagePerLevel), w.attackSpeed + (nl * w.attackSpeedPerLevel), w.critChance + (nl * w.critChancePerLevel), w.basePower + (nl * w.powerPerLevel));
        }
        else if (!isViewingWeapon && selectedArmorData != null)
        {
            ArmorData a = selectedArmorData;
            int lvl = PlayerPrefs.GetInt("ArmorLevel_" + a.armorID, 0);
            bool isBought = PlayerPrefs.GetInt("ArmorUnlocked_" + a.armorID, a.price == 0 ? 1 : 0) == 1;
            bool isEquipped = PlayerPrefs.GetInt($"EquippedArmor_{a.category}", 0) == a.prefabIndex;

            UpdateItemDetails(a.armorName, a.description, a.icon, lvl, a.maxUpgradeLevel, a.price, isBought, isEquipped, myDiamonds, a.GetUpgradeCost(lvl));
            bool aPreview = isBought && lvl < a.maxUpgradeLevel;
            int nla = lvl + 1;
            UpdateStatsUI(a.baseHealthBonus + (lvl * a.healthPerLevel), 500f, (a.baseDamageReduction + (lvl * a.reductionPerLevel)) * 100f, 60f, 0, 1f, a.basePower + (lvl * a.powerPerLevel),
                aPreview,
                a.baseHealthBonus + (nla * a.healthPerLevel), (a.baseDamageReduction + (nla * a.reductionPerLevel)) * 100f, 0, a.basePower + (nla * a.powerPerLevel));
        }

        if (animateText)
        {
            if (itemNameText != null) PopTextSafe(itemNameText);
            if (priceText != null) PopTextSafe(priceText);
            // Icon punch — quick scale bounce so the descriptionItemIcon
            // feels responsive to selection. Uses the same PopText coroutine
            // style on the icon's Transform for consistency.
            if (descriptionItemIcon != null) StartCoroutine(IconPunch(descriptionItemIcon.transform));
        }
        CalculateAndSaveTotalPower();
    }

    private IEnumerator IconPunch(Transform t)
    {
        if (t == null) yield break;
        Vector3 baseS = Vector3.one;
        // 1.15 -> 1.0 with sine ease
        float dur = 0.28f;
        float e = 0f;
        while (e < 1f)
        {
            e += Time.unscaledDeltaTime / dur;
            float k = 1f - Mathf.Clamp01(e);
            t.localScale = baseS * (1f + 0.15f * (k * k));
            yield return null;
        }
        t.localScale = baseS;
    }

    private void UpdateItemDetails(string name, string desc, Sprite icn, int lvl, int maxLvl, int price, bool isBought, bool isEquipped, int myDiamonds, int upgCost)
    {
        if (itemNameText) itemNameText.text = LocalizationManager.Tr(name) + (lvl > 0 ? $" <size=70%><color=#AAAAAA>{LocalizationManager.Tr("SHOP_ITEM_LEVEL_TAG", lvl, maxLvl)}</color></size>" : "");
        if (itemDescriptionText) itemDescriptionText.text = LocalizationManager.Tr(desc);
        if (descriptionItemIcon) { descriptionItemIcon.gameObject.SetActive(true); descriptionItemIcon.sprite = icn; }

        if (!isBought)
        {
            priceText.text = price.ToString("N0");
            buyButton.interactable = (myDiamonds >= price);
            priceText.color = (myDiamonds >= price) ? textNormalColor : textErrorColor;
            if (upgradeButton != null) upgradeButton.gameObject.SetActive(false);

            SetAffordabilityHint(myDiamonds >= price ? "" :
                LocalizationManager.Tr("SHOP_NEED_MORE_DIAMONDS", (price - myDiamonds).ToString("N0")));
        }
        else if (!isEquipped)
        {
            priceText.text = LocalizationManager.Tr("EQUIP");
            buyButton.interactable = true;
            priceText.color = textNormalColor;
            SetAffordabilityHint("");
        }
        else
        {
            priceText.text = LocalizationManager.Tr("EQUIPPED");
            buyButton.interactable = false;
            priceText.color = textSuccessColor;
            SetAffordabilityHint("");
        }

        if (isBought && upgradeButton != null)
        {
            upgradeButton.gameObject.SetActive(true);

            // The upgrade COST wasn't appearing — the button just read "Upgrade".
            // Write the price string to BOTH the dedicated price label AND the
            // button's own visible text child, whichever the scene actually shows,
            // so the cost is always on screen.
            var btnLabel = upgradeButton.GetComponentInChildren<TextMeshProUGUI>(true);
            if (upgradePriceText == null) upgradePriceText = btnLabel;

            // Exclude both the dedicated price label and the button's own text
            // child from the AutoLocalize walkers. The 0.5s scene repeater had
            // captured the baked "Upgrade" placeholder as a loc key and kept
            // re-writing Tr("Upgrade") over our "Upgrade for 250" every half
            // second — that's why the price never showed and the text flickered.
            MarkNoAutoLocalize(upgradePriceText);
            MarkNoAutoLocalize(btnLabel);

            string label;
            Color col;
            bool interactable;
            if (lvl < maxLvl)
            {
                label = LocalizationManager.Tr("Upgrade for {0}", upgCost.ToString("N0"));
                col = (myDiamonds >= upgCost) ? textNormalColor : textErrorColor;
                interactable = (myDiamonds >= upgCost);
                if (myDiamonds < upgCost)
                    SetAffordabilityHint(LocalizationManager.Tr("SHOP_NEED_MORE_DIAMONDS", (upgCost - myDiamonds).ToString("N0")));
            }
            else
            {
                label = LocalizationManager.Tr("MAX");
                col = new Color(0.95f, 0.80f, 0.45f);
                interactable = false;
            }

            if (upgradePriceText != null) { upgradePriceText.text = label; upgradePriceText.color = col; }
            if (btnLabel != null && btnLabel != upgradePriceText) { btnLabel.text = label; btnLabel.color = col; }
            upgradeButton.interactable = interactable;
        }
    }

    private void SetAffordabilityHint(string text)
    {
        if (affordabilityHintText == null) return;
        affordabilityHintText.text = text;
        affordabilityHintText.gameObject.SetActive(!string.IsNullOrEmpty(text));
    }

    // Tag a code-driven label so neither AutoLocalize walker re-captures its
    // baked English placeholder as a loc key and overwrites the live value.
    private static void MarkNoAutoLocalize(TextMeshProUGUI t)
    {
        if (t != null && t.GetComponent<NoAutoLocalize>() == null)
            t.gameObject.AddComponent<NoAutoLocalize>();
    }

    private Coroutine[] statBarLerps = new Coroutine[4];

    // preview* params: when showPreview is true (item is owned and not max), the
    // stat readouts show "current → next" so the player sees exactly what an
    // upgrade grants instead of buying blind. -1 max/val disables that stat's row.
    private void UpdateStatsUI(float val1, float max1, float val2, float max2, float val3, float max3, int power,
                              bool showPreview = false, float n1 = 0f, float n2 = 0f, float n3 = 0f, int nPower = 0)
    {
        string arrow = " <color=#A8E6CF>→</color> ";
        if (stat1PercentText)
        {
            string cur = isViewingWeapon ? Mathf.RoundToInt((val1 / max1) * 100) + "%" : "+" + Mathf.RoundToInt(val1) + " HP";
            string nxt = isViewingWeapon ? Mathf.RoundToInt((n1 / max1) * 100) + "%" : "+" + Mathf.RoundToInt(n1) + " HP";
            stat1PercentText.text = showPreview ? cur + arrow + "<color=#A8E6CF>" + nxt + "</color>" : cur;
        }
        if (stat2PercentText)
        {
            string cur = isViewingWeapon ? Mathf.RoundToInt((val2 / max2) * 100) + "%" : "+" + val2.ToString("F1") + "%";
            string nxt = isViewingWeapon ? Mathf.RoundToInt((n2 / max2) * 100) + "%" : "+" + n2.ToString("F1") + "%";
            stat2PercentText.text = showPreview ? cur + arrow + "<color=#A8E6CF>" + nxt + "</color>" : cur;
        }
        if (stat3PercentText)
        {
            string cur = Mathf.RoundToInt((val3 / max3) * 100) + "%";
            string nxt = Mathf.RoundToInt((n3 / max3) * 100) + "%";
            stat3PercentText.text = showPreview ? cur + arrow + "<color=#A8E6CF>" + nxt + "</color>" : cur;
        }
        if (powerPercentText) powerPercentText.text = showPreview ? power + arrow + "<color=#A8E6CF>" + nPower + "</color>" : power.ToString();

        // Bars ease to the CURRENT value (the → text carries the preview).
        LerpBar(0, stat1Fill, Mathf.Clamp01(val1 / max1));
        LerpBar(1, stat2Fill, Mathf.Clamp01(val2 / max2));
        LerpBar(2, stat3Fill, Mathf.Clamp01(val3 / max3));
        LerpBar(3, powerFill, Mathf.Clamp01((float)power / 1000f));
    }

    private void LerpBar(int slot, Image bar, float target)
    {
        if (bar == null) return;
        if (statBarLerps[slot] != null) StopCoroutine(statBarLerps[slot]);
        statBarLerps[slot] = StartCoroutine(BarLerpRoutine(bar, target));
    }

    private IEnumerator BarLerpRoutine(Image bar, float target)
    {
        // Ease-out from current to target over ~0.35s. Gives the stat bars
        // the feel of "the value is being weighed" rather than snapping.
        float from = bar.fillAmount;
        float dur = 0.35f;
        float t = 0f;
        while (t < 1f)
        {
            t += Time.unscaledDeltaTime / dur;
            float e = 1f - (1f - Mathf.Clamp01(t)) * (1f - Mathf.Clamp01(t));
            bar.fillAmount = Mathf.Lerp(from, target, e);
            yield return null;
        }
        bar.fillAmount = target;
    }

    private void CalculateAndSaveTotalPower()
    {
        int baseTotal = 50;
        float totalArmorHealth = 0f;
        float totalArmorReduction = 0f;
        int selWepID = PlayerPrefs.GetInt("SelectedWeaponID", 0);

        if (weapons != null)
        {
            foreach (var w in weapons)
            {
                if (w.weaponID == selWepID)
                {
                    int lvl = PlayerPrefs.GetInt("WeaponLevel_" + w.weaponID, 0);
                    baseTotal += w.basePower + (lvl * w.powerPerLevel);
                    PlayerPrefs.SetFloat("EquippedWeaponDamage", w.damageBonus + (lvl * w.damagePerLevel));
                    // Crit and attack speed were shown in the shop UI but never
                    // persisted, so the player never actually received them
                    // (crit stuck at 5%, attack rate fixed). Write them so
                    // PlayerController can apply the equipped weapon's real stats.
                    PlayerPrefs.SetFloat("EquippedWeaponCrit", w.critChance + (lvl * w.critChancePerLevel));
                    PlayerPrefs.SetFloat("EquippedWeaponAttackSpeed", w.attackSpeed + (lvl * w.attackSpeedPerLevel));
                }
            }
        }

        if (armors != null)
        {
            foreach (ArmorCategory cat in System.Enum.GetValues(typeof(ArmorCategory)))
            {
                int equippedIndex = PlayerPrefs.GetInt($"EquippedArmor_{cat}", 0);
                foreach (var a in armors)
                {
                    if (a != null && a.category == cat && a.prefabIndex == equippedIndex)
                    {
                        int lvl = PlayerPrefs.GetInt("ArmorLevel_" + a.armorID, 0);
                        baseTotal += a.basePower + (lvl * a.powerPerLevel);
                        totalArmorHealth += a.baseHealthBonus + (lvl * a.healthPerLevel);
                        totalArmorReduction += a.baseDamageReduction + (lvl * a.reductionPerLevel);
                        break;
                    }
                }
            }
        }

        PlayerPrefs.SetFloat("EquippedArmorHealth", totalArmorHealth);
        PlayerPrefs.SetFloat("EquippedArmorReduction", totalArmorReduction);
        // baseTotal is the GEAR-only power score (weapon + armor). Persist it
        // separately so PowerSystemManager can fold meta-upgrades and the
        // forge in on top without double-counting.
        PlayerPrefs.SetInt("PlayerTotalPower_Gear", baseTotal);
        PlayerPrefs.SetInt("PlayerTotalPower", baseTotal);
        if (PowerSystemManager.Instance != null) PowerSystemManager.Instance.RefreshAndSave();
    }

    private void SpawnDummyHero()
    {
        if (dummyHeroPrefab != null)
        {
            currentHeroModel = Instantiate(dummyHeroPrefab, heroPedestalPos.position, heroPedestalPos.rotation);
            currentHeroModel.transform.localScale = new Vector3(1f, 1f, 1f);

            PlayerController pc = currentHeroModel.GetComponent<PlayerController>();
            if (pc != null) pc.enabled = false;

            CharacterController cc = currentHeroModel.GetComponent<CharacterController>();
            if (cc != null) cc.enabled = false;

            Animator anim = currentHeroModel.GetComponentInChildren<Animator>();
            if (anim != null)
            {
                anim.SetBool("IsGrounded", true);
                anim.SetFloatSafe("Speed", 0f);
            }

            dummyArmorManager = currentHeroModel.GetComponent<ModularArmorManager>();
        }
    }

    private void EquipWeaponToHero(WeaponData w)
    {
        if (currentWeaponModel != null) DestroyImmediate(currentWeaponModel);
        if (currentHeroModel == null || w.shopPrefab == null) return;

        // Բ��: ������ ��� ��������� ����� ������!
        Transform socket = FindDeepChild(currentHeroModel.transform, "WeaponSocket");
        if (socket == null) socket = FindDeepChild(currentHeroModel.transform, "handslot.r");
        if (socket == null) socket = FindDeepChild(currentHeroModel.transform, "hand_r");
        if (socket == null) socket = FindDeepChild(currentHeroModel.transform, "hand_R");
        if (socket == null) socket = FindDeepChild(currentHeroModel.transform, "RightHand");

        if (socket != null)
        {
            currentWeaponModel = Instantiate(w.shopPrefab, socket);
            currentWeaponModel.transform.localPosition = Vector3.zero;
            currentWeaponModel.transform.localRotation = Quaternion.identity;

            foreach (var s in currentWeaponModel.GetComponents<MonoBehaviour>())
            {
                if (s != null) s.enabled = false;
            }
        }
    }

    private Transform FindDeepChild(Transform parent, string name)
    {
        foreach (Transform child in parent)
        {
            if (child.name.ToLower() == name.ToLower()) return child;
            Transform r = FindDeepChild(child, name);
            if (r != null) return r;
        }
        return null;
    }

    private IEnumerator PopText(TextMeshProUGUI textComponent)
    {
        if (textComponent == null) yield break;
        textComponent.transform.localScale = Vector3.one;
        // Softer pop (was 1.2 — too strong for a rapidly-repeating stat text).
        Vector3 popScale = new Vector3(1.12f, 1.12f, 1.12f);
        float t = 0;
        while (t < 1) { t += Time.unscaledDeltaTime * 12f; textComponent.transform.localScale = Vector3.Lerp(Vector3.one, popScale, t); yield return null; }
        t = 0;
        while (t < 1) { t += Time.unscaledDeltaTime * 10f; textComponent.transform.localScale = Vector3.Lerp(popScale, Vector3.one, Mathf.SmoothStep(0f, 1f, t)); yield return null; }
        textComponent.transform.localScale = Vector3.one;
    }

    public void GoToCampScene()
    {
        AudioManager.Instance?.PlayUI(AudioID.UI_Click);
        PlayerPrefs.SetInt("ReturningFromShop", 1);
        PlayerPrefs.Save();
        Cursor.visible = false;
        Cursor.lockState = CursorLockMode.Locked;

        // Բ��: ��������� �������� GlobalHUD.Instance!
        if (GlobalHUD.Instance != null) GlobalHUD.Instance.FadeAndLoadScene(campSceneName);
        else SceneLoader.LoadScene(campSceneName);
    }
}