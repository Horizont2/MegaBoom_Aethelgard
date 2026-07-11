using System;
using System.Collections.Generic;
using UnityEngine;

// Tactic choice from the pre-battle panel. Modifies casualty/win math and
// travel time. Deliberately not "attack stat" — the tactic is a strategy
// token, not a damage bonus.
public enum CampaignTactic
{
    Ambush = 0,   // -20% casualties on WIN, +10% loss chance if outnumbered heavily
    Assault = 1,  // neutral — reference battle
    Siege = 2,    // -30% casualties, but +50% travel time (siege engines lag)
}

// Rating shown to the player instead of a raw win %. Prevents save-scumming
// and reads as more diegetic ("Ризикова" > "78%").
public enum RiskBand
{
    Overwhelming, // <30% enemy vs army — walkover
    Favourable,   // 30-70%
    Even,         // 70-130%
    Risky,        // 130-200%
    Suicidal,     // >200%
}

// Immutable snapshot of a battle's math — created once by ResolvePreview
// during hire, and again with the real seeded RNG when the campaign resolves.
public struct BattleResult
{
    public bool won;
    public int totalArmyScore;
    public int enemyScore;
    public RiskBand risk;
    public List<int> lostUnitUIDs;    // permadeath list — MercenaryRoster.KillUnit each
    public int expectedCasualtyLow;   // for the pre-battle preview
    public int expectedCasualtyHigh;
    public int diamondReward;
}

// Pure functions. No MonoBehaviour, no Unity globals, no hidden state — same
// seed always produces the same result, so a paused-and-reloaded campaign
// resolves identically the second time.
public static class BattleResolver
{
    public static float TacticCasualtyMultiplier(CampaignTactic t)
    {
        switch (t)
        {
            case CampaignTactic.Ambush: return 0.8f;
            case CampaignTactic.Siege:  return 0.7f;
            default: return 1.0f;
        }
    }

    public static float TacticTravelMultiplier(CampaignTactic t)
    {
        switch (t)
        {
            case CampaignTactic.Ambush: return 0.85f; // scouts choose faster paths
            case CampaignTactic.Siege:  return 1.5f;  // siege trains are slow
            default: return 1.0f;
        }
    }

    public static RiskBand ClassifyRisk(int armyScore, int enemyScore)
    {
        if (armyScore <= 0) return RiskBand.Suicidal;
        float ratio = (float)enemyScore / armyScore;
        if (ratio < 0.3f) return RiskBand.Overwhelming;
        if (ratio < 0.7f) return RiskBand.Favourable;
        if (ratio < 1.3f) return RiskBand.Even;
        if (ratio < 2.0f) return RiskBand.Risky;
        return RiskBand.Suicidal;
    }

    // Preview for the pre-battle UI. No RNG here — designer-visible math only.
    public static BattleResult Preview(
        List<MercenaryUnitInstance> army,
        List<MercenaryUnitData> archetypes,
        int enemyStrength,
        CampaignTactic tactic,
        int rewardOnWin)
    {
        int armyScore = ComputeArmyScore(army, archetypes);
        var r = new BattleResult
        {
            totalArmyScore = armyScore,
            enemyScore = enemyStrength,
            risk = ClassifyRisk(armyScore, enemyStrength),
            lostUnitUIDs = new List<int>(),
            diamondReward = rewardOnWin,
        };

        // Casualty forecast is a band around the win/loss expectation.
        // The wider the mismatch, the wider the band tightens toward one side.
        float ratio = armyScore > 0 ? (float)enemyStrength / armyScore : 999f;
        float tacticMult = TacticCasualtyMultiplier(tactic);
        // Base casualty share is proportional to enemy ratio, capped at whole army.
        float baseShare = Mathf.Clamp01(ratio * 0.6f) * tacticMult;
        int total = army.Count;
        r.expectedCasualtyLow = Mathf.Clamp(Mathf.FloorToInt(baseShare * total - 0.5f), 0, total);
        r.expectedCasualtyHigh = Mathf.Clamp(Mathf.CeilToInt(baseShare * total + 0.5f), r.expectedCasualtyLow, total);

        r.won = ratio < 1.0f; // preview shows the expected outcome
        return r;
    }

