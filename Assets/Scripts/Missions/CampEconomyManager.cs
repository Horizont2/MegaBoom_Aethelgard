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
    [Tooltip("How often (seconds) accrued passive income is flushed to the stash while in camp.")]
    public float resourceTickInterval = 60f;
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
            yield return new WaitForSeconds(resourceTickInterval);
            TickPassiveIncome();
        }
    }

    private void TickPassiveIncome()
    {
        if (ResourceManager.Instance == null) return;

        AccumulatePerHour(out int wood, out int stone, out int food, out int diamonds);
        if (wood == 0 && stone == 0 && food == 0 && diamonds == 0) return;

        // Convert per-hour → per-tick and carry the fraction so small yields
        // (e.g. 5 wood/hr) accumulate instead of truncating to zero each tick.
        float tickFraction = resourceTickInterval / 3600f;
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
    }

    private void StampCollectTime()
    {
        PlayerPrefs.SetString(LAST_COLLECT_KEY, DateTime.UtcNow.Ticks.ToString());
    }
}
