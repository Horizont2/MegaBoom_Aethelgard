using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// Pre-battle screen for a region without a totem. Opened by MapPanelUI when
// the player picks a mercenary-only region. Every widget is a SerializeField
// reference so the panel prefab can be redesigned freely.
public class PreBattlePanel : MonoBehaviour
{
    [Header("Root")]
    public GameObject rootObject;
    public CanvasGroup canvasGroup;
    public Button closeButton;

    [Header("Region Header")]
    public TextMeshProUGUI regionNameText;
    public TextMeshProUGUI enemyStrengthText;
    public TextMeshProUGUI travelTimeText;

    [Header("Unit Selection List")]
    public Transform unitRowParent;
    public GameObject unitRowPrefab; // needs PreBattleUnitRow component

    [Header("Tactic Selection")]
    public Button tacticAmbushButton;
    public Button tacticAssaultButton;
    public Button tacticSiegeButton;
    public Image tacticAmbushHighlight;
    public Image tacticAssaultHighlight;
    public Image tacticSiegeHighlight;

    [Header("Forecast")]
    public TextMeshProUGUI riskBandText;
    public TextMeshProUGUI expectedCasualtiesText;
    public TextMeshProUGUI armyScoreText;

    [Header("Confirm")]
    public Button confirmButton;
    public TextMeshProUGUI confirmButtonText;

    private RegionData currentRegion;
    private CampaignTactic currentTactic = CampaignTactic.Assault;
    private readonly Dictionary<string, int> desiredCounts = new Dictionary<string, int>();
    private readonly List<GameObject> spawnedUnitRows = new List<GameObject>();

    private void Awake()
    {
        if (closeButton != null) closeButton.onClick.AddListener(Close);
        if (confirmButton != null) confirmButton.onClick.AddListener(OnConfirm);

        if (tacticAmbushButton != null) tacticAmbushButton.onClick.AddListener(() => SetTactic(CampaignTactic.Ambush));
        if (tacticAssaultButton != null) tacticAssaultButton.onClick.AddListener(() => SetTactic(CampaignTactic.Assault));
        if (tacticSiegeButton != null) tacticSiegeButton.onClick.AddListener(() => SetTactic(CampaignTactic.Siege));

        if (rootObject != null) rootObject.SetActive(false);
    }