    // Real resolve — seeded RNG so a mid-flight reload produces the same
    // result the second time.  Caller (MercenaryCampaignManager) hands us
    // the campaign start time binary as the seed source.
    public static BattleResult Resolve(
        List<MercenaryUnitInstance> army,
        List<MercenaryUnitData> archetypes,
        int enemyStrength,
        CampaignTactic tactic,
        int rewardOnWin,
        long seedSource)
    {
        var rng = new System.Random(unchecked((int)seedSource));
        int armyScore = ComputeArmyScore(army, archetypes);

        var r = new BattleResult
        {
            totalArmyScore = armyScore,
            enemyScore = enemyStrength,
            risk = ClassifyRisk(armyScore, enemyStrength),
            lostUnitUIDs = new List<int>(),
            diamondReward = 0,
        };

        if (armyScore <= 0)
        {
            // No survivors — entire army lost.
            for (int i = 0; i < army.Count; i++) r.lostUnitUIDs.Add(army[i].uid);
            r.won = false;
            return r;
        }

        // Win check: score ratio + small RNG bias to keep close battles unpredictable.
        float baseWinChance = Mathf.Clamp01(1f - (float)enemyStrength / (armyScore * 1.5f));
        // At armyScore == enemyScore * 1.5 → 100% win. At armyScore == enemyScore → ~33%.
        // RNG kicker: ±10% to prevent perfect determinism at each ratio bucket.
        float roll = (float)rng.NextDouble();
        float bias = ((float)rng.NextDouble() - 0.5f) * 0.2f;
        r.won = roll < (baseWinChance + bias);

        // Casualty math.
        float ratio = (float)enemyStrength / armyScore;
        float baseShare = Mathf.Clamp01(ratio * 0.6f);
        if (!r.won) baseShare = Mathf.Clamp(baseShare + 0.35f, 0f, 1f); // losing hurts more
        float tacticMult = TacticCasualtyMultiplier(tactic);
        float finalShare = Mathf.Clamp01(baseShare * tacticMult);

        int total = army.Count;
        int deaths = Mathf.RoundToInt(finalShare * total);

        // Pick which specific UIDs died — shuffle army by seeded RNG so it's
        // reproducible, then take the first `deaths` entries.
        var pool = new List<int>(total);
        for (int i = 0; i < total; i++) pool.Add(i);
        for (int i = 0; i < pool.Count; i++)
        {
            int j = rng.Next(i, pool.Count);
            (pool[i], pool[j]) = (pool[j], pool[i]);
        }
        for (int k = 0; k < deaths && k < pool.Count; k++)
        {
            r.lostUnitUIDs.Add(army[pool[k]].uid);
        }

        r.diamondReward = r.won ? rewardOnWin : 0;
        return r;
    }

    private static int ComputeArmyScore(List<MercenaryUnitInstance> army, List<MercenaryUnitData> archetypes)
    {
        int total = 0;
        if (army == null) return 0;
        for (int i = 0; i < army.Count; i++)
        {
            var u = army[i];
            var data = FindData(archetypes, u.unitID);
            if (data == null) continue;
            int level = 1;
            if (MercenaryRoster.Instance != null) level = MercenaryRoster.Instance.GetUpgradeLevel(u.unitID);
            total += data.ScoreAtLevel(level);
        }
        return total;
    }

    private static MercenaryUnitData FindData(List<MercenaryUnitData> catalogue, string unitID)
    {
        if (catalogue == null) return null;
        for (int i = 0; i < catalogue.Count; i++)
        {
            if (catalogue[i] != null && catalogue[i].unitID == unitID) return catalogue[i];
        }
        return null;
    }
}
