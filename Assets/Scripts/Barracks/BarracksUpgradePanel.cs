using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// Screen shown when the player interacts with the barracks (F key).
// Every widget is a SerializeField reference so the Figma-designed prefab
// can be dropped in and the fields wired by dragging — no code edits.
//
// Layout expectation (matches the Figma mockups):
//   HEADER      — title text + level-pip row (5 helm images) + diamond chip
//   TAB STRIP   — Hire / Upgrade Units / Upgrade Barracks (3 buttons + underlines)
//   HIRE CONTENT — hireRowParent hosts one hireRowPrefab per unit type
//   UPGRADE UNITS CONTENT — upgradeRowParent hosts upgradeRowPrefab per unit
//   UPGRADE BARRACKS CONTENT — single centred layout with diorama, level
//     breakdown, next-level card, resource chip trio, upgrade CTA + build time
public class BarracksUpgradePanel : MonoBehaviour
{
    [Header("Root")]
    public GameObject rootObject;
    public CanvasGroup canvasGroup;
    public Button closeButton;

    [Header("Header")]
    public TextMeshProUGUI titleText;
    public TextMeshProUGUI diamondsText;
    // Optional — 5 Image slots for the helm level pips at the top right.
    // Each pip gets swapped between the filled and empty sprite based on
    // hostBuilding.currentLevel. Leave empty if the panel doesn't use pips.
    public Image[] barracksLevelPips;
    public Sprite pipHelmFilledSprite;
    public Sprite pipHelmEmptySprite;

    [Header("Tabs")]
    public Button tabHireButton;
    public Button tabUpgradeUnitsButton;
    public Button tabUpgradeBarracksButton;
    public GameObject hireContainer;
    public GameObject upgradeUnitsContainer;
    public GameObject upgradeBarracksContainer;
    // Optional — thin brass underline images swapped visible/hidden per tab.
    public GameObject tabHireUnderline;
    public GameObject tabUpgradeUnitsUnderline;
    public GameObject tabUpgradeBarracksUnderline;
    // Optional — the darker background rectangle behind the active tab.
    // Wire these if your Figma prefab uses a separate rect for the "active"
    // state instead of a shared underline component.
    public GameObject tabHireActiveBg;
    public GameObject tabUpgradeUnitsActiveBg;
    public GameObject tabUpgradeBarracksActiveBg;
    // Tab-label TMPs so we can swap them between the cream (active) and
    // muted grey (inactive) colours on tab change. Leave null if your Figma
    // prefab uses a single colour for all tabs.
    public TextMeshProUGUI tabHireLabel;
    public TextMeshProUGUI tabUpgradeUnitsLabel;
    public TextMeshProUGUI tabUpgradeBarracksLabel;
    public Color tabActiveTextColor   = new Color(0.94f, 0.89f, 0.80f, 1f); // warm cream #F0E4CB
    public Color tabInactiveTextColor = new Color(0.54f, 0.52f, 0.47f, 1f); // muted grey #8A8478

    [Header("Hire Tab")]
    public Transform hireRowParent;
    public GameObject hireRowPrefab; // exposes BarracksHireRow fields
    public MercenaryRoster roster;   // fallback if singleton not ready

    [Header("Upgrade Units Tab")]
    public Transform upgradeRowParent;
    public GameObject upgradeRowPrefab; // exposes BarracksUpgradeUnitRow fields
    // Shared pip sprites for the per-unit level rows — the row prefab reads
    // these from the panel so we don't have to reassign them per row instance.
    public Sprite unitPipFilledSprite;
    public Sprite unitPipEmptySprite;

