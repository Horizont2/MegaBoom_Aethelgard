using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// Screen shown when the player interacts with the barracks (F key).
// Deliberately modular: every visual widget is a SerializeField reference,
// so the designer can rebuild the panel prefab from scratch and just re-wire
// the fields — no code changes needed.
//
// Layout expectation (mine is a placeholder — feel free to replace):
//   Root panel (CanvasGroup)
//     - Header: title text + close button
//     - Diamonds label
//     - Tab bar: Hire / Upgrade Units / Upgrade Barracks
//     - One content container per tab, toggled by tab clicks
//     - Hire container   : row per unit type (row prefab: hireRowPrefab)
//     - Upgrade container: row per unit type (row prefab: upgradeRowPrefab)
//     - Barracks container: current level, next-level cost, upgrade button
public class BarracksUpgradePanel : MonoBehaviour
{
    [Header("Root")]
    public GameObject rootObject;
    public CanvasGroup canvasGroup;
    public Button closeButton;

    [Header("Header")]
    public TextMeshProUGUI titleText;
    public TextMeshProUGUI diamondsText;

    [Header("Tabs")]
    public Button tabHireButton;
    public Button tabUpgradeUnitsButton;
    public Button tabUpgradeBarracksButton;
    public GameObject hireContainer;
    public GameObject upgradeUnitsContainer;
    public GameObject upgradeBarracksContainer;

    [Header("Hire Tab")]
    public Transform hireRowParent;
    public GameObject hireRowPrefab; // prefab must expose fields via BarracksHireRow
    public MercenaryRoster roster;   // fallback if singleton not ready

    [Header("Upgrade Units Tab")]
    public Transform upgradeRowParent;
    public GameObject upgradeRowPrefab; // prefab must expose fields via BarracksUpgradeUnitRow

    [Header("Upgrade Barracks Tab")]
    public TextMeshProUGUI barracksLevelText;
    public TextMeshProUGUI barracksNextCostText;
    public Button barracksUpgradeButton;
    public CampBuilding hostBuilding;  // the CampBuilding component driving the level

    // Runtime bookkeeping so we don't rebuild the row lists every frame.
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
    }

    private void Refresh()
    {
        var r = roster != null ? roster : MercenaryRoster.Instance;
        if (r == null) return;

        if (diamondsText != null && ResourceManager.Instance != null)
            diamondsText.text = ResourceManager.Instance.diamonds.ToString();

        RefreshHireTab(r);
        RefreshUpgradeTab(r);
        RefreshBarracksTab();
    }

    // --------- Hire ---------

    private void RefreshHireTab(MercenaryRoster r)
    {
        if (hireRowParent == null || hireRowPrefab == null) return;

        int hostLevel = hostBuilding != null ? hostBuilding.currentLevel : 1;

        // Ensure one row per catalogue entry.
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
            row.Bind(data, r);
        }
    }

    // --------- Upgrade Barracks ---------

    private void RefreshBarracksTab()
    {
        if (hostBuilding == null) return;
        int level = hostBuilding.currentLevel;
        if (barracksLevelText != null) barracksLevelText.text = $"Level {level}";

        if (hostBuilding.levels != null && level < hostBuilding.levels.Length)
        {
            var next = hostBuilding.levels[level];
            if (barracksNextCostText != null)
                barracksNextCostText.text = $"W:{next.costWood}  S:{next.costStone}  F:{next.costFood}";
            if (barracksUpgradeButton != null)
                barracksUpgradeButton.interactable =
                    ResourceManager.Instance != null &&
                    ResourceManager.Instance.CanAffordStash(next.costWood, next.costStone, next.costFood);
        }
        else
        {
            if (barracksNextCostText != null) barracksNextCostText.text = "MAX LEVEL";
            if (barracksUpgradeButton != null) barracksUpgradeButton.interactable = false;
        }
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
}

// Row component — placeholder for the "Hire" list. Prefab wires the widgets
// via SerializeField; when Bind() is called, we fill and re-wire the button.
public class BarracksHireRow : MonoBehaviour
{
    public Image iconImage;
    public TextMeshProUGUI nameText;
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
        if (ownedText != null) ownedText.text = $"Owned: {roster.CountAlive(data.unitID)}";

        int cost = data.baseHireCost;
        if (costText != null) costText.text = $"◆ {cost}";

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

// Row component — placeholder for the "Upgrade Units" list.
public class BarracksUpgradeUnitRow : MonoBehaviour
{
    public Image iconImage;
    public TextMeshProUGUI nameText;
    public TextMeshProUGUI levelText;
    public TextMeshProUGUI costText;
    public Button upgradeButton;

    private MercenaryUnitData boundData;
    private MercenaryRoster boundRoster;

    public void Bind(MercenaryUnitData data, MercenaryRoster roster)
    {
        boundData = data;
        boundRoster = roster;

        int lvl = roster.GetUpgradeLevel(data.unitID);
        if (iconImage != null && data.icon != null) iconImage.sprite = data.icon;
        if (nameText != null) nameText.text = data.displayName;
        if (levelText != null) levelText.text = $"Lv {lvl}/{data.MaxLevel}";

        bool isMax = lvl >= data.MaxLevel;
        int cost = 0;
        if (!isMax && data.upgradePricePerLevel != null && lvl - 1 < data.upgradePricePerLevel.Length)
            cost = data.upgradePricePerLevel[lvl - 1];

        if (costText != null) costText.text = isMax ? "MAX" : $"◆ {cost}";

        bool canAfford = !isMax && ResourceManager.Instance != null && ResourceManager.Instance.CanAffordDiamonds(cost);
        if (upgradeButton != null)
        {
            upgradeButton.interactable = canAfford;
            upgradeButton.onClick.RemoveAllListeners();
            upgradeButton.onClick.AddListener(() => OnUpgradeClick(cost));
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
