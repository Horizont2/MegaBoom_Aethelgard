using System;
using System.Collections.Generic;
using UnityEngine;

// Owner of every in-flight mercenary campaign. Ticks their timers on Update,
// resolves battles when a campaign reaches the Fighting phase, and hands
// survivors back to the roster when Returning finishes. Persists everything
// via PlayerPrefs so a session restart mid-flight resumes cleanly.
public class MercenaryCampaignManager : MonoBehaviour
{
    public static MercenaryCampaignManager Instance { get; private set; }

    public static event Action<MercenaryCampaign> OnCampaignStarted;
    public static event Action<MercenaryCampaign> OnCampaignResolved;
    public static event Action<MercenaryCampaign> OnCampaignReturned;

    [Header("Region Catalogue")]
    // All 24 RegionData assets — assigned in the bootstrap prefab so the
    // manager can look up a region by ID after loading a saved campaign.
    public List<RegionData> regionCatalogue = new List<RegionData>();

    [Header("Travel Timing")]
    // Nearest region gets minTravelSeconds; farthest gets maxTravelSeconds.
    // I use regionID rank as a stand-in for actual distance so we don't have
    // to place XY markers in Awake — designer can plug real markers later.
    public float minTravelSeconds = 45f;
    public float maxTravelSeconds = 180f;

    private const string PP_CAMPAIGNS = "MercCampaigns_v1";
    private const string PP_NEXT_ID = "MercCampNextID_v1";

