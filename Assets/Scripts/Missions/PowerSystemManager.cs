using UnityEngine;

public class PowerSystemManager : MonoBehaviour
{
    public static PowerSystemManager Instance;

    [Header("Databases")]
    public HeroData[] allHeroes;
    public WeaponData[] allWeapons;

    [Header("Power Weights")]
    [Tooltip("Внесок 1 HP у Power (не використовується якщо PlayerTotalPower вже містить gear-сумарну вагу)")]
    public float hpWeight = 0.5f;
    [Tooltip("Внесок 1 damage у Power")]
    public float damageWeight = 5f;
    [Tooltip("Скільки Power за рівень кузні")]
    public float forgeBonusPower = 20f;
    [Tooltip("Скільки Power за рівень MetaDamage")]
    public int metaDamageBonus = 15;
    [Tooltip("Скільки Power за рівень MetaHealth")]
    public int metaHealthBonus = 10;

    private void Awake()
    {
        if (Instance == null) Instance = this;
        else { Destroy(gameObject); return; }
    }

    private void Start()
    {
        // Make sure PlayerTotalPower in PlayerPrefs reflects current state so any
        // legacy reader (EnemyAI.Start, MapPanelUI) gets the full power score and
        // not just the gear sum the shop wrote.
        RefreshAndSave();
    }

    public int CalculatePlayerPower()
    {
        // Gear sum (weapon basePower + level + sum of equipped armor) is the
        // value ShopManager writes to PlayerTotalPower. We layer meta upgrades
        // and the forge on top so everyone reading "power" agrees.
        int gearPower = PlayerPrefs.GetInt("PlayerTotalPower_Gear", -1);
        if (gearPower < 0) gearPower = PlayerPrefs.GetInt("PlayerTotalPower", 50);

        int metaDmgLvl = PlayerPrefs.GetInt("UpgradeLevel_MetaDamage", 0);
        int metaHpLvl = PlayerPrefs.GetInt("UpgradeLevel_MetaHealth", 0);
        int forgeLevel = PlayerPrefs.GetInt("SaveBld_Forge", 0);

        int power = gearPower;
        power += metaDmgLvl * metaDamageBonus;
        power += metaHpLvl * metaHealthBonus;
        power += Mathf.RoundToInt(forgeLevel * forgeBonusPower);
        return power;
    }

    public void RefreshAndSave()
    {
        // Migrate one-time: snapshot the existing "gear" value into a dedicated
        // key so ShopManager can keep writing it without losing meta+forge bonus.
        if (!PlayerPrefs.HasKey("PlayerTotalPower_Gear"))
        {
            int existing = PlayerPrefs.GetInt("PlayerTotalPower", 50);
            PlayerPrefs.SetInt("PlayerTotalPower_Gear", existing);
        }

        int totalPower = CalculatePlayerPower();
        PlayerPrefs.SetInt("PlayerTotalPower", totalPower);
        PlayerPrefs.Save();
    }

    /// <summary>
    /// Difficulty multiplier mapped from playerPower vs recommendedPower.
    /// Under-levelled → harder (capped 2x). Over-levelled → easier
    /// (floored 0.7x). Never harder when over-recommended (the old code
    /// did Mathf.Abs and made the curve symmetric, which scaled difficulty
    /// up the more the player out-geared the region).
    /// </summary>
    public static float CalculateDifficultyMultiplier(int playerPower, int recommendedPower)
    {
        int delta = playerPower - recommendedPower;
        float power = (delta < 0)
            ? Mathf.Clamp(1f + (-delta) * 0.01f, 1f, 2f)
            : Mathf.Clamp(1f - delta * 0.003f, 0.7f, 1f);
        // Stack the player's chosen difficulty on top of the power-based
        // curve so the Settings_Difficulty dropdown actually does
        // something. Default = Normal (1×).
        return power * GetDifficultyScalar();
    }

    // Settings_Difficulty: 0 Easy, 1 Normal, 2 Hard, 3 Hardcore.
    public static float GetDifficultyScalar()
    {
        int idx = PlayerPrefs.GetInt("Settings_Difficulty", 1);
        return idx switch
        {
            0 => 0.75f,
            2 => 1.35f,
            3 => 1.7f,
            _ => 1f,
        };
    }

    /// <summary>
    /// Time-into-mission scaling, gentler than the old 5%/min curve:
    /// 2%/min capped at 1.5x so 20+ minute survival rounds don't 4x stats.
    /// </summary>
    public static float CalculateTimeMultiplier(float secondsInScene)
    {
        return Mathf.Min(1f + (secondsInScene / 60f) * 0.02f, 1.5f);
    }
}