    [Header("Upgrade Barracks Tab")]
    public CampBuilding hostBuilding;  // the CampBuilding driving level state
    // Left column
    public Image barracksDioramaImage;
    public TextMeshProUGUI barracksCurrentLevelText;   // "LEVEL 3 / 5"
    public TextMeshProUGUI barracksSummaryText;        // "MAX SIZE 5 UNITS · KNIGHT TIER UNLOCKED"
    // Right column — next level card
    public TextMeshProUGUI barracksNextLevelText;      // "LEVEL 4"
    public TextMeshProUGUI barracksPerksText;          // multi-line perks
    // Right column — cost chip trio
    public TextMeshProUGUI barracksCostWoodText;
    public TextMeshProUGUI barracksCostStoneText;
    public TextMeshProUGUI barracksCostFoodText;
    // Right column — CTA
    public Button barracksUpgradeButton;
    public TextMeshProUGUI barracksUpgradeButtonText;  // "UPGRADE"
    public TextMeshProUGUI barracksBuildTimeText;      // "4:00"
    // Colour used for cost numbers when the player can't afford them.
    public Color costUnaffordableColor = new Color(0.70f, 0.37f, 0.32f, 1f); // muted rust #B25E52
    public Color costAffordableColor   = new Color(0.94f, 0.89f, 0.80f, 1f); // warm cream

    [Header("Optional Per-Level Overrides (Barracks Upgrade Tab)")]
    // Designer can override the auto-derived perks text per level here.
    // Element 0 = perks displayed when currentLevel = 0 (going to Lv 1),
    // Element 4 = when at Lv 5 (max level → panel hides the CTA).
    [TextArea(2, 4)] public string[] perksTextPerLevel = new string[5];
    // Same idea for the max-cap summary line under the current-level text.
    [TextArea(1, 2)] public string[] summaryTextPerLevel = new string[6];

    // Runtime bookkeeping so we don't rebuild row lists every frame.
    private readonly List<GameObject> spawnedHireRows = new List<GameObject>();
    private readonly List<GameObject> spawnedUpgradeRows = new List<GameObject>();

    private void Awake()
    {
        if (closeButton != null) closeButton.onClick.AddListener(Close);
        if (tabHireButton != null) tabHireButton.onClick.AddListener(() => ShowTab(0));
        if (tabUpgradeUnitsButton != null) tabUpgradeUnitsButton.onClick.AddListener(() => ShowTab(1));
        if (tabUpgradeBarracksButton != null) tabUpgradeBarracksButton.onClick.AddListener(() => ShowTab(2));
        if (barracksUpgradeButton != null) barracksUpgradeButton.onClick.AddListener(OnUpgradeBarracks);

        if (rootObject != null) rootObject.SetActive(false);
    }

    private void OnEnable()
    {
        MercenaryRoster.OnRosterChanged += Refresh;
    }

    private void OnDisable()
    {
        MercenaryRoster.OnRosterChanged -= Refresh;
    }

    public void Open()
    {
        if (rootObject != null) rootObject.SetActive(true);
        if (canvasGroup != null)
        {
            canvasGroup.alpha = 1f;
            canvasGroup.interactable = true;
            canvasGroup.blocksRaycasts = true;
        }
        if (titleText != null) titleText.text = "BARRACKS";
        ShowTab(0);
        Refresh();
    }

    public void Close()
    {
        if (rootObject != null) rootObject.SetActive(false);
        if (canvasGroup != null)
        {
            canvasGroup.alpha = 0f;
            canvasGroup.interactable = false;
            canvasGroup.blocksRaycasts = false;
        }
    }

