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
//   * grants a lump on camp entry for the time the player spent AWAY FROM CAMP
//     IN THIS SESSION — a mission, the map, the barracks — capped, so leaving
//     the camp is not a way to stop earning.
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
    [Tooltip("Most hours of away-from-camp time a single camp entry can be paid for. A cap, not a target.")]
    public float maxAwayHours = 2f;

    // ==== TIME THE GAME WAS CLOSED EARNS NOTHING ====
    //
    // This used to stamp DateTime.UtcNow into PlayerPrefs and pay for the
    // wall-clock gap on the next camp entry, so shutting the game down was a
    // way to earn: quit for a day, come back to a windfall. The regions are
    // supposed to pay you for holding them while you play, not for not playing.
    //
    // So the clock is Time.realtimeSinceStartup, which starts at zero when the
    // application does and keeps running across scene loads. A static, because
    // this component only exists in the camp scene and the time worth paying
    // for is precisely the time it did not exist — the mission, the map, the
    // barracks. Nothing is written to disk, so a closed game has no clock at
    // all and nothing to catch up on.
    private static float s_paidUpToRealtime;

    // Fractional carry so sub-1/hr yields still add up over time.
    private float accWood, accStone, accFood, accDiamonds;

    private void Start()
    {
        GrantTimeAwayFromCamp();
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

    // Pays for the stretch of THIS SESSION spent outside the camp. The tick
    // below only runs while this component is alive, and it is alive only in
    // the camp scene — without this, a long mission would earn nothing at all
    // and the fastest way to get rich would be to stand still in the camp.
    private void GrantTimeAwayFromCamp()
    {
        float now = Time.realtimeSinceStartup;
        float away = now - s_paidUpToRealtime;
        s_paidUpToRealtime = now;

        AccumulatePerHour(out int wood, out int stone, out int food, out int diamonds);
        if (wood == 0 && stone == 0 && food == 0 && diamonds == 0) return;

        float hours = Mathf.Clamp(away / 3600f, 0f, Mathf.Max(0f, maxAwayHours));
        if (hours <= 0f) return;

        int addWood = Mathf.FloorToInt(wood * hours);
        int addStone = Mathf.FloorToInt(stone * hours);
        int addFood = Mathf.FloorToInt(food * hours);
        int addDiamonds = Mathf.FloorToInt(diamonds * hours);

        if (ResourceManager.Instance != null)
        {
            if (addWood > 0 || addStone > 0 || addFood > 0)
                ResourceManager.Instance.AddStashResources(addWood, addStone, addFood);
            if (addDiamonds > 0)
                ResourceManager.Instance.AddDiamonds(addDiamonds);
        }

        GameLog.Info($"[Economy] {hours:0.00}h away from camp this session: " +
                     $"+{addWood}W +{addStone}S +{addFood}F +{addDiamonds}D");
        Announce(addWood, addStone, addFood, addDiamonds);
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

        // Advance the session clock as we go, so the next camp entry does not
        // pay a second time for the stretch that was just paid live.
        s_paidUpToRealtime = Time.realtimeSinceStartup;

        Announce(flushWood, flushStone, flushFood, flushDiamonds);
    }

    // One line naming where it came from. Without it the numbers in the corner
    // simply change on their own, and a player who was not watching that corner
    // never learns that holding regions is what pays for anything.
    //
    // ==== IT WAS SAYING EVERYTHING TWICE, IN TWO DIFFERENT PLACES ====
    //
    // This used to build "+12 Wood  +8 Stone" itself and hand it to
    // ToastManager. Two things were wrong with that.
    //
    // The numbers were already on screen. AddStashResources and AddDiamonds
    // call GlobalHUD.ShowResourceGain on their way through, so the bottom-left
    // feed had printed "+12 Wood" and "+8 Stone" before this line ran. The
    // banner repeated them in a second place, in a second style.
    //
    // And that second style is a panel ToastManager's fallback renderer builds
    // at runtime when no ToastUIController is in the scene — which is why the
    // payout looked nothing like every other thing the game tells you about
    // resources.
    //
    // So the numbers are left to the feed that was already printing them, and
    // all this adds is the attribution, through ShowPickupPopup: the same
    // container, the same font, the same stack as a chest or a felled tree.
    // Called after the resources have been added, so it sits above them.
    private void Announce(int wood, int stone, int food, int diamonds)
    {
        if (!announcePayout) return;
        if (wood <= 0 && stone <= 0 && food <= 0 && diamonds <= 0) return;
        if (GlobalHUD.Instance == null) return;

        // Pale gold — reads as tribute next to the wood, stone and food lines
        // it will be sitting on top of, without competing with any of them.
        GlobalHUD.Instance.ShowPickupPopup(LocalizationManager.Tr("REGION_INCOME"),
                                           new Color(0.96f, 0.86f, 0.56f));
    }

}
