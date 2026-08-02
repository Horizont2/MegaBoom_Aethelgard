using System;
using System.Collections.Generic;
using UnityEngine;

// Runtime record of a hired mercenary. A hired unit belongs to a single
// campaign at a time (or none). Permadeath: once alive=false the entry is
// pruned from the roster on the next SaveRoster() cycle.
[Serializable]
public class MercenaryUnitInstance
{
    public int uid;
    public string unitID;
    public bool alive = true;
    // When set (>= 0), this unit is currently marching with campaign #id.
    public int activeCampaignID = -1;
}

// Singleton owner of the player's mercenary company.
// - Persists across scene loads (DontDestroyOnLoad).
// - Persists across sessions (PlayerPrefs JSON blobs).
// - Emits events so UI panels can refresh without polling.
public class MercenaryRoster : MonoBehaviour
{
    public static MercenaryRoster Instance { get; private set; }

    public static event Action OnRosterChanged;

    private const string PP_ROSTER = "MercRoster_v1";
    private const string PP_UPGRADES = "MercUpgrades_v1";
    private const string PP_NEXT_UID = "MercNextUID_v1";

    [Header("Unit Catalogue")]
    // Wire the 3 MercenaryUnitData assets here in the prefab that carries
    // this singleton (see BootstrapMercenaryRoster below).
    public List<MercenaryUnitData> catalogue = new List<MercenaryUnitData>();

    [Header("Company Capacity")]
    // Hard cap on total ALIVE units in the company — matches the 5 helm
    // pips shown on the barracks / pre-battle panels. Hire() refuses past
    // this, and the hire buttons grey out at the cap.
    public int maxArmySize = 5;

    public bool IsAtCapacity => CountAliveTotal() >= maxArmySize;

    // Runtime state — do NOT edit in inspector at runtime, use the API.
    [Header("Runtime State (read-only)")]
    [SerializeField] private List<MercenaryUnitInstance> roster = new List<MercenaryUnitInstance>();
    // Per-archetype upgrade level. Missing key → level 1.
    [SerializeField] private List<StringInt> upgradeLevels = new List<StringInt>();

    [Serializable]
    public struct StringInt { public string key; public int value; }

