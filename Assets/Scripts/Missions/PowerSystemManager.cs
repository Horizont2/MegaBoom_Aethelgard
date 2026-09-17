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

        // Same wrong key as PlayerController had: the Forge saves under
        // "SaveBld_Forge_01", so this always read 0 and the Forge contributed
        // nothing to Power either — which also meant it never moved the
        // recommended-power comparison on the world map.
        int forgeLevel = CampBuilding.ForgeLevel;

        // Power = equipped gear (weapon + armor) + forge bonus. The old
        // MetaDamage/MetaHealth terms were removed: those perks had no
        // purchase path, so they were always 0 and only muddied the formula.
        int power = gearPower;
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
        // The underpowered penalty is capped at 1.55, not 2.
        //
        // At 2x the enemies have double health AND double damage, which is not
        // "hard", it is a wall: the player dies in two or three hits while their
        // own attacks barely register, so there is nothing to learn from the
        // attempt. A fresh save has power 50, and every region asked for more
        // than that, so this ceiling was where new players actually lived rather
        // than being the rare punishment for wandering somewhere too early.
        // ==== THE REWARD FOR OUT-GEARING A REGION WAS FOUR PER CENT ====
        //
        // The two halves of this curve were not the same shape. Falling short
        // cost 0.7% per point; getting ahead paid 0.3%. A player who had gone
        // and earned fourteen points of power over the recommendation - which
        // is what the region screen tells them to do - bought themselves a 4%
        // discount, which is nothing, and the region still played as though
        // they had come in exactly at the line.
        //
        // Getting ahead now pays 0.6% a point: the same fourteen points read as
        // 8%, a hundred points as the full floor. Coming in under still costs
        // more than getting ahead pays, which is the part that was right - the
        // recommendation has to mean something in the direction that matters.
        float power = (delta < 0)
            ? Mathf.Clamp(1f + (-delta) * 0.007f, 1f, 1.55f)
            : Mathf.Clamp(1f - delta * 0.006f, 0.62f, 1f);
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
