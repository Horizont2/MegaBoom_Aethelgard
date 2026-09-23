using UnityEngine;
using System;
using System.Collections;
using System.Collections.Generic;

// Pays out the PASSIVE INCOME from conquered regions. Previously this class
// only LOGGED the totals — the actual AddResource call was commented out, so
// conquering and upgrading regions gave nothing. Now it:
//   * accrues each region's per-HOUR yield in real time (float accumulators
//     flushed to whole resources every tick — so a 5/hr region still pays out
//     instead of rounding to 0), and
//   * grants an offline lump on camp entry for the real time elapsed since the
//     last collection (missions, quitting the game), capped so a week away
//     doesn't dump a windfall.
// Values live on RegionData.upgradeLevels[level-1] (passiveWood/Stone/Food/
// Diamonds), the same fields the region-upgrade UI spends resources to raise.
public class CampEconomyManager : MonoBehaviour
{
    [Header("Economy Settings")]
    // ==== IT ARRIVES, IT IS NOT COLLECTED ====
    //
    // The map used to pop clickable resource icons onto conquered regions, and
    // they paid out of the same per-hour numbers this class already accrues —
    // so the regions paid twice, and how much a player got depended on how
    // often they opened the map and clicked. The icons are gone; this is now
    // the only way region income reaches the stash.
    //
    // A payout every quarter of an hour or so, rather than the old silent
    // minute tick: often enough that a session sees several, rare enough that
    // each one is an event worth a line on screen saying where it came from.
    // The interval only decides how often it is HANDED OVER — the amount is
    // accrued per hour either way, so nobody loses income by it being slower.
    [Tooltip("Shortest gap between region payouts, in minutes.")]
    public float payoutMinutesMin = 15f;
    [Tooltip("Longest gap between region payouts, in minutes.")]
    public float payoutMinutesMax = 30f;
    [Tooltip("Announce each payout on screen. Off: the resources still arrive, just silently.")]
    public bool announcePayout = true;
    [Tooltip("Maximum hours of offline income granted on camp entry, so a long absence can't dump a huge windfall.")]
    public float maxOfflineHours = 8f;

    private const string LAST_COLLECT_KEY = "LastPassiveCollectTicks";

    // Fractional carry so sub-1/hr yields still add up over time.
    private float accWood, accStone, accFood, accDiamonds;

    private void Start()
    {
        GrantOfflineIncome();
        StartCoroutine(EconomyTickRoutine());
    }

    // Sum the per-hour yield across every conquered region at its current level.
    private void AccumulatePerHour(out int wood, out int stone, out int food, out int diamonds)
    {
        wood = stone = food = diamonds = 0;
        if (MapProgressionManager.Instance == null || MapProgressionManager.Instance.allRegionsInGame == null) return;

        foreach (RegionData region in MapProgressionManager.Instance.allRegionsInGame)
        {
            if (region == null || region.currentState != RegionState.Conquered) continue;
            if (region.upgradeLevels == null || region.upgradeLevels.Length == 0) continue;

            int level = PlayerPrefs.GetInt("RegionLevel_" + region.regionID, 1);
            level = Mathf.Clamp(level, 1, region.upgradeLevels.Length);
            RegionLevelData data = region.upgradeLevels[level - 1];
            if (data == null) continue;

            wood += data.passiveWood;
            stone += data.passiveStone;
            food += data.passiveFood;
            diamonds += data.passiveDiamonds;
        }
    }

    private void GrantOfflineIncome()
    {
        AccumulatePerHour(out int wood, out int stone, out int food, out int diamonds);
        if (wood == 0 && stone == 0 && food == 0 && diamonds == 0)
        {
            StampCollectTime();
            return;
        }

        double hours = 0d;
        string saved = PlayerPrefs.GetString(LAST_COLLECT_KEY, "");
        if (!string.IsNullOrEmpty(saved) && long.TryParse(saved, out long lastTicks))
        {
            try
            {
                TimeSpan span = DateTime.UtcNow - new DateTime(lastTicks, DateTimeKind.Utc);
                hours = Mathf.Clamp((float)span.TotalHours, 0f, maxOfflineHours);
            }
            catch { hours = 0d; }
        }
        StampCollectTime();

        if (hours <= 0d) return;

        int addWood = Mathf.FloorToInt((float)(wood * hours));
        int addStone = Mathf.FloorToInt((float)(stone * hours));
        int addFood = Mathf.FloorToInt((float)(food * hours));
        int addDiamonds = Mathf.FloorToInt((float)(diamonds * hours));

        if (ResourceManager.Instance != null)
        {
            if (addWood > 0 || addStone > 0 || addFood > 0)
                ResourceManager.Instance.AddStashResources(addWood, addStone, addFood);
            if (addDiamonds > 0)
                ResourceManager.Instance.AddDiamonds(addDiamonds);
        }

        GameLog.Info($"[Economy] Offline income for {hours:0.0}h: +{addWood}W +{addStone}S +{addFood}F +{addDiamonds}D");
    }

