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
    // Casualty multiplier split into win / loss cases so each tactic can
    // have a distinct risk profile — not just "less losses is better".
    //
    //  Ambush  — hit-and-run: normal wins, catastrophic losses (surrounded)
    //  Assault — professional line: modest bonus both ways
    //  Siege   — walls + engines: minor losses regardless of outcome
    public static float TacticCasualtyMultiplier(CampaignTactic t, bool won)
    {
        switch (t)
        {
            case CampaignTactic.Ambush:
                return won ? 1.0f : 1.6f;    // fine if you win, brutal if you lose
            case CampaignTactic.Siege:
                return won ? 0.5f : 0.6f;    // methodical — few losses either way
            default: // Assault
                return won ? 0.9f : 0.8f;    // balanced, disciplined retreat
        }
    }

    // Legacy single-arg overload — kept so old callers still compile.
    // Assumes "won" for a preview-style query.
    public static float TacticCasualtyMultiplier(CampaignTactic t) =>
        TacticCasualtyMultiplier(t, true);

    public static float TacticTravelMultiplier(CampaignTactic t)
    {
        switch (t)
        {
            case CampaignTactic.Ambush: return 0.6f;  // scouts choose fastest paths, no baggage
            case CampaignTactic.Siege:  return 2.0f;  // siege trains crawl — engines and supplies
            default: return 1.0f;
        }
    }

    // Extra win-chance the tactic grants ON TOP of the raw army-vs-enemy
    // score ratio. Wider spread than before so tactic choice has real bite.
    public static float TacticWinChanceBonus(CampaignTactic t)
    {
        switch (t)
        {
            case CampaignTactic.Ambush: return 0.08f;
            case CampaignTactic.Siege:  return 0.12f;
            default: return 0f;
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

        // Casualty forecast — blend of win-case and loss-case share weighted
        // by the estimated win probability. Ambush shows huge on-loss
        // penalty, Siege stays low both ways — the range makes the trade-off
        // legible without a text tooltip.
        float ratio = armyScore > 0 ? (float)enemyStrength / armyScore : 999f;
        float baseShare = Mathf.Clamp01(ratio * 0.6f);
        // Approximate win chance same way Resolve does, for band centring.
        float winChance = armyScore > 0
            ? Mathf.Clamp01((1f - (float)enemyStrength / (armyScore * 1.2f)) + TacticWinChanceBonus(tactic))
            : 0f;
        float winShare  = Mathf.Clamp01(baseShare * TacticCasualtyMultiplier(tactic, true));
        float lossShare = Mathf.Clamp01((baseShare + 0.35f) * TacticCasualtyMultiplier(tactic, false));
        int total = army.Count;
        int lowEnd  = Mathf.Clamp(Mathf.FloorToInt(winShare * total - 0.5f), 0, total);
        int highEnd = Mathf.Clamp(Mathf.CeilToInt(lossShare * total + 0.5f), lowEnd, total);
        r.expectedCasualtyLow = lowEnd;
        r.expectedCasualtyHigh = highEnd;

        r.won = winChance > 0.5f;
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

        // Win check: score ratio + tactic bonus + small RNG bias to keep
        // close battles unpredictable. Ceiling tightened to 1.2× so cheap
        // Militia spam doesn't trivialise mid/late regions — you need a
        // stronger composition to guarantee wins.
        // At armyScore == enemyScore*1.2 → 100% win (Assault); at ratio
        // 1:1 → ~17% win. Ambush +3%, Siege +7% on top.
        float baseWinChance = Mathf.Clamp01(1f - (float)enemyStrength / (armyScore * 1.2f));
        baseWinChance = Mathf.Clamp01(baseWinChance + TacticWinChanceBonus(tactic));
        float roll = (float)rng.NextDouble();
        float bias = ((float)rng.NextDouble() - 0.5f) * 0.2f;
        r.won = roll < (baseWinChance + bias);

        // Casualty math — win/loss-aware tactic multiplier so Ambush truly
        // punishes a lost gamble and Siege stays cheap either way.
        float ratio = (float)enemyStrength / armyScore;
        float baseShare = Mathf.Clamp01(ratio * 0.6f);
        if (!r.won) baseShare = Mathf.Clamp(baseShare + 0.35f, 0f, 1f); // losing hurts more
        float tacticMult = TacticCasualtyMultiplier(tactic, r.won);
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
