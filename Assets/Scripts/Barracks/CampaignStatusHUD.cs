using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// Small always-on top-of-screen strip that lists every in-flight mercenary
// campaign with region name, phase icon, and remaining time. Lives on any
// canvas — attach to a persistent HUD or a scene-local HUD, both work.
//
// One row per campaign is instantiated from `rowPrefab` into `rowsParent`.
// The row prefab is expected to have at least these children by name so
// callers don't need to write per-field wiring code:
//   - "RegionName" (TextMeshProUGUI)
//   - "TimeRemaining" (TextMeshProUGUI)
//   - "PhaseIcon" (Image, optional)
//
// If your HUD prefab shape differs, just wire the components on each row
// manually via CampaignStatusRow.
public class CampaignStatusHUD : MonoBehaviour
{
    [Header("Row Spawner")]
    public RectTransform rowsParent;
    public GameObject rowPrefab;

    [Header("Phase Icons (optional)")]
    public Sprite iconMarching;
    public Sprite iconFighting;
    public Sprite iconReturning;

    [Header("Auto-hide")]
    // When true, the whole strip GameObject is hidden while no campaigns
    // are active. Prevents an empty box sitting on the HUD.
    public bool hideWhenEmpty = true;
    public GameObject stripRoot;

    private readonly Dictionary<int, CampaignStatusRow> rows = new Dictionary<int, CampaignStatusRow>();

    private void Start()
    {
        MercenaryCampaignManager.OnCampaignStarted += HandleStarted;
        MercenaryCampaignManager.OnCampaignReturned += HandleReturned;

        // Rebuild for any campaigns already in flight when the scene loaded.
        if (MercenaryCampaignManager.Instance != null)
        {
            foreach (var c in MercenaryCampaignManager.Instance.ActiveCampaigns) HandleStarted(c);
        }
        UpdateRootVisibility();
    }

    private void OnDestroy()
    {
        MercenaryCampaignManager.OnCampaignStarted -= HandleStarted;
        MercenaryCampaignManager.OnCampaignReturned -= HandleReturned;
    }

    private void Update()
    {
        if (MercenaryCampaignManager.Instance == null) return;
        foreach (var c in MercenaryCampaignManager.Instance.ActiveCampaigns)
        {
            if (!rows.TryGetValue(c.campaignID, out var row) || row == null) { HandleStarted(c); continue; }
            RefreshRow(row, c);
        }
    }

    private void HandleStarted(MercenaryCampaign c)
    {
        if (c == null) return;
        if (rowPrefab == null || rowsParent == null) return;
        if (rows.ContainsKey(c.campaignID)) return;

        var go = Instantiate(rowPrefab, rowsParent);
        var row = go.GetComponent<CampaignStatusRow>();
        if (row == null) row = go.AddComponent<CampaignStatusRow>();
        row.AutoWireIfNeeded();
        rows[c.campaignID] = row;
        RefreshRow(row, c);
        UpdateRootVisibility();
    }

    private void HandleReturned(MercenaryCampaign c)
    {
        if (c == null) return;
        if (rows.TryGetValue(c.campaignID, out var row))
        {
            if (row != null) Destroy(row.gameObject);
            rows.Remove(c.campaignID);
        }
        UpdateRootVisibility();
    }

    private void RefreshRow(CampaignStatusRow row, MercenaryCampaign c)
    {
        string regionName = "?";
        if (MercenaryCampaignManager.Instance != null)
        {
            foreach (var r in MercenaryCampaignManager.Instance.regionCatalogue)
            {
                if (r != null && r.regionID == c.regionID) { regionName = r.regionName; break; }
            }
        }

        var phase = c.CurrentPhase();
        float remain = Mathf.Max(0f, c.TotalPhaseDuration - c.SecondsSinceStart());
        int m = Mathf.FloorToInt(remain / 60f);
        int s = Mathf.FloorToInt(remain % 60f);
        string timeStr = m > 0 ? $"{m}:{s:D2}" : $"0:{s:D2}";

        string phaseLabel = phase == CampaignPhase.MarchingOut
            ? LocalizationManager.Tr("MERC_PHASE_MARCHING")
            : phase == CampaignPhase.Fighting
                ? LocalizationManager.Tr("MERC_PHASE_FIGHTING")
                : LocalizationManager.Tr("MERC_PHASE_RETURNING");

        if (row.regionNameText != null) row.regionNameText.text = LocalizationManager.Tr(regionName).ToUpper();
        if (row.timeText != null) row.timeText.text = $"{phaseLabel} · {timeStr}";
        if (row.phaseIcon != null)
        {
            Sprite s2 = phase == CampaignPhase.Fighting ? iconFighting
                      : phase == CampaignPhase.Returning ? iconReturning
                                                          : iconMarching;
            row.phaseIcon.sprite = s2;
            row.phaseIcon.enabled = s2 != null;
        }
    }

    private void UpdateRootVisibility()
    {
        if (!hideWhenEmpty) return;
        var root = stripRoot != null ? stripRoot : gameObject;
        bool hasAny = rows.Count > 0;
        if (root.activeSelf != hasAny) root.SetActive(hasAny);
    }
}