    private IEnumerator EconomyTickRoutine()
    {
        while (true)
        {
            // Qualified: this file has both `using System;` and
            // `using UnityEngine;`, so a bare Random is ambiguous and will not
            // compile.
            float minutes = UnityEngine.Random.Range(Mathf.Min(payoutMinutesMin, payoutMinutesMax),
                                                     Mathf.Max(payoutMinutesMin, payoutMinutesMax));
            float seconds = Mathf.Max(5f, minutes * 60f);
            yield return new WaitForSeconds(seconds);
            TickPassiveIncome(seconds);
        }
    }

    private void TickPassiveIncome(float elapsedSeconds)
    {
        if (ResourceManager.Instance == null) return;

        AccumulatePerHour(out int wood, out int stone, out int food, out int diamonds);
        if (wood == 0 && stone == 0 && food == 0 && diamonds == 0) return;

        // Convert per-hour → per-tick and carry the fraction so small yields
        // (e.g. 5 wood/hr) accumulate instead of truncating to zero each tick.
        // The elapsed time is passed in rather than read from a field: the gap
        // between payouts varies now, and paying a fixed fraction for a varying
        // wait would quietly under- or over-pay every single time.
        float tickFraction = elapsedSeconds / 3600f;
        accWood += wood * tickFraction;
        accStone += stone * tickFraction;
        accFood += food * tickFraction;
        accDiamonds += diamonds * tickFraction;

        int flushWood = Mathf.FloorToInt(accWood);
        int flushStone = Mathf.FloorToInt(accStone);
        int flushFood = Mathf.FloorToInt(accFood);
        int flushDiamonds = Mathf.FloorToInt(accDiamonds);

        if (flushWood > 0 || flushStone > 0 || flushFood > 0)
        {
            ResourceManager.Instance.AddStashResources(flushWood, flushStone, flushFood);
            accWood -= flushWood; accStone -= flushStone; accFood -= flushFood;
        }
        if (flushDiamonds > 0)
        {
            ResourceManager.Instance.AddDiamonds(flushDiamonds);
            accDiamonds -= flushDiamonds;
        }

        // Advance the offline baseline as we go so leaving mid-accrual doesn't
        // double-count the time we just paid out live.
        StampCollectTime();

        Announce(flushWood, flushStone, flushFood, flushDiamonds);
    }

    // One line naming where it came from. Without it the numbers in the corner
    // simply change on their own, and a player who was not watching that corner
    // never learns that holding regions is what pays for anything.
    private void Announce(int wood, int stone, int food, int diamonds)
    {
        if (!announcePayout) return;
        if (wood <= 0 && stone <= 0 && food <= 0 && diamonds <= 0) return;

        var parts = new List<string>(4);
        if (wood > 0) parts.Add($"+{wood} {LocalizationManager.Tr("Wood")}");
        if (stone > 0) parts.Add($"+{stone} {LocalizationManager.Tr("Stone")}");
        if (food > 0) parts.Add($"+{food} {LocalizationManager.Tr("Food")}");
        if (diamonds > 0) parts.Add($"+{diamonds} {LocalizationManager.Tr("Diamond")}");

        ToastManager.Show($"{LocalizationManager.Tr("REGION_INCOME")}: {string.Join("  ", parts)}");
    }

    private void StampCollectTime()
    {
        PlayerPrefs.SetString(LAST_COLLECT_KEY, DateTime.UtcNow.Ticks.ToString());
    }
}
