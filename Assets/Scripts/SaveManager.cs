using UnityEngine;

public static class SaveManager
{
    private const string CRYSTALS_KEY = "TotalCrystals";

    // Returns the current amount of saved crystals
    public static int GetTotalCrystals()
    {
        return PlayerPrefs.GetInt(CRYSTALS_KEY, 0);
    }

    // Adds crystals to the total and saves it
    public static void AddCrystals(int amount)
    {
        int current = GetTotalCrystals();
        PlayerPrefs.SetInt(CRYSTALS_KEY, current + amount);
        PlayerPrefs.Save();
    }

    // Tries to spend crystals. Returns true if successful
    public static bool SpendCrystals(int amount)
    {
        int current = GetTotalCrystals();
        if (current >= amount)
        {
            PlayerPrefs.SetInt(CRYSTALS_KEY, current - amount);
            PlayerPrefs.Save();
            return true;
        }
        return false;
    }

    // --- NEW: META UPGRADES SAVING ---

    // Gets the current level of a specific upgrade (e.g., "MetaHealth")
    public static int GetUpgradeLevel(string upgradeID)
    {
        return PlayerPrefs.GetInt("UpgradeLevel_" + upgradeID, 0); // Default level is 0
    }

    // Increases the level of a specific upgrade and saves it
    public static void SetUpgradeLevel(string upgradeID, int level)
    {
        PlayerPrefs.SetInt("UpgradeLevel_" + upgradeID, level);
        PlayerPrefs.Save();
    }
}