    public void Open(RegionData region)
    {
        currentRegion = region;
        desiredCounts.Clear();
        currentTactic = CampaignTactic.Assault;

        if (rootObject != null) rootObject.SetActive(true);
        if (canvasGroup != null)
        {
            canvasGroup.alpha = 1f;
            canvasGroup.interactable = true;
            canvasGroup.blocksRaycasts = true;
        }

        if (regionNameText != null) regionNameText.text = region.regionName.ToUpper();
        if (enemyStrengthText != null) enemyStrengthText.text = $"Enemy Strength: {region.enemyStrength}";

        SetTactic(CampaignTactic.Assault);
        RebuildUnitRows();
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

    private void SetTactic(CampaignTactic t)
    {
        currentTactic = t;
        if (tacticAmbushHighlight != null) tacticAmbushHighlight.enabled = t == CampaignTactic.Ambush;
        if (tacticAssaultHighlight != null) tacticAssaultHighlight.enabled = t == CampaignTactic.Assault;
        if (tacticSiegeHighlight != null) tacticSiegeHighlight.enabled = t == CampaignTactic.Siege;
        Refresh();
    }

    private void RebuildUnitRows()
    {
        var roster = MercenaryRoster.Instance;
        if (roster == null || unitRowParent == null || unitRowPrefab == null) return;

        // Clear old.
        for (int i = spawnedUnitRows.Count - 1; i >= 0; i--)
        {
            if (spawnedUnitRows[i] != null) Destroy(spawnedUnitRows[i]);
        }
        spawnedUnitRows.Clear();

        for (int i = 0; i < roster.catalogue.Count; i++)
        {
            var data = roster.catalogue[i];
            if (data == null) continue;
            var go = Instantiate(unitRowPrefab, unitRowParent);
            var row = go.GetComponent<PreBattleUnitRow>();
            if (row == null) row = go.AddComponent<PreBattleUnitRow>();
            row.Bind(data, roster, this);
            spawnedUnitRows.Add(go);
        }
    }

    // Called by a PreBattleUnitRow when its +/− buttons change.
    public void SetDesiredCount(string unitID, int count)
    {
        int idle = MercenaryRoster.Instance != null ? MercenaryRoster.Instance.CountIdle(unitID) : 0;
        desiredCounts[unitID] = Mathf.Clamp(count, 0, idle);
        Refresh();
    }

    public int GetDesiredCount(string unitID)
    {
        return desiredCounts.TryGetValue(unitID, out int v) ? v : 0;
    }

    private void Refresh()
    {
        var roster = MercenaryRoster.Instance;
        if (roster == null || currentRegion == null) return;

        // Compose the army selection into a runtime instance list for the preview.
        var chosen = new List<MercenaryUnitInstance>();
        var chosenUIDs = new List<int>();
        foreach (var kv in desiredCounts)
        {
            if (kv.Value <= 0) continue;
            var idleList = roster.GetIdleOfType(kv.Key);
            for (int i = 0; i < kv.Value && i < idleList.Count; i++)
            {
                chosen.Add(idleList[i]);
                chosenUIDs.Add(idleList[i].uid);
            }
        }

        // Travel time preview.
        if (travelTimeText != null && MercenaryCampaignManager.Instance != null)
        {
            float sec = MercenaryCampaignManager.Instance.ComputeTravelSeconds(currentRegion, currentTactic);
            travelTimeText.text = $"Travel: {FormatSeconds(sec)}";
        }

        // Battle forecast.
        var preview = BattleResolver.Preview(
            chosen,
            new List<MercenaryUnitData>(roster.catalogue),
            currentRegion.enemyStrength,
            currentTactic,
            currentRegion.autoBattleDiamondReward
        );

        if (armyScoreText != null) armyScoreText.text = $"Army Score: {preview.totalArmyScore}";
        if (riskBandText != null) riskBandText.text = $"Risk: {preview.risk}";
        if (expectedCasualtiesText != null)
            expectedCasualtiesText.text = chosen.Count > 0
                ? $"Expected Losses: {preview.expectedCasualtyLow}-{preview.expectedCasualtyHigh}"
                : "Expected Losses: —";

        if (confirmButtonText != null) confirmButtonText.text = "MARCH";
        if (confirmButton != null) confirmButton.interactable = chosen.Count > 0;

        // Update each row's live "idle available" number and its own affordability check.
        for (int i = 0; i < spawnedUnitRows.Count; i++)
        {
            var row = spawnedUnitRows[i].GetComponent<PreBattleUnitRow>();
            if (row != null) row.Refresh();
        }
    }

    private void OnConfirm()
    {
        var roster = MercenaryRoster.Instance;
        if (roster == null || currentRegion == null) return;

        var chosenUIDs = new List<int>();
        foreach (var kv in desiredCounts)
        {
            if (kv.Value <= 0) continue;
            var idleList = roster.GetIdleOfType(kv.Key);
            for (int i = 0; i < kv.Value && i < idleList.Count; i++)
                chosenUIDs.Add(idleList[i].uid);
        }

        if (chosenUIDs.Count == 0) return;
        if (MercenaryCampaignManager.Instance == null) return;

        MercenaryCampaignManager.Instance.StartCampaign(currentRegion, chosenUIDs, currentTactic);
        if (AudioManager.Instance != null) AudioManager.Instance.PlayUI(AudioID.UI_Click);
        Close();
    }

    private string FormatSeconds(float s)
    {
        int m = Mathf.FloorToInt(s / 60f);
        int sec = Mathf.FloorToInt(s % 60f);
        return m > 0 ? $"{m}m {sec:D2}s" : $"{sec}s";
    }
}

// Row displayed once per unit archetype in the pre-battle army selection.
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

    public void Bind(MercenaryUnitData data, MercenaryRoster roster, PreBattlePanel panel)
    {
        boundData = data;
        boundRoster = roster;
        boundPanel = panel;

        if (iconImage != null && data.icon != null) iconImage.sprite = data.icon;
        if (nameText != null) nameText.text = data.displayName;

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
        if (availableText != null) availableText.text = $"Available: {idle}";
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