    [SerializeField] private List<MercenaryCampaign> active = new List<MercenaryCampaign>();
    private int nextCampaignID = 1;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        DontDestroyOnLoad(gameObject);
        LoadCampaigns();
    }

    private void Update()
    {
        // Iterate on a copy — Resolve/Complete mutate the active list.
        for (int i = active.Count - 1; i >= 0; i--)
        {
            var c = active[i];
            var phase = c.CurrentPhase();

            if (!c.resolved && phase >= CampaignPhase.Fighting)
            {
                ResolveCampaign(c);
                SaveCampaigns();
            }

            if (phase == CampaignPhase.Done)
            {
                CompleteCampaign(c);
                active.RemoveAt(i);
                SaveCampaigns();
            }
        }
    }

    // --------- Public API ---------

    public IReadOnlyList<MercenaryCampaign> ActiveCampaigns => active;

    public MercenaryCampaign FindByRegion(int regionID)
    {
        for (int i = 0; i < active.Count; i++)
        {
            if (active[i].regionID == regionID) return active[i];
        }
        return null;
    }

    // Compute travel time in seconds for a given region. Uses regionID as a
    // proxy for map distance — later this can be swapped for a real XY
    // distance without touching callers.
    public float ComputeTravelSeconds(RegionData region, CampaignTactic tactic)
    {
        if (region == null) return maxTravelSeconds;

        // Normalise ID to 0..1 across the catalogue span. Missing catalogue
        // → assume linear over IDs 1..24.
        float t = 0.5f;
        if (regionCatalogue != null && regionCatalogue.Count > 1)
        {
            int minID = int.MaxValue, maxID = int.MinValue;
            for (int i = 0; i < regionCatalogue.Count; i++)
            {
                if (regionCatalogue[i] == null) continue;
                minID = Mathf.Min(minID, regionCatalogue[i].regionID);
                maxID = Mathf.Max(maxID, regionCatalogue[i].regionID);
            }
            if (maxID > minID) t = Mathf.InverseLerp(minID, maxID, region.regionID);
            else t = 0f;
        }
        else
        {
            // Regions are 0-indexed in this project (Region_1 → regionID 0,
            // Region_24 → regionID 23). InverseLerp(1, 24, x) was mis-scaled.
            t = Mathf.InverseLerp(0, 23, region.regionID);
        }

        float base_ = Mathf.Lerp(minTravelSeconds, maxTravelSeconds, t);
        return base_ * BattleResolver.TacticTravelMultiplier(tactic);
    }

    // Start a fresh campaign. Caller (PreBattlePanel) must have already
    // charged diamonds if any and confirmed the army list.
    public MercenaryCampaign StartCampaign(RegionData region, List<int> armyUIDs, CampaignTactic tactic)
    {
        if (region == null) return null;

        float outbound = ComputeTravelSeconds(region, tactic);
        // Return trip is 80% of outbound — survivors are tired, but don't
        // want to double the wall-clock for a lost battle.
        float ret = outbound * 0.8f;

        var c = new MercenaryCampaign
        {
            campaignID = nextCampaignID++,
            regionID = region.regionID,
            armyUIDs = new List<int>(armyUIDs),
            tactic = tactic,
            startTimeBinary = DateTime.UtcNow.ToBinary(),
            outboundDuration = outbound,
            battleDuration = 6f,
            returnDuration = ret,
            cachedEnemyStrength = region.enemyStrength,
            cachedRewardOnWin = region.autoBattleDiamondReward,
        };
        active.Add(c);

        if (MercenaryRoster.Instance != null)
            MercenaryRoster.Instance.AssignToCampaign(armyUIDs, c.campaignID);

        SaveCampaigns();
        OnCampaignStarted?.Invoke(c);
        return c;
    }

    // --------- Resolution ---------

    private void ResolveCampaign(MercenaryCampaign c)
    {
        c.resolved = true;

        if (MercenaryRoster.Instance == null) return;
        var roster = MercenaryRoster.Instance;

        // Assemble the live instance list for the battle math.
        var armyInstances = new List<MercenaryUnitInstance>();
        foreach (int uid in c.armyUIDs)
        {
            var u = FindUnitByUID(roster, uid);
            if (u != null && u.alive) armyInstances.Add(u);
        }

        var catalogue = new List<MercenaryUnitData>(roster.catalogue);
        var result = BattleResolver.Resolve(
            armyInstances,
            catalogue,
            c.cachedEnemyStrength,
            c.tactic,
            c.cachedRewardOnWin,
            c.startTimeBinary
        );

        c.won = result.won;
        c.lostUnitUIDs = result.lostUnitUIDs;
        c.diamondsAwarded = result.diamondReward;

        foreach (int uid in result.lostUnitUIDs) roster.KillUnit(uid);
        OnCampaignResolved?.Invoke(c);
    }

    // Called once the return trip finishes: release survivors, hand rewards,
    // and clear the campaign. Marks the region as Conquered on a win so the
    // world map updates the same way locations do.
    private void CompleteCampaign(MercenaryCampaign c)
    {
        if (MercenaryRoster.Instance != null)
            MercenaryRoster.Instance.ReleaseFromCampaign(c.campaignID);

        if (c.won)
        {
            if (ResourceManager.Instance != null && c.diamondsAwarded > 0)
                ResourceManager.Instance.AddDiamonds(c.diamondsAwarded);

            var region = FindRegionByID(c.regionID);
            if (region != null)
            {
                region.currentState = RegionState.Conquered;
                region.isNewlyUnlocked = true;
                if (MapProgressionManager.Instance != null)
                    MapProgressionManager.Instance.RefreshMapState();
            }
        }

        OnCampaignReturned?.Invoke(c);
    }

    private RegionData FindRegionByID(int id)
    {
        for (int i = 0; i < regionCatalogue.Count; i++)
        {
            if (regionCatalogue[i] != null && regionCatalogue[i].regionID == id) return regionCatalogue[i];
        }
        return null;
    }

    private MercenaryUnitInstance FindUnitByUID(MercenaryRoster roster, int uid)
    {
        var all = roster.GetAllUnits();
        for (int i = 0; i < all.Count; i++) if (all[i].uid == uid) return all[i];
        return null;
    }

    // --------- Debug (Context Menu) ---------

    // Right-click this component in the inspector at Play time → these
    // options appear. Lets you validate the whole hire → march → resolve
    // → return loop end-to-end WITHOUT the UI prefabs being wired.

    [ContextMenu("Debug/Hire 3 Militia (free)")]
    private void Debug_HireFreeMilitia()
    {
        if (MercenaryRoster.Instance == null) { Debug.LogWarning("MercenaryRoster not present"); return; }
        for (int i = 0; i < 3; i++) MercenaryRoster.Instance.Hire("militia");
        Debug.Log($"[Merc DEBUG] Hired 3 militia. Alive total: {MercenaryRoster.Instance.CountAliveTotal()}");
    }

    [ContextMenu("Debug/Hire 2 Ranger (free)")]
    private void Debug_HireFreeRanger()
    {
        if (MercenaryRoster.Instance == null) return;
        for (int i = 0; i < 2; i++) MercenaryRoster.Instance.Hire("ranger");
        Debug.Log($"[Merc DEBUG] Hired 2 ranger. Alive total: {MercenaryRoster.Instance.CountAliveTotal()}");
    }

    [ContextMenu("Debug/Hire 1 Knight (free)")]
    private void Debug_HireFreeKnight()
    {
        if (MercenaryRoster.Instance == null) return;
        MercenaryRoster.Instance.Hire("knight");
        Debug.Log($"[Merc DEBUG] Hired 1 knight. Alive total: {MercenaryRoster.Instance.CountAliveTotal()}");
    }

    [ContextMenu("Debug/Send All Idle Units → First Auto-Battle Region")]
    private void Debug_SendAllToFirstAutoBattle()
    {
        if (MercenaryRoster.Instance == null) { Debug.LogWarning("No roster"); return; }

        // Find the first Available region that uses mercenary auto-battle.
        RegionData target = null;
        for (int i = 0; i < regionCatalogue.Count; i++)
        {
            var r = regionCatalogue[i];
            if (r == null) continue;
            if (r.currentState != RegionState.Available) continue;
            if (!r.UsesMercenaryAutoBattle) continue;
            target = r; break;
        }
        if (target == null) { Debug.LogWarning("[Merc DEBUG] No Available auto-battle region found"); return; }

        // Collect every idle unit as our army.
        var uids = new List<int>();
        foreach (var u in MercenaryRoster.Instance.GetAllUnits())
        {
            if (u.alive && u.activeCampaignID < 0) uids.Add(u.uid);
        }
        if (uids.Count == 0) { Debug.LogWarning("[Merc DEBUG] No idle units — hire some first"); return; }

        var c = StartCampaign(target, uids, CampaignTactic.Assault);
        Debug.Log($"[Merc DEBUG] Sent {uids.Count} units to '{target.regionName}'. Outbound {c.outboundDuration:F1}s, return {c.returnDuration:F1}s. Watch for OnCampaignResolved / OnCampaignReturned in the log.");
    }

    [ContextMenu("Debug/Fast-Forward Active Campaigns By 30s")]
    private void Debug_FastForward30s()
    {
        // Shifts every active campaign's startTimeBinary 30 s into the past
        // so we don't have to wait real-time in the editor to see the resolve.
        var offset = TimeSpan.FromSeconds(30);
        for (int i = 0; i < active.Count; i++)
        {
            var c = active[i];
            var current = DateTime.FromBinary(c.startTimeBinary);
            c.startTimeBinary = current.Subtract(offset).ToBinary();
        }
        SaveCampaigns();
        Debug.Log($"[Merc DEBUG] Fast-forwarded {active.Count} campaigns by 30 s.");
    }

    [ContextMenu("Debug/Wipe Mercenary Save")]
    private void Debug_WipeSave()
    {
        PlayerPrefs.DeleteKey(PP_CAMPAIGNS);
        PlayerPrefs.DeleteKey(PP_NEXT_ID);
        PlayerPrefs.DeleteKey("MercRoster_v1");
        PlayerPrefs.DeleteKey("MercUpgrades_v1");
        PlayerPrefs.DeleteKey("MercNextUID_v1");
        PlayerPrefs.Save();
        active.Clear();
        nextCampaignID = 1;
        Debug.Log("[Merc DEBUG] Wiped mercenary save. Restart Play mode to see roster reset.");
    }

    // --------- Persistence ---------

    [Serializable]
    private class SaveBlob
    {
        public List<MercenaryCampaign> active;
        public int nextID;
    }

    private void SaveCampaigns()
    {
        var blob = new SaveBlob { active = active, nextID = nextCampaignID };
        PlayerPrefs.SetString(PP_CAMPAIGNS, JsonUtility.ToJson(blob));
        PlayerPrefs.SetInt(PP_NEXT_ID, nextCampaignID);
        PlayerPrefs.Save();
    }

    private void LoadCampaigns()
    {
        string json = PlayerPrefs.GetString(PP_CAMPAIGNS, "");
        if (string.IsNullOrEmpty(json)) return;
        try
        {
            var blob = JsonUtility.FromJson<SaveBlob>(json);
            if (blob != null)
            {
                if (blob.active != null) active = blob.active;
                nextCampaignID = Mathf.Max(1, blob.nextID);
            }
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[MercenaryCampaignManager] Load failed, starting fresh: {e.Message}");
        }
    }
}