    private int nextUID = 1;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        DontDestroyOnLoad(gameObject);
        LoadRoster();
    }

    // --------- Public API ---------

    public IReadOnlyList<MercenaryUnitInstance> GetAllUnits() => roster;

    public MercenaryUnitData GetData(string unitID)
    {
        for (int i = 0; i < catalogue.Count; i++)
        {
            if (catalogue[i] != null && catalogue[i].unitID == unitID) return catalogue[i];
        }
        return null;
    }

    public int GetUpgradeLevel(string unitID)
    {
        for (int i = 0; i < upgradeLevels.Count; i++)
        {
            if (upgradeLevels[i].key == unitID) return Mathf.Max(1, upgradeLevels[i].value);
        }
        return 1;
    }

    public void SetUpgradeLevel(string unitID, int level)
    {
        for (int i = 0; i < upgradeLevels.Count; i++)
        {
            if (upgradeLevels[i].key == unitID)
            {
                var e = upgradeLevels[i]; e.value = level; upgradeLevels[i] = e;
                SaveRoster();
                OnRosterChanged?.Invoke();
                return;
            }
        }
        upgradeLevels.Add(new StringInt { key = unitID, value = level });
        SaveRoster();
        OnRosterChanged?.Invoke();
    }

    // Live unit counters used by the barracks UI and campaign resolver.
    public int CountAlive(string unitID)
    {
        int n = 0;
        for (int i = 0; i < roster.Count; i++)
        {
            if (roster[i].alive && roster[i].unitID == unitID) n++;
        }
        return n;
    }

    public int CountAliveTotal()
    {
        int n = 0;
        for (int i = 0; i < roster.Count; i++) if (roster[i].alive) n++;
        return n;
    }

    // Idle = alive AND not currently in a campaign. UI uses this for the
    // "available to hire into an army" pool.
    public int CountIdle(string unitID)
    {
        int n = 0;
        for (int i = 0; i < roster.Count; i++)
        {
            var u = roster[i];
            if (u.alive && u.activeCampaignID < 0 && u.unitID == unitID) n++;
        }
        return n;
    }

    public List<MercenaryUnitInstance> GetIdleOfType(string unitID)
    {
        var list = new List<MercenaryUnitInstance>();
        for (int i = 0; i < roster.Count; i++)
        {
            var u = roster[i];
            if (u.alive && u.activeCampaignID < 0 && u.unitID == unitID) list.Add(u);
        }
        return list;
    }

    // Hire a new unit of the given archetype. Diamond cost handled by caller
    // — this method just adds the unit to the roster.
    public MercenaryUnitInstance Hire(string unitID)
    {
        var data = GetData(unitID);
        if (data == null)
        {
            Debug.LogWarning($"[MercenaryRoster] Hire failed: unknown unitID '{unitID}'");
            return null;
        }
        // Company cap — refuse hires past maxArmySize. Callers should
        // grey out their buttons via IsAtCapacity, but this is the
        // authoritative gate (protects against double-click races and
        // debug hires too).
        if (IsAtCapacity)
        {
            ToastManager.Show(LocalizationManager.Tr("MERC_TOAST_ARMY_FULL", maxArmySize), ToastManager.ToastKind.Warning);
            return null;
        }
        var inst = new MercenaryUnitInstance
        {
            uid = nextUID++,
            unitID = unitID,
            alive = true,
            activeCampaignID = -1,
        };
        roster.Add(inst);
        // Guide step 'hire your first mercenary' keys off this flag.
        if (PlayerPrefs.GetInt("MercFirstHired", 0) == 0)
        {
            PlayerPrefs.SetInt("MercFirstHired", 1);
        }
        SaveRoster();
        OnRosterChanged?.Invoke();

        // Achievements.
        AchievementSystem.Unlock("FOR_HIRE");
        if (CountAliveTotal() >= 5) AchievementSystem.Unlock("VETERANS");
        return inst;
    }

    // Mark a batch of units as marching with this campaign. Used when a
    // campaign starts so they don't show up as "available" in other UIs.
    public void AssignToCampaign(IEnumerable<int> uids, int campaignID)
    {
        foreach (int uid in uids)
        {
            int idx = roster.FindIndex(u => u.uid == uid);
            if (idx >= 0)
            {
                var u = roster[idx];
                u.activeCampaignID = campaignID;
                roster[idx] = u;
            }
        }
        SaveRoster();
        OnRosterChanged?.Invoke();
    }

    // Kill a specific unit (permadeath). Called by BattleResolver after it
    // decides who fell in the auto-battle.
    public void KillUnit(int uid)
    {
        int idx = roster.FindIndex(u => u.uid == uid);
        if (idx >= 0)
        {
            var u = roster[idx];
            u.alive = false;
            u.activeCampaignID = -1;
            roster[idx] = u;
        }
    }

    // Release survivors back to idle pool after a campaign resolves.
    public void ReleaseFromCampaign(int campaignID)
    {
        for (int i = 0; i < roster.Count; i++)
        {
            if (roster[i].activeCampaignID == campaignID)
            {
                var u = roster[i]; u.activeCampaignID = -1; roster[i] = u;
            }
        }
        // Prune permadeath entries now that they're no longer tied to the campaign.
        roster.RemoveAll(u => !u.alive);
        SaveRoster();
        OnRosterChanged?.Invoke();
    }

    // --------- Save / Load ---------

    [Serializable]
    private class SaveBlob
    {
        public List<MercenaryUnitInstance> roster;
        public List<StringInt> upgradeLevels;
        public int nextUID;
    }

    private void SaveRoster()
    {
        var blob = new SaveBlob { roster = roster, upgradeLevels = upgradeLevels, nextUID = nextUID };
        PlayerPrefs.SetString(PP_ROSTER, JsonUtility.ToJson(blob));
        PlayerPrefs.SetInt(PP_NEXT_UID, nextUID);
        PlayerPrefs.Save();
    }

    private void LoadRoster()
    {
        string json = PlayerPrefs.GetString(PP_ROSTER, "");
        if (string.IsNullOrEmpty(json)) return;
        try
        {
            var blob = JsonUtility.FromJson<SaveBlob>(json);
            if (blob != null)
            {
                if (blob.roster != null) roster = blob.roster;
                if (blob.upgradeLevels != null) upgradeLevels = blob.upgradeLevels;
                nextUID = Mathf.Max(1, blob.nextUID);
            }
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[MercenaryRoster] Load failed, starting fresh: {e.Message}");
        }
    }
}