    private void ShowTab(int idx)
    {
        if (hireContainer != null) hireContainer.SetActive(idx == 0);
        if (upgradeUnitsContainer != null) upgradeUnitsContainer.SetActive(idx == 1);
        if (upgradeBarracksContainer != null) upgradeBarracksContainer.SetActive(idx == 2);

        if (tabHireUnderline != null) tabHireUnderline.SetActive(idx == 0);
        if (tabUpgradeUnitsUnderline != null) tabUpgradeUnitsUnderline.SetActive(idx == 1);
        if (tabUpgradeBarracksUnderline != null) tabUpgradeBarracksUnderline.SetActive(idx == 2);

        if (tabHireActiveBg != null) tabHireActiveBg.SetActive(idx == 0);
        if (tabUpgradeUnitsActiveBg != null) tabUpgradeUnitsActiveBg.SetActive(idx == 1);
        if (tabUpgradeBarracksActiveBg != null) tabUpgradeBarracksActiveBg.SetActive(idx == 2);

        if (tabHireLabel != null)             tabHireLabel.color             = idx == 0 ? tabActiveTextColor : tabInactiveTextColor;
        if (tabUpgradeUnitsLabel != null)     tabUpgradeUnitsLabel.color     = idx == 1 ? tabActiveTextColor : tabInactiveTextColor;
        if (tabUpgradeBarracksLabel != null)  tabUpgradeBarracksLabel.color  = idx == 2 ? tabActiveTextColor : tabInactiveTextColor;
    }

    // Poll diamond total so the panel refreshes when the player spends
    // gold via another window (or via hiring in this same panel — Hire()
    // triggers OnRosterChanged which also calls Refresh, but the cost
    // colours on OTHER rows need to update too).
    private int lastKnownDiamonds = -1;
    private void Update()
    {
        if (rootObject != null && !rootObject.activeSelf) return;
        if (ResourceManager.Instance == null) return;
        if (ResourceManager.Instance.diamonds != lastKnownDiamonds)
        {
            lastKnownDiamonds = ResourceManager.Instance.diamonds;
            Refresh();
        }
    }

    private void Refresh()
    {
        var r = roster != null ? roster : MercenaryRoster.Instance;
        if (r == null) return;

        if (diamondsText != null && ResourceManager.Instance != null)
            diamondsText.text = ResourceManager.Instance.diamonds.ToString();

        RefreshHeaderPips();
        RefreshHireTab(r);
        RefreshUpgradeTab(r);
        RefreshBarracksTab();
    }

    private void RefreshHeaderPips()
    {
        if (barracksLevelPips == null || barracksLevelPips.Length == 0) return;
        int lvl = hostBuilding != null ? hostBuilding.currentLevel : 0;
        for (int i = 0; i < barracksLevelPips.Length; i++)
        {
            var img = barracksLevelPips[i];
            if (img == null) continue;
            img.sprite = (i < lvl) ? pipHelmFilledSprite : pipHelmEmptySprite;
        }
    }

    // --------- Hire ---------

    private void RefreshHireTab(MercenaryRoster r)
    {
        if (hireRowParent == null || hireRowPrefab == null) return;

        int hostLevel = hostBuilding != null ? hostBuilding.currentLevel : 1;

        EnsureRowCount(spawnedHireRows, hireRowParent, hireRowPrefab, r.catalogue.Count);

        for (int i = 0; i < r.catalogue.Count; i++)
        {
            var data = r.catalogue[i];
            var go = spawnedHireRows[i];
            if (data == null || go == null) continue;

            var row = go.GetComponent<BarracksHireRow>();
            if (row == null) row = go.AddComponent<BarracksHireRow>();
            row.Bind(data, r, hostLevel);
        }
    }

    // --------- Upgrade Units ---------

    private void RefreshUpgradeTab(MercenaryRoster r)
    {
        if (upgradeRowParent == null || upgradeRowPrefab == null) return;

        EnsureRowCount(spawnedUpgradeRows, upgradeRowParent, upgradeRowPrefab, r.catalogue.Count);

        for (int i = 0; i < r.catalogue.Count; i++)
        {
            var data = r.catalogue[i];
            var go = spawnedUpgradeRows[i];
            if (data == null || go == null) continue;

            var row = go.GetComponent<BarracksUpgradeUnitRow>();
            if (row == null) row = go.AddComponent<BarracksUpgradeUnitRow>();
            row.Bind(data, r, this);
        }
    }

    // --------- Upgrade Barracks ---------

