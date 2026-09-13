using UnityEngine;

public enum ItemCategory { Sword, Axe, Bow, Helmet, Armor, Gloves, Shield }

[CreateAssetMenu(fileName = "NewWeapon", menuName = "Game/Weapon Data")]
public class WeaponData : ScriptableObject
{
    [Header("Basic Info")]
    public int weaponID;
    public string weaponName;
    public ItemCategory category; // ����: ���� ������� � ���������

    public Sprite icon;

    [TextArea(3, 5)]
    public string description;
    public int price;

    [Header("Models")]
    public GameObject shopPrefab;
    public GameObject inGamePrefab;
    [Tooltip("Extra local rotation (Euler °) for the shop-preview weapon. Leave 0 for prefabs authored upright; use it to fix a prefab whose mesh sits crooked at identity (e.g. the default sword).")]
    public Vector3 shopRotationEuler = Vector3.zero;

    [Header("Power System")]
    public int basePower = 20;
    public int powerPerLevel = 15;

    [Header("Upgrade System")]
    public int maxUpgradeLevel = 5;
    public int baseUpgradeCost = 100;
    public float upgradeCostMultiplier = 1.5f;

    [Header("Base Stats")]
    public float damageBonus;
    public float attackSpeed;
    public float critChance;

    // ==== SHIELDS ====
    //
    // A shield is a held item bought and upgraded exactly like a weapon, so it
    // rides WeaponData rather than getting a parallel asset type with its own
    // index, its own shop path and its own save keys to keep in step.
    //
    // The four numbers below are deliberately not a power ladder. A tier-3
    // shield is not "better" than a tier-1 one, it is a different bargain: the
    // light round shield parries generously and costs little stamina but covers
    // a narrow arc, the barbarian slab covers everything and carries its own
    // reserve but is nearly impossible to parry with. That is what makes buying
    // one a choice about how you fight instead of a queue of upgrades.
    [Header("Shield (ItemCategory.Shield only)")]
    [Tooltip("Added to the base parry window. Positive is a forgiving shield, negative a punishing one.")]
    public float parryWindowBonus = 0f;
    [Tooltip("Multiplies the stamina an absorbed hit costs. Below 1 is light, above 1 is heavy.")]
    public float staminaMultiplier = 1f;
    [Tooltip("Added to the guard arc in degrees. A wide shield is harder to flank.")]
    public float guardAngleBonus = 0f;
    [Tooltip("Fraction of a blocked hit thrown back at the attacker. Spiked shields only.")]
    [Range(0f, 0.6f)] public float reflectFraction = 0f;
    [Tooltip("Extra stamina pool carried by the shield itself.")]
    public float bonusStamina = 0f;

    [Header("Shield Growth Per Level")]
    public float staminaMultiplierPerLevel = -0.04f;
    public float bonusStaminaPerLevel = 4f;
    public float reflectPerLevel = 0f;

    [Header("Stat Growth Per Level")]
    public float damagePerLevel = 10f;
    public float attackSpeedPerLevel = 0.02f;
    public float critChancePerLevel = 0.02f;

    public int GetUpgradeCost(int currentLevel)
    {
        return Mathf.RoundToInt(baseUpgradeCost * Mathf.Pow(upgradeCostMultiplier, currentLevel));
    }
}