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

    [Header("UI Stats Values")]
    public TextMeshProUGUI stat1PercentText;
    public TextMeshProUGUI stat2PercentText;
    public TextMeshProUGUI stat3PercentText;
    public TextMeshProUGUI powerPercentText;

    [Header("Rotation Settings")]
    public float rotationSpeed = 500f;

    private GameObject currentHeroModel;
    private GameObject currentWeaponModel;
    private ModularArmorManager dummyArmorManager;

    private bool isViewingWeapon = false;

    private WeaponData selectedWeaponData;
    private ArmorData selectedArmorData;

    private const string DIAMONDS_KEY = "PlayerDiamonds";

    private Dictionary<CanvasGroup, Coroutine> activeFades = new Dictionary<CanvasGroup, Coroutine>();

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
    }

    private void Update()
    {
        if (currentHeroModel != null && Input.GetMouseButton(0))
        {
            float rotX = Input.GetAxis("Mouse X") * rotationSpeed * Time.deltaTime;
            currentHeroModel.transform.Rotate(Vector3.up, -rotX, Space.World);
        }
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

    public void OpenWeaponCategory(ItemCategory cat)
    {
        if (AudioManager.Instance != null) AudioManager.Instance.PlayUI(AudioID.UI_Click);

        isViewingWeapon = true;
        SetupDynamicUI(true);

        FadeGroup(arsenalGridGroup, 0f);

        foreach (Transform child in itemListContent) Destroy(child.gameObject);

        WeaponData firstWep = null;
        foreach (var w in weapons)
        {
            if (w.category == cat)
            {
                if (firstWep == null) firstWep = w;
                CreateListButton(w.weaponName, w.icon, () => SelectWeapon(w));
            }
        }

        if (firstWep != null) SelectWeapon(firstWep);
        else SetEmptyUI();

        ShowContentPanels();
    }

    private void SelectWeapon(WeaponData w)
    {
        if (AudioManager.Instance != null) AudioManager.Instance.PlayUI(AudioID.UI_Hover);
        selectedWeaponData = w;
        EquipWeaponToHero(w);
        UpdateUI(true);
    }

    public void OpenArmorCategory(ArmorCategory cat)
    {
        if (AudioManager.Instance != null) AudioManager.Instance.PlayUI(AudioID.UI_Click);

        isViewingWeapon = false;
        SetupDynamicUI(false);

        FadeGroup(arsenalGridGroup, 0f);

        foreach (Transform child in itemListContent) Destroy(child.gameObject);

        ArmorData firstArm = null;
        if (armors != null)
        {
            foreach (var a in armors)
            {
                if (a != null && a.category == cat)
                {
                    if (firstArm == null) firstArm = a;
                    CreateListButton(a.armorName, a.icon, () => SelectArmor(a));
                }
            }
        }

        if (firstArm != null) SelectArmor(firstArm);
        else SetEmptyUI();

        ShowContentPanels();
    }

    private void SelectArmor(ArmorData a)
    {
        if (AudioManager.Instance != null) AudioManager.Instance.PlayUI(AudioID.UI_Hover);
        selectedArmorData = a;
        if (dummyArmorManager != null) dummyArmorManager.EquipArmorVisuals((ArmorSlot)a.category, a.prefabIndex);
        UpdateUI(true);
    }

    private void CreateListButton(string name, Sprite icon, UnityEngine.Events.UnityAction action)
    {
        GameObject btnObj = Instantiate(itemButtonPrefab, itemListContent);
        ShopItemButton itemSlot = btnObj.GetComponent<ShopItemButton>();
        if (itemSlot != null)
        {
            if (itemSlot.nameText != null) itemSlot.nameText.text = name;
            if (itemSlot.iconImage != null && icon != null) itemSlot.iconImage.sprite = icon;
            if (itemSlot.buttonComponent != null)
            {
                itemSlot.buttonComponent.onClick.AddListener(() => { PlayButtonAnim(itemSlot.transform); action.Invoke(); });
            }
        }
    }

    private void ShowContentPanels()
    {
        FadeGroup(arsenalContentGroup, 1f);
        FadeGroup(arsenalDescriptionGroup, 1f);
        FadeGroup(statsPanelGroup, 1f);
    }

    private void SetEmptyUI()
    {
        if (itemNameText) itemNameText.text = "Empty Category";
        if (itemDescriptionText) itemDescriptionText.text = "There are no items in this category yet.";
        UpdateUI(false);
    }

    public void ReturnToArsenalGrid()
    {
        if (AudioManager.Instance != null) AudioManager.Instance.PlayUI(AudioID.UI_Click);

        isViewingWeapon = false;

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
            if (stat1Label) stat1Label.text = "DAMAGE";
            if (stat2Label) stat2Label.text = "ATK SPEED";
            if (stat3Label) { stat3Label.gameObject.SetActive(true); stat3Label.text = "CRIT"; }
            if (stat3Fill) stat3Fill.gameObject.SetActive(true);
            if (stat3PercentText) stat3PercentText.gameObject.SetActive(true);
        }
        else
        {
            if (stat1Label) stat1Label.text = "HEALTH";
            if (stat2Label) stat2Label.text = "DEFENSE";
            if (stat3Label) stat3Label.gameObject.SetActive(false);
            if (stat3Fill) stat3Fill.gameObject.SetActive(false);
            if (stat3PercentText) stat3PercentText.gameObject.SetActive(false);
        }
        if (powerLabel) powerLabel.text = "POWER";
    }

    public void OnBuyOrSelectPressed()
    {
        int myDiamonds = PlayerPrefs.GetInt(DIAMONDS_KEY, 0);

        if (isViewingWeapon && selectedWeaponData != null)
        {
            int id = selectedWeaponData.weaponID;
            int price = selectedWeaponData.price;
            string unlockKey = "WeaponUnlocked_" + id;
            bool isBought = PlayerPrefs.GetInt(unlockKey, price == 0 ? 1 : 0) == 1;

            if (!isBought && myDiamonds >= price)
            {
                AudioManager.Instance?.PlayUI(AudioID.UI_Purchase);
                PlayerPrefs.SetInt(DIAMONDS_KEY, myDiamonds - price);
                PlayerPrefs.SetInt(unlockKey, 1);
                PlayerPrefs.SetInt("SelectedWeaponID", id);
            }
            else if (isBought)
            {
                AudioManager.Instance?.PlayUI(AudioID.UI_Click);
                PlayerPrefs.SetInt("SelectedWeaponID", id);
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
                PlayerPrefs.SetInt(DIAMONDS_KEY, myDiamonds - price);
                PlayerPrefs.SetInt(unlockKey, 1);
                dummyArmorManager?.EquipAndSaveArmor((ArmorSlot)selectedArmorData.category, selectedArmorData.prefabIndex);
            }
            else if (isBought)
            {
                AudioManager.Instance?.PlayUI(AudioID.UI_Click);
                dummyArmorManager?.EquipAndSaveArmor((ArmorSlot)selectedArmorData.category, selectedArmorData.prefabIndex);
            }
            else AudioManager.Instance?.PlayUI(AudioID.UI_Error);
        }

        PlayerPrefs.Save(); UpdateUI(true);
    }

    public void OnUpgradePressed()
    {
        int myDiamonds = PlayerPrefs.GetInt(DIAMONDS_KEY, 0);

        if (isViewingWeapon && selectedWeaponData != null)
        {
            int id = selectedWeaponData.weaponID;
            int level = PlayerPrefs.GetInt("WeaponLevel_" + id, 0);

            if (level < selectedWeaponData.maxUpgradeLevel && myDiamonds >= selectedWeaponData.GetUpgradeCost(level))
            {
                AudioManager.Instance?.PlayUI(AudioID.UI_LevelUp);
                PlayerPrefs.SetInt(DIAMONDS_KEY, myDiamonds - selectedWeaponData.GetUpgradeCost(level));
                PlayerPrefs.SetInt("WeaponLevel_" + id, level + 1);
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
                PlayerPrefs.SetInt(DIAMONDS_KEY, myDiamonds - selectedArmorData.GetUpgradeCost(level));
                PlayerPrefs.SetInt("ArmorLevel_" + id, level + 1);
            }
            else AudioManager.Instance?.PlayUI(AudioID.UI_Error);
        }

        PlayerPrefs.Save(); UpdateUI(true);
    }

    private void UpdateUI(bool animateText)
    {
        if (buyButton) buyButton.gameObject.SetActive(true);
        if (diamondBalanceText != null) diamondBalanceText.text = "Diamonds: " + PlayerPrefs.GetInt(DIAMONDS_KEY, 0).ToString("N0");
        int myDiamonds = PlayerPrefs.GetInt(DIAMONDS_KEY, 0);

        if (isViewingWeapon && selectedWeaponData != null)
        {
            WeaponData w = selectedWeaponData;
            int lvl = PlayerPrefs.GetInt("WeaponLevel_" + w.weaponID, 0);
            bool isBought = PlayerPrefs.GetInt("WeaponUnlocked_" + w.weaponID, w.price == 0 ? 1 : 0) == 1;
            bool isEquipped = PlayerPrefs.GetInt("SelectedWeaponID", 0) == w.weaponID;

            UpdateItemDetails(w.weaponName, w.description, w.icon, lvl, w.maxUpgradeLevel, w.price, isBought, isEquipped, myDiamonds, w.GetUpgradeCost(lvl));
            UpdateStatsUI(w.damageBonus + (lvl * w.damagePerLevel), 400f, w.attackSpeed + (lvl * w.attackSpeedPerLevel), 3f, w.critChance + (lvl * w.critChancePerLevel), 1f, w.basePower + (lvl * w.powerPerLevel));
        }
        else if (!isViewingWeapon && selectedArmorData != null)
        {
            ArmorData a = selectedArmorData;
            int lvl = PlayerPrefs.GetInt("ArmorLevel_" + a.armorID, 0);
            bool isBought = PlayerPrefs.GetInt("ArmorUnlocked_" + a.armorID, a.price == 0 ? 1 : 0) == 1;
            bool isEquipped = PlayerPrefs.GetInt($"EquippedArmor_{a.category}", 0) == a.prefabIndex;

            UpdateItemDetails(a.armorName, a.description, a.icon, lvl, a.maxUpgradeLevel, a.price, isBought, isEquipped, myDiamonds, a.GetUpgradeCost(lvl));
            UpdateStatsUI(a.baseHealthBonus + (lvl * a.healthPerLevel), 500f, (a.baseDamageReduction + (lvl * a.reductionPerLevel)) * 100f, 60f, 0, 1f, a.basePower + (lvl * a.powerPerLevel));
        }

        if (animateText)
        {
            if (itemNameText != null) StartCoroutine(PopText(itemNameText));
            if (priceText != null) StartCoroutine(PopText(priceText));
        }
        CalculateAndSaveTotalPower();
    }

    private void UpdateItemDetails(string name, string desc, Sprite icn, int lvl, int maxLvl, int price, bool isBought, bool isEquipped, int myDiamonds, int upgCost)
    {
        if (itemNameText) itemNameText.text = name + (lvl > 0 ? $" <size=70%><color=#AAAAAA>(Lv. {lvl}/{maxLvl})</color></size>" : "");
        if (itemDescriptionText) itemDescriptionText.text = desc;
        if (descriptionItemIcon) { descriptionItemIcon.gameObject.SetActive(true); descriptionItemIcon.sprite = icn; }

        if (!isBought)
        {
            priceText.text = price.ToString("N0");
            buyButton.interactable = (myDiamonds >= price);
            priceText.color = (myDiamonds >= price) ? textNormalColor : textErrorColor;
            if (upgradeButton != null) upgradeButton.gameObject.SetActive(false);
        }
        else if (!isEquipped)
        {
            priceText.text = "EQUIP";
            buyButton.interactable = true;
            priceText.color = textNormalColor;
        }
        else
        {
            priceText.text = "EQUIPPED";
            buyButton.interactable = false;
            priceText.color = textSuccessColor;
        }

        if (isBought && upgradeButton != null)
        {
            upgradeButton.gameObject.SetActive(true);
            if (lvl < maxLvl)
            {
                upgradePriceText.text = "Upgrade for " + upgCost.ToString("N0");
                upgradePriceText.color = (myDiamonds >= upgCost) ? textNormalColor : textErrorColor;
                upgradeButton.interactable = (myDiamonds >= upgCost);
            }
            else
            {
                upgradePriceText.text = "MAX";
                upgradePriceText.color = new Color(1f, 0.8f, 0.2f);
                upgradeButton.interactable = false;
            }
        }
    }

    private void UpdateStatsUI(float val1, float max1, float val2, float max2, float val3, float max3, int power)
    {
        if (stat1PercentText) stat1PercentText.text = isViewingWeapon ? Mathf.RoundToInt((val1 / max1) * 100) + "%" : "+" + Mathf.RoundToInt(val1) + " HP";
        if (stat2PercentText) stat2PercentText.text = isViewingWeapon ? Mathf.RoundToInt((val2 / max2) * 100) + "%" : "+" + val2.ToString("F1") + "%";
        if (stat3PercentText) stat3PercentText.text = Mathf.RoundToInt((val3 / max3) * 100) + "%";
        if (powerPercentText) powerPercentText.text = power.ToString();

        if (stat1Fill) stat1Fill.fillAmount = Mathf.Clamp01(val1 / max1);
        if (stat2Fill) stat2Fill.fillAmount = Mathf.Clamp01(val2 / max2);
        if (stat3Fill) stat3Fill.fillAmount = Mathf.Clamp01(val3 / max3);
        if (powerFill) powerFill.fillAmount = Mathf.Clamp01((float)power / 1000f);
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
        PlayerPrefs.SetInt("PlayerTotalPower", baseTotal);
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
                anim.SetFloat("Speed", 0f);
            }

            dummyArmorManager = currentHeroModel.GetComponent<ModularArmorManager>();
        }
    }

    private void EquipWeaponToHero(WeaponData w)
    {
        if (currentWeaponModel != null) DestroyImmediate(currentWeaponModel);
        if (currentHeroModel == null || w.shopPrefab == null) return;

        // ФІКС: Шукаємо наш ідеальний сокет першим!
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
        Vector3 popScale = new Vector3(1.2f, 1.2f, 1.2f);
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

        // ФІКС: Повернуто підтримку GlobalHUD.Instance!
        if (GlobalHUD.Instance != null) GlobalHUD.Instance.FadeAndLoadScene(campSceneName);
        else if (LoadingManager.Instance != null) LoadingManager.Instance.LoadScene(campSceneName);
        else SceneManager.LoadScene(campSceneName);
    }
}