    private void RefreshBarracksTab()
    {
        if (hostBuilding == null) return;
        int level = hostBuilding.currentLevel;
        int maxLevel = hostBuilding.levels != null ? hostBuilding.levels.Length : 0;

        if (barracksCurrentLevelText != null) barracksCurrentLevelText.text = $"LEVEL {level} / {maxLevel}";
        if (barracksSummaryText != null)
        {
            barracksSummaryText.text = ResolveSummaryText(level);
        }

        bool atMax = level >= maxLevel;

        if (atMax)
        {
            if (barracksNextLevelText != null) barracksNextLevelText.text = "MAX LEVEL";
            if (barracksPerksText != null) barracksPerksText.text = "All barracks perks unlocked.";
            SetCostText(barracksCostWoodText, 0, true);
            SetCostText(barracksCostStoneText, 0, true);
            SetCostText(barracksCostFoodText, 0, true);
            if (barracksUpgradeButton != null) barracksUpgradeButton.interactable = false;
            if (barracksUpgradeButtonText != null) barracksUpgradeButtonText.text = "MAX";
            if (barracksBuildTimeText != null) barracksBuildTimeText.text = "—";
            return;
        }

        var next = hostBuilding.levels[level];
        if (barracksNextLevelText != null) barracksNextLevelText.text = $"LEVEL {level + 1}";
        if (barracksPerksText != null)
        {
            barracksPerksText.text = ResolvePerksText(level, next);
        }

        bool canAfford = ResourceManager.Instance != null &&
                         ResourceManager.Instance.CanAffordStash(next.costWood, next.costStone, next.costFood);

        SetCostText(barracksCostWoodText, next.costWood,
            ResourceManager.Instance != null && ResourceManager.Instance.stashWood >= next.costWood);
        SetCostText(barracksCostStoneText, next.costStone,
            ResourceManager.Instance != null && ResourceManager.Instance.stashStone >= next.costStone);
        SetCostText(barracksCostFoodText, next.costFood,
            ResourceManager.Instance != null && ResourceManager.Instance.stashFood >= next.costFood);

        if (barracksUpgradeButton != null) barracksUpgradeButton.interactable = canAfford;
        if (barracksUpgradeButtonText != null) barracksUpgradeButtonText.text = "UPGRADE";
        if (barracksBuildTimeText != null) barracksBuildTimeText.text = FormatBuildTime(next.buildTime);
    }

    private string ResolveSummaryText(int level)
    {
        if (summaryTextPerLevel != null && level >= 0 && level < summaryTextPerLevel.Length &&
            !string.IsNullOrEmpty(summaryTextPerLevel[level]))
        {
            return summaryTextPerLevel[level];
        }
        // Fallback: build a summary from the CampBuilding's current-level description.
        if (hostBuilding != null && hostBuilding.levels != null &&
            level > 0 && level - 1 < hostBuilding.levels.Length)
        {
            var lv = hostBuilding.levels[level - 1];
            if (lv != null && !string.IsNullOrEmpty(lv.productionDescription))
                return lv.productionDescription.ToUpper();
        }
        return string.Empty;
    }

    private string ResolvePerksText(int level, BuildingLevel next)
    {
        if (perksTextPerLevel != null && level >= 0 && level < perksTextPerLevel.Length &&
            !string.IsNullOrEmpty(perksTextPerLevel[level]))
        {
            return perksTextPerLevel[level];
        }
        // Fallback: use the CampBuilding-level's own productionDescription
        // formatted with the Figma design's brass diamond bullet.
        if (next != null && !string.IsNullOrEmpty(next.productionDescription))
            return "◆ " + next.productionDescription;
        return string.Empty;
    }

    private void SetCostText(TextMeshProUGUI target, int value, bool affordable)
    {
        if (target == null) return;
        target.text = value.ToString();
        target.color = affordable ? costAffordableColor : costUnaffordableColor;
    }

