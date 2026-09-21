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

    // Nothing is ever a certainty. A sliver of doubt is what keeps the
    // pre-battle screen a decision instead of a formality.
    public const float MaxWinChance = 0.95f;

    // The single source of truth for "will this army win".
    //
    // This used to be written out twice — once in Preview, once in Resolve —
    // as `1 - enemy / (army * 1.2)`, and that formula had two problems.
    //
    // It contradicted the risk band displayed right beside it. ClassifyRisk
    // calls an evenly matched fight "Even"; the formula gave an evenly matched
    // fight a 17% chance. A player told the odds were even lost five times out
    // of six, and no amount of retuning the region numbers could fix a
    // disagreement between the label and the maths behind it.
    //
    // And it had no reachable top. Because the enemy term only ever divides,
    // the curve approaches 1 without arriving: an army at TEN times the
    // garrison's strength still read 88%. Its own comment claimed 100% at
    // 1.2x, which the code never did. That is why a starter company could not
    // take anything at all, whatever it was pointed at.
    //
    // The replacement maps the ratio the player is shown onto the odds the
    // player gets: half the garrison's strength is hopeless, an even match is
    // a coin flip, half again as strong is as close to certain as this game
    // ever promises. Tactic bonuses ride on top.
    public static float WinChance(int armyScore, int enemyStrength, CampaignTactic tactic)
    {
        if (armyScore <= 0) return 0f;
        if (enemyStrength <= 0) return MaxWinChance;

        float ratio = armyScore / (float)enemyStrength;
        float chance = Mathf.InverseLerp(0.5f, 1.5f, ratio) + TacticWinChanceBonus(tactic);
        return Mathf.Clamp(chance, 0f, MaxWinChance);
    }

    // The band the player is shown, derived from the odds the player will
    // actually get rather than from a second, independent set of thresholds.
    // Those thresholds were the other half of the disagreement described above:
    // whatever the win curve does, a label computed separately from it will
    // drift out of step with it again the first time either is retuned.
    //
    // Read at the neutral tactic on purpose — the band describes the FIGHT, and
    // a tactic is then something the player brings to it.
    public static RiskBand ClassifyRisk(int armyScore, int enemyScore)
    {
        if (armyScore <= 0) return RiskBand.Suicidal;
        float chance = WinChance(armyScore, enemyScore, CampaignTactic.Assault);
        if (chance >= 0.90f) return RiskBand.Overwhelming;
        if (chance >= 0.70f) return RiskBand.Favourable;
        if (chance >= 0.40f) return RiskBand.Even;
        if (chance >= 0.15f) return RiskBand.Risky;
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
        // Exactly what Resolve will use — not an approximation of it. The two
        // being separately written was how the preview and the outcome came to
        // disagree in the first place.
        float winChance = WinChance(armyScore, enemyStrength, tactic);
        float winShare  = Mathf.Clamp01(baseShare * TacticCasualtyMultiplier(tactic, true));
        int total = army.Count;
        int lowEnd  = Mathf.Clamp(Mathf.FloorToInt(winShare * total - 0.5f), 0, total);
        // The worst case is no longer "a big share of them". A defeat takes the
        // company, so the panel has to say the company — a range that tops out
        // below the army size would be understating the stake the player is
        // being asked to accept.
        int highEnd = total;
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

        // ==== THE NUMBER ON THE SCREEN IS THE NUMBER ====
        //
        // The roll used to be `base + (rng - 0.5) * 0.2`, so a player shown 76%
        // was actually being rolled against somewhere between 66% and 86%. In
        // expectation that is the same odds, and the stated reason — that a
        // preview should not read as a promise — was fair while a defeat only
        // cost a few soldiers.
        //
        // It is not fair any more. A lost campaign now costs the entire army
        // (see below), which makes the displayed percentage the single most
        // important number in the barracks. A number that important has to be
        // the one the dice are actually rolled against.
        float baseWinChance = WinChance(armyScore, enemyStrength, tactic);
        float roll = (float)rng.NextDouble();
        r.won = roll < Mathf.Clamp01(baseWinChance);

        int total = army.Count;
        int deaths;

        if (!r.won)
        {
            // ==== A DEFEAT IS NOT A BAD AFTERNOON ====
            //
            // Casualties were a share of the army scaled by the strength ratio,
            // so a strong army that lost anyway shed a fraction of itself —
            // and with a low-casualty tactic, or a small company, the rounding
            // could take NOBODY. The player watched their whole army march
            // home intact and then found the region still in enemy hands, with
            // nothing on screen explaining how both of those could be true.
            //
            // They did not take the region because they were beaten. Being
            // beaten now means the army does not come back. It makes the
            // outcome legible at a glance, it makes the odds worth reading,
            // and it makes sending a company somewhere a decision rather than
            // a free roll.
            deaths = total;
        }
        else
        {
            // Casualty math on a WIN — win/loss-aware tactic multiplier so
            // Siege stays cheap and Ambush pays for its edge.
            float ratio = (float)enemyStrength / armyScore;
            float baseShare = Mathf.Clamp01(ratio * 0.6f);
            float tacticMult = TacticCasualtyMultiplier(tactic, true);
            float finalShare = Mathf.Clamp01(baseShare * tacticMult);
            deaths = Mathf.RoundToInt(finalShare * total);
            // A victory always leaves somebody to carry the news home;
            // otherwise a "win" is indistinguishable from a defeat.
            deaths = Mathf.Min(deaths, Mathf.Max(0, total - 1));
        }

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
