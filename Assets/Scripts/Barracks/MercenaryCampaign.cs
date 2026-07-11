using System;
using System.Collections.Generic;
using UnityEngine;

// Serialisable record of one active or resolved mercenary campaign.
// Time is stored as a UTC binary tick so it survives session restarts —
// same trick as CampBuilding's upgrade timer.
[Serializable]
public class MercenaryCampaign
{
    public int campaignID;
    public int regionID;
    public List<int> armyUIDs = new List<int>(); // uids of hired units marching
    public CampaignTactic tactic;
    public long startTimeBinary;      // UTC.Binary at hire moment
    public float outboundDuration;    // seconds from start to arrival at region
    public float battleDuration = 6f; // artificial pause on arrival for the on-map SFX/flash
    public float returnDuration;      // seconds from resolution to survivors back at camp
    public int cachedEnemyStrength;   // snapshot so the auto-battle doesn't shift if RegionData is retuned mid-flight
    public int cachedRewardOnWin;
    // Filled at resolve — kept for BattleResultPanel to consume.
    public bool resolved;
    public bool won;
    public int diamondsAwarded;
    public List<int> lostUnitUIDs = new List<int>();

    // Phase helpers so the campaign manager doesn't have to know date math.
    public float SecondsSinceStart()
    {
        DateTime start = DateTime.FromBinary(startTimeBinary);
        return (float)(DateTime.UtcNow - start).TotalSeconds;
    }

    public float TotalPhaseDuration => outboundDuration + battleDuration + returnDuration;

    public CampaignPhase CurrentPhase()
    {
        float t = SecondsSinceStart();
        if (t < outboundDuration) return CampaignPhase.MarchingOut;
        if (t < outboundDuration + battleDuration) return CampaignPhase.Fighting;
        if (t < TotalPhaseDuration) return CampaignPhase.Returning;
        return CampaignPhase.Done;
    }

    // 0 → 1 progress along the marching-out phase, for the map figurine.
    public float OutboundProgress01()
    {
        float t = SecondsSinceStart();
        return Mathf.Clamp01(outboundDuration > 0 ? t / outboundDuration : 1f);
    }

    // 0 → 1 progress along the returning phase.
    public float ReturnProgress01()
    {
        float t = SecondsSinceStart() - outboundDuration - battleDuration;
        return Mathf.Clamp01(returnDuration > 0 ? t / returnDuration : 1f);
    }
}

public enum CampaignPhase
{
    MarchingOut,
    Fighting,
    Returning,
    Done,
}