    private string FormatBuildTime(float sec)
    {
        int total = Mathf.RoundToInt(sec);
        int m = total / 60;
        int s = total % 60;
        return m > 0 ? $"{m}:{s:D2}" : $"0:{s:D2}";
    }

    private void OnUpgradeBarracks()
    {
        if (hostBuilding == null || ResourceManager.Instance == null) return;
        int level = hostBuilding.currentLevel;
        if (hostBuilding.levels == null || level >= hostBuilding.levels.Length) return;

        var next = hostBuilding.levels[level];
        if (!ResourceManager.Instance.CanAffordStash(next.costWood, next.costStone, next.costFood)) return;

        ResourceManager.Instance.SpendStashResources(next.costWood, next.costStone, next.costFood);
        hostBuilding.currentLevel = level + 1;
        PlayerPrefs.SetInt("SaveBld_" + hostBuilding.buildingID, hostBuilding.currentLevel);
        PlayerPrefs.Save();

        if (AudioManager.Instance != null)
            AudioManager.Instance.PlaySFX3D(AudioID.Camp_BuildDone, hostBuilding.transform.position);

        Refresh();
    }

    // --------- Utils ---------

    private void EnsureRowCount(List<GameObject> list, Transform parent, GameObject prefab, int wanted)
    {
        while (list.Count < wanted)
        {
            var go = Instantiate(prefab, parent);
            list.Add(go);
        }
        while (list.Count > wanted)
        {
            var last = list[list.Count - 1];
            list.RemoveAt(list.Count - 1);
            if (last != null) Destroy(last);
        }
    }

    // Row prefabs call these to fetch the shared pip sprites off the panel
    // instead of duplicating references on every row.
    public Sprite GetUnitPipFilledSprite() => unitPipFilledSprite;
    public Sprite GetUnitPipEmptySprite() => unitPipEmptySprite;
}

// Row displayed once per unit archetype in the HIRE tab. Prefab wires the
// widgets via SerializeField; Bind() fills the values and hooks the button.
public class BarracksHireRow : MonoBehaviour
{
    public Image iconImage;
    public TextMeshProUGUI nameText;
    // NEW: one-line flavour under the name, filled from MercenaryUnitData.flavourText.
    public TextMeshProUGUI descriptionText;
    public TextMeshProUGUI ownedText;
    public TextMeshProUGUI costText;
    public Button hireButton;

    private MercenaryUnitData boundData;
    private MercenaryRoster boundRoster;

    public void Bind(MercenaryUnitData data, MercenaryRoster roster, int barracksLevel)
    {
        boundData = data;
        boundRoster = roster;

        if (iconImage != null && data.icon != null) iconImage.sprite = data.icon;
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

// Row displayed once per unit archetype in the UPGRADE UNITS tab.
public class BarracksUpgradeUnitRow : MonoBehaviour
{
    public Image iconImage;
    public TextMeshProUGUI nameText;
    // NEW: flavour text like the hire row.
    public TextMeshProUGUI descriptionText;
    // NEW: 5 pip Images (diamond glyphs) get sprite-swapped based on the
    // unit's current level. Element 0 = leftmost pip.
    public Image[] levelPips;
    // NEW: 4 stat preview slots — "ATK 25 → 33" is split into
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

    public void Bind(MercenaryUnitData data, MercenaryRoster roster, BarracksUpgradePanel panel)
    {
        boundData = data;
        boundRoster = roster;
        boundPanel = panel;

        int lvl = roster.GetUpgradeLevel(data.unitID);
        int maxLvl = data.MaxLevel;

        if (iconImage != null && data.icon != null) iconImage.sprite = data.icon;
        if (nameText != null) nameText.text = data.displayName;
        if (descriptionText != null) descriptionText.text = data.flavourText;

        // Pip sprites come from the panel so all rows stay in sync.
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

        if (costText != null) costText.text = isMax ? "MAX" : cost.ToString();
        if (upgradeButtonText != null) upgradeButtonText.text = isMax ? "MAX" : "UPGRADE";

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
